using System;
using System.IO;
using NModbus.Data;

namespace NModbus.Message
{
    public class ReadWriteMultipleRegistersRequest : AbstractModbusMessageWithData<RegisterCollection>, IModbusRequest
    {
        private ReadHoldingInputRegistersRequest _readRequest;
        private WriteMultipleRegistersRequest _writeRequest;

        public ReadWriteMultipleRegistersRequest()
        {
        }

        public ReadWriteMultipleRegistersRequest(
            byte slaveAddress,
            ushort startReadAddress,
            ushort numberOfPointsToRead,
            ushort startWriteAddress,
            RegisterCollection writeData)
            : base(slaveAddress, ModbusFunctionCodes.ReadWriteMultipleRegisters)
        {
            _readRequest = new ReadHoldingInputRegistersRequest(
                ModbusFunctionCodes.ReadHoldingRegisters,
                slaveAddress,
                startReadAddress,
                numberOfPointsToRead);

            _writeRequest = new WriteMultipleRegistersRequest(
                slaveAddress,
                startWriteAddress,
                writeData);

            // TODO: ugly hack for all ModbusSerialTransport-inheritances (ModbusIpTransport would not need this, as it implements complete different BuildMessageFrame)

            // fake ByteCount, Data can hold only even number of bytes
            ByteCount = (ProtocolDataUnit[1]);

            // fake Data, as this modbusmessage does not fit ModbusMessageImpl
            byte[] pdu = ProtocolDataUnit;
            byte[] dataBytes = new byte[pdu.Length - 2];
            Buffer.BlockCopy(pdu, 2, dataBytes, 0, dataBytes.Length);
            Data = new RegisterCollection(dataBytes);
        }

        public byte ByteCount
        {
            get => MessageImpl.ByteCount.Value;
            set => MessageImpl.ByteCount = value;
        }

        public override byte[] ProtocolDataUnit
        {
            get
            {
                byte[] readPdu = _readRequest.ProtocolDataUnit;
                byte[] writePdu = _writeRequest.ProtocolDataUnit;

                // Build the combined PDU directly into a single buffer — no MemoryStream, no ToArray.
                // PDU = [FunctionCode][readPdu without FC][writePdu without FC]
                int readLen = readPdu.Length - 1;
                int writeLen = writePdu.Length - 1;
                byte[] pdu = new byte[1 + readLen + writeLen];

                pdu[0] = FunctionCode;
                Buffer.BlockCopy(readPdu, 1, pdu, 1, readLen);
                Buffer.BlockCopy(writePdu, 1, pdu, 1 + readLen, writeLen);

                return pdu;
            }
        }

        public ReadHoldingInputRegistersRequest ReadRequest => _readRequest;

        public WriteMultipleRegistersRequest WriteRequest => _writeRequest;

        public override int MinimumFrameSize => 11;

        public override string ToString()
        {
            string msg = $"Write {_writeRequest.NumberOfPoints} holding registers starting at address {_writeRequest.StartAddress}, and read {_readRequest.NumberOfPoints} registers starting at address {_readRequest.StartAddress}.";
            return msg;
        }

        public void ValidateResponse(IModbusMessage response)
        {
            var typedResponse = (ReadHoldingInputRegistersResponse)response;
            var expectedByteCount = ReadRequest.NumberOfPoints * 2;

            if (expectedByteCount != typedResponse.ByteCount)
            {
                string msg = $"Unexpected byte count in response. Expected {expectedByteCount}, received {typedResponse.ByteCount}.";
                throw new IOException(msg);
            }
        }

        protected override void InitializeUnique(byte[] frame)
        {
            if (frame.Length < MinimumFrameSize + frame[10])
            {
                throw new FormatException("Message frame does not contain enough bytes.");
            }

            byte[] readFrame = new byte[2 + 4];
            byte[] writeFrame = new byte[frame.Length - 6 + 2];

            readFrame[0] = writeFrame[0] = SlaveAddress;
            readFrame[1] = writeFrame[1] = FunctionCode;

            Buffer.BlockCopy(frame, 2, readFrame, 2, 4);
            Buffer.BlockCopy(frame, 6, writeFrame, 2, frame.Length - 6);

            _readRequest = ModbusMessageFactory.CreateModbusMessage<ReadHoldingInputRegistersRequest>(readFrame);
            _writeRequest = ModbusMessageFactory.CreateModbusMessage<WriteMultipleRegistersRequest>(writeFrame);

            // TODO: ugly hack for all ModbusSerialTransport-inheritances (ModbusIpTransport would not need this, as it implements complete different BuildMessageFrame)

            // fake ByteCount, Data can hold only even number of bytes
            ByteCount = (ProtocolDataUnit[1]);

            // fake Data, as this modbusmessage does not fit ModbusMessageImpl
            byte[] pdu2 = ProtocolDataUnit;
            byte[] dataBytes2 = new byte[pdu2.Length - 2];
            Buffer.BlockCopy(pdu2, 2, dataBytes2, 0, dataBytes2.Length);
            Data = new RegisterCollection(dataBytes2);
        }
    }
}
