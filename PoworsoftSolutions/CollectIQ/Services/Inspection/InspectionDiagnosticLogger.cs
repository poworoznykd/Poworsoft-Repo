using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using System.Diagnostics;
using System.Text;

namespace CollectIQ.Services.Inspection
{
    /// <summary>
    /// Persistent diagnostics for the inspection workflows.
    /// Logging failures must never break an inspection.
    /// </summary>
    public static class InspectionDiagnosticLogger
    {
        private static readonly SemaphoreSlim WriteLock = new(1, 1);

        public static string DiagnosticDirectory =>
            Path.Combine(FileSystem.AppDataDirectory, "Diagnostics");

        public static string LatestPath =>
            Path.Combine(DiagnosticDirectory, "inspection_diagnostics_latest.txt");

        public static string HistoryPath =>
            Path.Combine(DiagnosticDirectory, "inspection_diagnostics_history.txt");

        public static async Task StartRunAsync(string inspectionName, string? detail = null)
        {
            try
            {
                Directory.CreateDirectory(DiagnosticDirectory);

                StringBuilder builder = new();
                builder.AppendLine("============================================================");
                builder.AppendLine("COLLECTIQ INSPECTION DIAGNOSTIC");
                builder.AppendLine("============================================================");
                builder.AppendLine($"UTC: {DateTime.UtcNow:O}");
                builder.AppendLine($"Inspection: {inspectionName}");
                builder.AppendLine($"Thread: {Environment.CurrentManagedThreadId}");
                builder.AppendLine($"Main thread: {MainThread.IsMainThread}");
                builder.AppendLine($"Platform: {DeviceInfo.Platform}");
                builder.AppendLine($"OS: {DeviceInfo.VersionString}");
                builder.AppendLine($"Device: {DeviceInfo.Manufacturer} {DeviceInfo.Model}");

                if (!string.IsNullOrWhiteSpace(detail))
                    builder.AppendLine($"Detail: {detail}");

                builder.AppendLine("------------------------------------------------------------");

                string text = builder.ToString();

                await WriteLock.WaitAsync();
                try
                {
                    await File.WriteAllTextAsync(LatestPath, text);
                    await File.AppendAllTextAsync(HistoryPath, text);
                }
                finally
                {
                    WriteLock.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionDiagnostic] StartRunAsync failed: {ex}");
            }
        }

        public static async Task WriteAsync(
            string inspectionName,
            string stage,
            string? detail = null,
            Exception? exception = null)
        {
            try
            {
                Directory.CreateDirectory(DiagnosticDirectory);

                StringBuilder builder = new();
                builder.AppendLine(
                    $"{DateTime.UtcNow:O} | {inspectionName} | {stage} | " +
                    $"Thread={Environment.CurrentManagedThreadId} | MainThread={MainThread.IsMainThread}");

                if (!string.IsNullOrWhiteSpace(detail))
                    builder.AppendLine($"  {detail}");

                if (exception != null)
                    builder.AppendLine($"  EXCEPTION: {exception}");

                string text = builder.ToString();

                await WriteLock.WaitAsync();
                try
                {
                    await File.AppendAllTextAsync(LatestPath, text);
                    await File.AppendAllTextAsync(HistoryPath, text);
                }
                finally
                {
                    WriteLock.Release();
                }

                Debug.WriteLine($"[InspectionDiagnostic] {inspectionName} / {stage} / {detail}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionDiagnostic] WriteAsync failed: {ex}");
            }
        }
    }
}
