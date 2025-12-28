using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Security.Claims;

namespace Bloomie.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class StaffRatingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffRatingController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10)
        {
            var ratings = await _context.Ratings
                .Include(r => r.User)
                .Include(r => r.Product)
                .Include(r => r.Replies).ThenInclude(reply => reply.User)
                .Include(r => r.Reports).ThenInclude(report => report.Reporter)
                .OrderByDescending(r => r.ReviewDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = await _context.Ratings.CountAsync();

            return View(ratings);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleRatingVisibility(int ratingId)
        {
            var rating = await _context.Ratings.FindAsync(ratingId);
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            rating.IsVisible = !rating.IsVisible;
            rating.LastModifiedBy = User.Identity?.Name ?? "Staff";
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
        public async Task<IActionResult> ToggleReplyVisibility(int replyId)
        {
            var reply = await _context.Replies.FindAsync(replyId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phản hồi." });
            }

            reply.IsVisible = !reply.IsVisible;
            reply.LastModifiedBy = User.Identity?.Name ?? "Staff";
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
            report.ResolvedBy = User.Identity?.Name ?? "Staff";

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
                LastModifiedBy = User.Identity?.Name ?? "Staff",
                LastModifiedDate = DateTime.Now
            };

            _context.Replies.Add(reply);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Phản hồi {(string.IsNullOrEmpty(comment) ? "mặc định" : "của bạn")} đã được gửi thành công." });
        }

        [HttpPost]
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
    }
}