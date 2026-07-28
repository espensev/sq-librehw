using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class RuntimePathsTests : IDisposable
{
    private readonly string _root;
    private readonly string _executablePath;

    public RuntimePathsTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            nameof(RuntimePathsTests),
            Guid.NewGuid().ToString("N"));
        string executableDirectory = Path.Combine(_root, "app");
        Directory.CreateDirectory(executableDirectory);
        _executablePath = Path.Combine(
            executableDirectory,
            RuntimePaths.SettingsFileName.Replace(".config", ".exe"));
    }

    [Fact]
    public void Resolve_RuntimeConfigurationWinsAndCreatesSelectedDirectories()
    {
        string configuredDataRoot = Path.Combine(_root, "configured-data");
        string runtimeConfigurationPath = WriteRuntimeConfiguration(
            configuredDataRoot,
            @"\SevGrp\AdminTask\LibreHW-No-UAC");
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            [RuntimePaths.DataRootEnvironmentVariable] = Path.Combine(_root, "explicit-environment"),
            [RuntimePaths.SqDataEnvironmentVariable] = Path.Combine(_root, "sqdata")
        };

        RuntimePaths paths = RuntimePaths.Resolve(_executablePath, environment.GetValueOrDefault);

        Assert.Equal(Path.GetFullPath(configuredDataRoot), paths.DataRoot);
        Assert.Equal(
            Path.Combine(configuredDataRoot, "logs"),
            paths.LogDirectory);
        Assert.Equal(
            Path.Combine(configuredDataRoot, RuntimePaths.SettingsFileName),
            paths.SettingsFilePath);
        Assert.Equal(runtimeConfigurationPath, paths.RuntimeConfigurationPath);
        Assert.Equal(@"\SevGrp\AdminTask\LibreHW-No-UAC", paths.ManagedStartupTaskPath);
        Assert.Equal(RuntimeDataRootSource.RuntimeConfiguration, paths.DataRootSource);
        Assert.True(Directory.Exists(paths.DataRoot));
        Assert.True(Directory.Exists(paths.LogDirectory));
        Assert.False(Directory.Exists(environment[RuntimePaths.DataRootEnvironmentVariable]));
    }

    [Fact]
    public void Resolve_ExplicitEnvironmentWinsOverSqData()
    {
        string explicitDataRoot = Path.Combine(_root, "explicit-environment");
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            [RuntimePaths.DataRootEnvironmentVariable] = explicitDataRoot,
            [RuntimePaths.SqDataEnvironmentVariable] = Path.Combine(_root, "sqdata")
        };

        RuntimePaths paths = RuntimePaths.Resolve(_executablePath, environment.GetValueOrDefault);

        Assert.Equal(Path.GetFullPath(explicitDataRoot), paths.DataRoot);
        Assert.Equal(RuntimeDataRootSource.ExplicitEnvironment, paths.DataRootSource);
        Assert.Null(paths.ManagedStartupTaskPath);
        Assert.True(Directory.Exists(paths.LogDirectory));
    }

    [Fact]
    public void Resolve_SqDataAddsApplicationDirectory()
    {
        string sqDataRoot = Path.Combine(_root, "sqdata");
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            [RuntimePaths.SqDataEnvironmentVariable] = sqDataRoot
        };

        RuntimePaths paths = RuntimePaths.Resolve(_executablePath, environment.GetValueOrDefault);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(sqDataRoot), RuntimePaths.ApplicationDataDirectoryName),
            paths.DataRoot);
        Assert.Equal(RuntimeDataRootSource.SqDataEnvironment, paths.DataRootSource);
        Assert.True(Directory.Exists(paths.LogDirectory));
    }

    [Fact]
    public void Resolve_FallsBackToExecutableDirectoryForPortableUse()
    {
        RuntimePaths paths = RuntimePaths.Resolve(_executablePath, _ => null);

        Assert.Equal(Path.GetDirectoryName(_executablePath), paths.DataRoot);
        Assert.Equal(RuntimeDataRootSource.ExecutableDirectory, paths.DataRootSource);
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(_executablePath), RuntimePaths.SettingsFileName),
            paths.SettingsFilePath);
        Assert.Equal(Path.GetDirectoryName(_executablePath), paths.LogDirectory);
        Assert.True(Directory.Exists(paths.LogDirectory));
        Assert.False(Directory.Exists(Path.Combine(paths.DataRoot, "logs")));
    }

    [Fact]
    public void Resolve_PortableSettingsNameFollowsRenamedExecutable()
    {
        string renamedExecutablePath = Path.Combine(
            Path.GetDirectoryName(_executablePath),
            "RenamedPortableMonitor.exe");

        RuntimePaths paths = RuntimePaths.Resolve(renamedExecutablePath, _ => null);

        Assert.Equal(
            Path.ChangeExtension(renamedExecutablePath, ".config"),
            paths.SettingsFilePath);
        Assert.Equal(RuntimeDataRootSource.ExecutableDirectory, paths.DataRootSource);
    }

    [Fact]
    public void Resolve_RejectsReparseLogDirectoryWithoutChangingTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string dataRoot = Path.Combine(_root, "reparse-log-data");
        string externalRoot = Path.Combine(_root, "reparse-log-external");
        string logLink = Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(externalRoot);
        string sentinelPath = Path.Combine(externalRoot, "sentinel.txt");
        File.WriteAllText(sentinelPath, "unchanged");
        Directory.CreateSymbolicLink(logLink, externalRoot);
        WriteRuntimeConfiguration(dataRoot, @"\SevGrp\AdminTask\LibreHW-No-UAC");

        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => RuntimePaths.Resolve(_executablePath, _ => null));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(logLink))
                Directory.Delete(logLink);
        }
    }

    [Fact]
    public void Resolve_RejectsMissingDataRootBelowReparseBeforeCreatingTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string parentRoot = Path.Combine(_root, "reparse-parent");
        string externalRoot = Path.Combine(_root, "reparse-parent-external");
        string parentLink = Path.Combine(parentRoot, "redirect");
        Directory.CreateDirectory(parentRoot);
        Directory.CreateDirectory(externalRoot);
        string sentinelPath = Path.Combine(externalRoot, "sentinel.txt");
        File.WriteAllText(sentinelPath, "unchanged");
        Directory.CreateSymbolicLink(parentLink, externalRoot);
        string selectedDataRoot = Path.Combine(parentLink, "new-data");
        WriteRuntimeConfiguration(selectedDataRoot, @"\SevGrp\AdminTask\LibreHW-No-UAC");

        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => RuntimePaths.Resolve(_executablePath, _ => null));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(sentinelPath));
            Assert.False(Directory.Exists(Path.Combine(externalRoot, "new-data")));
        }
        finally
        {
            if (Directory.Exists(parentLink))
                Directory.Delete(parentLink);
        }
    }

    [Fact]
    public void Resolve_RejectsReparseSettingsFileWithoutChangingTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string dataRoot = Path.Combine(_root, "reparse-settings-data");
        string externalRoot = Path.Combine(_root, "reparse-settings-external");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(externalRoot);
        string externalSettings = Path.Combine(externalRoot, "outside.config");
        File.WriteAllText(externalSettings, "unchanged");
        string settingsLink = Path.Combine(dataRoot, RuntimePaths.SettingsFileName);
        File.CreateSymbolicLink(settingsLink, externalSettings);
        WriteRuntimeConfiguration(dataRoot, @"\SevGrp\AdminTask\LibreHW-No-UAC");

        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => RuntimePaths.Resolve(_executablePath, _ => null));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(externalSettings));
        }
        finally
        {
            if (File.Exists(settingsLink))
                File.Delete(settingsLink);
        }
    }

    [Fact]
    public void SupportedAbsolutePath_PreservesUnixPortablePaths()
    {
        const string unixPath = "/opt/librehardwaremonitor/LibreHardwareMonitor.exe";

        Assert.True(RuntimePaths.IsSupportedAbsolutePath(
            unixPath,
            "/",
            PlatformID.Unix));
        Assert.True(RuntimePaths.IsSupportedAbsolutePath(
            unixPath,
            "/",
            PlatformID.MacOSX));
        Assert.False(RuntimePaths.IsSupportedAbsolutePath(
            unixPath,
            "/",
            PlatformID.Win32NT));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"schema\":\"wrong\",\"dataRoot\":\"C:\\\\data\"}")]
    [InlineData("{\"schema\":\"sq.librehw.runtime.v1\",\"dataRoot\":\"relative\"}")]
    [InlineData("{\"schema\":\"sq.librehw.runtime.v1\",\"dataRoot\":\"C:\\\\data\",\"managedStartupTaskPath\":\"relative\"}")]
    public void Resolve_PresentInvalidRuntimeConfigurationFailsWithoutFallback(string json)
    {
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(_executablePath), RuntimePaths.RuntimeConfigurationFileName),
            json);
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            [RuntimePaths.DataRootEnvironmentVariable] = Path.Combine(_root, "fallback")
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RuntimePaths.Resolve(_executablePath, environment.GetValueOrDefault));

        Assert.Contains(RuntimePaths.RuntimeConfigurationFileName, exception.Message);
        Assert.False(Directory.Exists(environment[RuntimePaths.DataRootEnvironmentVariable]));
    }

    [Fact]
    public void Resolve_RejectsUnknownRuntimeConfigurationProperty()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(_executablePath),
            RuntimePaths.RuntimeConfigurationFileName);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new
            {
                schema = RuntimePaths.RuntimeConfigurationSchema,
                dataRoot = Path.Combine(_root, "configured-data"),
                unexpected = true
            }));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RuntimePaths.Resolve(_executablePath, _ => null));

        Assert.Contains("unknown property", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unexpected", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_RuntimeConfigurationRequiresManagedStartupTaskPath(
        string managedStartupTaskPath)
    {
        WriteRuntimeConfiguration(
            Path.Combine(_root, "configured-data"),
            managedStartupTaskPath);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RuntimePaths.Resolve(_executablePath, _ => null));

        Assert.Contains(
            "managedStartupTaskPath",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsOversizedRuntimeConfiguration()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(_executablePath),
            RuntimePaths.RuntimeConfigurationFileName);
        File.WriteAllText(
            path,
            new string(' ', RuntimePaths.MaxRuntimeConfigurationBytes + 1),
            Encoding.ASCII);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RuntimePaths.Resolve(_executablePath, _ => null));

        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(RuntimePaths.MaxRuntimeConfigurationBytes.ToString(), exception.Message);
    }

    [Theory]
    [InlineData(@"\Folder\.\Task")]
    [InlineData(@"\Folder\..\Task")]
    [InlineData(@"\Folder\\Task")]
    public void Resolve_RejectsInvalidManagedTaskPathSegments(string managedStartupTaskPath)
    {
        WriteRuntimeConfiguration(
            Path.Combine(_root, "configured-data"),
            managedStartupTaskPath);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RuntimePaths.Resolve(_executablePath, _ => null));

        Assert.Contains(
            "managedStartupTaskPath",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RuntimePaths.DataRootEnvironmentVariable)]
    [InlineData(RuntimePaths.SqDataEnvironmentVariable)]
    public void Resolve_SelectedRelativeEnvironmentPathFails(string variableName)
    {
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            [variableName] = "relative"
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RuntimePaths.Resolve(_executablePath, environment.GetValueOrDefault));

        Assert.Contains(variableName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PawnIoInstallerLease_IsExclusiveAndCleansFileAndDirectory()
    {
        string temporaryRoot = Path.Combine(_root, "temporary");
        Directory.CreateDirectory(temporaryRoot);
        byte[] payload = Encoding.ASCII.GetBytes("test PawnIO payload");

        using (MemoryStream disposedSource = new(payload))
        {
            disposedSource.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => PawnIoInstallerLease.Create(disposedSource, temporaryRoot));
        }

        Assert.Empty(Directory.GetDirectories(
            temporaryRoot,
            PawnIoInstallerLease.DirectoryNamePrefix + "*"));

        string extractionDirectory;
        string installerPath;
        using (MemoryStream source = new(payload))
        using (PawnIoInstallerLease installer = PawnIoInstallerLease.Create(source, temporaryRoot))
        {
            extractionDirectory = installer.DirectoryPath;
            installerPath = installer.FilePath;

            Assert.True(Directory.Exists(extractionDirectory));
            Assert.True(File.Exists(installerPath));
            Assert.Equal(payload, File.ReadAllBytes(installerPath));
            Assert.Equal(
                (FileAttributes)0,
                File.GetAttributes(extractionDirectory) & FileAttributes.ReparsePoint);

            SecurityIdentifier administrators =
                new(WellKnownSidType.BuiltinAdministratorsSid, null);
            DirectorySecurity directorySecurity = new DirectoryInfo(extractionDirectory)
                .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            Assert.Equal(
                administrators,
                directorySecurity.GetOwner(typeof(SecurityIdentifier)));
            Assert.True(directorySecurity.AreAccessRulesProtected);

            FileSecurity fileSecurity = new FileInfo(installerPath)
                .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            Assert.Equal(
                administrators,
                fileSecurity.GetOwner(typeof(SecurityIdentifier)));
            Assert.True(fileSecurity.AreAccessRulesProtected);

            // The image loader needs a compatible read open while the identity lease is held.
            using FileStream compatibleReader = new(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            Assert.Equal(payload.Length, compatibleReader.Length);

            Assert.Throws<IOException>(() =>
            {
                using FileStream writer = new(
                    installerPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
            });
            Assert.Throws<IOException>(() => File.Delete(installerPath));

            using MemoryStream secondSource = new(payload);
            using PawnIoInstallerLease second =
                PawnIoInstallerLease.Create(secondSource, temporaryRoot);
            Assert.NotEqual(installer.DirectoryPath, second.DirectoryPath);
            Assert.NotEqual(installer.FilePath, second.FilePath);
        }

        Assert.False(File.Exists(installerPath));
        Assert.False(Directory.Exists(extractionDirectory));
        Assert.Empty(Directory.GetDirectories(
            temporaryRoot,
            PawnIoInstallerLease.DirectoryNamePrefix + "*"));
    }

    [Fact]
    public void SensorGadget_PreservesFourParameterConstructor()
    {
        Assert.NotNull(typeof(SensorGadget).GetConstructor(
            new[]
            {
                typeof(IComputer),
                typeof(PersistentSettings),
                typeof(UnitManager),
                typeof(System.Windows.Forms.Control)
            }));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        { }
        catch (UnauthorizedAccessException)
        { }
    }

    private string WriteRuntimeConfiguration(string dataRoot, string managedStartupTaskPath)
    {
        string path = Path.Combine(
            Path.GetDirectoryName(_executablePath),
            RuntimePaths.RuntimeConfigurationFileName);
        string json = JsonSerializer.Serialize(new
        {
            schema = RuntimePaths.RuntimeConfigurationSchema,
            dataRoot,
            managedStartupTaskPath
        });
        File.WriteAllText(path, json);
        return path;
    }
}
