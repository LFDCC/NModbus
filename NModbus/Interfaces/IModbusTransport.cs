using System;
using System.Threading;
using System.Threading.Tasks;
using NModbus.IO;

namespace NModbus
{
    public interface IModbusTransport : IDisposable
    {
        int Retries { get; set; }

        uint RetryOnOldResponseThreshold { get; set; }

        bool SlaveBusyUsesRetryCount { get; set; }

        int WaitToRetryMilliseconds { get; set; }

        int ReadTimeout { get; set; }

        int WriteTimeout { get; set; }

        T UnicastMessage<T>(IModbusMessage message) where T : IModbusMessage, new();

        /// <summary>Sends a broadcast message (address 0) without reading any response.</summary>
        void BroadcastWrite(IModbusMessage message);

        byte[] ReadRequest();

        byte[] BuildMessageFrame(IModbusMessage message);

        void Write(IModbusMessage message);

        IStreamResource StreamResource { get; }

        /// <summary>
        ///     Asynchronously sends a unicast message and returns the response.
        /// </summary>
        Task<T> UnicastMessageAsync<T>(IModbusMessage message, CancellationToken cancellationToken = default)
            where T : IModbusMessage, new();

        /// <summary>
        ///     Asynchronously sends a broadcast message (address 0) without reading any response.
        /// </summary>
        Task BroadcastWriteAsync(IModbusMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Asynchronously reads a request from the stream.
        /// </summary>
        Task<byte[]> ReadRequestAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Asynchronously writes a message to the stream.
        /// </summary>
        Task WriteAsync(IModbusMessage message, CancellationToken cancellationToken = default);
    }
}
