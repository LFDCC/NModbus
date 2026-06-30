using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using NModbus.Utility;

namespace NModbus.Data
{
    /// <summary>
    ///     Collection of 16 bit registers.
    /// </summary>
    public class RegisterCollection : Collection<ushort>, IModbusMessageDataCollection
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RegisterCollection" /> class.
        /// </summary>
        public RegisterCollection()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RegisterCollection" /> class.
        /// </summary>
        /// <param name="bytes">Array for register collection.</param>
        public RegisterCollection(byte[] bytes)
            : this((IList<ushort>)ModbusUtility.NetworkBytesToHostUInt16(bytes))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RegisterCollection" /> class.
        /// </summary>
        /// <param name="registers">Array for register collection.</param>
        public RegisterCollection(params ushort[] registers)
            : this((IList<ushort>)registers)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RegisterCollection" /> class.
        /// </summary>
        /// <param name="registers">List for register collection.</param>
        public RegisterCollection(IList<ushort> registers)
            : base(registers.IsReadOnly ? new List<ushort>(registers) : registers)
        {
        }

        public byte[] NetworkBytes
        {
            get
            {
                var result = new byte[ByteCount];
                WriteNetworkBytes(result);
                return result;
            }
        }

        /// <summary>
        ///     Gets the byte count.
        /// </summary>
        public byte ByteCount => (byte)(Count * 2);

        /// <summary>
        ///     Returns a <see cref="T:System.String" /> that represents the current <see cref="T:System.Object" />.
        /// </summary>
        /// <returns>
        ///     A <see cref="T:System.String" /> that represents the current <see cref="T:System.Object" />.
        /// </returns>
        public override string ToString()
        {
            return string.Concat("{", string.Join(", ", this.Select(v => v.ToString()).ToArray()), "}");
        }

        /// <summary>
        ///     Writes all registers in big-endian (network) byte order directly into the destination span.
        /// </summary>
        /// <param name="destination">The destination span. Must be at least <see cref="ByteCount"/> bytes long.</param>
        public void WriteNetworkBytes(Span<byte> destination)
        {
            for (int i = 0; i < Count; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(i * 2), this[i]);
            }
        }

        /// <summary>
        ///     Returns a new array containing the first <paramref name="count"/> registers.
        ///     Avoids LINQ Take().ToArray() overhead.
        /// </summary>
        public ushort[] TakeToArray(int count)
        {
            if (count > Count) count = Count;
            var result = new ushort[count];
            for (int i = 0; i < count; i++)
                result[i] = this[i];
            return result;
        }
    }
}
