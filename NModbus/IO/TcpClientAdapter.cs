using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Unme.Common;

namespace NModbus.IO
{
    /// <summary>
    ///     Concrete Implementor - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    public class TcpClientAdapter : IStreamResource
    {
        private TcpClient _tcpClient;

        public TcpClientAdapter(TcpClient tcpClient)
        {
            Debug.Assert(tcpClient != null, "Argument tcpClient cannot be null.");

            _tcpClient = tcpClient;
        }

        public int InfiniteTimeout => Timeout.Infinite;

        public int ReadTimeout
        {
            get => _tcpClient.GetStream().ReadTimeout;
            set => _tcpClient.GetStream().ReadTimeout = value;
        }

        public int WriteTimeout
        {
            get => _tcpClient.GetStream().WriteTimeout;
            set => _tcpClient.GetStream().WriteTimeout = value;
        }

        public void Write(byte[] buffer, int offset, int size)
        {
            _tcpClient.GetStream().Write(buffer, offset, size);
        }

        public int Read(byte[] buffer, int offset, int size)
        {
            return _tcpClient.GetStream().Read(buffer, offset, size);
        }

        public void DiscardInBuffer()
        {
            _tcpClient.GetStream().Flush();
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // NetworkStream.ReadAsync ignores ReadTimeout (unlike the synchronous Read), so enforce it here.
            //
            // We use cancel-based timeout and FORCE-CLOSE the underlying TcpClient on timeout. The
            // alternative (preserving connection) leaves the in-flight socket read running, which:
            //   1) causes the next request's response to be delivered to the orphaned read instead of
            //      the new one (a race that broke sequential operations against the same socket);
            //   2) serializes the next ReadAsync behind the orphan on .NET 6, since NetworkStream does
            //      not reliably honor cancellation to actually abort the underlying receive.
            // Forcing the socket closed makes the next operation on this transport fail immediately
            // with ObjectDisposedException rather than hanging behind a dead read. The caller is
            // expected to create a new master after such a failure.
            var stream = _tcpClient.GetStream();
            try
            {
                return await StreamResourceTimeout.ReadWithTimeoutAsync(
                    ct => stream.ReadAsync(buffer, ct),
                    stream.ReadTimeout,
                    () => new IOException("The read operation timed out.", new SocketException((int)SocketError.TimedOut)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                // The socket is in an indeterminate state (orphan read may still be pending). Close it
                // so the next operation fails fast rather than hanging behind the dead read.
                try { _tcpClient.Close(); } catch { /* best effort */ }
                throw;
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // NetworkStream.WriteAsync ignores WriteTimeout (unlike the synchronous Write), so enforce it here.
            // Same rationale as ReadAsync — force-close the socket on write timeout.
            var stream = _tcpClient.GetStream();
            try
            {
                await StreamResourceTimeout.WriteWithTimeoutAsync(
                    ct => stream.WriteAsync(buffer, ct),
                    stream.WriteTimeout,
                    () => new IOException("The write operation timed out.", new SocketException((int)SocketError.TimedOut)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                try { _tcpClient.Close(); } catch { /* best effort */ }
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposableUtility.Dispose(ref _tcpClient);
            }
        }
    }
}
