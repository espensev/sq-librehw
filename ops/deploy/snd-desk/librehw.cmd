@echo off
rem Start LibreHardwareMonitor using the configured launcher.
for %%H in (/ -) do if #%%H?==#%1 goto help
for %%H in (/ -) do if #"%%H?"==#%1 goto help
for %%H in (-h "-h" -Help "-Help" --help "--help") do if /i #%%H==#%1 goto help
"%SEV_LOCAL_BIN%\runw\runw.exe" /wait /quiet /cwd:- "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%MACHINE_TOOLS_ROOT%\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1" %*
exit /b %ERRORLEVEL%
:help
echo Usage: librehw [-StartupTimeoutSeconds 5-120] [-ValidateScriptOnly]
echo Start LibreHardwareMonitor using the configured launcher.
exit /b 0
