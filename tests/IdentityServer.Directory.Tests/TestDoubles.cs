using Microsoft.Extensions.Options;

namespace IdentityServer.Directory.Tests;

/// <summary>An options monitor that always returns the same value.</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Skips tests of Windows-only APIs, such as <c>SecurityIdentifier</c>, on other systems.</summary>
internal static class Platform
{
    public static void SkipUnlessWindows() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Parsing and encoding SIDs uses SecurityIdentifier, which needs Windows.");
}
