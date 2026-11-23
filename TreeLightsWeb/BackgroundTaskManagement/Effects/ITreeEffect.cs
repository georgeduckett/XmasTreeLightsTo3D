namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// An animation drawn a frame at a time from the LEDs' positions and the time, so it runs at the same speed however
    /// fast frames can be sent, and so it can be mixed with another (see <see cref="ShowcaseEffect"/>).
    /// </summary>
    public interface ITreeEffect
    {
        /// <summary>Draws the frame for <paramref name="t"/> seconds after the effect started.</summary>
        /// <param name="dt">Seconds since the previous frame (for effects that simulate things moving).</param>
        /// <param name="frame">Every LED's colour, to set (it still holds the previous frame).</param>
        void Render(float t, float dt, ColourBuffer frame);
    }
}
