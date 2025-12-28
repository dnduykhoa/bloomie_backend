using System.Collections.Concurrent;

namespace Bloomie.Services
{
    /// <summary>
    /// Service để chống spam tin nhắn - Rate Limiting
    /// Giới hạn số tin nhắn mỗi user có thể gửi trong khoảng thời gian
    /// </summary>
    public class RateLimitService
    {
        // Dictionary lưu danh sách timestamp các tin nhắn của từng userId
        private static readonly ConcurrentDictionary<string, List<DateTime>> _userMessageTimestamps = new();
        
        // Dictionary lưu thời điểm bị block của từng userId
        private static readonly ConcurrentDictionary<string, DateTime> _userBlockedUntil = new();
        
        private readonly int _maxMessagesPerWindow = 5; // Max 5 tin nhắn
        private readonly TimeSpan _timeWindow = TimeSpan.FromSeconds(60); // Trong 60 giây
        private readonly TimeSpan _blockDuration = TimeSpan.FromSeconds(30); // Block 30 giây

        /// <summary>
        /// Kiểm tra xem user có được phép gửi tin nhắn không
        /// </summary>
        /// <param name="userId">ID của user</param>
        /// <param name="remainingSeconds">Số giây còn lại nếu bị block</param>
        /// <returns>True nếu được phép gửi, False nếu bị chặn</returns>
        public bool CanSendMessage(string userId, out int remainingSeconds)
        {
            remainingSeconds = 0;
            var now = DateTime.Now;

            // 1. Kiểm tra xem user có đang bị block không
            if (_userBlockedUntil.TryGetValue(userId, out DateTime blockedUntil))
            {
                if (now < blockedUntil)
                {
                    // Vẫn còn trong thời gian block
                    remainingSeconds = (int)(blockedUntil - now).TotalSeconds;
                    return false;
                }
                else
                {
                    // Hết thời gian block, xóa khỏi dictionary
                    _userBlockedUntil.TryRemove(userId, out _);
                }
            }

            // 2. Lấy hoặc tạo danh sách timestamps của user
            var timestamps = _userMessageTimestamps.GetOrAdd(userId, _ => new List<DateTime>());

            // 3. Dọn dẹp các timestamp cũ (ngoài time window)
            lock (timestamps)
            {
                timestamps.RemoveAll(t => now - t > _timeWindow);

                // 4. Kiểm tra số lượng tin nhắn trong time window
                if (timestamps.Count >= _maxMessagesPerWindow)
                {
                    // Đã vượt quá giới hạn → Block user
                    var blockUntil = now.Add(_blockDuration);
                    _userBlockedUntil[userId] = blockUntil;
                    remainingSeconds = (int)_blockDuration.TotalSeconds;

                    // Xóa timestamps để reset
                    timestamps.Clear();

                    return false;
                }

                // 5. Thêm timestamp hiện tại
                timestamps.Add(now);
            }

            return true;
        }

        /// <summary>
        /// Lấy thông tin rate limit của user
        /// </summary>
        public object GetUserRateLimitInfo(string userId)
        {
            var now = DateTime.Now;
            var isBlocked = false;
            var remainingSeconds = 0;

            if (_userBlockedUntil.TryGetValue(userId, out DateTime blockedUntil))
            {
                if (now < blockedUntil)
                {
                    isBlocked = true;
                    remainingSeconds = (int)(blockedUntil - now).TotalSeconds;
                }
            }

            var timestamps = _userMessageTimestamps.GetOrAdd(userId, _ => new List<DateTime>());
            int messageCount = 0;
            
            lock (timestamps)
            {
                timestamps.RemoveAll(t => now - t > _timeWindow);
                messageCount = timestamps.Count;
            }

            return new
            {
                isBlocked = isBlocked,
                remainingSeconds = remainingSeconds,
                messageCount = messageCount,
                maxMessages = _maxMessagesPerWindow,
                timeWindowSeconds = (int)_timeWindow.TotalSeconds
            };
        }

        /// <summary>
        /// Reset rate limit cho một user (dùng khi cần unlock thủ công)
        /// </summary>
        public void ResetUserRateLimit(string userId)
        {
            _userBlockedUntil.TryRemove(userId, out _);
            if (_userMessageTimestamps.TryGetValue(userId, out var timestamps))
            {
                lock (timestamps)
                {
                    timestamps.Clear();
                }
            }
        }

        /// <summary>
        /// Cleanup định kỳ để xóa dữ liệu cũ (tránh memory leak)
        /// Nên gọi trong background task
        /// </summary>
        public void CleanupOldData()
        {
            var now = DateTime.Now;
            
            // Xóa các block entries đã hết hạn
            foreach (var kvp in _userBlockedUntil.ToArray())
            {
                if (now > kvp.Value.Add(TimeSpan.FromMinutes(5)))
                {
                    _userBlockedUntil.TryRemove(kvp.Key, out _);
                }
            }

            // Xóa timestamps của users không hoạt động > 10 phút
            foreach (var kvp in _userMessageTimestamps.ToArray())
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.Count == 0 || now - kvp.Value.Last() > TimeSpan.FromMinutes(10))
                    {
                        _userMessageTimestamps.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
    }
}
