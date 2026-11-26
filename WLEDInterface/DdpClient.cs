using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using static WLEDInterface.WledClient;

namespace WLEDInterface
{
    /// <summary>
    /// Sends LED colours to WLED with the Distributed Display Protocol (DDP, http://www.3waylabs.com/ddp/): plain UDP packets
    /// of up to 480 RGB LEDs, with nothing for WLED to parse, so it can keep up with an animation running at 30-40 frames a
    /// second (which its JSON API can't when every LED changes every frame).
    /// </summary>
    public class DdpClient : IDisposable
    {
        private const int DDP_PORT = 4048;

        private const int HEADER_LEN = 10;
        private const int MAX_PIXELS = 480; // 1440 bytes of RGB data, so each packet fits in one Ethernet frame
        private const byte VER1 = 0x40;
        private const byte PUSH = 0x01;
        private const byte DATATYPE = 0x0B; // RGB, 8 bits per channel
        private const byte DESTINATIONID = 0x01; // Default output device

        private byte _frameNumber = 0;
        private readonly UdpClient _UpdClient;
        private readonly byte[] _packet = new byte[HEADER_LEN + MAX_PIXELS * 3];
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private double _gamma = 1;
        private float[] _levels = CreateLevels(1);
        // Whether each LED was lit in the last frame sent (see KeepAboveDimmest)
        private bool[] _lit = new bool[1024];

        /// <summary>
        /// The gamma correction applied to every colour value before it's sent (1 for none). WLED doesn't gamma correct
        /// realtime data by default (it does colours set through its JSON API), so <see cref="WledClient"/> sets this to match.
        /// </summary>
        public double Gamma
        {
            get => _gamma;
            set
            {
                _gamma = value;
                _levels = CreateLevels(value);
            }
        }

        /// <summary>
        /// The dimmest level (0-255, as sent, after gamma correction) a lit LED is sent at, or 0 to send every level as it is.
        /// Very dim LEDs can't show colours properly (they tend to look red), so an LED that would be dimmer than
        /// <see cref="SwitchOffBelow"/> is switched off, and the rest are brightened, keeping their colour, on a sliding scale:
        /// the dimmest of them up to this level, and less and less up to no change at twice this level.
        /// </summary>
        public float DimmestLevel { get; set; }

        /// <summary>
        /// Where LEDs are switched off rather than brightened up to <see cref="DimmestLevel"/>, as a fraction of it (0-1).
        /// </summary>
        public float SwitchOffBelow { get; set; } = 0.25f;

        public DdpClient(string host)
        {
            if (System.Net.IPEndPoint.TryParse(host, out var address))
            {
                if (address.Port == 0)
                    address.Port = DDP_PORT;

                // Do not bind to the remote IP — create a client and connect to remote endpoint
                _UpdClient = new UdpClient();
                _UpdClient.Connect(address.Address, address.Port);
            }
            else
            {
                _UpdClient = new UdpClient();
                _UpdClient.Connect(host, DDP_PORT);
            }
        }

        /// <summary>
        /// What each colour value (0-255) is sent as with gamma correction, not yet rounded: WLED's gamma table (which
        /// rounds these) for colours set through its JSON API is the same curve.
        /// </summary>
        private static float[] CreateLevels(double gamma)
        {
            var levels = new float[256];
            for (var i = 0; i < 256; i++)
            {
                levels[i] = (float)(Math.Pow(i / 255.0, gamma) * 255.0);
            }
            return levels;
        }

        /// <summary>
        /// Sends a whole frame (the colour of every LED, the first one being LED <paramref name="firstLed"/>) and tells WLED
        /// to display it. It's at most a couple of packets, and resending everything means a lost packet can't leave LEDs
        /// showing the wrong colour.
        /// </summary>
        public async Task SendFrame(ReadOnlyMemory<RGBValue> colours, int firstLed = 0)
        {
            await _sendLock.WaitAsync();
            try
            {
                NextFrameNumber();
                for (var start = 0; start < colours.Length; start += MAX_PIXELS)
                {
                    var count = Math.Min(MAX_PIXELS, colours.Length - start);
                    var length = WritePacket(colours.Slice(start, count), firstLed + start, push: start + count >= colours.Length);
                    await _UpdClient.SendAsync(_packet.AsMemory(0, length));
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Sends just the given LEDs (each run of consecutive LEDs in its own packet) and tells WLED to display them.
        /// </summary>
        /// <param name="ledUpdates">Assumes ordered by index</param>
        public async Task SendPixels(LedUpdate[] ledUpdates)
        {
            // Group the led updates into groups of either max pixels or contiguous indices
            var ledUpdateGroups = GroupLedUpdates(ledUpdates);
            if (ledUpdateGroups.Count == 0)
            {
                return;
            }

            await _sendLock.WaitAsync();
            try
            {
                NextFrameNumber();
                for (var groupIndex = 0; groupIndex < ledUpdateGroups.Count; groupIndex++)
                {
                    var updateGroup = ledUpdateGroups[groupIndex];
                    // If this is the final packet, set the PUSH flag so this frame gets displayed
                    var length = WritePacket(updateGroup.Select(u => u.NewColour).ToArray(), updateGroup[0].LedIndex,
                                             push: groupIndex == ledUpdateGroups.Count - 1);
                    await _UpdClient.SendAsync(_packet.AsMemory(0, length));
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void NextFrameNumber()
        {
            // The sequence number is 4 bits, 1-15 (0 means it isn't used)
            _frameNumber++;
            if (_frameNumber >= 16)
            {
                _frameNumber = 1;
            }
        }

        /// <summary>Writes one packet into <see cref="_packet"/> and returns its length.</summary>
        private int WritePacket(ReadOnlyMemory<RGBValue> colours, int firstLed, bool push)
        {
            var span = colours.Span;
            var dataLen = span.Length * 3;
            var packet = _packet;

            // Header
            packet[0] = push ? (byte)(VER1 | PUSH) : VER1;
            packet[1] = _frameNumber;
            packet[2] = DATATYPE;
            packet[3] = DESTINATIONID;

            // Where the data starts, 4 bytes from 32bit number. It's an offset in bytes, not LEDs (WLED divides it by 3).
            var offset = firstLed * 3;
            packet[4] = (byte)(offset >> 24);
            packet[5] = (byte)(offset >> 16);
            packet[6] = (byte)(offset >> 8);
            packet[7] = (byte)(offset);

            // Length, 2 bytes from 16bit number
            packet[8] = (byte)(dataLen >> 8);
            packet[9] = (byte)(dataLen);

            // Data
            var levels = _levels;
            var dimmest = Math.Clamp(DimmestLevel, 0, 255);
            var switchOffBelow = Math.Clamp(SwitchOffBelow, 0.01f, 1) * dimmest;
            for (var i = 0; i < span.Length; i++)
            {
                var colour = span[i];
                float red = levels[colour.Red], green = levels[colour.Green], blue = levels[colour.Blue];
                if (dimmest > 0)
                {
                    KeepAboveDimmest(firstLed + i, ref red, ref green, ref blue, dimmest, switchOffBelow);
                }
                var o = HEADER_LEN + i * 3;
                packet[o] = ToByte(red);
                packet[o + 1] = ToByte(green);
                packet[o + 2] = ToByte(blue);
            }
            return HEADER_LEN + dataLen;
        }

        private static byte ToByte(float level) => (byte)Math.Clamp((int)(level + 0.5f), 0, 255);

        /// <summary>
        /// Switches the LED off if it's dimmer than <paramref name="switchOffBelow"/>, otherwise brightens it if it's dimmer
        /// than twice <paramref name="dimmest"/>, keeping its colour: its brightest channel goes from
        /// <paramref name="switchOffBelow"/> to twice <paramref name="dimmest"/> to between <paramref name="dimmest"/> and
        /// twice it. One that's lit stays lit until it's a fifth dimmer than that, so an LED hovering around it doesn't flicker.
        /// </summary>
        private void KeepAboveDimmest(int led, ref float red, ref float green, ref float blue, float dimmest, float switchOffBelow)
        {
            if (led >= _lit.Length)
            {
                Array.Resize(ref _lit, Math.Max(led + 1, _lit.Length * 2));
            }
            var peak = Math.Max(red, Math.Max(green, blue));
            var lit = peak > 0 && peak >= (_lit[led] ? switchOffBelow * 0.8f : switchOffBelow);
            _lit[led] = lit;
            if (!lit)
            {
                red = green = blue = 0;
                return;
            }
            var twiceDimmest = 2 * dimmest;
            if (peak >= twiceDimmest)
            {
                return;
            }
            var level = dimmest + (peak - switchOffBelow) * dimmest / (twiceDimmest - switchOffBelow);
            var scale = Math.Max(dimmest, level) / peak;
            red *= scale;
            green *= scale;
            blue *= scale;
        }

        private static List<List<LedUpdate>> GroupLedUpdates(LedUpdate[] ledUpdates)
        {
            List<List<LedUpdate>> groups = new List<List<LedUpdate>>();
            List<LedUpdate> currentGroup = new List<LedUpdate>();
            foreach (var ledUpdate in ledUpdates)
            {
                if (currentGroup.Count == 0)
                {
                    currentGroup.Add(ledUpdate);
                }
                else
                {
                    var lastLed = currentGroup.Last();
                    if (currentGroup.Count >= MAX_PIXELS || ledUpdate.LedIndex != lastLed.LedIndex + 1)
                    {
                        groups.Add(currentGroup);
                        currentGroup = new List<LedUpdate> { ledUpdate };
                    }
                    else
                    {
                        currentGroup.Add(ledUpdate);
                    }
                }
            }
            if (currentGroup.Count != 0)
            {
                // The last group (which is the one with the PUSH flag)
                groups.Add(currentGroup);
            }
            return groups;
        }

        public void Dispose()
        {
            _UpdClient?.Dispose();
            _sendLock.Dispose();
        }
    }
}
