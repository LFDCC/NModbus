using System;
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

            // read header
            var mbapHeader = new byte[6];
            int numBytesRead = 0;

            while (numBytesRead != 6)
            {
                int bRead = streamResource.Read(mbapHeader, numBytesRead, 6 - numBytesRead);

                if (bRead == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }

                numBytesRead += bRead;
            }

            logger.Debug($"MBAP header: {string.Join(", ", mbapHeader)}");
            var frameLength = BinaryPrimitives.ReadUInt16BigEndian(mbapHeader.AsSpan(4));
            logger.Debug($"{frameLength} bytes in PDU.");

            // read message
            var messageFrame = new byte[frameLength];
            numBytesRead = 0;

            while (numBytesRead != frameLength)
            {
                int bRead = streamResource.Read(messageFrame, numBytesRead, frameLength - numBytesRead);

                if (bRead == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }

                numBytesRead += bRead;
            }

            logger.Debug($"PDU: {frameLength}");
            var frame = new byte[6 + frameLength];
            mbapHeader.CopyTo(frame, 0);
            messageFrame.CopyTo(frame, 6);
            logger.LogFrameRx(frame);

            return frame;
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
            response.TransactionId = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(mbapHeader, 0));

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
            // read header
            var mbapHeader = new byte[6];
            int numBytesRead = 0;

            while (numBytesRead != 6)
            {
                int bRead = await StreamResource.ReadAsync(mbapHeader.AsMemory(numBytesRead, 6 - numBytesRead), cancellationToken).ConfigureAwait(false);

                if (bRead == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }

                numBytesRead += bRead;
            }

            Logger.Debug($"MBAP header: {string.Join(", ", mbapHeader)}");
            var frameLength = (ushort)BinaryPrimitives.ReadInt16BigEndian(mbapHeader.AsSpan(4));
            Logger.Debug($"{frameLength} bytes in PDU.");

            // read message
            var messageFrame = new byte[frameLength];
            numBytesRead = 0;

            while (numBytesRead != frameLength)
            {
                int bRead = await StreamResource.ReadAsync(messageFrame.AsMemory(numBytesRead, frameLength - numBytesRead), cancellationToken).ConfigureAwait(false);

                if (bRead == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }

                numBytesRead += bRead;
            }

            Logger.Debug($"PDU: {frameLength}");

            var frame = new byte[mbapHeader.Length + messageFrame.Length];
            mbapHeader.CopyTo(frame, 0);
            messageFrame.CopyTo(frame, mbapHeader.Length);

            Logger.LogFrameRx(frame);

            return frame;
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
