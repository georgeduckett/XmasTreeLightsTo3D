using Microsoft.AspNetCore.Hosting;
using System.Diagnostics;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public class TreeControllingHostedService : BackgroundService
    {
        private readonly ILogger<TreeControllingHostedService> _logger;
        private readonly WledTreeClient _treeClient;
        private ITreeTaskManager TaskQueue;
        private bool IsStarted = false;

        public TreeControllingHostedService(ITreeTaskManager taskQueue,
            ILogger<TreeControllingHostedService> logger, IWebHostEnvironment webHostEnvironment, WledTreeClient treeClient)
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
