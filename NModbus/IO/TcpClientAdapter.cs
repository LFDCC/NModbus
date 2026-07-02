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
            // Use the underlying Socket directly, not NetworkStream. NetworkStream.ReadAsync does
            // not reliably cancel synchronously even on .NET 7+, so a follow-up read sees the
            // previous one as "in progress" and throws InvalidOperationException. The Socket API
            // gives clean cancellation for per-request error isolation.
            var socket = _tcpClient.Client;
            var onTimeout = new Func<Exception>(
                () => new IOException("The read operation timed out.", new SocketException((int)SocketError.TimedOut)));

            int total = 0;
            while (total < buffer.Length)
            {
                int read = await StreamResourceTimeout.ReadWithTimeoutAsync(
                    ct => socket.ReceiveAsync(buffer.Slice(total), SocketFlags.None, ct),
                    socket.ReceiveTimeout,
                    onTimeout,
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                    throw new IOException("Read resulted in 0 bytes returned.");

                total += read;
            }

            return total;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var socket = _tcpClient.Client;
            await StreamResourceTimeout.WriteWithTimeoutAsync(
                async ct => await socket.SendAsync(buffer, SocketFlags.None, ct).ConfigureAwait(false),
                socket.SendTimeout,
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
