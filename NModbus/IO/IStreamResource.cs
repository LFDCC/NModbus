using System;
using System.Threading;
using System.Threading.Tasks;

namespace NModbus.IO
{
    /// <summary>
    ///     Represents a serial resource.
    ///     Implementor - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    public interface IStreamResource : IDisposable
    {
        /// <summary>
        ///     Indicates that no timeout should occur.
        /// </summary>
        int InfiniteTimeout { get; }

        /// <summary>
        ///     Gets or sets the number of milliseconds before a timeout occurs when a read operation does not finish.
        /// </summary>
        int ReadTimeout { get; set; }

        /// <summary>
        ///     Gets or sets the number of milliseconds before a timeout occurs when a write operation does not finish.
        /// </summary>
        int WriteTimeout { get; set; }

        /// <summary>
        ///     Purges the receive buffer.
        /// </summary>
        void DiscardInBuffer();

        /// <summary>
        ///     Reads a number of bytes from the input buffer and writes those bytes into a byte array at the specified offset.
        /// </summary>
        /// <param name="buffer">The byte array to write the input to.</param>
        /// <param name="offset">The offset in the buffer array to begin writing.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>The number of bytes read.</returns>
        int Read(byte[] buffer, int offset, int count);

        /// <summary>
        ///     Writes a specified number of bytes to the port from an output buffer, starting at the specified offset.
        /// </summary>
        /// <param name="buffer">The byte array that contains the data to write to the port.</param>
        /// <param name="offset">The offset in the buffer array to begin writing.</param>
        /// <param name="count">The number of bytes to write.</param>
        void Write(byte[] buffer, int offset, int count);

        /// <summary>
        ///     Asynchronously reads a number of bytes from the input buffer.
        /// </summary>
        /// <param name="buffer">The memory buffer to write the input to.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of bytes read.</returns>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Asynchronously writes bytes to the output buffer.
        /// </summary>
        /// <param name="buffer">The memory buffer containing data to write.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    }
}
