using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;
using Shouldly;
using Xunit;

namespace Modbus.IntegrationTests
{
    /// <summary>
    ///     Locks in the async TCP timeout behavior:
    ///     <list type="bullet">
    ///         <item><description>the async path honors <c>Transport.ReadTimeout</c> (the sync path already
    ///         did; the async path used to hang until the OS TCP stack gave up — tens of seconds);</description></item>
    ///         <item><description>it surfaces the same timeout-shaped exception as the sync path
    ///         (<c>IOException</c> with an inner <c>SocketException</c> whose code is <c>TimedOut</c>);</description></item>
    ///         <item><description>on timeout, the underlying <c>TcpClient</c> is FORCE-CLOSED. This is
    ///         necessary because <c>NetworkStream.ReadAsync</c> on .NET 6 does not reliably honor
    ///         cancellation to actually abort the in-flight receive — leaving it pending serializes
    ///         the next read behind the dead one, and the next request's response can be delivered
    ///         to the orphaned read instead of the new one. The trade-off is that
    ///         <c>Transport.Retries</c> cannot reuse the same socket after a timeout; the caller
    ///         must reconnect.</description></item>
    ///     </list>
    /// </summary>
    public class TcpAsyncTimeoutTests
    {
        private static readonly IModbusFactory Factory = new ModbusFactory();

        [Fact]
        public async Task ReadHoldingRegistersAsync_SlaveNeverResponds_ThrowsTimeout()
        {
            using var server = new SilentTcpServer();
            using var master = CreateMaster(server.Port, retries: 0);

            var exception = await Should.ThrowAsync<IOException>(
                () => master.ReadHoldingRegistersAsync(1, 0, 1));

            // Same shape as a synchronous NetworkStream read timeout.
            var inner = exception.InnerException.ShouldBeOfType<SocketException>();
            inner.SocketErrorCode.ShouldBe(SocketError.TimedOut);
        }

        [Fact]
        public async Task ReadHoldingRegistersAsync_AfterTimeout_SocketIsClosed_NotHung()
        {
            // Regression guard for the orphaned-read race: prior to the force-close fix, the orphaned
            // in-flight read on timeout serialized subsequent reads on the same socket, so the next
            // request would also time out (and may even pick up bytes destined for the previous orphan).
            // After the fix, the socket is closed on timeout so the next operation fails immediately
            // with ObjectDisposedException, not by hanging behind a dead read.
            using var server = new SilentTcpServer();
            using var master = CreateMaster(server.Port, retries: 0);

            await Should.ThrowAsync<IOException>(() => master.ReadHoldingRegistersAsync(1, 0, 1));

            // Second call must fail FAST (not hang for the timeout period) because the socket is closed.
            var stopwatch = Stopwatch.StartNew();
            await Should.ThrowAsync<ObjectDisposedException>(
                () => master.ReadHoldingRegistersAsync(2, 0, 1));
            stopwatch.Stop();

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task ReadTimeout_BoundsAsyncRead_InsteadOfHangingOnTheOsTcpTimeout()
        {
            using var server = new SilentTcpServer();
            using var master = CreateMaster(server.Port, retries: 0);

            var stopwatch = Stopwatch.StartNew();
            await Should.ThrowAsync<IOException>(() => master.ReadHoldingRegistersAsync(1, 0, 1));
            stopwatch.Stop();

            // ReadTimeout is 500 ms; it must fire well before the OS TCP stack would (tens of seconds).
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task CallerCancellation_SurfacesAsOperationCanceled_NotTimeout()
        {
            using var server = new SilentTcpServer();
            // No ReadTimeout configured: the caller's token is the only deadline.
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, server.Port);
            using var master = Factory.CreateMaster(client);
            master.Transport.Retries = 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            await Should.ThrowAsync<OperationCanceledException>(
                () => master.ReadHoldingRegistersAsync(1, 0, 1, cts.Token));
        }

        private static IModbusMaster CreateMaster(int port, int retries)
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);

            var master = Factory.CreateMaster(client);
            master.Transport.Retries = retries;
            master.Transport.ReadTimeout = 500;
            master.Transport.WriteTimeout = 500;

            return master;
        }

        /// <summary>
        ///     A TCP server that accepts connections and drains incoming bytes but never replies — simulating a
        ///     gateway/slave that silently drops requests for an unknown unit id.
        /// </summary>
        private sealed class SilentTcpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public SilentTcpServer()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _ = AcceptLoopAsync(_cts.Token);
            }

            public int Port { get; }

            private async Task AcceptLoopAsync(CancellationToken cancellationToken)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        _ = DrainAsync(client, cancellationToken);
                    }
                }
                catch
                {
                    // listener stopped during Dispose
                }
            }

            private static async Task DrainAsync(TcpClient client, CancellationToken cancellationToken)
            {
                try
                {
                    var buffer = new byte[256];
                    var stream = client.GetStream();

                    // Read (and discard) requests forever without ever sending a response.
                    while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
                    {
                    }
                }
                catch
                {
                    // connection closed / cancelled
                }
                finally
                {
                    client.Dispose();
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _cts.Dispose();
            }
        }
    }
}
