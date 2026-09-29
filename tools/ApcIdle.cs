using System; using System.Runtime.InteropServices; using System.Threading; using System.Text; using System.Collections.Generic;

// Organische Ruhezustands-Animation für das APC64.
// Encoder drücken = nächster Modus, Encoder drehen = Tempo, Shift = Dithering an/aus (Standard: an,
// wirkt nur bei PULS),
// Pads = Reaktion, Stop = beenden.
//
// Helligkeit: jede Farbe hat in der Palette eine helle, dunkle und sehr dunkle Variante
// (z. B. 5/6/7 = #FF0000/#590000/#190000), der MIDI-Kanal 0..6 dimmt zusätzlich auf
// 10/25/50/65/75/90/100 %. Zusammen ergibt das rund 20 Stufen, vor allem im dunklen Bereich.
// Dithering wechselt zwischen zwei Nachbarstufen, versetzt pro Pad.
public static class ApcIdle {
  [StructLayout(LayoutKind.Sequential)] struct HDR { public IntPtr data; public uint len, rec; public IntPtr user; public uint flags; public IntPtr next, res; public uint offset; [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)] public IntPtr[] r2; }
  delegate void Cb(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2);
  [DllImport("winmm.dll")] static extern uint midiOutOpen(out IntPtr h, uint id, IntPtr cb, IntPtr inst, uint flags);
  [DllImport("winmm.dll")] static extern uint midiOutShortMsg(IntPtr h, uint msg);
  [DllImport("winmm.dll")] static extern uint midiOutPrepareHeader(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiOutUnprepareHeader(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiOutLongMsg(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiOutClose(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr h, uint id, Cb cb, IntPtr inst, uint flags);
  [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInStop(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr h);

  const int FULL = 6;
  const int NOTE_ENC_PUSH = 90, NOTE_STOP = 93, NOTE_SHIFT = 120, CC_ENCODER = 90;
  static readonly double[] CHANNEL_LEVEL = { 0.10, 0.25, 0.50, 0.65, 0.75, 0.90, 1.00 };
  static readonly double[] VARIANT_LEVEL = { 1.00, 0.35, 0.10 };   // helle / dunkle / sehr dunkle Palettenvariante
  // Farbtöne mit drei Helligkeitsvarianten in der Palette (Index = helle Variante), nach Farbton sortiert
  static readonly int[] HUES = { 5, 9, 13, 17, 21, 25, 29, 33, 37, 41, 45, 49, 53, 57 };
  static readonly string[] NAMES = { "PULS", "AURORA", "ATMEN", "GLUEHWURM" };
  // Dithering nur, wo wenige Pads leuchten; bei vollflächigen Modi flackert es sichtbar.
  static readonly bool[] MODE_DITHER = { true, false, false, false };
  const double GAMMA = 2.0;
  const double AURORA_LO = 4, AURORA_HI = 13;   // HUES-Index: grün (21) bis pink (57)

  static IntPtr outH, inH; static Cb keep;
  static readonly object lk = new object();
  static readonly List<double[]> touches = new List<double[]>();   // {pad, zeit, farbton}
  static int mode, frame; static double speed = 1.0, clock;
  static volatile bool stop, textDirty = true, dither = true;
  static readonly int[] sentVel = new int[64], sentCh = new int[64];
  static readonly int[] curPick = new int[64], curHue = new int[64];   // für Hysterese
  static readonly double[] smooth = new double[64];                      // geglättete Helligkeit
  const double SMOOTHING = 0.25;                                         // Anteil pro Bild (60 fps)
  static readonly int[] stripVal = new int[8], stripCol = new int[8];
  static DateTime t0;
  static readonly Random rnd = new Random();

  // Helligkeitsstufen: alle Kombinationen (Variante, Kanal) aufsteigend sortiert
  static readonly List<double[]> steps = BuildSteps();
  // Gleichmäßige Leiter: die sehr dunkle Variante nur fürs schwache Ausklingen, darüber
  // durchgehend die helle Variante mit den 7 Kanälen. Ständiges Wechseln zwischen den
  // Varianten sieht wie Zittern aus, weil sie nicht exakt gleich getönt sind.
  static List<double[]> BuildSteps() {
    var l = new List<double[]>();
    for (int c = 0; c < 6; c++) l.Add(new double[] { VARIANT_LEVEL[2] * CHANNEL_LEVEL[c], 2, c });
    for (int c = 0; c < 7; c++) l.Add(new double[] { VARIANT_LEVEL[0] * CHANNEL_LEVEL[c], 0, c });
    return l;
  }

  static double Now() { return (DateTime.Now - t0).TotalSeconds; }
  static void Short(int st, int d1, int d2) { midiOutShortMsg(outH, (uint)(st | ((d1 & 0x7F) << 8) | ((d2 & 0x7F) << 16))); }

  static void SysEx(byte[] msg) {
    int sz = Marshal.SizeOf(typeof(HDR));
    IntPtr buf = Marshal.AllocHGlobal(msg.Length); Marshal.Copy(msg, 0, buf, msg.Length);
    var h = new HDR { data = buf, len = (uint)msg.Length, rec = (uint)msg.Length, r2 = new IntPtr[8] };
    IntPtr p = Marshal.AllocHGlobal(sz); Marshal.StructureToPtr(h, p, false);
    midiOutPrepareHeader(outH, p, (uint)sz); midiOutLongMsg(outH, p, (uint)sz);
    for (int i = 0; i < 100 && midiOutUnprepareHeader(outH, p, (uint)sz) == 65; i++) Thread.Sleep(1);
    Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(buf);
  }
  static void Apc(int id, params byte[] payload) {
    var m = new List<byte> { 0xF0, 0x47, 0x00, 0x53, (byte)id, (byte)(payload.Length >> 7), (byte)(payload.Length & 0x7F) };
    m.AddRange(payload); m.Add(0xF7); SysEx(m.ToArray());
  }
  static void Line(int n, string text) { var p = new List<byte> { (byte)n }; p.AddRange(Encoding.ASCII.GetBytes(text)); p.Add(0); Apc(0x10, p.ToArray()); }

  static void OnInput(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2) {
    if (msg != 0x3C3) return;
    uint d = (uint)p1.ToInt64(); int st = (int)(d & 0xFF), d1 = (int)((d >> 8) & 0x7F), d2 = (int)((d >> 16) & 0x7F);
    lock (lk) {
      if (st == 0x96 && d2 > 0 && d1 < 64) touches.Add(new double[] { d1, Now(), rnd.Next(HUES.Length) });
      else if (st == 0x90 && d2 > 0 && d1 == NOTE_ENC_PUSH) { mode = (mode + 1) % NAMES.Length; touches.Clear(); textDirty = true; }
      else if (st == 0x90 && d2 > 0 && d1 == NOTE_SHIFT) { dither = !dither; textDirty = true; }
      else if (st == 0x90 && d2 > 0 && d1 == NOTE_STOP) stop = true;
      else if (st == 0xB0 && d1 == CC_ENCODER) { speed = Math.Max(0.25, Math.Min(4, speed + (d2 < 64 ? 0.25 : -0.25))); textDirty = true; }
    }
  }

  // ---------- weiches Rauschen (Value Noise, 3D) ----------
  static double Hash(int x, int y, int z) {
    unchecked { int n = x * 374761393 + y * 668265263 + z * 1442695041; n = (n ^ (n >> 13)) * 1274126177; n ^= n >> 16; return (n & 0x7FFFFFFF) / (double)0x7FFFFFFF; }
  }
  static double Smooth(double t) { return t * t * (3 - 2 * t); }
  static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
  static double Noise(double x, double y, double z) {
    int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
    double xf = Smooth(x - xi), yf = Smooth(y - yi), zf = Smooth(z - zi);
    double a = Lerp(Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), xf), Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), xf), yf);
    double b = Lerp(Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), xf), Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), xf), yf);
    return Lerp(a, b, zf);
  }
  static double Fbm(double x, double y, double z) { return Noise(x, y, z) * 0.65 + Noise(x * 2.1 + 7, y * 2.1 + 3, z * 1.7) * 0.35; }
  static double Glow(double dist, double radius) { return Math.Exp(-(dist * dist) / (2 * radius * radius)); }
  static int Hue(double h) { int n = (int)Math.Floor(h) % HUES.Length; return n < 0 ? n + HUES.Length : n; }
  static double Dist(int p, double x, double y) { return Math.Sqrt(Math.Pow(p % 8 - x, 2) + Math.Pow(p / 8 - y, 2)); }

  // ---------- Modi ----------
  static readonly List<double[]> drops = new List<double[]>();   // {x, y, geburt, farbton}
  static double nextDrop;
  static readonly double[][] flies = new double[6][];           // {x, y, farbton}

  // hue: kontinuierlicher Index in HUES (wird mit Hysterese gerundet), lvl: Helligkeit 0..1
  static void Render(double t, double now, double[] hue, double[] lvl) {
    int m; List<double[]> tc;
    lock (lk) { m = mode; touches.RemoveAll(x => now - x[1] > 2.5); tc = new List<double[]>(touches); }
    for (int p = 0; p < 64; p++) { hue[p] = 0; lvl[p] = 0; }

    if (m == 0) {                                   // PULS: Tropfen an zufälligen Stellen, weiche Ringe
      if (t >= nextDrop) {
        drops.Add(new double[] { rnd.NextDouble() * 7, rnd.NextDouble() * 7, t, rnd.Next(HUES.Length) });
        nextDrop = t + 0.5 + rnd.NextDouble() * 1.4;
      }
      drops.RemoveAll(d => t - d[2] > 4 || t < d[2]);
      foreach (var d in drops) {
        double age = t - d[2], r = age * 2.6, fade = Math.Pow(1 - age / 4, 1.5);
        for (int p = 0; p < 64; p++) {
          double dist = Dist(p, d[0], d[1]);
          double v = Glow(dist - r, 0.8) * fade + Glow(dist, 0.6) * Math.Max(0, 1 - age * 2);
          if (v > lvl[p]) { lvl[p] = v; hue[p] = (int)d[3]; }
        }
      }
    } else if (m == 1) {                            // AURORA: wabernde Farb- und Helligkeitswolken
      // Polarlicht: Farbverlauf grün (unten) -> türkis -> cyan -> blau -> violett -> magenta/pink (oben),
      // senkrechte Vorhänge, die langsam seitlich wandern.
      for (int p = 0; p < 64; p++) {
        double x = p % 8, y = p / 8;
        double curtain = Fbm(x * 0.33 + t * 0.07, y * 0.08 + t * 0.02, t * 0.10);
        double body = 0.55 + 0.45 * Noise(x * 0.15 + 20, y * 0.25, t * 0.05);
        double b = Math.Max(0, curtain - 0.22) / 0.78 * body;
        double drift = (Noise(x * 0.1 + 40, 3, t * 0.04) - 0.5) * 1.6;
        double h = AURORA_LO + Math.Pow(y / 7.0, 1.35) * (AURORA_HI - AURORA_LO) + drift;
        hue[p] = Math.Max(AURORA_LO, Math.Min(AURORA_HI, h));   // nicht über Pink hinaus in Rot laufen
        lvl[p] = Math.Pow(b, 1.15);
      }
    } else if (m == 2) {                            // ATMEN: jedes Pad atmet leicht versetzt, wie Glut
      for (int p = 0; p < 64; p++) {
        double x = p % 8, y = p / 8;
        double phase = t * 0.22 + Noise(x * 0.35, y * 0.35, 5) * 1.3;
        double breathe = 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase);
        hue[p] = Noise(x * 0.25, y * 0.25, 1) * 3.2;             // feste warme Töne: rot bis gelb
        lvl[p] = 0.08 + 0.92 * Math.Pow(breathe, 1.8);
      }
    } else {                                        // GLUEHWURM: schwebende Lichtpunkte
      for (int i = 0; i < flies.Length; i++) {
        if (flies[i] == null) flies[i] = new double[] { 0, 0, (i * 5) % HUES.Length };
        flies[i][0] = 3.5 + 4.2 * (Noise(i * 13.1, 0.5, t * 0.12) - 0.5) * 2;
        flies[i][1] = 3.5 + 4.2 * (Noise(i * 7.7, 9.5, t * 0.12) - 0.5) * 2;
        double pulse = 0.55 + 0.45 * Math.Sin(t * 1.3 + i * 1.7);
        for (int p = 0; p < 64; p++) {
          double v = Glow(Dist(p, flies[i][0], flies[i][1]), 0.9) * pulse;
          if (v > lvl[p]) { lvl[p] = v; hue[p] = (int)flies[i][2]; }
        }
      }
    }

    foreach (var x in tc) {                         // Pads: weicher Ring in zufälliger Farbe
      int pad = (int)x[0]; double age = now - x[1], fade = Math.Pow(1 - age / 2.5, 2);
      for (int p = 0; p < 64; p++) {
        double dist = Dist(p, pad % 8, pad / 8);
        double v = (Glow(dist - age * 4.5, 0.9) + Glow(dist, 0.7) * Math.Max(0, 1 - age * 3)) * fade;
        if (v > lvl[p]) { lvl[p] = Math.Min(1, v); hue[p] = (int)x[2]; }
      }
    }
  }

  // Helligkeit 0..1 (mit Gamma) auf eine Stufe (Variante, Kanal) abbilden, mit Dithering dazwischen.
  static void SetPad(int p, double hueF, double v) {
    // Farbton mit Hysterese runden: erst wechseln, wenn der Wert die Grenze deutlich überschreitet
    int n = HUES.Length;
    int ideal = Hue(Math.Floor(hueF + 0.5));
    double diff = hueF - curHue[p];
    diff -= Math.Round(diff / n) * n;
    int hueIndex = Math.Abs(diff) < 0.75 ? curHue[p] : ideal;
    curHue[p] = hueIndex;

    double target = Math.Pow(Math.Max(0, Math.Min(1, v)), GAMMA);
    int vel = 0, ch = FULL;
    if (target >= steps[0][0] * 0.5) {
      int hi = 0; while (hi < steps.Count - 1 && steps[hi][0] < target) hi++;
      int pick = hi;
      if (hi > 0) {
        double lo = steps[hi - 1][0], up = steps[hi][0], frac = Math.Min(1, (target - lo) / (up - lo));
        if (dither && MODE_DITHER[mode]) pick = frac > ((frame * 0.618034 + p * 0.381966) % 1.0) ? hi : hi - 1;
        else {
          pick = frac < 0.5 ? hi - 1 : hi;
          // Helligkeits-Hysterese: bei Werten nahe der Grenze die bisherige Stufe behalten
          int cur = curPick[p];
          if ((cur == hi - 1 || cur == hi) && frac > 0.15 && frac < 0.85) pick = cur;
        }
      }
      curPick[p] = pick;
      vel = HUES[hueIndex] + (int)steps[pick][1];
      ch = (int)steps[pick][2];
    } else curPick[p] = 0;
    if (sentVel[p] == vel && sentCh[p] == ch) return;
    sentVel[p] = vel; sentCh[p] = ch;
    Short(0x90 | ch, p, vel);
  }

  // Strip = Energie der Pad-Spalte, in der Farbe des hellsten Pads der Spalte.
  static void SetStrips(double[] hue, double[] lvl) {
    for (int s = 0; s < 8; s++) {
      double sum = 0, best = -1; int bh = 0;
      for (int r = 0; r < 8; r++) { int p = r * 8 + s; sum += lvl[p]; if (lvl[p] > best) { best = lvl[p]; bh = curHue[p]; } }
      int color = HUES[bh];
      if (stripCol[s] != color) { stripCol[s] = color; Short(0xB0, 112 + s, color); }
      int val = (int)(Math.Min(1, sum / 3.0) * 16383);
      if (Math.Abs(stripVal[s] - val) < 64) continue;
      stripVal[s] = val; Short(0xE0 | s, val & 0x7F, val >> 7);
    }
  }

  public static string Run(uint outId, uint inId, double minutes) {
    if (midiOutOpen(out outH, outId, IntPtr.Zero, IntPtr.Zero, 0) != 0) return "Ausgang belegt (Live offen?)";
    keep = OnInput;
    if (midiInOpen(out inH, inId, keep, IntPtr.Zero, 0x30000) != 0) { midiOutClose(outH); return "Eingang belegt (Live offen?)"; }
    midiInStart(inH);
    t0 = DateTime.Now; stop = false; mode = 0; textDirty = true; clock = 0; nextDrop = 0; drops.Clear();
    for (int p = 0; p < 64; p++) { sentVel[p] = -1; sentCh[p] = -1; curPick[p] = 0; curHue[p] = 0; smooth[p] = 0; }
    for (int s = 0; s < 8; s++) { stripVal[s] = -999; stripCol[s] = -1; }
    try {
      SysEx(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 });   // Identity: gibt das Display frei
      Thread.Sleep(300);
      Apc(0x1C, 1);
      for (int s = 0; s < 8; s++) Short(0xB0, 104 + s, 1);
      var hue = new double[64]; var lvl = new double[64];
      double last = 0;
      while (!stop && (minutes <= 0 || Now() < minutes * 60)) {
        double now = Now(); double spd; lock (lk) spd = speed;
        clock += (now - last) * spd; last = now;
        if (textDirty) {
          textDirty = false; int m; lock (lk) m = mode;
          Line(0, dither && MODE_DITHER[m] ? "APC64 DITHER" : "APC64"); Line(1, NAMES[m]); Line(2, string.Format("Tempo {0:0.00}x", spd));
        }
        Render(clock, now, hue, lvl);
        for (int p = 0; p < 64; p++) {
          smooth[p] += (lvl[p] - smooth[p]) * SMOOTHING;   // kurze Schwankungen wegfiltern
          SetPad(p, hue[p], smooth[p]);
        }
        SetStrips(hue, lvl);
        frame++;
        Thread.Sleep(15);
      }
      return stop ? "Mit Stop beendet" : "Zeit abgelaufen";
    } finally {
      for (int p = 0; p < 64; p++) Short(0x90 | FULL, p, 0);
      for (int s = 0; s < 8; s++) { Short(0xB0, 104 + s, 0); Short(0xE0 | s, 0, 0); }
      Line(0, ""); Line(1, ""); Line(2, ""); Apc(0x1C, 0);
      midiInStop(inH); midiInClose(inH); midiOutClose(outH);
    }
  }
}
