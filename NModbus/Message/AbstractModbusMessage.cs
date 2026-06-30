using System;
using System.Buffers.Binary;

namespace NModbus.Message
{
    /// <summary>
    ///     Abstract Modbus message.
    /// </summary>
    public abstract class AbstractModbusMessage
    {
        private readonly ModbusMessageImpl _messageImpl;

        /// <summary>
        ///     Abstract Modbus message.
        /// </summary>
        internal AbstractModbusMessage()
        {
            _messageImpl = new ModbusMessageImpl();
        }

        /// <summary>
        ///     Abstract Modbus message.
        /// </summary>
        internal AbstractModbusMessage(byte slaveAddress, byte functionCode)
        {
            _messageImpl = new ModbusMessageImpl(slaveAddress, functionCode);
        }

        public ushort TransactionId
        {
            get => _messageImpl.TransactionId;
            set => _messageImpl.TransactionId = value;
        }

        public byte FunctionCode
        {
            get => _messageImpl.FunctionCode;
            set => _messageImpl.FunctionCode = value;
        }

        public byte SlaveAddress
        {
            get => _messageImpl.SlaveAddress;
            set => _messageImpl.SlaveAddress = value;
        }

        public byte[] MessageFrame => _messageImpl.MessageFrame;

        public virtual byte[] ProtocolDataUnit => _messageImpl.ProtocolDataUnit;

        public abstract int MinimumFrameSize { get; }

        internal ModbusMessageImpl MessageImpl => _messageImpl;

        public void Initialize(byte[] frame)
        {
            if (frame.Length < MinimumFrameSize)
            {
                string msg = $"Message frame must contain at least {MinimumFrameSize} bytes of data.";
                throw new FormatException(msg);
            }

            _messageImpl.Initialize(frame);
            InitializeUnique(frame);
        }

        /// <summary>
        ///     Read a big-endian 16-bit value at the given offset. Zero-allocation span-based replacement
        ///     for <c>(ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(frame, offset))</c>.
        /// </summary>
        protected static ushort ReadUInt16BigEndian(byte[] frame, int offset)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(offset));
        }

        protected abstract void InitializeUnique(byte[] frame);
    }
}
