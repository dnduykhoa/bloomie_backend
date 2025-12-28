using Microsoft.AspNetCore.Mvc;
using Bloomie.Services.Interfaces;
using Bloomie.Models.ViewModels;

namespace Bloomie.ApiControllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FlowerDetectionApiController : ControllerBase
    {
        private readonly IFlowerDetectionService _flowerDetectionService;

        public FlowerDetectionApiController(IFlowerDetectionService flowerDetectionService)
        {
            _flowerDetectionService = flowerDetectionService;
        }

        [HttpPost("detect")]
        public async Task<IActionResult> DetectFlowers([FromForm] FlowerDetectionRequest request)
        {
            try
            {
                if (request.ImageFile == null || request.ImageFile.Length == 0)
                {
                    return BadRequest(new { success = false, message = "Vui lòng chọn ảnh" });
                }

                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var extension = Path.GetExtension(request.ImageFile.FileName).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest(new { success = false, message = "Chỉ hỗ trợ file JPG, PNG, WebP" });
                }

                // Validate file size (max 5MB)
                if (request.ImageFile.Length > 5 * 1024 * 1024)
                {
                    return BadRequest(new { success = false, message = "Kích thước ảnh tối đa 5MB" });
                }

                var result = await _flowerDetectionService.DetectFlowersWithPricesAsync(request.ImageFile);

                if (!result.Success)
                {
                    return Ok(new { success = false, message = result.Message ?? "Không thể nhận diện hoa" });
                }

                return Ok(new
                {
                    success = true,
                    total = result.Total,
                    counts = result.Counts,
                    flowersWithPrices = result.FlowersWithPrices?.Select(f => new
                    {
                        name = f.Name,
                        displayName = f.DisplayName,
                        quantity = f.Quantity,
                        flowerTypeId = f.FlowerTypeId,
                        pricePerStem = f.PricePerStem,
                        totalPrice = f.TotalPrice
                    }),
                    totalValue = result.TotalValue,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi server: {ex.Message}"
                });
            }
        }

        [HttpGet("flower-price/{flowerClassId}")]
        public async Task<IActionResult> GetFlowerPrice(string flowerClassId)
        {
            try
            {
                var priceData = await _flowerDetectionService.GetFlowerPriceAsync(flowerClassId);

                if (priceData == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy giá cho loài hoa này" });
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        flowerTypeName = priceData.FlowerTypeName,
                        pricePerStem = new
                        {
                            recommended = priceData.PricePerStem?.Recommended,
                            min = priceData.PricePerStem?.Min,
                            max = priceData.PricePerStem?.Max
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi server: {ex.Message}"
                });
            }
        }
    }
}
