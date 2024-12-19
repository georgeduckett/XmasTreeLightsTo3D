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

        public TreeControllingHostedService(ITreeTaskManager taskQueue,
            ILogger<TreeControllingHostedService> logger, IWebHostEnvironment webHostEnvironment)
        {
            TaskQueue = taskQueue;
            _logger = logger;

            var contentFileProvider = webHostEnvironment.ContentRootFileProvider;

            var coordinatesFileInfo = contentFileProvider.GetFileInfo("coordinates.csv");

            using var coordiatesFileStream = coordinatesFileInfo.CreateReadStream();
            using var coordsReader = new StreamReader(coordiatesFileStream);
            var coords = coordsReader.ReadToEnd();
            _treeClient = new WledTreeClient("192.168.0.70", TimeSpan.FromSeconds(10), coords);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await _treeClient.LoadStateAsync();

            // Turn it on, at full brightness
            await _treeClient.SetOnOff(true, 255);

            await base.StartAsync(cancellationToken);
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await TaskQueue.DequeueAsync(_treeClient, stoppingToken);

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

            await _treeClient.DisposeAsync();

            await base.StopAsync(stoppingToken);
        }
    }
}
