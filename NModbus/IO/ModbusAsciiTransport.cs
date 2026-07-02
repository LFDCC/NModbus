using System;
using System.Buffers;
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

            // Decode the hex into a fresh tightly-sized byte[] (caller-owned). The string itself is
            // an unavoidable intermediate on the sync path because StreamResourceUtility.ReadLine is
            // built around a string; the Span overload on HexToBytes only helps the async path.
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
            // ReadLineAsync now returns the decoded bytes directly (it handles ':' stripping and
            // hex decoding internally using pooled buffers).
            byte[] frame = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            Logger.Trace($"RX: {string.Join(", ", frame)}");

            if (frame.Length < 3)
            {
                throw new IOException("Premature end of stream, message truncated.");
            }

            return frame;
        }

        // Maximum ASCII frame length — see StreamResourceUtility.MaxAsciiFrameLength for rationale
        private const int MaxAsciiFrameLength = 512;

        /// <summary>
        ///     Reads one ASCII frame asynchronously into a pooled buffer and returns the decoded
        ///     byte array. The <c>StringBuilder</c>+<c>ToString()</c>+hex-decode
        ///     chain that the old code used has been replaced with a single pooled <c>char[]</c>
        ///     scanned directly for <c>'\n'</c>, then a pooled <c>byte[]</c> written by
        ///     <see cref="ModbusUtility.HexToBytes(ReadOnlySpan{char}, Span{byte})"/>. The public
        ///     return value is a tightly-sized array (caller-owned) so downstream code is unchanged.
        /// </summary>
        private async Task<byte[]> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            char[] charBuffer = ArrayPool<char>.Shared.Rent(MaxAsciiFrameLength);
            try
            {
                int charLen = 0;
                int bufferPos = 0;
                int bufferLen = 0;

                while (true)
                {
                    if (charLen > MaxAsciiFrameLength)
                    {
                        throw new IOException($"ASCII frame exceeded maximum allowed length ({MaxAsciiFrameLength} chars). Possible DoS or corrupt stream.");
                    }

                    // Refill the byte buffer when drained.
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
                        if (charLen >= charBuffer.Length)
                        {
                            // Pool didn't give us enough — the char count guard above (charLen > MaxAsciiFrameLength)
                            // would already have thrown, so this is a pool-bucket round-down edge case. Grow.
                            char[] grown = ArrayPool<char>.Shared.Rent(charBuffer.Length * 2);
                            Array.Copy(charBuffer, 0, grown, 0, charBuffer.Length);
                            ArrayPool<char>.Shared.Return(charBuffer);
                            charBuffer = grown;
                        }
                        charBuffer[charLen++] = c;
                    }
                }

                // The wire frame starts with ':' which the previous code stripped via Substring(1) AFTER
                // building a string. We can avoid the leading ':' here: drop the first char if present.
                int hexStart = (charLen > 0 && charBuffer[0] == ':') ? 1 : 0;
                int hexLen = charLen - hexStart;
                if (hexLen % 2 != 0)
                {
                    throw new IOException($"ASCII frame hex length is not even ({hexLen} chars). Corrupt frame.");
                }

                int byteLen = hexLen / 2;
                byte[] bytes = new byte[byteLen];
                ModbusUtility.HexToBytes(charBuffer.AsSpan(hexStart, hexLen), bytes);
                return bytes;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }
    }
}
