using System;
using System.Threading;
using System.Threading.Tasks;

namespace NModbus.IO
{
    /// <summary>
    ///     Enforces <see cref="IStreamResource.ReadTimeout"/> / <see cref="IStreamResource.WriteTimeout"/>
    ///     on the asynchronous I/O path for adapters whose underlying stream API does not honor the
    ///     timeout property directly (e.g. <c>SerialPort.BaseStream</c> and the UDP socket).
    /// </summary>
    /// <remarks>
    ///     The connection-oriented TCP and raw-socket adapters (<c>TcpClientAdapter</c>,
    ///     <c>SocketAdapter</c>) handle the timeout inline with a linked <see cref="CancellationTokenSource"/>
    ///     because on .NET 7+ the underlying socket receive is actually aborted when the token fires
    ///     — no orphan read, per-request error isolation. The cancel-style helpers here are still used
    ///     by adapters where:
    ///     <list type="bullet">
    ///         <item><description>
    ///             the underlying resource is not a reusable connection (serial ports), so cancellation
    ///             is safe and we want the in-flight read to actually abort (the transport's
    ///             OCE-handler purges the half-duplex FIFO via <see cref="IStreamResource.DiscardInBuffer"/>);
    ///         </description></item>
    ///         <item><description>
    ///             or the resource reuses a shared buffer that must not be written to by an orphaned
    ///             receive (UDP reassembly buffer in <c>UdpClientAdapter</c>).
    ///         </description></item>
    ///     </list>
    ///     A cancellation coming from the caller's own token always surfaces as
    ///     <see cref="OperationCanceledException"/>, never as the timeout exception.
    /// </remarks>
    public static class StreamResourceTimeout
    {
        /// <summary>
        ///     Runs an async read, aborting it via a linked cancellation token if it does not complete within
        ///     <paramref name="timeoutMilliseconds"/>.
        /// </summary>
        public static async ValueTask<int> ReadWithTimeoutAsync(
            Func<CancellationToken, ValueTask<int>> operation,
            int timeoutMilliseconds,
            Func<Exception> onTimeout,
            CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));

            if (IsInfinite(timeoutMilliseconds))
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }

            using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                return await operation(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw onTimeout();
            }
        }

        /// <summary>
        ///     Runs an async write, aborting it via a linked cancellation token if it does not complete within
        ///     <paramref name="timeoutMilliseconds"/>.
        /// </summary>
        public static async ValueTask WriteWithTimeoutAsync(
            Func<CancellationToken, ValueTask> operation,
            int timeoutMilliseconds,
            Func<Exception> onTimeout,
            CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));

            if (IsInfinite(timeoutMilliseconds))
            {
                await operation(cancellationToken).ConfigureAwait(false);
                return;
            }

            using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await operation(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw onTimeout();
            }
        }

        /// <summary>
        ///     A non-positive value or <see cref="Timeout.Infinite"/> (-1) means "no timeout". This matches how
        ///     <c>NetworkStream.ReadTimeout</c> reports an unset timeout (-1) and how a freshly created
        ///     <c>Socket</c> reports <c>ReceiveTimeout</c>/<c>SendTimeout</c> (0).
        /// </summary>
        private static bool IsInfinite(int timeoutMilliseconds)
            => timeoutMilliseconds <= 0 || timeoutMilliseconds == Timeout.Infinite;
    }
}
