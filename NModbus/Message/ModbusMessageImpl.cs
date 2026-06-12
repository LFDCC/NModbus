using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using NModbus.Data;

namespace NModbus.Message
{
    /// <summary>
    ///     Class holding all implementation shared between two or more message types.
    ///     Interfaces expose subsets of type specific implementations.
    /// </summary>
    internal class ModbusMessageImpl
    {
        // smallest supported message frame size (sans checksum)
        private const int MinimumFrameSize = 2;


        public ModbusMessageImpl()
        {
        }

        public ModbusMessageImpl(byte slaveAddress, byte functionCode)
        {
            SlaveAddress = slaveAddress;
            FunctionCode = functionCode;
        }

        public byte? ByteCount { get; set; }

        public byte? ExceptionCode { get; set; }

        public ushort TransactionId { get; set; }

        public byte FunctionCode { get; set; }

        public ushort? NumberOfPoints { get; set; }

        public byte SlaveAddress { get; set; }

        public ushort? StartAddress { get; set; }

        public ushort? SubFunctionCode { get; set; }

        public IModbusMessageDataCollection Data { get; set; }

        public byte[] MessageFrame
        {
            get
            {
                var pdu = ProtocolDataUnit;
                var frame = new MemoryStream(1 + pdu.Length);

                frame.WriteByte(SlaveAddress);
                frame.Write(pdu, 0, pdu.Length);

                return frame.ToArray();
            }
        }

        public byte[] ProtocolDataUnit
        {
            get
            {
                List<byte> pdu = new List<byte>();

                pdu.Add(FunctionCode);

                if (ExceptionCode.HasValue)
                {
                    pdu.Add(ExceptionCode.Value);
                }

                if (SubFunctionCode.HasValue)
                {
                    pdu.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)SubFunctionCode.Value)));
                }

                if (StartAddress.HasValue)
                {
                    pdu.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)StartAddress.Value)));
                }

                if (NumberOfPoints.HasValue)
                {
                    pdu.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)NumberOfPoints.Value)));
                }

                if (ByteCount.HasValue)
                {
                    pdu.Add(ByteCount.Value);
                }

                if (Data != null)
                {
                    pdu.AddRange(Data.NetworkBytes);
                }

                return pdu.ToArray();
            }
        }

        public void Initialize(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame), "Argument frame cannot be null.");
            }

            if (frame.Length < MinimumFrameSize)
            {
                string msg = $"Message frame must contain at least {MinimumFrameSize} bytes of data.";
                throw new FormatException(msg);
            }

            SlaveAddress = frame[0];
            FunctionCode = frame[1];
        }

        /// <summary>
        ///     Gets the length of the Protocol Data Unit (PDU) without allocating a byte array.
        /// </summary>
        public int ProtocolDataUnitLength
        {
            get
            {
                int length = 1; // FunctionCode

                if (ExceptionCode.HasValue)
                    length += 1;

                if (SubFunctionCode.HasValue)
                    length += 2;

                if (StartAddress.HasValue)
                    length += 2;

                if (NumberOfPoints.HasValue)
                    length += 2;

                if (ByteCount.HasValue)
                    length += 1;

                if (Data != null)
                    length += Data.NetworkBytes.Length;

                return length;
            }
        }

        /// <summary>
        ///     Gets the length of the complete message frame (slave address + PDU) without allocating a byte array.
        /// </summary>
        public int MessageFrameLength => 1 + ProtocolDataUnitLength;

        /// <summary>
        ///     Writes the Protocol Data Unit directly into the destination span using BinaryPrimitives.
        ///     Returns the number of bytes written.
        /// </summary>
        /// <param name="destination">The destination span. Must be at least <see cref="ProtocolDataUnitLength"/> bytes.</param>
        /// <returns>The number of bytes written.</returns>
        public int WriteProtocolDataUnit(Span<byte> destination)
        {
            int offset = 0;

            destination[offset++] = FunctionCode;

            if (ExceptionCode.HasValue)
            {
                destination[offset++] = ExceptionCode.Value;
            }

            if (SubFunctionCode.HasValue)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset), SubFunctionCode.Value);
                offset += 2;
            }

            if (StartAddress.HasValue)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset), StartAddress.Value);
                offset += 2;
            }

            if (NumberOfPoints.HasValue)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset), NumberOfPoints.Value);
                offset += 2;
            }

            if (ByteCount.HasValue)
            {
                destination[offset++] = ByteCount.Value;
            }

            if (Data != null)
            {
                Data.NetworkBytes.CopyTo(destination.Slice(offset));
                offset += Data.NetworkBytes.Length;
            }

            return offset;
        }

        /// <summary>
        ///     Writes the complete message frame (slave address + PDU) directly into the destination span.
        ///     Returns the number of bytes written.
        /// </summary>
        /// <param name="destination">The destination span. Must be at least <see cref="MessageFrameLength"/> bytes.</param>
        /// <returns>The number of bytes written.</returns>
        public int WriteMessageFrame(Span<byte> destination)
        {
            destination[0] = SlaveAddress;
            int pduBytes = WriteProtocolDataUnit(destination.Slice(1));
            return 1 + pduBytes;
        }
    }
}
