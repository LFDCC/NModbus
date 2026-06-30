using NModbus;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NModbus.Device
{
    /// <summary>
    /// Provides concurrency control across multiple Modbus readers/writers.
    /// </summary>
    public class ConcurrentModbusMaster : IConcurrentModbusMaster
    {
        private readonly IModbusMaster _master;
        private readonly TimeSpan _minInterval;

        private bool _isDisposed;

        private readonly Stopwatch _stopwatch = new Stopwatch();

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public ConcurrentModbusMaster(IModbusMaster master, TimeSpan minInterval)
        {
            _master = master ?? throw new ArgumentNullException(nameof(master));
            _minInterval = minInterval;

            _stopwatch.Start();
        }

        private Task WaitAsync(CancellationToken cancellationToken)
        {
            int difference = (int)(_minInterval - _stopwatch.Elapsed).TotalMilliseconds;

            if (difference > 0)
            {
                return Task.Delay(difference, cancellationToken);
            }

            return Task.CompletedTask;
        }

        private async Task<T> PerformFuncAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            T value = default;

            await PerformAsync(async () => value = await action().ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

            return value;
        }

        private async Task PerformAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await WaitAsync(cancellationToken).ConfigureAwait(false);

                await action().ConfigureAwait(false);
            }
            finally
            {
                _stopwatch.Restart();
                _semaphore.Release();
            }
        }

        public async Task<ushort[]> ReadInputRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, ushort blockSize, CancellationToken cancellationToken)
        {
            return await PerformFuncAsync(async () =>
            {
                // Pre-allocate the result buffer; no intermediate List allocation.
                ushort[] result = new ushort[numberOfPoints];

                int soFar = 0;
                int thisRead = blockSize;

                while (soFar < numberOfPoints)
                {
                    // If we're _not_ on the first run through here, wait for the min time
                    if (soFar > 0)
                    {
                        await Task.Delay(_minInterval, cancellationToken).ConfigureAwait(false);
                    }

                    // Check to see if we've been cancelled
                    cancellationToken.ThrowIfCancellationRequested();

                    if (thisRead > (numberOfPoints - soFar))
                    {
                        thisRead = numberOfPoints - soFar;
                    }

                    // Perform this operation
                    ushort[] registersFromThisRead = await _master.ReadInputRegistersAsync(
                        slaveAddress,
                        (ushort)(startAddress + soFar),
                        (ushort)thisRead,
                        cancellationToken).ConfigureAwait(false);

                    // Copy into the pre-allocated result buffer
                    Buffer.BlockCopy(registersFromThisRead, 0, result, soFar * 2, thisRead * 2);

                    // Increment where we're at
                    soFar += thisRead;
                }

                return result;

            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, ushort blockSize, CancellationToken cancellationToken)
        {
            return await PerformFuncAsync(async () =>
            {
                // Pre-allocate the result buffer; no intermediate List allocation.
                ushort[] result = new ushort[numberOfPoints];

                int soFar = 0;
                int thisRead = blockSize;

                while (soFar < numberOfPoints)
                {
                    // If we're _not_ on the first run through here, wait for the min time
                    if (soFar > 0)
                    {
                        await Task.Delay(_minInterval, cancellationToken).ConfigureAwait(false);
                    }

                    // Check to see if we've been cancelled
                    cancellationToken.ThrowIfCancellationRequested();

                    if (thisRead > (numberOfPoints - soFar))
                    {
                        thisRead = numberOfPoints - soFar;
                    }

                    // Perform this operation
                    ushort[] registersFromThisRead = await _master.ReadHoldingRegistersAsync(
                        slaveAddress,
                        (ushort)(startAddress + soFar),
                        (ushort)thisRead,
                        cancellationToken).ConfigureAwait(false);

                    // Copy into the pre-allocated result buffer
                    Buffer.BlockCopy(registersFromThisRead, 0, result, soFar * 2, thisRead * 2);

                    // Increment where we're at
                    soFar += thisRead;
                }

                return result;

            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] data, ushort blockSize, CancellationToken cancellationToken)
        {
            await PerformAsync(async () =>
            {
                int soFar = 0;
                int thisWrite = blockSize;

                while (soFar < data.Length)
                {
                    // If we're _not_ on the first run through here, wait for the min time
                    if (soFar > 0)
                    {
                        await Task.Delay(_minInterval, cancellationToken).ConfigureAwait(false);
                    }

                    if (thisWrite > (data.Length - soFar))
                    {
                        thisWrite = data.Length - soFar;
                    }

                    // Slice without LINQ: use a sub-array copy
                    ushort[] registers = new ushort[thisWrite];
                    Buffer.BlockCopy(data, soFar * 2, registers, 0, thisWrite * 2);

                    await _master.WriteMultipleRegistersAsync(
                        slaveAddress,
                        (ushort)(startAddress + soFar),
                        registers,
                        cancellationToken).ConfigureAwait(false);

                    soFar += thisWrite;
                }

            }, cancellationToken).ConfigureAwait(false);
        }

        public Task WriteSingleRegisterAsync(byte slaveAddress, ushort address, ushort value, CancellationToken cancellationToken)
        {
            return PerformAsync(() => _master.WriteSingleRegisterAsync(slaveAddress, address, value, cancellationToken), cancellationToken);
        }

        public Task WriteCoilsAsync(byte slaveAddress, ushort startAddress, bool[] data, CancellationToken cancellationToken)
        {
            return PerformAsync(() => _master.WriteMultipleCoilsAsync(slaveAddress, startAddress, data, cancellationToken), cancellationToken);
        }

        public Task<bool[]> ReadCoilsAsync(byte slaveAddress, ushort startAddress, ushort number,
            CancellationToken cancellationToken)
        {
            return PerformFuncAsync(() => _master.ReadCoilsAsync(slaveAddress, startAddress, number, cancellationToken), cancellationToken);
        }

        public Task<bool[]> ReadDiscretesAsync(byte slaveAddress, ushort startAddress, ushort number, CancellationToken cancellationToken)
        {
            return PerformFuncAsync(() => _master.ReadInputsAsync(slaveAddress, startAddress, number, cancellationToken), cancellationToken);
        }

        public Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value, CancellationToken cancellationToken)
        {
            return PerformAsync(() => _master.WriteSingleCoilAsync(slaveAddress, coilAddress, value, cancellationToken), cancellationToken);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                _master.Dispose();
                _semaphore.Dispose();
            }
        }
    }
}
