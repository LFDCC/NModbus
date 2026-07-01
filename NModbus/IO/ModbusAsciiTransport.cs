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
        /// <summary>Reusable read buffer for ReadLineAsync. Sized to fit a full ASCII frame in a single
        /// underlying async read, eliminating per-byte syscall overhead.</summary>
        private readonly byte[] _readBuffer = new byte[1024];

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

        // Maximum ASCII frame length — see StreamResourceUtility.MaxAsciiFrameLength for rationale
        private const int MaxAsciiFrameLength = 512;

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            // Pre-size to the max frame size so we never reallocate.
            var sb = new StringBuilder(MaxAsciiFrameLength);

            int bufferPos = 0;
            int bufferLen = 0;

            while (true)
            {
                if (sb.Length > MaxAsciiFrameLength)
                {
                    throw new IOException($"ASCII frame exceeded maximum allowed length ({MaxAsciiFrameLength} chars). Possible DoS or corrupt stream.");
                }

                // Refill the buffer when drained.
                if (bufferPos >= bufferLen)
                {
                    int read;
                    try
                    {
                        read = await StreamResource.ReadAsync(_readBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // ASCII is half-duplex too: a cancelled read leaves the slave's mid-response
                        // bytes stranded in the serial FIFO. Purge so the next UnicastMessage
                        // doesn't start in the middle of a stale frame.
                        StreamResource.DiscardInBuffer();
                        throw;
                    }
                    if (read == 0)
                    {
                        throw new IOException("End of stream while reading ASCII frame.");
                    }
                    bufferLen = read;
                    bufferPos = 0;
                }

                // ASCII frames are 7-bit clean; cast is safe.
                char c = (char)_readBuffer[bufferPos++];
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
