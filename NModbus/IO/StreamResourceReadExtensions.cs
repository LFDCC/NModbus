using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NModbus.IO
{
    internal static class StreamResourceReadExtensions
    {
        public static byte[] ReadExactly(this IStreamResource resource, int count)
        {
            byte[] buffer = new byte[count];
            ReadExactly(resource, buffer, count);
            return buffer;
        }

        public static void ReadExactly(this IStreamResource resource, byte[] buffer, int count)
        {
            for (int total = 0; total < count;)
            {
                int read = resource.Read(buffer, total, count - total);
                if (read == 0)
                    throw new IOException("Read resulted in 0 bytes returned.");
                total += read;
            }
        }

        public static ValueTask<byte[]> ReadExactlyAsync(
            this IStreamResource resource, int count, CancellationToken ct, Action onCancel = null)
            => ReadExactlyAsyncCore(resource, new byte[count], 0, count, ct, onCancel);

        public static ValueTask<byte[]> ReadExactlyAsync(
            this IStreamResource resource, byte[] buffer, int count, CancellationToken ct)
            => ReadExactlyAsyncCore(resource, buffer, 0, count, ct, onCancel: null);

        private static async ValueTask<byte[]> ReadExactlyAsyncCore(
            IStreamResource resource, byte[] buffer, int offset, int count,
            CancellationToken ct, Action onCancel)
        {
            for (int total = 0; total < count;)
            {
                int read;
                try
                {
                    read = await resource.ReadAsync(buffer.AsMemory(offset + total, count - total), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    onCancel?.Invoke();
                    throw;
                }
                if (read == 0)
                    throw new IOException("Read resulted in 0 bytes returned.");
                total += read;
            }
            return buffer;
        }
    }
}
