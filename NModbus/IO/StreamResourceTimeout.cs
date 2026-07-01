using System;
using System.Threading;
using System.Threading.Tasks;

namespace NModbus.IO
{
    /// <summary>
    ///     Enforces <see cref="IStreamResource.ReadTimeout"/> / <see cref="IStreamResource.WriteTimeout"/>
    ///     on the asynchronous I/O path.
    /// </summary>
    /// <remarks>
    ///     The async socket / stream APIs (<c>NetworkStream.ReadAsync</c>, <c>Socket.ReceiveAsync</c>,
    ///     <c>SerialPort.BaseStream.ReadAsync</c>) silently IGNORE the stream's read/write timeout — that
    ///     property is only observed by the synchronous <c>Read</c>/<c>Write</c> calls. Without an explicit
    ///     guard, an async read against a peer that accepts the connection but never answers (e.g. a Modbus
    ///     TCP gateway asked for an unknown unit id) blocks until the OS TCP stack gives up — tens of seconds
    ///     — instead of the configured timeout, and behaves inconsistently with the synchronous path.
    ///
    ///     Two flavours are provided:
    ///     <list type="bullet">
    ///         <item><description>
    ///             The <c>*PreservingConnection*</c> overloads race the operation against a timer and, on
    ///             timeout, throw WITHOUT cancelling the underlying read. This matters for connection-oriented
    ///             streams (TCP / raw <c>Socket</c>): cancelling an in-flight socket read tears the socket
    ///             down on some runtimes (pre-.NET 7), so a subsequent retry fails with
    ///             "The operation is not allowed on non-connected sockets". Leaving the read pending keeps the
    ///             socket usable for the transport's retry loop. Safe only when each call uses its own buffer.
    ///         </description></item>
    ///         <item><description>
    ///             The plain overloads enforce the timeout by cancelling the operation via a linked token.
    ///             Used where the underlying resource is not a reusable connection (serial ports) or reuses a
    ///             shared buffer that must not be written by an orphaned read (UDP reassembly buffer).
    ///         </description></item>
    ///     </list>
    ///     In every case a cancellation coming from the caller's own token surfaces as
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
        ///     Runs an async read, throwing <paramref name="onTimeout"/> if it does not complete within
        ///     <paramref name="timeoutMilliseconds"/> WITHOUT cancelling the underlying read, so the
        ///     connection stays usable for a retry. The caller's <paramref name="cancellationToken"/> can
        ///     still abort the read (surfacing <see cref="OperationCanceledException"/>).
        /// </summary>
        public static ValueTask<int> ReadWithTimeoutPreservingConnectionAsync(
            Func<CancellationToken, ValueTask<int>> operation,
            int timeoutMilliseconds,
            Func<Exception> onTimeout,
            CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));

            // Start the read WITH the caller's token (so explicit cancellation still works) but NOT with the
            // timeout — the timeout is enforced by racing a timer, never by cancelling the read.
            ValueTask<int> pending = operation(cancellationToken);

            // Fast path: the read already completed (bytes were buffered, or it finished/faulted
            // synchronously) or no timeout is configured. Skip the timer / CTS / WhenAny machinery entirely
            // and stay allocation-free — this is the common request/response case where data is already
            // available on the socket right after the request was written.
            if (pending.IsCompleted || IsInfinite(timeoutMilliseconds))
            {
                return pending;
            }

            return new ValueTask<int>(
                RaceAgainstTimeoutAsync(pending.AsTask(), timeoutMilliseconds, onTimeout, cancellationToken));
        }

        /// <summary>
        ///     Runs an async write, throwing <paramref name="onTimeout"/> if it does not complete within
        ///     <paramref name="timeoutMilliseconds"/> WITHOUT cancelling the underlying write, so the
        ///     connection stays usable for a retry.
        /// </summary>
        public static ValueTask WriteWithTimeoutPreservingConnectionAsync(
            Func<CancellationToken, ValueTask> operation,
            int timeoutMilliseconds,
            Func<Exception> onTimeout,
            CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));

            ValueTask pending = operation(cancellationToken);

            // Fast path: synchronous completion or no timeout — no timer/CTS/WhenAny needed.
            if (pending.IsCompleted || IsInfinite(timeoutMilliseconds))
            {
                return pending;
            }

            return new ValueTask(
                RaceAgainstTimeoutAsync(AsCompletedTask(pending), timeoutMilliseconds, onTimeout, cancellationToken));
        }

        private static async Task<bool> AsCompletedTask(ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return true;
        }

        private static async Task<T> RaceAgainstTimeoutAsync<T>(
            Task<T> operationTask,
            int timeoutMilliseconds,
            Func<Exception> onTimeout,
            CancellationToken cancellationToken)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task delayTask = Task.Delay(timeoutMilliseconds, delayCts.Token);

            Task completed = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);

            if (completed == operationTask)
            {
                delayCts.Cancel(); // stop the timer
                return await operationTask.ConfigureAwait(false);
            }

            // Timed out (or the caller cancelled). Leave the operation running so the connection is not torn
            // down; observe its eventual exception to avoid an unobserved-task fault.
            ObserveException(operationTask);

            // Prefer caller-cancellation semantics over the timeout exception.
            cancellationToken.ThrowIfCancellationRequested();

            throw onTimeout();
        }

        private static void ObserveException(Task task)
        {
            _ = task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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
