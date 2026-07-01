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
            // Use the connection-preserving variant: cancelling an in-flight socket read tears the socket down
            // on some runtimes, which would break the transport's retry loop.
            var stream = _tcpClient.GetStream();
            return await StreamResourceTimeout.ReadWithTimeoutPreservingConnectionAsync(
                ct => stream.ReadAsync(buffer, ct),
                stream.ReadTimeout,
                () => new IOException("The read operation timed out.", new SocketException((int)SocketError.TimedOut)),
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // NetworkStream.WriteAsync ignores WriteTimeout (unlike the synchronous Write), so enforce it here.
            var stream = _tcpClient.GetStream();
            await StreamResourceTimeout.WriteWithTimeoutPreservingConnectionAsync(
                ct => stream.WriteAsync(buffer, ct),
                stream.WriteTimeout,
                () => new IOException("The write operation timed out.", new SocketException((int)SocketError.TimedOut)),
                cancellationToken).ConfigureAwait(false);
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
