using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WLEDInterface;

namespace WLEDClientWebHosting
{
    public class WledClientControllingHostedService<TClient> : BackgroundService where TClient : WledClient
    {
        private readonly ILogger<WledClientControllingHostedService<TClient>> _logger;
        private readonly TClient _wledClient;
        private IWledClientTaskManager<TClient> TaskQueue;

        public WledClientControllingHostedService(IWledClientTaskManager<TClient> taskQueue,
            ILogger<WledClientControllingHostedService<TClient>> logger, IWebHostEnvironment webHostEnvironment, TClient wledClient)
        {
            TaskQueue = taskQueue;
            _logger = logger;
            _wledClient = wledClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await TaskQueue.DequeueAsync(_wledClient, stoppingToken);

                try
                {
                    await workItem();
                }
                catch (Exception ex) when (!Debugger.IsAttached)
                {
                    _logger.LogError(ex,
                        "Error occurred executing {WorkItem}.", nameof(workItem));
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queued Hosted Service is stopping.");

            // This is called here since it handles waiting for the running task to finish which needs to happen before we dispose of the treeclient
            await base.StopAsync(stoppingToken);

            await _wledClient.DisposeAsync();

        }
    }
}
