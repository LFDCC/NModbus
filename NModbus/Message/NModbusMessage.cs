using System;

namespace NModbus.Message
{
    /// <summary>
    ///     Interface for a Modbus message pair containing both request and response.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    public interface INModbusMessagePair<out TRequest, out TResponse>
        where TRequest : IModbusMessage
        where TResponse : IModbusMessage
    {
        /// <summary>The request message.</summary>
        TRequest Request { get; }

        /// <summary>The response message.</summary>
        TResponse Response { get; }

        /// <summary>Hex string representation of the request frame.</summary>
        string RequestHex { get; }

        /// <summary>Hex string representation of the response frame.</summary>
        string ResponseHex { get; }

        /// <summary>Raw request frame bytes.</summary>
        byte[] RequestFrame { get; }

        /// <summary>Raw response frame bytes.</summary>
        byte[] ResponseFrame { get; }

        /// <summary>Time when the request was sent.</summary>
        DateTime Timestamp { get; }

        /// <summary>Time elapsed for the request-response round trip.</summary>
        TimeSpan Elapsed { get; }
    }

    /// <summary>
    ///     A Modbus message pair containing both request and response with hex frame representations.
    ///     Hex strings are computed lazily from stored frame bytes.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    public class NModbusMessage<TRequest, TResponse> : INModbusMessagePair<TRequest, TResponse>
        where TRequest : IModbusMessage
        where TResponse : IModbusMessage
    {
        private string _requestHex;
        private string _responseHex;

        /// <summary>The request message.</summary>
        public TRequest Request { get; set; }

        /// <summary>The response message.</summary>
        public TResponse Response { get; set; }

        /// <summary>Raw request frame bytes.</summary>
        public byte[] RequestFrame { get; set; }

        /// <summary>Raw response frame bytes.</summary>
        public byte[] ResponseFrame { get; set; }

        /// <summary>Hex string of the request frame (computed lazily, cached).</summary>
        public string RequestHex
        {
            get => _requestHex ??= FormatHex(RequestFrame);
            set => _requestHex = value;
        }

        /// <summary>Hex string of the response frame (computed lazily, cached).</summary>
        public string ResponseHex
        {
            get => _responseHex ??= FormatHex(ResponseFrame);
            set => _responseHex = value;
        }

        /// <summary>Time when the request was sent.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Time elapsed for the request-response round trip.</summary>
        public TimeSpan Elapsed { get; set; }

        private static string FormatHex(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return string.Empty;
            var hex = Convert.ToHexString(frame);
            return string.Create(hex.Length + hex.Length / 2, hex, (dst, src) =>
            {
                int di = 0;
                for (int si = 0; si < src.Length; si += 2)
                {
                    if (di > 0) dst[di++] = ' ';
                    dst[di++] = src[si];
                    dst[di++] = src[si + 1];
                }
            });
        }
    }
}
