using System.Diagnostics;
using System.Numerics;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
	public partial class TreePatterns
	{
		public async ValueTask RotateDynamic(WledTreeClient client, CancellationToken cancellationToken)
		{
			Console.WriteLine("Set all to black");
			client.SetAllLeds(Colours.Black);
			await ApplyUpdate(client, cancellationToken);

			var centre = new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2);
			var colours = _settings.ColoursFor(RotateDynamicColours);
			// The angles come from the time, so it turns at the same speed however fast the tree can be updated
			var clock = Stopwatch.StartNew();
			while (!cancellationToken.IsCancellationRequested)
			{
				if (cancellationToken.IsCancellationRequested) { break; }

				var seconds = (float)clock.Elapsed.TotalSeconds;
				var yaw = seconds * RotationSpeed;
				var pitch = yaw * 500 / 4800; // pitch changes a tenth as fast, so the plane slowly tilts as it spins
				var turn = Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0);
				// The palette's first two colours (red and green at Christmas), or its own colours
				var (first, second) = (colours["One half"].ToRGBValue(), colours["Other half"].ToRGBValue());
				client.SetLedsColours(c => Vector3.Transform(c - centre, turn).X >= 0 ? first : second);
				await ApplyUpdate(client, cancellationToken, delayAfterMS: 20);
			}
		}
	}
}
