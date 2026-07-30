// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

internal sealed class PawnIoInstallerLease : IDisposable
{
    internal const string DirectoryNamePrefix = "LibreHardwareMonitor-PawnIO-";

    private static readonly SecurityIdentifier Administrators =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier CurrentUser = GetCurrentUser();

    private FileStream _identityLock;
    private int _disposed;

    private PawnIoInstallerLease(string directoryPath, string filePath, FileStream identityLock)
    {
        DirectoryPath = directoryPath;
        FilePath = filePath;
        _identityLock = identityLock;
    }

    internal string DirectoryPath { get; }

    internal string FilePath { get; }

    internal static PawnIoInstallerLease Create(Stream source, string trustedTemporaryRoot)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        string pathRoot;
        try
        {
            pathRoot = string.IsNullOrWhiteSpace(trustedTemporaryRoot)
                ? null
                : Path.GetPathRoot(trustedTemporaryRoot);
        }
        catch (Exception exception) when (exception is ArgumentException ||
                                          exception is NotSupportedException ||
                                          exception is PathTooLongException)
        {
            throw new ArgumentException(
                "The trusted temporary root must be a fully qualified drive or UNC path.",
                nameof(trustedTemporaryRoot),
                exception);
        }

        if (!RuntimePaths.IsSupportedAbsolutePath(
                trustedTemporaryRoot,
                pathRoot,
                Environment.OSVersion.Platform))
        {
            throw new ArgumentException(
                "The trusted temporary root must be a fully qualified drive or UNC path.",
                nameof(trustedTemporaryRoot));
        }

        string rootPath = Path.GetFullPath(trustedTemporaryRoot);
        DirectoryInfo root = new(rootPath);
        root.Refresh();
        if (!root.Exists)
            throw new DirectoryNotFoundException($"Trusted temporary root '{rootPath}' does not exist.");
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new SecurityException($"Trusted temporary root '{rootPath}' cannot be a reparse point.");
        try
        {
            RuntimePaths.EnsureSafeMutableDirectory(
                rootPath,
                "The PawnIO trusted temporary root");
        }
        catch (InvalidDataException exception)
        {
            throw new SecurityException(
                $"Trusted temporary root '{rootPath}' has unsafe path ancestry.",
                exception);
        }

        string directoryPath = CreateSecureDirectory(rootPath);
        string filePath = Path.Combine(directoryPath, "PawnIO_setup.exe");
        FileStream identityLock = null;
        try
        {
            FileInfo installerFile = new(filePath);
            using (FileStream writer = FileSystemAclExtensions.Create(
                       installerFile,
                       FileMode.CreateNew,
                       FileSystemRights.FullControl,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough,
                       CreateFileSecurity()))
            {
                source.CopyTo(writer);
                writer.Flush(true);
            }

            installerFile.Refresh();
            if ((installerFile.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new SecurityException("PawnIO temporary installer cannot be a reparse point.");
            ValidateSecurity(
                installerFile.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
                "PawnIO temporary installer",
                InheritanceFlags.None);

            // The protected directory makes the close/reopen transition safe. Keep this
            // read-only identity handle through process launch and exit. FileShare.Read lets
            // the image loader read the EXE while denying writers, rename, and deletion.
            identityLock = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            return new PawnIoInstallerLease(directoryPath, filePath, identityLock);
        }
        catch
        {
            identityLock?.Dispose();
            TryDeleteFile(filePath);
            TryDeleteDirectory(directoryPath);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _identityLock?.Dispose();
        _identityLock = null;
        TryDeleteFile(FilePath);
        TryDeleteDirectory(DirectoryPath);
    }

    private static string CreateSecureDirectory(string rootPath)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string path = Path.Combine(rootPath, DirectoryNamePrefix + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(path))
                continue;

            DirectorySecurity security = CreateDirectorySecurity();
            FileSystemAclExtensions.CreateDirectory(security, path);

            DirectoryInfo directory = new(path);
            directory.Refresh();
            try
            {
                if (!directory.Exists)
                    throw new IOException($"PawnIO temporary directory '{path}' was not created.");
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new SecurityException("PawnIO temporary directory cannot be a reparse point.");

                ValidateSecurity(
                    directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
                    "PawnIO temporary directory",
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
                return path;
            }
            catch
            {
                TryDeleteDirectory(path);
                throw;
            }
        }

        throw new IOException("Could not allocate a fresh PawnIO temporary directory.");
    }

    private static DirectorySecurity CreateDirectorySecurity()
    {
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            CurrentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            Administrators,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystem,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static FileSecurity CreateFileSecurity()
    {
        FileSecurity security = new();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            CurrentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            Administrators,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystem,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private static void ValidateSecurity(
        FileSystemSecurity security,
        string description,
        InheritanceFlags expectedInheritanceFlags)
    {
        SecurityIdentifier owner =
            (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier));
        if (!CurrentUser.Equals(owner) && !Administrators.Equals(owner))
        {
            throw new SecurityException(
                $"{description} must be owned by the current identity or BUILTIN\\Administrators.");
        }
        if (!security.AreAccessRulesProtected)
            throw new SecurityException($"{description} must have inheritance disabled.");

        bool currentUserAllowed = false;
        bool administratorsAllowed = false;
        bool localSystemAllowed = false;
        AuthorizationRuleCollection rules =
            security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        foreach (AuthorizationRule authorizationRule in rules)
        {
            if (authorizationRule is not FileSystemAccessRule rule ||
                rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier identity ||
                rule.FileSystemRights != FileSystemRights.FullControl ||
                rule.InheritanceFlags != expectedInheritanceFlags ||
                rule.PropagationFlags != PropagationFlags.None)
            {
                throw new SecurityException($"{description} contains an unexpected access rule.");
            }

            bool allowedIdentity = false;
            if (CurrentUser.Equals(identity))
            {
                currentUserAllowed = true;
                allowedIdentity = true;
            }

            if (Administrators.Equals(identity))
            {
                administratorsAllowed = true;
                allowedIdentity = true;
            }

            if (LocalSystem.Equals(identity))
            {
                localSystemAllowed = true;
                allowedIdentity = true;
            }

            if (!allowedIdentity)
            {
                throw new SecurityException(
                    $"{description} grants access outside the current identity, Administrators, and SYSTEM.");
            }
        }

        if (!currentUserAllowed || !administratorsAllowed || !localSystemAllowed)
        {
            throw new SecurityException(
                $"{description} must grant the current identity, Administrators, and SYSTEM access.");
        }
    }

    private static SecurityIdentifier GetCurrentUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User ??
               throw new InvalidOperationException(
                   "The current Windows identity has no security identifier.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            Debug.WriteLine("PawnIO temporary installer cleanup failed: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine("PawnIO temporary installer cleanup failed: " + exception.Message);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, false);
        }
        catch (IOException exception)
        {
            Debug.WriteLine("PawnIO temporary directory cleanup failed: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine("PawnIO temporary directory cleanup failed: " + exception.Message);
        }
    }
}
