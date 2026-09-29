# APC64 Eingaben mitschneiden: lauscht auf allen APC64-Eingängen und wertet aus.
# Live muss geschlossen sein. Rohdaten landen in logs\.
#   powershell -ExecutionPolicy Bypass -File C:\DEV\APC64\tools\apc_monitor.ps1 [-Seconds 45]
param([int]$Seconds = 45)

Add-Type -Path (Join-Path $PSScriptRoot "MidiTool.cs")
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class ApcMon {
  public delegate void Cb(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2);
  [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr h, uint id, Cb cb, IntPtr inst, uint flags);
  [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInStop(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr h);
  static Cb keep; static List<IntPtr> handles = new List<IntPtr>();
  public static List<string> Events = new List<string>();
  static void On(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2) {
    if (msg != 0x3C3) return;
    uint d = (uint)p1.ToInt64();
    lock (Events) Events.Add(String.Format("{0} {1} {2:X2} {3} {4}", (uint)p2.ToInt64(), inst.ToInt32(), d & 0xFF, (d >> 8) & 0x7F, (d >> 16) & 0x7F));
  }
  public static string Start(uint[] ids) {
    keep = On; string err = "";
    foreach (var id in ids) { IntPtr h; uint r = midiInOpen(out h, id, keep, (IntPtr)id, 0x30000);
      if (r == 0) { midiInStart(h); handles.Add(h); } else err += " in[" + id + "] belegt"; }
    return err;
  }
  public static void Stop() { foreach (var h in handles) { midiInStop(h); midiInClose(h); } handles.Clear(); }
}
"@

$ins = [Midi]::Ins("APC64")
$outId = ([Midi]::Outs("APC64") | Where-Object { $_.Value -eq "APC64" }).Key
$inId  = ($ins | Where-Object { $_.Value -eq "APC64" }).Key

# Identity wie Live, damit das Gerät im selben Zustand ist
if ([Midi]::Open($outId, $inId)) { [void][Midi]::Request([byte[]]@(0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7), 800); [Midi]::Close() }

$err = [ApcMon]::Start([uint32[]]($ins | ForEach-Object { $_.Key }))
if ($err) { "Hinweis:$err" }
Write-Output ("[{0:HH:mm:ss}] Mitschnitt laeuft {1} s: Pads (sanft und fest, auch gedrueckt halten und Druck aendern), Buttons, Touch-Strips (langsam und schnell wischen), Encoder drehen und druecken." -f (Get-Date), $Seconds)
Start-Sleep $Seconds
[ApcMon]::Stop()
Write-Output ("[{0:HH:mm:ss}] Mitschnitt beendet, {1} Nachrichten" -f (Get-Date), [ApcMon]::Events.Count)

$names = @{}; $ins | ForEach-Object { $names[[string]$_.Key] = $_.Value }
$logDir = Join-Path (Split-Path $PSScriptRoot -Parent) "logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$logFile = Join-Path $logDir ("{0:yyyy-MM-dd_HHmmss}_monitor.txt" -f (Get-Date))
$ev = foreach ($e in [ApcMon]::Events) {
    $f = $e.Split(' ')
    [pscustomobject]@{ t = [int]$f[0]; port = $names[$f[1]]; st = [Convert]::ToInt32($f[2], 16); d1 = [int]$f[3]; d2 = [int]$f[4] }
}
$ev | ForEach-Object { "{0,7} ms  {1,-18} {2:X2} {3,3} {4,3}" -f $_.t, $_.port, $_.st, $_.d1, $_.d2 } | Set-Content $logFile -Encoding ascii
"Rohdaten: $logFile"
if (-not $ev) { return }

"`n== Nach Port"
$ev | Group-Object port | ForEach-Object { "  {0}: {1} Nachrichten" -f $_.Name, $_.Count }

"`n== Nach Nachrichtentyp (Status-Byte)"
$types = @{ 0x80 = "Note Off"; 0x90 = "Note On"; 0xA0 = "Poly Aftertouch"; 0xB0 = "CC"; 0xC0 = "Program"; 0xD0 = "Channel Pressure"; 0xE0 = "Pitch Bend" }
$ev | Group-Object { "{0} Kanal {1}" -f $types[$_.st -band 0xF0], (($_.st -band 0x0F) + 1) } | Sort-Object Name | ForEach-Object { "  {0,-28} {1,5}x" -f $_.Name, $_.Count }

"`n== Noten (Note On, Velocity min-max)"
$ev | Where-Object { ($_.st -band 0xF0) -eq 0x90 -and $_.d2 -gt 0 } | Group-Object d1 | Sort-Object { [int]$_.Name } | ForEach-Object {
    $v = $_.Group.d2 | Measure-Object -Minimum -Maximum
    "  Note {0,3}: {1,3}x, Velocity {2}-{3}, Kanal {4}" -f $_.Name, $_.Count, $v.Minimum, $v.Maximum, ((($_.Group | ForEach-Object { ($_.st -band 0x0F) + 1 }) | Sort-Object -Unique) -join ',')
}

$at = $ev | Where-Object { ($_.st -band 0xF0) -in 0xA0, 0xD0 }
if ($at) {
    "`n== Druck (Aftertouch)"
    $at | Group-Object { if (($_.st -band 0xF0) -eq 0xA0) { "Poly, Note " + $_.d1 } else { "Kanal" } } | Select-Object -First 12 | ForEach-Object {
        $v = $_.Group | ForEach-Object { if (($_.st -band 0xF0) -eq 0xA0) { $_.d2 } else { $_.d1 } } | Measure-Object -Minimum -Maximum
        "  {0}: {1}x, Werte {2}-{3}" -f $_.Name, $_.Count, $v.Minimum, $v.Maximum
    }
}

$pb = $ev | Where-Object { ($_.st -band 0xF0) -eq 0xE0 }
if ($pb) {
    "`n== Touch-Strips (Pitch Bend, 14 Bit)"
    $pb | Group-Object { ($_.st -band 0x0F) + 1 } | Sort-Object { [int]$_.Name } | ForEach-Object {
        $vals = $_.Group | ForEach-Object { $_.d1 + ($_.d2 -shl 7) }
        $m = $vals | Measure-Object -Minimum -Maximum
        $uniq = ($vals | Sort-Object -Unique)
        $steps = for ($k = 1; $k -lt $vals.Count; $k++) { [math]::Abs($vals[$k] - $vals[$k - 1]) }
        $minStep = ($steps | Where-Object { $_ -gt 0 } | Measure-Object -Minimum).Minimum
        "  Strip {0}: {1}x, Werte {2}-{3}, {4} verschiedene, kleinster Schritt {5}, LSB genutzt: {6}" -f $_.Name, $_.Count, $m.Minimum, $m.Maximum, $uniq.Count, $minStep, [bool]($_.Group | Where-Object { $_.d1 -ne 0 })
    }
}

$cc = $ev | Where-Object { ($_.st -band 0xF0) -eq 0xB0 }
if ($cc) {
    "`n== CCs"
    $cc | Group-Object d1 | Sort-Object { [int]$_.Name } | ForEach-Object {
        "  CC {0,3}: {1,4}x, Werte: {2}" -f $_.Name, $_.Count, ((($_.Group.d2 | Group-Object | Sort-Object Count -Descending | Select-Object -First 6) | ForEach-Object { "$($_.Name)($($_.Count)x)" }) -join ' ')
    }
}
