using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Extensions;
using NModbus.Logging;
using NModbus.Utility;

namespace NModbus.IO
{
    /// <summary>
    ///     Refined Abstraction - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    internal class ModbusRtuTransport : ModbusSerialTransport, IModbusRtuTransport
    {
        public const int RequestFrameStartLength = 7;

        public const int ResponseFrameStartLength = 4;

        internal ModbusRtuTransport(IStreamResource streamResource, IModbusFactory modbusFactory, IModbusLogger logger)
            : base(streamResource, modbusFactory, logger)
        {
            if (modbusFactory == null) throw new ArgumentNullException(nameof(modbusFactory));
            Debug.Assert(streamResource != null, "Argument streamResource cannot be null.");
        }

        internal int RequestBytesToRead(byte[] frameStart)
        {
            byte functionCode = frameStart[1];

            IModbusFunctionService service = ModbusFactory.GetFunctionServiceOrThrow(functionCode);
                
            return service.GetRtuRequestBytesToRead(frameStart);
        }

        internal int ResponseBytesToRead(byte[] frameStart)
        {
            byte functionCode = frameStart[1];

            if (functionCode > Modbus.ExceptionOffset)
            {
                return 1;
            }

            IModbusFunctionService service = ModbusFactory.GetFunctionServiceOrThrow(functionCode);

            return service.GetRtuResponseBytesToRead(frameStart);
        }

        public virtual byte[] Read(int count)
        {
            byte[] frameBytes = new byte[count];
            int numBytesReadTotal = 0;

            while (numBytesReadTotal != count)
            {
                int numBytesRead = StreamResource.Read(frameBytes, numBytesReadTotal, count - numBytesReadTotal);
                
                if (numBytesRead == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }
                
                numBytesReadTotal += numBytesRead;
            }

            return frameBytes;
        }

        public override byte[] BuildMessageFrame(IModbusMessage message)
        {
            var messageFrame = message.MessageFrame;
            ushort crc = ModbusUtility.CalculateCrc(messageFrame.AsSpan());
            byte[] frame = new byte[messageFrame.Length + 2];

            messageFrame.CopyTo(frame, 0);
            ModbusUtility.WriteCrc(frame.AsSpan(messageFrame.Length), crc);

            return frame;
        }

        public override bool ChecksumsMatch(IModbusMessage message, byte[] messageFrame)
        {
            ushort messageCrc = BitConverter.ToUInt16(messageFrame, messageFrame.Length - 2);
            ushort calculatedCrc = ModbusUtility.CalculateCrc(message.MessageFrame.AsSpan());

            return messageCrc == calculatedCrc;
        }

        public override IModbusMessage ReadResponse<T>()
        {
            byte[] frame = ReadResponse();

            Logger.LogFrameRx(frame);

            return CreateResponse<T>(frame);
        }

        private async Task<byte[]> ReadResponseAsync(CancellationToken cancellationToken = default)
        {
            byte[] frameStart = await ReadAsync(ResponseFrameStartLength, cancellationToken).ConfigureAwait(false);
            byte[] frameEnd = await ReadAsync(ResponseBytesToRead(frameStart), cancellationToken).ConfigureAwait(false);
            byte[] frame = new byte[frameStart.Length + frameEnd.Length];

            frameStart.CopyTo(frame, 0);
            frameEnd.CopyTo(frame, frameStart.Length);

            return frame;
        }

        public override async Task<IModbusMessage> ReadResponseAsync<T>(CancellationToken cancellationToken = default)
        {
            byte[] frame = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogFrameRx(frame);

            return CreateResponse<T>(frame);
        }

        public override async Task<byte[]> ReadRequestAsync(CancellationToken cancellationToken = default)
        {
            byte[] frameStart = await ReadAsync(RequestFrameStartLength, cancellationToken).ConfigureAwait(false);
            byte[] frameEnd = await ReadAsync(RequestBytesToRead(frameStart), cancellationToken).ConfigureAwait(false);
            byte[] frame = new byte[frameStart.Length + frameEnd.Length];

            frameStart.CopyTo(frame, 0);
            frameEnd.CopyTo(frame, frameStart.Length);

            Logger.LogFrameRx(frame);

            return frame;
        }

        private async Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken = default)
        {
            byte[] frameBytes = new byte[count];
            int numBytesReadTotal = 0;

            while (numBytesReadTotal != count)
            {
                int numBytesRead = await StreamResource.ReadAsync(frameBytes.AsMemory(numBytesReadTotal, count - numBytesReadTotal), cancellationToken).ConfigureAwait(false);

                if (numBytesRead == 0)
                {
                    throw new IOException("Read resulted in 0 bytes returned.");
                }

                numBytesReadTotal += numBytesRead;
            }

            return frameBytes;
        }

        private byte[] ReadResponse()
        {
            byte[] frameStart = Read(ResponseFrameStartLength);
            byte[] frameEnd = Read(ResponseBytesToRead(frameStart));
            byte[] frame = new byte[frameStart.Length + frameEnd.Length];

            frameStart.CopyTo(frame, 0);
            frameEnd.CopyTo(frame, frameStart.Length);

            return frame;
        }

        public override void IgnoreResponse()
        {
            byte[] frame = ReadResponse();

            Logger.LogFrameIgnoreRx(frame);
        }

        public override byte[] ReadRequest()
        {
            byte[] frameStart = Read(RequestFrameStartLength);
            byte[] frameEnd = Read(RequestBytesToRead(frameStart));
            byte[] frame = new byte[frameStart.Length + frameEnd.Length];

            frameStart.CopyTo(frame, 0);
            frameEnd.CopyTo(frame, frameStart.Length);

            Logger.LogFrameRx(frame);

            return frame;
        }
    }
}
