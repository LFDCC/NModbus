using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
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

            // 1 (':') + msgFrameAscii.Length + lrcAscii.Length + 2 ("\r\n").
            // Single allocation + three Span.CopyTo replaces the prior MemoryStream + ToArray()
            // (which was 4 fresh byte[] per call and a 2-pass internal copy).
            int totalLen = 1 + msgFrameAscii.Length + lrcAscii.Length + 2;
            var frame = new byte[totalLen];
            frame[0] = (byte)':';
            msgFrameAscii.AsSpan().CopyTo(frame.AsSpan(1));
            lrcAscii.AsSpan().CopyTo(frame.AsSpan(1 + msgFrameAscii.Length));
            frame[totalLen - 2] = (byte)'\r';
            frame[totalLen - 1] = (byte)'\n';
            return frame;
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

        // Vectorized byte search for the LF terminator. On .NET 8 this JITs down to SIMD
        // (SSE2/AVX2) instructions, dramatically faster than a per-byte loop on long frames.
        private static readonly SearchValues<byte> s_linefeed = System.Buffers.SearchValues.Create(new byte[] { (byte)'\n' });

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

                    // Use SearchValues<byte> to vectorize the LF scan over the remaining buffer bytes.
                    // On frames ≤ 512 bytes this is a single SIMD pass replacing the old per-byte loop.
                    int remaining = bufferLen - bufferPos;
                    int lfIndex = _readBuffer.AsSpan(bufferPos, remaining).IndexOfAny(s_linefeed);

                    if (lfIndex >= 0)
                    {
                        // LF found — append everything before it (skipping \r) and break.
                        for (int i = 0; i < lfIndex; i++)
                        {
                            char c = (char)_readBuffer[bufferPos + i];
                            if (c != '\r')
                                AppendChar(ref charBuffer, ref charLen, c);
                        }
                        bufferPos += lfIndex + 1;
                        break;
                    }

                    // No LF yet — append all non-\r chars then refill.
                    for (int i = 0; i < remaining; i++)
                    {
                        char c = (char)_readBuffer[bufferPos + i];
                        if (c != '\r')
                            AppendChar(ref charBuffer, ref charLen, c);
                    }
                    bufferPos = bufferLen;
                }

                // Drop leading ':' (frame start delimiter)
                int hexStart = (charLen > 0 && charBuffer[0] == ':') ? 1 : 0;
                int hexLen = charLen - hexStart;
                if (hexLen % 2 != 0)
                    throw new IOException($"ASCII frame hex length is not even ({hexLen} chars). Corrupt frame.");

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

        private static void AppendChar(ref char[] buffer, ref int length, char c)
        {
            if (length >= buffer.Length)
            {
                char[] grown = ArrayPool<char>.Shared.Rent(buffer.Length * 2);
                Array.Copy(buffer, 0, grown, 0, buffer.Length);
                ArrayPool<char>.Shared.Return(buffer);
                buffer = grown;
            }
            buffer[length++] = c;
        }
    }
}
