namespace TreeLightsWeb.BackgroundTaskManagement
{
    /// <summary>
    /// What the music page last heard (sent ~30 times a second through <see cref="TreeHub"/>): how loud the bass, middle
    /// and treble are (0-1, relative to the music's recent peaks) and how many beats it's counted.
    /// </summary>
    public sealed class MusicLevels
    {
        private readonly object _lock = new();
        private float _bass, _mid, _treble;
        private int _beats;
        private long _updated = long.MinValue / 2;

        public void Update(float bass, float mid, float treble, int beats)
        {
            lock (_lock)
            {
                _bass = Math.Clamp(bass, 0, 1);
                _mid = Math.Clamp(mid, 0, 1);
                _treble = Math.Clamp(treble, 0, 1);
                _beats = beats;
                _updated = Environment.TickCount64;
            }
        }

        public MusicSnapshot Read()
        {
            lock (_lock)
            {
                return new MusicSnapshot(_bass, _mid, _treble, _beats, (Environment.TickCount64 - _updated) / 1000.0);
            }
        }
    }

    public readonly record struct MusicSnapshot(float Bass, float Mid, float Treble, int Beats, double SecondsSinceUpdate)
    {
        /// <summary>Whether the music page is sending levels (it stops when the music's paused or the page is closed).</summary>
        public bool IsLive => SecondsSinceUpdate < 2;
    }
}
