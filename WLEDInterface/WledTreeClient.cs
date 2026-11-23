using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
                if (_ledCoordinates == null)
                {
                    throw new InvalidOperationException("LED coordinates were not provided.");
                }
                return _ledCoordinates;
            }
        }
        public WledTreeClient(string uriBase, TimeSpan timeout, bool useDDP = false, string? coords = null) : base(uriBase, timeout, useDDP)
        {
            if (coords != null)
            {
                _ledCoordinates = ParseCoordinates(coords);
            }
        }

        /// <summary>
        /// Uses new LED coordinates (the contents of a coordinates.csv file) from now on, e.g. after the images have been
        /// processed again.
        /// </summary>
        public void SetCoordinates(string coords)
        {
            _ledCoordinates = ParseCoordinates(coords);
        }

        private static ReadOnlyCollection<Vector3> ParseCoordinates(string coords)
        {
            // The file's numbers are always written with a '.' decimal point, whatever the culture
            return coords.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("index"))
                         .Select(line => new Vector3(line.Trim().Trim('[', ']').Split(',')
                                                         .Where(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                                                         .Select(s => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture))
                                                         .Skip(1).Take(3).ToArray().AsSpan()))
                         .ToArray().AsReadOnly();
        }

        public override async Task LoadStateAsync()
        {
            await base.LoadStateAsync();

            // Note: there are no coordinates until the tree's images have been captured and processed, which is fine
            if (_ledCoordinates != null && _ledCoordinates.Count != 0 && _ledCoordinates.Count != LedIndexEnd - LedIndexStart)
            {
                Console.WriteLine($"WARNING: The number of LEDs (in segment one, the only supported segment), {LedIndexEnd - LedIndexStart} does not match the number of LED coordinates we have, {LedCoordinates.Count}.");
            }
        }

        public void SetLedsColours(Func<Vector3, RGBValue> funcLedColour)
        {
            SetLedsColours(LedCoordinates.Select((c, i) => new LedUpdate(i, funcLedColour(c))).ToArray());
        }
        public void SetLedsColours(Func<Vector3, bool> ledSelector, Func<Vector3, RGBValue> funcLedColour)
        {
            // Number the LEDs before filtering them, so each selected LED keeps its own index
            SetLedsColours(LedCoordinates.Select((c, i) => (Coordinate: c, Index: i))
                                         .Where(led => ledSelector(led.Coordinate))
                                         .Select(led => new LedUpdate(led.Index, funcLedColour(led.Coordinate))).ToArray());
        }
        public void SetLedsColours(Func<int, Vector3, RGBValue> funcLedColour)
        {
            SetLedsColours(LedCoordinates.Select((c, i) => new LedUpdate(i, funcLedColour(i, c))).ToArray());
        }
    }
}