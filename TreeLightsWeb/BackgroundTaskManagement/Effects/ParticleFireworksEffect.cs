using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// Fireworks: rockets with a trail climb from the base and burst inside the tree into sparks (in colours from the
    /// palette) that fly out, slow down, fall and fade from a white flash through their colour to embers. Several shells
    /// can be in the air at once.
    /// </summary>
    public sealed class ParticleFireworksEffect(TreeGeometry tree, IPaletteSource palette) : ITreeEffect
    {
        private const float RocketGravity = 2.2f;   // tree units/s² (the tree is about 2 wide)
        private const float SparkGravity = 0.9f;
        private const float SparkDrag = 1.6f;       // per second
        private static readonly Rgb Ember = new(1, 0.35f, 0.05f);
        private static readonly Rgb RocketColour = new(1, 0.85f, 0.6f);

        private sealed class Rocket
        {
            public float X, Y, Z, Vx, Vy, Vz;
            public readonly List<(float X, float Y, float Z, float T)> Trail = [];
        }

        private sealed class Spark
        {
            public float X, Y, Z, Vx, Vy, Vz, Age, Life;
            public Rgb Colour;
            public bool Glitter;
        }

        private readonly List<Rocket> _rockets = [];
        private readonly List<Spark> _sparks = [];
        private readonly List<(float Start, Rgb Colour)> _flashes = [];
        private readonly Random _random = new();
        private float _nextLaunch = 0.2f;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            if (t >= _nextLaunch)
            {
                if (_rockets.Count < 3) Launch(t);
                _nextLaunch = t + 0.9f + _random.NextSingle() * 0.9f;
            }

            for (var r = _rockets.Count - 1; r >= 0; r--)
            {
                var rocket = _rockets[r];
                rocket.X += rocket.Vx * dt; rocket.Y += rocket.Vy * dt; rocket.Z += rocket.Vz * dt;
                rocket.Vz -= RocketGravity * dt;
                rocket.Trail.Add((rocket.X, rocket.Y, rocket.Z, t));
                rocket.Trail.RemoveAll(p => t - p.T > 0.25f);
                if (rocket.Vz <= 0)
                {
                    Burst(rocket, t);
                    _rockets.RemoveAt(r);
                }
            }

            var drag = MathF.Exp(-SparkDrag * dt);
            for (var s = _sparks.Count - 1; s >= 0; s--)
            {
                var spark = _sparks[s];
                spark.Vx *= drag; spark.Vy *= drag; spark.Vz = spark.Vz * drag - SparkGravity * dt;
                spark.X += spark.Vx * dt; spark.Y += spark.Vy * dt; spark.Z += spark.Vz * dt;
                spark.Age += dt;
                if (spark.Age >= spark.Life) _sparks.RemoveAt(s);
            }
            _flashes.RemoveAll(f => t - f.Start > 0.5f);

            frame.Clear();

            // Each burst briefly lights the whole tree a little, like a flash lighting up the sky
            foreach (var (start, colour) in _flashes)
            {
                var a = 0.12f * MathF.Exp(-(t - start) * 10);
                for (var i = 0; i < tree.Count; i++) frame.Add(i, colour, a);
            }

            foreach (var rocket in _rockets)
            {
                Glow(frame, rocket.X, rocket.Y, rocket.Z, RocketColour, 1, 0.006f);
                foreach (var p in rocket.Trail)
                {
                    var fade = 1 - (t - p.T) / 0.25f;
                    Glow(frame, p.X, p.Y, p.Z, Ember, 0.5f * fade * fade, 0.004f);
                }
            }

            foreach (var spark in _sparks)
            {
                var age = spark.Age / spark.Life;
                var colour = age < 0.08f ? Rgb.Lerp(Rgb.White, spark.Colour, age / 0.08f)
                           : age > 0.7f ? Rgb.Lerp(spark.Colour, Ember, (age - 0.7f) / 0.3f)
                           : spark.Colour;
                var brightness = MathF.Pow(1 - age, 1.2f);
                if (spark.Glitter) brightness *= 0.3f + 0.7f * _random.NextSingle();
                Glow(frame, spark.X, spark.Y, spark.Z, colour, brightness, 0.01f);
                // A short streak behind it, along the way it's moving
                for (var k = 1; k <= 2; k++)
                {
                    var back = k * 0.05f;
                    Glow(frame, spark.X - spark.Vx * back, spark.Y - spark.Vy * back, spark.Z - spark.Vz * back, colour, brightness * (0.55f / k), 0.008f);
                }
            }
        }

        private void Launch(float t)
        {
            // From near the trunk at the bottom, aimed to burst somewhere in the middle of the tree
            var angle = _random.NextSingle() * Tau;
            var radius = MathF.Sqrt(_random.NextSingle()) * tree.ConeRadius(0) * 0.5f;
            var burstHeight = (0.4f + _random.NextSingle() * 0.45f) * tree.ZMax;
            var rocket = new Rocket { X = radius * MathF.Cos(angle), Y = radius * MathF.Sin(angle), Z = 0 };
            rocket.Vz = MathF.Sqrt(2 * RocketGravity * burstHeight);
            var flightTime = rocket.Vz / RocketGravity;
            // Drift towards the middle so the burst is inside the tree
            rocket.Vx = -rocket.X * 0.5f / flightTime;
            rocket.Vy = -rocket.Y * 0.5f / flightTime;
            _rockets.Add(rocket);
        }

        private void Burst(Rocket rocket, float t)
        {
            var colours = palette.Palette;
            var colourIndex = _random.Next(colours.Count);
            var kind = _random.Next(4); // 0 one colour, 1 two colours, 2 gold glitter, 3 a ring
            var main = colours[colourIndex];
            var second = colours[colourIndex + 1];
            var gold = new Rgb(1, 0.78f, 0.35f);
            var count = 60 + _random.Next(30);

            // For a ring: two directions spanning a randomly tilted plane
            var (ax, ay, az) = RandomDirection();
            var (bx, by, bz) = Normalise(Cross((ax, ay, az), RandomDirection()));
            var (cx, cy, cz) = Cross((ax, ay, az), (bx, by, bz));

            for (var n = 0; n < count; n++)
            {
                float dx, dy, dz, speed;
                if (kind == 3)
                {
                    var a = n * Tau / count;
                    (dx, dy, dz) = (bx * MathF.Cos(a) + cx * MathF.Sin(a), by * MathF.Cos(a) + cy * MathF.Sin(a), bz * MathF.Cos(a) + cz * MathF.Sin(a));
                    speed = 1.15f;
                }
                else
                {
                    (dx, dy, dz) = RandomDirection();
                    speed = 0.9f + _random.NextSingle() * 0.4f;
                }
                _sparks.Add(new Spark
                {
                    X = rocket.X, Y = rocket.Y, Z = rocket.Z,
                    Vx = dx * speed, Vy = dy * speed, Vz = dz * speed,
                    Life = 1.4f + _random.NextSingle() * 0.7f,
                    Colour = kind == 2 ? gold : kind == 1 && n % 2 == 1 ? second : main,
                    Glitter = kind == 2,
                });
            }
            _flashes.Add((t, kind == 2 ? gold : main));
        }

        private void Glow(ColourBuffer frame, float x, float y, float z, Rgb colour, float brightness, float size)
        {
            if (brightness <= 0.005f) return;
            var reach = size * 5;
            for (var i = 0; i < tree.Count; i++)
            {
                var d2 = tree.DistanceSquared(i, x, y, z);
                if (d2 < reach) frame.Add(i, colour, MathF.Exp(-d2 / size) * brightness);
            }
        }

        private (float X, float Y, float Z) RandomDirection()
        {
            var z = _random.NextSingle() * 2 - 1;
            var a = _random.NextSingle() * Tau;
            var r = MathF.Sqrt(1 - z * z);
            return (r * MathF.Cos(a), r * MathF.Sin(a), z);
        }

        private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
            (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        private static (float X, float Y, float Z) Normalise((float X, float Y, float Z) v)
        {
            var l = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            return l < 1e-6f ? (1, 0, 0) : (v.X / l, v.Y / l, v.Z / l);
        }
    }
}
