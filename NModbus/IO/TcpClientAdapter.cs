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
            // Use the underlying Socket directly, not NetworkStream. The async APIs of NetworkStream
            // ignore the timeout property and on timeout they don't actually cancel the in-flight
            // receive synchronously — even on .NET 7+ — so a follow-up read sees the previous one as
            // "in progress" and throws InvalidOperationException. The Socket API is the underlying
            // primitive NetworkStream wraps; using it directly gives clean cancellation semantics and
            // allows back-to-back reads after a timeout, which is what the canonical "one TCP
            // connection to a Modbus gateway that proxies multiple slaves" use case needs.
            //
            // ReadTimeout enforcement: the Socket's ReceiveTimeout is NOT observed by ReceiveAsync
            // either, so we apply a linked-token timeout. If the timeout is infinite (0 / -1), the
            // caller's own cancellation token is the only deadline — in that case we don't wrap and
            // OCE propagates as OCE (no spurious "timed out" exception).
            var socket = _tcpClient.Client;
            int receiveTimeout = socket.ReceiveTimeout;

            int total = 0;
            while (total < buffer.Length)
            {
                int read;
                if (IsInfinite(receiveTimeout))
                {
                    read = await socket.ReceiveAsync(buffer.Slice(total), SocketFlags.None, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var timeoutCts = new CancellationTokenSource(receiveTimeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    try
                    {
                        read = await socket.ReceiveAsync(buffer.Slice(total), SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new IOException("The read operation timed out.", new SocketException((int)SocketError.TimedOut));
                    }
                }

                if (read == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }

                total += read;
            }

            return total;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var socket = _tcpClient.Client;
            int sendTimeout = socket.SendTimeout;

            if (IsInfinite(sendTimeout))
            {
                await socket.SendAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                return;
            }

            using var timeoutCts = new CancellationTokenSource(sendTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await socket.SendAsync(buffer, SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new IOException("The write operation timed out.", new SocketException((int)SocketError.TimedOut));
            }
        }

        private static bool IsInfinite(int timeoutMilliseconds)
            => timeoutMilliseconds <= 0 || timeoutMilliseconds == Timeout.Infinite;

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
