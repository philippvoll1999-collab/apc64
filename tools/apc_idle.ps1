# APC64 Ruhezustands-Animation im Stil der MikroFX-Idle-Modi.
# Shift = nächster Modus (PULS, WELLE, ATMEN, LAUFLICHT), Encoder = Tempo,
# Pads = Reaktion, Stop-Taste = beenden (räumt LEDs und Display auf).
# Live muss geschlossen sein.
#   powershell -ExecutionPolicy Bypass -File C:\DEV\APC64\tools\apc_idle.ps1 [-Minutes 30]   (0 = bis Stop)
param([double]$Minutes = 0)

Add-Type -Path (Join-Path $PSScriptRoot "MidiTool.cs")
Add-Type -Path (Join-Path $PSScriptRoot "ApcIdle.cs")
$outId = ([Midi]::Outs("APC64") | Where-Object { $_.Value -eq "APC64" }).Key
$inId  = ([Midi]::Ins("APC64")  | Where-Object { $_.Value -eq "APC64" }).Key
if ($null -eq $outId -or $null -eq $inId) { "APC64 nicht gefunden."; exit 1 }
Write-Output ("[{0:HH:mm:ss}] Animation laeuft. Shift = Modus, Encoder = Tempo, Pads = Reaktion, Stop = beenden." -f (Get-Date))
Write-Output ("[{0:HH:mm:ss}] {1}" -f (Get-Date), [ApcIdle]::Run($outId, $inId, $Minutes))
