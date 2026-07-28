// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

internal enum RuntimeDataRootSource
{
    RuntimeConfiguration,
    ExplicitEnvironment,
    SqDataEnvironment,
    ExecutableDirectory
}

internal sealed class RuntimePaths
{
    internal const string RuntimeConfigurationFileName = "librehw.runtime.json";
    internal const string RuntimeConfigurationSchema = "sq.librehw.runtime.v1";
    internal const string DataRootEnvironmentVariable = "LIBREHARDWAREMONITOR_DATA_ROOT";
    internal const string SqDataEnvironmentVariable = "sqdata";
    internal const string ApplicationDataDirectoryName = "LibreHardwareMonitor";
    internal const string SettingsFileName = "LibreHardwareMonitor.Windows.Forms.config";
    internal const int MaxRuntimeConfigurationBytes = 64 * 1024;

    private static readonly object CurrentLock = new();
    private static RuntimePaths _current;

    private RuntimePaths(
        string executablePath,
        string runtimeConfigurationPath,
        string dataRoot,
        string managedStartupTaskPath,
        RuntimeDataRootSource dataRootSource)
    {
        ExecutablePath = executablePath;
        ExecutableDirectory = Path.GetDirectoryName(executablePath);
        RuntimeConfigurationPath = runtimeConfigurationPath;
        DataRoot = dataRoot;
        LogDirectory = dataRootSource == RuntimeDataRootSource.ExecutableDirectory
            ? dataRoot
            : Path.Combine(dataRoot, "logs");
        SettingsFilePath = dataRootSource == RuntimeDataRootSource.ExecutableDirectory
            ? Path.ChangeExtension(executablePath, ".config")
            : Path.Combine(dataRoot, SettingsFileName);
        ManagedStartupTaskPath = managedStartupTaskPath;
        DataRootSource = dataRootSource;
    }

    internal static RuntimePaths Current
    {
        get
        {
            lock (CurrentLock)
            {
                return _current ??= Resolve(Application.ExecutablePath, Environment.GetEnvironmentVariable);
            }
        }
    }

    internal string ExecutablePath { get; }

    internal string ExecutableDirectory { get; }

    internal string RuntimeConfigurationPath { get; }

    internal string DataRoot { get; }

    internal string LogDirectory { get; }

    internal string SettingsFilePath { get; }

    internal string ManagedStartupTaskPath { get; }

    internal RuntimeDataRootSource DataRootSource { get; }

    internal static RuntimePaths Initialize(string executablePath)
    {
        lock (CurrentLock)
        {
            return _current ??= Resolve(executablePath, Environment.GetEnvironmentVariable);
        }
    }

    internal static RuntimePaths Resolve(string executablePath, Func<string, string> getEnvironmentVariable)
    {
        if (getEnvironmentVariable == null)
            throw new ArgumentNullException(nameof(getEnvironmentVariable));

        string normalizedExecutablePath = NormalizeAbsoluteFileSystemPath(
            executablePath,
            "The executable path");
        string executableDirectory = Path.GetDirectoryName(normalizedExecutablePath);
        string runtimeConfigurationPath = Path.Combine(executableDirectory, RuntimeConfigurationFileName);

        string dataRoot;
        string managedStartupTaskPath = null;
        RuntimeDataRootSource source;

        if (File.Exists(runtimeConfigurationPath))
        {
            RuntimeConfiguration configuration = LoadRuntimeConfiguration(runtimeConfigurationPath);
            dataRoot = NormalizeAbsoluteFileSystemPath(
                configuration.DataRoot,
                $"'{nameof(configuration.DataRoot)}' in '{runtimeConfigurationPath}'");
            managedStartupTaskPath = NormalizeManagedStartupTaskPath(
                configuration.ManagedStartupTaskPath,
                runtimeConfigurationPath);
            source = RuntimeDataRootSource.RuntimeConfiguration;
        }
        else
        {
            string explicitDataRoot = getEnvironmentVariable(DataRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(explicitDataRoot))
            {
                dataRoot = NormalizeAbsoluteFileSystemPath(
                    explicitDataRoot,
                    $"Environment variable {DataRootEnvironmentVariable}");
                source = RuntimeDataRootSource.ExplicitEnvironment;
            }
            else
            {
                string sqDataRoot = getEnvironmentVariable(SqDataEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(sqDataRoot))
                {
                    string normalizedSqDataRoot = NormalizeAbsoluteFileSystemPath(
                        sqDataRoot,
                        $"Environment variable {SqDataEnvironmentVariable}");
                    dataRoot = Path.Combine(normalizedSqDataRoot, ApplicationDataDirectoryName);
                    source = RuntimeDataRootSource.SqDataEnvironment;
                }
                else
                {
                    dataRoot = executableDirectory;
                    source = RuntimeDataRootSource.ExecutableDirectory;
                }
            }
        }

        EnsureSafeMutableDirectoryCreationPath(dataRoot, "The runtime data root");
        Directory.CreateDirectory(dataRoot);
        EnsureSafeMutableDirectory(dataRoot, "The runtime data root");
        RuntimePaths result = new(
            normalizedExecutablePath,
            runtimeConfigurationPath,
            dataRoot,
            managedStartupTaskPath,
            source);
        EnsureSafeMutableDirectoryCreationPath(result.LogDirectory, "The runtime log directory");
        Directory.CreateDirectory(result.LogDirectory);
        EnsureSafeMutableDirectory(result.LogDirectory, "The runtime log directory");
        EnsureSafeMutableFile(result.SettingsFilePath, "The runtime settings file");
        EnsureSafeMutableFile(result.SettingsFilePath + ".backup", "The runtime settings backup");
        EnsureSafeMutableFile(result.SettingsFilePath + ".new", "The runtime settings staging file");
        if (source != RuntimeDataRootSource.ExecutableDirectory)
            EnsureNoReparseChildren(result.LogDirectory, "The runtime log directory");
        return result;
    }

    internal static void EnsureSafeMutableDirectoryCreationPath(string path, string description)
    {
        if (!IsWindowsPlatform(Environment.OSVersion.Platform))
            return;

        string candidatePath = Path.GetFullPath(path);
        while (!Directory.Exists(candidatePath))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(candidatePath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"{description} creation path contains a reparse point: '{candidatePath}'.");
                }

                throw new InvalidDataException(
                    $"{description} creation path contains a non-directory entry: '{candidatePath}'.");
            }
            catch (FileNotFoundException)
            {
                // Continue to the nearest existing ancestor.
            }
            catch (DirectoryNotFoundException)
            {
                // Continue to the nearest existing ancestor.
            }

            string parentPath = Path.GetDirectoryName(candidatePath);
            if (string.IsNullOrWhiteSpace(parentPath) ||
                string.Equals(parentPath, candidatePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{description} has no verifiable existing ancestor: '{path}'.");
            }

            candidatePath = parentPath;
        }

        EnsureSafeMutableDirectory(candidatePath, description + " nearest existing ancestor");
    }

    internal static void EnsureSafeMutableDirectory(string path, string description)
    {
        if (!IsWindowsPlatform(Environment.OSVersion.Platform))
            return;

        string currentPath = Path.GetFullPath(path);
        if (!Directory.Exists(currentPath))
            throw new InvalidDataException($"{description} does not exist: '{currentPath}'.");

        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            FileAttributes attributes = File.GetAttributes(currentPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"{description} path ancestry contains a reparse point: '{currentPath}'.");
            }

            string parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) ||
                string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentPath = parentPath;
        }
    }

    internal static void EnsureSafeMutableFile(string path, string description)
    {
        if (!IsWindowsPlatform(Environment.OSVersion.Platform))
            return;

        string fullPath = Path.GetFullPath(path);
        EnsureSafeMutableDirectory(Path.GetDirectoryName(fullPath), description + " parent");
        try
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"{description} is a reparse point: '{fullPath}'.");
            if ((attributes & FileAttributes.Directory) != 0)
                throw new InvalidDataException($"{description} is a directory: '{fullPath}'.");
        }
        catch (FileNotFoundException)
        {
            // A missing destination is safe; its verified parent owns creation.
        }
        catch (DirectoryNotFoundException)
        {
            // A missing destination is safe; its verified parent owns creation.
        }
    }

    private static void EnsureNoReparseChildren(string directoryPath, string description)
    {
        foreach (string entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            if ((File.GetAttributes(entryPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"{description} contains a reparse point: '{entryPath}'.");
            }
        }
    }

    private static RuntimeConfiguration LoadRuntimeConfiguration(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            if (stream.Length > MaxRuntimeConfigurationBytes)
            {
                throw new InvalidDataException(
                    $"Runtime configuration '{path}' exceeds the {MaxRuntimeConfigurationBytes}-byte limit.");
            }

            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Runtime configuration '{path}' must contain one JSON object.");

            HashSet<string> properties = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "schema" &&
                    property.Name != "dataRoot" &&
                    property.Name != "managedStartupTaskPath")
                {
                    throw new InvalidDataException(
                        $"Runtime configuration '{path}' contains unknown property '{property.Name}'.");
                }

                if (!properties.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"Runtime configuration '{path}' contains duplicate property '{property.Name}'.");
                }
            }

            RuntimeConfiguration configuration =
                JsonSerializer.Deserialize<RuntimeConfiguration>(document.RootElement.GetRawText());
            if (configuration == null)
                throw new InvalidDataException($"Runtime configuration '{path}' is empty.");

            if (!string.Equals(configuration.Schema, RuntimeConfigurationSchema, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Runtime configuration '{path}' must declare schema '{RuntimeConfigurationSchema}'.");
            }

            if (string.IsNullOrWhiteSpace(configuration.DataRoot))
            {
                throw new InvalidDataException(
                    $"Runtime configuration '{path}' must declare an absolute dataRoot.");
            }

            if (string.IsNullOrWhiteSpace(configuration.ManagedStartupTaskPath))
            {
                throw new InvalidDataException(
                    $"Runtime configuration '{path}' must declare a managedStartupTaskPath.");
            }

            return configuration;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException ||
                                          exception is UnauthorizedAccessException ||
                                          exception is JsonException)
        {
            throw new InvalidDataException(
                $"Runtime configuration '{path}' could not be read: {exception.Message}",
                exception);
        }
    }

    private static string NormalizeAbsoluteFileSystemPath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException($"{description} must be an absolute path.");

        string candidate = path.Trim();
        string root;
        try
        {
            root = Path.GetPathRoot(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException ||
                                          exception is NotSupportedException ||
                                          exception is PathTooLongException)
        {
            throw new InvalidDataException($"{description} is not a valid absolute path.", exception);
        }

        if (!IsSupportedAbsolutePath(candidate, root, Environment.OSVersion.Platform))
            throw new InvalidDataException($"{description} must be an absolute path.");

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException ||
                                          exception is NotSupportedException ||
                                          exception is PathTooLongException)
        {
            throw new InvalidDataException($"{description} is not a valid absolute path.", exception);
        }
    }

    internal static bool IsSupportedAbsolutePath(
        string candidate,
        string root,
        PlatformID platform)
    {
        if (platform == PlatformID.Unix || platform == PlatformID.MacOSX)
        {
            return root == "/" &&
                   candidate.StartsWith("/", StringComparison.Ordinal);
        }

        bool isDriveAbsolute = root != null &&
                               root.Length >= 3 &&
                               root[1] == ':' &&
                               (root[2] == '\\' || root[2] == '/');
        bool isUncAbsolute = root != null &&
                             root.StartsWith(@"\\", StringComparison.Ordinal);
        return isDriveAbsolute || isUncAbsolute;
    }

    private static bool IsWindowsPlatform(PlatformID platform)
    {
        return platform != PlatformID.Unix && platform != PlatformID.MacOSX;
    }

    private static string NormalizeManagedStartupTaskPath(string taskPath, string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
            return null;

        string candidate = taskPath.Trim();
        bool hasAbsoluteRoot = candidate.Length > 1 &&
                               candidate[0] == '\\' &&
                               candidate[1] != '\\';
        bool hasTrailingSeparator = candidate[candidate.Length - 1] == '\\';
        if (!hasAbsoluteRoot ||
            hasTrailingSeparator ||
            candidate.IndexOf('/') >= 0)
        {
            throw new InvalidDataException(
                $"'managedStartupTaskPath' in '{configurationPath}' must be an absolute Task Scheduler path.");
        }

        string[] segments = candidate.Substring(1).Split('\\');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                throw new InvalidDataException(
                    $"'managedStartupTaskPath' in '{configurationPath}' contains an invalid path segment.");
            }
        }

        return candidate;
    }

    private sealed class RuntimeConfiguration
    {
        [JsonPropertyName("schema")]
        public string Schema { get; set; }

        [JsonPropertyName("dataRoot")]
        public string DataRoot { get; set; }

        [JsonPropertyName("managedStartupTaskPath")]
        public string ManagedStartupTaskPath { get; set; }
    }
}
