using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus.IO;
using NModbus.Message;
using NModbus.Logging;

namespace NModbus.Device
{
    using Extensions;

    /// <summary>
    /// Represents an incoming connection from a Modbus master. Contains the slave's logic to process the connection.
    /// </summary>
    internal class ModbusMasterTcpConnection : ModbusDevice, IDisposable
    {

        private readonly TcpClient _client;
        private readonly string _endPoint;
        private readonly Stream _stream;
        private readonly IModbusSlaveNetwork _slaveNetwork;
        private readonly IModbusFactory _modbusFactory;
        private readonly Task _requestHandlerTask;
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

        private readonly byte[] _mbapHeader = new byte[6];
        private byte[] _messageFrame;

        public ModbusMasterTcpConnection(TcpClient client, IModbusSlaveNetwork slaveNetwork, IModbusFactory modbusFactory, IModbusLogger logger)
            : base(new ModbusIpTransport(new TcpClientAdapter(client), modbusFactory, logger))
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _endPoint = client.Client.RemoteEndPoint.ToString();
            _stream = client.GetStream();
            _slaveNetwork = slaveNetwork ?? throw new ArgumentNullException(nameof(slaveNetwork));
            _modbusFactory = modbusFactory ?? throw new ArgumentNullException(nameof(modbusFactory));
            // Start the handler task but observe its exceptions so they never escape unobserved.
            _requestHandlerTask = Task.Run(HandleRequestAsync);
        }

        /// <summary>
        ///     Occurs when a Modbus master TCP connection is closed.
        /// </summary>
        public event EventHandler<TcpConnectionEventArgs> ModbusMasterTcpConnectionClosed;

        public IModbusLogger Logger { get; }

        public string EndPoint => _endPoint;

        public Stream Stream => _stream;

        public TcpClient TcpClient => _client;

        /// <summary>
        ///     The task that handles incoming requests from the master. Observing this task
        ///     ensures any unhandled exceptions are not lost to the unobserved-task handler.
        /// </summary>
        public Task RequestHandlerTask => _requestHandlerTask;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Signal the handler loop to stop, then close the stream so any in-flight
                // ReadAsync returns/breaks. The handler will observe the cancellation or
                // the disposed-stream exception and exit gracefully.
                _disposeCts.Cancel();

                try
                {
                    _stream.Dispose();
                }
                catch
                {
                    // Suppress — disposing twice is fine.
                }

                // Best-effort wait for the handler loop to observe the disposal and exit.
                // Avoids ObjectDisposedException races where the handler still touches _stream.
                try
                {
                    _requestHandlerTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // AggregateException from the handler is expected during shutdown.
                }

                _disposeCts.Dispose();
            }

            base.Dispose(disposing);
        }

        private async Task HandleRequestAsync()
        {
            // Observe the disposal token: when disposal cancels, we stop the loop and exit.
            CancellationToken disposeToken = _disposeCts.Token;

            try
            {
               while (!disposeToken.IsCancellationRequested)
               {
                   Logger.Debug($"Begin reading header from Master at IP: {EndPoint}");

                   // Read the full 6-byte MBAP header — loop to guard against short reads
                   int totalRead = 0;
                   while (totalRead < 6)
                   {
                       int readBytes = await Stream.ReadAsync(_mbapHeader, totalRead, 6 - totalRead, disposeToken).ConfigureAwait(false);
                       if (readBytes == 0)
                       {
                           Logger.Debug($"0 bytes read, Master at {EndPoint} has closed Socket connection.");
                           RaiseConnectionClosed();
                           return;
                       }
                       totalRead += readBytes;
                   }

                   ushort frameLength = BinaryPrimitives.ReadUInt16BigEndian(_mbapHeader.AsSpan(4));

                   // Validate MBAP Length field: must be 1..260 (Unit ID + PDU; PDU max = 253)
                   if (frameLength == 0 || frameLength > 260)
                   {
                       Logger.Warning($"Master at {EndPoint} sent invalid MBAP frame length {frameLength}. Closing connection.");
                       RaiseConnectionClosed();
                       return;
                   }

                   Logger.Debug($"Master at {EndPoint} sent header: \"{string.Join(", ", _mbapHeader)}\" with {frameLength} bytes in PDU");

                   // Read the full PDU — loop to guard against short reads
                   _messageFrame = new byte[frameLength];
                   totalRead = 0;
                   while (totalRead < frameLength)
                   {
                       int readBytes = await Stream.ReadAsync(_messageFrame, totalRead, frameLength - totalRead, disposeToken).ConfigureAwait(false);
                       if (readBytes == 0)
                       {
                           Logger.Debug($"0 bytes read, Master at {EndPoint} has closed Socket connection.");
                           RaiseConnectionClosed();
                           return;
                       }
                       totalRead += readBytes;
                   }

                   Logger.Debug($"Read frame from Master at {EndPoint} completed {frameLength} bytes");
                   byte[] frame = new byte[6 + frameLength];
                   _mbapHeader.CopyTo(frame, 0);
                   _messageFrame.CopyTo(frame, 6);

                   var request = _modbusFactory.CreateModbusRequest(_messageFrame);
                   request.TransactionId = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(0));

                   IModbusSlave slave = _slaveNetwork.GetSlave(request.SlaveAddress);

                   if (slave != null)
                   {
                       //TODO: Determine if this is appropriate

                       // perform action and build response
                       IModbusMessage response = slave.ApplyRequest(request);
                       response.TransactionId = request.TransactionId;

                       // write response
                       byte[] responseFrame = Transport.BuildMessageFrame(response);
                       Logger.Information($"TX to Master at {EndPoint}: {string.Join(", ", responseFrame)}");
                       await Stream.WriteAsync(responseFrame, 0, responseFrame.Length, disposeToken).ConfigureAwait(false);
                   }
               }
            }
            catch (OperationCanceledException) when (disposeToken.IsCancellationRequested)
            {
                // Expected during disposal — exit quietly.
            }
            // If an exception occurs (such as IOException in case of disconnect, or other failures), handle it as if the connection was gracefully closed
            catch (Exception e)
            {
                // During shutdown, ObjectDisposedException / IOException are expected — log at Debug, not Warning.
                if (disposeToken.IsCancellationRequested)
                {
                    Logger.Debug($"Connection to Master at {EndPoint} closed during shutdown: {e.GetType().Name}");
                }
                else
                {
                    Logger.Warning($"{e.GetType().Name} occured with Master at {EndPoint}. Closing connection.");
                }
            }
            finally
            {
                // Always raise the closed event exactly once — the handler guarantees no duplicate raises.
                RaiseConnectionClosed();
            }
        }

        private int _closedRaised; // 0 = not raised, 1 = raised

        /// <summary>
        ///     Raises <see cref="ModbusMasterTcpConnectionClosed"/> exactly once, even if the handler
        ///     loop exits through multiple paths (header EOF, PDU EOF, exception, disposal).
        /// </summary>
        private void RaiseConnectionClosed()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _closedRaised, 1, 0) == 0)
            {
                ModbusMasterTcpConnectionClosed?.Invoke(this, new TcpConnectionEventArgs(EndPoint));
            }
        }
    }
}
