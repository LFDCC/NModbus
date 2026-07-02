using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Unme.Common;

namespace NModbus.IO
{
    /// <summary>
    ///     Concrete Implementor - http://en.wikipedia.org/wiki/Bridge_Pattern
    ///     This implementation is for sockets that Convert Rs485 to Ethernet.
    /// </summary>
    public class SocketAdapter : IStreamResource
    {
        private Socket _socketClient;

        public SocketAdapter(Socket socketClient)
        {
            Debug.Assert(socketClient != null, "Argument socketClient van not be null");
            _socketClient = socketClient;
        }

        public int InfiniteTimeout => Timeout.Infinite;
        public int ReadTimeout
        {
            get => _socketClient.ReceiveTimeout;
            set => _socketClient.ReceiveTimeout = value;
        }
        public int WriteTimeout
        {
            get => _socketClient.SendTimeout;
            set => _socketClient.SendTimeout = value;
        }
        public void DiscardInBuffer()
        {
            // socket does not hold buffers.
            return;
        }

        public int Read(byte[] buffer, int offset, int size)
        {
            return _socketClient.Receive(buffer, offset, size, 0);
        }

        public void Write(byte[] buffer, int offset, int size)
        {
            _socketClient.Send(buffer, offset, size, 0);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Socket.ReceiveAsync ignores ReceiveTimeout (unlike the synchronous Receive), so enforce
            // it via a linked cancellation token. If the timeout is infinite, the caller's own
            // token is the only deadline — OCE propagates as OCE (no spurious "timed out" exception).
            int receiveTimeout = _socketClient.ReceiveTimeout;

            if (IsInfinite(receiveTimeout))
            {
                return await _socketClient.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            }

            using var timeoutCts = new CancellationTokenSource(receiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                return await _socketClient.ReceiveAsync(buffer, SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new SocketException((int)SocketError.TimedOut);
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int sendTimeout = _socketClient.SendTimeout;

            if (IsInfinite(sendTimeout))
            {
                await _socketClient.SendAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                return;
            }

            using var timeoutCts = new CancellationTokenSource(sendTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _socketClient.SendAsync(buffer, SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new SocketException((int)SocketError.TimedOut);
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
                DisposableUtility.Dispose(ref _socketClient);
            }
        }
    }
}
