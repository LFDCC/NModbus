using System;
using System.IO;
using System.Text;

namespace NModbus.IO
{
    internal static class StreamResourceUtility
    {
        /// <summary>
        /// Maximum ASCII frame length in bytes (excluding the trailing CRLF).
        /// An RTU PDU is at most 253 bytes → 506 hex chars + 2 (slave addr) + 2 (LRC) + 1 (':') = 511.
        /// We use 512 as a safety ceiling to reject obviously malicious or corrupt data.
        /// </summary>
        private const int MaxAsciiFrameLength = 512;

        /// <summary>
        /// Read buffer size — large enough that a typical ASCII frame (≤ 512 chars) is read
        /// in a single underlying syscall, eliminating per-byte Read overhead.
        /// </summary>
        private const int ReadBufferSize = 1024;

        internal static string ReadLine(IStreamResource stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            // Pre-size the builder to the max frame size so we never reallocate.
            var result = new StringBuilder(MaxAsciiFrameLength);
            byte[] buffer = new byte[ReadBufferSize];
            int bufferPos = 0;
            int bufferLen = 0;

            while (true)
            {
                if (result.Length > MaxAsciiFrameLength)
                {
                    throw new IOException($"ASCII frame exceeded maximum allowed length ({MaxAsciiFrameLength} chars). Possible DoS or corrupt stream.");
                }

                // Refill the buffer when drained.
                if (bufferPos >= bufferLen)
                {
                    bufferLen = stream.Read(buffer, 0, buffer.Length);
                    if (bufferLen == 0)
                    {
                        // 0 means EOF on most stream implementations; keep looping until
                        // a newline arrives, matching the original behavior. (The original
                        // code also did `continue` on 0 and only terminated on CRLF.)
                        continue;
                    }
                    bufferPos = 0;
                }

                byte b = buffer[bufferPos++];

                // ASCII frames are 7-bit clean; cast is safe and avoids per-byte char[] allocation
                // that the previous Encoding.UTF8.GetChars(singleByteBuffer).First() caused.
                char c = (char)b;

                if (c == '\n')
                {
                    break;
                }

                if (c != '\r')
                {
                    result.Append(c);
                }
            }

            // The returned string should not include the CRLF (the loop above skips both).
            // result.ToString() already excludes the CRLF because neither \r nor \n is appended.
            return result.ToString();
        }
    }
}
