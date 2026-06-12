using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;
using NModbus.Data;
using NModbus.Device;
using NModbus.IO;
using NModbus.Logging;
using NModbus.Utility;
using Xunit;
using Xunit.Abstractions;

namespace Modbus.IntegrationTests
{
    /// <summary>
    /// Performance benchmarks comparing sync vs async and old vs Span/Memory patterns.
    /// </summary>
    public class PerformanceBenchmarks : IDisposable
    {
        private const byte SlaveAddress = 1;
        private const int Port = 5020;
        private static readonly IPAddress IpAddress = IPAddress.Loopback;

        private readonly IModbusFactory _factory = new ModbusFactory();
        private readonly ITestOutputHelper _output;

        public PerformanceBenchmarks(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
        }

        [Fact]
        public async Task Benchmark_SyncVsAsync_ReadHoldingRegisters()
        {
            const int warmupOps = 200;
            const int benchOps = 5000;
            const ushort registerCount = 125;

            var dataStore = new DefaultSlaveDataStore();
            var values = new ushort[registerCount];
            for (int i = 0; i < registerCount; i++) values[i] = (ushort)(i + 1);
            dataStore.HoldingRegisters.WritePoints(0, values);

            var syncResult = await RunMasterSlaveBenchmark(
                "Sync ReadHoldingRegisters",
                warmupOps, benchOps,
                (master, ct) =>
                {
                    var regs = master.ReadHoldingRegisters(SlaveAddress, 0, registerCount);
                    return Task.CompletedTask;
                },
                dataStore);

            var asyncResult = await RunMasterSlaveBenchmark(
                "Async ReadHoldingRegisters",
                warmupOps, benchOps,
                async (master, ct) =>
                {
                    var regs = await master.ReadHoldingRegistersAsync(SlaveAddress, 0, registerCount, ct)
                        .ConfigureAwait(false);
                },
                dataStore);

            _output.WriteLine("");
            _output.WriteLine("=== ReadHoldingRegisters (125 regs) - Sync vs Async ===");
            PrintComparison(syncResult, asyncResult);
        }

        [Fact]
        public async Task Benchmark_SyncVsAsync_WriteMultipleRegisters()
        {
            const int warmupOps = 200;
            const int benchOps = 5000;
            const ushort registerCount = 100;
            var writeData = new ushort[registerCount];
            for (int i = 0; i < registerCount; i++) writeData[i] = (ushort)(i + 100);

            var dataStore = new DefaultSlaveDataStore();

            var syncResult = await RunMasterSlaveBenchmark(
                "Sync WriteMultipleRegisters",
                warmupOps, benchOps,
                (master, ct) =>
                {
                    master.WriteMultipleRegisters(SlaveAddress, 0, writeData);
                    return Task.CompletedTask;
                },
                dataStore);

            var asyncResult = await RunMasterSlaveBenchmark(
                "Async WriteMultipleRegisters",
                warmupOps, benchOps,
                async (master, ct) =>
                {
                    await master.WriteMultipleRegistersAsync(SlaveAddress, 0, writeData, ct)
                        .ConfigureAwait(false);
                },
                dataStore);

            _output.WriteLine("");
            _output.WriteLine("=== WriteMultipleRegisters (100 regs) - Sync vs Async ===");
            PrintComparison(syncResult, asyncResult);
        }

        [Fact]
        public async Task Benchmark_SyncVsAsync_MixedOperations()
        {
            const int warmupOps = 100;
            const int benchOps = 2000;
            var writeData = new ushort[] { 100, 200, 300, 400, 500 };

            var dataStore = new DefaultSlaveDataStore();
            dataStore.HoldingRegisters.WritePoints(0, new ushort[] { 1, 2, 3, 4, 5 });

            var syncResult = await RunMasterSlaveBenchmark(
                "Sync Mixed (Read5+Write5+ReadCoils)",
                warmupOps, benchOps,
                (master, ct) =>
                {
                    var regs = master.ReadHoldingRegisters(SlaveAddress, 0, 5);
                    master.WriteMultipleRegisters(SlaveAddress, 100, writeData);
                    var coils = master.ReadCoils(SlaveAddress, 0, 10);
                    return Task.CompletedTask;
                },
                dataStore);

            var asyncResult = await RunMasterSlaveBenchmark(
                "Async Mixed (Read5+Write5+ReadCoils)",
                warmupOps, benchOps,
                async (master, ct) =>
                {
                    var regs = await master.ReadHoldingRegistersAsync(SlaveAddress, 0, 5, ct).ConfigureAwait(false);
                    await master.WriteMultipleRegistersAsync(SlaveAddress, 100, writeData, ct).ConfigureAwait(false);
                    var coils = await master.ReadCoilsAsync(SlaveAddress, 0, 10, ct).ConfigureAwait(false);
                },
                dataStore);

            _output.WriteLine("");
            _output.WriteLine("=== Mixed Operations (Read+Write+ReadCoils) - Sync vs Async ===");
            PrintComparison(syncResult, asyncResult);
        }

        [Fact]
        public void Benchmark_Crc_OldVsSpan()
        {
            const int iterations = 500_000;
            byte[] data = new byte[256];
            var rng = new Random(42);
            rng.NextBytes(data);

            // Warmup
            for (int i = 0; i < 1000; i++)
            {
                ModbusUtility.CalculateCrc(data);
                ModbusUtility.CalculateCrc(data.AsSpan());
            }

            // Old: byte[] overload (returns byte[] via BitConverter.GetBytes)
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                byte[] crc = ModbusUtility.CalculateCrc(data);
            }
            sw.Stop();
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            long oldAlloc = allocAfter - allocBefore;
            long oldMs = sw.ElapsedMilliseconds;

            // New: ushort overload (Span-based, zero allocation)
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            allocBefore = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                ushort crc = ModbusUtility.CalculateCrc(data.AsSpan());
            }
            sw.Stop();
            allocAfter = GC.GetAllocatedBytesForCurrentThread();
            long spanAlloc = allocAfter - allocBefore;
            long spanMs = sw.ElapsedMilliseconds;

            _output.WriteLine("");
            _output.WriteLine("=== CRC Calculation (256B payload, 500K iter) ===");
            _output.WriteLine("  Old (byte[] + BitConverter.GetBytes):  " + oldMs + " ms, Alloc: " + (oldAlloc / 1024) + " KB");
            _output.WriteLine("  New (ushort + Span):                    " + spanMs + " ms, Alloc: " + (spanAlloc / 1024) + " KB");
            _output.WriteLine("  Speed: " + ((double)oldMs / Math.Max(spanMs, 1)).ToString("N2") + "x, Memory: " + ((double)oldAlloc / Math.Max(spanAlloc, 1)).ToString("N1") + "x reduction");
            _output.WriteLine("");
        }

        [Fact]
        public void Benchmark_RegisterCollection_OldVsSpan()
        {
            const int iterations = 200_000;
            const int registerCount = 125;
            var registers = new ushort[registerCount];
            for (int i = 0; i < registerCount; i++) registers[i] = (ushort)(i + 1);
            var collection = new RegisterCollection(registers);

            // Warmup
            for (int i = 0; i < 1000; i++)
            {
                var _ = collection.NetworkBytes;
            }

            // Old: NetworkBytes property (MemoryStream + BitConverter.GetBytes per register)
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var bytes = collection.NetworkBytes;
            }
            sw.Stop();
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            long oldAlloc = allocAfter - allocBefore;
            long oldMs = sw.ElapsedMilliseconds;

            // New: WriteNetworkBytes (Span + BinaryPrimitives)
            byte[] buffer = new byte[registerCount * 2];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            allocBefore = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                collection.WriteNetworkBytes(buffer);
            }
            sw.Stop();
            allocAfter = GC.GetAllocatedBytesForCurrentThread();
            long spanAlloc = allocAfter - allocBefore;
            long spanMs = sw.ElapsedMilliseconds;

            _output.WriteLine("");
            _output.WriteLine("=== RegisterCollection Serialization (125 regs, 200K iter) ===");
            _output.WriteLine("  Old (MemoryStream+BitConverter):  " + oldMs + " ms, Alloc: " + (oldAlloc / 1024) + " KB");
            _output.WriteLine("  New (Span+BinaryPrimitives):      " + spanMs + " ms, Alloc: " + (spanAlloc / 1024) + " KB");
            _output.WriteLine("  Speed: " + ((double)oldMs / Math.Max(spanMs, 1)).ToString("N2") + "x, Memory: " + ((double)oldAlloc / Math.Max(spanAlloc, 1)).ToString("N1") + "x reduction");
            _output.WriteLine("");
        }

        [Fact]
        public async Task Benchmark_FullReport_SyncVsAsync()
        {
            _output.WriteLine("");
            _output.WriteLine("==========================================================");
            _output.WriteLine("  NModbus Performance Benchmark Report");
            _output.WriteLine("  Native Async + Span/Memory Zero-Copy Optimization");
            _output.WriteLine("==========================================================");

            // Test 1: Read registers
            var readDataStore = new DefaultSlaveDataStore();
            var readValues = new ushort[125];
            for (int i = 0; i < 125; i++) readValues[i] = (ushort)(i + 1);
            readDataStore.HoldingRegisters.WritePoints(0, readValues);

            var syncRead = await RunMasterSlaveBenchmark("Sync", 300, 8000,
                (m, ct) => { m.ReadHoldingRegisters(SlaveAddress, 0, 125); return Task.CompletedTask; },
                readDataStore);

            var asyncRead = await RunMasterSlaveBenchmark("Async", 300, 8000,
                async (m, ct) => { await m.ReadHoldingRegistersAsync(SlaveAddress, 0, 125, ct).ConfigureAwait(false); },
                readDataStore);

            _output.WriteLine("");
            _output.WriteLine("--- ReadHoldingRegisters (125 regs, 8000 ops) ---");
            PrintComparison(syncRead, asyncRead);

            // Test 2: Write registers
            var writeDataStore = new DefaultSlaveDataStore();
            var writeData = new ushort[100];
            for (int i = 0; i < 100; i++) writeData[i] = (ushort)(i + 100);

            var syncWrite = await RunMasterSlaveBenchmark("Sync", 300, 8000,
                (m, ct) => { m.WriteMultipleRegisters(SlaveAddress, 0, writeData); return Task.CompletedTask; },
                writeDataStore);

            var asyncWrite = await RunMasterSlaveBenchmark("Async", 300, 8000,
                async (m, ct) => { await m.WriteMultipleRegistersAsync(SlaveAddress, 0, writeData, ct).ConfigureAwait(false); },
                writeDataStore);

            _output.WriteLine("--- WriteMultipleRegisters (100 regs, 8000 ops) ---");
            PrintComparison(syncWrite, asyncWrite);

            // Test 3: CRC benchmark
            _output.WriteLine("--- CRC Calculation (500K iterations) ---");
            Benchmark_Crc_OldVsSpan();

            // Test 4: RegisterCollection benchmark
            _output.WriteLine("--- RegisterCollection Serialization (200K iterations) ---");
            Benchmark_RegisterCollection_OldVsSpan();

            _output.WriteLine("==========================================================");
            _output.WriteLine("  Benchmark Complete");
            _output.WriteLine("==========================================================");
        }

        #region Helper Methods

        private async Task<BenchmarkResult> RunMasterSlaveBenchmark(
            string name,
            int warmupOps,
            int benchOps,
            Func<IModbusMaster, CancellationToken, Task> operation,
            DefaultSlaveDataStore dataStore)
        {
            var slave = _factory.CreateSlave(SlaveAddress, dataStore);
            var listener = new TcpListener(IpAddress, Port);
            var slaveNetwork = new ModbusTcpSlaveNetwork(listener, _factory, NullModbusLogger.Instance);
            slaveNetwork.AddSlave(slave);

            using var cts = new CancellationTokenSource();
            var listenTask = Task.Run(() => slaveNetwork.ListenAsync(cts.Token));
            await Task.Delay(200);

            var client = new TcpClient();
            client.Connect(IpAddress, Port);
            var master = _factory.CreateMaster(client);

            try
            {
                // Warmup
                for (int i = 0; i < warmupOps; i++)
                {
                    await operation(master, cts.Token).ConfigureAwait(false);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long gcAllocBefore = GC.GetAllocatedBytesForCurrentThread();
                int gen0Before = GC.CollectionCount(0);
                int gen1Before = GC.CollectionCount(1);
                int gen2Before = GC.CollectionCount(2);

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < benchOps; i++)
                {
                    await operation(master, cts.Token).ConfigureAwait(false);
                }
                sw.Stop();

                long gcAllocAfter = GC.GetAllocatedBytesForCurrentThread();
                int gen0After = GC.CollectionCount(0);
                int gen1After = GC.CollectionCount(1);
                int gen2After = GC.CollectionCount(2);

                return new BenchmarkResult
                {
                    Name = name,
                    Operations = benchOps,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    AllocatedBytes = gcAllocAfter - gcAllocBefore,
                    Gen0Collections = gen0After - gen0Before,
                    Gen1Collections = gen1After - gen1Before,
                    Gen2Collections = gen2After - gen2Before
                };
            }
            finally
            {
                client.Dispose();
                cts.Cancel();
                try { await listenTask.ConfigureAwait(false); } catch { }
                slaveNetwork.Dispose();
            }
        }

        private void PrintComparison(BenchmarkResult sync, BenchmarkResult asyncResult)
        {
            double syncOpsPerSec = sync.Operations / (sync.ElapsedMs / 1000.0);
            double asyncOpsPerSec = asyncResult.Operations / (asyncResult.ElapsedMs / 1000.0);
            double throughputGain = asyncOpsPerSec / syncOpsPerSec;
            double syncAllocPerOp = sync.AllocatedBytes / (double)sync.Operations;
            double asyncAllocPerOp = asyncResult.AllocatedBytes / (double)asyncResult.Operations;
            double allocReduction = syncAllocPerOp / Math.Max(asyncAllocPerOp, 1);

            _output.WriteLine("  " + "Metric".PadRight(30) + "Sync".PadLeft(12) + "Async".PadLeft(12));
            _output.WriteLine("  " + new string('-', 54));
            _output.WriteLine("  " + "Total time".PadRight(30) + (sync.ElapsedMs + " ms").PadLeft(12) + (asyncResult.ElapsedMs + " ms").PadLeft(12));
            _output.WriteLine("  " + "Ops/sec".PadRight(30) + syncOpsPerSec.ToString("N0").PadLeft(12) + asyncOpsPerSec.ToString("N0").PadLeft(12));
            _output.WriteLine("  " + "Alloc/op".PadRight(30) + (syncAllocPerOp.ToString("N0") + " B").PadLeft(12) + (asyncAllocPerOp.ToString("N0") + " B").PadLeft(12));
            _output.WriteLine("  " + "GC Gen0".PadRight(30) + sync.Gen0Collections.ToString().PadLeft(12) + asyncResult.Gen0Collections.ToString().PadLeft(12));
            _output.WriteLine("  " + "GC Gen1".PadRight(30) + sync.Gen1Collections.ToString().PadLeft(12) + asyncResult.Gen1Collections.ToString().PadLeft(12));
            _output.WriteLine("  " + "GC Gen2".PadRight(30) + sync.Gen2Collections.ToString().PadLeft(12) + asyncResult.Gen2Collections.ToString().PadLeft(12));
            _output.WriteLine("");
            _output.WriteLine("  >> Async throughput gain:  " + throughputGain.ToString("N2") + "x");
            _output.WriteLine("  >> Alloc/op reduction:     " + allocReduction.ToString("N1") + "x");
            _output.WriteLine("");
        }

        #endregion

        private class BenchmarkResult
        {
            public string Name { get; set; }
            public int Operations { get; set; }
            public long ElapsedMs { get; set; }
            public long AllocatedBytes { get; set; }
            public int Gen0Collections { get; set; }
            public int Gen1Collections { get; set; }
            public int Gen2Collections { get; set; }
        }
    }
}
