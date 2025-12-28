using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Services.Implementations
{
    public class ShippingService : IShippingService
    {
        private readonly ApplicationDbContext _context;

        public ShippingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<decimal?> GetShippingFee(string wardCode)
        {
            if (string.IsNullOrWhiteSpace(wardCode))
                return null;

            var shippingFee = await _context.ShippingFees
                .Where(sf => sf.WardCode == wardCode && sf.IsActive)
                .FirstOrDefaultAsync();

            return shippingFee?.Fee;
        }

        public async Task<bool> IsWardSupported(string wardCode)
        {
            if (string.IsNullOrWhiteSpace(wardCode))
                return false;

            return await _context.ShippingFees
                .AnyAsync(sf => sf.WardCode == wardCode && sf.IsActive);
        }

        public async Task<List<ShippingFee>> GetAllActiveShippingFees()
        {
            return await _context.ShippingFees
                .Where(sf => sf.IsActive)
                .OrderBy(sf => sf.WardName)
                .ToListAsync();
        }
    }
}
