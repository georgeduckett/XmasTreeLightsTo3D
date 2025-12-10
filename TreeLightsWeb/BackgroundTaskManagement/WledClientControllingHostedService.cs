using Microsoft.AspNetCore.Hosting;
using System.Diagnostics;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public class WledClientControllingHostedService<TClient> : BackgroundService where TClient : WledClient
    {
        private readonly ILogger<WledClientControllingHostedService<TClient>> _logger;
        private readonly TClient _treeClient;
        private IWledClientTaskManager<TClient> TaskQueue;
        private bool IsStarted = false;

        public WledClientControllingHostedService(IWledClientTaskManager<TClient> taskQueue,
            ILogger<WledClientControllingHostedService<TClient>> logger, IWebHostEnvironment webHostEnvironment, TClient treeClient)
        {
            TaskQueue = taskQueue;
            _logger = logger;
            _treeClient = treeClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await TaskQueue.DequeueAsync(_treeClient, stoppingToken);

                if (!IsStarted)
                {
                    // Turn it on, at full brightness
                    await _treeClient.SetOnOff(true, 255);
                    IsStarted = true;
                }

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

            await _treeClient.DisposeAsync();

        }
    }
}
