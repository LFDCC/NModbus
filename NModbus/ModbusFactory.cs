using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NModbus.Device.MessageHandlers;
using System.Linq;
using System.Net.Sockets;
using NModbus.Data;
using NModbus.Device;
using NModbus.IO;
using NModbus.Logging;

namespace NModbus
{
    public class ModbusFactory : IModbusFactory
    {
        /// <summary>
        /// The "built-in" message handlers.
        /// </summary>
        private static readonly IModbusFunctionService[] BuiltInFunctionServices = 
        {
            new ReadCoilsService(),
            new ReadInputsService(),
            new ReadHoldingRegistersService(),
            new ReadInputRegistersService(),
            new DiagnosticsService(),
            new WriteSingleCoilService(),
            new WriteSingleRegisterService(),
            new WriteMultipleCoilsService(),
            new WriteMultipleRegistersService(),
            new WriteFileRecordService(),
            new ReadWriteMultipleRegistersService(),
            new ReadDeviceIdentificationService(),
        };

        private readonly FrozenDictionary<byte, IModbusFunctionService> _functionServices;

        /// <summary>
        /// Create a factory which uses the built in standard slave function handlers.
        /// </summary>
        public ModbusFactory()
        {
            _functionServices = BuiltInFunctionServices
                .ToDictionary(s => s.FunctionCode, s => s)
                .ToFrozenDictionary();

            Logger = NullModbusLogger.Instance;
        }

        /// <summary>
        /// Create a factory which optionally uses the built in function services and allows custom services to be added.
        /// </summary>
        /// <param name="functionServices">User provided function services.</param>
        /// <param name="includeBuiltIn">If true, the built in function services are included. Otherwise, all function services will come from the functionService parameter.</param>
        /// <param name="logger">Logger</param>
        public ModbusFactory(
            IEnumerable<IModbusFunctionService> functionServices = null,
            bool includeBuiltIn = true,
            IModbusLogger logger = null)
        {
            Logger = logger ?? NullModbusLogger.Instance;

            // Build a mutable dictionary first, merge custom services, then freeze.
            Dictionary<byte, IModbusFunctionService> services;

            if (includeBuiltIn)
            {
                services = BuiltInFunctionServices
                    .ToDictionary(s => s.FunctionCode, s => s);
            }
            else
            {
                services = new Dictionary<byte, IModbusFunctionService>();
            }

            if (functionServices != null)
            {
                foreach (IModbusFunctionService service in functionServices)
                {
                    services[service.FunctionCode] = service;
                }
            }

            _functionServices = services.ToFrozenDictionary();
        }

        public IModbusSlave CreateSlave(byte unitId, ISlaveDataStore dataStore = null)
        {
            if (dataStore == null)
                dataStore = new DefaultSlaveDataStore();

            return new ModbusSlave(unitId, dataStore, GetAllFunctionServices());
        }

        public IModbusSlaveNetwork CreateSlaveNetwork(IModbusRtuTransport transport)
        {
            return new ModbusSerialSlaveNetwork(transport, this, Logger);
        }

        public IModbusSlaveNetwork CreateSlaveNetwork(IModbusAsciiTransport transport)
        {
            return new ModbusSerialSlaveNetwork(transport, this, Logger);
        }

        public IModbusTcpSlaveNetwork CreateSlaveNetwork(TcpListener tcpListener)
        {
            return new ModbusTcpSlaveNetwork(tcpListener, this, Logger);
        }

        public IModbusSlaveNetwork CreateSlaveNetwork(UdpClient client)
        {
            return new ModbusUdpSlaveNetwork(client, this, Logger);
        }

        public IModbusRtuTransport CreateRtuTransport(IStreamResource streamResource)
        {
            return new ModbusRtuTransport(streamResource, this, Logger);
        }

        public IModbusAsciiTransport CreateAsciiTransport(IStreamResource streamResource)
        {
            return new ModbusAsciiTransport(streamResource, this, Logger);
        }

        public IModbusTransport CreateIpTransport(IStreamResource streamResource)
        {
            return new ModbusIpTransport(streamResource, this, Logger);
        }

        public IModbusLogger Logger { get; }

        public IModbusFunctionService[] GetAllFunctionServices()
        {
            return _functionServices
                .Values
                .ToArray();
        }

        public IModbusSerialMaster CreateMaster(IModbusSerialTransport transport)
        {
            return new ModbusSerialMaster(transport);
        }

        public IModbusMaster CreateMaster(UdpClient client)
        {
            var adapter = new UdpClientAdapter(client);

            var transport = new ModbusIpTransport(adapter, this, Logger);

            return new ModbusIpMaster(transport);
        }

        public IModbusMaster CreateMaster(TcpClient client)
        {
            var adapter = new TcpClientAdapter(client);

            var transport = new ModbusIpTransport(adapter, this, Logger);

            return new ModbusIpMaster(transport);
        }

        public IModbusMaster CreateMaster(Socket client)
        {
            var adapter = new SocketAdapter(client);

            var transport = new ModbusRtuTransport(adapter, this, Logger);

            return new ModbusSerialMaster(transport);
        }

        public IModbusFunctionService GetFunctionService(byte functionCode)
        {
            _functionServices.TryGetValue(functionCode, out var service);
            return service;
        }
    }
}