using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheAllocator.Models;

namespace TheAllocator.Services;

public sealed class TelemetryService
{
    private const string UploadEndpoint = "http://143.229.29.168/api/logs";
    private const string ApiKey = "6t61AqHOTia8BTJ2Tvi_CU-x9-u2QLr6j4f-1bSIdq0";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TelemetryService(string telemetryRoot, string jsonLogPath, TelemetryContext context)
    {
        TelemetryRoot = telemetryRoot;
        JsonLogPath = jsonLogPath;
        Context = context;
        CurrentBatchPath = Path.Combine(
            TelemetryRoot,
            "Telemetry",
            "pending",
            $"{Context.JobId}.json");
    }

    public string TelemetryRoot { get; }

    public string JsonLogPath { get; }

    public TelemetryContext Context { get; }

    private string CurrentBatchPath { get; }

    public List<TelemetryEvent> PendingEvents { get; } = [];

    public void WriteInfo(
        string message,
        string? phase = null,
        string? status = null,
        string? path = null,
        double? durationSeconds = null,
        int? filesCopied = null,
        int? filesSkipped = null,
        long? bytesCopied = null,
        int? warningCount = null,
        int? errorCount = null) =>
        WriteEvent("info", message, phase, status, null, null, path, durationSeconds, filesCopied, filesSkipped, bytesCopied, warningCount, errorCount);

    public void WriteWarning(
        string message,
        string? phase = null,
        string? status = null,
        string? errorCode = null,
        string? path = null) =>
        WriteEvent("warning", message, phase, status, errorCode, null, path, null, null, null, null, null, null);

    public void WriteError(
        string message,
        string? phase = null,
        string? status = null,
        string? errorCode = null,
        Exception? exception = null,
        string? path = null) =>
        WriteEvent("error", message, phase, status, errorCode, exception?.GetType().Name, path, null, null, null, null, null, null);

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await PersistPendingBatchAsync(cancellationToken);
        await UploadPendingBatchesAsync(TelemetryRoot, cancellationToken);
    }

    private void WriteEvent(
        string level,
        string message,
        string? phase,
        string? status,
        string? errorCode,
        string? exceptionType,
        string? path,
        double? durationSeconds,
        int? filesCopied,
        int? filesSkipped,
        long? bytesCopied,
        int? warningCount,
        int? errorCount)
    {
        var telemetryEvent = new TelemetryEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            App = "The Allocator",
            JobId = Context.JobId,
            Computer = Environment.MachineName,
            Operation = Context.Operation,
            Message = message,
            Version = Context.Version,
            Tech = Environment.UserName,
            UserProfile = Context.UserProfile,
            SourceComputer = Context.SourceComputer,
            TargetComputer = Context.TargetComputer,
            Phase = phase,
            Status = status,
            ErrorCode = errorCode,
            ExceptionType = exceptionType,
            Path = path,
            DurationSeconds = durationSeconds,
            FilesCopied = filesCopied,
            FilesSkipped = filesSkipped,
            BytesCopied = bytesCopied,
            WarningCount = warningCount,
            ErrorCount = errorCount,
            BackupPath = Context.BackupPath,
            RestorePath = Context.RestorePath,
            SourceOperatingSystem = Context.SourceOperatingSystem,
            TargetOperatingSystem = Context.TargetOperatingSystem
        };

        PendingEvents.Add(telemetryEvent);
        AppendJsonLine(telemetryEvent);
        PersistPendingSnapshot();
    }

    private void AppendJsonLine(TelemetryEvent telemetryEvent)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JsonLogPath) ?? TelemetryRoot);
            var json = JsonSerializer.Serialize(telemetryEvent, JsonOptions);
            File.AppendAllText(JsonLogPath, json + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private async Task PersistPendingBatchAsync(CancellationToken cancellationToken)
    {
        if (PendingEvents.Count == 0)
        {
            return;
        }

        var batchPayload = JsonSerializer.Serialize(new { events = PendingEvents }, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentBatchPath) ?? TelemetryRoot);
        await File.WriteAllTextAsync(CurrentBatchPath, batchPayload, Encoding.UTF8, cancellationToken);
    }

    private void PersistPendingSnapshot()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CurrentBatchPath) ?? TelemetryRoot);
            var batchPayload = JsonSerializer.Serialize(new { events = PendingEvents }, JsonOptions);
            File.WriteAllText(CurrentBatchPath, batchPayload, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static async Task UploadPendingBatchesAsync(string telemetryRoot, CancellationToken cancellationToken)
    {
        var pendingDirectory = Path.Combine(telemetryRoot, "Telemetry", "pending");
        if (!Directory.Exists(pendingDirectory))
        {
            return;
        }

        var sentDirectory = Path.Combine(telemetryRoot, "Telemetry", "sent");
        var failedDirectory = Path.Combine(telemetryRoot, "Telemetry", "failed");
        var uploadStatusLogPath = Path.Combine(telemetryRoot, "Telemetry", "upload-status.log");
        Directory.CreateDirectory(sentDirectory);
        Directory.CreateDirectory(failedDirectory);

        foreach (var batchPath in Directory.EnumerateFiles(pendingDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var content = new StringContent(
                    await File.ReadAllTextAsync(batchPath, cancellationToken),
                    Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint)
                {
                    Content = content
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                using var response = await HttpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    AppendUploadStatus(uploadStatusLogPath, $"[{DateTime.Now:u}] Uploaded telemetry batch successfully: {Path.GetFileName(batchPath)}");
                    MoveBatch(batchPath, sentDirectory);
                    continue;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                AppendUploadStatus(
                    uploadStatusLogPath,
                    $"[{DateTime.Now:u}] Telemetry upload returned {(int)response.StatusCode} {response.StatusCode} for {Path.GetFileName(batchPath)}. Response: {responseBody}");

                if (IsPermanentFailure(response.StatusCode))
                {
                    MoveBatch(batchPath, failedDirectory);
                    continue;
                }

                break;
            }
            catch
            {
                AppendUploadStatus(uploadStatusLogPath, $"[{DateTime.Now:u}] Telemetry upload threw an exception for {Path.GetFileName(batchPath)}.");
                break;
            }
        }
    }

    private static bool IsPermanentFailure(HttpStatusCode statusCode)
    {
        var numericCode = (int)statusCode;
        if (numericCode == 408 || numericCode == 429)
        {
            return false;
        }

        return numericCode >= 400 && numericCode < 500;
    }

    private static void MoveBatch(string batchPath, string destinationDirectory)
    {
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(batchPath));
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(batchPath, destinationPath);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static void AppendUploadStatus(string logPath, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());
            File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    public static string GetTelemetryRootForBackup(string backupDestinationFolder) =>
        backupDestinationFolder;

    public static string GetTelemetryRootForRestore(string restorePackagePath)
    {
        var archiveDirectory = Path.GetDirectoryName(restorePackagePath) ?? string.Empty;
        var parentDirectory = Directory.GetParent(archiveDirectory)?.FullName;
        return string.IsNullOrWhiteSpace(parentDirectory) ? archiveDirectory : parentDirectory;
    }

    public static async Task FlushWellKnownPendingBatchesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var telemetryRoot in GetWellKnownTelemetryRoots())
        {
            try
            {
                await UploadPendingBatchesAsync(telemetryRoot, cancellationToken);
            }
            catch
            {
            }
        }
    }

    public static string GetAppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";

    private static IEnumerable<string> GetWellKnownTelemetryRoots()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            var rootPath = drive.RootDirectory.FullName;
            var directRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar);
            var preparedDriveRoot = Path.Combine(rootPath, "User Backups");

            if (Directory.Exists(Path.Combine(directRoot, "Telemetry", "pending")))
            {
                results.Add(directRoot);
            }

            if (Directory.Exists(Path.Combine(preparedDriveRoot, "Telemetry", "pending")))
            {
                results.Add(preparedDriveRoot);
            }
        }

        return results;
    }
}

public sealed class TelemetryContext
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");

    public string Operation { get; set; } = string.Empty;

    public string Version { get; set; } = TelemetryService.GetAppVersion();

    public string? UserProfile { get; set; }

    public string? SourceComputer { get; set; }

    public string? TargetComputer { get; set; }

    public string? BackupPath { get; set; }

    public string? RestorePath { get; set; }

    public string? SourceOperatingSystem { get; set; }

    public string? TargetOperatingSystem { get; set; }
}
