using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tools_protocol.Network
{
    /// <summary>
    /// Reassembles one TCP direction into NUL-terminated UTF-8 packets. Use a
    /// separate instance for each direction; this class is not thread-safe.
    /// Interior line breaks are preserved. Only one final CRLF, LF or CR is removed.
    /// </summary>
    public sealed class NullTerminatedPacketDecoder
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly int _maxPacketBytes;
        private readonly MemoryStream _pending = new MemoryStream();

        public NullTerminatedPacketDecoder(int maxPacketBytes = 1048576)
        {
            if (maxPacketBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPacketBytes));
            _maxPacketBytes = maxPacketBytes;
        }

        /// <summary>
        /// Returns complete nonempty packets, retaining an incomplete last packet.
        /// A size limit violation or invalid UTF-8 resets the pending packet and throws.
        /// </summary>
        public IList<string> Append(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset > data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > data.Length - offset) throw new ArgumentOutOfRangeException(nameof(count));
            var packets = new List<string>();
            int end = offset + count;
            while (offset < end)
            {
                int delimiter = Array.IndexOf(data, (byte)0, offset, end - offset);
                int length = (delimiter < 0 ? end : delimiter) - offset;
                if (_pending.Length + length > _maxPacketBytes)
                {
                    Reset();
                    throw new InvalidDataException("Le paquet dépasse la limite de " + _maxPacketBytes + " octets.");
                }
                _pending.Write(data, offset, length);
                if (delimiter < 0) break;
                string packet;
                try { packet = StrictUtf8.GetString(_pending.GetBuffer(), 0, (int)_pending.Length); }
                finally { Reset(); }
                if (packet.EndsWith("\r\n", StringComparison.Ordinal))
                    packet = packet.Substring(0, packet.Length - 2);
                else if (packet.EndsWith("\n", StringComparison.Ordinal) ||
                    packet.EndsWith("\r", StringComparison.Ordinal))
                    packet = packet.Substring(0, packet.Length - 1);
                if (packet.Length > 0) packets.Add(packet);
                offset = delimiter + 1;
            }
            return packets;
        }

        public void Reset()
        {
            _pending.SetLength(0);
            _pending.Position = 0;
        }
    }
}
