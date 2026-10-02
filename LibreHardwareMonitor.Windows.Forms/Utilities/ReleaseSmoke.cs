using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

internal static class ReleaseSmoke
{
    internal const string ReceiptEnvironmentVariable = "LHM_RELEASE_SMOKE_RECEIPT";
    private const string Argument = "--release-smoke";

    internal static bool TryRun(
        string[] arguments,
        string executablePath,
        Func<string, string> getEnvironmentVariable,
        out int exitCode)
    {
        exitCode = 0;
        int smokeArgumentCount = arguments.Count(argument =>
            string.Equals(argument, Argument, StringComparison.Ordinal));
        if (smokeArgumentCount == 0)
            return false;

        if (arguments.Length != 1 || smokeArgumentCount != 1)
        {
            exitCode = 2;
            return true;
        }

        try
        {
            string fullExecutablePath = Path.GetFullPath(executablePath);
            if (!Path.IsPathRooted(executablePath) || !File.Exists(fullExecutablePath))
                throw new InvalidOperationException("Release-smoke executable path is not an existing absolute file.");

            string receiptValue = getEnvironmentVariable(ReceiptEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(receiptValue) || !Path.IsPathRooted(receiptValue))
                throw new InvalidOperationException("Release-smoke receipt path is not absolute.");

            string receiptPath = Path.GetFullPath(receiptValue);
            string receiptDirectory = Path.GetDirectoryName(receiptPath);
            if (string.IsNullOrWhiteSpace(receiptDirectory) || !Directory.Exists(receiptDirectory))
                throw new InvalidOperationException("Release-smoke receipt directory does not exist.");

            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(fullExecutablePath);
            if (string.IsNullOrWhiteSpace(versionInfo.FileVersion) ||
                string.IsNullOrWhiteSpace(versionInfo.ProductVersion))
                throw new InvalidOperationException("Release-smoke executable has no file/product version metadata.");

            ReleaseSmokeReceipt receipt = new()
            {
                Schema = "sq.lhm-release-smoke",
                Version = 1,
#if NETFRAMEWORK
                TargetFramework = "net472",
#else
                TargetFramework = "net10.0-windows",
#endif
                ExecutablePath = fullExecutablePath,
                FileVersion = versionInfo.FileVersion,
                ProductVersion = versionInfo.ProductVersion
            };

            using FileStream stream = new(
                receiptPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            JsonSerializer.Serialize(
                stream,
                receipt,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            stream.Flush(true);
        }
        catch
        {
            exitCode = 1;
        }

        return true;
    }

    private sealed class ReleaseSmokeReceipt
    {
        public string Schema { get; set; }

        public int Version { get; set; }

        public string TargetFramework { get; set; }

        public string ExecutablePath { get; set; }

        public string FileVersion { get; set; }

        public string ProductVersion { get; set; }
    }
}
