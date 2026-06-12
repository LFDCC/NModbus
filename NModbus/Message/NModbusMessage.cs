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
    }

    /// <summary>
    ///     A Modbus message pair containing both request and response with hex frame representations.
    ///     Useful for logging, debugging, and protocol analysis.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    public class NModbusMessage<TRequest, TResponse> : INModbusMessagePair<TRequest, TResponse>
        where TRequest : IModbusMessage
        where TResponse : IModbusMessage
    {
        /// <summary>The request message.</summary>
        public TRequest Request { get; set; }

        /// <summary>The response message.</summary>
        public TResponse Response { get; set; }

        /// <summary>Hex string representation of the request frame.</summary>
        public string RequestHex { get; set; }

        /// <summary>Hex string representation of the response frame.</summary>
        public string ResponseHex { get; set; }
    }
}
