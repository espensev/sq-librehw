@echo off
"%SEV_LOCAL_BIN%\runw\runw.exe" /wait /quiet /cwd:- "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1" %*
exit /b %ERRORLEVEL%
