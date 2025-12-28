using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminPromotionController : Controller
    {
        private readonly ApplicationDbContext _context;
        public AdminPromotionController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admin/AdminPromotion
        public async Task<IActionResult> Index(string? searchString, string? type, string? status, bool? isPublic)
        {
            var query = _context.Promotions
                .Include(p => p.PromotionCodes)
                .AsQueryable();
            
            // Apply filters
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => 
                    p.Name.Contains(searchString) || 
                    p.PromotionCodes.Any(pc => pc.Code.Contains(searchString))
                );
            }
            
            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(p => p.Type.ToString() == type);
            }
            
            var now = DateTime.Now;
            
            if (!string.IsNullOrEmpty(status))
            {
                switch (status.ToLower())
                {
                    case "active":
                        query = query.Where(p => 
                            p.IsActive && 
                            p.StartDate <= now && 
                            (!p.EndDate.HasValue || p.EndDate.Value >= now)
                        );
                        break;
                    case "upcoming":
                        query = query.Where(p => p.StartDate > now);
                        break;
                    case "expired":
                        query = query.Where(p => p.EndDate.HasValue && p.EndDate.Value < now);
                        break;
                    case "inactive":
                        query = query.Where(p => !p.IsActive);
                        break;
                }
            }
            
            if (isPublic.HasValue)
            {
                query = query.Where(p => p.PromotionCodes.Any(pc => pc.IsPublic == isPublic.Value));
            }
            
            var promotions = await query
                .OrderByDescending(p => p.Id)
                .ToListAsync();
            
            // Calculate statistics (all data, not filtered)
            var allPromotions = await _context.Promotions.ToListAsync();
            ViewBag.TotalPromotions = allPromotions.Count;
            ViewBag.ActivePromotions = allPromotions.Count(p => 
                p.IsActive && 
                p.StartDate <= now && 
                (!p.EndDate.HasValue || p.EndDate.Value >= now)
            );
            ViewBag.UpcomingPromotions = allPromotions.Count(p => 
                p.StartDate > now
            );
            ViewBag.ExpiredPromotions = allPromotions.Count(p => 
                p.EndDate.HasValue && p.EndDate.Value < now
            );
            
            // Pass filter values back to view
            ViewBag.SearchString = searchString;
            ViewBag.Type = type;
            ViewBag.Status = status;
            ViewBag.IsPublic = isPublic;
            
            return View(promotions);
        }

        // GET: Admin/AdminPromotion/ManageCodes
        public async Task<IActionResult> ManageCodes()
        {
            var codes = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .OrderByDescending(pc => pc.Id)
                .ToListAsync();
            return View(codes);
        }

        // ============ MANAGE CAMPAIGNS ============
        // GET: Admin/AdminPromotion/ManageCampaigns
        public async Task<IActionResult> ManageCampaigns(string? searchString, string? typeFilter, string? statusFilter, string? subTypeFilter)
        {
            var now = DateTime.Now;
            var unifiedPromotions = new List<UnifiedPromotionVM>();

            // 1. Get Campaigns (Flash Sale, Lucky Wheel)
            var campaignsQuery = _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .AsQueryable();

            // Apply search filter for campaigns
            if (!string.IsNullOrEmpty(searchString))
            {
                campaignsQuery = campaignsQuery.Where(c => c.Name.Contains(searchString) || c.Description.Contains(searchString));
            }

            var campaigns = await campaignsQuery
                .OrderByDescending(vc => vc.CreatedDate)
                .ToListAsync();

            foreach (var campaign in campaigns)
            {
                var isRunning = campaign.IsActive && campaign.StartDate <= now && campaign.EndDate >= now;
                var isNotStarted = campaign.IsActive && campaign.StartDate > now;
                var isEnded = campaign.EndDate < now;
                var isPaused = !campaign.IsActive;

                string status, statusClass;
                if (isPaused)
                {
                    status = "Tạm dừng";
                    statusClass = "secondary";
                }
                else if (isEnded)
                {
                    status = "Đã kết thúc";
                    statusClass = "danger";
                }
                else if (isNotStarted)
                {
                    status = "Chưa bắt đầu";
                    statusClass = "warning";
                }
                else
                {
                    status = "Đang hoạt động";
                    statusClass = "success";
                }

                unifiedPromotions.Add(new UnifiedPromotionVM
                {
                    Type = "Campaign",
                    Id = campaign.Id,
                    Name = campaign.Name,
                    Description = campaign.Description,
                    SubType = campaign.Type == CampaignType.FlashSale ? "FlashSale" : "LuckyWheel",
                    Value = campaign.Type == CampaignType.FlashSale ? "Flash Sale" : "Vòng Quay",
                    AppliesTo = "-",
                    TimeRange = $"{campaign.StartDate:dd/MM/yyyy HH:mm} - {campaign.EndDate:dd/MM/yyyy HH:mm}",
                    StartDate = campaign.StartDate,
                    EndDate = campaign.EndDate,
                    IsActive = campaign.IsActive,
                    Status = status,
                    StatusClass = statusClass,
                    Stats = $"{campaign.CollectedCount} voucher đã phát",
                    BadgeClass = campaign.Type == CampaignType.FlashSale ? "bg-danger" : "bg-warning text-dark",
                    Icon = campaign.Type == CampaignType.FlashSale ? "fa-bolt" : "fa-dharmachakra",
                    OriginalEntity = campaign
                });
            }

            // 2. Get Product Discounts
            var productDiscounts = await _context.ProductDiscounts
                .OrderByDescending(pd => pd.CreatedDate)
                .ToListAsync();

            foreach (var pd in productDiscounts)
            {
                var isRunning = pd.IsActive && pd.StartDate <= now && (!pd.EndDate.HasValue || pd.EndDate >= now);
                var isNotStarted = pd.IsActive && pd.StartDate > now;
                var isEnded = pd.EndDate.HasValue && pd.EndDate < now;
                var isPaused = !pd.IsActive;

                string status, statusClass;
                if (isPaused)
                {
                    status = "Tạm dừng";
                    statusClass = "secondary";
                }
                else if (isEnded)
                {
                    status = "Đã kết thúc";
                    statusClass = "danger";
                }
                else if (isNotStarted)
                {
                    status = "Chưa bắt đầu";
                    statusClass = "warning";
                }
                else
                {
                    status = "Đang hoạt động";
                    statusClass = "success";
                }

                var value = pd.DiscountType == "percent"
                    ? $"{pd.DiscountValue}%" + (pd.MaxDiscount.HasValue ? $" (max {pd.MaxDiscount:N0}đ)" : "")
                    : $"{pd.DiscountValue:N0}đ";

                var appliesTo = pd.ApplyTo == "all" ? "Tất cả sản phẩm"
                    : pd.ApplyTo == "products" ? $"{(string.IsNullOrEmpty(pd.ProductIds) ? 0 : System.Text.Json.JsonSerializer.Deserialize<List<int>>(pd.ProductIds)?.Count ?? 0)} sản phẩm"
                    : $"{(string.IsNullOrEmpty(pd.CategoryIds) ? 0 : System.Text.Json.JsonSerializer.Deserialize<List<int>>(pd.CategoryIds)?.Count ?? 0)} danh mục";

                unifiedPromotions.Add(new UnifiedPromotionVM
                {
                    Type = "ProductDiscount",
                    Id = pd.Id,
                    Name = pd.Name,
                    Description = pd.Description,
                    SubType = pd.DiscountType == "percent" ? "Percent" : "Fixed",
                    Value = value,
                    AppliesTo = appliesTo,
                    TimeRange = pd.EndDate.HasValue 
                        ? $"{pd.StartDate:dd/MM/yyyy} - {pd.EndDate:dd/MM/yyyy}"
                        : $"Từ {pd.StartDate:dd/MM/yyyy}",
                    StartDate = pd.StartDate,
                    EndDate = pd.EndDate,
                    IsActive = pd.IsActive,
                    Status = status,
                    StatusClass = statusClass,
                    Stats = $"{pd.ViewCount} lượt xem | {pd.PurchaseCount} đã mua",
                    BadgeClass = "bg-primary",
                    Icon = "fa-tags",
                    OriginalEntity = pd
                });
            }

            // 3. Get Point Rewards
            var pointRewards = await _context.PointRewards
                .Include(pr => pr.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .OrderByDescending(pr => pr.CreatedDate)
                .ToListAsync();

            foreach (var pr in pointRewards)
            {
                var isAvailable = pr.IsActive && (pr.Stock == null || pr.Stock > 0);

                string status = pr.IsActive
                    ? (pr.Stock == null || pr.Stock > 0 ? "Đang hoạt động" : "Hết hàng")
                    : "Tạm dừng";
                string statusClass = pr.IsActive
                    ? (pr.Stock == null || pr.Stock > 0 ? "success" : "warning")
                    : "secondary";

                // Count redeemed from PointRedemptions table
                var redeemedCount = await _context.PointRedemptions
                    .Where(up => up.PointRewardId == pr.Id)
                    .CountAsync();

                var stats = pr.Stock.HasValue
                    ? $"{redeemedCount} đã đổi | Còn lại: {pr.Stock - redeemedCount}"
                    : $"{redeemedCount} đã đổi | Không giới hạn";

                unifiedPromotions.Add(new UnifiedPromotionVM
                {
                    Type = "PointReward",
                    Id = pr.Id,
                    Name = pr.Name,
                    Description = pr.Description,
                    SubType = "PointShop",
                    Value = $"{pr.PointsCost} điểm",
                    AppliesTo = "-",
                    TimeRange = $"Từ {pr.CreatedDate:dd/MM/yyyy}",
                    StartDate = null,
                    EndDate = null,
                    IsActive = pr.IsActive,
                    Status = status,
                    StatusClass = statusClass,
                    Stats = stats,
                    BadgeClass = "bg-success",
                    Icon = "fa-gift",
                    OriginalEntity = pr
                });
            }

            // Apply filters on unified list
            var filteredPromotions = unifiedPromotions.AsEnumerable();

            if (!string.IsNullOrEmpty(typeFilter))
            {
                if (typeFilter == "FlashSale" || typeFilter == "LuckyWheel")
                {
                    // Filter by SubType for campaigns
                    filteredPromotions = filteredPromotions.Where(p => p.SubType == typeFilter);
                }
                else
                {
                    // Filter by Type for other promotions
                    filteredPromotions = filteredPromotions.Where(p => p.Type == typeFilter);
                }
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                switch (statusFilter.ToLower())
                {
                    case "active":
                        filteredPromotions = filteredPromotions.Where(p => p.Status == "Đang hoạt động");
                        break;
                    case "upcoming":
                        filteredPromotions = filteredPromotions.Where(p => p.Status == "Chưa bắt đầu");
                        break;
                    case "ended":
                        filteredPromotions = filteredPromotions.Where(p => p.Status == "Đã kết thúc");
                        break;
                    case "paused":
                        filteredPromotions = filteredPromotions.Where(p => p.Status == "Tạm dừng");
                        break;
                }
            }

            if (!string.IsNullOrEmpty(subTypeFilter))
            {
                filteredPromotions = filteredPromotions.Where(p => p.SubType == subTypeFilter);
            }

            var viewModel = new ManageCampaignsVM
            {
                Promotions = filteredPromotions.OrderByDescending(p => p.StartDate ?? DateTime.MinValue).ToList(),
                TotalCampaigns = campaigns.Count,
                TotalProductDiscounts = productDiscounts.Count,
                TotalPointRewards = pointRewards.Count,
                ActiveCount = unifiedPromotions.Count(p => p.StatusClass == "success")
            };

            // Pass filter values to view
            ViewBag.SearchString = searchString;
            ViewBag.TypeFilter = typeFilter;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.SubTypeFilter = subTypeFilter;

            return View(viewModel);
        }

        // POST: Admin/AdminPromotion/ToggleCampaignActive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCampaignActive(int id)
        {
            var campaign = await _context.VoucherCampaigns.FindAsync(id);
            if (campaign == null)
            {
                return NotFound();
            }

            campaign.IsActive = !campaign.IsActive;
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã {(campaign.IsActive ? "kích hoạt" : "tạm dừng")} campaign '{campaign.Name}'!";
            return RedirectToAction(nameof(ManageCampaigns));
        }

        // POST: Admin/AdminPromotion/DeleteCampaign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCampaign(int id)
        {
            var campaign = await _context.VoucherCampaigns.FindAsync(id);
            if (campaign == null)
            {
                return NotFound();
            }

            _context.VoucherCampaigns.Remove(campaign);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã xóa campaign '{campaign.Name}'!";
            return RedirectToAction(nameof(ManageCampaigns));
        }

        // GET: Admin/AdminPromotion/EditCampaign
        public async Task<IActionResult> EditCampaign(int id)
        {
            var campaign = await _context.VoucherCampaigns
                .Include(vc => vc.PromotionCode)
                .FirstOrDefaultAsync(vc => vc.Id == id);

            if (campaign == null)
            {
                return NotFound();
            }

            ViewBag.PromotionCodes = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                .ToListAsync();

            return View(campaign);
        }

        // POST: Admin/AdminPromotion/EditCampaign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCampaign(
            int id,
            string name,
            string? description,
            DateTime startDate,
            DateTime endDate,
            string? bannerImage,
            int? spinsPerUser,
            string? voucherRatesJson)
        {
            var campaign = await _context.VoucherCampaigns.FindAsync(id);
            if (campaign == null)
            {
                return NotFound();
            }

            campaign.Name = name;
            campaign.Description = description;
            campaign.StartDate = startDate;
            campaign.EndDate = endDate;
            campaign.BannerImage = bannerImage;

            // Cập nhật Config nếu là Lucky Wheel
            if (campaign.Type == CampaignType.LuckyWheel && !string.IsNullOrEmpty(voucherRatesJson))
            {
                var voucherRates = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(voucherRatesJson);
                campaign.Config = System.Text.Json.JsonSerializer.Serialize(new
                {
                    SpinsPerUser = spinsPerUser ?? 3,
                    VoucherRates = voucherRates
                });
            }

            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã cập nhật campaign '{campaign.Name}'!";
            return RedirectToAction(nameof(ManageCampaigns));
        }

        // GET: Admin/AdminPromotion/CreateTypeSelect
        public IActionResult CreateTypeSelect()
        {
            return View();
        }

        // GET: Admin/AdminPromotion/CreateOrderDiscount
        public IActionResult CreateOrderDiscount()
        {
            return View();
        }

        // POST: Admin/AdminPromotion/CreateOrderDiscount
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrderDiscount(Bloomie.Models.ViewModels.PromotionOrderDiscountVM model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Xác định giá trị điều kiện chính để lưu vào ConditionType/ConditionValue
            decimal? conditionValue = null;
            if (model.ConditionType == "MinOrderValue") conditionValue = model.MinOrderValue;
            else if (model.ConditionType == "MinProductQuantity") conditionValue = model.MinProductQuantity;

            var promotion = new Promotion
            {
                Name = model.Code,
                Type = Bloomie.Models.Entities.PromotionType.Order,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                IsActive = true,
                Description = $"Mã giảm giá đơn hàng: {model.Code}",
                AllowCombineOrder = model.AllowCombineOrder,
                AllowCombineProduct = model.AllowCombineProduct,
                AllowCombineShipping = model.AllowCombineShipping,
                ConditionType = model.ConditionType,
                ConditionValue = conditionValue,
                MinOrderValue = model.MinOrderValue,
                MinProductQuantity = model.MinProductQuantity,
            };
            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();

            // Tạo PromotionCode
            var promoCode = new PromotionCode
            {
                Code = model.Code,
                PromotionId = promotion.Id,
                Value = model.Value,
                IsPercent = model.IsPercent,
                MaxDiscount = model.MaxDiscount,
                ExpiryDate = model.EndDate,
                IsActive = true,
                UsageLimit = model.UsageLimit,
                LimitPerCustomer = model.LimitPerCustomer,
                MinOrderValue = model.ConditionType == "MinOrderValue" && model.ConditionValue.HasValue ? (int?)model.ConditionValue.Value : null,
                UsedCount = 0,
                IsPublic = model.IsPublic  // Lấy từ model
            };
            _context.PromotionCodes.Add(promoCode);
            await _context.SaveChangesAsync();

            TempData["success"] = "Tạo mã khuyến mãi thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/CreateProductDiscount
        public IActionResult CreateProductDiscount()
        {
            var products = _context.Products.Where(p => p.IsActive).ToList();
            var categories = _context.Categories.ToList();
            ViewBag.Products = products;
            ViewBag.Categories = categories;
            return View();
        }

        // POST: Admin/AdminPromotion/CreateProductDiscount
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProductDiscount(Bloomie.Models.ViewModels.PromotionProductDiscountVM model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                ViewBag.Categories = _context.Categories.ToList();
                return View(model);
            }

            // Lưu tất cả các giá trị điều kiện
            decimal? conditionValue = null;
            if (model.ConditionType == "MinOrderValue") conditionValue = model.MinOrderValue;
            else if (model.ConditionType == "MinProductValue") conditionValue = model.MinProductValue;
            else if (model.ConditionType == "MinProductQuantity") conditionValue = model.MinProductQuantity;
            var promotion = new Promotion
            {
                Name = model.Code,
                Type = Bloomie.Models.Entities.PromotionType.Product,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                IsActive = true,
                Description = $"Mã giảm giá sản phẩm: {model.Code}",
                AllowCombineOrder = model.AllowCombineOrder,
                AllowCombineProduct = model.AllowCombineProduct,
                AllowCombineShipping = model.AllowCombineShipping,
                ConditionType = model.ConditionType,
                ConditionValue = conditionValue,
                MinOrderValue = model.MinOrderValue,
                MinProductValue = model.MinProductValue,
                MinProductQuantity = model.MinProductQuantity,
            };
            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();

            var promoCode = new PromotionCode
            {
                Code = model.Code,
                PromotionId = promotion.Id,
                Value = model.Value,
                IsPercent = model.IsPercent,
                MaxDiscount = model.MaxDiscount,
                ExpiryDate = model.EndDate,
                IsActive = true,
                UsageLimit = model.UsageLimit,
                LimitPerCustomer = model.LimitPerCustomer,
                MinOrderValue = model.MinOrderValue.HasValue ? (int?)model.MinOrderValue.Value : null,
                UsedCount = 0,
                IsPublic = model.IsPublic 
            };
            _context.PromotionCodes.Add(promoCode);
            await _context.SaveChangesAsync();


            // Lưu theo ApplyType: product hoặc category
            var applyType = Request.Form["ApplyType"].ToString();
            if (applyType == "product" && model.ProductIds != null && model.ProductIds.Any())
            {
                foreach (var pid in model.ProductIds)
                {
                    var promoProduct = new PromotionProduct
                    {
                        PromotionId = promotion.Id,
                        ProductId = pid
                    };
                    _context.PromotionProducts.Add(promoProduct);
                }
                await _context.SaveChangesAsync();
            }
            else if (applyType == "category" && model.CategoryIds != null && model.CategoryIds.Any())
            {
                foreach (var cid in model.CategoryIds)
                {
                    var promoCategory = new PromotionCategory
                    {
                        PromotionId = promotion.Id,
                        CategoryId = cid
                    };
                    _context.PromotionCategories.Add(promoCategory);
                }
                await _context.SaveChangesAsync();
            }

            TempData["success"] = "Tạo mã giảm giá sản phẩm thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/CreateGift
        public IActionResult CreateGift()
        {
            ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
            ViewBag.Categories = _context.Categories.ToList();
            return View();
        }

        // POST: Admin/AdminPromotion/CreateGift
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateGift(Bloomie.Models.ViewModels.PromotionGiftVM model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                ViewBag.Categories = _context.Categories.ToList();
                return View(model);
            }


            var promotion = new Promotion
            {
                Name = model.Code,
                Type = Bloomie.Models.Entities.PromotionType.Gift,
                StartDate = model.StartDate,
                EndDate = model.EndDate, // Không gán mặc định, giữ nguyên null nếu không có ngày kết thúc
                IsActive = true,
                Description = $"Mua X tặng Y ({model.Code})",
                AllowCombineOrder = model.AllowCombineOrder,
                AllowCombineProduct = model.AllowCombineProduct,
                AllowCombineShipping = model.AllowCombineShipping,
                ConditionType = "Gift",
                ConditionValue = null
            };
            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();

            // Tạo PromotionCode cho chương trình Gift (Buy X Get Y)
            var promoCode = new PromotionCode
            {
                Code = model.Code,
                PromotionId = promotion.Id,
                Value = 0, // Không giảm giá trực tiếp, chỉ dùng để xác thực mã
                IsPercent = false,
                MaxDiscount = null,
                ExpiryDate = model.EndDate,
                IsActive = true,
                UsageLimit = model.UsageLimit, // Lưu giới hạn sử dụng từ form
                LimitPerCustomer = model.LimitPerCustomer, // Lưu giới hạn mỗi khách hàng
                MinOrderValue = null,
                UsedCount = 0,
                IsPublic = model.IsPublic 
            };
            _context.PromotionCodes.Add(promoCode);
            await _context.SaveChangesAsync();

            var promoGift = new PromotionGift
            {
                PromotionId = promotion.Id,
                BuyConditionType = model.BuyConditionType,
                BuyConditionValue = model.BuyConditionValue,
                BuyConditionValueMoney = model.BuyConditionValueMoney,
                BuyApplyType = model.BuyApplyType,
                GiftApplyType = model.GiftApplyType,
                GiftQuantity = model.GiftQuantity,
                GiftDiscountType = model.GiftDiscountType,
                GiftDiscountValue = model.GiftDiscountValue,
                GiftDiscountMoneyValue = model.GiftDiscountMoneyValue,
                LimitPerOrder = model.LimitPerOrder // Thêm giới hạn số lần áp dụng trong đơn
            };
            _context.PromotionGifts.Add(promoGift);
            await _context.SaveChangesAsync();

            // Liên kết sản phẩm/danh mục mua
            if (model.BuyProductIds != null && model.BuyProductIds.Any())
            {
                foreach (var pid in model.BuyProductIds)
                {
                    _context.PromotionGiftBuyProducts.Add(new PromotionGiftBuyProduct
                    {
                        PromotionGiftId = promoGift.Id,
                        ProductId = pid
                    });
                }
            }
            if (model.BuyCategoryIds != null && model.BuyCategoryIds.Any())
            {
                foreach (var cid in model.BuyCategoryIds)
                {
                    _context.PromotionGiftBuyCategories.Add(new PromotionGiftBuyCategory
                    {
                        PromotionGiftId = promoGift.Id,
                        CategoryId = cid
                    });
                }
            }
            // Liên kết sản phẩm/danh mục tặng
            if (model.GiftProductIds != null && model.GiftProductIds.Any())
            {
                foreach (var pid in model.GiftProductIds)
                {
                    _context.PromotionGiftGiftProducts.Add(new PromotionGiftGiftProduct
                    {
                        PromotionGiftId = promoGift.Id,
                        ProductId = pid
                    });
                }
            }
            if (model.GiftCategoryIds != null && model.GiftCategoryIds.Any())
            {
                foreach (var cid in model.GiftCategoryIds)
                {
                    _context.PromotionGiftGiftCategories.Add(new PromotionGiftGiftCategory
                    {
                        PromotionGiftId = promoGift.Id,
                        CategoryId = cid
                    });
                }
            }
            await _context.SaveChangesAsync();

            TempData["success"] = "Tạo chương trình mua X tặng Y thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/CreateShippingDiscount
        public IActionResult CreateShippingDiscount()
        {
            var model = new PromotionShippingDiscountVM();
            return View(model);
        }

        // POST: Admin/AdminPromotion/CreateShippingDiscount
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateShippingDiscount(Bloomie.Models.ViewModels.PromotionShippingDiscountVM model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Xác định điều kiện chính
            decimal? conditionValue = null;
            if (model.ConditionType == "MinOrderValue") conditionValue = model.MinOrderValue;
            else if (model.ConditionType == "MinProductValue") conditionValue = model.MinProductValue;
            else if (model.ConditionType == "MinProductQuantity") conditionValue = model.MinProductQuantity;

            var promotion = new Promotion
            {
                Name = model.Code,
                Type = Bloomie.Models.Entities.PromotionType.Shipping,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                IsActive = true,
                Description = $"Mã miễn phí vận chuyển: {model.Code}",
                AllowCombineOrder = model.AllowCombineOrder,
                AllowCombineProduct = model.AllowCombineProduct,
                ConditionType = model.ConditionType ?? "None",
                ConditionValue = conditionValue,
                MinOrderValue = model.MinOrderValue,
                MinProductValue = model.MinProductValue,
                MinProductQuantity = model.MinProductQuantity,
                ShippingDiscountType = model.ShippingDiscountType,
                ShippingDiscountValue = model.ShippingDiscountValue,
                ApplyDistricts = (model.ApplyScope == "wards" && model.ApplyDistricts != null && model.ApplyDistricts.Any()) ? System.Text.Json.JsonSerializer.Serialize(model.ApplyDistricts) : null,
                ApplyRadiusKm = model.ApplyRadiusKm
            };
            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();

            var promoCode = new PromotionCode
            {
                Code = model.Code,
                PromotionId = promotion.Id,
                Value = model.ShippingDiscountType == "free" ? 0 : model.ShippingDiscountValue,
                IsPercent = model.ShippingDiscountType == "percent",
                ExpiryDate = model.EndDate,
                IsActive = true,
                UsageLimit = model.UsageLimit,
                LimitPerCustomer = model.LimitPerCustomer,
                MinOrderValue = model.ConditionType == "MinOrderValue" && model.MinOrderValue.HasValue ? (int?)model.MinOrderValue.Value : null,
                UsedCount = 0,
                IsPublic = model.IsPublic 
            };
            _context.PromotionCodes.Add(promoCode);
            await _context.SaveChangesAsync();

            TempData["success"] = "Tạo mã miễn phí vận chuyển thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/EditOrderDiscount/5
        public IActionResult EditOrderDiscount(int id)
        {
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Order);
            if (promo == null) return NotFound();
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            var vm = new PromotionOrderDiscountVM
            {
                Code = code?.Code,
                Value = code?.Value ?? 0,
                IsPercent = code?.IsPercent ?? false,
                MaxDiscount = code?.MaxDiscount,
                StartDate = promo.StartDate,
                EndDate = promo.EndDate,
                ConditionType = promo.ConditionType ?? "None",
                ConditionValue = promo.ConditionValue,
                MinOrderValue = promo.MinOrderValue,
                MinProductQuantity = promo.MinProductQuantity,
                UsageLimit = code?.UsageLimit,
                LimitPerCustomer = code?.LimitPerCustomer ?? false,
                AllowCombineOrder = promo.AllowCombineOrder,
                AllowCombineProduct = promo.AllowCombineProduct,
                AllowCombineShipping = promo.AllowCombineShipping,
                IsActive = promo.IsActive
            };
            return View(vm);
        }

        // POST: Admin/AdminPromotion/EditOrderDiscount/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrderDiscount(int id, PromotionOrderDiscountVM model)
        {
            if (!ModelState.IsValid)
                return View(model);
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Order);
            if (promo == null) return NotFound();
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            // Cập nhật dữ liệu
            promo.Name = model.Code;
            promo.StartDate = model.StartDate;
            promo.EndDate = model.EndDate;
            promo.AllowCombineOrder = model.AllowCombineOrder;
            promo.AllowCombineProduct = model.AllowCombineProduct;
            promo.AllowCombineShipping = model.AllowCombineShipping;
            // Lưu tất cả các giá trị điều kiện
            promo.MinOrderValue = model.MinOrderValue;
            promo.MinProductQuantity = model.MinProductQuantity;
            // Chỉ lấy giá trị của loại đang chọn để set ConditionType/ConditionValue
            decimal? conditionValue = null;
            if (model.ConditionType == "MinOrderValue") conditionValue = model.MinOrderValue;
            else if (model.ConditionType == "MinProductQuantity") conditionValue = model.MinProductQuantity;
            promo.ConditionType = model.ConditionType;
            promo.ConditionValue = conditionValue;
            code.Code = model.Code;
            code.Value = model.Value;
            code.IsPercent = model.IsPercent;
            code.MaxDiscount = model.MaxDiscount;
            code.ExpiryDate = model.EndDate;
            code.UsageLimit = model.UsageLimit;
            code.LimitPerCustomer = model.LimitPerCustomer;
            code.MinOrderValue = model.ConditionType == "MinOrderValue" && model.MinOrderValue.HasValue ? (int?)model.MinOrderValue.Value : null;
            await _context.SaveChangesAsync();
            TempData["success"] = "Cập nhật mã giảm giá đơn hàng thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/EditProductDiscount/5
        public IActionResult EditProductDiscount(int id)
        {
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Product);
            if (promo == null) return NotFound();
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            var productIds = _context.PromotionProducts.Where(x => x.PromotionId == promo.Id).Select(x => x.ProductId).ToList();
            var categoryIds = _context.PromotionCategories.Where(x => x.PromotionId == promo.Id).Select(x => x.CategoryId).ToList();
            var vm = new PromotionProductDiscountVM
            {
                Code = code?.Code,
                Value = code?.Value ?? 0,
                IsPercent = code?.IsPercent ?? false,
                MaxDiscount = code?.MaxDiscount,
                StartDate = promo.StartDate,
                EndDate = promo.EndDate,
                ProductIds = productIds,
                CategoryIds = categoryIds,
                ConditionType = promo.ConditionType ?? "None",
                ConditionValue = promo.ConditionValue,
                MinOrderValue = promo.MinOrderValue,
                MinProductValue = promo.MinProductValue,
                MinProductQuantity = promo.MinProductQuantity,
                UsageLimit = code?.UsageLimit,
                LimitPerCustomer = code?.LimitPerCustomer ?? false,
                AllowCombineOrder = promo.AllowCombineOrder,
                AllowCombineProduct = promo.AllowCombineProduct,
                AllowCombineShipping = promo.AllowCombineShipping,
                IsActive = promo.IsActive
            };
            ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
            ViewBag.Categories = _context.Categories.ToList();
            return View(vm);
        }

        // POST: Admin/AdminPromotion/EditProductDiscount/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProductDiscount(int id, PromotionProductDiscountVM model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                ViewBag.Categories = _context.Categories.ToList();
                return View(model);
            }
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Product);
            if (promo == null) return NotFound();
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            // Cập nhật dữ liệu
            promo.Name = model.Code;
            promo.StartDate = model.StartDate;
            // Nếu model.EndDate có giá trị thì gán, nếu rỗng/null (tức là không có ngày kết thúc) thì set null
            promo.EndDate = model.EndDate;
            promo.AllowCombineOrder = model.AllowCombineOrder;
            promo.AllowCombineProduct = model.AllowCombineProduct;
            promo.AllowCombineShipping = model.AllowCombineShipping;
            // Lưu tất cả các giá trị điều kiện
            promo.MinOrderValue = model.MinOrderValue;
            promo.MinProductValue = model.MinProductValue;
            promo.MinProductQuantity = model.MinProductQuantity;
            // Chỉ lấy giá trị của loại đang chọn để set ConditionType/ConditionValue
            decimal? conditionValue = null;
            if (model.ConditionType == "MinOrderValue") conditionValue = model.MinOrderValue;
            else if (model.ConditionType == "MinProductValue") conditionValue = model.MinProductValue;
            else if (model.ConditionType == "MinProductQuantity") conditionValue = model.MinProductQuantity;
            promo.ConditionType = model.ConditionType;
            promo.ConditionValue = conditionValue;
            code.Code = model.Code;
            code.Value = model.Value;
            code.IsPercent = model.IsPercent;
            code.MaxDiscount = model.MaxDiscount;
            code.ExpiryDate = model.EndDate;
            code.UsageLimit = model.UsageLimit;
            code.LimitPerCustomer = model.LimitPerCustomer;
            code.MinOrderValue = model.MinOrderValue.HasValue ? (int?)model.MinOrderValue.Value : null;
            // Xóa liên kết cũ
            var oldProducts = _context.PromotionProducts.Where(x => x.PromotionId == promo.Id);
            _context.PromotionProducts.RemoveRange(oldProducts);
            var oldCategories = _context.PromotionCategories.Where(x => x.PromotionId == promo.Id);
            _context.PromotionCategories.RemoveRange(oldCategories);

            var applyType = Request.Form["ApplyType"].ToString();
            if (applyType == "product" && model.ProductIds != null && model.ProductIds.Any())
            {
                foreach (var pid in model.ProductIds)
                {
                    var promoProduct = new PromotionProduct
                    {
                        PromotionId = promo.Id,
                        ProductId = pid
                    };
                    _context.PromotionProducts.Add(promoProduct);
                }
            }
            else if (applyType == "category" && model.CategoryIds != null && model.CategoryIds.Any())
            {
                foreach (var cid in model.CategoryIds)
                {
                    var promoCategory = new PromotionCategory
                    {
                        PromotionId = promo.Id,
                        CategoryId = cid
                    };
                    _context.PromotionCategories.Add(promoCategory);
                }
            }
            await _context.SaveChangesAsync();
            TempData["success"] = "Cập nhật mã giảm giá sản phẩm thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/EditGift/5
        public IActionResult EditGift(int id)
        {
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Gift);
            if (promo == null) return NotFound();
            var gift = _context.PromotionGifts.FirstOrDefault(g => g.PromotionId == promo.Id);
            var buyProductIds = new List<int>();
            var buyCategoryIds = new List<int>();
            var giftProductIds = new List<int>();
            var giftCategoryIds = new List<int>();
            if (gift != null)
            {
                buyProductIds = _context.PromotionGiftBuyProducts.Where(x => x.PromotionGiftId == gift.Id).Select(x => x.ProductId).ToList();
                buyCategoryIds = _context.PromotionGiftBuyCategories.Where(x => x.PromotionGiftId == gift.Id).Select(x => x.CategoryId).ToList();
                giftProductIds = _context.PromotionGiftGiftProducts.Where(x => x.PromotionGiftId == gift.Id).Select(x => x.ProductId).ToList();
                giftCategoryIds = _context.PromotionGiftGiftCategories.Where(x => x.PromotionGiftId == gift.Id).Select(x => x.CategoryId).ToList();
            }
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            var vm = new PromotionGiftVM
            {
                Id = gift?.Id ?? 0,
                Code = promo.Name,
                StartDate = promo.StartDate,
                EndDate = promo.EndDate,
                BuyConditionType = gift?.BuyConditionType,
                BuyConditionValue = gift?.BuyConditionValue,
                BuyConditionValueMoney = gift?.BuyConditionValueMoney,
                BuyApplyType = gift?.BuyApplyType,
                BuyProductIds = buyProductIds,
                BuyCategoryIds = buyCategoryIds,
                GiftQuantity = gift?.GiftQuantity ?? 1,
                GiftApplyType = gift?.GiftApplyType,
                GiftProductIds = giftProductIds,
                GiftCategoryIds = giftCategoryIds,
                GiftDiscountType = gift?.GiftDiscountType,
                GiftDiscountValue = gift?.GiftDiscountValue,
                GiftDiscountMoneyValue = gift?.GiftDiscountMoneyValue,
                LimitPerOrder = gift?.LimitPerOrder ?? false, // Load giá trị LimitPerOrder
                UsageLimit = code?.UsageLimit,
                LimitPerCustomer = code?.LimitPerCustomer ?? false,
                AllowCombineOrder = promo.AllowCombineOrder,
                AllowCombineProduct = promo.AllowCombineProduct,
                AllowCombineShipping = promo.AllowCombineShipping,
                IsActive = promo.IsActive
            };
            ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
            ViewBag.Categories = _context.Categories.ToList();
            return View(vm);
        }

        // POST: Admin/AdminPromotion/EditGift/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditGift(int id, PromotionGiftVM model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                ViewBag.Categories = _context.Categories.ToList();
                return View(model);
            }
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Gift);
            if (promo == null) return NotFound();
            var gift = _context.PromotionGifts.FirstOrDefault(g => g.PromotionId == promo.Id);
            // Cập nhật dữ liệu
            promo.Name = model.Code;
            promo.StartDate = model.StartDate;
            promo.EndDate = model.EndDate; // Nếu null sẽ set null, đúng ý "không có ngày kết thúc"
            promo.AllowCombineOrder = model.AllowCombineOrder;
            promo.AllowCombineProduct = model.AllowCombineProduct;
            promo.AllowCombineShipping = model.AllowCombineShipping;
            
            // Cập nhật PromotionCode (UsageLimit và LimitPerCustomer)
            var promoCode = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            if (promoCode != null)
            {
                promoCode.UsageLimit = model.UsageLimit;
                promoCode.LimitPerCustomer = model.LimitPerCustomer;
            }
            
            if (gift != null)
            {
                gift.BuyConditionType = model.BuyConditionType;
                gift.BuyConditionValue = model.BuyConditionValue;
                gift.BuyConditionValueMoney = model.BuyConditionValueMoney;
                gift.BuyApplyType = model.BuyApplyType;
                gift.GiftApplyType = model.GiftApplyType;
                gift.GiftQuantity = model.GiftQuantity;
                gift.GiftDiscountType = model.GiftDiscountType;
                gift.GiftDiscountValue = model.GiftDiscountValue;
                gift.GiftDiscountMoneyValue = model.GiftDiscountMoneyValue;
                gift.LimitPerOrder = model.LimitPerOrder; // Cập nhật giới hạn số lần áp dụng trong đơn

                // Xóa liên kết cũ
                var oldBuyProducts = _context.PromotionGiftBuyProducts.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftBuyProducts.RemoveRange(oldBuyProducts);
                var oldBuyCategories = _context.PromotionGiftBuyCategories.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftBuyCategories.RemoveRange(oldBuyCategories);
                var oldGiftProducts = _context.PromotionGiftGiftProducts.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftGiftProducts.RemoveRange(oldGiftProducts);
                var oldGiftCategories = _context.PromotionGiftGiftCategories.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftGiftCategories.RemoveRange(oldGiftCategories);

                // Thêm liên kết mới
                if (model.BuyProductIds != null && model.BuyProductIds.Any())
                {
                    foreach (var pid in model.BuyProductIds)
                    {
                        _context.PromotionGiftBuyProducts.Add(new PromotionGiftBuyProduct
                        {
                            PromotionGiftId = gift.Id,
                            ProductId = pid
                        });
                    }
                }
                if (model.BuyCategoryIds != null && model.BuyCategoryIds.Any())
                {
                    foreach (var cid in model.BuyCategoryIds)
                    {
                        _context.PromotionGiftBuyCategories.Add(new PromotionGiftBuyCategory
                        {
                            PromotionGiftId = gift.Id,
                            CategoryId = cid
                        });
                    }
                }
                if (model.GiftProductIds != null && model.GiftProductIds.Any())
                {
                    foreach (var pid in model.GiftProductIds)
                    {
                        _context.PromotionGiftGiftProducts.Add(new PromotionGiftGiftProduct
                        {
                            PromotionGiftId = gift.Id,
                            ProductId = pid
                        });
                    }
                }
                if (model.GiftCategoryIds != null && model.GiftCategoryIds.Any())
                {
                    foreach (var cid in model.GiftCategoryIds)
                    {
                        _context.PromotionGiftGiftCategories.Add(new PromotionGiftGiftCategory
                        {
                            PromotionGiftId = gift.Id,
                            CategoryId = cid
                        });
                    }
                }
            }
            await _context.SaveChangesAsync();
            TempData["success"] = "Cập nhật chương trình mua X tặng Y thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/EditShippingDiscount/5
        public IActionResult EditShippingDiscount(int id)
        {
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Shipping);
            if (promo == null) return NotFound();
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            var vm = new PromotionShippingDiscountVM
            {
                Code = code?.Code,
                StartDate = promo.StartDate,
                EndDate = promo.EndDate,
                ConditionType = promo.ConditionType ?? "None",
                MinOrderValue = promo.MinOrderValue,
                MinProductValue = promo.MinProductValue,
                MinProductQuantity = promo.MinProductQuantity,
                ShippingDiscountType = promo.ShippingDiscountType,
                ShippingDiscountValue = promo.ShippingDiscountValue,
                ApplyScope = !string.IsNullOrEmpty(promo.ApplyDistricts) ? "wards" : "all",
                ApplyDistricts = !string.IsNullOrEmpty(promo.ApplyDistricts) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(promo.ApplyDistricts) : new List<string>(),
                ApplyRadiusKm = promo.ApplyRadiusKm,
                UsageLimit = code?.UsageLimit,
                LimitPerCustomer = code?.LimitPerCustomer ?? false,
                AllowCombineOrder = promo.AllowCombineOrder,
                AllowCombineProduct = promo.AllowCombineProduct,
                AllowCombineShipping = promo.AllowCombineShipping,
                IsActive = promo.IsActive
            };
            return View(vm);
        }

        // POST: Admin/AdminPromotion/EditShippingDiscount/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditShippingDiscount(int id, PromotionShippingDiscountVM model)
        {
            if (!ModelState.IsValid)
                return View(model);
            var promo = _context.Promotions.FirstOrDefault(p => p.Id == id && p.Type == PromotionType.Shipping);
            if (promo == null) return NotFound();
            var code = _context.PromotionCodes.FirstOrDefault(c => c.PromotionId == promo.Id);
            // Cập nhật dữ liệu
            promo.Name = model.Code;
            promo.StartDate = model.StartDate;
            promo.EndDate = model.EndDate;
            promo.ConditionType = model.ConditionType ?? "None";
            promo.MinOrderValue = model.MinOrderValue;
            promo.MinProductValue = model.MinProductValue;
            promo.MinProductQuantity = model.MinProductQuantity;
            decimal? conditionValue = null;
            if (model.ConditionType == "MinOrderValue") conditionValue = model.MinOrderValue;
            else if (model.ConditionType == "MinProductValue") conditionValue = model.MinProductValue;
            else if (model.ConditionType == "MinProductQuantity") conditionValue = model.MinProductQuantity;
            promo.ConditionValue = conditionValue;
            promo.ShippingDiscountType = model.ShippingDiscountType;
            promo.ShippingDiscountValue = model.ShippingDiscountValue;
            promo.ApplyDistricts = model.ApplyDistricts != null && model.ApplyDistricts.Any() ? System.Text.Json.JsonSerializer.Serialize(model.ApplyDistricts) : null;
            promo.ApplyRadiusKm = model.ApplyRadiusKm;
            promo.AllowCombineOrder = model.AllowCombineOrder;
            promo.AllowCombineProduct = model.AllowCombineProduct;
            promo.AllowCombineShipping = model.AllowCombineShipping;
            code.Code = model.Code;
            code.UsageLimit = model.UsageLimit;
            code.LimitPerCustomer = model.LimitPerCustomer;
            code.MinOrderValue = model.ConditionType == "MinOrderValue" && model.MinOrderValue.HasValue ? (int?)model.MinOrderValue.Value : null;
            code.ExpiryDate = model.EndDate;
            // Nếu có giảm giá số tiền/% thì cập nhật code.Value
            if (model.ShippingDiscountType == "money" || model.ShippingDiscountType == "percent")
                code.Value = model.ShippingDiscountValue.HasValue ? (decimal?)model.ShippingDiscountValue.Value : 0;
            else
                code.Value = 0;
            await _context.SaveChangesAsync();

            TempData["success"] = "Cập nhật mã miễn phí vận chuyển thành công!";
            return RedirectToAction("Index");
        }

        // GET: Admin/AdminPromotion/Details/5
        public IActionResult Details(int id)
        {
            var promo = _context.Promotions.Find(id);
            if (promo == null) return NotFound();

            // Truyền các trường kết hợp khuyến mãi cho view
            ViewBag.AllowCombineOrder = promo.AllowCombineOrder;
            ViewBag.AllowCombineProduct = promo.AllowCombineProduct;
            ViewBag.AllowCombineShipping = promo.AllowCombineShipping;

            // Lấy PromotionCode đầu tiên (nếu có)
            var promoCode = _context.PromotionCodes.FirstOrDefault(x => x.PromotionId == promo.Id);
            ViewBag.MaxDiscount = promoCode?.MaxDiscount;
            ViewBag.PromoCode = promoCode?.Code;
            ViewBag.IsPercent = promoCode?.IsPercent;
            ViewBag.Value = promoCode?.Value;

            // Load related data for each type if needed
            if (promo.Type == Models.Entities.PromotionType.Product)
            {
                ViewBag.ProductList = _context.PromotionProducts
                    .Where(x => x.PromotionId == promo.Id)
                    .Select(x => x.Product)
                    .ToList();
                ViewBag.CategoryList = _context.PromotionCategories
                    .Where(x => x.PromotionId == promo.Id)
                    .Select(x => x.Category)
                    .ToList();
            }
            else if (promo.Type == Models.Entities.PromotionType.Gift)
            {
                var gift = _context.PromotionGifts.FirstOrDefault(x => x.PromotionId == promo.Id);
                if (gift != null)
                {
                    ViewBag.GiftBuyProducts = _context.PromotionGiftBuyProducts
                        .Where(x => x.PromotionGiftId == gift.Id)
                        .Select(x => x.Product)
                        .ToList();
                    ViewBag.GiftBuyCategories = _context.PromotionGiftBuyCategories
                        .Where(x => x.PromotionGiftId == gift.Id)
                        .Select(x => x.Category)
                        .ToList();
                    ViewBag.GiftGiftProducts = _context.PromotionGiftGiftProducts
                        .Where(x => x.PromotionGiftId == gift.Id)
                        .Select(x => x.Product)
                        .ToList();
                    ViewBag.GiftGiftCategories = _context.PromotionGiftGiftCategories
                        .Where(x => x.PromotionGiftId == gift.Id)
                        .Select(x => x.Category)
                        .ToList();
                    // Truyền các trường chi tiết Gift
                    ViewBag.BuyConditionType = gift.BuyConditionType;
                    ViewBag.BuyConditionValue = gift.BuyConditionValue;
                    ViewBag.BuyConditionValueMoney = gift.BuyConditionValueMoney;
                    ViewBag.GiftQuantity = gift.GiftQuantity;
                    ViewBag.GiftDiscountType = gift.GiftDiscountType;
                    ViewBag.GiftDiscountValue = gift.GiftDiscountValue;
                    ViewBag.GiftDiscountMoneyValue = gift.GiftDiscountMoneyValue;
                    ViewBag.LimitPerOrder = gift.LimitPerOrder;
                }
            }
            // For Order/Shipping, no extra data needed for now
            return View(promo);
        }

        // GET: Admin/AdminPromotion/Delete/5
        public IActionResult Delete(int id)
        {
            var promo = _context.Promotions.Find(id);
            if (promo == null) return NotFound();
            return View(promo);
        }

        // POST: Admin/AdminPromotion/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var promo = _context.Promotions.Find(id);
            if (promo == null) return NotFound();

            // Xóa các PromotionCode liên quan
            var codes = _context.PromotionCodes.Where(c => c.PromotionId == id);
            _context.PromotionCodes.RemoveRange(codes);

            // Xóa các PromotionProduct liên quan
            var products = _context.PromotionProducts.Where(p => p.PromotionId == id);
            _context.PromotionProducts.RemoveRange(products);

            // Xóa các PromotionCategory liên quan
            var categories = _context.PromotionCategories.Where(c => c.PromotionId == id);
            _context.PromotionCategories.RemoveRange(categories);

            // Xóa các PromotionGift và các liên kết nếu là loại Gift
            var gift = _context.PromotionGifts.FirstOrDefault(g => g.PromotionId == id);
            if (gift != null)
            {
                var buyProducts = _context.PromotionGiftBuyProducts.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftBuyProducts.RemoveRange(buyProducts);
                var buyCategories = _context.PromotionGiftBuyCategories.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftBuyCategories.RemoveRange(buyCategories);
                var giftProducts = _context.PromotionGiftGiftProducts.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftGiftProducts.RemoveRange(giftProducts);
                var giftCategories = _context.PromotionGiftGiftCategories.Where(x => x.PromotionGiftId == gift.Id);
                _context.PromotionGiftGiftCategories.RemoveRange(giftCategories);
                _context.PromotionGifts.Remove(gift);
            }

            _context.Promotions.Remove(promo);
            await _context.SaveChangesAsync();
            TempData["success"] = "Đã xóa khuyến mãi thành công!";
            return RedirectToAction(nameof(Index));
        }

        // ============ TOGGLE IS PUBLIC ============
        // POST: Admin/AdminPromotion/TogglePublic/5
        [HttpPost]
        public async Task<IActionResult> TogglePublic(int promotionCodeId)
        {
            var promoCode = await _context.PromotionCodes.FindAsync(promotionCodeId);
            if (promoCode == null)
            {
                return Json(new { success = false, message = "Mã không tồn tại!" });
            }

            promoCode.IsPublic = !promoCode.IsPublic;
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                isPublic = promoCode.IsPublic,
                message = promoCode.IsPublic ? "Đã công khai mã!" : "Đã ẩn mã!" 
            });
        }

        // POST: Admin/AdminPromotion/SetAllPublic
        [HttpPost]
        public async Task<IActionResult> SetAllPublic(bool makePublic = true)
        {
            var codes = await _context.PromotionCodes.ToListAsync();
            foreach (var code in codes)
            {
                code.IsPublic = makePublic;
            }
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                count = codes.Count,
                message = $"Đã {(makePublic ? "công khai" : "ẩn")} {codes.Count} mã!" 
            });
        }

        // ============ FLASH SALE ============
        // GET: Admin/AdminPromotion/CreateFlashSale
        public IActionResult CreateFlashSale()
        {
            // Load các promotion codes active và chưa hết hạn để chọn
            var promoCodes = _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                .ToList();
            ViewBag.PromotionCodes = promoCodes;
            return View();
        }

        // POST: Admin/AdminPromotion/CreateFlashSale
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFlashSale(
            string name,
            string? description,
            int promotionCodeId,
            DateTime startDate,
            DateTime endDate,
            int totalVouchers,
            int maxVouchersPerUser)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.PromotionCodes = _context.PromotionCodes
                    .Include(pc => pc.Promotion)
                    .Where(pc => pc.IsActive && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                    .ToList();
                return View();
            }

            // Tạo config JSON cho Flash Sale
            var config = System.Text.Json.JsonSerializer.Serialize(new
            {
                TotalVouchers = totalVouchers,
                MaxVouchersPerUser = maxVouchersPerUser
            });

            var campaign = new VoucherCampaign
            {
                Name = name,
                Type = CampaignType.FlashSale,
                Description = description,
                PromotionCodeId = promotionCodeId,
                StartDate = startDate,
                EndDate = endDate,
                Config = config,
                IsActive = true,
                CreatedDate = DateTime.Now,
                CollectedCount = 0
            };

            _context.VoucherCampaigns.Add(campaign);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã tạo Flash Sale '{name}' thành công!";
            return RedirectToAction("Index");
        }

        // ============ LUCKY WHEEL ============
        // GET: Admin/AdminPromotion/CreateLuckyWheel
        public IActionResult CreateLuckyWheel()
        {
            var promoCodes = _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                .ToList();
            ViewBag.PromotionCodes = promoCodes;
            return View();
        }

        // POST: Admin/AdminPromotion/CreateLuckyWheel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLuckyWheel(
            string name,
            string? description,
            DateTime startDate,
            DateTime endDate,
            int spinsPerUser,
            string voucherIdsJson, // JSON array [{"Id": 1, "Rate": 0.1}, {"Id": 2, "Rate": 0.2}, ...]
            string? bannerImage)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.PromotionCodes = _context.PromotionCodes
                    .Include(pc => pc.Promotion)
                    .Where(pc => pc.IsActive && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                    .ToList();
                return View();
            }

            // Parse voucher rates với JsonElement
            var voucherRates = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(voucherIdsJson);

            // Tạo config JSON cho Lucky Wheel
            var config = System.Text.Json.JsonSerializer.Serialize(new
            {
                SpinsPerUser = spinsPerUser,
                VoucherRates = voucherRates
            });

            // Lấy ID voucher đầu tiên từ array JSON
            int firstVoucherId = 0;
            if (voucherRates.ValueKind == System.Text.Json.JsonValueKind.Array && voucherRates.GetArrayLength() > 0)
            {
                var firstItem = voucherRates[0];
                if (firstItem.TryGetProperty("Id", out var idElement))
                {
                    firstVoucherId = idElement.GetInt32();
                }
            }

            var campaign = new VoucherCampaign
            {
                Name = name,
                Type = CampaignType.LuckyWheel,
                Description = description,
                PromotionCodeId = firstVoucherId,
                StartDate = startDate,
                EndDate = endDate,
                Config = config,
                IsActive = true,
                CreatedDate = DateTime.Now,
                CollectedCount = 0,
                BannerImage = bannerImage
            };

            _context.VoucherCampaigns.Add(campaign);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã tạo Vòng Quay May Mắn '{name}' thành công!";
            return RedirectToAction("Index");
        }

        // ============ POINT SHOP MANAGEMENT ============
        // GET: Admin/AdminPromotion/ManagePointRewards
        public async Task<IActionResult> ManagePointRewards()
        {
            var rewards = await _context.PointRewards
                .Include(r => r.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();
            
            return View(rewards);
        }

        // GET: Admin/AdminPromotion/CreatePointReward
        public async Task<IActionResult> CreatePointReward()
        {
            // Lấy danh sách promotion codes để chọn
            var promotionCodes = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive)
                .OrderBy(pc => pc.Code)
                .ToListAsync();
            
            ViewBag.PromotionCodes = promotionCodes;
            return View();
        }

        // POST: Admin/AdminPromotion/CreatePointReward
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePointReward(
            string name,
            string? description,
            int pointsCost,
            int promotionCodeId,
            int? stock,
            int validDays,
            IFormFile? imageFile)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["error"] = "Vui lòng nhập tên phần quà!";
                return RedirectToAction(nameof(CreatePointReward));
            }

            if (pointsCost <= 0)
            {
                TempData["error"] = "Số điểm phải lớn hơn 0!";
                return RedirectToAction(nameof(CreatePointReward));
            }

            if (promotionCodeId <= 0)
            {
                TempData["error"] = "Vui lòng chọn Promotion Code!";
                return RedirectToAction(nameof(CreatePointReward));
            }

            string? imageUrl = null;
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "rewards");
                Directory.CreateDirectory(uploadsFolder);
                
                var uniqueFileName = $"{Guid.NewGuid()}_{imageFile.FileName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }
                
                imageUrl = $"/images/rewards/{uniqueFileName}";
            }

            // Lấy promotion code để xác định loại
            var promotionCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .FirstOrDefaultAsync(pc => pc.Id == promotionCodeId);

            if (promotionCode == null || promotionCode.Promotion == null)
            {
                TempData["error"] = "Promotion Code không hợp lệ!";
                return RedirectToAction(nameof(CreatePointReward));
            }

            // Tự động xác định RewardType dựa trên PromotionType
            RewardType rewardType = promotionCode.Promotion.Type switch
            {
                PromotionType.Shipping => RewardType.FreeShip,
                _ => RewardType.Voucher // Order, Product, Gift đều là Voucher
            };

            var reward = new PointReward
            {
                Name = name,
                Description = description,
                PointsCost = pointsCost,
                PromotionCodeId = promotionCodeId,
                RewardType = rewardType,
                Stock = stock,
                ValidDays = validDays,
                ImageUrl = imageUrl,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _context.PointRewards.Add(reward);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã tạo phần quà '{name}' thành công!";
            return RedirectToAction(nameof(ManagePointRewards));
        }

        // GET: Admin/AdminPromotion/EditPointReward/5
        public async Task<IActionResult> EditPointReward(int id)
        {
            var reward = await _context.PointRewards
                .Include(r => r.PromotionCode)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reward == null)
            {
                TempData["error"] = "Không tìm thấy phần quà!";
                return RedirectToAction(nameof(ManagePointRewards));
            }

            var promotionCodes = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive)
                .OrderBy(pc => pc.Code)
                .ToListAsync();
            
            ViewBag.PromotionCodes = promotionCodes;
            return View(reward);
        }

        // POST: Admin/AdminPromotion/EditPointReward/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPointReward(
            int id,
            string name,
            string? description,
            int pointsCost,
            int promotionCodeId,
            int? stock,
            int validDays,
            bool isActive,
            IFormFile? imageFile)
        {
            var reward = await _context.PointRewards.FindAsync(id);
            if (reward == null)
            {
                TempData["error"] = "Không tìm thấy phần quà!";
                return RedirectToAction(nameof(ManagePointRewards));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["error"] = "Vui lòng nhập tên phần quà!";
                return RedirectToAction(nameof(EditPointReward), new { id });
            }

            if (pointsCost <= 0)
            {
                TempData["error"] = "Số điểm phải lớn hơn 0!";
                return RedirectToAction(nameof(EditPointReward), new { id });
            }

            if (promotionCodeId <= 0)
            {
                TempData["error"] = "Vui lòng chọn Promotion Code!";
                return RedirectToAction(nameof(EditPointReward), new { id });
            }

            // Upload ảnh mới nếu có
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "rewards");
                Directory.CreateDirectory(uploadsFolder);
                
                var uniqueFileName = $"{Guid.NewGuid()}_{imageFile.FileName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }
                
                reward.ImageUrl = $"/images/rewards/{uniqueFileName}";
            }

            // Lấy promotion code để xác định loại
            var promotionCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .FirstOrDefaultAsync(pc => pc.Id == promotionCodeId);

            if (promotionCode == null || promotionCode.Promotion == null)
            {
                TempData["error"] = "Promotion Code không hợp lệ!";
                return RedirectToAction(nameof(EditPointReward), new { id });
            }

            // Tự động xác định RewardType dựa trên PromotionType
            RewardType rewardType = promotionCode.Promotion.Type switch
            {
                PromotionType.Shipping => RewardType.FreeShip,
                _ => RewardType.Voucher
            };

            reward.Name = name;
            reward.Description = description;
            reward.PointsCost = pointsCost;
            reward.PromotionCodeId = promotionCodeId;
            reward.RewardType = rewardType;
            reward.Stock = stock;
            reward.ValidDays = validDays;
            reward.IsActive = isActive;

            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã cập nhật phần quà '{name}' thành công!";
            return RedirectToAction(nameof(ManagePointRewards));
        }

        // POST: Admin/AdminPromotion/TogglePointRewardActive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePointRewardActive(int id)
        {
            var reward = await _context.PointRewards.FindAsync(id);
            if (reward == null)
            {
                TempData["error"] = "Không tìm thấy phần quà!";
                return RedirectToAction(nameof(ManagePointRewards));
            }

            reward.IsActive = !reward.IsActive;
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã {(reward.IsActive ? "kích hoạt" : "tắt")} phần quà '{reward.Name}'!";
            return RedirectToAction(nameof(ManagePointRewards));
        }

        // POST: Admin/AdminPromotion/DeletePointReward/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePointReward(int id)
        {
            var reward = await _context.PointRewards.FindAsync(id);
            if (reward == null)
            {
                TempData["error"] = "Không tìm thấy phần quà!";
                return RedirectToAction(nameof(ManagePointRewards));
            }

            // Kiểm tra xem có ai đã đổi chưa
            var hasRedemptions = await _context.PointRedemptions.AnyAsync(r => r.PointRewardId == id);
            if (hasRedemptions)
            {
                TempData["error"] = "Không thể xóa phần quà này vì đã có người đổi!";
                return RedirectToAction(nameof(ManagePointRewards));
            }

            _context.PointRewards.Remove(reward);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã xóa phần quà '{reward.Name}'!";
            return RedirectToAction(nameof(ManagePointRewards));
        }
    }
}
