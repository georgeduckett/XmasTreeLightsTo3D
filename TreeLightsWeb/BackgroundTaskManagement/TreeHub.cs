using Microsoft.AspNetCore.SignalR;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public class TreeHub(MusicLevels musicLevels) : Hub
    {
        /// <summary>The music page calls this about 30 times a second with what it's hearing (see <see cref="MusicLevels"/>).</summary>
        public void SendMusicLevels(float bass, float mid, float treble, int beats) => musicLevels.Update(bass, mid, treble, beats);
    }
}
