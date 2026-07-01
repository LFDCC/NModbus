using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NModbus.IO;
using NModbus.Logging;
using NModbus.Message;
using Xunit;

namespace NModbus.UnitTests.IO
{
    public class ModbusAsciiTransportFixture
    {
        private static IStreamResource StreamResource => new Mock<IStreamResource>(MockBehavior.Strict).Object;

        [Fact]
        public void BuildMessageFrame()
        {
            byte[] expected = { 58, 48, 50, 48, 49, 48, 48, 48, 48, 48, 48, 48, 49, 70, 67, 13, 10 };
            var request = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, 2, 0, 1);
            var actual = new ModbusAsciiTransport(StreamResource, new ModbusFactory(), NullModbusLogger.Instance)
                .BuildMessageFrame(request);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ReadRequestResponse()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            IStreamResource stream = mock.Object;
            var transport = new ModbusAsciiTransport(stream, new ModbusFactory(), NullModbusLogger.Instance);
            byte[] bytes = Encoding.ASCII.GetBytes(":110100130025B6\r\n");

            // The new ReadLine implementation reads in batches; feed the whole frame in one call.
            int offset = 0;
            mock.Setup(s => s.Read(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((byte[] buffer, int bufOffset, int count) =>
                {
                    int toCopy = System.Math.Min(count, bytes.Length - offset);
                    if (toCopy <= 0) return 0;
                    System.Buffer.BlockCopy(bytes, offset, buffer, bufOffset, toCopy);
                    offset += toCopy;
                    return toCopy;
                });

            Assert.Equal(new byte[] { 17, 1, 0, 19, 0, 37, 182 }, transport.ReadRequestResponse());
            mock.VerifyAll();
        }

        [Fact]
        public void ReadRequestResponseNotEnoughBytes()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            IStreamResource stream = mock.Object;
            var transport = new ModbusAsciiTransport(stream, new ModbusFactory(), NullModbusLogger.Instance);
            byte[] bytes = Encoding.ASCII.GetBytes(":10\r\n");

            int offset = 0;
            mock.Setup(s => s.Read(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((byte[] buffer, int bufOffset, int count) =>
                {
                    int toCopy = System.Math.Min(count, bytes.Length - offset);
                    if (toCopy <= 0) return 0;
                    System.Buffer.BlockCopy(bytes, offset, buffer, bufOffset, toCopy);
                    offset += toCopy;
                    return toCopy;
                });

            Assert.Throws<IOException>(() => transport.ReadRequestResponse());
            mock.VerifyAll();
        }

        [Fact]
        public void ChecksumsMatchSucceed()
        {
            var transport = new ModbusAsciiTransport(StreamResource, new ModbusFactory(), NullModbusLogger.Instance);
            var message = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, 17, 19, 37);
            byte[] frame = { 17, ModbusFunctionCodes.ReadCoils, 0, 19, 0, 37, 182 };

            Assert.True(transport.ChecksumsMatch(message, frame));
        }

        [Fact]
        public void ChecksumsMatchFail()
        {
            var transport = new ModbusAsciiTransport(StreamResource, new ModbusFactory(), NullModbusLogger.Instance);
            var message = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, 17, 19, 37);
            byte[] frame = { 17, ModbusFunctionCodes.ReadCoils, 0, 19, 0, 37, 181 };

            Assert.False(transport.ChecksumsMatch(message, frame));
        }

        [Fact]
        public async Task ReadRequestAsync_Cancelled_DiscardsInputBuffer()
        {
            // ASCII is half-duplex: a cancelled read leaves the slave's mid-response bytes
            // stranded in the serial FIFO. Verify the transport purges on cancel.
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            mock.Setup(s => s.DiscardInBuffer());
            mock.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());

            var transport = new ModbusAsciiTransport(mock.Object, new ModbusFactory(), NullModbusLogger.Instance);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transport.ReadRequestAsync(cts.Token));

            mock.Verify(s => s.DiscardInBuffer(), Times.Once);
        }
    }
}
