# APC64 Display-Test 2: Zeilenlänge per Lineal.
#   powershell -ExecutionPolicy Bypass -File C:\DEV\APC64\tools\apc_display2.ps1
param([string]$Port = "APC64", [int]$Hold = 10)

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
function Step($t) { Write-Output ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $t) }

try {
    # Ohne Identity-Abfrage ignoriert das APC64 nach dem Einschalten alle Display-Befehle.
    [void][Midi]::Request([byte[]]@(0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7), 800)
    [Midi]::Send((Sx 0x1C ([byte[]]@(1))))

    Lines "ABCDEFGHIJKLMNOPQRST" "abcdefghijklmnopqrst" "0123456789ABCDEFGHIJ"
    Step "Lineal: Welches Zeichen ist pro Zeile das letzte sichtbare? ($Hold s)"
    Start-Sleep $Hold

    # Der fruehere Header-Test (Note 89 auf Kanal 1/2/7/16) sperrt das Display bis zum
    # Aus- und Einschalten des APC64 und ist deshalb entfernt.
}
finally {
    Lines "" "" ""
    [Midi]::Send((Sx 0x1C ([byte[]]@(0))))
    Step "Display an das Geraet zurueckgegeben"
    [Midi]::Close()
}
