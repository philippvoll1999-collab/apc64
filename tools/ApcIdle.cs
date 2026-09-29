using System; using System.Runtime.InteropServices; using System.Threading; using System.Text; using System.Collections.Generic;

// Ruhezustands-Animation für das APC64 im Stil der MikroFX-Idle-Modi.
// Shift = nächster Modus, Encoder = Tempo, Pads = Reaktion, Stop = beenden.
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

  const int FULL = 6, HALF = 0;               // LED-Kanäle: volle / halbe Helligkeit
  const int WHITE = 3;
  const int NOTE_SHIFT = 120, NOTE_STOP = 93, CC_ENCODER = 90;
  static readonly int[] RAINBOW = { 5, 9, 13, 17, 21, 29, 33, 37, 41, 45, 49, 53, 57 };
  static readonly int[] ROW_COLORS = { 9, 49, 37, 21 };   // orange, lila, cyan, grün (je 2 Reihen)
  static readonly string[] NAMES = { "PULS", "WELLE", "ATMEN", "LAUFLICHT" };

  static IntPtr outH, inH; static Cb keep;
  static readonly object lk = new object();
  static readonly List<double[]> touches = new List<double[]>();   // {pad, zeit, farbe}
  static int mode, nextColor; static double speed = 1.5, clock;
  static volatile bool stop, textDirty = true;
  static readonly int[] sentColor = new int[64], sentCh = new int[64];
  static readonly int[] stripVal = new int[8], stripCol = new int[8];
  static DateTime t0;

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

  static void Line(int n, string text) {
    var p = new List<byte> { (byte)n }; p.AddRange(Encoding.ASCII.GetBytes(text)); p.Add(0); Apc(0x10, p.ToArray());
  }

  static void OnInput(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2) {
    if (msg != 0x3C3) return;
    uint d = (uint)p1.ToInt64(); int st = (int)(d & 0xFF), d1 = (int)((d >> 8) & 0x7F), d2 = (int)((d >> 16) & 0x7F);
    lock (lk) {
      if (st == 0x96 && d2 > 0 && d1 < 64) {                     // Pad
        nextColor = (nextColor + 4) % RAINBOW.Length;
        touches.Add(new double[] { d1, Now(), RAINBOW[nextColor] });
      } else if (st == 0x90 && d2 > 0 && d1 == NOTE_SHIFT) {     // Shift: nächster Modus
        mode = (mode + 1) % NAMES.Length; touches.Clear(); textDirty = true;
      } else if (st == 0x90 && d2 > 0 && d1 == NOTE_STOP) {      // Stop: beenden
        stop = true;
      } else if (st == 0xB0 && d1 == CC_ENCODER) {               // Encoder: Tempo
        speed = Math.Max(0.25, Math.Min(4, speed + (d2 < 64 ? 0.25 : -0.25))); textDirty = true;
      }
    }
  }

  static double Crest(double dist, double width) {
    dist = Math.Abs(dist); return dist >= width ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * dist / width);
  }
  static double Breath(double phase, double lo) {
    double v = 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase); return lo + (1 - lo) * Math.Pow(v, 1.6);
  }
  static int Rainbow(double step) { int n = (int)Math.Floor(step) % RAINBOW.Length; return RAINBOW[n < 0 ? n + RAINBOW.Length : n]; }
  static double Dist(int a, int b) { return Math.Sqrt(Math.Pow(a / 8 - b / 8, 2) + Math.Pow(a % 8 - b % 8, 2)); }
  static int Snake(int step) { step = ((step % 64) + 64) % 64; int row = step / 8, col = step % 8; if (row % 2 == 1) col = 7 - col; return row * 8 + col; }
  static int SnakeIndex(int pad) { int row = pad / 8, col = pad % 8; if (row % 2 == 1) col = 7 - col; return row * 8 + col; }
  static double Corner(int pad, int corner) {
    int r = pad / 8, c = pad % 8;
    switch (corner) { case 1: return 14 - r - c; case 2: return r + 7 - c; case 3: return 7 - r + c; default: return r + c; }
  }

  // Ein Bild berechnen: Farbe und Helligkeit (0..1) für 64 Pads, Wert (0..1) und Farbe für 8 Strips.
  static void Render(double t, double now, int[] color, double[] level, double[] strip, int[] scol) {
    int m; List<double[]> tc;
    lock (lk) { m = mode; touches.RemoveAll(x => now - x[1] > (mode == 3 ? 5 : 1.2)); tc = new List<double[]>(touches); }
    for (int p = 0; p < 64; p++) { color[p] = 0; level[p] = 0; }
    if (m == 0) {                                   // PULS: Band von Ecke zu Ecke, jede Welle neue Farbe
      int n = (int)Math.Floor(t / 2.4); double front = (t - n * 2.4) / 1.4 * 20 - 3;
      int c = RAINBOW[(n * 5) % RAINBOW.Length];
      for (int p = 0; p < 64; p++) { color[p] = c; level[p] = Crest(Corner(p, n % 4) - front, 3); }
      bool right = n % 4 == 0 || n % 4 == 3;
      for (int s = 0; s < 8; s++) { scol[s] = c; strip[s] = Crest((right ? s : 7 - s) * 2 - front, 4); }
    } else if (m == 1) {                            // WELLE: Regenbogen mit rollendem Kamm
      double hue = t * 0.5, front = (t * 3.5) % 24 - 5;
      for (int p = 0; p < 64; p++) { int d = p / 8 + p % 8; color[p] = Rainbow(hue + d * 0.5); level[p] = 0.3 + 0.7 * Crest(d - front, 3.5); }
      for (int s = 0; s < 8; s++) { scol[s] = Rainbow(hue + s); strip[s] = 0.5 + 0.5 * Math.Sin(t * 2 + s * 0.8); }
    } else if (m == 2) {                            // ATMEN: Reihen pulsieren in Farben
      for (int p = 0; p < 64; p++) { int r = p / 8; color[p] = ROW_COLORS[r / 2]; level[p] = Breath(t / 5 - r * 0.07, 0.2); }
      for (int s = 0; s < 8; s++) { scol[s] = ROW_COLORS[s / 2]; strip[s] = Breath(t / 5 - s * 0.07, 0.1); }
    } else {                                        // LAUFLICHT: Schlange mit Schweif, Strips als Knight Rider
      double head = t * 12;
      DrawRunner(color, level, head, Rainbow(head / 64 * 3));
      double pos = t * 6 % 14; if (pos > 7) pos = 14 - pos;
      for (int s = 0; s < 8; s++) { scol[s] = 5; strip[s] = Crest(s - pos, 2); }
    }
    foreach (var x in tc) {                         // Reaktionen auf Pads
      int pad = (int)x[0]; double age = now - x[1]; int c = (int)x[2];
      if (m == 3) { DrawRunner(color, level, SnakeIndex(pad) + age * 16, c); continue; }
      double fade = 1 - age / 1.2;
      for (int p = 0; p < 64; p++) {
        double v = Crest(Dist(p, pad) - age * 8, 1.6) * fade;
        if (v > 0.35 && v > level[p]) { color[p] = m == 0 ? c : WHITE; level[p] = v; }
      }
    }
  }

  static void DrawRunner(int[] color, double[] level, double head, int c) {
    for (int step = 0; step < 64; step++) {
      double behind = ((head - step) % 64 + 64) % 64;
      double v = behind < 1 ? 1 : behind < 7 ? 1 - (behind - 1) / 6 : 0;
      int p = Snake(step);
      if (v > level[p]) { level[p] = v; color[p] = c; }
    }
  }

  static void SetPad(int p, int color, double v) {
    int ch = -1, c = color;
    if (v >= 0.6) ch = FULL; else if (v >= 0.22) ch = HALF;
    if (ch < 0) { c = 0; ch = FULL; }
    if (sentColor[p] == c && sentCh[p] == ch) return;
    sentColor[p] = c; sentCh[p] = ch;
    Short(0x90 | ch, p, c);
  }

  static void SetStrip(int s, int color, double v) {
    if (stripCol[s] != color) { stripCol[s] = color; Short(0xB0, 112 + s, color); }
    int val = (int)(Math.Max(0, Math.Min(1, v)) * 16383);
    if (Math.Abs(stripVal[s] - val) < 64) return;
    stripVal[s] = val; Short(0xE0 | s, val & 0x7F, val >> 7);
  }

  public static string Run(uint outId, uint inId, double minutes) {
    if (midiOutOpen(out outH, outId, IntPtr.Zero, IntPtr.Zero, 0) != 0) return "Ausgang belegt (Live offen?)";
    keep = OnInput;
    if (midiInOpen(out inH, inId, keep, IntPtr.Zero, 0x30000) != 0) { midiOutClose(outH); return "Eingang belegt (Live offen?)"; }
    midiInStart(inH);
    t0 = DateTime.Now; stop = false; mode = 0; textDirty = true;
    for (int p = 0; p < 64; p++) { sentColor[p] = -1; sentCh[p] = -1; }
    for (int s = 0; s < 8; s++) { stripVal[s] = -999; stripCol[s] = -1; }
    try {
      SysEx(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 });   // Identity: gibt das Display frei
      Thread.Sleep(300);
      Apc(0x1C, 1);
      Short(0x90 | FULL, NOTE_SHIFT, 1);                            // Shift leuchtet als Hinweis
      for (int s = 0; s < 8; s++) Short(0xB0, 104 + s, 1);
      var color = new int[64]; var level = new double[64]; var strip = new double[8]; var scol = new int[8];
      double last = 0;
      while (!stop && (minutes <= 0 || Now() < minutes * 60)) {
        double now = Now(); double spd; lock (lk) spd = speed;
        clock += (now - last) * spd; last = now;
        if (textDirty) {
          textDirty = false; int m; lock (lk) m = mode;
          Line(0, "APC64"); Line(1, NAMES[m]); Line(2, string.Format("Tempo {0:0.00}x", spd));
        }
        Render(clock, now, color, level, strip, scol);
        for (int p = 0; p < 64; p++) SetPad(p, color[p], level[p]);
        for (int s = 0; s < 8; s++) SetStrip(s, scol[s], strip[s]);
        Thread.Sleep(30);
      }
      return stop ? "Mit Stop beendet" : "Zeit abgelaufen";
    } finally {
      for (int p = 0; p < 64; p++) { Short(0x90 | FULL, p, 0); Short(0x90 | HALF, p, 0); }
      Short(0x90 | FULL, NOTE_SHIFT, 0);
      for (int s = 0; s < 8; s++) { Short(0xB0, 104 + s, 0); Short(0xE0 | s, 0, 0); }
      Line(0, ""); Line(1, ""); Line(2, ""); Apc(0x1C, 0);
      midiInStop(inH); midiInClose(inH); midiOutClose(outH);
    }
  }
}
