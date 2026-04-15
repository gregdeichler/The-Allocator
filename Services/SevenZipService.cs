using System.IO;
using System.Diagnostics;
using System.Text;

namespace TheAllocator.Services;

public sealed class SevenZipService
{
    public SevenZipService(string appBaseDirectory)
    {
        ExecutablePath = Path.Combine(appBaseDirectory, "tools", "7zip", "x64", "7za.exe");
        DllPath = Path.Combine(appBaseDirectory, "tools", "7zip", "x64", "7za.dll");
        ExtractDllPath = Path.Combine(appBaseDirectory, "tools", "7zip", "x64", "7zxa.dll");
    }

    public string ExecutablePath { get; }

    public string DllPath { get; }

    public string ExtractDllPath { get; }

    public bool IsAvailable =>
        File.Exists(ExecutablePath) &&
        File.Exists(DllPath) &&
        File.Exists(ExtractDllPath);

    public string GetRecommendedArchiveName(string userName)
    {
        var safeUserName = string.IsNullOrWhiteSpace(userName) ? "user" : userName;
        return $"{safeUserName}-backup.7z";
    }

    public string GetCompressionSummary() =>
        "7-Zip portable engine, low compression, optimized for speed over size.";

    public async Task<int> CreateArchiveAsync(
        string archivePath,
        string sourceDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        progress?.Report("Compressing staged backup content...");

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = sourceDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("a");
        startInfo.ArgumentList.Add("-t7z");
        startInfo.ArgumentList.Add("-mx=1");
        startInfo.ArgumentList.Add("-mmt=on");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add(@".\*");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            progress?.Report(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            progress?.Report(stderr.Trim());
        }

        return process.ExitCode;
    }

    public async Task<int> CreateArchiveFromPathsAsync(
        string archivePath,
        string workingDirectory,
        IReadOnlyList<string> relativePaths,
        IReadOnlyList<string> excludeArguments,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        return await RunArchiveCommandAsync(
            archivePath,
            workingDirectory,
            relativePaths,
            excludeArguments,
            replaceArchive: true,
            progress,
            cancellationToken);
    }

    public async Task<int> AddFilesToArchiveAsync(
        string archivePath,
        string workingDirectory,
        IReadOnlyList<string> relativePaths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunArchiveCommandAsync(
            archivePath,
            workingDirectory,
            relativePaths,
            [],
            replaceArchive: false,
            progress,
            cancellationToken);
    }

    public async Task<int> ExtractArchiveAsync(
        string archivePath,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string[]? excludePatterns = null,
        params string[] includePatterns)
    {
        Directory.CreateDirectory(outputDirectory);

        progress?.Report("Extracting backup archive...");

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = outputDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add($"-o{outputDirectory}");

        foreach (var pattern in includePatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            startInfo.ArgumentList.Add(pattern);
        }

        foreach (var excludePattern in (excludePatterns ?? []).Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            startInfo.ArgumentList.Add($"-xr!{excludePattern}");
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            progress?.Report(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            progress?.Report(stderr.Trim());
        }

        return process.ExitCode;
    }

    private async Task<int> RunArchiveCommandAsync(
        string archivePath,
        string workingDirectory,
        IReadOnlyList<string> relativePaths,
        IReadOnlyList<string> excludeArguments,
        bool replaceArchive,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (relativePaths.Count == 0)
        {
            return 0;
        }

        var listFilePath = Path.Combine(Path.GetTempPath(), $"allocator-7zip-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllLinesAsync(listFilePath, relativePaths, Encoding.UTF8, cancellationToken);

            progress?.Report(replaceArchive
                ? "Compressing backup directly into the archive..."
                : "Adding backup details to the archive...");

            var startInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("a");
            startInfo.ArgumentList.Add("-t7z");
            startInfo.ArgumentList.Add("-mx=1");
            startInfo.ArgumentList.Add("-mmt=on");
            startInfo.ArgumentList.Add("-bsp1");
            startInfo.ArgumentList.Add("-bso1");
            startInfo.ArgumentList.Add("-bse1");
            startInfo.ArgumentList.Add(archivePath);
            startInfo.ArgumentList.Add($"@{listFilePath}");

            foreach (var excludeArgument in excludeArguments)
            {
                startInfo.ArgumentList.Add(excludeArgument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = PumpReaderAsync(process.StandardOutput, progress, cancellationToken);
            var stderrTask = PumpReaderAsync(process.StandardError, progress, cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken));

            return process.ExitCode;
        }
        finally
        {
            try
            {
                if (File.Exists(listFilePath))
                {
                    File.Delete(listFilePath);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task PumpReaderAsync(StreamReader reader, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report(line.Trim());
            }
        }
    }
}
