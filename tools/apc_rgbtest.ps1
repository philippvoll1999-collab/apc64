# APC64 Test: 7 Helligkeitsstufen per MIDI-Kanal und freie RGB-Farben per SysEx 24
# (Format aus der Doku des APC mini mk2). Live muss geschlossen sein.
#   powershell -ExecutionPolicy Bypass -File C:\DEV\APC64\tools\apc_rgbtest.ps1
param([int]$Hold = 8)

Add-Type -Path (Join-Path $PSScriptRoot "MidiTool.cs")
$outId = ([Midi]::Outs("APC64") | Where-Object { $_.Value -eq "APC64" }).Key
$inId  = ([Midi]::Ins("APC64")  | Where-Object { $_.Value -eq "APC64" }).Key
if ($null -eq $outId -or -not [Midi]::Open($outId, $inId)) { "APC64 nicht verfuegbar (Live offen?)."; exit 1 }

function Step($t) { Write-Output ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $t) }
function Line([int]$n, [string]$t) { $p = [byte[]](@($n) + [Text.Encoding]::ASCII.GetBytes($t) + @(0)); [Midi]::Send([byte[]](@(0xF0, 0x47, 0x00, 0x53, 0x10, 0, $p.Length) + $p + @(0xF7))) }
# RGB-SysEx: pro Pad (start, ende, R, G, B) mit 8-Bit-Werten als MSB/LSB
function Rgb([int]$dev, [int]$pad, [int]$r, [int]$g, [int]$b) {
    $data = [byte[]]@($pad, $pad, ($r -shr 7), ($r -band 0x7F), ($g -shr 7), ($g -band 0x7F), ($b -shr 7), ($b -band 0x7F))
    [Midi]::Send([byte[]](@(0xF0, 0x47, $dev, 0x53, 0x24, ($data.Length -shr 7), ($data.Length -band 0x7F)) + $data + @(0xF7)))
}
function Clear { foreach ($ch in 0..6) { 0..63 | ForEach-Object { [Midi]::Short(0x90 -bor $ch, $_, 0) } } }

try {
    [void][Midi]::Request([byte[]]@(0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7), 800)
    [Midi]::Send([byte[]]@(0xF0, 0x47, 0x00, 0x53, 0x1C, 0x00, 0x01, 0x01, 0xF7))
    Clear

    Line 0 "HELLIGKEIT"; Line 1 "Kanal 1-7"; Line 2 "10% ... 100%"
    0..6 | ForEach-Object { [Midi]::Short(0x90 -bor $_, $_, 5) }          # unterste Reihe: rot, Kanal 1..7
    0..6 | ForEach-Object { [Midi]::Short(0x90 -bor $_, 8 + $_, 45) }     # 2. Reihe: blau, Kanal 1..7
    Step "Helligkeit: unterste Reihe rot, 2. Reihe blau, von links nach rechts Kanal 1-7 (10 % ... 100 %) ($Hold s)"
    Start-Sleep $Hold
    Clear

    foreach ($dev in 0x7F, 0x00) {
        Line 0 "RGB SYSEX"; Line 1 ("Geraete-ID " + ('{0:X2}' -f $dev)); Line 2 "Verlauf"
        # unterste Reihe: Rot-Verlauf dunkel -> hell, 2. Reihe: Farbverlauf rot -> blau
        0..7 | ForEach-Object { Rgb $dev $_ ([int](($_ + 1) * 255 / 8)) 0 0 }
        0..7 | ForEach-Object { Rgb $dev (8 + $_) ([int](255 - $_ * 255 / 7)) 0 ([int]($_ * 255 / 7)) }
        Step ("RGB-SysEx mit Geraete-ID {0:X2}: unterste Reihe Rot-Verlauf dunkel->hell, 2. Reihe rot->blau ($Hold s)" -f $dev)
        Start-Sleep $Hold
        Clear
    }
}
finally {
    Clear
    0..2 | ForEach-Object { Line $_ "" }
    [Midi]::Send([byte[]]@(0xF0, 0x47, 0x00, 0x53, 0x1C, 0x00, 0x01, 0x00, 0xF7))
    [Midi]::Close()
    Step "aufgeraeumt"
}
