using System.Net.Http.Headers;
using System.Text.Json;
using Bloomie.Models.ViewModels;
using Bloomie.Services.Interfaces;

namespace Bloomie.Services.Implementations
{
    public class FlowerDetectionService : IFlowerDetectionService
    {
        private readonly HttpClient _aiHttpClient;
        private readonly HttpClient _backendHttpClient;
        private readonly IConfiguration _configuration;
        private readonly string _aiBaseUrl;

        public FlowerDetectionService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _aiHttpClient = httpClientFactory.CreateClient();
            _backendHttpClient = httpClientFactory.CreateClient();
            _configuration = configuration;
            
            // Python AI server URL
            _aiBaseUrl = configuration["FlowerDetection:AiServerUrl"] ?? "http://172.20.10.3:8000";
            
            _aiHttpClient.BaseAddress = new Uri(_aiBaseUrl);
            _aiHttpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<FlowerDetectionResponse> DetectFlowersWithPricesAsync(IFormFile imageFile)
        {
            try
            {
                // Step 1: Call Python AI to detect flowers
                using var content = new MultipartFormDataContent();
                using var fileStream = imageFile.OpenReadStream();
                using var streamContent = new StreamContent(fileStream);
                
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(imageFile.ContentType);
                content.Add(streamContent, "file", imageFile.FileName);

                var aiResponse = await _aiHttpClient.PostAsync("/detect", content);
                
                if (!aiResponse.IsSuccessStatusCode)
                {
                    return new FlowerDetectionResponse
                    {
                        Success = false,
                        Message = "Không thể kết nối đến AI server"
                    };
                }

                var aiResultJson = await aiResponse.Content.ReadAsStringAsync();
                var aiResult = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(aiResultJson);

                if (aiResult == null || !aiResult.ContainsKey("success") || !aiResult["success"].GetBoolean())
                {
                    return new FlowerDetectionResponse
                    {
                        Success = false,
                        Message = "AI không nhận diện được hoa"
                    };
                }

                var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(
                    aiResult["counts"].GetRawText()
                ) ?? new Dictionary<string, int>();

                var total = aiResult.ContainsKey("total") ? aiResult["total"].GetInt32() : 0;

                // Step 2: Get prices from backend for each flower type
                var flowersWithPrices = new List<FlowerWithPriceViewModel>();

                foreach (var entry in counts)
                {
                    var flowerTypeIdStr = entry.Key;
                    var quantity = entry.Value;

                    var priceData = await GetFlowerPriceAsync(flowerTypeIdStr);

                    var pricePerStem = priceData?.PricePerStem?.Recommended ?? 0;
                    var totalPrice = pricePerStem * quantity;

                    flowersWithPrices.Add(new FlowerWithPriceViewModel
                    {
                        Name = flowerTypeIdStr,
                        DisplayName = FormatFlowerName(flowerTypeIdStr, priceData),
                        Quantity = quantity,
                        FlowerTypeId = int.TryParse(flowerTypeIdStr, out var id) ? id : null,
                        PricePerStem = pricePerStem,
                        TotalPrice = totalPrice,
                        PriceData = priceData
                    });
                }

                var totalValue = flowersWithPrices.Sum(f => f.TotalPrice);

                return new FlowerDetectionResponse
                {
                    Success = true,
                    Total = total,
                    Counts = counts,
                    FlowersWithPrices = flowersWithPrices,
                    TotalValue = totalValue,
                    Message = $"Phát hiện {total} bông hoa"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DetectFlowersWithPricesAsync: {ex.Message}");
                return new FlowerDetectionResponse
                {
                    Success = false,
                    Message = $"Lỗi: {ex.Message}"
                };
            }
        }

        public async Task<FlowerPriceData?> GetFlowerPriceAsync(string flowerClassId)
        {
            try
            {
                if (!int.TryParse(flowerClassId, out var flowerTypeId))
                {
                    Console.WriteLine($"Invalid flower class ID: {flowerClassId}");
                    return null;
                }

                // Call ProductApiController endpoint
                var response = await _backendHttpClient.GetAsync(
                    $"{_configuration["AppSettings:BaseUrl"]}/api/ProductApi/flower-price-by-id?flowerTypeId={flowerTypeId}"
                );

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (result == null || !result.ContainsKey("success") || !result["success"].GetBoolean())
                {
                    return null;
                }

                var data = result["data"];
                var priceData = new FlowerPriceData
                {
                    FlowerTypeName = data.GetProperty("flowerTypeName").GetString(),
                    PricePerStem = new PriceRange
                    {
                        Recommended = data.GetProperty("pricePerStem").GetProperty("recommended").GetDecimal(),
                        Min = data.GetProperty("pricePerStem").GetProperty("min").GetDecimal(),
                        Max = data.GetProperty("pricePerStem").GetProperty("max").GetDecimal()
                    }
                };

                return priceData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetFlowerPriceAsync: {ex.Message}");
                return null;
            }
        }

        private string FormatFlowerName(string className, FlowerPriceData? priceData)
        {
            if (priceData?.FlowerTypeName != null)
            {
                return priceData.FlowerTypeName;
            }

            return className.Replace("_", " ").ToUpper();
        }
    }
}
