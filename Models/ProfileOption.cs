namespace TheAllocator.Models;

public sealed class ProfileOption
{
    public string DisplayName { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string ProfilePath { get; init; } = string.Empty;

    public string Sid { get; init; } = string.Empty;

    public override string ToString() => DisplayName;
}
