using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NModbus.Data;
using NModbus.IO;
using NModbus.Logging;
using NModbus.Message;
using NModbus.Utility;
using Xunit;

namespace NModbus.UnitTests.IO
{
    public class ModbusRtuTransportFixture
    {
        private static IStreamResource StreamResource => new Mock<IStreamResource>(MockBehavior.Strict).Object;
        private static IModbusFactory Factory = new ModbusFactory();

        [Fact]
        public void BuildMessageFrame()
        {
            byte[] message = { 17, ModbusFunctionCodes.ReadCoils, 0, 19, 0, 37, 14, 132 };
            var request = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, 17, 19, 37);
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);

            Assert.Equal(message, transport.BuildMessageFrame(request));
        }

        [Fact]
        public void ResponseBytesToReadCoils()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x11, 0x01, 0x05, 0xCD, 0x6B, 0xB2, 0x0E, 0x1B };
            Assert.Equal(6, transport.ResponseBytesToRead(frameStart));
        }

        [Fact]
        public void ResponseBytesToReadCoilsNoData()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x11, 0x01, 0x00, 0x00, 0x00 };
            Assert.Equal(1, transport.ResponseBytesToRead(frameStart));
        }

        [Fact]
        public void ResponseBytesToReadWriteCoilsResponse()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x11, 0x0F, 0x00, 0x13, 0x00, 0x0A, 0, 0 };
            Assert.Equal(4, transport.ResponseBytesToRead(frameStart));
        }

        [Fact]
        public void ResponseBytesToReadDiagnostics()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x01, 0x08, 0x00, 0x00 };
            Assert.Equal(4, transport.ResponseBytesToRead(frameStart));
        }

        [Fact]
        public void ResponseBytesToReadSlaveException()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x01, Modbus.ExceptionOffset + 1, 0x01 };
            Assert.Equal(1, transport.ResponseBytesToRead(frameStart));
        }

        [Fact]
        public void ResponseBytesToReadInvalidFunctionCode()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frame = { 0x11, 0x16, 0x00, 0x01, 0x00, 0x02, 0x04 };
            Assert.Throws<NotImplementedException>(() => transport.ResponseBytesToRead(frame));
        }

        [Fact]
        public void RequestBytesToReadDiagnostics()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frame = { 0x01, 0x08, 0x00, 0x00, 0xA5, 0x37, 0, 0 };
            Assert.Equal(1, transport.RequestBytesToRead(frame));
        }

        [Fact]
        public void RequestBytesToReadCoils()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x11, 0x01, 0x00, 0x13, 0x00, 0x25 };
            Assert.Equal(1, transport.RequestBytesToRead(frameStart));
        }

        [Fact]
        public void RequestBytesToReadWriteCoilsRequest()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x11, 0x0F, 0x00, 0x13, 0x00, 0x0A, 0x02, 0xCD, 0x01 };
            Assert.Equal(4, transport.RequestBytesToRead(frameStart));
        }

        [Fact]
        public void RequestBytesToReadWriteMultipleHoldingRegisters()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frameStart = { 0x11, 0x10, 0x00, 0x01, 0x00, 0x02, 0x04 };
            Assert.Equal(6, transport.RequestBytesToRead(frameStart));
        }

        [Fact]
        public void RequestBytesToReadInvalidFunctionCode()
        {
            var transport = new ModbusRtuTransport(StreamResource, Factory, NullModbusLogger.Instance);
            byte[] frame = { 0x11, 0xFF, 0x00, 0x01, 0x00, 0x02, 0x04 };
            Assert.Throws<NotImplementedException>(() => transport.RequestBytesToRead(frame));
        }

        [Fact]
        public void ChecksumsMatchSucceed()
        {
            var factory = new ModbusFactory();
            var transport = new ModbusRtuTransport(StreamResource, factory, NullModbusLogger.Instance);
            var message = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, 17, 19, 37);
            byte[] frame = { 17, ModbusFunctionCodes.ReadCoils, 0, 19, 0, 37, 14, 132 };

            Assert.True(transport.ChecksumsMatch(message, frame));
        }

        [Fact]
        public void ChecksumsMatchFail()
        {
            var factory = new ModbusFactory();
            var transport = new ModbusRtuTransport(StreamResource, factory, NullModbusLogger.Instance);
            var message = new ReadCoilsInputsRequest(ModbusFunctionCodes.ReadCoils, 17, 19, 38);
            byte[] frame = { 17, ModbusFunctionCodes.ReadCoils, 0, 19, 0, 37, 14, 132 };

            Assert.False(transport.ChecksumsMatch(message, frame));
        }

        [Fact]
        public void ReadResponse()
        {
            var factory = new ModbusFactory();
            var mock = new Mock<ModbusRtuTransport>(StreamResource, factory, NullModbusLogger.Instance) { CallBase = true };
            var transport = mock.Object;

            mock.Setup(t => t.Read(ModbusRtuTransport.ResponseFrameStartLength)).Returns(new byte[] { 1, 1, 1, 0 });
            mock.Setup(t => t.Read(2)).Returns(new byte[] { 81, 136 });

            var response = transport.ReadResponse<ReadCoilsInputsResponse>();
            Assert.IsType<ReadCoilsInputsResponse>(response);

            var expectedResponse = new ReadCoilsInputsResponse(ModbusFunctionCodes.ReadCoils, 1, 1, new DiscreteCollection(false));
            Assert.Equal(expectedResponse.MessageFrame, response.MessageFrame);

            mock.VerifyAll();
        }

        [Fact]
        public void ReadResponseSlaveException()
        {
            var factory = new ModbusFactory();
            var mock = new Mock<ModbusRtuTransport>(StreamResource, factory, NullModbusLogger.Instance) { CallBase = true };
            var transport = mock.Object;

            byte[] messageFrame = { 0x01, 0x81, 0x02 };
            byte[] crc = ModbusUtility.CalculateCrc(messageFrame);

            mock.Setup(t => t.Read(ModbusRtuTransport.ResponseFrameStartLength))
                .Returns(Enumerable.Concat(messageFrame, new byte[] { crc[0] }).ToArray());

            mock.Setup(t => t.Read(1))
                .Returns(new byte[] { crc[1] });

            var response = transport.ReadResponse<ReadCoilsInputsResponse>();
            Assert.IsType<SlaveExceptionResponse>(response);

            var expectedResponse = new SlaveExceptionResponse(0x01, 0x81, 0x02);
            Assert.Equal(expectedResponse.MessageFrame, response.MessageFrame);

            mock.VerifyAll();
        }

        /// <summary>
        /// We want to throw an IOException for any message w/ an invalid checksum,
        /// this must preceed throwing a SlaveException based on function code > 127
        /// </summary>
        [Fact]
        public void ReadResponseSlaveExceptionWithErroneousLrc()
        {
            var factory = new ModbusFactory();
            var mock = new Mock<ModbusRtuTransport>(StreamResource, factory, NullModbusLogger.Instance) { CallBase = true };
            var transport = mock.Object;

            byte[] messageFrame = { 0x01, 0x81, 0x02 };

            // invalid crc
            byte[] crc = { 0x9, 0x9 };

            mock.Setup(t => t.Read(ModbusRtuTransport.ResponseFrameStartLength))
                .Returns(Enumerable.Concat(messageFrame, new byte[] { crc[0] }).ToArray());

            mock.Setup(t => t.Read(1))
                .Returns(new byte[] { crc[1] });

            Assert.Throws<IOException>(() => transport.ReadResponse<ReadCoilsInputsResponse>());

            mock.VerifyAll();
        }

        [Fact]
        public void ReadRequest()
        {
            var factory = new ModbusFactory();
            var mock = new Mock<ModbusRtuTransport>(StreamResource, factory, NullModbusLogger.Instance) { CallBase = true };
            var transport = mock.Object;

            mock.Setup(t => t.Read(ModbusRtuTransport.RequestFrameStartLength))
                .Returns(new byte[] { 1, 1, 1, 0, 1, 0, 0 });

            mock.Setup(t => t.Read(1))
                .Returns(new byte[] { 5 });

            Assert.Equal(new byte[] { 1, 1, 1, 0, 1, 0, 0, 5 }, transport.ReadRequest());

            mock.VerifyAll();
        }

        [Fact]
        public void Read()
        {
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);

            mock.Setup(s => s.Read(It.Is<byte[]>(x => x.Length == 5), 0, 5))
                .Returns((byte[] buf, int offset, int count) =>
                {
                    Array.Copy(new byte[] { 2, 2, 2 }, buf, 3);
                    return 3;
                });

            mock.Setup(s => s.Read(It.Is<byte[]>(x => x.Length == 5), 3, 2))
                .Returns((byte[] buf, int offset, int count) =>
                {
                    Array.Copy(new byte[] { 3, 3 }, 0, buf, 3, 2);
                    return 2;
                });

            var factory = new ModbusFactory();

            ModbusRtuTransport transport = new ModbusRtuTransport(mock.Object, factory, NullModbusLogger.Instance);
            Assert.Equal(new byte[] { 2, 2, 2, 3, 3 }, transport.Read(5));

            mock.VerifyAll();
        }

        [Fact]
        public async Task ReadRequestAsync_Cancelled_DiscardsInputBuffer()
        {
            // Set up a mock whose async read always throws OCE; verify the transport:
            //   1) propagates OperationCanceledException out of ReadRequestAsync, and
            //   2) has called StreamResource.DiscardInBuffer exactly once before re-throwing
            // (otherwise RTU would carry the slave's mid-response bytes into the next request).
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            mock.Setup(s => s.DiscardInBuffer());
            mock.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());

            var transport = new ModbusRtuTransport(mock.Object, new ModbusFactory(), NullModbusLogger.Instance);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transport.ReadRequestAsync(cts.Token));

            mock.Verify(s => s.DiscardInBuffer(), Times.Once);
        }

        [Fact]
        public async Task ReadResponseAsync_Cancelled_DiscardsInputBuffer()
        {
            // Mirror of the ReadRequestAsync test, exercising the response path.
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            mock.Setup(s => s.DiscardInBuffer());
            mock.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());

            var transport = new ModbusRtuTransport(mock.Object, new ModbusFactory(), NullModbusLogger.Instance);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transport.ReadResponseAsync<ReadCoilsInputsResponse>(cts.Token));

            mock.Verify(s => s.DiscardInBuffer(), Times.Once);
        }

        [Fact]
        public async Task ReadRequestAsync_NoCancellation_DoesNotDiscardInputBuffer()
        {
            // Sanity check: a successful read must NOT call DiscardInBuffer (the transport only
            // purges on cancel; normal write-path purging lives in ModbusSerialTransport.WriteAsync).
            // First read returns a valid ReadCoils request frame-start (RequestBytesToRead = 1).
            // Second read returns 1 trailing byte.
            var mock = new Mock<IStreamResource>(MockBehavior.Strict);
            int callIndex = 0;
            mock.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Memory<byte> buf, CancellationToken ct) =>
                {
                    if (callIndex++ == 0)
                    {
                        new byte[] { 0x11, 0x01, 0x00, 0x13, 0x00, 0x25 }.AsSpan().CopyTo(buf.Span);
                        return 7;
                    }
                    buf.Span[0] = 0x00;
                    return 1;
                });

            var transport = new ModbusRtuTransport(mock.Object, new ModbusFactory(), NullModbusLogger.Instance);

            using var cts = new CancellationTokenSource(50);
            await transport.ReadRequestAsync(cts.Token);

            mock.Verify(s => s.DiscardInBuffer(), Times.Never);
        }
    }
}
