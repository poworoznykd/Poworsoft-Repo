namespace CollectIQ.Services.Inspection
{
    /// <summary>
    /// Runs heavy inspection work away from the MAUI UI thread and prevents
    /// a detector from leaving the page in an endless loading state.
    /// </summary>
    public static class InspectionExecution
    {
        public static async Task<T> RunAsync<T>(
            string inspectionName,
            string stage,
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operationCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            await InspectionDiagnosticLogger.WriteAsync(
                inspectionName,
                $"{stage} START",
                $"Timeout={timeout.TotalSeconds:0}s");

            DateTime startedUtc = DateTime.UtcNow;

            // The entire service begins on the thread pool so synchronous OpenCV,
            // ImageSharp and array-processing work does not block the MAUI UI thread.
            Task<T> work = Task.Run(
                () => operation(operationCts.Token),
                CancellationToken.None);

            Task delay = Task.Delay(timeout, cancellationToken);
            Task completed = await Task.WhenAny(work, delay);

            if (completed != work)
            {
                operationCts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();

                TimeoutException timeoutException = new(
                    $"{inspectionName} did not finish {stage.ToLowerInvariant()} " +
                    $"within {timeout.TotalSeconds:0} seconds.");

                await InspectionDiagnosticLogger.WriteAsync(
                    inspectionName,
                    $"{stage} TIMEOUT",
                    $"Elapsed={(DateTime.UtcNow - startedUtc).TotalSeconds:0.0}s",
                    timeoutException);

                // Some native/image routines cannot be interrupted mid-call.
                // The worker may finish later, but the page is released immediately.
                _ = work.ContinueWith(
                    completedWork =>
                    {
                        _ = completedWork.Exception;
                    },
                    TaskContinuationOptions.OnlyOnFaulted);

                throw timeoutException;
            }

            try
            {
                T result = await work;

                await InspectionDiagnosticLogger.WriteAsync(
                    inspectionName,
                    $"{stage} COMPLETE",
                    $"Elapsed={(DateTime.UtcNow - startedUtc).TotalSeconds:0.0}s");

                return result;
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync(
                    inspectionName,
                    $"{stage} FAILED",
                    $"Elapsed={(DateTime.UtcNow - startedUtc).TotalSeconds:0.0}s",
                    ex);

                throw;
            }
        }

        public static Task<T> RunCpuAsync<T>(
            string inspectionName,
            string stage,
            Func<T> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(
                inspectionName,
                stage,
                _ => Task.FromResult(operation()),
                timeout,
                cancellationToken);
        }
    }
}
