using System;
using System.Threading;
using System.Threading.Tasks;
using NModbus.IO;

namespace NModbus.SerialPortStream
{
    /// <summary>
    /// An adapter for the SerialPortStream class. Useful for getting serial port access on non-Windows devices.
    /// </summary>
    public class SerialPortStreamAdapter : IStreamResource
    {
        private readonly RJCP.IO.Ports.SerialPortStream _serialPortStream;

        public SerialPortStreamAdapter(RJCP.IO.Ports.SerialPortStream serialPortStream)
        {
            _serialPortStream = serialPortStream;
        }

        public int InfiniteTimeout => RJCP.IO.Ports.SerialPortStream.InfiniteTimeout;

        public int ReadTimeout
        {
            get => _serialPortStream.ReadTimeout;
            set => _serialPortStream.ReadTimeout = value;
        }
        public int WriteTimeout
        {
            get => _serialPortStream.WriteTimeout;
            set => _serialPortStream.WriteTimeout = value;
        }

        public void DiscardInBuffer()
        {
            _serialPortStream.DiscardInBuffer();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int result = _serialPortStream.Read(buffer, offset, count);

            if (result == 0)
                throw new TimeoutException();

            return result;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _serialPortStream.Write(buffer, offset, count);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Guard the async read with ReadTimeout: the underlying SerialPortStream.ReadAsync does not
            // reliably observe ReadTimeout, so enforce it explicitly (matching the synchronous path).
            int result = await StreamResourceTimeout.ReadWithTimeoutAsync(
                ct => _serialPortStream.ReadAsync(buffer, ct),
                _serialPortStream.ReadTimeout,
                () => new TimeoutException("The serial read operation timed out."),
                cancellationToken).ConfigureAwait(false);

            if (result == 0)
                throw new TimeoutException();

            return result;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await StreamResourceTimeout.WriteWithTimeoutAsync(
                ct => _serialPortStream.WriteAsync(buffer, ct),
                _serialPortStream.WriteTimeout,
                () => new TimeoutException("The serial write operation timed out."),
                cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _serialPortStream.Dispose();
        }
    }
}
