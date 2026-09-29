using System.Text.Json;
using Windows.Graphics;

namespace TheAllocator.WinUI.Infrastructure;

public static class WindowBoundsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vassar College",
        "The Allocator",
        "window.json");

    public static (PointInt32 Position, SizeInt32 Size)? Load()
    {
        try
        {
            var state = JsonSerializer.Deserialize<WindowState>(File.ReadAllText(SettingsPath));
            if (state is null || state.Width < 1024 || state.Height < 720) return null;
            return (new PointInt32(state.X, state.Y), new SizeInt32(state.Width, state.Height));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(PointInt32 position, SizeInt32 size)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new WindowState(position.X, position.Y, size.Width, size.Height)));
        }
        catch
        {
        }
    }

    private sealed record WindowState(int X, int Y, int Width, int Height);
}
