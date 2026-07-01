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
            // Socket.ReceiveAsync ignores ReceiveTimeout (unlike the synchronous Receive), so enforce it here.
            // Use the connection-preserving variant: cancelling an in-flight socket read tears the socket down
            // on some runtimes, which would break the transport's retry loop.
            return await StreamResourceTimeout.ReadWithTimeoutPreservingConnectionAsync(
                ct => _socketClient.ReceiveAsync(buffer, SocketFlags.None, ct),
                _socketClient.ReceiveTimeout,
                () => new SocketException((int)SocketError.TimedOut),
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Socket.SendAsync ignores SendTimeout (unlike the synchronous Send), so enforce it here.
            await StreamResourceTimeout.WriteWithTimeoutPreservingConnectionAsync(
                async ct => await _socketClient.SendAsync(buffer, SocketFlags.None, ct).ConfigureAwait(false),
                _socketClient.SendTimeout,
                () => new SocketException((int)SocketError.TimedOut),
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
                DisposableUtility.Dispose(ref _socketClient);
            }
        }
    }
}
