using System.Text.Json;
using TheAllocator.Models;
using TheAllocator.Services;

var repositoryRoot = FindRepositoryRoot();
var restoreServiceSource = File.ReadAllText(Path.Combine(repositoryRoot, "Services", "RestoreService.cs"));
var windowsProfileSource = File.ReadAllText(Path.Combine(repositoryRoot, "Services", "WindowsProfileService.cs"));

var tests = new (string Name, Action Body)[]
{
    ("saved host address wins over port name", () =>
        Equal("printer.example.edu", PrinterPortAddressPolicy.ResolveRestoreHostAddress("printer.example.edu", "IP_10.1.2.3"))),

    ("legacy IP_ port infers IPv4 address", () =>
        Equal("10.1.2.3", PrinterPortAddressPolicy.ResolveRestoreHostAddress("", "IP_10.1.2.3"))),

    ("legacy raw IP port infers address", () =>
        Equal("10.1.2.3", PrinterPortAddressPolicy.ResolveRestoreHostAddress(null, "10.1.2.3"))),

    ("legacy hostname port infers hostname", () =>
        Equal("print01.ad.vassar.edu", PrinterPortAddressPolicy.ResolveRestoreHostAddress("", "print01.ad.vassar.edu"))),

    ("ambiguous custom port name is not guessed", () =>
        Equal(string.Empty, PrinterPortAddressPolicy.ResolveRestoreHostAddress("", "Faculty Office Printer Port"))),

    ("old printer JSON remains compatible", () =>
    {
        const string json = "{\"Name\":\"Old Printer\",\"DriverName\":\"Driver\",\"PortName\":\"IP_10.20.30.40\"}";
        var printer = JsonSerializer.Deserialize<BackupPrinterInfo>(json) ?? throw new Exception("Printer JSON returned null.");
        Equal(string.Empty, printer.HostAddress);
        Equal("IP_10.20.30.40", printer.PortName);
    }),

    ("profile hives are excluded", () =>
        True(PortableProfilePolicy.ShouldExcludeFile("NTUSER.DAT"))),

    ("AppData Local is machine-specific", () =>
        True(PortableProfilePolicy.IsMachineSpecificPath(Path.Combine("AppData", "Local", "Microsoft", "Windows")))),

    ("portable roaming app data is allowed", () =>
        False(PortableProfilePolicy.IsMachineSpecificPath(Path.Combine("AppData", "Roaming", "Mozilla", "Firefox")))),

    ("matching SIDs pass", () =>
        RestoreIdentityPolicy.EnsureSidMatches("S-1-5-21-100", "s-1-5-21-100", "test account")),

    ("mismatched SIDs are blocked", () =>
        Throws<InvalidOperationException>(() =>
            RestoreIdentityPolicy.EnsureSidMatches("S-1-5-21-100", "S-1-5-21-200", "test account"))),

    ("missing SID is blocked", () =>
        Throws<InvalidOperationException>(() =>
            RestoreIdentityPolicy.EnsureSidMatches("", "S-1-5-21-200", "test account"))),

    ("restore uses saved host address policy", () =>
        Contains("PrinterPortAddressPolicy.ResolveRestoreHostAddress(printer.HostAddress, printer.PortName)", restoreServiceSource)),

    ("required printer commands remain fatal", () =>
    {
        True(CountOccurrences(restoreServiceSource, "throwOnError: true") >= 3);
        Contains("if (throwOnError && process.ExitCode != 0)", restoreServiceSource);
    }),

    ("existing profile SID check uses testable policy", () =>
    {
        Contains("RestoreIdentityPolicy.EnsureSidMatches", windowsProfileSource);
        False(windowsProfileSource.Contains("CreateIdentityFromSid(", StringComparison.Ordinal));
    })
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"Regression tests: {tests.Length - failures.Count} passed, {failures.Count} failed.");
if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "TheAllocator.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate TheAllocator.csproj from the current working directory.");
}

static int CountOccurrences(string text, string value)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new Exception($"Expected source to contain '{expected}'.");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"Expected '{expected}', got '{actual}'.");
    }
}

static void True(bool value)
{
    if (!value)
    {
        throw new Exception("Expected true, got false.");
    }
}

static void False(bool value)
{
    if (value)
    {
        throw new Exception("Expected false, got true.");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new Exception($"Expected {typeof(TException).Name} to be thrown.");
}
