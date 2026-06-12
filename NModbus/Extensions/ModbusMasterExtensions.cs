using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Data;
using NModbus.Message;

namespace NModbus.Extensions
{
    /// <summary>
    ///     Extension methods for IModbusMaster providing batch read/write operations
    ///     and request/response message pair access.
    /// </summary>
    public static class ModbusMasterExtensions
    {
        private static readonly Dictionary<byte, ushort> FuncMaxLenDict = new()
        {
            { ModbusFunctionCodes.ReadCoils, 2000 },
            { ModbusFunctionCodes.ReadInputs, 2000 },
            { ModbusFunctionCodes.ReadHoldingRegisters, 121 },
            { ModbusFunctionCodes.ReadInputRegisters, 121 }
        };

        #region BatchRead

        /// <summary>
        ///     Batch read multiple addresses with different data types in a single pass.
        ///     Automatically splits into multiple requests when addresses exceed MaxLength.
        /// </summary>
        /// <param name="master">The Modbus master.</param>
        /// <param name="addressList">Dictionary of address to data type.</param>
        /// <param name="slaveAddress">Slave address.</param>
        /// <param name="functionCode">Function code (0x01-0x04).</param>
        /// <returns>Dictionary of address to interpreted value.</returns>
        public static Dictionary<ushort, object> BatchRead(
            this IModbusMaster master,
            Dictionary<ushort, DataTypeEnum> addressList,
            byte slaveAddress,
            byte functionCode)
        {
            var result = new Dictionary<ushort, object>();
            var minAddr = addressList.Min(t => t.Key);
            var maxAddr = addressList.Max(t => t.Key);

            while (maxAddr >= minAddr)
            {
                var readMaxLen = FuncMaxLenDict[functionCode];
                var list = addressList.Where(t => t.Key >= minAddr && t.Key < minAddr + readMaxLen).ToList();
                if (list.Count == 0)
                {
                    minAddr += readMaxLen;
                    continue;
                }

                // Calculate actual read length based on the last address's data type
                var lastItem = list.OrderByDescending(t => t.Key).First();
                var readLength = lastItem.Value switch
                {
                    DataTypeEnum.Bool or DataTypeEnum.Int16 or DataTypeEnum.UInt16
                        => lastItem.Key + 1 - minAddr,
                    DataTypeEnum.Int32 or DataTypeEnum.UInt32 or DataTypeEnum.Float
                        => lastItem.Key + 2 - minAddr,
                    DataTypeEnum.Int64 or DataTypeEnum.UInt64 or DataTypeEnum.Double
                        => lastItem.Key + 4 - minAddr,
                    _ => throw new NotSupportedException($"BatchRead unsupported type: {lastItem.Value}")
                };

                var message = master.ReadWithResp(slaveAddress, (ushort)minAddr, (ushort)readLength, functionCode);

                if (message.Response is ReadCoilsInputsResponse coilsResp)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        result.Add(item.Key, coilsResp.Data[item.Key - minAddr]);
                    }
                }
                else if (message.Response is ReadHoldingInputRegistersResponse regsResp)
                {
                    var data = regsResp.Data.TakeToArray(readLength);
                    for (var i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        var startIndex = item.Key - minAddr;
                        var obj = item.Value switch
                        {
                            DataTypeEnum.Int16 => RegisterConverter.ReadInt16(data, startIndex),
                            DataTypeEnum.UInt16 => RegisterConverter.ReadUInt16(data, startIndex),
                            DataTypeEnum.Int32 => RegisterConverter.ReadInt32(data, startIndex),
                            DataTypeEnum.UInt32 => RegisterConverter.ReadUInt32(data, startIndex),
                            DataTypeEnum.Int64 => RegisterConverter.ReadInt64(data, startIndex),
                            DataTypeEnum.UInt64 => RegisterConverter.ReadUInt64(data, startIndex),
                            DataTypeEnum.Float => RegisterConverter.ReadFloat(data, startIndex),
                            DataTypeEnum.Double => (object)RegisterConverter.ReadDouble(data, startIndex),
                            _ => throw new NotSupportedException($"BatchRead unsupported type: {item.Value}")
                        };
                        result.Add(item.Key, obj);
                    }
                }

                minAddr += (ushort)readLength;
            }

            return result;
        }

        /// <summary>
        ///     Asynchronously batch read multiple addresses with different data types.
        /// </summary>
        public static async Task<Dictionary<ushort, object>> BatchReadAsync(
            this IModbusMaster master,
            Dictionary<ushort, DataTypeEnum> addressList,
            byte slaveAddress,
            byte functionCode,
            CancellationToken cancellationToken = default)
        {
            // Use async transport path
            var result = new Dictionary<ushort, object>();
            var minAddr = addressList.Min(t => t.Key);
            var maxAddr = addressList.Max(t => t.Key);

            while (maxAddr >= minAddr)
            {
                var readMaxLen = FuncMaxLenDict[functionCode];
                var list = addressList.Where(t => t.Key >= minAddr && t.Key < minAddr + readMaxLen).ToList();
                if (list.Count == 0)
                {
                    minAddr += readMaxLen;
                    continue;
                }

                var lastItem = list.OrderByDescending(t => t.Key).First();
                var readLength = lastItem.Value switch
                {
                    DataTypeEnum.Bool or DataTypeEnum.Int16 or DataTypeEnum.UInt16
                        => lastItem.Key + 1 - minAddr,
                    DataTypeEnum.Int32 or DataTypeEnum.UInt32 or DataTypeEnum.Float
                        => lastItem.Key + 2 - minAddr,
                    DataTypeEnum.Int64 or DataTypeEnum.UInt64 or DataTypeEnum.Double
                        => lastItem.Key + 4 - minAddr,
                    _ => throw new NotSupportedException($"BatchRead unsupported type: {lastItem.Value}")
                };

                var message = await master.ReadWithRespAsync(slaveAddress, (ushort)minAddr, (ushort)readLength, functionCode, cancellationToken).ConfigureAwait(false);

                if (message.Response is ReadCoilsInputsResponse coilsResp)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        result.Add(item.Key, coilsResp.Data[item.Key - minAddr]);
                    }
                }
                else if (message.Response is ReadHoldingInputRegistersResponse regsResp)
                {
                    var data = regsResp.Data.TakeToArray(readLength);
                    for (var i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        var startIndex = item.Key - minAddr;
                        var obj = item.Value switch
                        {
                            DataTypeEnum.Int16 => RegisterConverter.ReadInt16(data, startIndex),
                            DataTypeEnum.UInt16 => RegisterConverter.ReadUInt16(data, startIndex),
                            DataTypeEnum.Int32 => RegisterConverter.ReadInt32(data, startIndex),
                            DataTypeEnum.UInt32 => RegisterConverter.ReadUInt32(data, startIndex),
                            DataTypeEnum.Int64 => RegisterConverter.ReadInt64(data, startIndex),
                            DataTypeEnum.UInt64 => RegisterConverter.ReadUInt64(data, startIndex),
                            DataTypeEnum.Float => RegisterConverter.ReadFloat(data, startIndex),
                            DataTypeEnum.Double => (object)RegisterConverter.ReadDouble(data, startIndex),
                            _ => throw new NotSupportedException($"BatchRead unsupported type: {item.Value}")
                        };
                        result.Add(item.Key, obj);
                    }
                }

                minAddr += (ushort)readLength;
            }

            return result;
        }

        #endregion

        #region BatchWrite

        /// <summary>
        ///     Batch write coils. Automatically merges consecutive addresses into WriteMultipleCoils,
        ///     non-consecutive addresses are written in separate transactions.
        /// </summary>
        public static List<NModbusMessage<WriteMultipleCoilsRequest, WriteMultipleCoilsResponse>> BatchWriteCoils(
            this IModbusMaster master,
            Dictionary<ushort, bool> addressList,
            byte slaveAddress)
        {
            var messages = new List<NModbusMessage<WriteMultipleCoilsRequest, WriteMultipleCoilsResponse>>();
            if (addressList == null || addressList.Count == 0)
                return messages;

            const int maxBatch = 1968;
            var sorted = addressList.OrderBy(t => t.Key).ToList();
            var i = 0;

            while (i < sorted.Count)
            {
                var groupStart = sorted[i].Key;
                var groupValues = new List<bool>();

                while (i < sorted.Count
                       && sorted[i].Key == groupStart + groupValues.Count
                       && groupValues.Count < maxBatch)
                {
                    groupValues.Add(sorted[i].Value);
                    i++;
                }

                var msg = master.WriteMultipleCoilsWithResp(slaveAddress, groupStart, groupValues.ToArray());
                messages.Add(msg);
            }

            return messages;
        }

        /// <summary>
        ///     Batch write registers. Automatically merges consecutive addresses into WriteMultipleRegisters,
        ///     non-consecutive addresses are written in separate transactions.
        /// </summary>
        public static List<NModbusMessage<WriteMultipleRegistersRequest, WriteMultipleRegistersResponse>> BatchWriteRegisters(
            this IModbusMaster master,
            Dictionary<ushort, ushort> addressList,
            byte slaveAddress)
        {
            var messages = new List<NModbusMessage<WriteMultipleRegistersRequest, WriteMultipleRegistersResponse>>();
            if (addressList == null || addressList.Count == 0)
                return messages;

            const int maxBatch = 123;
            var sorted = addressList.OrderBy(t => t.Key).ToList();
            var i = 0;

            while (i < sorted.Count)
            {
                var groupStart = sorted[i].Key;
                var nextExpected = groupStart;
                var groupRegisters = new List<ushort>();

                while (i < sorted.Count
                       && sorted[i].Key == nextExpected
                       && groupRegisters.Count < maxBatch)
                {
                    groupRegisters.Add(sorted[i].Value);
                    nextExpected = (ushort)(sorted[i].Key + 1);
                    i++;
                }

                var msg = master.WriteMultipleRegistersWithResp(slaveAddress, groupStart, groupRegisters.ToArray());
                messages.Add(msg);
            }

            return messages;
        }

        #endregion

        #region ReadWithResp

        /// <summary>
        ///     Read data with function code dispatch, returning the request/response pair.
        /// </summary>
        public static INModbusMessagePair<IModbusMessage, IModbusMessage> ReadWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints, byte functionCode)
        {
            return functionCode switch
            {
                ModbusFunctionCodes.ReadCoils => master.ReadCoilsWithResp(slaveAddress, startAddress, numberOfPoints),
                ModbusFunctionCodes.ReadInputs => master.ReadInputsWithResp(slaveAddress, startAddress, numberOfPoints),
                ModbusFunctionCodes.ReadHoldingRegisters => master.ReadHoldingRegistersWithResp(slaveAddress, startAddress, numberOfPoints),
                ModbusFunctionCodes.ReadInputRegisters => master.ReadInputRegistersWithResp(slaveAddress, startAddress, numberOfPoints),
                _ => throw new NotSupportedException($"Function code 0x{functionCode:X2} is not supported.")
            };
        }

        /// <summary>
        ///     Async read data with function code dispatch, returning the request/response pair.
        /// </summary>
        public static async Task<INModbusMessagePair<IModbusMessage, IModbusMessage>> ReadWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints, byte functionCode,
            CancellationToken cancellationToken = default)
        {
            return functionCode switch
            {
                ModbusFunctionCodes.ReadCoils => await master.ReadCoilsWithRespAsync(slaveAddress, startAddress, numberOfPoints, cancellationToken).ConfigureAwait(false),
                ModbusFunctionCodes.ReadInputs => await master.ReadInputsWithRespAsync(slaveAddress, startAddress, numberOfPoints, cancellationToken).ConfigureAwait(false),
                ModbusFunctionCodes.ReadHoldingRegisters => await master.ReadHoldingRegistersWithRespAsync(slaveAddress, startAddress, numberOfPoints, cancellationToken).ConfigureAwait(false),
                ModbusFunctionCodes.ReadInputRegisters => await master.ReadInputRegistersWithRespAsync(slaveAddress, startAddress, numberOfPoints, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Function code 0x{functionCode:X2} is not supported.")
            };
        }

        #endregion

        #region ReadCoils / ReadInputs WithResp

        /// <summary>Read coils and return the request/response pair with hex frames.</summary>
        public static NModbusMessage<ReadCoilsInputsRequest, ReadCoilsInputsResponse> ReadCoilsWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints)
        {
            ValidateNumberOfPoints(numberOfPoints, 2000);
            var request = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, slaveAddress, startAddress, numberOfPoints);
            var response = master.Transport.UnicastMessage<ReadCoilsInputsResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async read coils and return the request/response pair with hex frames.</summary>
        public static async Task<NModbusMessage<ReadCoilsInputsRequest, ReadCoilsInputsResponse>> ReadCoilsWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints,
            CancellationToken cancellationToken = default)
        {
            ValidateNumberOfPoints(numberOfPoints, 2000);
            var request = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, slaveAddress, startAddress, numberOfPoints);
            var response = await master.Transport.UnicastMessageAsync<ReadCoilsInputsResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Read discrete inputs and return the request/response pair with hex frames.</summary>
        public static NModbusMessage<ReadCoilsInputsRequest, ReadCoilsInputsResponse> ReadInputsWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints)
        {
            ValidateNumberOfPoints(numberOfPoints, 2000);
            var request = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadInputs, slaveAddress, startAddress, numberOfPoints);
            var response = master.Transport.UnicastMessage<ReadCoilsInputsResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async read discrete inputs and return the request/response pair with hex frames.</summary>
        public static async Task<NModbusMessage<ReadCoilsInputsRequest, ReadCoilsInputsResponse>> ReadInputsWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints,
            CancellationToken cancellationToken = default)
        {
            ValidateNumberOfPoints(numberOfPoints, 2000);
            var request = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadInputs, slaveAddress, startAddress, numberOfPoints);
            var response = await master.Transport.UnicastMessageAsync<ReadCoilsInputsResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        #endregion

        #region ReadHoldingRegisters / ReadInputRegisters WithResp

        /// <summary>Read holding registers and return the request/response pair with hex frames.</summary>
        public static NModbusMessage<ReadHoldingInputRegistersRequest, ReadHoldingInputRegistersResponse> ReadHoldingRegistersWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints)
        {
            ValidateNumberOfPoints(numberOfPoints, 125);
            var request = new ReadHoldingInputRegistersRequest(ModbusFunctionCodes.ReadHoldingRegisters, slaveAddress, startAddress, numberOfPoints);
            var response = master.Transport.UnicastMessage<ReadHoldingInputRegistersResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async read holding registers and return the request/response pair with hex frames.</summary>
        public static async Task<NModbusMessage<ReadHoldingInputRegistersRequest, ReadHoldingInputRegistersResponse>> ReadHoldingRegistersWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints,
            CancellationToken cancellationToken = default)
        {
            ValidateNumberOfPoints(numberOfPoints, 125);
            var request = new ReadHoldingInputRegistersRequest(ModbusFunctionCodes.ReadHoldingRegisters, slaveAddress, startAddress, numberOfPoints);
            var response = await master.Transport.UnicastMessageAsync<ReadHoldingInputRegistersResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Read input registers and return the request/response pair with hex frames.</summary>
        public static NModbusMessage<ReadHoldingInputRegistersRequest, ReadHoldingInputRegistersResponse> ReadInputRegistersWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints)
        {
            ValidateNumberOfPoints(numberOfPoints, 125);
            var request = new ReadHoldingInputRegistersRequest(ModbusFunctionCodes.ReadInputRegisters, slaveAddress, startAddress, numberOfPoints);
            var response = master.Transport.UnicastMessage<ReadHoldingInputRegistersResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async read input registers and return the request/response pair with hex frames.</summary>
        public static async Task<NModbusMessage<ReadHoldingInputRegistersRequest, ReadHoldingInputRegistersResponse>> ReadInputRegistersWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort numberOfPoints,
            CancellationToken cancellationToken = default)
        {
            ValidateNumberOfPoints(numberOfPoints, 125);
            var request = new ReadHoldingInputRegistersRequest(ModbusFunctionCodes.ReadInputRegisters, slaveAddress, startAddress, numberOfPoints);
            var response = await master.Transport.UnicastMessageAsync<ReadHoldingInputRegistersResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        #endregion

        #region WriteSingleCoil / WriteSingleRegister WithResp

        /// <summary>Write single coil and return the request/response pair.</summary>
        public static NModbusMessage<WriteSingleCoilRequestResponse, WriteSingleCoilRequestResponse> WriteSingleCoilWithResp(
            this IModbusMaster master, byte slaveAddress, ushort coilAddress, bool value)
        {
            var request = new WriteSingleCoilRequestResponse(slaveAddress, coilAddress, value);
            var response = master.Transport.UnicastMessage<WriteSingleCoilRequestResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async write single coil and return the request/response pair.</summary>
        public static async Task<NModbusMessage<WriteSingleCoilRequestResponse, WriteSingleCoilRequestResponse>> WriteSingleCoilWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort coilAddress, bool value,
            CancellationToken cancellationToken = default)
        {
            var request = new WriteSingleCoilRequestResponse(slaveAddress, coilAddress, value);
            var response = await master.Transport.UnicastMessageAsync<WriteSingleCoilRequestResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Write single register and return the request/response pair.</summary>
        public static NModbusMessage<WriteSingleRegisterRequestResponse, WriteSingleRegisterRequestResponse> WriteSingleRegisterWithResp(
            this IModbusMaster master, byte slaveAddress, ushort registerAddress, ushort value)
        {
            var request = new WriteSingleRegisterRequestResponse(slaveAddress, registerAddress, value);
            var response = master.Transport.UnicastMessage<WriteSingleRegisterRequestResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async write single register and return the request/response pair.</summary>
        public static async Task<NModbusMessage<WriteSingleRegisterRequestResponse, WriteSingleRegisterRequestResponse>> WriteSingleRegisterWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort registerAddress, ushort value,
            CancellationToken cancellationToken = default)
        {
            var request = new WriteSingleRegisterRequestResponse(slaveAddress, registerAddress, value);
            var response = await master.Transport.UnicastMessageAsync<WriteSingleRegisterRequestResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        #endregion

        #region WriteMultipleRegisters / WriteMultipleCoils WithResp

        /// <summary>Write multiple registers and return the request/response pair.</summary>
        public static NModbusMessage<WriteMultipleRegistersRequest, WriteMultipleRegistersResponse> WriteMultipleRegistersWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort[] data)
        {
            ValidateData(data, 123);
            var request = new WriteMultipleRegistersRequest(slaveAddress, startAddress, new RegisterCollection(data));
            var response = master.Transport.UnicastMessage<WriteMultipleRegistersResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async write multiple registers and return the request/response pair.</summary>
        public static async Task<NModbusMessage<WriteMultipleRegistersRequest, WriteMultipleRegistersResponse>> WriteMultipleRegistersWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, ushort[] data,
            CancellationToken cancellationToken = default)
        {
            ValidateData(data, 123);
            var request = new WriteMultipleRegistersRequest(slaveAddress, startAddress, new RegisterCollection(data));
            var response = await master.Transport.UnicastMessageAsync<WriteMultipleRegistersResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Write multiple coils and return the request/response pair.</summary>
        public static NModbusMessage<WriteMultipleCoilsRequest, WriteMultipleCoilsResponse> WriteMultipleCoilsWithResp(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, bool[] data)
        {
            ValidateData(data, 1968);
            var request = new WriteMultipleCoilsRequest(slaveAddress, startAddress, new DiscreteCollection(data));
            var response = master.Transport.UnicastMessage<WriteMultipleCoilsResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async write multiple coils and return the request/response pair.</summary>
        public static async Task<NModbusMessage<WriteMultipleCoilsRequest, WriteMultipleCoilsResponse>> WriteMultipleCoilsWithRespAsync(
            this IModbusMaster master, byte slaveAddress, ushort startAddress, bool[] data,
            CancellationToken cancellationToken = default)
        {
            ValidateData(data, 1968);
            var request = new WriteMultipleCoilsRequest(slaveAddress, startAddress, new DiscreteCollection(data));
            var response = await master.Transport.UnicastMessageAsync<WriteMultipleCoilsResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        #endregion

        #region ReadWriteMultipleRegisters WithResp

        /// <summary>Read/write multiple registers and return the request/response pair.</summary>
        public static NModbusMessage<ReadWriteMultipleRegistersRequest, ReadHoldingInputRegistersResponse> ReadWriteMultipleRegistersWithResp(
            this IModbusMaster master, byte slaveAddress,
            ushort startReadAddress, ushort numberOfPointsToRead,
            ushort startWriteAddress, ushort[] writeData)
        {
            ValidateNumberOfPoints(numberOfPointsToRead, 125);
            ValidateData(writeData, 121);
            var request = new ReadWriteMultipleRegistersRequest(
                slaveAddress, startReadAddress, numberOfPointsToRead, startWriteAddress, new RegisterCollection(writeData));
            var response = master.Transport.UnicastMessage<ReadHoldingInputRegistersResponse>(request);
            return CreateMessagePair(master, request, response);
        }

        /// <summary>Async read/write multiple registers and return the request/response pair.</summary>
        public static async Task<NModbusMessage<ReadWriteMultipleRegistersRequest, ReadHoldingInputRegistersResponse>> ReadWriteMultipleRegistersWithRespAsync(
            this IModbusMaster master, byte slaveAddress,
            ushort startReadAddress, ushort numberOfPointsToRead,
            ushort startWriteAddress, ushort[] writeData,
            CancellationToken cancellationToken = default)
        {
            ValidateNumberOfPoints(numberOfPointsToRead, 125);
            ValidateData(writeData, 121);
            var request = new ReadWriteMultipleRegistersRequest(
                slaveAddress, startReadAddress, numberOfPointsToRead, startWriteAddress, new RegisterCollection(writeData));
            var response = await master.Transport.UnicastMessageAsync<ReadHoldingInputRegistersResponse>(request, cancellationToken).ConfigureAwait(false);
            return CreateMessagePair(master, request, response);
        }

        #endregion

        #region Frame / Hex Helpers

        /// <summary>Get the raw message frame bytes.</summary>
        public static byte[] GetMessageFrame(this IModbusMaster master, IModbusMessage message)
        {
            return master.Transport.BuildMessageFrame(message);
        }

        /// <summary>Get the message frame as a hex string (e.g. "01 03 00 00 00 0A C5 CD").</summary>
        public static string GetMessageHex(this IModbusMaster master, IModbusMessage message)
        {
            var data = master.Transport.BuildMessageFrame(message);
            return BitConverter.ToString(data).Replace("-", " ");
        }

        #endregion

        #region Private Helpers

        private static NModbusMessage<TReq, TResp> CreateMessagePair<TReq, TResp>(
            IModbusMaster master, TReq request, TResp response)
            where TReq : IModbusMessage
            where TResp : IModbusMessage
        {
            return new NModbusMessage<TReq, TResp>
            {
                Request = request,
                RequestHex = master.GetMessageHex(request),
                Response = response,
                ResponseHex = master.GetMessageHex(response)
            };
        }

        private static void ValidateData<T>(T[] data, int maxDataLength)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0 || data.Length > maxDataLength)
                throw new ArgumentException($"Data length must be between 1 and {maxDataLength}.");
        }

        private static void ValidateNumberOfPoints(ushort numberOfPoints, ushort maxNumberOfPoints)
        {
            if (numberOfPoints < 1 || numberOfPoints > maxNumberOfPoints)
                throw new ArgumentException($"numberOfPoints must be between 1 and {maxNumberOfPoints}.");
        }

        #endregion
    }
}
