using System;
using System.Buffers.Binary;
using NModbus.Utility;

namespace NModbus.Extensions
{
    public static class CrcExtensions
    {
        /// <summary>
        /// Determines whether the crc stored in the message matches the calculated crc.
        /// Uses Span-based CRC calculation to avoid allocations.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static bool DoesCrcMatch(this byte[] message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (message.Length < 4)
                throw new ArgumentException("message must be at least four bytes long");

            // Use Span-based CRC (returns ushort, no allocation)
            ushort calculatedCrc = ModbusUtility.CalculateCrc(message.AsSpan(0, message.Length - 2));

            // Get the crc that is stored in the message
            ushort messageCrc = message.GetCRC();

            return calculatedCrc == messageCrc;
        }

        /// <summary>
        /// Gets the CRC of the message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static ushort GetCRC(this byte[] message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (message.Length < 4)
                throw new ArgumentException("message must be at least four bytes long");

            return BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(message.Length - 2));
        }
    }
}
