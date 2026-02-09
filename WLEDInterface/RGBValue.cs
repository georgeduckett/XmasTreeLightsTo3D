using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WLEDInterface
{
    public record struct RGBValue(byte Red, byte Green, byte Blue)
    {
        public RGBValue() : this(0, 0, 0) { }
        public readonly string ToHex() => $"{Red:X2}{Green:X2}{Blue:X2}";

        public static implicit operator RGBValueUnclamped(RGBValue a) => new RGBValueUnclamped(a.Red, a.Green, a.Blue);
        public static RGBValueUnclamped operator *(RGBValue a, double b) => new(a.Red * b, a.Green * b, a.Blue * b);
        public static RGBValueUnclamped operator +(RGBValue a, RGBValue b) => new(a.Red + b.Red, a.Green + b.Green, a.Blue + b.Blue);
        public static RGBValueUnclamped operator -(RGBValue a, RGBValue b) => new(a.Red - b.Red, a.Green - b.Green, a.Blue - b.Blue);
    }
    public record struct RGBValueUnclamped(double Red, double Green, double Blue)
    {
        public RGBValueUnclamped() : this(0, 0, 0) { }
        public static explicit operator RGBValue(RGBValueUnclamped a) => new(
            (byte)Math.Clamp(Math.Round(a.Red), 0, 255),
            (byte)Math.Clamp(Math.Round(a.Green), 0, 255),
            (byte)Math.Clamp(Math.Round(a.Blue), 0, 255)
        );
        public static RGBValueUnclamped operator *(RGBValueUnclamped a, double b) => new RGBValueUnclamped(a.Red * b, a.Green * b, a.Blue * b);
        public static RGBValueUnclamped operator +(RGBValueUnclamped a, RGBValueUnclamped b) => new RGBValueUnclamped(a.Red + b.Red, a.Green + b.Green, a.Blue + b.Blue);
    }

    public static class Colours
    {
        public static RGBValue Black => new(0, 0, 0);
        public static RGBValue White => new(255, 255, 255);
        public static RGBValue Red => new(255, 0, 0);
        public static RGBValue Pink => new(217, 1, 102);
        public static RGBValue Green => new(0, 255, 0);
        public static RGBValue Blue => new(0, 0, 255);
        public static RGBValue Orange => new(255, 165, 0);
        public static RGBValue Yellow => new(255, 255, 0);
    }
}
