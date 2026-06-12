using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Data;

namespace NModbus
{
	/// <summary>
	///     Modbus master device.
	/// </summary>
	public interface IModbusMaster : IDisposable
	{
		/// <summary>
		///     Transport used by this master.
		/// </summary>
		IModbusTransport Transport { get; }

		/// <summary>
		///    Reads from 1 to 2000 contiguous coils status.
		/// </summary>
		bool[] ReadCoils(byte slaveAddress, ushort startAddress, ushort numberOfPoints);

		/// <summary>
		///    Asynchronously reads from 1 to 2000 contiguous coils status.
		/// </summary>
		Task<bool[]> ReadCoilsAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Reads from 1 to 2000 contiguous discrete input status.
		/// </summary>
		bool[] ReadInputs(byte slaveAddress, ushort startAddress, ushort numberOfPoints);

		/// <summary>
		///    Asynchronously reads from 1 to 2000 contiguous discrete input status.
		/// </summary>
		Task<bool[]> ReadInputsAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Reads contiguous block of holding registers.
		/// </summary>
		ushort[] ReadHoldingRegisters(byte slaveAddress, ushort startAddress, ushort numberOfPoints);

		/// <summary>
		///    Asynchronously reads contiguous block of holding registers.
		/// </summary>
		Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Reads contiguous block of input registers.
		/// </summary>
		ushort[] ReadInputRegisters(byte slaveAddress, ushort startAddress, ushort numberOfPoints);

		/// <summary>
		///    Asynchronously reads contiguous block of input registers.
		/// </summary>
		Task<ushort[]> ReadInputRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Writes a single coil value.
		/// </summary>
		void WriteSingleCoil(byte slaveAddress, ushort coilAddress, bool value);

		/// <summary>
		///    Asynchronously writes a single coil value.
		/// </summary>
		Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Writes a single holding register.
		/// </summary>
		void WriteSingleRegister(byte slaveAddress, ushort registerAddress, ushort value);

		/// <summary>
		///    Asynchronously writes a single holding register.
		/// </summary>
		Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Writes a block of 1 to 123 contiguous registers.
		/// </summary>
		void WriteMultipleRegisters(byte slaveAddress, ushort startAddress, ushort[] data);

		/// <summary>
		///    Asynchronously writes a block of 1 to 123 contiguous registers.
		/// </summary>
		Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] data,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Sends a broadcast write (slave address 0) of a single holding register.
		///    No response is read. All slaves on the network process the write.
		/// </summary>
		void BroadcastWriteSingleRegister(ushort registerAddress, ushort value);

		/// <summary>
		///    Asynchronously sends a broadcast write (slave address 0) of a single holding register.
		///    No response is read. All slaves on the network process the write.
		/// </summary>
		Task BroadcastWriteSingleRegisterAsync(ushort registerAddress, ushort value,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Writes a sequence of coils.
		/// </summary>
		void WriteMultipleCoils(byte slaveAddress, ushort startAddress, bool[] data);

		/// <summary>
		///    Asynchronously writes a sequence of coils.
		/// </summary>
		Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] data,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Performs a combination of one read operation and one write operation in a single Modbus transaction.
		///    The write operation is performed before the read.
		/// </summary>
		ushort[] ReadWriteMultipleRegisters(
				byte slaveAddress,
				ushort startReadAddress,
				ushort numberOfPointsToRead,
				ushort startWriteAddress,
				ushort[] writeData);

		/// <summary>
		///    Asynchronously performs a combination of one read operation and one write operation in a single Modbus transaction.
		///    The write operation is performed before the read.
		/// </summary>
		Task<ushort[]> ReadWriteMultipleRegistersAsync(
				byte slaveAddress,
				ushort startReadAddress,
				ushort numberOfPointsToRead,
				ushort startWriteAddress,
				ushort[] writeData,
				CancellationToken cancellationToken = default);

		/// <summary>
		/// Write a file record to the device.
		/// </summary>
		void WriteFileRecord(byte slaveAdress, ushort fileNumber, ushort startingAddress, byte[] data);

		/// <summary>
		///    Executes the custom message.
		/// </summary>
		TResponse ExecuteCustomMessage<TResponse>(IModbusMessage request)
				where TResponse : IModbusMessage, new();

		/// <summary>
		///    Reads device identification objects from a Modbus slave
		///    (function code 0x2B, MEI type 0x0E).
		/// </summary>
		Dictionary<byte, string> ReadDeviceIdentification(byte slaveAddress,
			DeviceIdCategory category, byte objectId);

		/// <summary>
		///    Asynchronously reads device identification objects from a Modbus slave.
		/// </summary>
		Task<Dictionary<byte, string>> ReadDeviceIdentificationAsync(byte slaveAddress,
			DeviceIdCategory category, byte objectId,
			CancellationToken cancellationToken = default);

		/// <summary>
		///    Reads basic device identification (vendor name, product code, revision).
		/// </summary>
		Dictionary<byte, string> ReadBasicDeviceIdentification(byte slaveAddress);

		/// <summary>
		///    Asynchronously reads basic device identification.
		/// </summary>
		Task<Dictionary<byte, string>> ReadBasicDeviceIdentificationAsync(byte slaveAddress,
			CancellationToken cancellationToken = default);
	}
}
