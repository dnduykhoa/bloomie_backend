using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Controllers
{
    [Authorize]
    public class PromotionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public PromotionController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Promotion/MyVouchers
        public async Task<IActionResult> MyVouchers(string tab = "available")
        {
            var userId = _userManager.GetUserId(User);
            var now = DateTime.Now;

            var query = _context.UserVouchers
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                        .ThenInclude(p => p!.PromotionGifts!)
                            .ThenInclude(pg => pg.BuyProducts!)
                                .ThenInclude(bp => bp.Product)
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                        .ThenInclude(p => p!.PromotionGifts!)
                            .ThenInclude(pg => pg.BuyCategories!)
                                .ThenInclude(bc => bc.Category)
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                        .ThenInclude(p => p!.PromotionGifts!)
                            .ThenInclude(pg => pg.GiftProducts!)
                                .ThenInclude(gp => gp.Product)
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                        .ThenInclude(p => p!.PromotionGifts!)
                            .ThenInclude(pg => pg.GiftCategories!)
                                .ThenInclude(gc => gc.Category)
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                        .ThenInclude(p => p!.PromotionProducts!)
                            .ThenInclude(pp => pp.Product)
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                        .ThenInclude(p => p!.PromotionCategories!)
                            .ThenInclude(pc => pc.Category)
                .Where(uv => uv.UserId == userId);

            // Filter based on tab
            switch (tab.ToLower())
            {
                case "available":
                    query = query.Where(uv => !uv.IsUsed && uv.ExpiryDate > now);
                    break;
                case "used":
                    query = query.Where(uv => uv.IsUsed);
                    break;
                case "expired":
                    query = query.Where(uv => !uv.IsUsed && uv.ExpiryDate <= now);
                    break;
                default:
                    query = query.Where(uv => !uv.IsUsed && uv.ExpiryDate > now);
                    break;
            }

            var vouchers = await query.OrderByDescending(uv => uv.CollectedDate).ToListAsync();

            ViewBag.CurrentTab = tab.ToLower();
            return View(vouchers);
        }

        // POST: Promotion/CollectVoucher (For future use - Lucky Wheel, Flash Sale, etc.)
        [HttpPost]
        public async Task<IActionResult> CollectVoucher(int promotionCodeId, string source)
        {
            var userId = _userManager.GetUserId(User);
            
            // Check if promotion code exists and is active
            var promotionCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .FirstOrDefaultAsync(pc => pc.Id == promotionCodeId);

            if (promotionCode == null)
            {
                return Json(new { success = false, message = "Mã khuyến mãi không tồn tại!" });
            }

            if (!promotionCode.IsActive)
            {
                return Json(new { success = false, message = "Mã khuyến mãi không còn hiệu lực!" });
            }

            // Check if user already has this voucher
            var existingVoucher = await _context.UserVouchers
                .FirstOrDefaultAsync(uv => uv.UserId == userId 
                    && uv.PromotionCodeId == promotionCodeId 
                    && !uv.IsUsed 
                    && uv.ExpiryDate > DateTime.Now);

            if (existingVoucher != null)
            {
                return Json(new { success = false, message = "Bạn đã có voucher này rồi!" });
            }

            // Create new user voucher
            var userVoucher = new UserVoucher
            {
                UserId = userId!,
                PromotionCodeId = promotionCodeId,
                Source = source,
                CollectedDate = DateTime.Now,
                ExpiryDate = promotionCode.Promotion?.EndDate ?? DateTime.Now.AddDays(30),
                IsUsed = false
            };

            _context.UserVouchers.Add(userVoucher);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Thu thập voucher thành công!" });
        }

        // GET: Promotion/VoucherCampaigns (Public voucher collection page)
        [AllowAnonymous]
        public async Task<IActionResult> VoucherCampaigns()
        {
            var now = DateTime.Now;
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;

            // Lấy các promotion codes công khai còn hiệu lực
            var activeCampaigns = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                    .ThenInclude(p => p!.PromotionGifts)
                .Include(pc => pc.Promotion)
                    .ThenInclude(p => p!.PromotionShippings)
                .Where(pc => pc.IsActive 
                    && pc.IsPublic  // Chỉ lấy mã được đánh dấu công khai
                    && (pc.ExpiryDate == null || pc.ExpiryDate > now) // Allow NULL expiry
                    && pc.Promotion != null
                    && pc.Promotion.IsActive
                    && pc.Promotion.StartDate <= now
                    && (pc.Promotion.EndDate == null || pc.Promotion.EndDate >= now)) // Allow NULL end date
                .OrderByDescending(pc => pc.Promotion!.StartDate)
                .ToListAsync();

            // Lấy Flash Sale campaigns đang active
            var activeFlashSaleCampaigns = await _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .Where(vc => vc.Type == CampaignType.FlashSale
                    && vc.IsActive
                    && vc.StartDate <= now
                    && vc.EndDate >= now)
                .ToListAsync();

            // Lấy Lucky Wheel campaigns đang active
            var activeLuckyWheelCampaigns = await _context.VoucherCampaigns
                .Where(vc => vc.Type == CampaignType.LuckyWheel
                    && vc.IsActive
                    && vc.StartDate <= now
                    && vc.EndDate >= now)
                .ToListAsync();

            ViewBag.FlashSaleCampaigns = activeFlashSaleCampaigns;
            ViewBag.LuckyWheelCampaigns = activeLuckyWheelCampaigns;
            ViewBag.HasSpecialPrograms = activeFlashSaleCampaigns.Any() || activeLuckyWheelCampaigns.Any();

            // Nếu user đã login, check xem họ đã có voucher nào chưa
            if (userId != null)
            {
                var userVoucherIds = await _context.UserVouchers
                    .Where(uv => uv.UserId == userId && !uv.IsUsed && uv.ExpiryDate > now)
                    .Select(uv => uv.PromotionCodeId)
                    .ToListAsync();
                
                ViewBag.UserVoucherIds = userVoucherIds;

                // Check Flash Sale đã collect
                var userFlashSaleVouchers = new Dictionary<int, int>();
                foreach (var campaign in activeFlashSaleCampaigns)
                {
                    var count = await _context.UserVouchers
                        .CountAsync(uv => uv.UserId == userId 
                            && uv.Source == "FlashSale" 
                            && uv.Note != null
                            && uv.Note.Contains($"CampaignId:{campaign.Id}"));
                    userFlashSaleVouchers[campaign.Id] = count;
                }
                ViewBag.UserFlashSaleVouchers = userFlashSaleVouchers;
            }
            else
            {
                ViewBag.UserVoucherIds = new List<int>();
                ViewBag.UserFlashSaleVouchers = new Dictionary<int, int>();
            }

            return View(activeCampaigns);
        }

        // POST: Promotion/CollectCampaignVoucher
        [HttpPost]
        public async Task<IActionResult> CollectCampaignVoucher(int promotionCodeId)
        {
            if (!User.Identity?.IsAuthenticated ?? false)
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập để thu thập voucher!" });
            }

            var userId = _userManager.GetUserId(User);
            var now = DateTime.Now;

            // Check promotion code
            var promotionCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .FirstOrDefaultAsync(pc => pc.Id == promotionCodeId);

            if (promotionCode == null || !promotionCode.IsActive)
            {
                return Json(new { success = false, message = "Voucher không tồn tại hoặc không còn hiệu lực!" });
            }

            if (promotionCode.ExpiryDate <= now)
            {
                return Json(new { success = false, message = "Voucher đã hết hạn!" });
            }

            // Check usage limit
            if (promotionCode.UsageLimit.HasValue)
            {
                var collectedCount = await _context.UserVouchers
                    .CountAsync(uv => uv.PromotionCodeId == promotionCodeId);

                if (collectedCount >= promotionCode.UsageLimit.Value)
                {
                    return Json(new { success = false, message = "Voucher đã hết số lượng!" });
                }
            }

            // Check if user already has this voucher (limit 1 per user for campaigns)
            var existingVoucher = await _context.UserVouchers
                .FirstOrDefaultAsync(uv => uv.UserId == userId 
                    && uv.PromotionCodeId == promotionCodeId);

            if (existingVoucher != null)
            {
                return Json(new { success = false, message = "Bạn đã thu thập voucher này rồi!" });
            }

            // Create user voucher
            var userVoucher = new UserVoucher
            {
                UserId = userId!,
                PromotionCodeId = promotionCodeId,
                Source = "Campaign",
                CollectedDate = now,
                ExpiryDate = promotionCode.ExpiryDate ?? now.AddMonths(1),
                IsUsed = false,
                Note = $"Thu thập từ chương trình: {promotionCode.Promotion?.Name ?? promotionCode.Code}"
            };

            _context.UserVouchers.Add(userVoucher);
            
            // Increment used count
            promotionCode.UsedCount = promotionCode.UsedCount + 1;
            
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Thu thập voucher thành công! Kiểm tra trong Ví Voucher của bạn." });
        }

        // ============ FLASH SALE ============
        // GET: Promotion/FlashSale
        [AllowAnonymous]
        public async Task<IActionResult> FlashSale()
        {
            var now = DateTime.Now;
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;

            var activeFlashSales = await _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .Where(vc => vc.Type == CampaignType.FlashSale
                    && vc.IsActive
                    && vc.StartDate <= now
                    && vc.EndDate >= now)
                .OrderBy(vc => vc.StartDate)
                .ToListAsync();

            // Nếu user đã login, check xem đã thu thập voucher nào chưa
            if (userId != null)
            {
                var userVoucherCampaignIds = await _context.UserVouchers
                    .Where(uv => uv.UserId == userId && uv.Source == "FlashSale")
                    .Select(uv => uv.Note) // Note chứa campaignId
                    .ToListAsync();
                
                ViewBag.UserCollectedCampaigns = userVoucherCampaignIds;
            }
            else
            {
                ViewBag.UserCollectedCampaigns = new List<string>();
            }

            return View(activeFlashSales);
        }

        // POST: Promotion/CollectFlashSale
        [HttpPost]
        public async Task<IActionResult> CollectFlashSale(int campaignId)
        {
            if (!User.Identity?.IsAuthenticated ?? false)
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập để thu thập voucher!" });
            }

            var userId = _userManager.GetUserId(User);
            var now = DateTime.Now;

            var campaign = await _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                .FirstOrDefaultAsync(vc => vc.Id == campaignId && vc.Type == CampaignType.FlashSale);

            if (campaign == null || !campaign.IsActive)
            {
                return Json(new { success = false, message = "Flash Sale không tồn tại hoặc đã kết thúc!" });
            }

            if (campaign.StartDate > now || campaign.EndDate < now)
            {
                return Json(new { success = false, message = "Flash Sale chưa bắt đầu hoặc đã kết thúc!" });
            }

            // Parse config
            var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(campaign.Config ?? "{}");
            var totalVouchers = config!.ContainsKey("TotalVouchers") ? config["TotalVouchers"] : 100;
            var maxPerUser = config.ContainsKey("MaxVouchersPerUser") ? config["MaxVouchersPerUser"] : 1;

            // Check total limit
            if (campaign.CollectedCount >= totalVouchers)
            {
                return Json(new { success = false, message = "Flash Sale đã hết voucher!" });
            }

            // Check user limit
            var userCollectedCount = await _context.UserVouchers
                .CountAsync(uv => uv.UserId == userId 
                    && uv.Source == "FlashSale" 
                    && uv.Note == $"CampaignId:{campaignId}");

            if (userCollectedCount >= maxPerUser)
            {
                return Json(new { success = false, message = $"Bạn đã thu thập tối đa {maxPerUser} voucher từ Flash Sale này!" });
            }

            // Create UserVoucher
            var userVoucher = new UserVoucher
            {
                UserId = userId!,
                PromotionCodeId = campaign.PromotionCodeId,
                Source = "FlashSale",
                CollectedDate = now,
                ExpiryDate = campaign.PromotionCode?.ExpiryDate ?? now.AddMonths(1),
                IsUsed = false,
                Note = $"CampaignId:{campaignId}"
            };

            _context.UserVouchers.Add(userVoucher);
            campaign.CollectedCount++;
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = "Thu thập voucher Flash Sale thành công!",
                remaining = totalVouchers - campaign.CollectedCount
            });
        }

        // ============ LUCKY WHEEL ============
        // GET: Promotion/LuckyWheel
        [AllowAnonymous]
        public async Task<IActionResult> LuckyWheel()
        {
            var now = DateTime.Now;
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;

            var activeLuckyWheels = await _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .Where(vc => vc.Type == CampaignType.LuckyWheel
                    && vc.IsActive
                    && vc.StartDate <= now
                    && vc.EndDate >= now)
                .OrderBy(vc => vc.StartDate)
                .ToListAsync();

            // Lấy danh sách voucher cho mỗi campaign
            var campaignVouchers = new Dictionary<int, List<object>>();
            foreach (var campaign in activeLuckyWheels)
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(campaign.Config ?? "{}");
                if (config.TryGetProperty("VoucherRates", out var voucherRatesElement) && voucherRatesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var vouchers = new List<object>();
                    foreach (var vr in voucherRatesElement.EnumerateArray())
                    {
                        // Kiểm tra xem có phải ô "Chúc may mắn" không
                        if (vr.TryGetProperty("IsLucky", out var isLuckyElement) && isLuckyElement.GetBoolean())
                        {
                            vouchers.Add(new { 
                                id = 0,
                                text = "🍀 Chúc May Mắn",
                                code = "LUCKY",
                                isLucky = true
                            });
                        }
                        else if (vr.TryGetProperty("Id", out var idElement))
                        {
                            var voucherId = idElement.GetInt32();
                            var promoCode = await _context.PromotionCodes
                                .Include(pc => pc.Promotion)
                                .FirstOrDefaultAsync(pc => pc.Id == voucherId);
                            
                            if (promoCode != null)
                            {
                                // Tạo text hiển thị cho voucher
                                string displayText = "";
                                if (promoCode.Value.HasValue)
                                {
                                    if (promoCode.IsPercent)
                                    {
                                        displayText = $"{promoCode.Value}%";
                                    }
                                    else
                                    {
                                        displayText = $"{promoCode.Value:N0}đ";
                                    }
                                }
                                else
                                {
                                    displayText = "LUCKY";
                                }

                                vouchers.Add(new { 
                                    id = promoCode.Id, 
                                    text = displayText,
                                    code = promoCode.Code,
                                    isLucky = false
                                });
                            }
                        }
                    }
                    campaignVouchers[campaign.Id] = vouchers;
                }
            }
            ViewBag.CampaignVouchers = campaignVouchers;

            // Check user spins
            if (userId != null)
            {
                var userSpinsData = new Dictionary<int, int>();
                foreach (var campaign in activeLuckyWheels)
                {
                    // Lấy số lượt quay tối đa từ config
                    var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(campaign.Config ?? "{}");
                    var maxSpins = config.TryGetProperty("SpinsPerUser", out var spinsElement) ? spinsElement.GetInt32() : 3;
                    
                    // Đếm số lượt đã quay
                    var spinsUsed = await _context.UserVouchers
                        .CountAsync(uv => uv.UserId == userId 
                            && uv.Source == "LuckyWheel" 
                            && uv.Note != null
                            && uv.Note.Contains($"CampaignId:{campaign.Id}"));
                    
                    // Tính số lượt còn lại
                    userSpinsData[campaign.Id] = maxSpins - spinsUsed;
                }
                ViewBag.UserSpins = userSpinsData;
            }
            else
            {
                ViewBag.UserSpins = new Dictionary<int, int>();
            }

            return View(activeLuckyWheels);
        }

        // POST: Promotion/SpinLuckyWheel
        [HttpPost]
        public async Task<IActionResult> SpinLuckyWheel(int campaignId)
        {
            if (!User.Identity?.IsAuthenticated ?? false)
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập để quay!" });
            }

            var userId = _userManager.GetUserId(User);
            var now = DateTime.Now;

            var campaign = await _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                .FirstOrDefaultAsync(vc => vc.Id == campaignId && vc.Type == CampaignType.LuckyWheel);

            if (campaign == null || !campaign.IsActive)
            {
                return Json(new { success = false, message = "Vòng quay không tồn tại!" });
            }

            if (campaign.StartDate > now || campaign.EndDate < now)
            {
                return Json(new { success = false, message = "Vòng quay chưa bắt đầu hoặc đã kết thúc!" });
            }

            // Parse config
            var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(campaign.Config ?? "{}");
            var spinsPerUser = config.TryGetProperty("SpinsPerUser", out var spinsElement) ? spinsElement.GetInt32() : 3;

            // Check user spins (bao gồm cả ô "Chúc may mắn")
            var userSpinsUsed = await _context.UserVouchers
                .CountAsync(uv => uv.UserId == userId 
                    && uv.Source == "LuckyWheel" 
                    && uv.Note != null
                    && uv.Note.Contains($"CampaignId:{campaignId}"));

            if (userSpinsUsed >= spinsPerUser)
            {
                return Json(new { success = false, message = $"Bạn đã hết lượt quay! (Tối đa {spinsPerUser} lượt)" });
            }

            // Random voucher based on rates
            System.Text.Json.JsonElement voucherRatesElement = default;
            if (!config.TryGetProperty("VoucherRates", out voucherRatesElement) || voucherRatesElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return Json(new { success = false, message = "Cấu hình vòng quay không hợp lệ!" });
            }

            var random = new Random().NextDouble();
            double cumulative = 0;
            int wonVoucherId = 0;
            int wonVoucherIndex = 0;
            bool isLuckySlot = false; // Đánh dấu có trúng ô "Chúc may mắn" không

            int index = 0;
            foreach (var vr in voucherRatesElement.EnumerateArray())
            {
                if (vr.TryGetProperty("Rate", out var rateElement))
                {
                    var rate = rateElement.GetDouble();
                    cumulative += rate;
                    if (random <= cumulative && wonVoucherId == 0 && !isLuckySlot)
                    {
                        // Kiểm tra xem có phải ô "Chúc may mắn" không
                        if (vr.TryGetProperty("IsLucky", out var isLuckyElement) && isLuckyElement.GetBoolean())
                        {
                            isLuckySlot = true;
                            wonVoucherIndex = index;
                            break;
                        }
                        else if (vr.TryGetProperty("Id", out var idElement))
                        {
                            wonVoucherId = idElement.GetInt32();
                            wonVoucherIndex = index;
                            break;
                        }
                    }
                }
                index++;
            }

            // Nếu trúng ô "Chúc may mắn"
            if (isLuckySlot)
            {
                // Không tặng voucher, chỉ tăng số lượt đã quay
                var luckySpinRecord = new UserVoucher
                {
                    UserId = userId!,
                    PromotionCodeId = 0, // 0 để đánh dấu không có voucher
                    Source = "LuckyWheel",
                    CollectedDate = now,
                    ExpiryDate = now,
                    IsUsed = true, // Đánh dấu đã "sử dụng" để đếm lượt
                    Note = $"CampaignId:{campaignId}|NoWin"
                };
                
                _context.UserVouchers.Add(luckySpinRecord);
                await _context.SaveChangesAsync();
                
                return Json(new { 
                    success = true, 
                    isLucky = true,
                    message = "Chúc bạn may mắn lần sau! 🍀",
                    voucherCode = "🍀 Chúc May Mắn Lần Sau",
                    voucherDiscount = "Đừng nản! Hãy thử lại nhé 💪",
                    spinsRemaining = spinsPerUser - userSpinsUsed - 1,
                    wonIndex = wonVoucherIndex
                });
            }

            // Fallback to last voucher if no winner
            if (wonVoucherId == 0 && voucherRatesElement.GetArrayLength() > 0)
            {
                var lastItem = voucherRatesElement[voucherRatesElement.GetArrayLength() - 1];
                if (lastItem.TryGetProperty("Id", out var lastIdElement))
                {
                    wonVoucherId = lastIdElement.GetInt32();
                    wonVoucherIndex = voucherRatesElement.GetArrayLength() - 1;
                }
            }

            // Get voucher info
            var wonPromoCode = await _context.PromotionCodes.FindAsync(wonVoucherId);
            if (wonPromoCode == null)
            {
                return Json(new { success = false, message = "Voucher không tồn tại!" });
            }

            // Create UserVoucher
            var userVoucher = new UserVoucher
            {
                UserId = userId!,
                PromotionCodeId = wonVoucherId,
                Source = "LuckyWheel",
                CollectedDate = now,
                ExpiryDate = wonPromoCode.ExpiryDate ?? now.AddMonths(1),
                IsUsed = false,
                Note = $"CampaignId:{campaignId}"
            };

            _context.UserVouchers.Add(userVoucher);
            campaign.CollectedCount++;
            await _context.SaveChangesAsync();

            // Reload để verify đã lưu
            var savedVoucher = await _context.UserVouchers
                .Include(uv => uv.PromotionCode)
                .FirstOrDefaultAsync(uv => uv.UserId == userId 
                    && uv.PromotionCodeId == wonVoucherId 
                    && uv.Source == "LuckyWheel"
                    && uv.Note == $"CampaignId:{campaignId}");

            var discountText = wonPromoCode.IsPercent 
                ? $"Giảm {wonPromoCode.Value}%" 
                : $"Giảm {(wonPromoCode.Value ?? 0):N0}đ";

            return Json(new { 
                success = true, 
                message = $"Chúc mừng! Bạn nhận được voucher {wonPromoCode.Code}!",
                voucherCode = wonPromoCode.Code,
                voucherDiscount = discountText,
                spinsRemaining = spinsPerUser - userSpinsUsed - 1,
                wonIndex = wonVoucherIndex, // Trả về index để frontend biết ô nào trúng
                debug = new {
                    voucherId = savedVoucher?.Id,
                    expiryDate = savedVoucher?.ExpiryDate.ToString("dd/MM/yyyy HH:mm"),
                    isUsed = savedVoucher?.IsUsed,
                    promoCodeIsActive = wonPromoCode.IsActive
                }
            });
        }
    }
}
