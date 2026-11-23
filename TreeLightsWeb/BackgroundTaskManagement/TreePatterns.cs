using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using TreeLightsWeb.BackgroundTaskManagement.Effects;
using TreeLightsWeb.Settings;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        // The browser preview (the Tree page) is sent the LEDs' colours at most this often, so a fast animation can't flood it
        private const int PreviewIntervalMS = 50;
        // Animations made from an effect aim for 40 frames a second (WLED and the LEDs set the real limit)
        private const int EffectFrameIntervalMS = 25;

        private readonly IHubContext<TreeHub> _contextHub;
        private readonly MusicLevels _musicLevels;
        private readonly TreeSettingsService _settings;
        private readonly IWebHostEnvironment _environment;
        private readonly Dictionary<int, RGBValue> _previewChanges = [];
        private long _lastPreview = long.MinValue / 2;

        public TreePatterns(IHubContext<TreeHub> contextHub, MusicLevels musicLevels, TreeSettingsService settings, IWebHostEnvironment environment)
        {
            _contextHub = contextHub;
            _musicLevels = musicLevels;
            _settings = settings;
            _environment = environment;
        }
        private async Task DelayNoException(int delay, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) { }
        }
        private async Task ApplyUpdate(WledTreeClient client, CancellationToken ct, int delayBeforeMS = 0, int delayAfterMS = 0)
        {
            var ledUpdates = await client.ApplyUpdate(ct, delayBeforeMS, delayAfterMS);

            foreach (var ledUpdate in ledUpdates)
            {
                _previewChanges[ledUpdate.LedIndex] = ledUpdate.NewColour;
            }
            var now = Environment.TickCount64;
            if (_previewChanges.Count != 0 && now - _lastPreview >= PreviewIntervalMS)
            {
                _lastPreview = now;
                var changes = _previewChanges.Select(c => new WledClient.LedUpdate(c.Key, c.Value)).ToArray();
                _previewChanges.Clear();
                await _contextHub.Clients.All.SendAsync("UpdateLeds", changes);
            }
        }

        /// <summary>
        /// Runs an effect until the animation's stopped: draws a frame from the time since it started, sends it, and repeats
        /// (about every 25ms, or as fast as the tree can be updated if that's slower; either way it runs at the same speed).
        /// </summary>
        private async ValueTask RunEffect(WledTreeClient client, CancellationToken cancellationToken, Func<TreeGeometry, ITreeEffect> createEffect)
        {
            var tree = TreeGeometry.For(client);
            var effect = createEffect(tree);
            var frame = new ColourBuffer(tree.Count);
            var ledCount = Math.Min(tree.Count, client.LedIndexEnd - client.LedIndexStart);
            var clock = Stopwatch.StartNew();
            var previous = 0.0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = clock.Elapsed.TotalSeconds;
                var dt = (float)Math.Clamp(now - previous, 0, 0.1);
                previous = now;

                effect.Render((float)now, dt, frame);
                for (var i = 0; i < ledCount; i++)
                {
                    client.SetLedColour(i, frame.ToRGBValue(i));
                }
                await ApplyUpdate(client, cancellationToken, delayAfterMS: EffectFrameIntervalMS);
            }
        }
    }
}
