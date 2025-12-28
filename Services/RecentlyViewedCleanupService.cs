using Bloomie.Data;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Services
{
    public class RecentlyViewedCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RecentlyViewedCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6); // Run every 6 hours

        public RecentlyViewedCleanupService(
            IServiceProvider serviceProvider,
            ILogger<RecentlyViewedCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Recently Viewed Cleanup Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupOldRecordsAsync(stoppingToken);
                    await Task.Delay(_cleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    _logger.LogInformation("Recently Viewed Cleanup Service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while cleaning up old recently viewed records");
                    // Wait a bit before retrying
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("Recently Viewed Cleanup Service stopped");
        }

        private async Task CleanupOldRecordsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var twentyFourHoursAgo = DateTime.Now.AddHours(-24);

            try
            {
                // Delete records older than 24 hours
                var oldRecords = await context.RecentlyViewedProducts
                    .Where(rv => rv.ViewedAt < twentyFourHoursAgo)
                    .ToListAsync(cancellationToken);

                if (oldRecords.Any())
                {
                    context.RecentlyViewedProducts.RemoveRange(oldRecords);
                    await context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Cleaned up {Count} old recently viewed records from before {CutoffTime}",
                        oldRecords.Count,
                        twentyFourHoursAgo);
                }
                else
                {
                    _logger.LogInformation("No old recently viewed records to clean up");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup of old recently viewed records");
                throw;
            }
        }
    }
}
