using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;

namespace Bloomie.Controllers
{
    [Authorize]
    public class PointShopController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public PointShopController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            // Get user points
            var userPoints = await _context.UserPoints
                .FirstOrDefaultAsync(up => up.UserId == user.Id);

            // Get available rewards
            var rewards = await _context.PointRewards
                .Where(r => r.IsActive)
                .Include(r => r.PromotionCode)
                .OrderBy(r => r.PointsCost)
                .ToListAsync();

            ViewBag.UserPoints = userPoints?.TotalPoints ?? 0;
            ViewBag.Rewards = rewards;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> RedeemReward(int rewardId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Json(new { success = false, message = "Vui lòng đăng nhập" });

            // Get reward
            var reward = await _context.PointRewards
                .Include(r => r.PromotionCode)
                .FirstOrDefaultAsync(r => r.Id == rewardId && r.IsActive);

            if (reward == null)
                return Json(new { success = false, message = "Phần quà không tồn tại" });

            // Check stock
            if (reward.Stock.HasValue && reward.Stock.Value <= 0)
                return Json(new { success = false, message = "Phần quà đã hết" });

            // Get user points
            var userPoints = await _context.UserPoints
                .FirstOrDefaultAsync(up => up.UserId == user.Id);

            if (userPoints == null || userPoints.TotalPoints < reward.PointsCost)
                return Json(new { success = false, message = "Bạn không đủ điểm để đổi quà này" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Create user voucher if reward is a voucher
                UserVoucher? userVoucher = null;
                if (reward.RewardType == RewardType.Voucher && reward.PromotionCodeId.HasValue)
                {
                    var validUntil = DateTime.Now.AddDays(reward.ValidDays);
                    userVoucher = new UserVoucher
                    {
                        UserId = user.Id,
                        PromotionCodeId = reward.PromotionCodeId.Value,
                        CollectedDate = DateTime.Now,
                        IsUsed = false,
                        ExpiryDate = validUntil,
                        Source = "PointShop"
                    };
                    _context.UserVouchers.Add(userVoucher);
                    await _context.SaveChangesAsync();
                }

                // Create redemption record
                var redemption = new PointRedemption
                {
                    UserId = user.Id,
                    User = user,
                    PointRewardId = reward.Id,
                    PointReward = reward,
                    PointsSpent = reward.PointsCost,
                    RedeemedDate = DateTime.Now,
                    UserVoucherId = userVoucher?.Id
                };
                _context.PointRedemptions.Add(redemption);

                // Deduct points
                userPoints.TotalPoints -= reward.PointsCost;
                userPoints.LastUpdated = DateTime.Now;

                // Decrease stock if applicable
                if (reward.Stock.HasValue)
                {
                    reward.Stock = reward.Stock.Value - 1;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = "Đổi thưởng thành công!",
                    remainingPoints = userPoints.TotalPoints,
                    voucherCode = reward.PromotionCode?.Code
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Có lỗi xảy ra, vui lòng thử lại" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> RedemptionHistory()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var history = await _context.PointRedemptions
                .Include(r => r.PointReward)
                .ThenInclude(r => r.PromotionCode)
                .Include(r => r.UserVoucher)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.RedeemedDate)
                .ToListAsync();

            return View(history);
        }
    }
}
