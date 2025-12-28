using Bloomie.Models.ViewModels;

namespace Bloomie.Services.Interfaces
{
    public interface IFlowerDetectionService
    {
        Task<FlowerDetectionResponse> DetectFlowersWithPricesAsync(IFormFile imageFile);
        Task<FlowerPriceData?> GetFlowerPriceAsync(string flowerClassId);
    }
}
