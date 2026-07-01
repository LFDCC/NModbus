using System;
using System.Buffers.Binary;

namespace NModbus.Utility
{
    /// <summary>
    ///     Modbus utility methods.
    /// </summary>
    public static class ModbusUtility
    {
        private static readonly ushort[] CrcTable =
        {
            0X0000, 0XC0C1, 0XC181, 0X0140, 0XC301, 0X03C0, 0X0280, 0XC241,
            0XC601, 0X06C0, 0X0780, 0XC741, 0X0500, 0XC5C1, 0XC481, 0X0440,
            0XCC01, 0X0CC0, 0X0D80, 0XCD41, 0X0F00, 0XCFC1, 0XCE81, 0X0E40,
            0X0A00, 0XCAC1, 0XCB81, 0X0B40, 0XC901, 0X09C0, 0X0880, 0XC841,
            0XD801, 0X18C0, 0X1980, 0XD941, 0X1B00, 0XDBC1, 0XDA81, 0X1A40,
            0X1E00, 0XDEC1, 0XDF81, 0X1F40, 0XDD01, 0X1DC0, 0X1C80, 0XDC41,
            0X1400, 0XD4C1, 0XD581, 0X1540, 0XD701, 0X17C0, 0X1680, 0XD641,
            0XD201, 0X12C0, 0X1380, 0XD341, 0X1100, 0XD1C1, 0XD081, 0X1040,
            0XF001, 0X30C0, 0X3180, 0XF141, 0X3300, 0XF3C1, 0XF281, 0X3240,
            0X3600, 0XF6C1, 0XF781, 0X3740, 0XF501, 0X35C0, 0X3480, 0XF441,
            0X3C00, 0XFCC1, 0XFD81, 0X3D40, 0XFF01, 0X3FC0, 0X3E80, 0XFE41,
            0XFA01, 0X3AC0, 0X3B80, 0XFB41, 0X3900, 0XF9C1, 0XF881, 0X3840,
            0X2800, 0XE8C1, 0XE981, 0X2940, 0XEB01, 0X2BC0, 0X2A80, 0XEA41,
            0XEE01, 0X2EC0, 0X2F80, 0XEF41, 0X2D00, 0XEDC1, 0XEC81, 0X2C40,
            0XE401, 0X24C0, 0X2580, 0XE541, 0X2700, 0XE7C1, 0XE681, 0X2640,
            0X2200, 0XE2C1, 0XE381, 0X2340, 0XE101, 0X21C0, 0X2080, 0XE041,
            0XA001, 0X60C0, 0X6180, 0XA141, 0X6300, 0XA3C1, 0XA281, 0X6240,
            0X6600, 0XA6C1, 0XA781, 0X6740, 0XA501, 0X65C0, 0X6480, 0XA441,
            0X6C00, 0XACC1, 0XAD81, 0X6D40, 0XAF01, 0X6FC0, 0X6E80, 0XAE41,
            0XAA01, 0X6AC0, 0X6B80, 0XAB41, 0X6900, 0XA9C1, 0XA881, 0X6840,
            0X7800, 0XB8C1, 0XB981, 0X7940, 0XBB01, 0X7BC0, 0X7A80, 0XBA41,
            0XBE01, 0X7EC0, 0X7F80, 0XBF41, 0X7D00, 0XBDC1, 0XBC81, 0X7C40,
            0XB401, 0X74C0, 0X7580, 0XB541, 0X7700, 0XB7C1, 0XB681, 0X7640,
            0X7200, 0XB2C1, 0XB381, 0X7340, 0XB101, 0X71C0, 0X7080, 0XB041,
            0X5000, 0X90C1, 0X9181, 0X5140, 0X9301, 0X53C0, 0X5280, 0X9241,
            0X9601, 0X56C0, 0X5780, 0X9741, 0X5500, 0X95C1, 0X9481, 0X5440,
            0X9C01, 0X5CC0, 0X5D80, 0X9D41, 0X5F00, 0X9FC1, 0X9E81, 0X5E40,
            0X5A00, 0X9AC1, 0X9B81, 0X5B40, 0X9901, 0X59C0, 0X5880, 0X9841,
            0X8801, 0X48C0, 0X4980, 0X8941, 0X4B00, 0X8BC1, 0X8A81, 0X4A40,
            0X4E00, 0X8EC1, 0X8F81, 0X4F40, 0X8D01, 0X4DC0, 0X4C80, 0X8C41,
            0X4400, 0X84C1, 0X8581, 0X4540, 0X8701, 0X47C0, 0X4680, 0X8641,
            0X8201, 0X42C0, 0X4380, 0X8341, 0X4100, 0X81C1, 0X8081, 0X4040
        };

        /// <summary>
        ///     Converts four UInt16 values into a IEEE 64 floating point format.
        /// </summary>
        /// <param name="b3">Highest-order ushort value.</param>
        /// <param name="b2">Second-to-highest-order ushort value.</param>
        /// <param name="b1">Second-to-lowest-order ushort value.</param>
        /// <param name="b0">Lowest-order ushort value.</param>
        /// <returns>IEEE 64 floating point value.</returns>
        public static double GetDouble(ushort b3, ushort b2, ushort b1, ushort b0)
        {
            Span<byte> value = stackalloc byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(0), b0);
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(2), b1);
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(4), b2);
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(6), b3);

            return BitConverter.ToDouble(value);
        }

        /// <summary>
        ///     Converts two UInt16 values into a IEEE 32 floating point format.
        /// </summary>
        /// <param name="highOrderValue">High order ushort value.</param>
        /// <param name="lowOrderValue">Low order ushort value.</param>
        /// <returns>IEEE 32 floating point value.</returns>
        public static float GetSingle(ushort highOrderValue, ushort lowOrderValue)
        {
            Span<byte> value = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(0), lowOrderValue);
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(2), highOrderValue);

            return BitConverter.ToSingle(value);
        }

        /// <summary>
        ///     Converts two UInt16 values into a UInt32.
        /// </summary>
        public static uint GetUInt32(ushort highOrderValue, ushort lowOrderValue)
        {
            Span<byte> value = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(0), lowOrderValue);
            BinaryPrimitives.WriteUInt16LittleEndian(value.Slice(2), highOrderValue);

            return BitConverter.ToUInt32(value);
        }

        /// <summary>
        ///     Converts an array of bytes to an ASCII byte array (uppercase hex).
        ///     Each byte becomes two ASCII characters ("X2" format).
        /// </summary>
        /// <param name="numbers">The byte array.</param>
        /// <returns>An array of ASCII byte values.</returns>
        public static byte[] GetAsciiBytes(params byte[] numbers)
        {
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));

            // Single allocation: convert each byte directly to its two ASCII hex chars.
            // Replaces the previous SelectMany(n => n.ToString("X2")).ToArray() + Encoding.GetBytes
            // pattern that allocated a char[] and an intermediate string per byte.
            byte[] result = new byte[numbers.Length * 2];

            for (int i = 0; i < numbers.Length; i++)
            {
                byte b = numbers[i];
                byte hi = (byte)(b >> 4);
                byte lo = (byte)(b & 0x0F);

                result[i * 2]     = (byte)(hi < 10 ? '0' + hi : 'A' + hi - 10);
                result[i * 2 + 1] = (byte)(lo < 10 ? '0' + lo : 'A' + lo - 10);
            }

            return result;
        }

        /// <summary>
        ///     Converts an array of UInt16 to an ASCII byte array (uppercase hex).
        ///     Each ushort becomes four ASCII characters ("X4" format).
        /// </summary>
        /// <param name="numbers">The ushort array.</param>
        /// <returns>An array of ASCII byte values.</returns>
        public static byte[] GetAsciiBytes(params ushort[] numbers)
        {
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));

            byte[] result = new byte[numbers.Length * 4];

            for (int i = 0; i < numbers.Length; i++)
            {
                ushort v = numbers[i];
                byte b3 = (byte)((v >> 12) & 0x0F);
                byte b2 = (byte)((v >> 8)  & 0x0F);
                byte b1 = (byte)((v >> 4)  & 0x0F);
                byte b0 = (byte)(v         & 0x0F);

                int o = i * 4;
                result[o]     = (byte)(b3 < 10 ? '0' + b3 : 'A' + b3 - 10);
                result[o + 1] = (byte)(b2 < 10 ? '0' + b2 : 'A' + b2 - 10);
                result[o + 2] = (byte)(b1 < 10 ? '0' + b1 : 'A' + b1 - 10);
                result[o + 3] = (byte)(b0 < 10 ? '0' + b0 : 'A' + b0 - 10);
            }

            return result;
        }

        /// <summary>
        ///     Converts a network order byte array to an array of UInt16 values in host order.
        /// </summary>
        /// <param name="networkBytes">The network order byte array.</param>
        /// <returns>The host order ushort array.</returns>
        public static ushort[] NetworkBytesToHostUInt16(byte[] networkBytes)
        {
            if (networkBytes == null)
            {
                throw new ArgumentNullException(nameof(networkBytes));
            }

            if (networkBytes.Length % 2 != 0)
            {
                throw new FormatException(Resources.NetworkBytesNotEven);
            }

            ushort[] result = new ushort[networkBytes.Length / 2];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt16BigEndian(networkBytes.AsSpan(i * 2));
            }

            return result;
        }

        /// <summary>
        ///     Converts a hex string to a byte array.
        /// </summary>
        /// <param name="hex">The hex string.</param>
        /// <returns>Array of bytes.</returns>
        public static byte[] HexToBytes(string hex)
        {
            if (hex == null)
            {
                throw new ArgumentNullException(nameof(hex));
            }

            if (hex.Length % 2 != 0)
            {
                throw new FormatException(Resources.HexCharacterCountNotEven);
            }

            // Polyfill for Convert.FromHexString (introduced in .NET 5, not available on netstandard2.1).
            // Input is validated to be a clean (no whitespace, no prefix) even-length string by the checks above.
            byte[] bytes = new byte[hex.Length >> 1];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((HexCharToInt(hex[i * 2]) << 4) | HexCharToInt(hex[i * 2 + 1]));
            }
            return bytes;
        }

        private static int HexCharToInt(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new FormatException(Resources.HexCharacterCountNotEven);
        }

        /// <summary>
        ///     Polyfill for <c>Convert.ToHexString</c> (introduced in .NET 5, not available on netstandard2.1).
        ///     Returns the upper-case hex encoding of <paramref name="bytes"/> with no separators.
        /// </summary>
        public static string BytesToHex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return string.Create(bytes.Length * 2, bytes, (span, state) =>
            {
                for (int i = 0; i < state.Length; i++)
                {
                    state[i].TryFormat(span.Slice(i * 2, 2), out _, "X2");
                }
            });
        }

        /// <summary>
        ///     Calculate Longitudinal Redundancy Check.
        /// </summary>
        /// <param name="data">The data used in LRC.</param>
        /// <returns>LRC value.</returns>
        public static byte CalculateLrc(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            byte lrc = 0;

            foreach (byte b in data)
            {
                lrc += b;
            }

            lrc = (byte)((lrc ^ 0xFF) + 1);

            return lrc;
        }

        /// <summary>
        ///     Calculate Cyclical Redundancy Check.
        /// </summary>
        /// <param name="data">The data used in CRC.</param>
        /// <returns>CRC value.</returns>
        public static byte[] CalculateCrc(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            ushort crc = CalculateCrc(data.AsSpan());
            byte[] result = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(result, crc);
            return result;
        }

        /// <summary>
        ///     Calculate Cyclical Redundancy Check from a span of bytes.
        ///     Uses the same table-based algorithm as the byte[] overload for performance.
        /// </summary>
        /// <param name="data">The data used in CRC.</param>
        /// <returns>CRC value as a ushort.</returns>
        public static ushort CalculateCrc(ReadOnlySpan<byte> data)
        {
            ushort crc = ushort.MaxValue;

            foreach (byte b in data)
            {
                byte tableIndex = (byte)(crc ^ b);
                crc >>= 8;
                crc ^= CrcTable[tableIndex];
            }

            return crc;
        }

        /// <summary>
        ///     Writes a CRC value into a destination span in little-endian byte order.
        /// </summary>
        /// <param name="destination">The destination span (must be at least 2 bytes).</param>
        /// <param name="crc">The CRC value to write.</param>
        public static void WriteCrc(Span<byte> destination, ushort crc)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, crc);
        }

        /// <summary>
        ///     Reads a UInt16 value from a span in big-endian byte order.
        /// </summary>
        /// <param name="source">The source span (must be at least 2 bytes).</param>
        /// <returns>The UInt16 value in host byte order.</returns>
        public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(source);
        }

        /// <summary>
        ///     Writes a UInt16 value into a span in big-endian byte order.
        /// </summary>
        /// <param name="destination">The destination span (must be at least 2 bytes).</param>
        /// <param name="value">The UInt16 value to write.</param>
        public static void WriteUInt16BigEndian(Span<byte> destination, ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }

        /// <summary>
        ///     Converts big-endian bytes into host-order UInt16 values.
        /// </summary>
        /// <param name="source">The source span of big-endian bytes (length must be even).</param>
        /// <param name="destination">The destination span for host-order UInt16 values.</param>
        public static void NetworkBytesToHostUInt16(ReadOnlySpan<byte> source, Span<ushort> destination)
        {
            if (source.Length % 2 != 0)
            {
                throw new FormatException(Resources.NetworkBytesNotEven);
            }

            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(i * 2));
            }
        }

        /// <summary>
        ///     Converts four bytes from a span into an IEEE 64 floating point format.
        ///     Bytes are interpreted in little-endian order (low-order first).
        /// </summary>
        /// <param name="data">The source span (must be at least 8 bytes).</param>
        /// <returns>IEEE 64 floating point value.</returns>
        public static double GetDouble(ReadOnlySpan<byte> data)
        {
            return BitConverter.ToDouble(data);
        }

        /// <summary>
        ///     Converts bytes from a span into an IEEE 32 floating point format.
        ///     Bytes are interpreted in little-endian order (low-order first).
        /// </summary>
        /// <param name="data">The source span (must be at least 4 bytes).</param>
        /// <returns>IEEE 32 floating point value.</returns>
        public static float GetSingle(ReadOnlySpan<byte> data)
        {
            return BitConverter.ToSingle(data);
        }

        /// <summary>
        ///     Converts bytes from a span into a UInt32.
        ///     Bytes are interpreted in little-endian order (low-order first).
        /// </summary>
        /// <param name="data">The source span (must be at least 4 bytes).</param>
        /// <returns>UInt32 value.</returns>
        public static uint GetUInt32(ReadOnlySpan<byte> data)
        {
            return BitConverter.ToUInt32(data);
        }
    }
}
