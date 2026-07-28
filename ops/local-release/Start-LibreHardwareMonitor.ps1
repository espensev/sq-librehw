[CmdletBinding()]
param(
    [ValidateRange(5, 120)]
    [int] $StartupTimeoutSeconds = 30,

    [switch] $ValidateScriptOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$InstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW'
$ExecutablePath = Join-Path $InstallRoot 'LibreHardwareMonitor.Windows.Forms.exe'
$RuntimeConfigPath = Join-Path $InstallRoot 'librehw.runtime.json'
$ManagedTaskPath = '\SevGrp\AdminTask\LibreHW-No-UAC'
$IdentityVerifierPath =
    'C:\Users\Sev\OneDrive\common\common_dev\Get-VerifiedMachineIdentity.ps1'
$ExpectedMachineId = 'snd-desk'
$ProcessName = 'LibreHardwareMonitor.Windows.Forms'
$MutexName = 'Local\Sev.LibreHardwareMonitorLauncher'

function Assert-LauncherMachineIdentity {
    if (-not (Test-Path -LiteralPath $IdentityVerifierPath -PathType Leaf)) {
        throw "Machine identity verifier not found: '$IdentityVerifierPath'."
    }

    $identity = & $IdentityVerifierPath
    if ([string]$identity.status -cne 'VERIFIED' -or
        [string]$identity.machineId -cne $ExpectedMachineId) {
        throw "This launcher is restricted to verified machine '$ExpectedMachineId'."
    }
}

function Test-LauncherPathEqual {
    param(
        [Parameter(Mandatory)]
        [string] $Left,

        [Parameter(Mandatory)]
        [string] $Right
    )

    return [string]::Equals(
        [System.IO.Path]::GetFullPath($Left),
        [System.IO.Path]::GetFullPath($Right),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-InstalledRuntime {
    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw "Libre Hardware Monitor was not found at '$ExecutablePath'."
    }

    if (-not (Test-Path -LiteralPath $RuntimeConfigPath -PathType Leaf)) {
        throw "Libre Hardware Monitor runtime config was not found at '$RuntimeConfigPath'."
    }

    $runtimeConfigInfo = Get-Item -LiteralPath $RuntimeConfigPath
    if ($runtimeConfigInfo.Length -gt 65536) {
        throw 'Libre Hardware Monitor runtime config is unexpectedly large.'
    }

    try {
        $runtimeConfig =
            Get-Content -LiteralPath $RuntimeConfigPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Libre Hardware Monitor runtime config is invalid: $($_.Exception.Message)"
    }

    if ([string]$runtimeConfig.schema -cne 'sq.librehw.runtime.v1') {
        throw "Unsupported Libre Hardware Monitor runtime schema '$($runtimeConfig.schema)'."
    }
    if ([string]$runtimeConfig.dataRoot -notmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+(?:\\|$))') {
        throw 'Libre Hardware Monitor runtime dataRoot must be absolute.'
    }
    if (-not (Test-LauncherPathEqual `
        -Left ([string]$runtimeConfig.dataRoot) `
        -Right 'E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor')) {
        throw 'Libre Hardware Monitor runtime dataRoot is not the fixed machine-local data root.'
    }
    if ([string]$runtimeConfig.managedStartupTaskPath -cne $ManagedTaskPath) {
        throw "Libre Hardware Monitor runtime task must be '$ManagedTaskPath'."
    }

    $actualProperties = @($runtimeConfig.PSObject.Properties.Name | Sort-Object)
    $expectedProperties = @(@('dataRoot', 'managedStartupTaskPath', 'schema') | Sort-Object)
    if (($actualProperties -join "`n") -cne ($expectedProperties -join "`n")) {
        throw 'Libre Hardware Monitor runtime config contains unsupported or missing fields.'
    }
}

if (-not ('Sev.LibreHardwareMonitorLauncher.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Sev.LibreHardwareMonitorLauncher
{
    public static class NativeMethods
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint fromThread, uint toThread, bool attach);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        public static IntPtr FindMainWindow(int processId)
        {
            IntPtr match = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                uint ownerProcessId;
                GetWindowThreadProcessId(hWnd, out ownerProcessId);
                if (ownerProcessId != processId)
                    return true;

                StringBuilder title = new StringBuilder(512);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.ToString().StartsWith("Libre Hardware Monitor", StringComparison.OrdinalIgnoreCase))
                {
                    match = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return match;
        }

        private static bool HasVisibleChildWindow(IntPtr hWnd)
        {
            bool found = false;
            EnumChildWindows(hWnd, (child, _) =>
            {
                if (IsWindowVisible(child))
                {
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static bool WaitForManagedUi(IntPtr hWnd)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                if (IsWindowVisible(hWnd) && HasVisibleChildWindow(hWnd))
                    return true;

                System.Threading.Thread.Sleep(50);
            }

            return false;
        }

        public static bool RestoreAndActivate(IntPtr hWnd)
        {
            const int SW_RESTORE = 9;

            if (!IsWindowVisible(hWnd))
            {
                const uint WM_SYSCOMMAND = 0x0112;
                const uint SC_MINIMIZE = 0xF020;
                const uint SMTO_ABORTIFHUNG = 0x0002;
                UIntPtr messageResult;

                // Route through the app-owned tray toggle. Forcing a hidden
                // WinForms HWND visible can expose an empty native frame.
                if (SendMessageTimeout(
                        hWnd,
                        WM_SYSCOMMAND,
                        new UIntPtr(SC_MINIMIZE),
                        IntPtr.Zero,
                        SMTO_ABORTIFHUNG,
                        2000,
                        out messageResult) == IntPtr.Zero)
                    return false;
            }
            else if (IsIconic(hWnd))
            {
                ShowWindowAsync(hWnd, SW_RESTORE);
            }

            if (!WaitForManagedUi(hWnd))
                return false;

            IntPtr foregroundWindow = GetForegroundWindow();
            uint ignoredProcessId;
            uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, out ignoredProcessId);
            uint targetThread = GetWindowThreadProcessId(hWnd, out ignoredProcessId);
            uint currentThread = GetCurrentThreadId();

            bool attachedForeground = currentThread != foregroundThread &&
                                      AttachThreadInput(currentThread, foregroundThread, true);
            bool attachedTarget = currentThread != targetThread &&
                                  AttachThreadInput(currentThread, targetThread, true);

            try
            {
                const uint SWP_NOSIZE = 0x0001;
                const uint SWP_NOMOVE = 0x0002;
                const uint SWP_SHOWWINDOW = 0x0040;
                IntPtr HWND_TOPMOST = new IntPtr(-1);
                IntPtr HWND_NOTOPMOST = new IntPtr(-2);

                BringWindowToTop(hWnd);
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
                SetForegroundWindow(hWnd);
                SetFocus(hWnd);

                return GetForegroundWindow() == hWnd;
            }
            finally
            {
                if (attachedTarget)
                    AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
'@
}

function Get-LibreHardwareMonitorProcess {
    $entries = @(foreach ($process in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
        $path = $null
        try {
            $path = $process.Path
        }
        catch {
            $path = $null
        }

        [pscustomobject]@{
            Process = $process
            Path = $path
            IsExact = $path -and (Test-LauncherPathEqual -Left $path -Right $ExecutablePath)
        }
    })

    $mismatched = @($entries | Where-Object { $_.Path -and -not $_.IsExact })
    if ($mismatched.Count -gt 0) {
        $details = ($mismatched | ForEach-Object { "PID $($_.Process.Id): $($_.Path)" }) -join '; '
        throw "A path-mismatched Libre Hardware Monitor process is running: $details"
    }

    if ($entries.Count -gt 1) {
        throw "More than one Libre Hardware Monitor process is running: $($entries.Process.Id -join ', ')."
    }

    # An unelevated launcher may be denied Process.Path for one elevated LHM
    # process. Treat that single PID as existing so the wrapper never creates a
    # duplicate; known path mismatches above still fail closed.
    # PowerShell 5.1 StrictMode rejects member enumeration on an empty array.
    # Project through the pipeline so the absent-process branch returns @()
    # and can continue to the managed task.
    return @($entries | ForEach-Object { $_.Process })
}

function Restore-LibreHardwareMonitor {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $windowHandle = [IntPtr]::Zero
    do {
        $windowHandle =
            [Sev.LibreHardwareMonitorLauncher.NativeMethods]::FindMainWindow($Process.Id)
        if ($windowHandle -ne [IntPtr]::Zero) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "Libre Hardware Monitor PID $($Process.Id) has no discoverable main window."
    }

    if (-not [Sev.LibreHardwareMonitorLauncher.NativeMethods]::RestoreAndActivate($windowHandle)) {
        throw 'Libre Hardware Monitor was restored, but Windows refused foreground ownership.'
    }
}

function Start-ManagedLibreHardwareMonitor {
    $task = Get-ScheduledTask `
        -TaskPath '\SevGrp\AdminTask\' `
        -TaskName 'LibreHW-No-UAC' `
        -ErrorAction Stop
    $actions = @($task.Actions)
    $triggers = @($task.Triggers)
    if ($actions.Count -ne 1 -or
        -not (Test-LauncherPathEqual -Left ([string]$actions[0].Execute) -Right $ExecutablePath) -or
        -not (Test-LauncherPathEqual -Left ([string]$actions[0].WorkingDirectory) -Right $InstallRoot) -or
        [string]$task.State -ceq 'Disabled' -or
        -not [bool]$task.Settings.Enabled -or
        [string]$task.Principal.UserId -notin @('Sev', 'SND-DESK\Sev') -or
        [string]$task.Principal.LogonType -cne 'Interactive' -or
        [string]$task.Principal.RunLevel -cne 'Highest' -or
        [string]$task.Settings.MultipleInstances -cne 'IgnoreNew' -or
        -not [bool]$task.Settings.StartWhenAvailable -or
        [bool]$task.Settings.AllowHardTerminate -or
        $triggers.Count -ne 1 -or
        [string]$triggers[0].CimClass.CimClassName -cne 'MSFT_TaskLogonTrigger') {
        throw "Managed task '$ManagedTaskPath' does not target the stable installed runtime."
    }

    Start-ScheduledTask -TaskPath '\SevGrp\AdminTask\' -TaskName 'LibreHW-No-UAC'
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $processes = @(Get-LibreHardwareMonitorProcess)
        if ($processes.Count -eq 1) {
            return $processes[0]
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out waiting for '$ExecutablePath' after starting '$ManagedTaskPath'."
}

if ($ValidateScriptOnly) {
    # The public CMD shim uses Windows PowerShell 5.1. Exercise process
    # materialization here so StrictMode scalar/null Count regressions are
    # caught without launching, restoring, or otherwise mutating the app.
    $detectedProcesses = @(Get-LibreHardwareMonitorProcess)
    [pscustomobject]@{
        Result = 'PASS'
        InstallRoot = $InstallRoot
        ExecutablePath = $ExecutablePath
        RuntimeConfigPath = $RuntimeConfigPath
        ManagedTaskPath = $ManagedTaskPath
        ExpectedMachineId = $ExpectedMachineId
        DetectedProcessCount = $detectedProcesses.Count
        MutationPerformed = $false
    }
    return
}

$launcherMutex = [System.Threading.Mutex]::new($false, $MutexName)
$lockTaken = $false

try {
    try {
        $lockTaken = $launcherMutex.WaitOne([TimeSpan]::FromSeconds(10))
    }
    catch [System.Threading.AbandonedMutexException] {
        $lockTaken = $true
    }

    if (-not $lockTaken) {
        throw 'Timed out waiting for another Libre Hardware Monitor launch request.'
    }

    Assert-LauncherMachineIdentity
    Assert-InstalledRuntime

    $processes = @(Get-LibreHardwareMonitorProcess)
    $process = if ($processes.Count -eq 1) {
        $processes[0]
    }
    else {
        Start-ManagedLibreHardwareMonitor
    }

    Restore-LibreHardwareMonitor -Process $process
}
finally {
    if ($lockTaken) {
        $launcherMutex.ReleaseMutex()
    }

    $launcherMutex.Dispose()
}
