using System;
using System.Linq;
using NModbus.Data;
using NModbus.Extensions;

namespace NModbus.Message
{
    public class ReadHoldingInputRegistersResponse : AbstractModbusMessageWithData<RegisterCollection>, IModbusMessage
    {
        public ReadHoldingInputRegistersResponse()
        {
        }

        public ReadHoldingInputRegistersResponse(byte functionCode, byte slaveAddress, RegisterCollection data)
            : base(slaveAddress, functionCode)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            ByteCount = data.ByteCount;
            Data = data;
        }

        public byte ByteCount
        {
            get => MessageImpl.ByteCount.Value;
            set => MessageImpl.ByteCount = value;
        }

        public override int MinimumFrameSize => 3;

        public override string ToString()
        {
            string msg = $"Read {Data.Count} {(FunctionCode == ModbusFunctionCodes.ReadHoldingRegisters ? "holding" : "input")} registers.";
            return msg;
        }

        protected override void InitializeUnique(byte[] frame)
        {
            if (frame.Length < MinimumFrameSize + frame[2])
            {
                throw new FormatException("Message frame does not contain enough bytes.");
            }

            ByteCount = frame[2];

            // Per Modbus spec, register data is always an even number of bytes
            // (2 per 16-bit register). A non-compliant device or wire corruption
            // can produce an odd value, which would fail downstream in
            // NetworkBytesToHostUInt16 with a cryptic message. Fail early with context.
            if ((ByteCount & 1) != 0)
            {
                throw new FormatException(
                    $"ByteCount {ByteCount} in ReadHoldingInputRegisters response is not even (function code 0x{FunctionCode:X2}). Possible corrupt response or non-compliant device.");
            }

            Data = new RegisterCollection(frame.Slice(3, ByteCount).ToArray());
        }
    }
}
