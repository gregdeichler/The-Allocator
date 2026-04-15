namespace TheAllocator.Services;

public static class SizeFormattingService
{
    public static string ToReadableSize(long bytes)
    {
        if (bytes < 0)
        {
            return "Unknown";
        }

        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:N0} {units[unitIndex]}"
            : $"{size:N2} {units[unitIndex]}";
    }
}
