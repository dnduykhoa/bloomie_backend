using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Security.Claims;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRatingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminRatingController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10, int? stars = null, string? status = null, string? search = null, bool? hasReply = null, int? productId = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.Ratings
                .Include(r => r.User)
                .Include(r => r.Product)
                .Include(r => r.Replies).ThenInclude(reply => reply.User)
                .Include(r => r.Reports).ThenInclude(report => report.Reporter)
                .Include(r => r.Images)
                .AsQueryable();

                // Lọc theo sản phẩm
                if (productId.HasValue && productId.Value > 0)
                {
                    query = query.Where(r => r.ProductId == productId.Value);
                }

            // Lọc theo thời gian
            if (fromDate.HasValue)
            {
                query = query.Where(r => r.ReviewDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                // Thêm 1 ngày để bao gồm cả ngày toDate
                var endDate = toDate.Value.AddDays(1);
                query = query.Where(r => r.ReviewDate < endDate);
            }

            // Lọc theo số sao
            if (stars.HasValue && stars.Value >= 1 && stars.Value <= 5)
            {
                query = query.Where(r => r.Star == stars.Value);
            }

            // Lọc theo trạng thái
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "visible")
                    query = query.Where(r => r.IsVisible);
                else if (status == "hidden")
                    query = query.Where(r => !r.IsVisible);
                else if (status == "reported")
                    query = query.Where(r => r.Reports.Any(rp => !rp.IsResolved));
            }

            // Tìm kiếm theo sản phẩm hoặc người dùng
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(r => 
                    r.Product!.Name.Contains(search) || 
                    r.User!.FullName.Contains(search) ||
                    r.Comment.Contains(search));
            }

            // Lọc theo có phản hồi hay chưa
            if (hasReply.HasValue)
            {
                if (hasReply.Value)
                    query = query.Where(r => r.Replies.Any());
                else
                    query = query.Where(r => !r.Replies.Any());
            }

            var totalItems = await query.CountAsync();
            var ratings = await query
                .OrderByDescending(r => r.ReviewDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Thống kê
            ViewBag.TotalRatings = await _context.Ratings.CountAsync();
            ViewBag.TotalUnreplied = await _context.Ratings.Where(r => !r.Replies.Any()).CountAsync();
            ViewBag.TotalReported = await _context.Reports.Where(r => !r.IsResolved).CountAsync();
            ViewBag.Star1Count = await _context.Ratings.Where(r => r.Star == 1).CountAsync();
            ViewBag.Star2Count = await _context.Ratings.Where(r => r.Star == 2).CountAsync();
            ViewBag.Star3Count = await _context.Ratings.Where(r => r.Star == 3).CountAsync();
            ViewBag.Star4Count = await _context.Ratings.Where(r => r.Star == 4).CountAsync();
            ViewBag.Star5Count = await _context.Ratings.Where(r => r.Star == 5).CountAsync();
            ViewBag.AverageStar = await _context.Ratings.AverageAsync(r => (double?)r.Star) ?? 0;

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.Stars = stars;
            ViewBag.Status = status;
            ViewBag.Search = search;
            ViewBag.HasReply = hasReply;
            ViewBag.ProductId = productId;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            
            // Lấy danh sách sản phẩm cho dropdown
            ViewBag.Products = await _context.Products
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();

            // Thống kê top sản phẩm được đánh giá nhiều nhất
            ViewBag.TopProducts = await _context.Ratings
                .GroupBy(r => new { r.ProductId, r.Product!.Name })
                .Select(g => new { ProductName = g.Key.Name, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            // Thống kê top user đánh giá nhiều nhất
            ViewBag.TopUsers = await _context.Ratings
                .GroupBy(r => new { r.UserId, r.User!.FullName })
                .Select(g => new { UserName = g.Key.FullName, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            return View(ratings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRatingVisibility(int ratingId)
        {
            var rating = await _context.Ratings.FindAsync(ratingId);
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            rating.IsVisible = !rating.IsVisible;
            rating.LastModifiedBy = User.Identity?.Name ?? "Admin";
            rating.LastModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            return Json(new
            {
                success = true,
                isVisible = rating.IsVisible,
                message = $"Đánh giá đã được {(rating.IsVisible ? "hiển thị" : "ẩn")}",
                statusClass = rating.IsVisible ? "bg-success" : "bg-danger",
                statusText = rating.IsVisible ? "Hiển thị" : "Ẩn"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleReplyVisibility(int replyId)
        {
            var reply = await _context.Replies.FindAsync(replyId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phản hồi." });
            }

            reply.IsVisible = !reply.IsVisible;
            reply.LastModifiedBy = User.Identity?.Name ?? "Admin";
            reply.LastModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            return Json(new
            {
                success = true,
                isVisible = reply.IsVisible,
                message = $"Phản hồi đã được {(reply.IsVisible ? "hiển thị" : "ẩn")}",
                statusClass = reply.IsVisible ? "bg-success" : "bg-danger",
                statusText = reply.IsVisible ? "Hiển thị" : "Ẩn"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveReport(int reportId, bool isApproved)
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Không tìm thấy báo cáo." });
            }

            var rating = await _context.Ratings
                .Include(r => r.Reports)
                .FirstOrDefaultAsync(r => r.Reports.Any(rp => rp.Id == reportId));
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá liên quan." });
            }

            report.IsResolved = true;
            report.ResolvedDate = DateTime.Now;
            report.ResolvedBy = User.Identity?.Name ?? "Admin";

            if (isApproved)
            {
                rating.IsVisible = false;
            }

            _context.Reports.Update(report);
            if (isApproved)
            {
                _context.Ratings.Update(rating);
            }
            await _context.SaveChangesAsync();

            string message = isApproved
                ? "Báo cáo đã được chấp thuận và đánh giá đã bị ẩn."
                : "Báo cáo đã được từ chối.";
            return Json(new { success = true, message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReply(int ratingId, string comment)
        {
            var ratingExists = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
            if (ratingExists == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để trả lời." });
            }

            string replyComment = string.IsNullOrEmpty(comment)
                ? "Bloomie Shop cảm ơn bạn đã tin tưởng và ủng hộ sản phẩm của chúng tôi! Chúng tôi rất vui khi nhận được đánh giá của bạn và hy vọng sẽ tiếp tục mang đến những sản phẩm chất lượng. Nếu có bất cứ vấn đề nào sai xót trong quá trình giao hàng, đừng ngần ngại liên hệ cho chúng tôi trên tất cả các nền tảng để được hỗ trợ. ❤️"
                : comment;

            var reply = new Reply
            {
                RatingId = ratingId,
                UserId = userId,
                Comment = replyComment,
                ReplyDate = DateTime.Now,
                IsVisible = true,
                LastModifiedBy = User.Identity?.Name ?? "Admin",
                LastModifiedDate = DateTime.Now
            };

            _context.Replies.Add(reply);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Phản hồi {(string.IsNullOrEmpty(comment) ? "mặc định" : "của bạn")} đã được gửi thành công." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRating(int ratingId)
        {
            var rating = await _context.Ratings
                .Include(r => r.Replies)
                .Include(r => r.Reports)
                .FirstOrDefaultAsync(r => r.Id == ratingId);

            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            _context.Replies.RemoveRange(rating.Replies);
            _context.Reports.RemoveRange(rating.Reports);
            _context.Ratings.Remove(rating);

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đánh giá đã được xóa thành công." });
        }

        [HttpGet]
        public async Task<IActionResult> GetReportsByRating(int ratingId)
        {
            var reports = await _context.Reports
                .Include(r => r.Reporter)
                .Where(r => r.RatingId == ratingId)
                .ToListAsync();

            if (reports == null || !reports.Any())
            {
                return Json(new { success = false, message = "Không tìm thấy báo cáo nào cho đánh giá này." });
            }

            var result = reports.Select(r => new
            {
                id = r.Id,
                reporter = new { fullName = r.Reporter?.FullName ?? "N/A" },
                reason = r.Reason,
                reportDate = r.ReportDate,
                isResolved = r.IsResolved
            });

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PinRating(int ratingId)
        {
            var rating = await _context.Ratings.FindAsync(ratingId);
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            rating.IsPinned = !rating.IsPinned;
            rating.LastModifiedBy = User.Identity?.Name ?? "Admin";
            rating.LastModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            return Json(new
            {
                success = true,
                isPinned = rating.IsPinned,
                message = $"Đánh giá đã được {(rating.IsPinned ? "ghim" : "bỏ ghim")}"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditReply(int replyId, string comment)
        {
            var reply = await _context.Replies.FindAsync(replyId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phản hồi." });
            }

            if (string.IsNullOrWhiteSpace(comment))
            {
                return Json(new { success = false, message = "Nội dung phản hồi không được để trống." });
            }

            reply.Comment = comment;
            reply.LastModifiedBy = User.Identity?.Name ?? "Admin";
            reply.LastModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Phản hồi đã được cập nhật." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReply(int replyId)
        {
            var reply = await _context.Replies.FindAsync(replyId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phản hồi." });
            }

            _context.Replies.Remove(reply);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Phản hồi đã được xóa." });
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel()
        {
            var ratings = await _context.Ratings
                .Include(r => r.User)
                .Include(r => r.Product)
                .Include(r => r.Replies)
                .OrderByDescending(r => r.ReviewDate)
                .ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("STT,Người đánh giá,Sản phẩm,Số sao,Nội dung,Ngày đánh giá,Trạng thái,Số phản hồi");

            int index = 1;
            foreach (var rating in ratings)
            {
                csv.AppendLine($"{index},{rating.User?.FullName},{rating.Product?.Name},{rating.Star},\"{rating.Comment}\",{rating.ReviewDate:dd/MM/yyyy HH:mm},{(rating.IsVisible ? "Hiển thị" : "Ẩn")},{rating.Replies?.Count ?? 0}");
                index++;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"DanhGia_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> SearchSuggestions(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return Json(new List<object>());
            }

            var suggestions = new List<object>();

            // Tìm sản phẩm
            var products = await _context.Products
                .Where(p => p.Name.Contains(query))
                .Take(5)
                .Select(p => new { type = "Sản phẩm", label = p.Name })
                .ToListAsync();
            suggestions.AddRange(products);

            // Tìm người dùng
            var users = await _context.Users
                .Where(u => u.FullName.Contains(query))
                .Take(5)
                .Select(u => new { type = "Người dùng", label = u.FullName })
                .ToListAsync();
            suggestions.AddRange(users);

            return Json(suggestions.Take(10));
        }

        [HttpGet]
        public async Task<IActionResult> Reports(int page = 1, int pageSize = 10, string? status = null, string? search = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.Reports
                .Include(r => r.Reporter)
                .Include(r => r.Rating)
                    .ThenInclude(rating => rating!.User)
                .Include(r => r.Rating)
                    .ThenInclude(rating => rating!.Product)
                .Include(r => r.Rating)
                    .ThenInclude(rating => rating!.Replies)
                .AsQueryable();

            // Lọc theo trạng thái
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "pending")
                    query = query.Where(r => !r.IsResolved);
                else if (status == "resolved")
                    query = query.Where(r => r.IsResolved);
            }

            // Tìm kiếm theo người báo cáo hoặc lý do
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(r => 
                    r.Reporter!.FullName.Contains(search) || 
                    r.Reason.Contains(search) ||
                    r.Rating!.Comment.Contains(search));
            }

            // Lọc theo thời gian
            if (fromDate.HasValue)
            {
                query = query.Where(r => r.ReportDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                var endDate = toDate.Value.AddDays(1);
                query = query.Where(r => r.ReportDate < endDate);
            }

            var totalItems = await query.CountAsync();
            var reports = await query
                .OrderByDescending(r => r.ReportDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Thống kê
            ViewBag.TotalReports = await _context.Reports.CountAsync();
            ViewBag.TotalPending = await _context.Reports.Where(r => !r.IsResolved).CountAsync();
            ViewBag.TotalResolved = await _context.Reports.Where(r => r.IsResolved).CountAsync();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.Status = status;
            ViewBag.Search = search;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            return View(reports);
        }
    }
}
