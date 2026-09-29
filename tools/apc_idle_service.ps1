# Wächter für die APC64-Idle-Animation, gedacht für den Windows-Autostart.
# Läuft die Animation, solange Live nicht offen ist; gibt das APC64 frei, sobald Live startet,
# und startet die Animation wieder, wenn Live geschlossen wird.
# Stop-Taste pausiert, Pad oder Encoder-Druck weckt. Läuft nur einmal gleichzeitig.
#   powershell -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\DEV\APC64\tools\apc_idle_service.ps1

$mutex = New-Object System.Threading.Mutex($false, "APC64IdleService")
if (-not $mutex.WaitOne(0)) { exit 0 }   # läuft schon

$logDir = Join-Path (Split-Path $PSScriptRoot -Parent) "logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir "service.log"
function Log($t) { try { Add-Content -Path $log -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $t) } catch {} }

Add-Type -Path (Join-Path $PSScriptRoot "MidiTool.cs")
Add-Type -Path (Join-Path $PSScriptRoot "ApcIdle.cs")
Log "Waechter gestartet"

try {
    while ($true) {
        if (Get-Process "Ableton Live*" -ErrorAction SilentlyContinue) { Start-Sleep 2; continue }
        $outId = ([Midi]::Outs("APC64") | Where-Object { $_.Value -eq "APC64" }).Key
        $inId  = ([Midi]::Ins("APC64")  | Where-Object { $_.Value -eq "APC64" }).Key
        if ($null -eq $outId -or $null -eq $inId) { Start-Sleep 10; continue }
        Log "Animation startet"
        $result = [ApcIdle]::Run($outId, $inId, 0, $true)
        Log $result
        if ($result -like "*belegt*") { Start-Sleep 10 } else { Start-Sleep 3 }
    }
}
finally { $mutex.ReleaseMutex() }
