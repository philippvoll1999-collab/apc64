# APC64 – MIDI-/SysEx-Protokoll (Notizen)

Stand: 29.09.2026. Quelle: Lives eigenes APC64-Script (dekompiliert in [gluon/AbletonLive12_MIDIRemoteScripts/APC64](https://github.com/gluon/AbletonLive12_MIDIRemoteScripts/tree/main/APC64): `midi.py`, `elements.py`, `display.py`, `touch_strip.py`, `colors.py`). Was nicht als **geprüft** markiert ist, muss am Gerät bestätigt werden.

## Geprüft am Gerät (29.09.2026, `tools/apc_probe.ps1`)

- **Identity:** Antwort auf `F0 7E 7F 06 01 F7` enthält `47 53 00 19` und die Firmware-Kennung `01 02 01 06`, danach die Seriennummer als ASCII (nicht im Repo).
- **Wichtig: Erst Identity, dann Display.** Nach dem Einschalten ignoriert das APC64 alle Display-Befehle (Pads gehen trotzdem), bis ein Programm den Universal Identity Request `F0 7E 7F 06 01 F7` geschickt hat. Lives Script macht das beim Verbinden genauso.
- **Funktioniert sichtbar:** Display mit 3 Zeilen (nach Besitz-Übernahme `1C 01`), Pad-Farben per Velocity, LED-Modi per Kanal (voll, halb, pulsierend, blinkend), Touch-Strip-LEDs mit Stil, Farbe und Wert per Pitch Bend. Aufräumen und Rückgabe des Displays mit `1C 00` klappen.
- **Display:** Alle Buchstaben inkl. Kleinbuchstaben und ASCII-Sonderzeichen werden richtig dargestellt. Zähler mit ca. 30 Updates/s läuft flüssig.
- **Header-Farbe über Note 89** hat auf den Kanälen 1, 2, 7 und 16 **nicht** reagiert. Weg noch unbekannt.
- **Noch offen:** maximale Zeilenlänge, Header-Farbe, Eingaben mitschneiden (Pad-Velocity/Aftertouch, Strip-Auflösung, Encoder), Werte für den Firmware-Modus `19`.

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
