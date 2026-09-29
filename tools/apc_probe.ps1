# APC64 Test: Identity, Display, Pad-Farben und -Modi, Touch-Strip-LEDs.
# Alles flüchtig, am Ende wird aufgeräumt. Live muss geschlossen sein.
#   powershell -ExecutionPolicy Bypass -File C:\DEV\APC64\tools\apc_probe.ps1
param([string]$Port = "APC64", [int]$Hold = 6)

Add-Type -Path (Join-Path $PSScriptRoot "MidiTool.cs")
$outId = ([Midi]::Outs("APC64") | Where-Object { $_.Value -eq $Port } | Select-Object -First 1).Key
$inId  = ([Midi]::Ins("APC64")  | Where-Object { $_.Value -eq $Port } | Select-Object -First 1).Key
if ($null -eq $outId -or $null -eq $inId) { "Port '$Port' nicht gefunden: " + (([Midi]::Outs("APC64") | % Value) -join ', '); exit 1 }
if (-not [Midi]::Open($outId, $inId)) { "Ports belegt (Live offen?)."; exit 1 }

function Sx([int]$id, [byte[]]$payload) {
    $n = $payload.Length
    [byte[]](@(0xF0, 0x47, 0x00, 0x53, $id, ($n -shr 7), ($n -band 0x7F)) + $payload + @(0xF7))
}
function Line([int]$i, [string]$t) { [Midi]::Send((Sx 0x10 ([byte[]](@($i) + [Text.Encoding]::ASCII.GetBytes($t) + @(0))))) }
function Pad([int]$note, [int]$color, [int]$ch = 6) { [Midi]::Short(0x90 -bor $ch, $note, $color) }
function Hex([byte[]]$b) { ($b | ForEach-Object { '{0:X2}' -f $_ }) -join ' ' }
function Step($t) { Write-Output ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $t) }

function Clear-All {
    foreach ($ch in 0, 6, 10, 14) { 0..63 | ForEach-Object { Pad $_ 0 $ch } }
    Pad 89 0
    0..7 | ForEach-Object { [Midi]::Short(0xB0, 104 + $_, 0); [Midi]::Short(0xE0 -bor $_, 0, 0) }
}

try {
    # 1. Identity
    $id = [Midi]::Request([byte[]]@(0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7), 500)
    Step ("Identity-Antwort: " + $(if ($id) { Hex $id } else { "(keine)" }))

    # 2. Display
    [Midi]::Send((Sx 0x1C ([byte[]]@(1))))
    Line 0 "MIKRO FX"; Line 1 "HALLO"; Line 2 "APC64"
    Pad 89 45
    Step "Display: 3 Zeilen 'MIKRO FX' / 'HALLO' / 'APC64', Header-Farbe blau ($Hold s)"
    Start-Sleep $Hold

    # 3. Pads: Palette 1..64 auf allen 64 Pads, volle Helligkeit
    Line 1 "FARBEN"; Line 2 "1-64"
    0..63 | ForEach-Object { Pad $_ ($_ + 1) }
    Step "Pads: Farben 1-64 (Pad unten links = 1), volle Helligkeit ($Hold s)"
    Start-Sleep $Hold
    Line 2 "65-127"
    0..62 | ForEach-Object { Pad $_ ($_ + 65) }
    Pad 63 0
    Step "Pads: Farben 65-127 ($Hold s)"
    Start-Sleep $Hold

    # 4. LED-Modi pro Reihe: unten voll, dann halb, puls, blink
    Clear-All
    [Midi]::Send((Sx 0x1C ([byte[]]@(1))))
    Line 1 "MODI"; Line 2 "V H P B"
    0..7   | ForEach-Object { Pad $_ 21 6 }      # Reihe 1 voll grün
    8..15  | ForEach-Object { Pad $_ 21 0 }      # Reihe 2 halb
    16..23 | ForEach-Object { Pad $_ 21 10 }     # Reihe 3 pulsierend
    24..31 | ForEach-Object { Pad $_ 21 14 }     # Reihe 4 blinkend
    Step "LED-Modi: Reihe 1 voll, 2 halb, 3 pulsierend, 4 blinkend ($Hold s)"
    Start-Sleep $Hold

    # 5. Touch-Strips: Stil, Farbe, Wert
    Line 1 "STRIPS"; Line 2 ""
    $stripColors = 5, 9, 13, 21, 37, 45, 49, 53
    0..7 | ForEach-Object { [Midi]::Short(0xB0, 104 + $_, 1); [Midi]::Short(0xB0, 112 + $_, $stripColors[$_]) }
    Step "Touch-Strips: jede in eigener Farbe, Werte fahren hoch und runter"
    foreach ($pass in 0..1) {
        foreach ($v in (0..32) + (32..0)) {
            $val = [int]($v / 32.0 * 16383)
            0..7 | ForEach-Object { $x = [int](($val + $_ * 1000) % 16384); [Midi]::Short(0xE0 -bor $_, $x -band 0x7F, $x -shr 7) }
            Start-Sleep -Milliseconds 25
        }
    }
    0..7 | ForEach-Object { [Midi]::Short(0xB0, 104 + $_, 3); [Midi]::Short(0xE0 -bor $_, 0, 64) }
    Step "Touch-Strips: Stil bipolar, alle in der Mitte (3 s)"
    Start-Sleep 3
}
finally {
    Clear-All
    Line 0 ""; Line 1 ""; Line 2 ""
    [Midi]::Send((Sx 0x1C ([byte[]]@(0))))
    Step "Aufgeraeumt: LEDs aus, Display an das Geraet zurueckgegeben"
    [Midi]::Close()
}
