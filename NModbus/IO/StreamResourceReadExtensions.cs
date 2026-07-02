using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NModbus.IO
{
    /// <summary>
    ///     Low-level "read exactly N bytes" helpers backed by <see cref="ArrayPool{T}.Shared"/>.
    ///     Centralises the "0-byte read ⇒ <see cref="IOException"/>" contract that previously
    ///     lived at every transport's read loop, and removes the per-call <c>new byte[count]</c>
    ///     allocation that the inner read loops used to make.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On success the caller OWNS the returned buffer and must hand it back to the pool
    ///         (typically via <c>ArrayPool&lt;byte&gt;.Shared.Return(buffer)</c>) once it is no
    ///         longer needed. The rented buffer may be larger than <c>count</c>; only the first
    ///         <c>count</c> bytes are valid. Callers that need a tightly-sized copy should
    ///         <c>Array.Copy</c> into a freshly-allocated <c>byte[count]</c> themselves.
    ///     </para>
    ///     <para>
    ///         Cancellation semantics: the onCancel callback (passed to <c>ReadExactlyAsync</c>)
    ///         runs inside the catch when an in-flight read is aborted by the cancellation token.
    ///         The serial RTU/ASCII transports use it to call <see cref="IStreamResource.DiscardInBuffer"/>
    ///         so stranded bytes do not corrupt the next request on the half-duplex FIFO.
    ///     </para>
    ///     <para>
    ///         Timeout is intentionally NOT enforced here. Per-call timeouts are applied one
    ///         layer up, by <see cref="StreamResourceTimeout"/>, which still wraps the
    ///         <see cref="IStreamResource.ReadAsync(Memory{byte}, CancellationToken)"/> call.
    ///     </para>
    /// </remarks>
    internal static class StreamResourceReadExtensions
    {
        /// <summary>
        ///     Synchronously reads exactly <paramref name="count"/> bytes from
        ///     <paramref name="resource"/> into a pooled buffer.
        /// </summary>
        /// <param name="resource">The underlying byte stream.</param>
        /// <param name="count">The exact number of bytes to read.</param>
        public static byte[] ReadExactly(this IStreamResource resource, int count)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0.");

            byte[] buffer = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                int numBytesReadTotal = 0;
                while (numBytesReadTotal != count)
                {
                    int numBytesRead = resource.Read(buffer, numBytesReadTotal, count - numBytesReadTotal);

                    if (numBytesRead == 0)
                    {
                        throw new IOException("Read resulted in 0 bytes returned.");
                    }

                    numBytesReadTotal += numBytesRead;
                }

                return buffer;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        /// <summary>
        ///     Asynchronously reads exactly <paramref name="count"/> bytes from
        ///     <paramref name="resource"/> into a pooled buffer.
        /// </summary>
        /// <param name="resource">The underlying byte stream.</param>
        /// <param name="count">The exact number of bytes to read.</param>
        /// <param name="cancellationToken">Cancellation token aborts the in-flight read.</param>
        /// <param name="onCancel">
        ///     Optional hook invoked if the read is cancelled before <paramref name="count"/>
        ///     bytes arrive. Pass a delegate that calls <see cref="IStreamResource.DiscardInBuffer"/>
        ///     for half-duplex serial transports.
        /// </param>
        public static async ValueTask<byte[]> ReadExactlyAsync(
            this IStreamResource resource,
            int count,
            CancellationToken cancellationToken,
            Action onCancel = null)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0.");

            byte[] buffer = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                int numBytesReadTotal = 0;
                while (numBytesReadTotal != count)
                {
                    int numBytesRead;
                    try
                    {
                        numBytesRead = await resource
                            .ReadAsync(buffer.AsMemory(numBytesReadTotal, count - numBytesReadTotal), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        onCancel?.Invoke();
                        throw;
                    }

                    if (numBytesRead == 0)
                    {
                        throw new IOException("Read resulted in 0 bytes returned.");
                    }

                    numBytesReadTotal += numBytesRead;
                }

                return buffer;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }
    }
}