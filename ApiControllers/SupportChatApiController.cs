using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Hubs;

namespace Bloomie.ApiControllers
{
    [Route("api/supportchat")]
    [ApiController]
    [Authorize]
    [IgnoreAntiforgeryToken] // API không cần CSRF token
    public class SupportChatApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _chatHubContext;

        public SupportChatApiController(ApplicationDbContext context, IHubContext<ChatHub> chatHubContext)
        {
            _context = context;
            _chatHubContext = chatHubContext;
        }

        /// <summary>
        /// Test endpoint - check auth
        /// </summary>
        [HttpGet("test")]
        public IActionResult Test()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Ok(new { authenticated = User.Identity?.IsAuthenticated, userId = userId });
        }

        /// <summary>
        /// Lấy danh sách conversations của user hiện tại
        /// </summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(
            [FromQuery] string? tag = null,
            [FromQuery] int? priority = null,
            [FromQuery] string? staffId = null,
            [FromQuery] string? searchText = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            IQueryable<SupportConversation> query = _context.SupportConversations
                .Include(c => c.Customer)
                .Include(c => c.Staff);

            if (isStaff)
            {
                // Staff xem tất cả conversations (bao gồm cả đã đóng)
                // Không filter IsClosed nữa để admin vẫn thấy hội thoại đã đóng
            }
            else
            {
                // Customer chỉ xem conversations của mình
                query = query.Where(c => c.CustomerId == userId);
            }

            // FILTER: Lọc theo Tag
            if (!string.IsNullOrEmpty(tag))
            {
                query = query.Where(c => c.Tag == tag);
            }

            // FILTER: Lọc theo Priority
            if (priority.HasValue)
            {
                query = query.Where(c => c.Priority == priority.Value);
            }

            // FILTER: Lọc theo Staff phụ trách
            if (!string.IsNullOrEmpty(staffId))
            {
                query = query.Where(c => c.StaffId == staffId);
            }

            // FILTER: Tìm kiếm theo tên khách hàng hoặc nội dung tin nhắn
            if (!string.IsNullOrEmpty(searchText))
            {
                var searchLower = searchText.ToLower();
                var conversationIdsWithMatchingMessages = await _context.SupportMessages
                    .Where(m => m.Message.ToLower().Contains(searchLower))
                    .Select(m => m.ConversationId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(c => 
                    (c.Customer != null && c.Customer.FullName != null && c.Customer.FullName.ToLower().Contains(searchLower)) ||
                    (c.Customer != null && c.Customer.UserName != null && c.Customer.UserName.ToLower().Contains(searchLower)) ||
                    conversationIdsWithMatchingMessages.Contains(c.Id)
                );
            }

            var conversations = await query
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .ToListAsync();

            // Get last message info for each conversation
            var conversationsWithLastMessageInfo = new List<object>();
            foreach (var c in conversations)
            {
                // Get last message to determine sender
                var lastMsg = await _context.SupportMessages
                    .Where(m => m.ConversationId == c.Id)
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => new { m.IsFromStaff, m.SenderId })
                    .FirstOrDefaultAsync();

                conversationsWithLastMessageInfo.Add(new
                {
                    id = c.Id,
                    customerId = c.CustomerId,
                    customerName = c.Customer != null ? c.Customer.FullName ?? c.Customer.UserName : "Unknown",
                    customerAvatar = c.Customer != null ? c.Customer.ProfileImageUrl : null,
                    staffId = c.StaffId,
                    staffName = c.Staff != null ? c.Staff.FullName ?? c.Staff.UserName : null,
                    staffAvatar = c.Staff != null ? c.Staff.ProfileImageUrl : null,
                    lastMessage = c.LastMessage,
                    lastMessageAt = c.LastMessageAt,
                    lastMessageIsFromStaff = lastMsg?.IsFromStaff ?? false,
                    lastMessageSenderId = lastMsg?.SenderId,
                    createdAt = c.CreatedAt,
                    isActive = c.IsActive,
                    isClosed = c.IsClosed,
                    unreadByStaff = c.UnreadByStaff,
                    unreadByCustomer = c.UnreadByCustomer,
                    tag = c.Tag,
                    priority = c.Priority
                });
            }

            return Ok(conversationsWithLastMessageInfo);
        }

        /// <summary>
        /// Lấy chi tiết một conversation
        /// </summary>
        [HttpGet("conversation/{id}")]
        public async Task<IActionResult> GetConversation(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            var conversation = await _context.SupportConversations
                .Include(c => c.Customer)
                .Include(c => c.Staff)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (conversation == null)
                return NotFound();

            // Kiểm tra quyền truy cập
            if (!isStaff && conversation.CustomerId != userId)
                return Forbid();

            // Kiểm tra user có online không (dựa vào active SignalR connections)
            var isCustomerOnline = Hubs.ChatHub.IsUserOnline(conversation.CustomerId);
            
            return Ok(new
            {
                id = conversation.Id,
                customerId = conversation.CustomerId,
                customerName = conversation.Customer?.FullName ?? conversation.Customer?.UserName ?? "Unknown",
                customerAvatar = conversation.Customer?.ProfileImageUrl,
                customerLastSeenAt = conversation.Customer?.LastSeenAt,
                isCustomerOnline = isCustomerOnline,
                staffId = conversation.StaffId,
                staffName = conversation.Staff?.FullName ?? conversation.Staff?.UserName,
                staffAvatar = conversation.Staff?.ProfileImageUrl,
                lastMessage = conversation.LastMessage,
                lastMessageAt = conversation.LastMessageAt,
                createdAt = conversation.CreatedAt,
                isActive = conversation.IsActive,
                isClosed = conversation.IsClosed,
                unreadByStaff = conversation.UnreadByStaff,
                unreadByCustomer = conversation.UnreadByCustomer,
                isBlockedFromChat = conversation.Customer?.IsBlockedFromChat ?? false,
                blockedFromChatAt = conversation.Customer?.BlockedFromChatAt,
                blockedFromChatReason = conversation.Customer?.BlockedFromChatReason
            });
        }

        /// <summary>
        /// Lấy danh sách messages trong một conversation
        /// </summary>
        [HttpGet("conversation/{id}/messages")]
        public async Task<IActionResult> GetMessages(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            var conversation = await _context.SupportConversations.FindAsync(id);
            if (conversation == null)
                return NotFound();

            // Kiểm tra quyền truy cập
            if (!isStaff && conversation.CustomerId != userId)
                return Forbid();

            var messages = await _context.SupportMessages
                .Where(m => m.ConversationId == id)
                .Include(m => m.Sender)
                .OrderByDescending(m => m.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new
                {
                    id = m.Id,
                    conversationId = m.ConversationId,
                    senderId = m.SenderId,
                    senderName = m.Sender != null ? m.Sender.FullName ?? m.Sender.UserName : "Unknown",
                    senderAvatar = m.Sender != null ? m.Sender.ProfileImageUrl : null,
                    message = m.Message,
                    sentAt = m.SentAt,
                    isRead = m.IsRead,
                    readAt = m.ReadAt,
                    isFromStaff = m.IsFromStaff,
                    attachmentUrl = m.AttachmentUrl
                })
                .ToListAsync();

            return Ok(messages.OrderBy(m => m.sentAt)); // Đảo lại để hiển thị từ cũ đến mới
        }

        /// <summary>
        /// Bắt đầu conversation mới (Customer)
        /// </summary>
        [HttpPost("conversation/start")]
        public async Task<IActionResult> StartConversation([FromBody] StartConversationRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Kiểm tra xem đã có conversation chưa (kể cả đã đóng)
            var existingConversation = await _context.SupportConversations
                .Where(c => c.CustomerId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            if (existingConversation != null)
            {
                // Nếu hội thoại đã đóng, mở lại và reset staff
                if (existingConversation.IsClosed)
                {
                    existingConversation.IsClosed = false;
                    existingConversation.StaffId = null; // Reset staff để người khác có thể nhận
                    await _context.SaveChangesAsync();
                }
                
                // Trả về conversation đó (cũ hoặc vừa mở lại)
                return Ok(new { conversationId = existingConversation.Id });
            }

            // Tạo conversation mới
            var conversation = new SupportConversation
            {
                CustomerId = userId,
                CreatedAt = DateTime.Now,
                IsActive = true,
                IsClosed = false,
                UnreadByStaff = 0,
                UnreadByCustomer = 0
            };

            _context.SupportConversations.Add(conversation);
            await _context.SaveChangesAsync();

            // Nếu có tin nhắn đầu tiên, gửi luôn
            if (!string.IsNullOrWhiteSpace(request.InitialMessage))
            {
                var message = new SupportMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = userId,
                    Message = request.InitialMessage,
                    SentAt = DateTime.Now,
                    IsRead = false,
                    IsFromStaff = false
                };

                _context.SupportMessages.Add(message);
                conversation.LastMessage = request.InitialMessage.Length > 100 
                    ? request.InitialMessage.Substring(0, 100) + "..." 
                    : request.InitialMessage;
                conversation.LastMessageAt = DateTime.Now;
                conversation.UnreadByStaff = 1;

                await _context.SaveChangesAsync();
            }

            return Ok(new { conversationId = conversation.Id });
        }

        /// <summary>
        /// Assign staff cho conversation (Admin/Manager/Staff)
        /// </summary>
        [HttpPut("conversation/{id}/assign")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> AssignStaff(int id, [FromBody] AssignStaffRequest request)
        {
            var conversation = await _context.SupportConversations.FindAsync(id);
            if (conversation == null)
                return NotFound();

            var staff = await _context.Users.FindAsync(request.StaffId);
            if (staff == null)
                return BadRequest("Staff not found");

            conversation.StaffId = request.StaffId;
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        /// <summary>
        /// Đóng conversation (Admin/Manager/Staff)
        /// </summary>
        [HttpPut("conversation/{id}/close")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> CloseConversation(int id)
        {
            var conversation = await _context.SupportConversations.FindAsync(id);
            if (conversation == null)
                return NotFound();

            conversation.IsClosed = true;
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        /// <summary>
        /// Mở lại conversation (Customer hoặc Staff)
        /// </summary>
        [HttpPut("conversation/{id}/reopen")]
        public async Task<IActionResult> ReopenConversation(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            var conversation = await _context.SupportConversations.FindAsync(id);
            if (conversation == null)
                return NotFound();

            // Kiểm tra quyền
            if (!isStaff && conversation.CustomerId != userId)
                return Forbid();

            conversation.IsClosed = false;
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        /// <summary>
        /// Xóa vĩnh viễn conversation (chỉ Admin/Manager)
        /// </summary>
        [HttpDelete("conversation/{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteConversation(int id)
        {
            var conversation = await _context.SupportConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id);
            
            if (conversation == null)
                return NotFound();

            // Xóa tất cả tin nhắn trong conversation
            _context.SupportMessages.RemoveRange(conversation.Messages);
            
            // Xóa conversation
            _context.SupportConversations.Remove(conversation);
            
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa hội thoại vĩnh viễn" });
        }

        /// <summary>
        /// Lấy danh sách tin nhắn gần đây (Admin/Manager/Staff only)
        /// </summary>
        [HttpGet("recent-messages")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> GetRecentMessages([FromQuery] int limit = 5)
        {
            var conversations = await _context.SupportConversations
                .Include(c => c.Customer)
                .Where(c => !c.IsClosed) // Lấy TẤT CẢ conversation chưa đóng (cả đã đọc và chưa đọc)
                .OrderByDescending(c => c.LastMessageAt)
                .Take(limit)
                .Select(c => new
                {
                    id = c.Id,
                    customerName = c.Customer.FullName ?? c.Customer.UserName,
                    lastMessage = c.LastMessage,
                    lastMessageAt = c.LastMessageAt,
                    unreadByStaff = c.UnreadByStaff,
                    isRead = c.UnreadByStaff == 0 // Thêm trường để phân biệt đã đọc/chưa đọc
                })
                .ToListAsync();

            return Ok(new { conversations });
        }

        /// <summary>
        /// Đánh dấu conversation là đã đọc (khi admin click vào tin nhắn)
        /// </summary>
        [HttpPost("mark-read/{conversationId}")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> MarkAsRead(int conversationId)
        {
            var conversation = await _context.SupportConversations.FindAsync(conversationId);
            if (conversation == null)
                return NotFound();

            // Reset unread counter cho staff
            conversation.UnreadByStaff = 0;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã đánh dấu là đã đọc" });
        }

        /// <summary>
        /// Lấy số lượng tin nhắn chưa đọc
        /// </summary>
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            int unreadCount;

            if (isStaff)
            {
                // Đếm tất cả conversations có UnreadByStaff > 0
                unreadCount = await _context.SupportConversations
                    .Where(c => !c.IsClosed && c.UnreadByStaff > 0)
                    .SumAsync(c => c.UnreadByStaff);
            }
            else
            {
                // Đếm conversations của customer có UnreadByCustomer > 0
                unreadCount = await _context.SupportConversations
                    .Where(c => c.CustomerId == userId && !c.IsClosed && c.UnreadByCustomer > 0)
                    .SumAsync(c => c.UnreadByCustomer);
            }

            return Ok(new { unreadCount = unreadCount });
        }

        /// <summary>
        /// Upload ảnh cho chat
        /// </summary>
        [HttpPost("upload-image")]
        public async Task<IActionResult> UploadImage(IFormFile image)
        {
            if (image == null || image.Length == 0)
                return BadRequest(new { message = "Không có file được chọn" });

            // Validate file type
            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
            if (!allowedTypes.Contains(image.ContentType.ToLower()))
                return BadRequest(new { message = "Chỉ chấp nhận file ảnh (jpg, png, gif, webp)" });

            // Validate file size (max 5MB)
            if (image.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "Kích thước ảnh không được vượt quá 5MB" });

            try
            {
                // Create uploads directory if not exists
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "chat");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                // Generate unique filename
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                // Return URL
                var imageUrl = $"/uploads/chat/{fileName}";
                return Ok(new { imageUrl = imageUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi upload ảnh: " + ex.Message });
            }
        }

        /// <summary>
        /// Xóa một tin nhắn
        /// </summary>
        [HttpDelete("message/{messageId}")]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            var message = await _context.SupportMessages
                .Include(m => m.Conversation)
                .FirstOrDefaultAsync(m => m.Id == messageId);

            if (message == null)
                return NotFound(new { message = "Tin nhắn không tồn tại" });

            // Check permissions: staff can delete any message, customers can only delete their own
            if (!isStaff && message.SenderId != userId)
                return Forbid();

            _context.SupportMessages.Remove(message);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa tin nhắn thành công" });
        }

        /// <summary>
        /// Xóa nhiều tin nhắn
        /// </summary>
        [HttpDelete("messages")]
        public async Task<IActionResult> DeleteMessages([FromBody] int[] messageIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");

            if (messageIds == null || messageIds.Length == 0)
                return BadRequest(new { message = "Không có tin nhắn nào được chọn" });

            var messages = await _context.SupportMessages
                .Where(m => messageIds.Contains(m.Id))
                .ToListAsync();

            if (messages.Count == 0)
                return NotFound(new { message = "Không tìm thấy tin nhắn nào" });

            // Check permissions: staff can delete any message, customers can only delete their own
            if (!isStaff)
            {
                var unauthorizedMessages = messages.Where(m => m.SenderId != userId).ToList();
                if (unauthorizedMessages.Any())
                    return Forbid();
            }

            _context.SupportMessages.RemoveRange(messages);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Đã xóa {messages.Count} tin nhắn thành công" });
        }

        /// <summary>
        /// Thu hồi tin nhắn (customer chỉ thu hồi trong 5 phút)
        /// </summary>
        [HttpPost("message/{id}/recall")]
        public async Task<IActionResult> RecallMessage(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                var message = await _context.SupportMessages
                    .Include(m => m.Conversation)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (message == null)
                    return NotFound(new { message = "Tin nhắn không tồn tại" });

                // Kiểm tra quyền: chỉ người gửi mới được thu hồi
                if (message.SenderId != userId)
                    return Forbid();

                // Kiểm tra thời gian: chỉ thu hồi trong vòng 5 phút (dùng DateTime.Now thay vì UTC)
                var timeSinceSent = DateTime.Now - message.SentAt;
                if (timeSinceSent.TotalMinutes > 5)
                    return BadRequest(new { message = "Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi" });

                // Cập nhật nội dung tin nhắn
                message.Message = "[Tin nhắn đã được thu hồi]";
                message.AttachmentUrl = null; // Xóa attachment nếu có
                
                // Cập nhật LastMessage của conversation nếu đây là tin nhắn cuối
                if (message.Conversation != null)
                {
                    var lastMessage = await _context.SupportMessages
                        .Where(m => m.ConversationId == message.ConversationId)
                        .OrderByDescending(m => m.SentAt)
                        .FirstOrDefaultAsync();
                    
                    if (lastMessage != null && lastMessage.Id == id)
                    {
                        message.Conversation.LastMessage = "[Tin nhắn đã được thu hồi]";
                    }
                }
                
                await _context.SaveChangesAsync();

                return Ok(new { 
                    success = true,
                    message = "Thu hồi tin nhắn thành công",
                    messageId = id,
                    conversationId = message.ConversationId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi thu hồi tin nhắn: " + ex.Message });
            }
        }

        /// <summary>
        /// Update tag và priority cho conversation (Admin/Staff only)
        /// </summary>
        [HttpPost("conversation/{id}/tag")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> UpdateConversationTag(int id, [FromBody] UpdateTagRequest request)
        {
            var conversation = await _context.SupportConversations.FindAsync(id);
            if (conversation == null)
                return NotFound(new { message = "Không tìm thấy hội thoại" });

            conversation.Tag = request.Tag;
            conversation.Priority = request.Priority;
            await _context.SaveChangesAsync();

            return Ok(new { 
                success = true, 
                tag = conversation.Tag,
                priority = conversation.Priority
            });
        }

        /// <summary>
        /// Tìm kiếm tin nhắn theo nội dung
        /// </summary>
        [HttpPost("search-messages")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> SearchMessages([FromBody] SearchMessagesRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
                return BadRequest(new { message = "Vui lòng nhập từ khóa tìm kiếm" });

            var searchLower = request.SearchText.ToLower();

            var messagesQuery = _context.SupportMessages
                .Include(m => m.Conversation)
                    .ThenInclude(c => c!.Customer)
                .Include(m => m.Sender)
                .Where(m => m.Message.ToLower().Contains(searchLower));

            // Nếu chỉ tìm trong 1 conversation cụ thể
            if (request.ConversationId.HasValue)
            {
                messagesQuery = messagesQuery.Where(m => m.ConversationId == request.ConversationId.Value);
            }

            var messages = await messagesQuery
                .OrderByDescending(m => m.SentAt)
                .Take(100) // Giới hạn 100 kết quả
                .Select(m => new
                {
                    id = m.Id,
                    conversationId = m.ConversationId,
                    conversationCustomerName = m.Conversation != null && m.Conversation.Customer != null 
                        ? (m.Conversation.Customer.FullName ?? m.Conversation.Customer.UserName ?? "Unknown")
                        : "Unknown",
                    senderId = m.SenderId,
                    senderName = m.Sender != null ? (m.Sender.FullName ?? m.Sender.UserName) : "Unknown",
                    message = m.Message,
                    attachmentUrl = m.AttachmentUrl,
                    sentAt = m.SentAt,
                    isFromStaff = m.IsFromStaff
                })
                .ToListAsync();

            return Ok(new { 
                success = true, 
                count = messages.Count,
                results = messages 
            });
        }

        /// <summary>
        /// Lấy danh sách tất cả staff (Admin, Manager, Staff)
        /// </summary>
        [HttpGet("staff-list")]
        [Authorize(Roles = "Admin,Manager,Staff")]
        public async Task<IActionResult> GetStaffList()
        {
            var admins = await _context.Users
                .Where(u => _context.UserRoles
                    .Join(_context.Roles,
                        ur => ur.RoleId,
                        r => r.Id,
                        (ur, r) => new { ur.UserId, r.Name })
                    .Any(x => x.UserId == u.Id && (x.Name == "Admin" || x.Name == "Manager" || x.Name == "Staff")))
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName ?? u.UserName ?? "Unknown",
                    email = u.Email,
                    role = _context.UserRoles
                        .Join(_context.Roles,
                            ur => ur.RoleId,
                            r => r.Id,
                            (ur, r) => new { ur.UserId, r.Name })
                        .Where(x => x.UserId == u.Id && (x.Name == "Admin" || x.Name == "Manager" || x.Name == "Staff"))
                        .Select(x => x.Name)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(admins);
        }

        /// <summary>
        /// Lấy thông tin giờ làm việc của shop (Public - không cần auth)
        /// </summary>
        [HttpGet("working-hours")]
        [AllowAnonymous]
        public IActionResult GetWorkingHours([FromServices] Services.AutoReplyService autoReplyService)
        {
            var info = autoReplyService.GetWorkingHoursInfo();
            return Ok(info);
        }

        /// <summary>
        /// Block user khỏi chat (Admin/Manager only)
        /// </summary>
        [HttpPost("block-user")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> BlockUser([FromBody] BlockUserRequest request)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy người dùng" });

            // Không cho phép block admin khác
            var isTargetAdmin = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == request.UserId && 
                    _context.Roles.Any(r => r.Id == ur.RoleId && (r.Name == "Admin" || r.Name == "Manager")));
            
            if (isTargetAdmin)
                return BadRequest(new { success = false, message = "Không thể block Admin/Manager" });

            user.IsBlockedFromChat = true;
            user.BlockedFromChatAt = DateTime.Now;
            user.BlockedFromChatReason = request.Reason ?? "Vi phạm chính sách sử dụng";
            user.BlockedByUserId = adminId;

            await _context.SaveChangesAsync();

            // Gửi SignalR event realtime cho customer
            await _chatHubContext.Clients.User(request.UserId).SendAsync("UserBlocked", new
            {
                message = user.BlockedFromChatReason,
                blockedAt = user.BlockedFromChatAt,
                reason = user.BlockedFromChatReason
            });

            return Ok(new { 
                success = true, 
                message = "Đã block người dùng khỏi chat",
                userId = user.Id,
                userName = user.FullName ?? user.UserName
            });
        }

        /// <summary>
        /// Unblock user (Admin/Manager only)
        /// </summary>
        [HttpPost("unblock-user")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UnblockUser([FromBody] UnblockUserRequest request)
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy người dùng" });

            user.IsBlockedFromChat = false;
            user.BlockedFromChatAt = null;
            user.BlockedFromChatReason = null;
            user.BlockedByUserId = null;

            await _context.SaveChangesAsync();

            // Gửi SignalR event realtime cho customer
            await _chatHubContext.Clients.User(request.UserId).SendAsync("UserUnblocked", new
            {
                message = "Bạn đã được mở khóa và có thể gửi tin nhắn trở lại"
            });

            return Ok(new { 
                success = true, 
                message = "Đã mở khóa người dùng",
                userId = user.Id,
                userName = user.FullName ?? user.UserName
            });
        }

        /// <summary>
        /// Chuyển conversation cho staff khác
        /// </summary>
        [HttpPost("transfer/{conversationId}")]
        public async Task<IActionResult> TransferConversation(int conversationId, [FromBody] TransferChatRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var conversation = await _context.SupportConversations
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation == null)
                return NotFound(new { message = "Conversation không tồn tại" });

            var targetStaff = await _context.Users.FindAsync(request.StaffId);
            if (targetStaff == null)
                return BadRequest(new { message = "Staff không tồn tại" });

            var currentStaffName = User.Identity?.Name ?? "Unknown";

            // Update conversation
            conversation.StaffId = request.StaffId;
            conversation.UpdatedAt = DateTime.Now;

            // Create system message
            var systemMessage = new SupportMessage
            {
                ConversationId = conversationId,
                Message = $"💼 Conversation đã được chuyển từ {currentStaffName} đến {targetStaff.FullName ?? targetStaff.UserName}" +
                          (string.IsNullOrEmpty(request.Reason) ? "" : $"\n📝 Lý do: {request.Reason}"),
                IsFromStaff = true,
                SentAt = DateTime.Now,
                IsRead = false
            };
            _context.SupportMessages.Add(systemMessage);

            await _context.SaveChangesAsync();

            // Send notification to new staff
            if (request.Notify && conversation.Customer != null)
            {
                await _chatHubContext.Clients.User(request.StaffId).SendAsync("ConversationTransferred", new
                {
                    conversationId = conversationId,
                    customerName = conversation.Customer.FullName ?? conversation.Customer.UserName ?? "Khách hàng",
                    from = currentStaffName,
                    reason = request.Reason
                });
            }

            return Ok(new { 
                success = true, 
                message = "Đã chuyển conversation thành công",
                newStaffName = targetStaff.FullName ?? targetStaff.UserName
            });
        }

        /// <summary>
        /// Analytics Dashboard - Báo cáo thống kê
        /// </summary>
        [HttpGet("analytics")]
        public async Task<IActionResult> GetAnalytics([FromQuery] DateTime from, [FromQuery] DateTime to)
        {
            try
            {
                // Total conversations in date range
                var totalConversations = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                    .CountAsync();

                // Resolved conversations (closed)
                var resolvedConversations = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to && c.IsClosed)
                    .CountAsync();

                // Pending conversations (not closed)
                var pendingConversations = totalConversations - resolvedConversations;

                // Average response time (in minutes)
                var avgResponseTime = await CalculateAverageResponseTime(from, to);

                // Daily conversations
                var dailyConversationsRaw = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                    .GroupBy(c => c.CreatedAt.Date)
                    .Select(g => new
                    {
                        date = g.Key,
                        count = g.Count()
                    })
                    .OrderBy(x => x.date)
                    .ToListAsync();
                
                var dailyConversations = dailyConversationsRaw
                    .Select(x => new
                    {
                        date = x.date.ToString("dd/MM"),
                        count = x.count
                    })
                    .ToList();

                // Tag distribution
                var tagDistribution = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                    .GroupBy(c => c.Tag ?? "Không tag")
                    .Select(g => new
                    {
                        tag = g.Key,
                        count = g.Count()
                    })
                    .OrderByDescending(x => x.count)
                    .ToListAsync();

                // Staff performance - Simplified query
                var staffPerformanceData = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to && c.StaffId != null)
                    .GroupBy(c => c.StaffId)
                    .Select(g => new
                    {
                        staffId = g.Key,
                        totalConversations = g.Count(),
                        resolvedConversations = g.Count(c => c.IsClosed)
                    })
                    .ToListAsync();

                // Get staff names separately
                var staffIds = staffPerformanceData.Select(s => s.staffId).ToList();
                var staffUsers = await _context.Users
                    .Where(u => staffIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName, u.UserName })
                    .ToListAsync();

                var staffStats = staffPerformanceData
                    .Select(s =>
                    {
                        var staff = staffUsers.FirstOrDefault(u => u.Id == s.staffId);
                        return new
                        {
                            staffId = s.staffId,
                            staffName = staff?.FullName ?? staff?.UserName ?? "Unknown",
                            role = "Staff",
                            totalConversations = s.totalConversations,
                            resolvedConversations = s.resolvedConversations,
                            avgResponseTime = "N/A"
                        };
                    })
                    .OrderByDescending(x => x.totalConversations)
                    .Take(10)
                    .ToList();

                // Hourly distribution
                var hourlyDistribution = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                    .GroupBy(c => c.CreatedAt.Hour)
                    .Select(g => new
                    {
                        hour = g.Key,
                        count = g.Count()
                    })
                    .OrderBy(x => x.hour)
                    .ToListAsync();

                // Priority distribution
                var priorityDistribution = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                    .GroupBy(c => c.Priority)
                    .Select(g => new
                    {
                        priority = g.Key,
                        count = g.Count()
                    })
                    .OrderBy(x => x.priority)
                    .ToListAsync();

                // Top customers - Simplified query
                var topCustomersData = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to && c.CustomerId != null)
                    .GroupBy(c => c.CustomerId)
                    .Select(g => new
                    {
                        customerId = g.Key,
                        conversationCount = g.Count()
                    })
                    .OrderByDescending(x => x.conversationCount)
                    .Take(10)
                    .ToListAsync();

                // Get customer names separately
                var customerIds = topCustomersData.Select(c => c.customerId).ToList();
                var customers = await _context.Users
                    .Where(u => customerIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName, u.UserName })
                    .ToListAsync();

                var topCustomers = topCustomersData
                    .Select(c =>
                    {
                        var customer = customers.FirstOrDefault(u => u.Id == c.customerId);
                        return new
                        {
                            customerId = c.customerId,
                            customerName = customer?.FullName ?? customer?.UserName ?? "Khách hàng",
                            conversationCount = c.conversationCount
                        };
                    })
                    .ToList();

                // Keywords (simple implementation - extract from messages)
                var keywords = new List<object>
                {
                    new { keyword = "giá", count = 0 },
                    new { keyword = "giao hàng", count = 0 },
                    new { keyword = "đặt hàng", count = 0 },
                    new { keyword = "thanh toán", count = 0 },
                    new { keyword = "khuyến mãi", count = 0 }
                };

                return Ok(new
                {
                    totalConversations,
                    resolvedConversations,
                    pendingConversations,
                    avgResponseTime,
                    dailyConversations,
                    tagDistribution,
                    staffPerformance = staffStats,
                    hourlyDistribution,
                    priorityDistribution,
                    topCustomers,
                    keywords
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy analytics", error = ex.Message });
            }
        }

        private async Task<string> CalculateAverageResponseTime(DateTime from, DateTime to)
        {
            try
            {
                // Get conversation IDs first
                var conversationIds = await _context.SupportConversations
                    .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (!conversationIds.Any())
                    return "N/A";

                var responseTimes = new List<double>();
                
                // Process in batches to avoid memory issues
                foreach (var convId in conversationIds)
                {
                    var messages = await _context.SupportMessages
                        .Where(m => m.ConversationId == convId)
                        .OrderBy(m => m.SentAt)
                        .Take(10) // Only take first 10 messages to find first response
                        .ToListAsync();

                    if (!messages.Any()) continue;

                    var firstCustomerMsg = messages.FirstOrDefault(m => !m.IsFromStaff);
                    var firstStaffMsg = messages.FirstOrDefault(m => m.IsFromStaff);

                    if (firstCustomerMsg != null && firstStaffMsg != null && firstStaffMsg.SentAt > firstCustomerMsg.SentAt)
                    {
                        var diff = (firstStaffMsg.SentAt - firstCustomerMsg.SentAt).TotalMinutes;
                        responseTimes.Add(diff);
                    }
                }

                if (responseTimes.Count == 0)
                    return "N/A";

                var avgMinutes = responseTimes.Average();
                
                if (avgMinutes < 1)
                    return "< 1 phút";
                else if (avgMinutes < 60)
                    return $"{Math.Round(avgMinutes)} phút";
                else
                    return $"{Math.Round(avgMinutes / 60, 1)} giờ";
            }
            catch
            {
                return "N/A";
            }
        }
    }

    // Request models
    public class BlockUserRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public class UnblockUserRequest
    {
        public string UserId { get; set; } = string.Empty;
    }
    public class StartConversationRequest
    {
        public string? InitialMessage { get; set; }
    }

    public class AssignStaffRequest
    {
        public string StaffId { get; set; } = string.Empty;
    }

    public class UpdateTagRequest
    {
        public string? Tag { get; set; }
        public int Priority { get; set; } = 0;
    }

    public class SearchMessagesRequest
    {
        public string SearchText { get; set; } = string.Empty;
        public int? ConversationId { get; set; }
    }

    public class TransferChatRequest
    {
        public string StaffId { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public bool Notify { get; set; } = true;
    }
}
