using System;
using System.Buffers.Binary;

namespace NModbus.Extensions
{
    /// <summary>
    ///     Converts Modbus register arrays (ushort[]) to/from typed values.
    ///     Uses Span + BinaryPrimitives for zero-allocation conversions.
    ///     All multi-register values use big-endian (Modbus standard) byte order:
    ///     high-order register first, low-order register second.
    /// </summary>
    public static class RegisterConverter
    {
        #region 1 Register → 16-bit

        /// <summary>Read 1 register as Int16.</summary>
        public static short ReadInt16(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 1) throw new ArgumentException("Not enough registers");
            return (short)registers[startIndex];
        }

        /// <summary>Read 1 register as UInt16.</summary>
        public static ushort ReadUInt16(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 1) throw new ArgumentException("Not enough registers");
            return registers[startIndex];
        }

        #endregion

        #region 2 Registers → 32-bit

        /// <summary>Read 2 registers as Int32 (big-endian: high register first).</summary>
        public static int ReadInt32(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 2) throw new ArgumentException("Not enough registers");
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), registers[startIndex + 1]);
            return BinaryPrimitives.ReadInt32LittleEndian(buf);
        }

        /// <summary>Read 2 registers as UInt32 (big-endian: high register first).</summary>
        public static uint ReadUInt32(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 2) throw new ArgumentException("Not enough registers");
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), registers[startIndex + 1]);
            return BinaryPrimitives.ReadUInt32LittleEndian(buf);
        }

        /// <summary>Read 2 registers as Float (IEEE 754, big-endian: high register first).</summary>
        public static float ReadFloat(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 2) throw new ArgumentException("Not enough registers");
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), registers[startIndex + 1]);
            return BitConverter.ToSingle(buf);
        }

        #endregion

        #region 4 Registers → 64-bit

        /// <summary>Read 4 registers as Int64 (big-endian: highest register first).</summary>
        public static long ReadInt64(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 4) throw new ArgumentException("Not enough registers");
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), registers[startIndex + 1]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(4), registers[startIndex + 2]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(6), registers[startIndex + 3]);
            return BinaryPrimitives.ReadInt64LittleEndian(buf);
        }

        /// <summary>Read 4 registers as UInt64 (big-endian: highest register first).</summary>
        public static ulong ReadUInt64(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 4) throw new ArgumentException("Not enough registers");
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), registers[startIndex + 1]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(4), registers[startIndex + 2]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(6), registers[startIndex + 3]);
            return BinaryPrimitives.ReadUInt64LittleEndian(buf);
        }

        /// <summary>Read 4 registers as Double (IEEE 754, big-endian: highest register first).</summary>
        public static double ReadDouble(ushort[] registers, int startIndex = 0)
        {
            if (registers.Length < startIndex + 4) throw new ArgumentException("Not enough registers");
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), registers[startIndex + 1]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(4), registers[startIndex + 2]);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(6), registers[startIndex + 3]);
            return BitConverter.ToDouble(buf);
        }

        #endregion

        #region Value → Registers (for writing)

        /// <summary>Convert Int16 to 1 register.</summary>
        public static ushort[] WriteInt16(short value) => new ushort[] { (ushort)value };

        /// <summary>Convert UInt16 to 1 register.</summary>
        public static ushort[] WriteUInt16(ushort value) => new ushort[] { value };

        /// <summary>Convert Int32 to 2 registers (big-endian: high register first).</summary>
        public static ushort[] WriteInt32(int value)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf, value);
            return new ushort[]
            {
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2))
            };
        }

        /// <summary>Convert UInt32 to 2 registers (big-endian: high register first).</summary>
        public static ushort[] WriteUInt32(uint value)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
            return new ushort[]
            {
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2))
            };
        }

        /// <summary>Convert Float to 2 registers (IEEE 754, big-endian).</summary>
        public static ushort[] WriteFloat(float value)
        {
            Span<byte> buf = stackalloc byte[4];
            BitConverter.TryWriteBytes(buf, value);
            return new ushort[]
            {
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2))
            };
        }

        /// <summary>Convert Int64 to 4 registers (big-endian: highest register first).</summary>
        public static ushort[] WriteInt64(long value)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buf, value);
            return new ushort[]
            {
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(4)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(6))
            };
        }

        /// <summary>Convert UInt64 to 4 registers (big-endian: highest register first).</summary>
        public static ushort[] WriteUInt64(ulong value)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
            return new ushort[]
            {
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(4)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(6))
            };
        }

        /// <summary>Convert Double to 4 registers (IEEE 754, big-endian).</summary>
        public static ushort[] WriteDouble(double value)
        {
            Span<byte> buf = stackalloc byte[8];
            BitConverter.TryWriteBytes(buf, value);
            return new ushort[]
            {
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(4)),
                BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(6))
            };
        }

        #endregion
    }
}
