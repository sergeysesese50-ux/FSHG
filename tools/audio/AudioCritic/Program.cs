using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>One analysed file (or a trimmed fight span of a demo).</summary>
public sealed class Entry
{
    public string Palette, Rel, FullPath, Role, Situation = "", LoopMode = "";
    public double FightStart = -1;
    public double TrimStart, TrimEnd = -1;
    public string Label => Palette + "/" + Rel + (Role.EndsWith("-fight") ? " [fight]" : "");
    public AudioBuf A;
    public double[] X;
    public int Fs;
    // results
    public double Dur, Peak, TP, Rms, ActRms, Dc, FirstRel, LastRel;
    public int Full, Near;
    public M.Loud Loud;
    public M.Spec Spec;
    public M.Frames Fr;
    public (double med, double p90, int n) Mod25 = (double.NaN, double.NaN, 0), Mod28 = (double.NaN, double.NaN, 0), ModBB20 = (double.NaN, double.NaN, 0);
    public double Harsh = double.NaN, HarshPh = double.NaN;
    public M.Ticks Tk;
    public (double r, double lag) WRep = (double.NaN, double.NaN), FRep = (double.NaN, double.NaN);
    public List<M.Seam> Seams = new List<M.Seam>();
    public double LoopPeriod = double.NaN;
    public M.Lines Lines;
    public Variants.Result Var;
    public int Prominent = -1;
    public List<(string sev, string msg)> Flags = new List<(string, string)>();
    public bool Sustained => Role is "reel" or "drag" or "line" or "rod" or "loop" or "rawloop" or "rotation" or "demoreel" or "demoreel-fight" or "stem" or "seamcheck";
}

public static class Program
{
    static string Root = RepoPaths.Previews();
    static readonly string Here = AppContext.BaseDirectory;
    static string HarnessDir => Path.GetFullPath(Path.Combine(Here, "..", "..", ".."));

    // fight start (s) inside each demo file, from the renderers: current 0.8 (12 wait + 36 bite frames), natural 3.9 (234 frames),
    // cozy perch 5.9 / bream 3.9 (its out.txt), tactile 4.583 (first Fight row of its frames.csv)
    static double FightStartOf(string palette, string name)
    {
        switch (palette)
        {
            case "current": return 0.8;
            case "natural": return 3.9;
            case "cozy": return name.Contains("perch") ? 5.9 : 3.9;
            case "tactile": return 4.583;
        }
        return 0;
    }

    static Entry Classify(string palette, string rel, string full)
    {
        var e = new Entry { Palette = palette, Rel = rel.Replace('\\', '/'), FullPath = full, Role = "oneshot" };
        string n = Path.GetFileNameWithoutExtension(rel).ToLowerInvariant();
        bool inParts = e.Rel.StartsWith("parts/");
        if (palette == "loops")
        {
            e.Role = "rawloop"; e.LoopMode = "wrap";
            e.Palette = n.Split('_')[1];
            e.Rel = "raw:" + n.Substring(n.IndexOf('_', 5) + 1);
            if (n.Contains("ambience")) e.Role = "rawamb";
            return e;
        }
        if (palette == "current")
        {
            if (n.EndsWith("_loop")) { e.Role = "loop"; e.LoopMode = "tiled"; }
            else if (n.StartsWith("clip_dragclick_rotation") || n.StartsWith("clip_rodcreak_rotation")) e.Role = "rotation";
            else if (Regex.IsMatch(n, @"^clip_dragclick\d$")) e.Role = "grain";
            else if (n.StartsWith("fightdemo_"))
            {
                if (n.EndsWith("_reelbed")) e.Role = "demoreel";
                else if (n.EndsWith("_whir") || n.EndsWith("_growl")) e.Role = "stem";
                else e.Role = "demo";
            }
            if (n == "clip_whir_loop") e.Situation = "reel_free";
            if (n == "clip_growl_loop") e.Situation = "reel_load";
            if (n == "clip_dragclick_rotation_12ps") e.Situation = "drag_slow";
            if (n == "clip_clickloop_loop") e.Situation = "drag_fast";
            if (n == "clip_linesing_loop") e.Situation = "line";
            if (n == "clip_rodcreak_rotation") e.Situation = "rod";
        }
        else if (inParts)
        {
            if (n.StartsWith("grains_")) e.Role = "variants";
            else if (n.StartsWith("loop_")) { e.Role = "loop"; e.LoopMode = "mid+wrap"; }
        }
        else
        {
            int num = int.TryParse(n.Substring(0, 2), out var v) ? v : -1;
            switch (palette)
            {
                case "natural":
                    if (num is 1 or 2 or 3 or 27) e.Role = "reel";
                    else if (num is 4 or 5) e.Role = "drag";
                    else if (num == 6) e.Role = "line";
                    else if (num == 7) e.Role = "rod";
                    else if (num == 30) { e.Role = "ambience"; e.LoopMode = "wrap"; }
                    else if (num == 31) { e.Role = "seamcheck"; e.LoopMode = "mid"; }
                    else if (num is 40 or 41) e.Role = "demo";
                    else if (num is 42 or 43) e.Role = "demoreel";
                    else if (num >= 50) e.Role = "variants";
                    e.Situation = num switch { 1 => "reel_free", 2 => "reel_load", 3 => "reel_fast", 4 => "drag_slow", 5 => "drag_fast", 6 => "line", 7 => "rod", 30 => "amb", _ => "" };
                    break;
                case "cozy":
                    if (num is >= 1 and <= 4) e.Role = "reel";
                    else if (num is 5 or 6) e.Role = "drag";
                    else if (num == 7) e.Role = "line";
                    else if (num == 8) e.Role = "rod";
                    else if (num == 27) e.Role = "oneshot";
                    else if (num == 28) { e.Role = "ambience"; e.LoopMode = "wrap"; }
                    else if (num == 29) e.Role = "ambience";
                    else if (num is 30 or 31) e.Role = "demo";
                    e.Situation = num switch { 1 => "reel_free", 2 => "reel_load", 3 => "reel_fast", 5 => "drag_slow", 6 => "drag_fast", 7 => "line", 8 => "rod", 28 => "amb", _ => "" };
                    break;
                case "tactile":
                    if (num is >= 1 and <= 4) e.Role = "reel";
                    else if (num is 5 or 6) e.Role = "drag";
                    else if (num == 7) e.Role = "line";
                    else if (num == 8) e.Role = "rod";
                    else if (num == 30) { e.Role = "ambience"; e.LoopMode = "wrap"; }
                    else if (num is >= 40 and <= 43) e.Role = "variants";
                    else if (num is 90 or 91) e.Role = n.EndsWith("_reel_only") ? "demoreel" : "demo";
                    e.Situation = num switch { 1 => "reel_free", 2 => "reel_load", 3 => "reel_fast", 5 => "drag_slow", 6 => "drag_fast", 7 => "line", 8 => "rod", 30 => "amb", _ => "" };
                    break;
            }
        }
        if (e.Role is "demo" or "demoreel")
        {
            string fish = n.Contains("perch") ? "perch" : n.Contains("bream") ? "bream" : n.Contains("pike") ? "pike" : "?";
            e.Situation = (e.Role == "demo" ? "demo_" : "demoreel_") + fish;
            e.FightStart = FightStartOf(palette, n);
        }
        return e;
    }

    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length > 0 && args[0] == "--debug70") { Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture; SelfTest.Debug70(); return; }
        if (args.Length > 0) Root = args[0];
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string outPath = Path.Combine(HarnessDir, "out.txt");
        var o = new StringWriter();
        o.WriteLine($"AudioCritic run {DateTime.Now:yyyy-MM-dd HH:mm}; root {Root}");
        o.WriteLine();
        SelfTest.Run(o);

        // ---------- catalog ----------
        var entries = new List<Entry>();
        foreach (var pal in new[] { "current", "natural", "cozy", "tactile" })
            foreach (var f in Directory.GetFiles(Path.Combine(Root, pal), "*.wav", SearchOption.AllDirectories).OrderBy(s => s, StringComparer.Ordinal))
                entries.Add(Classify(pal, Path.GetRelativePath(Path.Combine(Root, pal), f), f));
        string loopDir = Path.Combine(HarnessDir, "loops");
        if (Directory.Exists(loopDir))
            foreach (var f in Directory.GetFiles(loopDir, "loop_*.wav").OrderBy(s => s, StringComparer.Ordinal))
                entries.Add(Classify("loops", Path.GetFileName(f), f));
        foreach (var e in entries) { e.A = Wav.Read(e.FullPath); e.X = e.A.X; e.Fs = e.A.Fs; }
        // fight-span virtual entries for demos
        var fights = new List<Entry>();
        foreach (var e in entries.Where(z => z.Role is "demo" or "demoreel"))
        {
            int s0 = (int)Math.Round(e.FightStart * e.Fs);
            var seg = e.X.Skip(s0).ToArray();
            fights.Add(new Entry
            {
                Palette = e.Palette, Rel = e.Rel, FullPath = e.FullPath, Role = e.Role + "-fight", Situation = e.Situation, FightStart = 0,
                TrimStart = e.FightStart, A = e.A, X = seg, Fs = e.Fs
            });
        }
        entries.AddRange(fights);

        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, e =>
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Analyze(e);
        });
        foreach (var e in entries) Judge(e);

        Report(o, entries);
        var ab = BuildAB(o, entries);
        o.WriteLine($"\nruntime {sw.Elapsed.TotalSeconds:F1} s");
        File.WriteAllText(outPath, o.ToString(), new UTF8Encoding(false));
        WriteCsv(Path.Combine(HarnessDir, "metrics.csv"), entries);
        Console.Write(o.ToString());
    }

    // =====================================================================================================
    static void Analyze(Entry e)
    {
        var x = e.X; int fs = e.Fs;
        e.Dur = (double)x.Length / fs;
        e.Peak = M.Peak(x); e.TP = M.TruePeak(x); e.Rms = M.Rms(x); e.ActRms = M.ActiveRms(x, fs); e.Dc = M.Mean(x);
        if (!e.Role.EndsWith("-fight")) { e.Full = e.A.FullScale; e.Near = e.A.NearClip; }
        else { double thr = Math.Pow(10, -0.1 / 20); e.Full = x.Count(v => Math.Abs(v) >= 32767.0 / 32768.0); e.Near = x.Count(v => Math.Abs(v) >= thr); }
        e.FirstRel = x.Length > 0 ? Math.Abs(x[0]) / Math.Max(e.Peak, 1e-12) : 0;
        e.LastRel = x.Length > 0 ? Math.Abs(x[x.Length - 1]) / Math.Max(e.Peak, 1e-12) : 0;
        e.Loud = M.Loudness(x, fs);
        int seg = M.SegFor(fs);
        e.Spec = M.Bands(M.Welch(x, seg), (double)fs / seg);
        bool sustained = e.Sustained || e.Role is "demo" or "demo-fight" or "ambience" or "rawamb";
        e.Fr = M.FrameStats(x, fs, true);
        if (sustained && e.Dur >= 0.4)
        {
            bool circ = e.Role is "rawloop" or "rawamb";
            e.Mod25 = M.ModIndex(x, fs, 2000, 5000, 20, 150, circ);
            e.Mod28 = M.ModIndex(x, fs, 0, 0, 2, 8, circ);
            e.ModBB20 = M.ModIndex(x, fs, 0, 0, 20, 150, circ);
            e.Harsh = e.Spec.A[2] * e.Mod25.med;
            e.HarshPh = e.Spec.Ph[2] * e.Mod25.med;
        }
        if (e.Role is "reel" or "drag" or "rod" or "rotation" or "demoreel" or "demoreel-fight" or "demo-fight" or "variants")
            e.Tk = M.DetectTicks(x, fs, e.Role == "variants");
        // repetition
        if (e.Role is "loop" or "reel" or "drag" or "line" or "rod" or "demoreel-fight" or "stem" or "ambience" or "rawloop" or "rawamb" or "seamcheck")
        {
            bool circ = e.Role is "rawloop" or "rawamb" || (e.Role == "ambience" && e.LoopMode == "wrap");
            e.WRep = M.WaveRepeat(x, fs, 0.05, circ);
            if (e.Dur >= 20 || e.Role is "ambience" or "rawamb") e.FRep = M.FeatureRepeat(x, fs, 1.0, circ);
        }
        if (e.Role is "loop" or "rawloop" or "rawamb" || (e.Role == "ambience" && e.LoopMode == "wrap")) e.Lines = M.LineDepth(x, fs);
        if (e.Role == "variants") e.Var = Variants.Analyze(x, fs);
        // seams
        if (e.LoopMode == "wrap") e.Seams.Add(M.SeamAt(x, fs, 0));
        if (e.LoopMode is "mid" or "mid+wrap") e.Seams.Add(M.SeamAt(x, fs, x.Length / 2));
        if (e.LoopMode == "mid+wrap") e.Seams.Add(M.SeamAt(x, fs, 0));
        if (e.LoopMode == "tiled")
        {
            // find the loop period: exact repeat (r ≥ 0.99) at the smallest such lag
            var (r, lag) = M.WaveRepeat(x, fs, 0.05, false);
            int P = FindPeriod(x, fs);
            if (P > 0) { e.LoopPeriod = (double)P / fs; e.Seams.Add(M.SeamAt(x.Take(P).ToArray(), fs, 0)); }
        }
        // prominent moments in ambience: 1 s windows ≥ 6 dB above the median 1 s level
        if (e.Role is "ambience" or "rawamb")
        {
            int w = fs; int nw = x.Length / w;
            var lv = Enumerable.Range(0, nw).Select(j => M.DbP(Enumerable.Range(j * w, w).Select(i => x[i] * x[i]).Average())).ToArray();
            double med = D.Median(lv);
            e.Prominent = lv.Count(v => v >= med + 6);
        }
    }

    /// <summary>Exact loop period of a tiled file: smallest lag ≥ 50 ms where the waveform repeats (normalised correlation ≥ 0.999).</summary>
    static int FindPeriod(double[] x, int fs)
    {
        int n = x.Length;
        double e0 = 0;
        for (int lag = (int)(0.05 * fs); lag <= n / 2; lag++)
        {
            double acc = 0, ea = 0, eb = 0;
            int cnt = Math.Min(n - lag, fs / 2);
            for (int i = 0; i < cnt; i++) { acc += x[i] * x[i + lag]; ea += x[i] * x[i]; eb += x[i + lag] * x[i + lag]; }
            if (acc / Math.Sqrt(Math.Max(ea * eb, 1e-30)) >= 0.999) return lag;
        }
        return -1;
    }

    // =====================================================================================================
    static void Judge(Entry e)
    {
        void F(string sev, string msg) => e.Flags.Add((sev, msg));
        bool fight = e.Role.EndsWith("-fight");
        if (!fight && e.Full > 0) F("DEFECT", $"clipping: {e.Full} samples at full scale");
        if (!fight && M.Db(e.TP) > -1.0) F("WARN", $"true peak {M.Db(e.TP):F1} dBTP > -1 (rule 18)");
        if (Math.Abs(e.Dc) > 0.002) F("WARN", $"DC offset {e.Dc:F4}");
        if (e.Spec.Sub20 > 0.05) F("WARN", $"{100 * e.Spec.Sub20:F0}% of energy below 20 Hz");
        if ((e.Role is "oneshot" or "grain") && e.FirstRel > 0.02) F("WARN", $"starts on a jump: first sample {100 * e.FirstRel:F0}% of peak");
        if ((e.Role is "oneshot" or "grain") && e.LastRel > 0.02) F("WARN", $"ends abruptly: last sample {100 * e.LastRel:F0}% of peak");
        bool repeated = e.Sustained || e.Role is "variants" or "grain";
        bool isAmb = e.Role is "ambience" or "rawamb";
        if (repeated && !isAmb)
        {
            if (e.Spec.Flat[2] > 0.10) F("DEFECT", $"2-5 kHz holds {100 * e.Spec.Flat[2]:F0}% of energy (rule 13: ≤10% for repeated layers)");
            if (e.Spec.Flat[3] > 0.02) F("WARN", $">5 kHz holds {100 * e.Spec.Flat[3]:F1}% of energy (rule 13)");
            if (e.Spec.Ph[2] > 0.15) F("WARN", $"on a phone speaker 2-5 kHz is {100 * e.Spec.Ph[2]:F0}% of what is heard");
            if (e.Spec.Cpow < 400 || e.Spec.Cpow > 1300) F("NOTE", $"centroid {e.Spec.Cpow:F0} Hz outside 400-1300 (rule 13)");
        }
        if (e.Lines != null)
        {
            if (e.Lines.D28 > 0.10) F("DEFECT", $"periodic AM {100 * e.Lines.D28:F0}% at {e.Lines.F28:F2} Hz (rule 14: ≤10% at 2-8 Hz on sustained layers)");
            if (e.Lines.D20 > 0.10) F("DEFECT", $"periodic AM {100 * e.Lines.D20:F0}% at {e.Lines.F20:F1} Hz (rule 14: ≤10% at 20-150 Hz)");
            if (e.Lines.D820 > 0.10) F("WARN", $"periodic AM {100 * e.Lines.D820:F0}% at {e.Lines.F820:F1} Hz (8-20 Hz flutter, between the rule-14 bands)");
        }
        if ((e.Sustained || e.Role is "demo" or "demo-fight") && e.Fr.BuzzFrac > 0.2)
            F(e.Role is "rod" ? "WARN" : "DEFECT", $"buzzer-like harmonic comb (harmonics ≥ {M.BuzzThreshold} dB over the gaps) in {100 * e.Fr.BuzzFrac:F0}% of active energy (p50 {e.Fr.BuzzMed:F1} dB)");
        if (e.Tk != null && e.Tk.T.Count >= 12 && e.Role != "variants")
        {
            var t = e.Tk;
            bool micro = t.IoiMed < 0.015;   // >= 67 onsets/s: stick-slip micro-pulses inside a creak/strain, not separate grains
            bool regular = t.Kept >= 0.7;    // a steady rhythm (most consecutive IOIs within 30% of each other)
            if (t.RotationK > 0) F("DEFECT", $"fixed rotation of {t.RotationK} variants (lag-{t.RotationK} similarity {t.SimLag[t.RotationK]:F2} vs lag-1 {t.SimLag[1]:F2})");
            if (e.Role is "rod" || micro)
            {
                if (t.Ident > 0.5) F("NOTE", $"{100 * t.Ident:F0}% of consecutive micro-pulses identical at {1 / t.IoiMed:F0}/s: a stick-slip texture (pitch/roughness), not separate grains");
            }
            else if (t.Ident > 0.5) F("DEFECT", $"machine-gun: {100 * t.Ident:F0}% of consecutive grains identical (sim >= 0.97)");
            else if (t.Ident > 0.10) F("WARN", $"{100 * t.Ident:F0}% of consecutive grains identical (sim >= 0.97): near-duplicate variants");
            if (regular && t.T.Count >= 20 && t.JitMed < 1.0) F("WARN", $"metronomic: local IOI change only {t.JitMed:F1}% (rule 1 asks +-4%, i.e. about 3.8%)");
            else if (regular && t.T.Count >= 20 && t.JitMed < 2.5 && !micro) F("NOTE", $"very steady rhythm: local IOI change {t.JitMed:F1}% (rule 1 target about 3.8%)");
            if (regular && t.JitMed > 12) F("WARN", $"limping rhythm: local IOI change {t.JitMed:F1}%");
            if (t.T.Count >= 30 && t.R60 > 0.4) F("WARN", $"onsets snapped to the 60 fps frame grid ({100 * t.R60:F0}% of IOIs on the grid)");
        }
        if (e.Var != null && e.Var.N >= 4)
        {
            var v = e.Var;
            if (v.NearestMed >= 0.97 || v.Clusters <= v.N / 3)
                F("WARN", $"variants nearly identical: {v.Clusters} distinct sounds among {v.N} at 0.97, nearest-sibling similarity p50 {v.NearestMed:F2}, pairwise mean {v.PairMean:F2}; variation rests on gain/timing");
            else if (v.NearestMed >= 0.93)
                F("NOTE", $"variants close: nearest-sibling similarity p50 {v.NearestMed:F2}, {v.Clusters} distinct among {v.N}");
        }
        // AM-based harshness only means roughness for dense material: sparse ticks (< 20/s) read high merely because their envelope is peaky
        bool sparse = e.Tk != null && e.Tk.T.Count >= 12 && e.Tk.Rate < 20;
        if (e.Harsh >= 0.05 && !sparse && (e.Sustained || e.Role is "demo-fight")) F("WARN", $"harsh/rough: A-weighted 2-5 kHz share {100 * e.Spec.A[2]:F0}% x AM(20-150) {e.Mod25.med:F2} = {e.Harsh:F3}");
    }

    // =====================================================================================================
    static string P(double v, string f = "F1") => double.IsNaN(v) ? "-" : v.ToString(f);
    static string Pc(double v) => double.IsNaN(v) ? "-" : (100 * v).ToString("F1");

    static void Report(StringWriter o, List<Entry> all)
    {
        var order = new[] { "current", "natural", "cozy", "tactile" };
        IEnumerable<Entry> Ord(IEnumerable<Entry> s) => s.OrderBy(e => Array.IndexOf(order, e.Palette)).ThenBy(e => e.Rel, StringComparer.Ordinal).ThenBy(e => e.Role);

        o.WriteLine("=== A. LEVELS (every file; fight-span rows start at the hookset) ===");
        o.WriteLine($"{"file",-58} {"role",-14} {"fs",5} {"sec",6} {"peak",6} {"TP",6} {"RMS",6} {"actRMS",6} {"crest",5} {"DC",8} {"FS#",4} {"LUFS-I",6} {"Mmax",6} {"LRA",5} {"x0%",4} {"xN%",4}");
        foreach (var e in Ord(all))
            o.WriteLine($"{Trunc(e.Label, 58),-58} {e.Role,-14} {e.Fs,5} {e.Dur,6:F2} {M.Db(e.Peak),6:F1} {M.Db(e.TP),6:F1} {M.Db(e.Rms),6:F1} {M.Db(e.ActRms),6:F1} {M.Db(e.Peak) - M.Db(e.ActRms),5:F1} {e.Dc,8:F5} {e.Full,4} {P(e.Loud.I),6} {P(e.Loud.Mmax),6} {P(e.Loud.Lra),5} {100 * e.FirstRel,4:F0} {100 * e.LastRel,4:F0}");

        o.WriteLine();
        o.WriteLine("=== B. SPECTRUM: shares % <300 | 300-2k | 2-5k | >5k  (flat / A-weighted / phone x A); centroids Hz (power, magnitude); centroid over time ===");
        o.WriteLine($"{"file",-58} {"flat <300 mid 2-5k >5k",-24} {"A 2-5k",6} {"ph 2-5k",7} {"Cpow",5} {"Cmag",5} {"Ct-mean",7} {"Ct-sd",5} {"Ct p10-p90",11} {"dC%",5}");
        foreach (var e in Ord(all))
            o.WriteLine($"{Trunc(e.Label, 58),-58} {100 * e.Spec.Flat[0],5:F1} {100 * e.Spec.Flat[1],5:F1} {100 * e.Spec.Flat[2],5:F1} {100 * e.Spec.Flat[3],5:F1}  {100 * e.Spec.A[2],6:F1} {100 * e.Spec.Ph[2],7:F1} {e.Spec.Cpow,5:F0} {e.Spec.Cmag,5:F0} {P(e.Fr.CMean, "F0"),7} {P(e.Fr.CStd, "F0"),5} {P(e.Fr.CP10, "F0") + "-" + P(e.Fr.CP90, "F0"),11} {P(e.Fr.CDeltaMed),5}");

        o.WriteLine();
        o.WriteLine("=== C. MODULATION / ROUGHNESS / BUZZ (sustained material) ===");
        o.WriteLine("  m25 = AM index 20-150 Hz of the 2-5 kHz band (p50/p90); H = A-weighted 2-5k share x m25 (harshness proxy), Hph = phone version;");
        o.WriteLine("  m2-8 / m20-150 = AM index of the whole signal (rule 14 applies to loops); buzz = harmonic contrast dB p50/p90 (harmonics vs gaps, f0 80-400 Hz), frac = energy share with contrast ≥ " + M.BuzzThreshold);
        o.WriteLine($"{"file",-58} {"m25 p50/p90",11} {"H",6} {"Hph",6} {"m2-8",6} {"m20-150",7} {"buzz",9} {"frac%",5}");
        foreach (var e in Ord(all.Where(z => !double.IsNaN(z.Mod25.med) || !double.IsNaN(z.ModBB20.med))))
            o.WriteLine($"{Trunc(e.Label, 58),-58} {P(e.Mod25.med, "F2") + "/" + P(e.Mod25.p90, "F2"),11} {P(e.Harsh, "F3"),6} {P(e.HarshPh, "F3"),6} {P(e.Mod28.med, "F2"),6} {P(e.ModBB20.med, "F2"),7} {P(e.Fr.BuzzMed) + "/" + P(e.Fr.BuzzP90),9} {Pc(e.Fr.BuzzFrac),5}");

        o.WriteLine();
        o.WriteLine("=== D. TICKS / GRAINS: onsets, rate over busy stretches, local IOI change (p50/p90, pairs kept), frame-grid locking Q60/Q20 (% of IOIs on a 16.7/50 ms grid),");
        o.WriteLine("       consecutive-grain similarity (p50, % identical ≥0.97), lag-k similarity k=1..6, fixed rotation; variant sets: pairwise mean/max, clusters@0.97 ===");
        o.WriteLine($"{"file",-58} {"n",5} {"rate",6} {"jit p50/p90",11} {"kept",4} {"Q60%",5} {"Q20%",5} {"sim",5} {"ident",5} {"lag1..6",-35} {"rot",3} {"pair m/max",10} {"cl",3}");
        foreach (var e in Ord(all.Where(z => z.Tk != null)))
        {
            var t = e.Tk;
            o.WriteLine($"{Trunc(e.Label, 58),-58} {t.T.Count,5} {P(t.Rate),6} {P(t.JitMed) + "/" + P(t.JitP90),11} {Pc(t.Kept).Replace(".0", ""),4} {Pc(t.R60),5} {Pc(t.R20),5} {P(t.SimMed, "F2"),5} {Pc(t.Ident),5} {string.Join(" ", t.SimLag.Skip(1).Select(v => P(v, "F2"))),-35} {t.RotationK,3} {P(t.PairMean, "F2") + "/" + P(t.PairMax, "F2"),10} {(t.Clusters > 0 ? t.Clusters.ToString() : "-"),3}");
        }

        o.WriteLine();
        o.WriteLine("  variant sets (whole variants segmented at silences, pairwise max xcorr ±0.5 ms; clusters = sounds ≥ 0.97 alike):");
        foreach (var e in Ord(all.Where(z => z.Var != null)))
            o.WriteLine($"    {Trunc(e.Label, 56),-56} variants {e.Var.N,3}  pairwise mean {P(e.Var.PairMean, "F2")}  max {P(e.Var.PairMax, "F2")}  nearest-sibling p50 {P(e.Var.NearestMed, "F2")}  distinct {e.Var.Clusters}");

        o.WriteLine();
        o.WriteLine("=== E. REPETITION AND LOOP SEAMS ===");
        o.WriteLine("  wave r = max normalised waveform autocorrelation at lag ≥ 50 ms (1.0 = sample-exact repeat); feat r = 7-band level-pattern self-similarity at lag ≥ 1 s");
        foreach (var e in Ord(all.Where(z => !double.IsNaN(z.WRep.r) || z.Seams.Count > 0)))
        {
            o.Write($"{Trunc(e.Label, 58),-58} wave r {P(e.WRep.r, "F2")} @ {P(e.WRep.lag, "F2")} s");
            if (!double.IsNaN(e.FRep.r)) o.Write($"; feat r {e.FRep.r:F2} @ {e.FRep.lag:F1} s");
            if (!double.IsNaN(e.LoopPeriod)) o.Write($"; tiled loop period {e.LoopPeriod:F3} s");
            if (e.Prominent >= 0) o.Write($"; prominent 1-s moments (≥ +6 dB over median) {e.Prominent}");
            if (e.Lines != null) o.Write($"; periodic AM lines: 2-8 Hz {100 * e.Lines.D28:F1}% @{e.Lines.F28:F2}, 8-20 Hz {100 * e.Lines.D820:F1}% @{e.Lines.F820:F1}, 20-150 Hz {100 * e.Lines.D20:F1}% @{e.Lines.F20:F1}");
            o.WriteLine();
            foreach (var s in e.Seams) o.WriteLine($"      seam {s}");
        }

        // ---------- situation comparison ----------
        o.WriteLine();
        o.WriteLine("=== F. SAME SITUATION ACROSS PALETTES (current = raw FightSynth clip where no rendered situation exists) ===");
        var sits = new[] { "reel_free", "reel_load", "reel_fast", "drag_slow", "drag_fast", "line", "rod", "amb", "demo_perch", "demo_bream", "demoreel_perch", "demoreel_bream" };
        o.WriteLine($"{"situation",-15} {"palette",-8} {"file",-40} {"LUFS",6} {"Cpow",5} {"<300%",5} {"2-5k%",6} {"A%",5} {"ph%",5} {">5k%",5} {"phLoss",6} {"H",6} {"buzz%",5} {"rate",5} {"jit",5} {"sim",5} {"m2-8",5}");
        foreach (var s in sits)
        {
            foreach (var pal in order)
            {
                var cand = all.Where(z => z.Palette == pal && z.Situation == s).ToList();
                var e = cand.FirstOrDefault(z => z.Role.EndsWith("-fight")) ?? cand.FirstOrDefault();
                if (e == null) continue;
                o.WriteLine($"{s,-15} {pal,-8} {Trunc(e.Rel + (e.Role.EndsWith("-fight") ? " [fight]" : ""), 40),-40} {P(e.Loud.I),6} {e.Spec.Cpow,5:F0} {100 * e.Spec.Flat[0],5:F1} {100 * e.Spec.Flat[2],6:F1} {100 * e.Spec.A[2],5:F1} {100 * e.Spec.Ph[2],5:F1} {100 * e.Spec.Flat[3],5:F1} {e.Spec.PhoneLossDb,6:F1} {P(e.Harsh, "F3"),6} {Pc(e.Fr.BuzzFrac),5} {P(e.Tk?.Rate ?? double.NaN),5} {P(e.Tk?.JitMed ?? double.NaN),5} {P(e.Tk?.SimMed ?? double.NaN, "F2"),5} {P(e.Mod28.med, "F2"),5}");
            }
            o.WriteLine();
        }

        // ---------- ambience under reel (difference method) ----------
        o.WriteLine("=== G. AMBIENCE + EVENTS UNDER THE REEL (full demo minus reel-only demo, sample-aligned; 400 ms blocks in the fight span where the reel is active) ===");
        foreach (var (pal, full, reel) in new[] { ("natural", "40_demo_perch300_holdreel.wav", "42_demo_perch300_reel_only.wav"), ("natural", "41_demo_bream2kg_technique.wav", "43_demo_bream2kg_reel_drag_only.wav"),
                                                  ("tactile", "90_fight_perch300_HoldReel.wav", "90_fight_perch300_HoldReel_reel_only.wav"), ("tactile", "91_fight_bream2kg_Technique.wav", "91_fight_bream2kg_Technique_reel_only.wav") })
        {
            var a = all.FirstOrDefault(z => z.Palette == pal && z.Rel == full && z.Role == "demo");
            var b = all.FirstOrDefault(z => z.Palette == pal && z.Rel == reel && z.Role == "demoreel");
            if (a == null || b == null || a.X.Length != b.X.Length) { o.WriteLine($"  {pal}/{full}: n/a"); continue; }
            var diff = new double[a.X.Length];
            for (int i = 0; i < diff.Length; i++) diff[i] = a.X[i] - b.X[i];
            int s0 = (int)(a.FightStart * a.Fs);
            var ld = M.Loudness(diff.Skip(s0).ToArray(), a.Fs).Short100;
            var lr = M.Loudness(b.X.Skip(s0).ToArray(), a.Fs).Short100;
            double med = D.Median(lr);
            var gaps = Enumerable.Range(0, Math.Min(ld.Length, lr.Length)).Where(i => lr[i] >= med - 10).Select(i => lr[i] - ld[i]).ToArray();
            o.WriteLine($"  {pal}/{full}: reel-only fight LUFS {M.Loudness(b.X.Skip(s0).ToArray(), a.Fs).I:F1}, (ambience+events) fight LUFS {M.Loudness(diff.Skip(s0).ToArray(), a.Fs).I:F1}; reel minus bed per block p10/p50/p90 = {D.Pct(gaps, 10):F1}/{D.Pct(gaps, 50):F1}/{D.Pct(gaps, 90):F1} dB");
        }

        // ---------- level consistency ----------
        o.WriteLine();
        o.WriteLine("=== H. LEVEL CONSISTENCY ===");
        foreach (var s in new[] { "demo_perch", "demo_bream", "demoreel_perch", "demoreel_bream" })
        {
            var rows = order.Select(p => all.FirstOrDefault(z => z.Palette == p && z.Situation == s && z.Role.EndsWith("-fight"))).Where(z => z != null).ToList();
            var cur = rows.FirstOrDefault(z => z.Palette == "current");
            o.WriteLine($"  {s}: " + string.Join("; ", rows.Select(z => $"{z.Palette} {z.Loud.I:F1} LUFS (TP {M.Db(z.TP):F1})" + (cur != null && z != cur ? $" = {z.Loud.I - cur.Loud.I:+0.0;-0.0} LU vs current" : ""))));
        }
        foreach (var pal in order)
        {
            var sit = all.Where(z => z.Palette == pal && z.Role is "reel" or "drag" or "line" or "rod").ToList();
            if (sit.Count > 0)
                o.WriteLine($"  {pal} situation files LUFS: " + string.Join(", ", sit.OrderBy(z => z.Rel).Select(z => $"{z.Rel.Substring(0, 2)} {z.Loud.I:F1}")));
            var os = all.Where(z => z.Palette == pal && z.Role == "oneshot").ToList();
            if (os.Count > 0)
            {
                var mm = os.Select(z => z.Loud.Mmax).ToArray(); double med = D.Median(mm);
                var outl = os.Where(z => Math.Abs(z.Loud.Mmax - med) > 6).Select(z => $"{z.Rel} {z.Loud.Mmax:F1}");
                o.WriteLine($"  {pal} one-shots max-momentary LUFS: median {med:F1}, range {mm.Min():F1}..{mm.Max():F1}; >6 LU from median: {string.Join(", ", outl)}");
            }
        }

        // ---------- flags ----------
        o.WriteLine();
        o.WriteLine("=== I. FLAGS (DEFECT = breaks a research rule or is audible; WARN = check by ear; NOTE = informational) ===");
        foreach (var pal in order.Concat(new[] { "loops" }))
            foreach (var e in Ord(all.Where(z => z.Palette == pal && z.Flags.Count > 0)))
                foreach (var (sev, msg) in e.Flags.OrderBy(f => f.sev == "DEFECT" ? 0 : f.sev == "WARN" ? 1 : 2))
                    o.WriteLine($"  [{sev,-6}] {e.Label}: {msg}");
        o.WriteLine();
        o.WriteLine("  flag counts per palette (DEFECT/WARN/NOTE): " + string.Join("; ", order.Select(p =>
        {
            var fl = all.Where(z => z.Palette == p).SelectMany(z => z.Flags).ToList();
            return $"{p} {fl.Count(f => f.sev == "DEFECT")}/{fl.Count(f => f.sev == "WARN")}/{fl.Count(f => f.sev == "NOTE")}";
        })));
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 2) + "..";

    // =====================================================================================================
    /// <summary>Level-matched A/B files in the critic preview folder: the same fight from each palette, fight span only, equal integrated loudness.</summary>
    static List<string> BuildAB(StringWriter o, List<Entry> all)
    {
        var outDir = Path.Combine(Root, "critic");
        Directory.CreateDirectory(outDir);
        var made = new List<string>();
        o.WriteLine();
        o.WriteLine("=== J. LEVEL-MATCHED A/B FILES (fight span from the hookset, equal integrated loudness, 1.2 s silence between palettes) ===");
        var order = new[] { "current", "natural", "cozy", "tactile" };
        foreach (var (sit, name, maxLen) in new[] { ("demo_perch", "ab_perch_fight_4palettes.wav", 20.0), ("demo_bream", "ab_bream_fight_4palettes.wav", 20.0), ("demoreel_perch", "ab_perch_reel_only_3palettes.wav", 20.0) })
        {
            var rows = order.Select(p => all.FirstOrDefault(z => z.Palette == p && z.Situation == sit && z.Role is "demo" or "demoreel")).Where(z => z != null).ToList();
            if (rows.Count < 2) continue;
            int fs = rows[0].Fs;
            if (rows.Any(r => r.Fs != fs)) { o.WriteLine($"  {name}: mixed rates, skipped"); continue; }
            double len = Math.Min(maxLen, rows.Min(r => r.Dur - r.FightStart));
            var segs = new List<double[]>(); var louds = new List<double>(); var tps = new List<double>();
            foreach (var r in rows)
            {
                int s0 = Math.Max(0, (int)Math.Round((r.FightStart - 0.25) * fs));
                int n = Math.Min(r.X.Length - s0, (int)Math.Round((len + 0.25) * fs));
                var seg = new double[n]; Array.Copy(r.X, s0, seg, 0, n);
                int fi = (int)(0.01 * fs), fo = (int)(0.08 * fs);
                for (int i = 0; i < fi && i < n; i++) seg[i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / fi);
                for (int i = 0; i < fo && i < n; i++) seg[n - 1 - i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / fo);
                segs.Add(seg); louds.Add(M.Loudness(seg, fs).I); tps.Add(M.Db(M.TruePeak(seg)));
            }
            double target = -23;
            for (int i = 0; i < rows.Count; i++) target = Math.Min(target, louds[i] + (-1.5 - tps[i]));
            var outBuf = new List<double>();
            var gap = new double[(int)(1.2 * fs)];
            var parts = new List<string>();
            double t = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                double g = Math.Pow(10, (target - louds[i]) / 20);
                parts.Add($"{t:F1}-{t + segs[i].Length / (double)fs:F1} s {rows[i].Palette} (gain {M.Db(g):+0.0;-0.0} dB)");
                outBuf.AddRange(segs[i].Select(v => v * g));
                t += segs[i].Length / (double)fs;
                if (i + 1 < rows.Count) { outBuf.AddRange(gap); t += gap.Length / (double)fs; }
            }
            var path = Path.Combine(outDir, name);
            Wav.Write16(path, outBuf.ToArray(), fs);
            made.Add(path);
            o.WriteLine($"  {path}: each segment {target:F1} LUFS, {len:F1} s of fight: " + string.Join("; ", parts));
        }
        // reel situations: natural -> cozy -> tactile per situation, each excerpt 6 s, equal loudness
        {
          foreach (var (fileName, sitList) in new[] { ("ab_reel_situations_3palettes.wav", new[] { "reel_free", "reel_load", "reel_fast" }), ("ab_drag_situations_3palettes.wav", new[] { "drag_slow", "drag_fast" }) })
          {
            var pals = new[] { "natural", "cozy", "tactile" };
            var pieces = new List<(string sit, string pal, double[] seg, double lufs, double tp)>();
            foreach (var sit in sitList)
                foreach (var p in pals)
                {
                    var r = all.FirstOrDefault(z => z.Palette == p && z.Situation == sit && z.Role is "reel" or "drag");
                    if (r == null || r.Fs != 48000) continue;
                    int fs0 = r.Fs, s0 = (int)(0.5 * fs0), n = Math.Min(r.X.Length - s0, 6 * fs0);
                    var seg = new double[n]; Array.Copy(r.X, s0, seg, 0, n);
                    int fi = (int)(0.01 * fs0), fo = (int)(0.08 * fs0);
                    for (int i = 0; i < fi; i++) seg[i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / fi);
                    for (int i = 0; i < fo; i++) seg[n - 1 - i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / fo);
                    pieces.Add((sit, p, seg, M.Loudness(seg, fs0).I, M.Db(M.TruePeak(seg))));
                }
            if (pieces.Count > 0)
            {
                int fs = 48000;
                double target = -23;
                foreach (var q in pieces) target = Math.Min(target, q.lufs + (-1.5 - q.tp));
                var buf = new List<double>(); var parts = new List<string>(); double t = 0; string prevSit = null;
                foreach (var q in pieces)
                {
                    if (prevSit != null) { int g = (int)((q.sit == prevSit ? 0.8 : 2.0) * fs); buf.AddRange(new double[g]); t += g / (double)fs; }
                    double gain = Math.Pow(10, (target - q.lufs) / 20);
                    parts.Add($"{t:F1}s {q.sit}/{q.pal} ({(gain >= 1 ? "+" : "")}{M.Db(gain):F1} dB)");
                    buf.AddRange(q.seg.Select(v => v * gain)); t += q.seg.Length / (double)fs; prevSit = q.sit;
                }
                var path = Path.Combine(outDir, fileName);
                Wav.Write16(path, buf.ToArray(), fs);
                made.Add(path);
                o.WriteLine($"  {path}: each excerpt {target:F1} LUFS (6 s from 0.5 s): " + string.Join("; ", parts));
            }
          }
        }
        return made;
    }

    static void WriteCsv(string path, List<Entry> all)
    {
        var sb = new StringBuilder();
        sb.AppendLine("palette,file,role,situation,fs,sec,peak_dbfs,tp_dbtp,rms_dbfs,act_rms_dbfs,dc,fullscale,lufs_i,mmax,lra,flat_lt300,flat_300_2k,flat_2k_5k,flat_gt5k,a_2k_5k,ph_2k_5k,c_pow,c_mag,ct_mean,ct_sd,dC_pct,m25,m25_p90,harsh,harsh_ph,m2_8,m20_150,buzz_p50,buzz_frac,ticks,rate,jit_p50,jit_p90,r60,sim1,ident,rot,wave_r,wave_lag,feat_r,feat_lag,flags");
        foreach (var e in all)
        {
            var t = e.Tk;
            sb.AppendLine(string.Join(",", new[]
            {
                e.Palette, e.Rel + (e.Role.EndsWith("-fight") ? " [fight]" : ""), e.Role, e.Situation, e.Fs.ToString(), e.Dur.ToString("F3"),
                M.Db(e.Peak).ToString("F2"), M.Db(e.TP).ToString("F2"), M.Db(e.Rms).ToString("F2"), M.Db(e.ActRms).ToString("F2"), e.Dc.ToString("F6"), e.Full.ToString(),
                P(e.Loud.I, "F2"), P(e.Loud.Mmax, "F2"), P(e.Loud.Lra, "F2"),
                (100 * e.Spec.Flat[0]).ToString("F2"), (100 * e.Spec.Flat[1]).ToString("F2"), (100 * e.Spec.Flat[2]).ToString("F2"), (100 * e.Spec.Flat[3]).ToString("F2"),
                (100 * e.Spec.A[2]).ToString("F2"), (100 * e.Spec.Ph[2]).ToString("F2"), e.Spec.Cpow.ToString("F0"), e.Spec.Cmag.ToString("F0"),
                P(e.Fr.CMean, "F0"), P(e.Fr.CStd, "F0"), P(e.Fr.CDeltaMed, "F1"),
                P(e.Mod25.med, "F3"), P(e.Mod25.p90, "F3"), P(e.Harsh, "F4"), P(e.HarshPh, "F4"), P(e.Mod28.med, "F3"), P(e.ModBB20.med, "F3"),
                P(e.Fr.BuzzMed, "F2"), P(e.Fr.BuzzFrac, "F3"),
                t?.T.Count.ToString() ?? "", P(t?.Rate ?? double.NaN), P(t?.JitMed ?? double.NaN, "F2"), P(t?.JitP90 ?? double.NaN, "F2"), P(t?.R60 ?? double.NaN, "F2"),
                P(t?.SimMed ?? double.NaN, "F3"), P(t?.Ident ?? double.NaN, "F3"), t?.RotationK.ToString() ?? "",
                P(e.WRep.r, "F3"), P(e.WRep.lag, "F3"), P(e.FRep.r, "F3"), P(e.FRep.lag, "F2"),
                "\"" + string.Join(" | ", e.Flags.Select(f => f.sev + ": " + f.msg)).Replace("\"", "'") + "\""
            }));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
