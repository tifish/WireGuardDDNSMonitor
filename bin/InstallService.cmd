sc create "WireGuard DDNS Monitor" binPath="%~dp0WireGuardDDNSMonitor.exe" start=auto
if errorlevel 1 pause
