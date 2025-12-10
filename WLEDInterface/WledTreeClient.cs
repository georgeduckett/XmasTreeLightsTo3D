using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace WLEDInterface
{
    public class WledTreeClient : WledClient
    {
        private ReadOnlyCollection<Vector3>? _ledCoordinates = null;
        public ReadOnlyCollection<Vector3> LedCoordinates
        {
            get
            {
                if(_ledCoordinates == null)
                {
                    throw new InvalidOperationException("LED coordinates were not provided.");
                }
                return _ledCoordinates;
            }
        }
        public WledTreeClient(string uriBase, TimeSpan timeout, string? coords = null) : base(uriBase, timeout)
        {
            if (coords != null)
            {
                _ledCoordinates = coords.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("index")).Select(line => new Vector3(line.Trim().Trim('[', ']').Split(',').Where(s => float.TryParse(s, out _)).Select(s => float.Parse(s)).Skip(1).Take(3).ToArray().AsSpan())).ToArray().AsReadOnly();
            }
        }

        public void SetLedsColours(Func<Vector3, RGBValue> funcLedColour)
        {
            SetLedsColours(LedCoordinates.Select((c, i) => new LedUpdate(i, funcLedColour(c))).ToArray());
        }
        public void SetLedsColours(Func<Vector3, bool> ledSelector, Func<Vector3, RGBValue> funcLedColour)
        {
            SetLedsColours(LedCoordinates.Where(ledSelector).Select((c, i) => new LedUpdate(i, funcLedColour(c))).ToArray());
        }
        public void SetLedsColours(Func<int, Vector3, RGBValue> funcLedColour)
        {
            SetLedsColours(LedCoordinates.Select((c, i) => new LedUpdate(i, funcLedColour(i, c))).ToArray());
        }
    }
}