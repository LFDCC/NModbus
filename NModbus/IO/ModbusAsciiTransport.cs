using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Logging;
using NModbus.Utility;

namespace NModbus.IO
{
    /// <summary>
    ///     Refined Abstraction - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    internal class ModbusAsciiTransport : ModbusSerialTransport, IModbusAsciiTransport
    {
        internal ModbusAsciiTransport(IStreamResource streamResource, IModbusFactory modbusFactory, IModbusLogger logger)
            : base(streamResource, modbusFactory, logger)
        {
            Debug.Assert(streamResource != null, "Argument streamResource cannot be null.");
        }

        public override byte[] BuildMessageFrame(IModbusMessage message)
        {
            var msgFrame = message.MessageFrame;

            var msgFrameAscii = ModbusUtility.GetAsciiBytes(msgFrame);
            var lrcAscii = ModbusUtility.GetAsciiBytes(ModbusUtility.CalculateLrc(msgFrame));
            var nlAscii = Encoding.UTF8.GetBytes(Modbus.NewLine.ToCharArray());

            var frame = new MemoryStream(1 + msgFrameAscii.Length + lrcAscii.Length + nlAscii.Length);
            frame.WriteByte((byte)':');
            frame.Write(msgFrameAscii, 0, msgFrameAscii.Length);
            frame.Write(lrcAscii, 0, lrcAscii.Length);
            frame.Write(nlAscii, 0, nlAscii.Length);

            return frame.ToArray();
        }

        public override bool ChecksumsMatch(IModbusMessage message, byte[] messageFrame)
        {
            return ModbusUtility.CalculateLrc(message.MessageFrame) == messageFrame[messageFrame.Length - 1];
        }

        public override byte[] ReadRequest()
        {
            return ReadRequestResponse();
        }

        public override IModbusMessage ReadResponse<T>()
        {
            return CreateResponse<T>(ReadRequestResponse());
        }

        internal byte[] ReadRequestResponse()
        {
            // read message frame, removing frame start ':'
            string frameHex = StreamResourceUtility.ReadLine(StreamResource).Substring(1);

            // convert hex to bytes
            byte[] frame = ModbusUtility.HexToBytes(frameHex);
            Logger.Trace($"RX: {string.Join(", ", frame)}");

            if (frame.Length < 3)
            {
                throw new IOException("Premature end of stream, message truncated.");
            }

            return frame;
        }

        public override void IgnoreResponse()
        {
            ReadRequestResponse();
        }

        public override async Task<byte[]> ReadRequestAsync(CancellationToken cancellationToken = default)
        {
            return await ReadRequestResponseAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async Task<IModbusMessage> ReadResponseAsync<T>(CancellationToken cancellationToken = default)
        {
            byte[] frame = await ReadRequestResponseAsync(cancellationToken).ConfigureAwait(false);
            return CreateResponse<T>(frame);
        }

        private async Task<byte[]> ReadRequestResponseAsync(CancellationToken cancellationToken = default)
        {
            // Read line asynchronously, removing frame start ':'
            string frameHex = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            frameHex = frameHex.Substring(1);

            // convert hex to bytes
            byte[] frame = ModbusUtility.HexToBytes(frameHex);
            Logger.Trace($"RX: {string.Join(", ", frame)}");

            if (frame.Length < 3)
            {
                throw new IOException("Premature end of stream, message truncated.");
            }

            return frame;
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            var buffer = new byte[1];

            while (true)
            {
                int bytesRead = await StreamResource.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException("End of stream while reading ASCII frame.");
                }

                char c = (char)buffer[0];
                if (c == '\n')
                {
                    break;
                }

                if (c != '\r')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
