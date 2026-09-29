# APC64 Display-Test: Zeilenlänge, Zeichensatz, Update-Rate, Header-Farbe.
# Live muss geschlossen sein. Am Ende bekommt das Gerät das Display zurück.
#   powershell -ExecutionPolicy Bypass -File C:\DEV\APC64\tools\apc_display.ps1
param([string]$Port = "APC64", [int]$Hold = 6)

Add-Type -Path (Join-Path $PSScriptRoot "MidiTool.cs")
$outId = ([Midi]::Outs("APC64") | Where-Object { $_.Value -eq $Port } | Select-Object -First 1).Key
$inId  = ([Midi]::Ins("APC64")  | Where-Object { $_.Value -eq $Port } | Select-Object -First 1).Key
if ($null -eq $outId -or -not [Midi]::Open($outId, $inId)) { "Port '$Port' nicht verfuegbar (Live offen?)."; exit 1 }

function Sx([int]$id, [byte[]]$payload) {
    $n = $payload.Length
    [byte[]](@(0xF0, 0x47, 0x00, 0x53, $id, ($n -shr 7), ($n -band 0x7F)) + $payload + @(0xF7))
}
function Line([int]$i, [string]$t) { [Midi]::Send((Sx 0x10 ([byte[]](@($i) + [Text.Encoding]::ASCII.GetBytes($t) + @(0))))) }
function Lines($a, $b, $c) { Line 0 $a; Line 1 $b; Line 2 $c }
function Header([int]$color) { [Midi]::Short(0x96, 89, $color) }
function Step($t) { Write-Output ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $t) }

try {
    [Midi]::Send((Sx 0x1C ([byte[]]@(1))))
    Header 3

    Lines "12345678" "123456789012" "12345678901234567890"
    Step "Laenge: Zeile 1 = 8, Zeile 2 = 12, Zeile 3 = 20 Ziffern. Wie viele sind sichtbar? ($Hold s)"
    Start-Sleep $Hold

    Lines "abcdefgh" "Hallo Welt" "Proton 72%"
    Step "Kleinbuchstaben und gemischt: 'abcdefgh' / 'Hallo Welt' / 'Proton 72%' ($Hold s)"
    Start-Sleep $Hold

    Lines "!?#%&/()" "+-*=<>[]" "@_:;.,'~"
    Step "Sonderzeichen: '!?#%&/()' / '+-*=<>[]' / '@_:;.,'~' ($Hold s)"
    Start-Sleep $Hold

    Lines "" "nur Mitte" ""
    Step "Leere Zeilen: nur Zeile 2 'nur Mitte' ($Hold s)"
    Start-Sleep $Hold

    Lines "HEADER" "FARBEN" ""
    Step "Header-Farbe wechselt jede Sekunde: rot, amber, gelb, gruen, cyan, blau, lila, pink, weiss"
    foreach ($c in 5, 9, 13, 21, 37, 45, 49, 53, 3) {
        Line 2 ("Farbe " + $c); Header $c; Start-Sleep 1
    }

    Lines "TEMPO" "" "Zaehler"
    Step "Update-Rate: Zaehler in Zeile 2 zaehlt 5 s so schnell wie moeglich (alle 20 ms)"
    $t0 = Get-Date; $n = 0
    while (((Get-Date) - $t0).TotalSeconds -lt 5) { Line 1 ("{0:D5}" -f $n); $n++; Start-Sleep -Milliseconds 20 }
    Step ("  gesendet: $n Updates in 5 s. Lief der Zaehler fluessig oder sprang er?")
    Start-Sleep 2

    Step "Laufschrift in Zeile 2 (8 Zeichen breit, 5 s)"
    $text = "        GRP PROTON  06 BR 1/16  72%  LATCH        "
    $t0 = Get-Date; $i = 0
    while (((Get-Date) - $t0).TotalSeconds -lt 5) { Lines "SCROLL" $text.Substring($i % ($text.Length - 8), 8) ""; $i++; Start-Sleep -Milliseconds 150 }
}
finally {
    Lines "" "" ""
    Header 0
    [Midi]::Send((Sx 0x1C ([byte[]]@(0))))
    Step "Display an das Geraet zurueckgegeben"
    [Midi]::Close()
}
