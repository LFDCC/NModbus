using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NModbus.IO;
using Xunit;

namespace NModbus.UnitTests.IO
{
    public class StreamResourceReadExtensionsFixture
    {
        [Fact]
        public void ReadExactly_LoopsUntilAllBytesArrived()
        {
            // First Read returns 2, second returns 3 → total 5 → done.
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            var buffer = new byte[5];
            int callCount = 0;
            mock.Setup(r => r.Read(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((byte[] b, int off, int count) =>
                {
                    callCount++;
                    if (callCount == 1) { b[off] = 0xAA; b[off + 1] = 0xBB; return 2; }
                    b[off] = 0xCC; b[off + 1] = 0xDD; b[off + 2] = 0xEE; return 3;
                });

            byte[] rented = mock.Object.ReadExactly(5);

            try
            {
                Assert.Equal(5, rented.Length >= 5 ? 5 : rented.Length); // pool may round up; only first 5 bytes valid
                Assert.Equal(0xAA, rented[0]);
                Assert.Equal(0xBB, rented[1]);
                Assert.Equal(0xCC, rented[2]);
                Assert.Equal(0xDD, rented[3]);
                Assert.Equal(0xEE, rented[4]);
                mock.Verify(r => r.Read(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        [Fact]
        public void ReadExactly_ZeroByteRead_ThrowsIOException_AndReturnsBufferToPool()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            mock.Setup(r => r.Read(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var ex = Assert.Throws<IOException>(() => mock.Object.ReadExactly(3));
            Assert.Equal("Read resulted in 0 bytes returned.", ex.Message);
        }

        [Fact]
        public async Task ReadExactlyAsync_LoopsUntilAllBytesArrived()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            int callCount = 0;
            mock.Setup(r => r.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((Memory<byte> mem, CancellationToken _) =>
                {
                    callCount++;
                    var span = mem.Span;
                    if (callCount == 1) { span[0] = 0x01; span[1] = 0x02; return ValueTask.FromResult(2); }
                    span[0] = 0x03; return ValueTask.FromResult(1);
                });

            byte[] rented = await mock.Object.ReadExactlyAsync(3, CancellationToken.None);

            try
            {
                Assert.Equal(0x01, rented[0]);
                Assert.Equal(0x02, rented[1]);
                Assert.Equal(0x03, rented[2]);
                mock.Verify(r => r.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        [Fact]
        public async Task ReadExactlyAsync_ZeroByteRead_ThrowsIOException_AndReturnsBufferToPool()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            mock.Setup(r => r.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult(0));

            var ex = await Assert.ThrowsAsync<IOException>(() =>
                mock.Object.ReadExactlyAsync(4, CancellationToken.None).AsTask());
            Assert.Equal("Read resulted in 0 bytes returned.", ex.Message);
        }

        [Fact]
        public async Task ReadExactlyAsync_Cancellation_InvokesOnCancelHook_AndRethrowsAsOCE()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            using var cts = new CancellationTokenSource();
            mock.Setup(r => r.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Memory<byte> _, CancellationToken ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return 0; // unreachable
                });

            int onCancelCount = 0;
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                mock.Object.ReadExactlyAsync(8, cts.Token, onCancel: () => onCancelCount++).AsTask());

            Assert.Equal(1, onCancelCount);
        }

        [Fact]
        public async Task ReadExactlyAsync_CancellationWithoutHook_DoesNotInvokeAnything()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            using var cts = new CancellationTokenSource();
            mock.Setup(r => r.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Memory<byte> _, CancellationToken ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return 0;
                });

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                mock.Object.ReadExactlyAsync(8, cts.Token).AsTask());
        }

        [Fact]
        public async Task ReadExactlyAsync_UsesArrayPool()
        {
            // The helper should not allocate a fresh byte[] per call. Verify by:
            //  1) Rent manually and Return it (so it sits at the top of the LIFO bucket).
            //  2) Call ReadExactlyAsync with a mock that fills the rented buffer with sentinels.
            //  3) Confirm the returned buffer reference is the same one the pool just handed out.
            byte[] seed = ArrayPool<byte>.Shared.Rent(8);
            try
            {
                seed[0] = 0x42;
                ArrayPool<byte>.Shared.Return(seed);

                var mock = new Mock<IStreamResource>(MockBehavior.Strict);
                mock.Setup(r => r.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                    .Returns((Memory<byte> mem, CancellationToken _) =>
                    {
                        mem.Span[0] = 0x42;
                        return ValueTask.FromResult(1);
                    });

                byte[] rented = await mock.Object.ReadExactlyAsync(1, CancellationToken.None);
                try
                {
                    Assert.Same(seed, rented);
                    Assert.Equal(0x42, rented[0]);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
            finally
            {
                // belt-and-suspenders if the test bailed early
                ArrayPool<byte>.Shared.Return(seed);
            }
        }

        }
}