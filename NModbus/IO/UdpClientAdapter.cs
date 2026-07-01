using System;
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
    public class UdpClientAdapter : IStreamResource
    {
        // strategy for cross platform r/w
        private const int MaxBufferSize = ushort.MaxValue;
        private UdpClient _udpClient;
        private readonly byte[] _buffer = new byte[MaxBufferSize];
        private int _bufferOffset;

        public UdpClientAdapter(UdpClient udpClient)
        {
            if (udpClient == null)
            {
                throw new ArgumentNullException(nameof(udpClient));
            }

            _udpClient = udpClient;
        }

        public int InfiniteTimeout => Timeout.Infinite;

        public int ReadTimeout
        {
            get => _udpClient.Client.ReceiveTimeout;
            set => _udpClient.Client.ReceiveTimeout = value;
        }

        public int WriteTimeout
        {
            get => _udpClient.Client.SendTimeout;
            set => _udpClient.Client.SendTimeout = value;
        }

        public void DiscardInBuffer()
        {
            // no-op
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    "Argument offset must be greater than or equal to 0.");
            }

            if (offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    "Argument offset cannot be greater than the length of buffer.");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "Argument count must be greater than or equal to 0.");
            }

            if (count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "Argument count cannot be greater than the length of buffer minus offset.");
            }

            if (_bufferOffset == 0)
            {
                _bufferOffset = _udpClient.Client.Receive(_buffer);
            }

            if (_bufferOffset < count)
            {
                throw new IOException("Not enough bytes in the datagram.");
            }

            Buffer.BlockCopy(_buffer, 0, buffer, offset, count);
            _bufferOffset -= count;
            Buffer.BlockCopy(_buffer, count, _buffer, 0, _bufferOffset);

            return count;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    "Argument offset must be greater than or equal to 0.");
            }

            if (offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    "Argument offset cannot be greater than the length of buffer.");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "Argument count must be greater than or equal to 0.");
            }

            if (count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "Argument count cannot be greater than the length of buffer minus offset.");
            }

            // Fix: use Send with offset/count directly instead of LINQ Skip/Take/ToArray
            _udpClient.Client.Send(buffer, offset, count, SocketFlags.None);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_bufferOffset == 0)
            {
                // Socket.ReceiveAsync ignores ReceiveTimeout (unlike the synchronous Receive), so enforce it here.
                var result = await StreamResourceTimeout.ReadWithTimeoutAsync(
                    ct => _udpClient.Client.ReceiveAsync(_buffer, SocketFlags.None, ct),
                    _udpClient.Client.ReceiveTimeout,
                    () => new SocketException((int)SocketError.TimedOut),
                    cancellationToken).ConfigureAwait(false);
                _bufferOffset = result;
            }

            int count = buffer.Length;
            if (_bufferOffset < count)
            {
                throw new IOException("Not enough bytes in the datagram.");
            }

            _buffer.AsSpan(0, count).CopyTo(buffer.Span);
            _bufferOffset -= count;
            if (_bufferOffset > 0)
            {
                _buffer.AsSpan(count, _bufferOffset).CopyTo(_buffer);
            }

            return count;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Socket.SendAsync ignores SendTimeout (unlike the synchronous Send), so enforce it here.
            await StreamResourceTimeout.WriteWithTimeoutAsync(
                async ct => await _udpClient.Client.SendAsync(buffer, SocketFlags.None, ct).ConfigureAwait(false),
                _udpClient.Client.SendTimeout,
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
                DisposableUtility.Dispose(ref _udpClient);
            }
        }
    }
}
