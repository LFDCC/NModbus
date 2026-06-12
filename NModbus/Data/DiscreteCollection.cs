using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace NModbus.Data
{
    /// <summary>
    ///     Collection of discrete values.
    /// </summary>
    public class DiscreteCollection : Collection<bool>, IModbusMessageDataCollection
    {
        /// <summary>
        ///     Number of bits per byte.
        /// </summary>
        private const int BitsPerByte = 8;
        private readonly List<bool> _discretes;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiscreteCollection" /> class.
        /// </summary>
        public DiscreteCollection()
            : this(new List<bool>())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiscreteCollection" /> class.
        /// </summary>
        /// <param name="bits">Array for discrete collection.</param>
        public DiscreteCollection(params bool[] bits)
            : this((IList<bool>)bits)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiscreteCollection" /> class.
        /// </summary>
        /// <param name="bytes">Array for discrete collection.</param>
        public DiscreteCollection(params byte[] bytes)
            : this()
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            _discretes.Capacity = bytes.Length * BitsPerByte;

            foreach (byte b in bytes)
            {
                _discretes.Add((b & 1) == 1);
                _discretes.Add((b & 2) == 2);
                _discretes.Add((b & 4) == 4);
                _discretes.Add((b & 8) == 8);
                _discretes.Add((b & 16) == 16);
                _discretes.Add((b & 32) == 32);
                _discretes.Add((b & 64) == 64);
                _discretes.Add((b & 128) == 128);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiscreteCollection" /> class.
        /// </summary>
        /// <param name="bits">List for discrete collection.</param>
        public DiscreteCollection(IList<bool> bits)
            : this(new List<bool>(bits))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiscreteCollection" /> class.
        /// </summary>
        /// <param name="bits">List for discrete collection.</param>
        internal DiscreteCollection(List<bool> bits)
            : base(bits)
        {
            Debug.Assert(bits != null, "Discrete bits is null.");
            _discretes = bits;
        }

        /// <summary>
        ///     Gets the network bytes.
        /// </summary>
        public byte[] NetworkBytes
        {
            get
            {
                byte[] bytes = new byte[ByteCount];

                for (int index = 0; index < _discretes.Count; index++)
                {
                    if (_discretes[index])
                    {
                        bytes[index / BitsPerByte] |= (byte)(1 << (index % BitsPerByte));
                    }
                }

                return bytes;
            }
        }

        /// <summary>
        ///     Gets the byte count.
        /// </summary>
        public byte ByteCount => (byte)((Count + 7) / 8);

        /// <summary>
        ///     Returns a <see cref="T:System.String" /> that represents the current <see cref="T:System.Object" />.
        /// </summary>
        /// <returns>
        ///     A <see cref="T:System.String" /> that represents the current <see cref="T:System.Object" />.
        /// </returns>
        public override string ToString()
        {
            return string.Concat("{", string.Join(", ", this.Select(discrete => discrete ? "1" : "0").ToArray()), "}");
        }

        /// <summary>
        ///     Packs discrete bool values into bytes directly into the destination span.
        ///     Same packing logic as the <see cref="NetworkBytes"/> property.
        /// </summary>
        /// <param name="destination">The destination span. Must be at least <see cref="ByteCount"/> bytes long.</param>
        public void WriteNetworkBytes(Span<byte> destination)
        {
            destination.Slice(0, ByteCount).Clear();

            for (int index = 0; index < _discretes.Count; index++)
            {
                if (_discretes[index])
                {
                    destination[index / BitsPerByte] |= (byte)(1 << (index % BitsPerByte));
                }
            }
        }

        /// <summary>
        ///     Returns a new array containing the first <paramref name="count"/> discrete values.
        ///     Avoids LINQ Take().ToArray() overhead.
        /// </summary>
        public bool[] TakeToArray(int count)
        {
            if (count > _discretes.Count) count = _discretes.Count;
            var result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = _discretes[i];
            return result;
        }
    }
}
