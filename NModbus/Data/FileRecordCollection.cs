using NModbus.Unme.Common;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NModbus.Data
{
    public class FileRecordCollection : IModbusMessageDataCollection
    {
        private IReadOnlyList<byte> networkBytes;
        private IReadOnlyList<byte> dataBytes;

        public FileRecordCollection(ushort fileNumber, ushort startingAddress, byte[] data)
        {
            Build(fileNumber, startingAddress, data);
            FileNumber = fileNumber;
            StartingAddress = startingAddress;
        }

        public FileRecordCollection(byte[] messageFrame)
        {
            var fileNumber = BinaryPrimitives.ReadUInt16BigEndian(messageFrame.AsSpan(4));
            var startingAdress = BinaryPrimitives.ReadUInt16BigEndian(messageFrame.AsSpan(6));
            var count = BinaryPrimitives.ReadUInt16BigEndian(messageFrame.AsSpan(8));
            var data = messageFrame.Slice(10, count * 2).ToArray();

            Build(fileNumber, startingAdress, data);
            FileNumber = fileNumber;
            StartingAddress = startingAdress;
        }

        private void Build(ushort fileNumber, ushort startingAddress, byte[] data)
        {
            if (data.Length % 2 != 0)
            {
                throw new FormatException("Number of bytes has to be even");
            }

            var values = new List<byte>
            {
                6, // Reference type, demanded by standard definition
            };
            
            void addAsBytes(int value)
            {
                var buf = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)value);
                values.AddRange(buf);
            }

            addAsBytes(fileNumber);
            addAsBytes(startingAddress);
            addAsBytes(data.Length / 2);
            
            values.AddRange(data);

            dataBytes = data;
            networkBytes = values;
        }

        /// <summary>
        /// The Extended Memory file number
        /// </summary>
        public ushort FileNumber { get; }

        /// <summary>
        /// The starting register address within the file.
        /// </summary>
        public ushort StartingAddress { get; }

        /// <summary>
        ///  The bytes written to the extended memory file.
        /// </summary>
        public IReadOnlyList<byte> DataBytes => dataBytes;

        public byte[] NetworkBytes => networkBytes.ToArray();

        /// <summary>
        ///     Gets the byte count.
        /// </summary>
        public byte ByteCount => (byte)networkBytes.Count;

        /// <summary>
        ///     Writes the network bytes directly into the destination span (zero-allocation).
        /// </summary>
        public void WriteNetworkBytes(Span<byte> destination)
        {
            for (int i = 0; i < networkBytes.Count; i++)
                destination[i] = networkBytes[i];
        }

        /// <summary>
        ///     Returns a <see cref="T:System.String" /> that represents the current <see cref="T:System.Object" />.
        /// </summary>
        /// <returns>
        ///     A <see cref="T:System.String" /> that represents the current <see cref="T:System.Object" />.
        /// </returns>
        public override string ToString()
        {
            return string.Concat("{", string.Join(", ", this.networkBytes.Select(v => v.ToString()).ToArray()), "}");
        }
    }
}
