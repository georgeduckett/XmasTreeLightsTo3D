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
    }

    public static class Colours
    {
        public static RGBValue Black => new(0, 0, 0);
        public static RGBValue White => new(255, 255, 255);
        public static RGBValue Red => new(255, 0, 0);
        public static RGBValue Green => new(0, 255, 0);
        public static RGBValue Blue => new(0, 0, 255);
    }
}
