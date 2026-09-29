# APC64 – MIDI-/SysEx-Protokoll (Notizen)

Stand: 29.09.2026. Quelle: Lives eigenes APC64-Script (dekompiliert in [gluon/AbletonLive12_MIDIRemoteScripts/APC64](https://github.com/gluon/AbletonLive12_MIDIRemoteScripts/tree/main/APC64): `midi.py`, `elements.py`, `display.py`, `touch_strip.py`, `colors.py`). Was nicht als **geprüft** markiert ist, muss am Gerät bestätigt werden.

## Geprüft am Gerät (29.09.2026, `tools/apc_probe.ps1`)

- **Identity:** Antwort auf `F0 7E 7F 06 01 F7` enthält `47 53 00 19` und die Firmware-Kennung `01 02 01 06`, danach die Seriennummer als ASCII (nicht im Repo).
- **Wichtig: Erst Identity, dann Display.** Nach dem Einschalten ignoriert das APC64 alle Display-Befehle (Pads gehen trotzdem), bis ein Programm den Universal Identity Request `F0 7E 7F 06 01 F7` geschickt hat. Lives Script macht das beim Verbinden genauso.
- **Funktioniert sichtbar:** Display mit 3 Zeilen (nach Besitz-Übernahme `1C 01`), Pad-Farben per Velocity, LED-Modi per Kanal (voll, halb, pulsierend, blinkend), Touch-Strip-LEDs mit Stil, Farbe und Wert per Pitch Bend. Aufräumen und Rückgabe des Displays mit `1C 00` klappen.
- **Display:** Alle Buchstaben inkl. Kleinbuchstaben und ASCII-Sonderzeichen werden richtig dargestellt. Zähler mit ca. 30 Updates/s läuft flüssig.
- **Zeilenbreite:** Proportionale Schrift, Text wird **zentriert** und links und rechts abgeschnitten. Bei 20 Zeichen sichtbar: Zeile 1 `E`–`P` (12 Großbuchstaben), Zeile 2 `c`–`q` (15 Kleinbuchstaben), Zeile 3 `4`–`F` (12 Ziffern/Großbuchstaben). Faustregel: ca. 12 Zeichen, bei Kleinbuchstaben etwas mehr.
- **Header-Farbe über Note 89** hat auf den Kanälen 1, 2, 7 und 16 **nicht** reagiert. **Achtung:** Dieser Test (Note On 89 auf Kanal 1, 2, 7 und 16 mit Note Off) hat das Display danach gesperrt, bis das APC64 aus- und wieder eingeschaltet wurde. Pads funktionierten weiter. Welche Nachricht genau schuld ist, ist offen. Bis dahin Note 89 außer auf Kanal 7 nicht senden.
- **Wiederholbarkeit:** Ohne den Header-Test funktioniert das Display auch bei mehreren Läufen hintereinander, jeweils mit Identity-Abfrage vorweg.
- **Eingaben** (`tools/apc_monitor.ps1`, 45 s, 828 Nachrichten, nach Identity-Abfrage):
  - **Port „APC64“** (Script-Port):
    - Pads schicken Note **0–63 auf Kanal 7** mit **fester Velocity 127**, Note Off beim Loslassen.
    - Buttons und Strip-Berührungen schicken Noten 64–126 auf **Kanal 1**, Velocity 127.
    - Encoder: **CC 90**, `1` = rechts, `127` = links.
    - Touch-Strips: **Pitch Bend auf Kanal 1–8** (Strip 1 nicht getestet), Bereich 0–16383, **kleinster Schritt 64**, also effektiv **256 Stufen**.
  - **Port „MIDIIN2 (APC64)“** gleichzeitig: dieselben Pad-Anschläge als **Musiknoten 24–83 auf Kanal 1** mit **echter Velocity** (12–127) und **polyphonem Aftertouch** (0–127, `A0`).
  - Für ein Script heißt das: Pad-Position über den Script-Port, Anschlagstärke und Druck über „MIDIIN2 (APC64)“. Beide Ports müsste das Script abhören, oder der Modus lässt sich umstellen (offen).
- **Noch offen:** Header-Farbe (welche Nachricht sperrt das Display?), wie die Pad-Noten 0–63 den Musiknoten 24–83 zugeordnet sind, Werte für den Firmware-Modus `19`.

- **Helligkeit per MIDI-Kanal (geprüft):** wie beim APC mini mk2, Kanal 1–7 = 10 / 25 / 50 / 65 / 75 / 90 / 100 %. Lives Script nutzt nur Kanal 1 („halb“, eigentlich 10 %) und 7. Laut mini-mk2-Doku zusätzlich Kanal 8–11 = Pulsieren (1/16 … 1/2), 12–16 = Blinken (1/24 … 1/2).
- **Farbpalette:** Die 128 Velocity-Farben entsprechen vermutlich der Tabelle im [APC mini mk2 Communication Protocol](https://cdn.inmusicbrands.com/akai/attachments/APC%20mini%20mk2%20-%20Communication%20Protocol%20-%20v1.0.pdf) (z. B. 5 = #FF0000, 13 = #FFFF00, 21 = #00FF00, 45 = #0000FF).
- **Palettenvarianten:** Viele Farben gibt es in drei Helligkeiten (z. B. 5/6/7 = #FF0000/#590000/#190000). Die dunkle Variante ist farbtreu (35 % der hellen), die **sehr dunkle ist bei manchen Farben anders getönt** (Orange 11 = #271B00, Lime 19 = #142B00). Wechsel dorthin sieht wie Farbflackern aus.
- **Idle-Animation** (`tools/apc_idle.ps1`, `ApcIdle.cs`): Helligkeit aus helle/dunkle Variante x 7 Kanäle mit Gamma, Glättung und Hysterese. Dithering flackert, wenn viele Pads leuchten, und ist deshalb aus.
- **RGB per SysEx `24`** (aus der mini-mk2-Doku): `F0 47 <dev> 53 24 <len_hi> <len_lo> {<start> <end> <R_msb> <R_lsb> <G_msb> <G_lsb> <B_msb> <B_lsb>}… F7`, 8 Bit pro Farbe. **Geprüft: funktioniert beim APC64 nicht**, weder mit Geräte-ID 7F noch mit 00 (Test `tools/apc_rgbtest.ps1`). Es bleiben die Palettenfarben plus 7 Helligkeitsstufen.

## SysEx

```
F0 47 00 53  <msg_id>  <len_hi> <len_lo>  <payload …>  F7
   Akai  APC64
```
`len` = Anzahl der Payload-Bytes, 14 Bit in zwei 7-Bit-Bytes.

| msg_id | Name | Payload |
|---|---|---|
| `10` (16) | Display-Zeile | `<zeile 0–2> <ASCII …> 00` |
| `1C` (28) | Display-Besitz | `01` = Script schreibt ins Display, `00` = Gerät zeigt wieder selbst an |
| `19` (25) | Firmware-Modus | `<modus>` (Werte unbekannt) |
| `1B` (27) | Track-Typ | `<typ>` |
| `20`–`22` | Render-to-Clip Start/Daten/Ende | |

- Das Display hat **3 Zeilen**. Live schreibt pro Zeile höchstens 8 Zeichen, zentriert.
- Identity-Antwort enthält `47 53 00 19` (Universal Identity Request `F0 7E 7F 06 01 F7`).
- Die Header-Farbe über dem Display ist die LED von Note **89** („Track_Color_Element“).

## Pads und Buttons (Noten)

- **Pads 8×8: Noten 0–63**, die unterste Reihe ist 0–7, die oberste 56–63 (`flip_rows=True`).
- **LED-Farbe = Velocity** einer Note On auf dieselbe Note, der **Kanal bestimmt das Verhalten**:

| Kanal (0-basiert) | Verhalten |
|---|---|
| 0 | halbe Helligkeit |
| 6 | volle Helligkeit |
| 10 | pulsieren |
| 14 | blinken |

- Farbpalette (Velocity): `0` aus, `1` grau, `3` weiß, `5` rot, `9` amber, `13` gelb, `21` grün, `45` blau … Vermutlich dieselbe 128er-Palette wie beim APC mini mk2.

| Noten | Buttons |
|---|---|
| 64–71 | Track-State (unter dem Grid) |
| 72 | Tempo |
| 73–76 | Clear, Duplicate, Quantize, Fixed Length |
| 77 | Undo |
| 82–89 | Touch-Strips berührt (Note On/Off) |
| 89 | auch: Header-Farbe über dem Display |
| 90 | Encoder gedrückt |
| 91–93 | Play, Record, Stop |
| 94–97 | Hoch, Runter, Links, Rechts |
| 100–107 | Track Select |
| 108–111 | Rec Arm, Mute, Solo, Clip Stop |
| 112–119 | Scene Launch |
| 120 | Shift |
| 121 | Device |
| 122–126 | Volume, Pan, Send, Channel Strip, Off |

## Touch-Strips (8 Stück)

- Position: **Pitch Bend** auf Kanal 0–7 (Strip 1–8), 14 Bit.
- Berührung: Note 82–89 an/aus.
- LEDs: Das Script schickt den Wert per Pitch Bend zurück, das Gerät zeigt ihn an.
- **LED-Stil:** CC `104 + Kanal`: `0` aus, `1` normal, `3` bipolar (+1 = Automation aktiv).
- **LED-Farbe:** CC `112 + Kanal`, Wert = Farbindex.

## Encoder

- CC 90, relativ im Zweierkomplement. Drücken = Note 90.

## Ports

Live nutzt den Port **„APC64“** für das Script (Noten, CCs, SysEx). Ein zweiter Ausgang ist für MIDI-Clock (Sync) vorgesehen.
