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

			var yaw = 0.0f;
			var pitch = 0.0f;
			while (!cancellationToken.IsCancellationRequested)
			{
				if (cancellationToken.IsCancellationRequested) { break; }

				client.SetLedsColours(c => Vector3.Transform(c - new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2), Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0)).X >= 0 ? Colours.Red : Colours.Green);
				await ApplyUpdate(client, cancellationToken);
				yaw += (float)(180 / Math.PI) / 500;
				pitch += (float)(180 / Math.PI) / 4800;
			}
		}
	}
}
