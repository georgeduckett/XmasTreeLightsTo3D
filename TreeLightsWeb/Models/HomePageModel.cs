using TreeLightsWeb.BackgroundTaskManagement;

namespace TreeLightsWeb.Models
{
    /// <summary>
    /// The home page: the animations (in their groups, in order), the colours to draw each one's string of lights in,
    /// what's playing, Scrolling Text's message, how the Showcase is set up (how long each effect, and how many), and the
    /// palette in use.
    /// </summary>
    public sealed record HomePageModel(IReadOnlyList<AnimationInfo> Animations, IReadOnlyDictionary<string, IReadOnlyList<string>> Lights,
                                       NowPlayingState NowPlaying, string Message, int ShowcaseSecondsEach, int ShowcaseCount, string PaletteName);
}
