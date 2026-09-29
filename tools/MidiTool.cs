using System; using System.Runtime.InteropServices; using System.Collections.Generic; using System.Threading; using System.Text;
public static class Midi {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct INCAPS { public ushort mid, pid; public uint ver; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string name; public uint sup; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct OUTCAPS { public ushort mid, pid; public uint ver; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string name; public ushort tech, voices, notes, mask; public uint sup; }
  [StructLayout(LayoutKind.Sequential)] public struct HDR { public IntPtr data; public uint len, rec; public IntPtr user; public uint flags; public IntPtr next, res; public uint offset; [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)] public IntPtr[] r2; }
  [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
  [DllImport("winmm.dll")] static extern uint midiOutGetNumDevs();
  [DllImport("winmm.dll", CharSet=CharSet.Unicode)] static extern uint midiInGetDevCapsW(uint id, ref INCAPS c, uint sz);
  [DllImport("winmm.dll", CharSet=CharSet.Unicode)] static extern uint midiOutGetDevCapsW(uint id, ref OUTCAPS c, uint sz);
  [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr h, uint id, IntPtr cb, IntPtr inst, uint flags);
  [DllImport("winmm.dll")] static extern uint midiInPrepareHeader(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiInUnprepareHeader(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiInAddBuffer(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInReset(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr h);
  [DllImport("winmm.dll")] static extern uint midiOutOpen(out IntPtr h, uint id, IntPtr cb, IntPtr inst, uint flags);
  [DllImport("winmm.dll")] static extern uint midiOutPrepareHeader(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiOutUnprepareHeader(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiOutLongMsg(IntPtr h, IntPtr hdr, uint sz);
  [DllImport("winmm.dll")] static extern uint midiOutClose(IntPtr h);

  public static List<KeyValuePair<uint,string>> Ins(string m) { var l = new List<KeyValuePair<uint,string>>(); uint n = midiInGetNumDevs();
    for (uint i = 0; i < n; i++) { var c = new INCAPS(); midiInGetDevCapsW(i, ref c, (uint)Marshal.SizeOf(c)); if (c.name.Contains(m)) l.Add(new KeyValuePair<uint,string>(i, c.name)); } return l; }
  public static List<KeyValuePair<uint,string>> Outs(string m) { var l = new List<KeyValuePair<uint,string>>(); uint n = midiOutGetNumDevs();
    for (uint i = 0; i < n; i++) { var c = new OUTCAPS(); midiOutGetDevCapsW(i, ref c, (uint)Marshal.SizeOf(c)); if (c.name.Contains(m)) l.Add(new KeyValuePair<uint,string>(i, c.name)); } return l; }

  static IntPtr MakeHdr(byte[] data, int cap) {
    IntPtr buf = Marshal.AllocHGlobal(cap); if (data != null) Marshal.Copy(data, 0, buf, data.Length);
    var h = new HDR(); h.data = buf; h.len = (uint)cap; h.rec = data == null ? 0 : (uint)data.Length; h.r2 = new IntPtr[8];
    IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(h)); Marshal.StructureToPtr(h, p, false); return p; }
  static void FreeHdr(IntPtr p) { var h = (HDR)Marshal.PtrToStructure(p, typeof(HDR)); Marshal.FreeHGlobal(h.data); Marshal.FreeHGlobal(p); }

  // Sends one sysex to output 'outId' and returns everything the listed inputs received as sysex.
  public static string Ask(uint outId, uint[] inIds, byte[] msg, int waitMs) {
    int sz = Marshal.SizeOf(typeof(HDR)); var ins = new List<IntPtr>(); var hdrs = new List<KeyValuePair<int,IntPtr>>();
    for (int k = 0; k < inIds.Length; k++) { IntPtr h; if (midiInOpen(out h, inIds[k], IntPtr.Zero, IntPtr.Zero, 0) != 0) { ins.Add(IntPtr.Zero); continue; }
      ins.Add(h); for (int b = 0; b < 4; b++) { IntPtr p = MakeHdr(null, 4096); midiInPrepareHeader(h, p, (uint)sz); midiInAddBuffer(h, p, (uint)sz); hdrs.Add(new KeyValuePair<int,IntPtr>(k, p)); } midiInStart(h); }
    IntPtr o; string err = null;
    if (midiOutOpen(out o, outId, IntPtr.Zero, IntPtr.Zero, 0) == 0) {
      IntPtr p = MakeHdr(msg, msg.Length); midiOutPrepareHeader(o, p, (uint)sz); midiOutLongMsg(o, p, (uint)sz);
      Thread.Sleep(waitMs);
      for (int i = 0; i < 100 && midiOutUnprepareHeader(o, p, (uint)sz) == 65; i++) Thread.Sleep(10);
      FreeHdr(p); midiOutClose(o);
    } else { err = "Ausgang nicht zu oeffnen (belegt?)"; Thread.Sleep(waitMs); }
    var sb = new StringBuilder(); if (err != null) sb.AppendLine("    " + err);
    for (int k = 0; k < ins.Count; k++) if (ins[k] != IntPtr.Zero) midiInReset(ins[k]);
    foreach (var kv in hdrs) { IntPtr h = ins[kv.Key]; var hd = (HDR)Marshal.PtrToStructure(kv.Value, typeof(HDR));
      if (hd.rec > 0) { var bytes = new byte[hd.rec]; Marshal.Copy(hd.data, bytes, 0, (int)hd.rec);
        sb.AppendLine("    <- in[" + inIds[kv.Key] + "] " + BitConverter.ToString(bytes).Replace("-", " ")); }
      midiInUnprepareHeader(h, kv.Value, (uint)sz); FreeHdr(kv.Value); }
    for (int k = 0; k < ins.Count; k++) if (ins[k] != IntPtr.Zero) midiInClose(ins[k]);
    for (int k = 0; k < inIds.Length; k++) if (ins[k] == IntPtr.Zero) sb.AppendLine("    in[" + inIds[k] + "] nicht zu oeffnen (belegt?)");
    return sb.Length == 0 ? "    (keine Antwort)" : sb.ToString().TrimEnd();
  }

  // ---- Session: keeps one output and one input open for many requests ----
  static IntPtr sOut = IntPtr.Zero, sIn = IntPtr.Zero;
  static List<IntPtr> sBufs = new List<IntPtr>();

  public static bool Open(uint outId, uint inId) {
    Close();
    if (midiOutOpen(out sOut, outId, IntPtr.Zero, IntPtr.Zero, 0) != 0) { sOut = IntPtr.Zero; return false; }
    if (midiInOpen(out sIn, inId, IntPtr.Zero, IntPtr.Zero, 0) != 0) { sIn = IntPtr.Zero; Close(); return false; }
    midiInStart(sIn);
    return true;
  }

  public static void Close() {
    int sz = Marshal.SizeOf(typeof(HDR));
    if (sIn != IntPtr.Zero) {
      midiInReset(sIn);
      foreach (var p in sBufs) { midiInUnprepareHeader(sIn, p, (uint)sz); FreeHdr(p); }
      sBufs.Clear(); midiInClose(sIn); sIn = IntPtr.Zero;
    }
    if (sOut != IntPtr.Zero) { midiOutClose(sOut); sOut = IntPtr.Zero; }
  }

  // Sends one sysex and waits for the next complete sysex on the input.
  // Returns the reply bytes, or null on timeout.
  public static byte[] Request(byte[] msg, int timeoutMs) {
    int sz = Marshal.SizeOf(typeof(HDR));
    IntPtr inHdr = MakeHdr(null, 4096);
    midiInPrepareHeader(sIn, inHdr, (uint)sz); midiInAddBuffer(sIn, inHdr, (uint)sz); sBufs.Add(inHdr);
    IntPtr outHdr = MakeHdr(msg, msg.Length);
    midiOutPrepareHeader(sOut, outHdr, (uint)sz); midiOutLongMsg(sOut, outHdr, (uint)sz);
    byte[] reply = null;
    var until = DateTime.Now.AddMilliseconds(timeoutMs);
    while (DateTime.Now < until) {
      var h = (HDR)Marshal.PtrToStructure(inHdr, typeof(HDR));
      if ((h.flags & 1) != 0 && h.rec > 0) { reply = new byte[h.rec]; Marshal.Copy(h.data, reply, 0, (int)h.rec); break; }
      Thread.Sleep(2);
    }
    for (int i = 0; i < 100 && midiOutUnprepareHeader(sOut, outHdr, (uint)sz) == 65; i++) Thread.Sleep(2);
    FreeHdr(outHdr);
    if (reply != null) { midiInUnprepareHeader(sIn, inHdr, (uint)sz); FreeHdr(inHdr); sBufs.Remove(inHdr); }
    return reply;
  }

  // Fire-and-forget sysex over the open session (e.g. RAM writes).
  public static void Send(byte[] msg) {
    int sz = Marshal.SizeOf(typeof(HDR));
    IntPtr outHdr = MakeHdr(msg, msg.Length);
    midiOutPrepareHeader(sOut, outHdr, (uint)sz); midiOutLongMsg(sOut, outHdr, (uint)sz);
    for (int i = 0; i < 100 && midiOutUnprepareHeader(sOut, outHdr, (uint)sz) == 65; i++) Thread.Sleep(1);
    FreeHdr(outHdr);
  }

  [DllImport("winmm.dll")] static extern uint midiOutShortMsg(IntPtr h, uint msg);
  // Short message (note, CC, pitch bend) over the open session.
  public static void Short(int status, int d1, int d2) {
    midiOutShortMsg(sOut, (uint)(status | ((d1 & 0x7F) << 8) | ((d2 & 0x7F) << 16)));
  }
}
