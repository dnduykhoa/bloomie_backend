using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using System.Threading.Tasks;

namespace Bloomie.Services
{
    /// <summary>
    /// Service để phát hiện và xử lý spam tự động
    /// </summary>
    public class SpamDetectionService
    {
        // Lưu lịch sử tin nhắn gần đây của mỗi user (để phát hiện duplicate)
        private static readonly ConcurrentDictionary<string, List<MessageHistory>> _userMessageHistory 
            = new ConcurrentDictionary<string, List<MessageHistory>>();

        // Đếm số lần vi phạm của mỗi user
        private static readonly ConcurrentDictionary<string, int> _userViolationCount 
            = new ConcurrentDictionary<string, int>();

        // Danh sách từ khóa cấm (có thể mở rộng)
        private static readonly HashSet<string> _blacklistKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "casino", "cờ bạc", "đánh bạc", "cá cược", "vay tiền", "vay nợ",
            "18+", "xxx", "porn", "sex", "ma túy", "ma tuý",
            "hack", "lừa đảo", "lua dao", "kiếm tiền nhanh", "kiem tien nhanh",
            "đa cấp", "da cap", "bán hàng đa cấp", "MLM", "pyramid"
        };

        private readonly ApplicationDbContext _context;

        // Cấu hình
        private const int MAX_DUPLICATE_MESSAGES = 3; // Gửi trùng 3 lần → spam
        private const int MAX_LINKS_PER_MESSAGE = 3; // Quá 3 links → spam
        private const int MAX_VIOLATIONS_BEFORE_BLOCK = 3; // 3 lần vi phạm → auto block
        private const int MESSAGE_HISTORY_SIZE = 10; // Lưu 10 tin nhắn gần nhất

        public SpamDetectionService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Kiểm tra tin nhắn có spam không
        /// </summary>
        public async Task<SpamCheckResult> CheckMessageAsync(string userId, string message)
        {
            var result = new SpamCheckResult { IsSpam = false };

            // 1. Kiểm tra duplicate messages
            var duplicateCheck = CheckDuplicateMessage(userId, message);
            if (duplicateCheck.IsSpam)
            {
                result.IsSpam = true;
                result.Reason = duplicateCheck.Reason;
                result.ViolationType = "Duplicate";
            }

            // 2. Kiểm tra từ khóa cấm
            if (!result.IsSpam)
            {
                var keywordCheck = CheckBlacklistKeywords(message);
                if (keywordCheck.IsSpam)
                {
                    result.IsSpam = true;
                    result.Reason = keywordCheck.Reason;
                    result.ViolationType = "Blacklist";
                }
            }

            // 3. Kiểm tra link spam
            if (!result.IsSpam)
            {
                var linkCheck = CheckLinkSpam(message);
                if (linkCheck.IsSpam)
                {
                    result.IsSpam = true;
                    result.Reason = linkCheck.Reason;
                    result.ViolationType = "LinkSpam";
                }
            }

            // Nếu phát hiện spam → Tăng violation count
            if (result.IsSpam)
            {
                var violationCount = _userViolationCount.AddOrUpdate(userId, 1, (key, count) => count + 1);
                result.ViolationCount = violationCount;

                // Auto-block sau MAX_VIOLATIONS_BEFORE_BLOCK lần vi phạm
                if (violationCount >= MAX_VIOLATIONS_BEFORE_BLOCK)
                {
                    await AutoBlockUserAsync(userId, result.Reason);
                    result.UserBlocked = true;
                }
            }

            return result;
        }

        /// <summary>
        /// Kiểm tra tin nhắn trùng lặp
        /// </summary>
        private SpamCheckResult CheckDuplicateMessage(string userId, string message)
        {
            var result = new SpamCheckResult { IsSpam = false };

            // Normalize message (trim, lowercase)
            var normalizedMessage = message.Trim().ToLower();
            
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return result;

            // Lấy lịch sử tin nhắn của user
            var history = _userMessageHistory.GetOrAdd(userId, new List<MessageHistory>());

            // Đếm số lần tin nhắn này xuất hiện gần đây
            var duplicateCount = history.Count(h => 
                h.NormalizedMessage == normalizedMessage && 
                DateTime.Now.Subtract(h.SentAt).TotalMinutes < 5); // Trong vòng 5 phút

            if (duplicateCount >= MAX_DUPLICATE_MESSAGES)
            {
                result.IsSpam = true;
                result.Reason = $"Gửi tin nhắn trùng lặp {duplicateCount} lần";
            }

            // Thêm tin nhắn này vào lịch sử
            history.Add(new MessageHistory
            {
                NormalizedMessage = normalizedMessage,
                SentAt = DateTime.Now
            });

            // Giữ tối đa MESSAGE_HISTORY_SIZE tin nhắn gần nhất
            if (history.Count > MESSAGE_HISTORY_SIZE)
            {
                history.RemoveAt(0);
            }

            return result;
        }

        /// <summary>
        /// Kiểm tra từ khóa cấm
        /// </summary>
        private SpamCheckResult CheckBlacklistKeywords(string message)
        {
            var result = new SpamCheckResult { IsSpam = false };

            var lowerMessage = message.ToLower();

            foreach (var keyword in _blacklistKeywords)
            {
                if (lowerMessage.Contains(keyword.ToLower()))
                {
                    result.IsSpam = true;
                    result.Reason = $"Chứa từ khóa cấm: '{keyword}'";
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Kiểm tra spam link
        /// </summary>
        private SpamCheckResult CheckLinkSpam(string message)
        {
            var result = new SpamCheckResult { IsSpam = false };

            // Regex để tìm URLs
            var urlPattern = @"(https?:\/\/[^\s]+)|(www\.[^\s]+)|([a-zA-Z0-9-]+\.(com|net|org|vn|io|co)[^\s]*)";
            var matches = Regex.Matches(message, urlPattern, RegexOptions.IgnoreCase);

            if (matches.Count > MAX_LINKS_PER_MESSAGE)
            {
                result.IsSpam = true;
                result.Reason = $"Chứa quá nhiều link ({matches.Count} links)";
            }

            return result;
        }

        /// <summary>
        /// Tự động block user khi vi phạm quá nhiều
        /// </summary>
        private async Task AutoBlockUserAsync(string userId, string reason)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null && !user.IsBlockedFromChat)
                {
                    user.IsBlockedFromChat = true;
                    user.BlockedFromChatAt = DateTime.Now;
                    user.BlockedFromChatReason = $"Tự động chặn: {reason}";
                    user.BlockedByUserId = "SYSTEM"; // System auto-block

                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error auto-blocking user {userId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset violation count của user (khi admin unblock)
        /// </summary>
        public void ResetUserViolations(string userId)
        {
            _userViolationCount.TryRemove(userId, out _);
            _userMessageHistory.TryRemove(userId, out _);
        }

        /// <summary>
        /// Lấy thông tin vi phạm của user
        /// </summary>
        public UserViolationInfo GetUserViolationInfo(string userId)
        {
            var violationCount = _userViolationCount.GetOrAdd(userId, 0);
            var history = _userMessageHistory.GetOrAdd(userId, new List<MessageHistory>());

            return new UserViolationInfo
            {
                ViolationCount = violationCount,
                RecentMessageCount = history.Count,
                IsCloseToBlock = violationCount >= MAX_VIOLATIONS_BEFORE_BLOCK - 1
            };
        }

        /// <summary>
        /// Cleanup dữ liệu cũ (gọi định kỳ)
        /// </summary>
        public void CleanupOldData()
        {
            var cutoffTime = DateTime.Now.AddMinutes(-10);

            foreach (var kvp in _userMessageHistory)
            {
                var history = kvp.Value;
                history.RemoveAll(h => h.SentAt < cutoffTime);

                if (history.Count == 0)
                {
                    _userMessageHistory.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    // ===== Models =====

    public class SpamCheckResult
    {
        public bool IsSpam { get; set; }
        public string Reason { get; set; }
        public string ViolationType { get; set; } // "Duplicate", "Blacklist", "LinkSpam"
        public int ViolationCount { get; set; }
        public bool UserBlocked { get; set; }
    }

    public class MessageHistory
    {
        public string NormalizedMessage { get; set; }
        public DateTime SentAt { get; set; }
    }

    public class UserViolationInfo
    {
        public int ViolationCount { get; set; }
        public int RecentMessageCount { get; set; }
        public bool IsCloseToBlock { get; set; }
    }
}
