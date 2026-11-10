using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VRCX
{
    /// <summary>
    /// Splits bytes read from the pipe into packets. Each packet ends at a 0x00 byte, a read may hold
    /// part of one packet or several, and empty packets are skipped. Packets are decoded as UTF-8 only
    /// once complete, so multi-byte characters split across reads survive.
    /// </summary>
    public class IPCPacketFramer
    {
        public const int MaxPacketSize = 1024 * 1024;

        private readonly MemoryStream _buffer = new MemoryStream();
        private bool _discarding;

        public List<string> Push(byte[] bytes, int offset, int count)
        {
            var packets = new List<string>();
            var end = offset + count;

            while (offset < end)
            {
                var separator = Array.IndexOf(bytes, (byte)0x00, offset, end - offset);
                var chunkEnd = separator < 0 ? end : separator;
                Append(bytes, offset, chunkEnd - offset);

                if (separator < 0)
                    break;

                if (!_discarding && _buffer.Length > 0)
                    packets.Add(Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length));

                _buffer.SetLength(0);
                _discarding = false;
                offset = separator + 1;
            }

            return packets;
        }

        // A packet larger than MaxPacketSize is dropped up to its separator.
        private void Append(byte[] bytes, int offset, int count)
        {
            if (_discarding || count == 0)
                return;

            if (_buffer.Length + count > MaxPacketSize)
            {
                _buffer.SetLength(0);
                _discarding = true;
                return;
            }

            _buffer.Write(bytes, offset, count);
        }
    }
}
