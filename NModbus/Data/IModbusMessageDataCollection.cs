using System;
using System.Diagnostics.CodeAnalysis;

namespace NModbus.Data
{
    /// <summary>
    ///     Modbus message containing data.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public interface IModbusMessageDataCollection
    {
        /// <summary>
        ///     Gets the network bytes.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        byte[] NetworkBytes { get; }

        /// <summary>
        ///     Gets the byte count.
        /// </summary>
        byte ByteCount { get; }

        /// <summary>
        ///     Writes the network bytes directly into the destination span (zero-allocation).
        /// </summary>
        /// <param name="destination">The destination span. Must be at least <see cref="ByteCount"/> bytes long.</param>
        void WriteNetworkBytes(Span<byte> destination);
    }
}
