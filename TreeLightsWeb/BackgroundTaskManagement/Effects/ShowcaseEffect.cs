using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// Runs the effects one after another (in a shuffled order), changing from one to the next with a wipe: either a
    /// tilted plane sweeping across the tree or a plane spinning round the trunk like a clock hand (the rotating plane from
    /// RotateAroundAxis), with a bright edge where the two effects meet. Which effects it uses, and for how long each, are
    /// read as it goes, so changes on the Configuration page apply from the next effect. It says which effect it's
    /// starting (and which is next) as it goes, for the home page.
    /// </summary>
    public sealed class ShowcaseEffect : ITreeEffect
    {
        private const float TransitionSeconds = 3;
        private const float EdgeWidth = 0.1f; // tree units

        private readonly TreeGeometry _tree;
        private readonly Func<IReadOnlyList<string>> _effects;
        private readonly Func<string, ITreeEffect> _create;
        private readonly Func<float> _secondsEach;
        private readonly Func<string, float> _speedOf;
        private readonly Action<string, string, float>? _showing;
        private readonly ColourBuffer _incomingFrame;
        private readonly Random _random = new();
        private readonly Queue<string> _order = new();
        private string? _lastShown;

        private ITreeEffect _current;
        private string _currentName;
        private float _currentStart, _currentTime;
        private ITreeEffect? _incoming;
        private string? _incomingName;
        private float _incomingStart, _incomingTime;
        private int _transitions;
        // The wipe: a plane (normal, and the range of positions it sweeps over), or a hand going round from an angle
        private float _nx, _ny, _nz, _from, _to, _handAngle;

        /// <param name="effects">The names of the effects to show (there must be at least one).</param>
        /// <param name="create">Makes the effect with the given name.</param>
        /// <param name="secondsEach">How long to show each effect for.</param>
        /// <param name="speedOf">How fast each effect runs (1 for normal speed), read each frame so changes apply straight away.</param>
        /// <param name="showing">Told the effect that's starting, the one after it, and how long it'll show for.</param>
        public ShowcaseEffect(TreeGeometry tree, Func<IReadOnlyList<string>> effects, Func<string, ITreeEffect> create, Func<float> secondsEach,
                              Func<string, float>? speedOf = null, Action<string, string, float>? showing = null)
        {
            _tree = tree;
            _effects = effects;
            _create = create;
            _secondsEach = secondsEach;
            _speedOf = speedOf ?? (_ => 1f);
            _showing = showing;
            _incomingFrame = new ColourBuffer(tree.Count);
            _currentName = NextName();
            _current = Start(_currentName);
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            if (_incoming == null && t - _currentStart >= SecondsEach())
            {
                StartTransition(t);
            }

            var currentDt = dt * _speedOf(_currentName);
            _currentTime += currentDt;
            _current.Render(_currentTime, currentDt, frame);
            if (_incoming == null) return;

            var incomingDt = dt * _speedOf(_incomingName!);
            _incomingTime += incomingDt;
            _incoming.Render(_incomingTime, incomingDt, _incomingFrame);
            var progress = (t - _incomingStart) / TransitionSeconds;
            var edgeColour = new Rgb(1, 0.95f, 0.8f);
            for (var i = 0; i < _tree.Count; i++)
            {
                // How far the LED is behind the wipe's edge (positive: the new effect has reached it)
                float behind;
                if (_transitions % 2 == 0)
                {
                    var along = _tree.X[i] * _nx + _tree.Y[i] * _ny + _tree.Z[i] * _nz;
                    behind = _from + (_to - _from) * progress - along;
                }
                else
                {
                    var swept = progress * (Tau + 1) - 0.5f;
                    behind = (swept - WrapAngle(_tree.Theta[i] - _handAngle)) * MathF.Max(_tree.R[i], 0.15f);
                }
                var mix = SmoothStep(-EdgeWidth, EdgeWidth, behind);
                var edge = MathF.Exp(-(behind * behind) / (EdgeWidth * EdgeWidth * 0.5f)) * 0.8f;
                frame.Set(i, Rgb.Lerp(frame[i], _incomingFrame[i], mix) + edgeColour * edge);
            }

            if (progress >= 1)
            {
                _current = _incoming;
                _currentName = _incomingName!;
                _currentTime = _incomingTime;
                _currentStart = _incomingStart;
                _incoming = null;
                _incomingName = null;
                for (var i = 0; i < _tree.Count; i++) frame.Set(i, _incomingFrame[i]);
            }
        }

        private float SecondsEach() => MathF.Max(TransitionSeconds + 1, _secondsEach());

        private ITreeEffect Start(string name)
        {
            var effect = _create(name);
            _showing?.Invoke(name, Upcoming(), SecondsEach());
            return effect;
        }

        private void StartTransition(float t)
        {
            _incomingName = NextName();
            _incoming = Start(_incomingName);
            _incomingTime = 0;
            _incomingStart = t;
            _transitions++;
            if (_transitions % 2 == 0)
            {
                // A plane tilted at a random angle (not too near horizontal or vertical) sweeping right across the tree
                var azimuth = _random.NextSingle() * Tau;
                var elevation = (_random.NextSingle() * 2 - 1) * 0.6f;
                _nx = MathF.Cos(elevation) * MathF.Cos(azimuth);
                _ny = MathF.Cos(elevation) * MathF.Sin(azimuth);
                _nz = MathF.Sin(elevation);
                float min = float.MaxValue, max = float.MinValue;
                for (var i = 0; i < _tree.Count; i++)
                {
                    var along = _tree.X[i] * _nx + _tree.Y[i] * _ny + _tree.Z[i] * _nz;
                    min = MathF.Min(min, along);
                    max = MathF.Max(max, along);
                }
                (_from, _to) = (min - 2 * EdgeWidth, max + 2 * EdgeWidth);
            }
            else
            {
                _handAngle = _random.NextSingle() * Tau;
            }
        }

        private string NextName()
        {
            var name = Upcoming();
            _order.Dequeue();
            return _lastShown = name;
        }

        /// <summary>The next in the shuffled order that's still wanted (shuffling the next round, if this one's finished).</summary>
        private string Upcoming()
        {
            var effects = _effects();
            while (_order.Count > 0 && !effects.Contains(_order.Peek()))
            {
                _order.Dequeue();
            }
            if (_order.Count == 0)
            {
                var shuffled = effects.OrderBy(_ => _random.Next()).ToList();
                if (shuffled.Count > 1 && shuffled[0] == _lastShown)
                {
                    // Don't show the same effect twice in a row
                    (shuffled[0], shuffled[^1]) = (shuffled[^1], shuffled[0]);
                }
                foreach (var name in shuffled) _order.Enqueue(name);
            }
            return _order.Peek();
        }
    }
}
