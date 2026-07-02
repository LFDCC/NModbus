using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NModbus.Message;

namespace NModbus.Device 
{
    public class ModbusSlave : IModbusSlave
    {
        private readonly byte _unitId;
        private readonly ISlaveDataStore _dataStore;

        private readonly FrozenDictionary<byte, IModbusFunctionService> _handlers;

        public ModbusSlave(byte unitId, ISlaveDataStore dataStore, IEnumerable<IModbusFunctionService> handlers)
        {
            if (dataStore == null) throw new ArgumentNullException(nameof(dataStore));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            _unitId = unitId;
            _dataStore = dataStore;
            _handlers = handlers.ToDictionary(h => h.FunctionCode, h => h).ToFrozenDictionary();
        }

        public byte UnitId => _unitId;

        public ISlaveDataStore DataStore => _dataStore;

        public IModbusMessage ApplyRequest(IModbusMessage request)
        {
            IModbusMessage response;

            try
            {
                //Try to get a handler for this function.
                _handlers.TryGetValue(request.FunctionCode, out var handler);

                //Check to see if we found a handler for this function code.
                if (handler == null)
                {
                    throw new InvalidModbusRequestException(SlaveExceptionCodes.IllegalFunction);
                }

                //Process the request
                response = handler.HandleSlaveRequest(request, DataStore);
            }
            catch (InvalidModbusRequestException ex)
            {
                // Catches the exception for an illegal function or a custom exception from the ModbusSlaveRequestReceived event.
                response = new SlaveExceptionResponse(
                    request.SlaveAddress,
                    (byte) (Modbus.ExceptionOffset + request.FunctionCode),
                    ex.ExceptionCode);
            }
#if NET45 || NET46
            catch (Exception ex)
            {
                //Okay - this is no beuno.
                response = new SlaveExceptionResponse(request.SlaveAddress,
                    (byte) (Modbus.ExceptionOffset + request.FunctionCode),
                    SlaveExceptionCodes.SlaveDeviceFailure);

                //Give the consumer a chance at seeing what the *(&& happened.
                Trace.WriteLine(ex.ToString());
            }
#else
            catch (Exception)
            {
                //Okay - this is no beuno.
                response = new SlaveExceptionResponse(request.SlaveAddress,
                    (byte)(Modbus.ExceptionOffset + request.FunctionCode),
                    SlaveExceptionCodes.SlaveDeviceFailure);
            }
#endif


            return response;
        }
    }
}