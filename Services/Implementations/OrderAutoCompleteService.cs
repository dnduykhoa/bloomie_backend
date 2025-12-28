using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Bloomie.Data;

namespace Bloomie.Services.Implementations
{
    public class OrderAutoCompleteService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromHours(6); // Kiểm tra mỗi 6 tiếng

        public OrderAutoCompleteService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var now = DateTime.Now;

                    var orders = context.Orders
                        .Where(o => o.Status == "Đã giao" && o.DeliveryDate != null)
                        .AsEnumerable() // chuyển sang xử lý trên memory
                        .Where(o => (now - o.DeliveryDate.Value).TotalDays >= 2)
                        .ToList();

                    foreach (var order in orders)
                    {
                        order.Status = "Hoàn thành";
                    }
                    if (orders.Count > 0)
                    {
                        await context.SaveChangesAsync();
                    }
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
