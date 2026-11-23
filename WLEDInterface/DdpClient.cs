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

        /// <summary>
        /// Applied to every colour value before it's sent, or null to send them unchanged. WLED doesn't gamma correct realtime
        /// data by default (it does colours set through its JSON API), so <see cref="WledClient"/> sets this to match.
        /// </summary>
        public byte[]? GammaTable { get; set; }

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

        /// <summary>The same lookup table WLED uses for gamma correction.</summary>
        public static byte[] CreateGammaTable(double gamma)
        {
            var table = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                table[i] = (byte)Math.Clamp((int)(Math.Pow(i / 255.0, gamma) * 255.0 + 0.5), 0, 255);
            }
            return table;
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
            var gamma = GammaTable;
            for (var i = 0; i < span.Length; i++)
            {
                var colour = span[i];
                var o = HEADER_LEN + i * 3;
                if (gamma != null)
                {
                    packet[o] = gamma[colour.Red];
                    packet[o + 1] = gamma[colour.Green];
                    packet[o + 2] = gamma[colour.Blue];
                }
                else
                {
                    packet[o] = colour.Red;
                    packet[o + 1] = colour.Green;
                    packet[o + 2] = colour.Blue;
                }
            }
            return HEADER_LEN + dataLen;
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
