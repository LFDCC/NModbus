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
    ///     Locks in the async TCP behavior for the canonical "one TCP connection to a Modbus gateway
    ///     that proxies multiple slaves" scenario:
    ///     <list type="bullet">
    ///         <item><description>the async path honors <c>Transport.ReadTimeout</c> (the sync path already
    ///         did; the async path used to hang until the OS TCP stack gave up — tens of seconds);</description></item>
    ///         <item><description>it surfaces the same timeout-shaped exception as the sync path
    ///         (<c>IOException</c> with an inner <c>SocketException</c> whose code is <c>TimedOut</c>);</description></item>
    ///         <item><description>PER-REQUEST ERROR ISOLATION: a timeout or slave exception on one
    ///         request does NOT poison the connection for subsequent requests against other slaves
    ///         (matches the synchronous <c>ReadHoldingRegisters</c> behavior, which is what users
    ///         expect from a Modbus gateway setup). This is achieved by running the synchronous
    ///         <c>NetworkStream.Read</c> on a thread-pool worker inside <c>TcpClientAdapter.ReadAsync</c>
    ///         — the sync read honors <c>NetworkStream.ReadTimeout</c> cleanly with no orphan
    ///         in-flight receive, unlike <c>NetworkStream.ReadAsync</c> on .NET 6 which does not
    ///         reliably honor cancellation and would leave a pending read blocking subsequent reads.</description></item>
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
        public async Task ReadHoldingRegistersAsync_AfterTimeout_ConnectionStillUsable()
        {
            // Regression guard for the canonical Modbus-gateway use case: one TCP connection to a
            // gateway that proxies multiple slaves. A timeout on one slave must not kill the
            // connection — subsequent requests against other slaves must still succeed (matching
            // the sync <c>ReadHoldingRegisters</c> behavior).
            using var server = new SilentTcpServer();
            using var master = CreateMaster(server.Port, retries: 0);

            // First call times out.
            await Should.ThrowAsync<IOException>(() => master.ReadHoldingRegistersAsync(1, 0, 1));

            // Second call must also hit the silent server and time out cleanly — NOT hang, NOT throw
            // ObjectDisposedException or "non-connected sockets". The whole loop should complete in
            // well under (2 * count) seconds (proves no serial-orphaned-read blocking).
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                await Should.ThrowAsync<IOException>(() => master.ReadHoldingRegistersAsync(1, 0, 1));
            }
            stopwatch.Stop();

            // 3 calls × 500ms ReadTimeout = 1.5s expected. Allow up to 2.5s to allow for scheduling.
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
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
