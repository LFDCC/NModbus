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
        /// <remarks>
        ///     This is honored by the synchronous <see cref="Read"/> path on every adapter, and by the
        ///     async path on TCP (<c>NetworkStream</c>) and UDP / raw <c>Socket</c>. For serial
        ///     (<c>SerialPortAdapter</c> in <c>NModbus.Serial</c>) it is ignored on the async path —
        ///     <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> with a cancellation token is the
        ///     reliable async timeout.
        /// </remarks>
        int ReadTimeout { get; set; }

        /// <summary>
        ///     Gets or sets the number of milliseconds before a timeout occurs when a write operation does not finish.
        /// </summary>
        int WriteTimeout { get; set; }

        /// <summary>
        ///     Purges the receive buffer.
        /// </summary>
        /// <remarks>
        ///     For serial transports (RTU/ASCII) the transport automatically calls this on the
        ///     cancellation path of an in-flight read so the next request does not start in the
        ///     middle of a stale frame. TCP/IP and UDP adapters treat this as a no-op.
        /// </remarks>
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
        /// <remarks>
        ///     Cancellation semantics differ per adapter and are independent of <see cref="ReadTimeout"/>:
        ///     <list type="bullet">
        ///         <item>
        ///             <description><b>TCP</b> (<c>TcpClientAdapter</c> / <c>NetworkStream</c>): on .NET
        ///             Framework the underlying socket is force-closed when the token fires; on .NET 5+
        ///             the read is left to finish and the bytes already received stay in the socket
        ///             buffer, so the next <see cref="ReadAsync"/> on the same master may return the
        ///             previous response's tail.</description>
        ///         </item>
        ///         <item>
        ///             <description><b>UDP</b> (<c>UdpClientAdapter</c> / <c>SocketAdapter</c>): cancellation
        ///             truly aborts the in-flight <c>Socket.ReceiveAsync</c>; the socket stays usable.</description>
        ///         </item>
        ///         <item>
        ///             <description><b>RTU / ASCII</b> (<c>SerialPortAdapter</c>): cancellation of an
        ///             in-flight read does not reliably abort the underlying overlapped read on Windows.
        ///             The slave's response bytes may still arrive in the serial FIFO. The
        ///             <c>ModbusRtuTransport</c> and <c>ModbusAsciiTransport</c> compensate by calling
        ///             <see cref="DiscardInBuffer"/> on the cancellation path. Note that
        ///             <c>SerialPort.ReadTimeout</c> is NOT honored on the async path — use
        ///             <paramref name="cancellationToken"/> instead.</description>
        ///         </item>
        ///     </list>
        /// </remarks>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Asynchronously writes bytes to the output buffer.
        /// </summary>
        /// <param name="buffer">The memory buffer containing data to write.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    }
}
