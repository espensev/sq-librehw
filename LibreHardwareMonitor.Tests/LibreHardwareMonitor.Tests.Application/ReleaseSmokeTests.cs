using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class ReleaseSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "lhm-release-smoke-tests-" + Guid.NewGuid().ToString("N"));

    public ReleaseSmokeTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryRun_WithoutReleaseSmokeArgument_DoesNotHandleStartup()
    {
        bool handled = ReleaseSmoke.TryRun(
            Array.Empty<string>(),
            Environment.ProcessPath!,
            _ => throw new InvalidOperationException("Environment must not be read."),
            out int exitCode);

        Assert.False(handled);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void TryRun_WithExactArgument_WritesBoundedIdentityReceipt()
    {
        string executablePath = Environment.ProcessPath!;
        string receiptPath = Path.Combine(_root, "receipt.json");

        bool handled = ReleaseSmoke.TryRun(
            new[] { "--release-smoke" },
            executablePath,
            name => name == ReleaseSmoke.ReceiptEnvironmentVariable ? receiptPath : null,
            out int exitCode);

        Assert.True(handled);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(receiptPath));
        JsonElement root = document.RootElement;
        FileVersionInfo expectedVersion = FileVersionInfo.GetVersionInfo(executablePath);
        Assert.Equal("sq.lhm-release-smoke", root.GetProperty("schema").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("net10.0-windows", root.GetProperty("targetFramework").GetString());
        Assert.Equal(Path.GetFullPath(executablePath), root.GetProperty("executablePath").GetString());
        Assert.Equal(expectedVersion.FileVersion, root.GetProperty("fileVersion").GetString());
        Assert.Equal(expectedVersion.ProductVersion, root.GetProperty("productVersion").GetString());
    }

    [Fact]
    public void TryRun_WithMalformedArguments_FailsWithoutStartingNormalApplication()
    {
        string receiptPath = Path.Combine(_root, "malformed.json");

        bool handled = ReleaseSmoke.TryRun(
            new[] { "--release-smoke", "unexpected" },
            Environment.ProcessPath!,
            _ => receiptPath,
            out int exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(receiptPath));
    }

    [Fact]
    public void TryRun_WhenReceiptExists_FailsWithoutReplacingIt()
    {
        string receiptPath = Path.Combine(_root, "existing.json");
        File.WriteAllText(receiptPath, "preserve");

        bool handled = ReleaseSmoke.TryRun(
            new[] { "--release-smoke" },
            Environment.ProcessPath!,
            _ => receiptPath,
            out int exitCode);

        Assert.True(handled);
        Assert.Equal(1, exitCode);
        Assert.Equal("preserve", File.ReadAllText(receiptPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
