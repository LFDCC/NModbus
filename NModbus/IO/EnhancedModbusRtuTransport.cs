using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Logging;
using NModbus.Message;
using NModbus.Utility;

namespace NModbus.IO
{
    /// <summary>
    ///     Extended RTU transport that handles variable-length responses such as
    ///     Read Device Identification (function code 0x2B). Use
    ///     <see cref="ModbusFactoryExtensions.CreateRtuMasterWithDeviceId"/> to
    ///     create a master that uses this transport.
    /// </summary>
    internal class EnhancedModbusRtuTransport : ModbusRtuTransport
    {
        private const byte DeviceIdFunctionCode = ModbusFunctionCodes.ReadDeviceIdentification;
        private const byte MeiType = 0x0E;

        public EnhancedModbusRtuTransport(
            IStreamResource streamResource,
            IModbusFactory modbusFactory,
            IModbusLogger logger)
            : base(streamResource, modbusFactory, logger)
        {
        }

        /// <summary>
        ///     Overrides response reading to detect Device ID responses and
        ///     read their variable-length frames correctly.
        /// </summary>
        public override IModbusMessage ReadResponse<T>()
        {
            byte[] frameStart = Read(2); // [SlaveAddress][FunctionCode]

            if (frameStart[1] == DeviceIdFunctionCode)
            {
                byte[] fullFrame = ReadDeviceIdResponseFrame(frameStart);
                Logger.LogFrameRx(fullFrame);
                return CreateResponse<T>(fullFrame);
            }
            else if ((frameStart[1] & 0x80) == 0x80)
            {
                // Error response: [SlaveAddr][FC|0x80][ExceptionCode][CRC][CRC]
                byte exceptionCode = Read(1)[0];
                byte[] crc = Read(2);

                byte[] fullFrame = new byte[5];
                fullFrame[0] = frameStart[0];
                fullFrame[1] = frameStart[1];
                fullFrame[2] = exceptionCode;
                fullFrame[3] = crc[0];
                fullFrame[4] = crc[1];

                Logger.LogFrameRx(fullFrame);
                return CreateResponse<T>(fullFrame);
            }
            else
            {
                byte[] fullFrame = ReadStandardModbusResponse(frameStart);
                Logger.LogFrameRx(fullFrame);
                return CreateResponse<T>(fullFrame);
            }
        }

        public override async Task<IModbusMessage> ReadResponseAsync<T>(CancellationToken cancellationToken = default)
        {
            byte[] frameStart = await ReadAsync(2, cancellationToken).ConfigureAwait(false); // [SlaveAddress][FunctionCode]

            if (frameStart[1] == DeviceIdFunctionCode)
            {
                byte[] fullFrame = await ReadDeviceIdResponseFrameAsync(frameStart, cancellationToken).ConfigureAwait(false);
                Logger.LogFrameRx(fullFrame);
                return CreateResponse<T>(fullFrame);
            }
            else if ((frameStart[1] & 0x80) == 0x80)
            {
                // Error response: [SlaveAddr][FC|0x80][ExceptionCode][CRC][CRC]
                byte exceptionCode = (await ReadAsync(1, cancellationToken).ConfigureAwait(false))[0];
                byte[] crc = await ReadAsync(2, cancellationToken).ConfigureAwait(false);

                byte[] fullFrame = new byte[5];
                fullFrame[0] = frameStart[0];
                fullFrame[1] = frameStart[1];
                fullFrame[2] = exceptionCode;
                fullFrame[3] = crc[0];
                fullFrame[4] = crc[1];

                Logger.LogFrameRx(fullFrame);
                return CreateResponse<T>(fullFrame);
            }
            else
            {
                byte[] fullFrame = await ReadStandardModbusResponseAsync(frameStart, cancellationToken).ConfigureAwait(false);
                Logger.LogFrameRx(fullFrame);
                return CreateResponse<T>(fullFrame);
            }
        }

        /// <summary>
        ///     Reads a standard Modbus response after the first 2 bytes have
        ///     already been consumed.
        /// </summary>
        private byte[] ReadStandardModbusResponse(byte[] frameStart)
        {
            // Pre-allocate a buffer large enough for the largest possible response of the declared
            // function code. Modbus spec caps read-register payloads at 253 bytes, so 260 is a
            // safe upper bound for the variable-length read cases; fixed-length responses are sized
            // exactly. This eliminates the List<byte>+ToArray() allocations the previous code paid.
            byte functionCode = frameStart[1];

            byte[] scratch = ArrayPool<byte>.Shared.Rent(MaxStandardResponseBytes(functionCode));
            try
            {
                int written = 0;
                // Copy the 2-byte frameStart prefix into the scratch buffer.
                scratch[0] = frameStart[0];
                scratch[1] = frameStart[1];
                written = 2;

                switch (functionCode)
                {
                    case 0x01: // Read Coils
                    case 0x02: // Read Discrete Inputs
                    case 0x03: // Read Holding Registers
                    case 0x04: // Read Input Registers
                    case 0x11: // Report Slave ID
                    case 0x17: // Read/Write Multiple Registers
                    {
                        byte byteCount = Read(1)[0];
                        scratch[written++] = byteCount;
                        ReadInto(scratch, written, byteCount);
                        written += byteCount;
                        ReadInto(scratch, written, 2); // CRC
                        written += 2;
                        break;
                    }

                    case 0x05: // Write Single Coil
                    case 0x06: // Write Single Register
                    case 0x0F: // Write Multiple Coils
                    case 0x10: // Write Multiple Registers
                    {
                        ReadInto(scratch, written, 4); // Address + Value/Quantity
                        written += 4;
                        ReadInto(scratch, written, 2); // CRC
                        written += 2;
                        break;
                    }

                    case 0x16: // Mask Write Register
                    {
                        ReadInto(scratch, written, 6); // Address + AND mask + OR mask
                        written += 6;
                        ReadInto(scratch, written, 2); // CRC
                        written += 2;
                        break;
                    }

                    case 0x18: // Read FIFO Queue
                    {
                        byte[] counts = Read(4);
                        scratch[written++] = counts[0];
                        scratch[written++] = counts[1];
                        scratch[written++] = counts[2];
                        scratch[written++] = counts[3];
                        ushort byteCount = (ushort)((counts[0] << 8) | counts[1]);
                        if (byteCount > 2)
                        {
                            ReadInto(scratch, written, byteCount - 2);
                            written += byteCount - 2;
                        }
                        ReadInto(scratch, written, 2); // CRC
                        written += 2;
                        break;
                    }

                    default:
                        throw new IOException(
                            $"EnhancedModbusRtuTransport: unsupported function code 0x{functionCode:X2}");
                }

                byte[] frame = new byte[written];
                Buffer.BlockCopy(scratch, 0, frame, 0, written);
                return frame;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        /// <summary>
        ///     Upper bound on bytes for the standard response of a given function code, including the
        ///     2-byte frameStart prefix and 2-byte CRC trailer.
        /// </summary>
        private static int MaxStandardResponseBytes(byte functionCode)
        {
            switch (functionCode)
            {
                case 0x01:
                case 0x02:
                case 0x03:
                case 0x04:
                case 0x11:
                case 0x17:
                    // 2 (frameStart) + 1 (byteCount) + 253 (max payload) + 2 (CRC)
                    return 260;
                case 0x05:
                case 0x06:
                case 0x0F:
                case 0x10:
                    return 8;
                case 0x16:
                    return 10;
                case 0x18:
                    // 2 (frameStart) + 4 (counts) + 60 (FIFO bytes-2) + 2 (CRC)
                    return 70;
                default:
                    // Unknown function code: caller will throw; allocate a minimal buffer.
                    return 16;
            }
        }

        private void ReadInto(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = StreamResource.Read(buffer, offset + total, count - total);
                if (read == 0)
                    throw new IOException("Read resulted in 0 bytes returned.");
                total += read;
            }
        }

        private async Task<byte[]> ReadStandardModbusResponseAsync(byte[] frameStart, CancellationToken cancellationToken = default)
        {
            byte functionCode = frameStart[1];

            byte[] scratch = ArrayPool<byte>.Shared.Rent(MaxStandardResponseBytes(functionCode));
            try
            {
                int written = 0;
                scratch[0] = frameStart[0];
                scratch[1] = frameStart[1];
                written = 2;

                switch (functionCode)
                {
                    case 0x01: // Read Coils
                    case 0x02: // Read Discrete Inputs
                    case 0x03: // Read Holding Registers
                    case 0x04: // Read Input Registers
                    case 0x11: // Report Slave ID
                    case 0x17: // Read/Write Multiple Registers
                    {
                        byte[] first = await ReadAsync(1, cancellationToken).ConfigureAwait(false);
                        byte byteCount = first[0];
                        scratch[written++] = byteCount;
                        await ReadIntoAsync(scratch, written, byteCount, cancellationToken).ConfigureAwait(false);
                        written += byteCount;
                        await ReadIntoAsync(scratch, written, 2, cancellationToken).ConfigureAwait(false); // CRC
                        written += 2;
                        break;
                    }

                    case 0x05: // Write Single Coil
                    case 0x06: // Write Single Register
                    case 0x0F: // Write Multiple Coils
                    case 0x10: // Write Multiple Registers
                    {
                        await ReadIntoAsync(scratch, written, 4, cancellationToken).ConfigureAwait(false); // Address + Value/Quantity
                        written += 4;
                        await ReadIntoAsync(scratch, written, 2, cancellationToken).ConfigureAwait(false); // CRC
                        written += 2;
                        break;
                    }

                    case 0x16: // Mask Write Register
                    {
                        await ReadIntoAsync(scratch, written, 6, cancellationToken).ConfigureAwait(false); // Address + AND mask + OR mask
                        written += 6;
                        await ReadIntoAsync(scratch, written, 2, cancellationToken).ConfigureAwait(false); // CRC
                        written += 2;
                        break;
                    }

                    case 0x18: // Read FIFO Queue
                    {
                        byte[] counts = await ReadAsync(4, cancellationToken).ConfigureAwait(false);
                        scratch[written++] = counts[0];
                        scratch[written++] = counts[1];
                        scratch[written++] = counts[2];
                        scratch[written++] = counts[3];
                        ushort byteCount = (ushort)((counts[0] << 8) | counts[1]);
                        if (byteCount > 2)
                        {
                            await ReadIntoAsync(scratch, written, byteCount - 2, cancellationToken).ConfigureAwait(false);
                            written += byteCount - 2;
                        }
                        await ReadIntoAsync(scratch, written, 2, cancellationToken).ConfigureAwait(false); // CRC
                        written += 2;
                        break;
                    }

                    default:
                        throw new IOException(
                            $"EnhancedModbusRtuTransport: unsupported function code 0x{functionCode:X2}");
                }

                byte[] frame = new byte[written];
                Buffer.BlockCopy(scratch, 0, frame, 0, written);
                return frame;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        private async Task ReadIntoAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < count)
            {
                int read = await StreamResource.ReadAsync(buffer.AsMemory(offset + total, count - total), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new IOException("Read resulted in 0 bytes returned.");
                total += read;
            }
        }

        /// <summary>
        ///     Reads a complete Device ID response frame including CRC verification.
        /// </summary>
        private byte[] ReadDeviceIdResponseFrame(byte[] frameStart)
        {
            // Realistic Device ID responses fit well under 4KB even with multiple objects. The
            // Modbus spec allows up to 256 objects × 255 bytes = ~64KB in theory, but a device
            // returning that much is almost certainly corrupt. Cap at 4KB and grow if needed.
            const int InitialDeviceIdCapacity = 4096;

            byte[] scratch = ArrayPool<byte>.Shared.Rent(InitialDeviceIdCapacity);
            int capacity = InitialDeviceIdCapacity;
            try
            {
                int written = 0;
                scratch[written++] = frameStart[0];
                scratch[written++] = frameStart[1];

                // Error response check
                if ((frameStart[1] & 0x80) == 0x80)
                {
                    byte[] exceptionPlusCrc = Read(3); // exception code + 2 CRC bytes
                    EnsureCapacity(ref scratch, ref capacity, written, 3);
                    scratch[written++] = exceptionPlusCrc[0];
                    scratch[written++] = exceptionPlusCrc[1];
                    scratch[written++] = exceptionPlusCrc[2];

                    byte[] resultFrame = new byte[written];
                    Buffer.BlockCopy(scratch, 0, resultFrame, 0, written);
                    return resultFrame;
                }

                // Header: MEI type + Device ID code + Conformity + MoreFollows + NextObjId + NumObjects
                byte meiType = Read(1)[0];
                scratch[written++] = meiType;

                if (meiType != MeiType)
                    throw new IOException($"Unexpected MEI type 0x{meiType:X2} in Device ID response.");

                byte[] header = Read(5); // DeviceIdCode, Conformity, MoreFollows, NextObjId, NumObjects
                EnsureCapacity(ref scratch, ref capacity, written, 5);
                scratch[written++] = header[0];
                scratch[written++] = header[1];
                scratch[written++] = header[2];
                scratch[written++] = header[3];
                scratch[written++] = header[4];

                byte numberOfObjects = header[4];

                // Read each object: [ObjectId][Length][Value...]
                for (int i = 0; i < numberOfObjects; i++)
                {
                    byte objectId = Read(1)[0];
                    byte objectLength = Read(1)[0];
                    EnsureCapacity(ref scratch, ref capacity, written, 2 + objectLength);
                    scratch[written++] = objectId;
                    scratch[written++] = objectLength;

                    if (objectLength > 0)
                    {
                        ReadInto(scratch, written, objectLength);
                        written += objectLength;
                    }
                }

                // Read and verify CRC
                // RTU CRC is transmitted low-byte first (little-endian)
                EnsureCapacity(ref scratch, ref capacity, written, 2);
                ReadInto(scratch, written, 2);
                ushort received = BinaryPrimitives.ReadUInt16LittleEndian(scratch.AsSpan(written, 2));

                byte[] dataFrame = new byte[written];
                Buffer.BlockCopy(scratch, 0, dataFrame, 0, written);

                ushort calculated = ModbusUtility.CalculateCrc(dataFrame.AsSpan());
                if (received != calculated)
                    throw new IOException($"Device ID response CRC mismatch. Received: 0x{received:X4}, calculated: 0x{calculated:X4}");

                return dataFrame;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        /// <summary>
        ///     Ensure <paramref name="scratch"/> has at least <paramref name="needed"/> bytes of
        ///     remaining capacity starting at <paramref name="currentWritten"/>. If the rented
        ///     buffer is too small, rent a larger one and copy the accumulated bytes over.
        /// </summary>
        private static void EnsureCapacity(ref byte[] scratch, ref int capacity, int currentWritten, int needed)
        {
            if (currentWritten + needed <= capacity) return;

            int newCapacity = capacity * 2;
            while (newCapacity < currentWritten + needed) newCapacity *= 2;

            byte[] grown = ArrayPool<byte>.Shared.Rent(newCapacity);
            Buffer.BlockCopy(scratch, 0, grown, 0, currentWritten);
            ArrayPool<byte>.Shared.Return(scratch);
            scratch = grown;
            capacity = newCapacity;
        }

        private async Task<byte[]> ReadDeviceIdResponseFrameAsync(byte[] frameStart, CancellationToken cancellationToken = default)
        {
            const int InitialDeviceIdCapacity = 4096;

            byte[] scratch = ArrayPool<byte>.Shared.Rent(InitialDeviceIdCapacity);
            int capacity = InitialDeviceIdCapacity;
            try
            {
                int written = 0;
                scratch[written++] = frameStart[0];
                scratch[written++] = frameStart[1];

                // Error response check
                if ((frameStart[1] & 0x80) == 0x80)
                {
                    byte[] exceptionCode = await ReadAsync(1, cancellationToken).ConfigureAwait(false);
                    byte[] crcBytes = await ReadAsync(2, cancellationToken).ConfigureAwait(false);
                    EnsureCapacity(ref scratch, ref capacity, written, 3);
                    scratch[written++] = exceptionCode[0];
                    scratch[written++] = crcBytes[0];
                    scratch[written++] = crcBytes[1];

                    byte[] resultFrame = new byte[written];
                    Buffer.BlockCopy(scratch, 0, resultFrame, 0, written);
                    return resultFrame;
                }

                // Header: MEI type + Device ID code + Conformity + MoreFollows + NextObjId + NumObjects
                byte[] meiArr = await ReadAsync(1, cancellationToken).ConfigureAwait(false);
                byte meiType = meiArr[0];
                scratch[written++] = meiType;

                if (meiType != MeiType)
                    throw new IOException($"Unexpected MEI type 0x{meiType:X2} in Device ID response.");

                byte[] header = await ReadAsync(5, cancellationToken).ConfigureAwait(false); // DeviceIdCode, Conformity, MoreFollows, NextObjId, NumObjects
                EnsureCapacity(ref scratch, ref capacity, written, 5);
                scratch[written++] = header[0];
                scratch[written++] = header[1];
                scratch[written++] = header[2];
                scratch[written++] = header[3];
                scratch[written++] = header[4];

                byte numberOfObjects = header[4];

                // Read each object: [ObjectId][Length][Value...]
                for (int i = 0; i < numberOfObjects; i++)
                {
                    byte[] objectIdArr = await ReadAsync(1, cancellationToken).ConfigureAwait(false);
                    byte[] objectLengthArr = await ReadAsync(1, cancellationToken).ConfigureAwait(false);
                    byte objectId = objectIdArr[0];
                    byte objectLength = objectLengthArr[0];
                    EnsureCapacity(ref scratch, ref capacity, written, 2 + objectLength);
                    scratch[written++] = objectId;
                    scratch[written++] = objectLength;

                    if (objectLength > 0)
                    {
                        await ReadIntoAsync(scratch, written, objectLength, cancellationToken).ConfigureAwait(false);
                        written += objectLength;
                    }
                }

                // Read and verify CRC
                // RTU CRC is transmitted low-byte first (little-endian)
                EnsureCapacity(ref scratch, ref capacity, written, 2);
                await ReadIntoAsync(scratch, written, 2, cancellationToken).ConfigureAwait(false);
                ushort received = BinaryPrimitives.ReadUInt16LittleEndian(scratch.AsSpan(written, 2));

                byte[] dataFrame = new byte[written];
                Buffer.BlockCopy(scratch, 0, dataFrame, 0, written);

                ushort calculated = ModbusUtility.CalculateCrc(dataFrame.AsSpan());
                if (received != calculated)
                    throw new IOException($"Device ID response CRC mismatch. Received: 0x{received:X4}, calculated: 0x{calculated:X4}");

                return dataFrame;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }
    }
}
