using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Logging;
using NModbus.Unme.Common;

namespace NModbus.IO
{
    /// <summary>
    ///     Transport for Internet protocols.
    ///     Refined Abstraction - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    public class ModbusIpTransport : ModbusTransport
    {
        private static readonly object _transactionIdLock = new object();
        private ushort _transactionId;

        public ModbusIpTransport(IStreamResource streamResource, IModbusFactory modbusFactory, IModbusLogger logger)
            : base(streamResource, modbusFactory, logger)
        {
            if (streamResource == null) throw new ArgumentNullException(nameof(streamResource));
        }

        public static byte[] ReadRequestResponse(IStreamResource streamResource, IModbusLogger logger)
        {
            if (streamResource == null) throw new ArgumentNullException(nameof(streamResource));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            // Rent the two scratch buffers (MBAP header + PDU body) from the shared pool — both are
            // returned to the pool before this method hands the combined `frame` back to the caller.
            byte[] mbapHeader = ArrayPool<byte>.Shared.Rent(6);
            byte[] messageFrame = null;
            try
            {
                // Read header
                streamResource.ReadExactly(mbapHeader, 6);

                logger.Debug($"MBAP header: {string.Join(", ", mbapHeader)}");
                var frameLength = BinaryPrimitives.ReadUInt16BigEndian(mbapHeader.AsSpan(4));
                logger.Debug($"{frameLength} bytes in PDU.");

                // Validate MBAP Length field: must be 1..260 (Unit ID + PDU; PDU max = 253+6 padding)
                if (frameLength == 0)
                    throw new IOException($"Invalid MBAP frame length: 0. Connection may be corrupt.");
                if (frameLength > 260)
                    throw new IOException($"Invalid MBAP frame length: {frameLength}. Exceeds maximum allowed (260). Possible DoS or corrupt frame.");

                // Read body
                messageFrame = ArrayPool<byte>.Shared.Rent(frameLength);
                streamResource.ReadExactly(messageFrame, frameLength);

                logger.Debug($"PDU: {frameLength}");
                // Combined frame is the public return value — must be a tight array, not pooled, because
                // CreateMessageAndInitializeTransactionId and downstream consumers take ownership.
                var frame = new byte[6 + frameLength];
                mbapHeader.AsSpan(0, 6).CopyTo(frame);
                messageFrame.AsSpan(0, frameLength).CopyTo(frame.AsSpan(6));
                logger.LogFrameRx(frame);

                return frame;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(mbapHeader);
                if (messageFrame != null) ArrayPool<byte>.Shared.Return(messageFrame);
            }
        }

        public static byte[] GetMbapHeader(IModbusMessage message)
        {
            byte[] header = new byte[7];
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0), message.TransactionId);
            // header[2] and header[3] are protocol ID (0x0000) - already zero
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(message.ProtocolDataUnit.Length + 1));
            header[6] = message.SlaveAddress;

            return header;
        }

        /// <summary>
        ///     Create a new transaction ID.
        /// </summary>
        public virtual ushort GetNewTransactionId()
        {
            lock (_transactionIdLock)
            {
                _transactionId = _transactionId == ushort.MaxValue ? (ushort)1 : ++_transactionId;
            }

            return _transactionId;
        }

        public IModbusMessage CreateMessageAndInitializeTransactionId<T>(byte[] fullFrame)
            where T : IModbusMessage, new()
        {
            byte[] mbapHeader = new byte[6];
            byte[] messageFrame = new byte[fullFrame.Length - 6];
            Array.Copy(fullFrame, 0, mbapHeader, 0, 6);
            Array.Copy(fullFrame, 6, messageFrame, 0, fullFrame.Length - 6);

            IModbusMessage response = CreateResponse<T>(messageFrame);
            response.TransactionId = BinaryPrimitives.ReadUInt16BigEndian(mbapHeader.AsSpan(0));

            return response;
        }

        public override byte[] BuildMessageFrame(IModbusMessage message)
        {
            byte[] header = GetMbapHeader(message);
            byte[] pdu = message.ProtocolDataUnit;
            byte[] frame = new byte[header.Length + pdu.Length];

            header.CopyTo(frame, 0);
            pdu.CopyTo(frame, header.Length);

            return frame;
        }

        public override void Write(IModbusMessage message)
        {
            message.TransactionId = GetNewTransactionId();
            byte[] frame = BuildMessageFrame(message);

            Logger.LogFrameTx(frame);

            StreamResource.Write(frame, 0, frame.Length);
        }

        public override byte[] ReadRequest()
        {
            return ReadRequestResponse(StreamResource, Logger);
        }

        public override IModbusMessage ReadResponse<T>()
        {
            return CreateMessageAndInitializeTransactionId<T>(ReadRequestResponse(StreamResource, Logger));
        }

        public override async Task WriteAsync(IModbusMessage message, CancellationToken cancellationToken = default)
        {
            message.TransactionId = GetNewTransactionId();
            byte[] frame = BuildMessageFrame(message);

            Logger.LogFrameTx(frame);

            await StreamResource.WriteAsync(frame.AsMemory(0, frame.Length), cancellationToken).ConfigureAwait(false);
        }

        public override async Task<IModbusMessage> ReadResponseAsync<T>(CancellationToken cancellationToken = default)
        {
            byte[] frame = await ReadRequestResponseAsync(cancellationToken).ConfigureAwait(false);

            return CreateMessageAndInitializeTransactionId<T>(frame);
        }

        public override async Task<byte[]> ReadRequestAsync(CancellationToken cancellationToken = default)
        {
            return await ReadRequestResponseAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<byte[]> ReadRequestResponseAsync(CancellationToken cancellationToken = default)
        {
            // Rent the two scratch buffers (MBAP header + PDU body) from the shared pool — both are
            // returned to the pool before this method hands the combined `frame` back to the caller.
            byte[] mbapHeader = ArrayPool<byte>.Shared.Rent(6);
            byte[] messageFrame = null;
            try
            {
                // Read header
                await StreamResource.ReadExactlyAsync(mbapHeader, 6, cancellationToken).ConfigureAwait(false);

                Logger.Debug($"MBAP header: {string.Join(", ", mbapHeader)}");
                var frameLength = BinaryPrimitives.ReadUInt16BigEndian(mbapHeader.AsSpan(4));
                Logger.Debug($"{frameLength} bytes in PDU.");

                // Validate MBAP Length field: must be 1..260 (Unit ID + PDU; PDU max = 253+6 padding)
                if (frameLength == 0)
                    throw new IOException($"Invalid MBAP frame length: 0. Connection may be corrupt.");
                if (frameLength > 260)
                    throw new IOException($"Invalid MBAP frame length: {frameLength}. Exceeds maximum allowed (260). Possible DoS or corrupt frame.");

                // Read body
                messageFrame = ArrayPool<byte>.Shared.Rent(frameLength);
                await StreamResource.ReadExactlyAsync(messageFrame, frameLength, cancellationToken).ConfigureAwait(false);

                Logger.Debug($"PDU: {frameLength}");

                // Combined frame is the public return value — must be a tight array, not pooled, because
                // CreateMessageAndInitializeTransactionId and downstream consumers take ownership.
                var frame = new byte[6 + frameLength];
                mbapHeader.AsSpan(0, 6).CopyTo(frame);
                messageFrame.AsSpan(0, frameLength).CopyTo(frame.AsSpan(6));

                Logger.LogFrameRx(frame);

                return frame;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(mbapHeader);
                if (messageFrame != null) ArrayPool<byte>.Shared.Return(messageFrame);
            }
        }

        internal override void OnValidateResponse(IModbusMessage request, IModbusMessage response)
        {
            if (request.TransactionId != response.TransactionId)
            {
                string msg = $"Response was not of expected transaction ID. Expected {request.TransactionId}, received {response.TransactionId}.";
                throw new IOException(msg);
            }
        }

        public override bool OnShouldRetryResponse(IModbusMessage request, IModbusMessage response)
        {
            if (request.TransactionId > response.TransactionId && request.TransactionId - response.TransactionId < RetryOnOldResponseThreshold)
            {
                // This response was from a previous request
                return true;
            }

            return base.OnShouldRetryResponse(request, response);
        }
    }
}
