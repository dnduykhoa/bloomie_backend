using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models.Entities;

namespace Bloomie.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly Services.AutoReplyService _autoReplyService;
        private readonly Services.RateLimitService _rateLimitService;
        private readonly Services.SpamDetectionService _spamDetectionService;
        private readonly IHubContext<NotificationHub> _notificationHubContext;
        private static readonly Dictionary<string, HashSet<string>> _userConnections = new();
        private static readonly object _lock = new();

        public ChatHub(ApplicationDbContext context, Services.AutoReplyService autoReplyService, Services.RateLimitService rateLimitService, Services.SpamDetectionService spamDetectionService, IHubContext<NotificationHub> notificationHubContext)
        {
            _context = context;
            _autoReplyService = autoReplyService;
            _rateLimitService = rateLimitService;
            _spamDetectionService = spamDetectionService;
            _notificationHubContext = notificationHubContext;
        }
        
        public static bool IsUserOnline(string userId)
        {
            lock (_lock)
            {
                return _userConnections.ContainsKey(userId) && _userConnections[userId].Count > 0;
            }
        }

        /// <summary>
        /// Join vào group của một conversation để nhận tin nhắn real-time
        /// </summary>
        public async Task JoinConversation(int conversationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        }

        /// <summary>
        /// Rời khỏi group của conversation
        /// </summary>
        public async Task LeaveConversation(int conversationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        }

        /// <summary>
        /// Gửi tin nhắn trong conversation (được gọi từ client)
        /// </summary>
        public async Task SendMessage(int conversationId, string message, string? attachmentUrl = null)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            // Lấy conversation và kiểm tra quyền
            var conversation = await _context.SupportConversations
                .Include(c => c.Customer)
                .Include(c => c.Staff)
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation == null) return;

            // Kiểm tra user có quyền gửi tin nhắn trong conversation này không
            var isAdmin = Context.User?.IsInRole("Admin") == true;
            var isManager = Context.User?.IsInRole("Manager") == true;
            var isStaff = Context.User?.IsInRole("Staff") == true;
            var isAnyStaff = isAdmin || isManager || isStaff;
            
            if (!isAnyStaff && conversation.CustomerId != userId) return;
            
            // CHECK BLOCKED: Kiểm tra user có bị block khỏi chat không
            if (!isAnyStaff)
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null && user.IsBlockedFromChat)
                {
                    await Clients.Caller.SendAsync("UserBlocked", new
                    {
                        message = "🚫 Tài khoản của bạn đã bị khóa khỏi chat do vi phạm quy định.",
                        reason = user.BlockedFromChatReason ?? "Vi phạm chính sách sử dụng",
                        blockedAt = user.BlockedFromChatAt
                    });
                    return;
                }
            }
            
            // RATE LIMITING: Chỉ áp dụng cho customer (không áp dụng cho staff)
            if (!isAnyStaff)
            {
                if (!_rateLimitService.CanSendMessage(userId, out int remainingSeconds))
                {
                    await Clients.Caller.SendAsync("RateLimitExceeded", new
                    {
                        message = $"⚠️ Bạn đang gửi tin nhắn quá nhanh. Vui lòng đợi {remainingSeconds} giây.",
                        remainingSeconds = remainingSeconds
                    });
                    return;
                }
            }
            
            // SPAM DETECTION: Kiểm tra spam tự động (chỉ cho customer)
            if (!isAnyStaff)
            {
                var spamCheck = await _spamDetectionService.CheckMessageAsync(userId, message);
                if (spamCheck.IsSpam)
                {
                    // Thông báo cho user
                    await Clients.Caller.SendAsync("SpamDetected", new
                    {
                        message = $"⚠️ {spamCheck.Reason}",
                        violationType = spamCheck.ViolationType,
                        violationCount = spamCheck.ViolationCount,
                        maxViolations = 3
                    });
                    
                    // Nếu user bị auto-block
                    if (spamCheck.UserBlocked)
                    {
                        await Clients.Caller.SendAsync("UserBlocked", new
                        {
                            message = "🚫 Tài khoản của bạn đã bị khóa tự động do spam quá nhiều.",
                            reason = spamCheck.Reason,
                            blockedAt = DateTime.Now
                        });
                        
                        // Thông báo cho admin
                        await NotifyAdminsSpamBlock(userId, conversation, spamCheck.Reason);
                    }
                    
                    return; // Chặn tin nhắn spam
                }
            }
            
            // Chỉ Admin có thể gửi tin nhắn vào BẤT KỲ hội thoại nào
            // Manager và Staff phải nhấn "Nhận" trước
            if (isAnyStaff && !isAdmin && conversation.StaffId != userId)
            {
                await Clients.Caller.SendAsync("Error", "Bạn cần nhấn 'Nhận' để tư vấn khách hàng này");
                return;
            }

            // Tạo tin nhắn mới
            var supportMessage = new SupportMessage
            {
                ConversationId = conversationId,
                SenderId = userId,
                Message = message,
                AttachmentUrl = attachmentUrl,
                SentAt = DateTime.Now,
                IsRead = false,
                IsFromStaff = isAnyStaff
            };

            _context.SupportMessages.Add(supportMessage);

            // Update conversation
            conversation.LastMessage = message.Length > 100 ? message.Substring(0, 100) + "..." : message;
            conversation.LastMessageAt = DateTime.Now;

            if (isAnyStaff)
            {
                conversation.UnreadByCustomer++;
            }
            else
            {
                conversation.UnreadByStaff++;
            }

            await _context.SaveChangesAsync();

            // Lấy thông tin sender để gửi đi
            var sender = await _context.Users.FindAsync(userId);

            // Nếu là tin nhắn từ customer, notify admin/staff về tin nhắn mới
            if (!isAnyStaff)
            {
                await NotifyStaffNewMessage(conversationId, sender?.FullName ?? "Khách hàng");
            }

            // Gửi tin nhắn real-time đến tất cả thành viên trong group
            await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
            {
                id = supportMessage.Id,
                conversationId = conversationId,
                senderId = userId,
                senderName = sender?.FullName ?? sender?.UserName ?? "Unknown",
                senderAvatar = sender?.ProfileImageUrl,
                message = message,
                attachmentUrl = attachmentUrl,
                sentAt = supportMessage.SentAt,
                isFromStaff = isAnyStaff,
                isRead = false,
                readAt = (DateTime?)null
            });

            // AUTO REPLY: Nếu là tin nhắn đầu tiên từ customer và ngoài giờ làm việc
            if (!isAnyStaff && string.IsNullOrEmpty(conversation.StaffId) && !_autoReplyService.IsWorkingHours())
            {
                await Task.Delay(1000); // Đợi 1 giây cho tự nhiên

                // Lấy admin đầu tiên để làm sender cho auto-reply
                var systemUser = await _context.Users
                    .Where(u => _context.UserRoles
                        .Join(_context.Roles,
                            ur => ur.RoleId,
                            r => r.Id,
                            (ur, r) => new { ur.UserId, r.Name })
                        .Any(x => x.UserId == u.Id && x.Name == "Admin"))
                    .FirstOrDefaultAsync();

                // Nếu không tìm thấy admin, dùng userId hiện tại (customer) - fallback
                var autoReplySenderId = systemUser?.Id ?? userId;

                var autoReplyMessage = new SupportMessage
                {
                    ConversationId = conversationId,
                    SenderId = autoReplySenderId,
                    Message = _autoReplyService.GetOutOfOfficeMessage(),
                    SentAt = DateTime.Now,
                    IsRead = false,
                    IsFromStaff = true // Hiển thị như staff message
                };

                _context.SupportMessages.Add(autoReplyMessage);
                conversation.LastMessage = "🤖 Tin nhắn tự động";
                conversation.LastMessageAt = DateTime.Now;
                await _context.SaveChangesAsync();

                // Gửi auto reply
                await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
                {
                    id = autoReplyMessage.Id,
                    conversationId = conversationId,
                    senderId = autoReplySenderId,
                    senderName = "Bloomie Auto Reply 🤖",
                    senderAvatar = "/images/logos/bloomie_logo.png",
                    message = autoReplyMessage.Message,
                    attachmentUrl = (string?)null,
                    sentAt = autoReplyMessage.SentAt,
                    isFromStaff = true,
                    isRead = false,
                    readAt = (DateTime?)null
                });
            }
        }

        /// <summary>
        /// Đánh dấu tin nhắn đã đọc
        /// </summary>
        public async Task MarkAsRead(int conversationId)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            var conversation = await _context.SupportConversations.FindAsync(conversationId);
            if (conversation == null) return;

            var isStaff = Context.User?.IsInRole("Admin") == true || 
                         Context.User?.IsInRole("Manager") == true || 
                         Context.User?.IsInRole("Staff") == true;

            // Kiểm tra quyền
            if (!isStaff && conversation.CustomerId != userId) return;

            // Lấy các tin nhắn chưa đọc
            var unreadMessages = await _context.SupportMessages
                .Where(m => m.ConversationId == conversationId && !m.IsRead)
                .ToListAsync();

            // Đánh dấu đã đọc các tin nhắn của người còn lại
            var readAt = DateTime.Now;
            foreach (var msg in unreadMessages)
            {
                if ((isStaff && !msg.IsFromStaff) || (!isStaff && msg.IsFromStaff))
                {
                    msg.IsRead = true;
                    msg.ReadAt = readAt;
                }
            }

            // Reset unread count
            if (isStaff)
            {
                conversation.UnreadByStaff = 0;
            }
            else
            {
                conversation.UnreadByCustomer = 0;
            }

            await _context.SaveChangesAsync();

            // Thông báo cho người còn lại rằng tin nhắn đã được đọc
            await Clients.Group($"conversation_{conversationId}").SendAsync("MessagesRead", new
            {
                conversationId = conversationId,
                readBy = isStaff ? "staff" : "customer",
                readAt = readAt
            });
        }

        /// <summary>
        /// Typing indicator - Thông báo đang gõ
        /// </summary>
        public async Task Typing(int conversationId)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            var sender = await _context.Users.FindAsync(userId);
            var isStaff = Context.User?.IsInRole("Admin") == true || 
                         Context.User?.IsInRole("Manager") == true || 
                         Context.User?.IsInRole("Staff") == true;

            await Clients.OthersInGroup($"conversation_{conversationId}").SendAsync("UserTyping", new
            {
                conversationId = conversationId,
                userId = userId,
                userName = sender?.FullName ?? sender?.UserName ?? "Unknown",
                isStaff = isStaff
            });
        }

        /// <summary>
        /// Stop typing indicator
        /// </summary>
        public async Task StopTyping(int conversationId)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            await Clients.OthersInGroup($"conversation_{conversationId}").SendAsync("UserStoppedTyping", new
            {
                conversationId = conversationId,
                userId = userId
            });
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
            if (!string.IsNullOrEmpty(userId))
            {
                // Join user vào group cá nhân để nhận notification
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                
                // Track connection
                bool isFirstConnection = false;
                lock (_lock)
                {
                    if (!_userConnections.ContainsKey(userId))
                    {
                        _userConnections[userId] = new HashSet<string>();
                        isFirstConnection = true;
                    }
                    _userConnections[userId].Add(Context.ConnectionId);
                }
                
                // Broadcast user online status nếu đây là connection đầu tiên
                if (isFirstConnection)
                {
                    await Clients.All.SendAsync("UserStatusChanged", userId, true);
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                
                // Remove connection
                bool isLastConnection = false;
                lock (_lock)
                {
                    if (_userConnections.ContainsKey(userId))
                    {
                        _userConnections[userId].Remove(Context.ConnectionId);
                        if (_userConnections[userId].Count == 0)
                        {
                            _userConnections.Remove(userId);
                            isLastConnection = true;
                            
                            // Chỉ cập nhật LastSeenAt khi user hoàn toàn offline (không còn connection nào)
                            Task.Run(async () =>
                            {
                                using var scope = Context.GetHttpContext()?.RequestServices.CreateScope();
                                if (scope != null)
                                {
                                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                    var user = await dbContext.Users.FindAsync(userId);
                                    if (user != null)
                                    {
                                        user.LastSeenAt = DateTime.Now;
                                        await dbContext.SaveChangesAsync();
                                    }
                                }
                            });
                        }
                    }
                }
                
                // Broadcast user offline status nếu đây là connection cuối cùng
                if (isLastConnection)
                {
                    await Clients.All.SendAsync("UserStatusChanged", userId, false, DateTime.Now);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Notify khi tin nhắn được thu hồi
        /// </summary>
        public async Task RecallMessage(int conversationId, int messageId)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return;

            // Broadcast đến tất cả members trong conversation
            await Clients.Group($"conversation_{conversationId}")
                .SendAsync("MessageRecalled", new
                {
                    conversationId = conversationId,
                    messageId = messageId,
                    recalledBy = userId,
                    recalledAt = DateTime.UtcNow
                });
        }

        /// <summary>
        /// Thông báo admin khi user bị auto-block do spam
        /// </summary>
        private async Task NotifyAdminsSpamBlock(string userId, SupportConversation conversation, string reason)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                var userName = user?.FullName ?? user?.UserName ?? "Unknown";

                // Lưu notification vào database
                var notification = new Bloomie.Models.Entities.Notification
                {
                    Message = $"🚨 User '{userName}' đã bị tự động chặn do spam: {reason}",
                    Link = $"/Admin/Chat?conversationId={conversation.Id}",
                    Type = "danger",
                    IsRead = false,
                    CreatedAt = DateTime.Now,
                    UserId = null // null = gửi cho tất cả admin/manager
                };
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                // Gửi notification đến tất cả admin/manager đang online
                var adminRoleIds = await _context.Roles
                    .Where(r => r.Name == "Admin" || r.Name == "Manager")
                    .Select(r => r.Id)
                    .ToListAsync();
                
                var adminUsers = await _context.UserRoles
                    .Where(ur => adminRoleIds.Contains(ur.RoleId))
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .ToListAsync();

                foreach (var adminId in adminUsers)
                {
                    // Gửi popup notification (realtime)
                    if (IsUserOnline(adminId))
                    {
                        await Clients.User(adminId).SendAsync("SpamBlockNotification", new
                        {
                            userId = userId,
                            userName = userName,
                            conversationId = conversation.Id,
                            reason = reason,
                            timestamp = DateTime.Now,
                            message = $"🚨 User '{userName}' đã bị tự động chặn do spam: {reason}"
                        });
                    }

                    // Gửi bell notification (database + số đếm)
                    var unreadCount = await _context.Notifications
                        .Where(n => (n.UserId == null || n.UserId == adminId) && !n.IsRead)
                        .CountAsync();

                    await Clients.User(adminId).SendAsync("ReceiveNotification", new
                    {
                        notificationId = notification.Id,
                        message = notification.Message,
                        link = notification.Link,
                        type = notification.Type,
                        createdAt = notification.CreatedAt,
                        unreadCount = unreadCount
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error notifying admins: {ex.Message}");
            }
        }

        // Helper: Notify staff về tin nhắn mới từ customer
        private async Task NotifyStaffNewMessage(int conversationId, string customerName)
        {
            try
            {
                // Query admin/manager/staff role IDs
                var staffRoleIds = await _context.Roles
                    .Where(r => r.Name == "Admin" || r.Name == "Manager" || r.Name == "Staff")
                    .Select(r => r.Id)
                    .ToListAsync();

                // Query user IDs with those roles
                var staffUserIds = await _context.UserRoles
                    .Where(ur => staffRoleIds.Contains(ur.RoleId))
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .ToListAsync();

                // Đếm lại số tin nhắn chưa đọc
                var unreadCount = await _context.SupportConversations
                    .Where(c => !c.IsClosed && c.Messages.Any(m => !m.IsRead && !m.IsFromStaff))
                    .CountAsync();

                // Gửi event "NewChatMessage" qua NotificationHub cho tất cả staff
                foreach (var staffId in staffUserIds)
                {
                    await _notificationHubContext.Clients.User(staffId).SendAsync("NewChatMessage", new
                    {
                        conversationId,
                        customerName,
                        unreadCount
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error notifying staff: {ex.Message}");
            }
        }
    }
}
