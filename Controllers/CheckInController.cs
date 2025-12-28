using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;

namespace Bloomie.Controllers
{
    [Authorize]
    public class CheckInController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public CheckInController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

                // Get or create user points record
                var userPoints = await _context.UserPoints
                    .FirstOrDefaultAsync(up => up.UserId == user.Id);

            if (userPoints == null)
            {
                userPoints = new UserPoints
                {
                    UserId = user.Id,
                    User = user,
                    TotalPoints = 0,
                    LifetimePoints = 0,
                    ConsecutiveCheckIns = 0,
                    LastCheckInDate = null,
                    LastUpdated = DateTime.Now
                };
                _context.UserPoints.Add(userPoints);
                await _context.SaveChangesAsync();
            }

            // Get check-in history for current month
            var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var checkInHistory = await _context.UserCheckIns
                .Where(c => c.UserId == user.Id && c.CheckInDate >= startOfMonth)
                .OrderByDescending(c => c.CheckInDate)
                .ToListAsync();

            ViewBag.UserPoints = userPoints;
            ViewBag.CheckInHistory = checkInHistory;
            ViewBag.CanCheckInToday = await CanCheckInToday(user.Id);

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> DailyCheckIn()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Json(new { success = false, message = "Vui lòng đăng nhập" });

            // Check if already checked in today
            if (!await CanCheckInToday(user.Id))
            {
                return Json(new { success = false, message = "Bạn đã điểm danh hôm nay rồi!" });
            }

            // Get or create user points
            var userPoints = await _context.UserPoints
                .FirstOrDefaultAsync(up => up.UserId == user.Id);

            if (userPoints == null)
            {
                userPoints = new UserPoints
                {
                    UserId = user.Id,
                    User = user,
                    TotalPoints = 0,
                    LifetimePoints = 0,
                    ConsecutiveCheckIns = 0,
                    LastCheckInDate = null,
                    LastUpdated = DateTime.Now
                };
                _context.UserPoints.Add(userPoints);
            }

            // Calculate consecutive days
            int consecutiveDays = 1;
            if (userPoints.LastCheckInDate.HasValue)
            {
                var daysSinceLastCheckIn = (DateTime.Today - userPoints.LastCheckInDate.Value.Date).Days;
                
                if (daysSinceLastCheckIn == 1)
                {
                    // Consecutive day
                    consecutiveDays = userPoints.ConsecutiveCheckIns + 1;
                    if (consecutiveDays > 7)
                        consecutiveDays = 1; // Reset after 7 days
                }
                else
                {
                    // Streak broken, reset
                    consecutiveDays = 1;
                }
            }

            // Calculate points based on consecutive days
            int pointsEarned = consecutiveDays switch
            {
                1 => 5,
                2 => 10,
                3 => 15,
                4 => 20,
                5 => 25,
                6 => 30,
                7 => 50,
                _ => 5
            };

            // Create check-in record
            var checkIn = new UserCheckIn
            {
                UserId = user.Id,
                User = user,
                CheckInDate = DateTime.Today,
                PointsEarned = pointsEarned,
                ConsecutiveDays = consecutiveDays,
                CreatedDate = DateTime.Now
            };
            _context.UserCheckIns.Add(checkIn);

            // Update user points
            userPoints.TotalPoints += pointsEarned;
            userPoints.LifetimePoints += pointsEarned;
            userPoints.LastCheckInDate = DateTime.Today;
            userPoints.ConsecutiveCheckIns = consecutiveDays;
            userPoints.LastUpdated = DateTime.Now;

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Điểm danh thành công! Bạn nhận được {pointsEarned} điểm",
                pointsEarned = pointsEarned,
                totalPoints = userPoints.TotalPoints,
                consecutiveDays = consecutiveDays
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetCheckInStatus()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Json(new { success = false });

            var userPoints = await _context.UserPoints
                .FirstOrDefaultAsync(up => up.UserId == user.Id);

            var canCheckIn = await CanCheckInToday(user.Id);

            return Json(new
            {
                success = true,
                canCheckIn = canCheckIn,
                totalPoints = userPoints?.TotalPoints ?? 0,
                consecutiveDays = userPoints?.ConsecutiveCheckIns ?? 0,
                lastCheckInDate = userPoints?.LastCheckInDate?.ToString("dd/MM/yyyy")
            });
        }

        private async Task<bool> CanCheckInToday(string userId)
        {
            var today = DateTime.Today;
            var checkInToday = await _context.UserCheckIns
                .AnyAsync(c => c.UserId == userId && c.CheckInDate.Date == today);
            
            return !checkInToday;
        }
    }
}
