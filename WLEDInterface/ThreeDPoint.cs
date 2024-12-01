using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WLEDInterface
{
    public record ThreeDPoint(double X, double Y, double Z)
    {
        public ThreeDPoint(double[] points) : this(points[0], points[1], points[2]) { }
    }
}
