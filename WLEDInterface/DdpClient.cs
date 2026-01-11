using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static WLEDInterface.WledClient;

namespace WLEDInterface
{
    public class DdpClient : IDisposable
    {
        private const int DDP_PORT = 4048;

        private const int HEADER_LEN = 10;
        private const int MAX_PIXELS = 480;
        private const int MAX_DATALEN = MAX_PIXELS * 3;
        private const byte VER1 = 0x40;
        private const byte PUSH = 0x01;
        private const byte DATATYPE = 0x01; // RGB
        private const byte DESTINATIONID = 0x01; // Default output device

        private byte _frameNumber = 0;
        private readonly UdpClient _UpdClient;

        public DdpClient(string host)
        {
            if (System.Net.IPEndPoint.TryParse(host, out var address))
            {
                if (address.Port == 0)
                {
                    address.Port = DDP_PORT;
                }

                _UpdClient = new UdpClient(address);
            }
            else
            {
                _UpdClient = new UdpClient(host, DDP_PORT);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ledUpdates">Assumes ordered by index</param>
        public async Task SendPixels(LedUpdate[] ledUpdates)
        {
            _frameNumber++;
            if (_frameNumber == 16)
            {
                _frameNumber = 1;
            }

            // Group the led updates into groups of either max pixels or contiguous indices
            var ledUpdateGroups = GroupLedUpdates(ledUpdates);

            for (var groupIndex = 0; groupIndex < ledUpdateGroups.Count; groupIndex++)
            {
                var updateGroup = ledUpdateGroups[groupIndex];
                int dataLen = updateGroup.Count * 3;
                byte[] packet = new byte[HEADER_LEN + dataLen];
                // Header
                packet[0] = VER1;
                if (groupIndex == ledUpdateGroups.Count - 1)
                {
                    // If this is the final packet , set the PUSH flag so this frame gets displayed
                    packet[0] |= PUSH;
                }

                packet[1] = _frameNumber;
                packet[2] = DATATYPE;
                packet[3] = DESTINATIONID;

                // Start index, 4 bytes from 32bit number
                int startIndex = updateGroup.First().LedIndex;
                packet[4] = (byte)(startIndex >> 24);
                packet[5] = (byte)(startIndex >> 16);
                packet[6] = (byte)(startIndex >> 8);
                packet[7] = (byte)(startIndex);

                // Length, 2 bytes from 16bit number
                packet[8] = (byte)(dataLen >> 8);
                packet[9] = (byte)(dataLen);

                // Data
                for (int i = 0; i < updateGroup.Count; i++)
                {
                    var ledUpdate = updateGroup[i];
                    packet[HEADER_LEN + i * 3 + 0] = ledUpdate.NewColour.Red;
                    packet[HEADER_LEN + i * 3 + 1] = ledUpdate.NewColour.Green;
                    packet[HEADER_LEN + i * 3 + 2] = ledUpdate.NewColour.Blue;
                }
                // Send the packet
                await _UpdClient.SendAsync(packet, packet.Length);
            }
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
            return groups;
        }

        public void Dispose()
        {
            _UpdClient?.Dispose();
        }
    }
}
