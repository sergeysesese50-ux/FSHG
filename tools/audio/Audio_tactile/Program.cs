using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Fishing.Core;
using Fishing.Tests;
using Fishing.Unity;
using UnityEngine;

namespace Fishing.Unity
{
    // LakeState lives in FishingController.cs (MonoBehaviour) in the project; same values here.
    public enum LakeState { Idle, Casting, Waiting, Bite, Fight, Caught, Lost }
}

internal static class Program
{
    const int SR = TactileSynth.SampleRate;
    const int OR = TactileMixer.OutRate;
    const float Master = 0.8f;
    static string OutDir;
    static readonly List<string> Files = new List<string>();
    static Dictionary<TactileSound, float[]> Clips;
    static float[] PurrClip, HumClip, AmbClip;
    static System.Text.StringBuilder Log = new System.Text.StringBuilder();

    static void P(string s = "") { Console.WriteLine(s); Log.AppendLine(s); }

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        OutDir = args.Length > 0 ? args[0] : RepoPaths.Previews("tactile");
        Directory.CreateDirectory(OutDir);
        foreach (var old in Directory.GetFiles(OutDir, "*.wav")) File.Delete(old);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        BuildClips();
        P($"clip generation: {sw.ElapsedMilliseconds} ms (22050 Hz loops + one-shots, 64 s ambience) ");
        sw.Restart();
        var bank = new TactileGrainBank(OR);
        P($"grain bank @48k: {sw.ElapsedMilliseconds} ms, {bank.Detent.Sum(a => a.Length) + bank.DragBright.Sum(a => a.Length) + bank.DragDark.Sum(a => a.Length) + bank.Strain.Sum(a => a.Length) + bank.CreakSoft.Sum(a => a.Length) + bank.CreakHard.Sum(a => a.Length) + bank.Rotor.Sum(a => a.Length)} samples");

        GrainReport(bank);
        LoopReport();
        Scenes();
        OneShots();
        GrainSheets(bank);
        P();
        Demo("90_fight_perch300_HoldReel", FightKit.Perch, 300, FightKit.FloatTackle, FightKit.FloatTackle().AutoDragG, 1500, d => FightBots.HoldReel(d), 3, 26);
        Demo("91_fight_bream2kg_Technique", FightKit.Bream, 2000, FightKit.FloatTackle, FightKit.FloatTackle().AutoDragG, 1500, d => FightBots.Technique(d), 3, 25.5);
        P();
        P("FILES:");
        foreach (var f in Files) P("  " + f);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out.txt"), Log.ToString());
        return 0;
    }

    // =====================================================================================
    // Clips
    // =====================================================================================

    static void BuildClips()
    {
        PurrClip = TactileSynth.SpoolPurr();
        HumClip = TactileSynth.LineHum();
        var swA = System.Diagnostics.Stopwatch.StartNew();
        AmbClip = TactileSynth.Ambience(64.0);
        P($"ambience 64 s generation: {swA.ElapsedMilliseconds} ms");
        Clips = new Dictionary<TactileSound, float[]>
        {
            [TactileSound.CastWhoosh] = TactileSynth.CastWhoosh(),
            [TactileSound.FloatPlop] = TactileSynth.FloatPlop(),
            [TactileSound.BiteBob] = TactileSynth.BiteBob(),
            [TactileSound.Hookset] = TactileSynth.Hookset(),
            [TactileSound.TeleRun] = TactileSynth.TeleRun(),
            [TactileSound.TeleDive] = TactileSynth.TeleDive(),
            [TactileSound.TeleSurge] = TactileSynth.TeleSurge(),
            [TactileSound.TeleJerk] = TactileSynth.TeleJerk(),
            [TactileSound.TeleJump] = TactileSynth.TeleJump(),
            [TactileSound.SplashSmall] = TactileSynth.Splash(0),
            [TactileSound.SplashBig] = TactileSynth.Splash(1),
            [TactileSound.SplashJump] = TactileSynth.Splash(2),
            [TactileSound.LineThrum] = TactileSynth.LineThrum(),
            [TactileSound.FishFlop] = TactileSynth.FishFlop(),
            [TactileSound.ChimeSmall] = TactileSynth.Chime(0),
            [TactileSound.Chime] = TactileSynth.Chime(1),
            [TactileSound.ChimeBig] = TactileSynth.Chime(2),
            [TactileSound.LineSnap] = TactileSynth.LineSnap(),
            [TactileSound.RodCrack] = TactileSynth.RodCrack(),
            [TactileSound.Escape] = TactileSynth.Escape(),
        };
        // Determinism: a second build must be bit-identical.
        var again = TactileSynth.Hookset();
        bool same = again.SequenceEqual(Clips[TactileSound.Hookset]) && TactileSynth.SpoolPurr().SequenceEqual(PurrClip);
        P($"determinism (rebuild identical): {same}");
    }

    static TactileMixer NewMixer(double seconds) => new TactileMixer((int)((seconds + 5) * OR), Clips, PurrClip, HumClip, AmbClip);

    // =====================================================================================
    // Measurements
    // =====================================================================================

    sealed class Meas
    {
        public double Sec, PeakDb, RmsDb, Lufs, Dc, Cent, H25, H25A, H25Ph, Above5, Below300;
    }

    static Meas Measure(float[] x, int fs)
    {
        var m = new Meas { Sec = (double)x.Length / fs, PeakDb = Dsp.Db(Dsp.Peak(x)), RmsDb = Dsp.Db(Dsp.Rms(x)), Dc = Dsp.Mean(x), Lufs = Lufs(x, fs) };
        Dsp.Spectrum sp;
        int seg = fs >= 44100 ? 8192 : 4096;
        if (x.Length >= seg * 2) sp = Dsp.Bands(Dsp.Welch(x, seg), (double)fs / seg);
        else
        {
            int pad = 1; while (pad < Math.Max(x.Length, 8192)) pad <<= 1;
            sp = Dsp.Bands(Dsp.Power(x, pad), (double)fs / pad);
        }
        m.Cent = sp.CentroidHz; m.H25 = 100 * sp.Flat[2]; m.H25A = 100 * sp.A[2]; m.H25Ph = 100 * sp.PhoneA[2];
        m.Above5 = 100 * sp.Flat[3]; m.Below300 = 100 * sp.Flat[0];
        return m;
    }

    static string Head() => $"{"file",-34}{"sec",6}{"peak",7}{"rms",7}{"LUFS~",7}{"DC",9}{"cent",7}{"<300",6}{"2-5k",6}{"2-5kA",6}{"phA",6}{">5k",6}";
    static string Row(string name, Meas m) =>
        $"{name,-34}{m.Sec,6:0.00}{m.PeakDb,7:0.0}{m.RmsDb,7:0.0}{(double.IsNaN(m.Lufs) ? "  n/a" : m.Lufs.ToString("0.0")),7}{m.Dc,9:0.00000}{m.Cent,7:0}{m.Below300,6:0.0}{m.H25,6:0.0}{m.H25A,6:0.0}{m.H25Ph,6:0.0}{m.Above5,6:0.0}";

    /// <summary>BS.1770-style integrated loudness (K-weighting designed for fs, 400 ms blocks, 75% overlap, gates −70/−10).</summary>
    static double Lufs(float[] x, int fs)
    {
        var y = KWeight(x, fs);
        int block = (int)(0.4 * fs), hop = (int)(0.1 * fs);
        var z = new List<double>();
        if (x.Length < block)
        {
            double e = y.Sum(v => v * v) / Math.Max(1, y.Length);
            return -0.691 + 10 * Math.Log10(Math.Max(e, 1e-20));
        }
        for (int s = 0; s + block <= y.Length; s += hop)
        {
            double e = 0;
            for (int i = s; i < s + block; i++) e += y[i] * y[i];
            z.Add(e / block);
        }
        var g1 = z.Where(v => -0.691 + 10 * Math.Log10(Math.Max(v, 1e-20)) > -70).ToList();
        if (g1.Count == 0) return double.NaN;
        double l1 = -0.691 + 10 * Math.Log10(g1.Average());
        var g2 = g1.Where(v => -0.691 + 10 * Math.Log10(Math.Max(v, 1e-20)) > l1 - 10).ToList();
        return -0.691 + 10 * Math.Log10(g2.Average());
    }

    static double[] KWeight(float[] x, int fs)
    {
        // pyloudnorm-style design of the two K-weighting stages for any fs.
        double G = 3.99984385397, Q1 = 0.7071752369554193, f1 = 1681.9744509555319;
        double A = Math.Pow(10, G / 40), w = 2 * Math.PI * f1 / fs, al = Math.Sin(w) / (2 * Q1), c = Math.Cos(w);
        double[] b1 = { A * ((A + 1) + (A - 1) * c + 2 * Math.Sqrt(A) * al), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - 2 * Math.Sqrt(A) * al) };
        double[] a1 = { (A + 1) - (A - 1) * c + 2 * Math.Sqrt(A) * al, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - 2 * Math.Sqrt(A) * al };
        double f2 = 38.13547087613982, Q2 = 0.5003270373253953;
        w = 2 * Math.PI * f2 / fs; al = Math.Sin(w) / (2 * Q2); c = Math.Cos(w);
        double[] b2 = { (1 + c) / 2, -(1 + c), (1 + c) / 2 };
        double[] a2 = { 1 + al, -2 * c, 1 - al };
        var y = Biquad(x.Select(v => (double)v).ToArray(), b1, a1);
        return Biquad(y, b2, a2);
    }

    static double[] Biquad(double[] x, double[] b, double[] a)
    {
        var y = new double[x.Length];
        double z1 = 0, z2 = 0;
        double b0 = b[0] / a[0], bb1 = b[1] / a[0], bb2 = b[2] / a[0], aa1 = a[1] / a[0], aa2 = a[2] / a[0];
        for (int i = 0; i < x.Length; i++)
        {
            double o = b0 * x[i] + z1;
            z1 = bb1 * x[i] - aa1 * o + z2;
            z2 = bb2 * x[i] - aa2 * o;
            y[i] = o;
        }
        return y;
    }

    /// <summary>Envelope AM: |x| through 2× one-pole 400 Hz, decimated to 1 kHz; returns max single-bin depth in 2–8 Hz and 20–150 Hz.</summary>
    static (double d2_8, double f2_8, double d20_150, double f20_150) AmDepth(float[] x, int fs)
    {
        int dec = fs / 1000;
        double a = 1 - Math.Exp(-2 * Math.PI * 400.0 / fs), y1 = 0, y2 = 0;
        // circular warm-up
        for (int i = 0; i < x.Length; i++) { y1 += a * (Math.Abs(x[i]) - y1); y2 += a * (y1 - y2); }
        var env = new List<double>();
        for (int i = 0; i < x.Length; i++)
        {
            y1 += a * (Math.Abs(x[i]) - y1); y2 += a * (y1 - y2);
            if (i % dec == 0) env.Add(y2);
        }
        int n = env.Count;
        var (re, im) = Dsp.Dft(env.ToArray());
        double dc = re[0] / n;
        double best1 = 0, f1 = 0, best2 = 0, f2 = 0;
        double binHz = (double)fs / dec / n;
        for (int k = 1; k <= n / 2; k++)
        {
            double f = k * binHz;
            double d = 2 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / n / Math.Max(dc, 1e-12);
            if (f >= 2 && f <= 8 && d > best1) { best1 = d; f1 = f; }
            if (f >= 20 && f <= 150 && d > best2) { best2 = d; f2 = f; }
        }
        return (best1, f1, best2, f2);
    }

    // =====================================================================================
    // Export
    // =====================================================================================

    /// <summary>Gain to −20 LUFS-ish (or `target`), capped so the peak stays ≤ −3 dBFS. Returns the applied gain in dB.</summary>
    static double Export(string name, float[] x, int fs, double target = -20, bool measure = true)
    {
        double l = Lufs(x, fs), peak = Dsp.Peak(x);
        double g = double.IsNaN(l) ? 0 : target - l;
        g = Math.Min(g, -3 - Dsp.Db(peak));
        float k = (float)Math.Pow(10, g / 20);
        var y = x.Select(v => v * k).ToArray();
        int clip = y.Count(v => Math.Abs(v) >= 0.9999f);
        string path = Path.Combine(OutDir, name + ".wav");
        Dsp.WriteWav(path, y, fs);
        Files.Add(path);
        if (measure) P(Row(name, Measure(y, fs)) + $"  gain {g,6:+0.0;-0.0} dB{(clip > 0 ? $"  CLIPPED {clip}" : "")}");
        return g;
    }

    // =====================================================================================
    // 1. Grains
    // =====================================================================================

    static void GrainReport(TactileGrainBank bank)
    {
        P();
        P("=== GRAINS @48k (per-variant mean; centroid flat; 2-5k share flat / A-weighted / phone-A) ===");
        void G(string name, float[][] set)
        {
            var ms = set.Select(g => Measure(g, OR)).ToList();
            double tan = 0;
            P($"  {name,-12} n={set.Length,2} len {1000.0 * set.Average(g => g.Length) / OR,5:0} ms  centroid {ms.Average(m => m.Cent),5:0} Hz (min {ms.Min(m => m.Cent):0}, max {ms.Max(m => m.Cent):0})  2-5k {ms.Average(m => m.H25),4:0.0}% / A {ms.Average(m => m.H25A),4:0.0}% / phA {ms.Average(m => m.H25Ph),4:0.0}%  >5k {ms.Average(m => m.Above5),4:0.0}%  <300 {ms.Average(m => m.Below300),4:0.0}%  DC max {ms.Max(m => Math.Abs(m.Dc)):0.00000}");
            // spread between variants: spectral distance via centroid & RMS
            var rms = set.Select(g => Dsp.Db(Dsp.MaxShortRms(g, OR / 100))).ToList();
            P($"               variant spread: centroid sd {Sd(ms.Select(m => m.Cent)),4:0} Hz, 10ms-RMS sd {Sd(rms):0.00} dB; first-sample |x| max {set.Max(g => Math.Abs(g[0])):0.0000}, last {set.Max(g => Math.Abs(g[g.Length - 1])):0.0000}; max 1-sample step / peak {set.Max(g => MaxStep(g) / Dsp.Peak(g)):0.000}");
        }
        G("detent", bank.Detent);
        G("drag bright", bank.DragBright);
        G("drag dark", bank.DragDark);
        G("strain", bank.Strain);
        G("creak soft", bank.CreakSoft);
        G("creak hard", bank.CreakHard);
        G("rotor swish", bank.Rotor);
        P("  tanφ = 1/(π f τ) of repeated modes: detent 620 Hz/13 ms " + Tan(620, 0.013) + ", 1040/8 ms " + Tan(1040, 0.008) + ", 1560/3.4 ms " + Tan(1560, 0.0034)
          + "; drag 1250/3.8 ms " + Tan(1250, 0.0038) + ", 560/5 ms " + Tan(560, 0.005) + "  (metal < 0.005)");
    }

    static string Tan(double f, double tau) => (1 / (Math.PI * f * tau)).ToString("0.000");
    static double Sd(IEnumerable<double> v) { var a = v.ToList(); double m = a.Average(); return Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / a.Count); }
    static double MaxStep(float[] g) { double m = 0; for (int i = 1; i < g.Length; i++) m = Math.Max(m, Math.Abs(g[i] - g[i - 1])); return m; }

    // =====================================================================================
    // 2. Loops
    // =====================================================================================

    static void LoopReport()
    {
        P();
        P("=== LOOPS (22050 Hz) — seam = wrap step / curvature as percentile within the loop's own samples; AM = max single-bin envelope depth ===");
        foreach (var (name, clip) in new[] { ("spool purr", PurrClip), ("line hum", HumClip), ("ambience", AmbClip) })
        {
            var (step, sr, cr) = Dsp.Seam(clip);
            var m = Measure(clip, SR);
            string am = "";
            if (clip.Length < SR * 10)
            {
                var a = AmDepth(clip, SR);
                am = $" AM 2-8 Hz {100 * a.d2_8:0.0}% @{a.f2_8:0.0}, 20-150 Hz {100 * a.d20_150:0.0}% @{a.f20_150:0}";
            }
            // RMS continuity across the seam: 20 ms windows straddling the wrap vs the loop's 20 ms RMS distribution.
            int w = SR / 50;
            var seamWin = new float[w];
            for (int i = 0; i < w; i++) seamWin[i] = clip[(clip.Length - w / 2 + i) % clip.Length];
            double seamRms = Dsp.Rms(seamWin);
            var wins = new List<double>();
            for (int s = 0; s + w <= clip.Length; s += w / 2) wins.Add(Dsp.Rms(clip.Skip(s).Take(w).ToArray()));
            double rank = 100.0 * wins.Count(v => v < seamRms) / wins.Count;
            P($"  {name,-11} {clip.Length / (double)SR,5:0.00} s  peak {m.PeakDb,5:0.0} dBFS  DC {m.Dc:0.00000}  centroid {m.Cent,5:0} Hz  2-5k {m.H25:0.0}% (A {m.H25A:0.0}%)  seam step {step:0.0000} = pct {sr:0.0}, curvature pct {cr:0.0}, 20ms-RMS at seam = pct {rank:0}{am}");
        }
    }

    // =====================================================================================
    // 3. Reel/drag/line/rod scenes through the real runtime (TactileCues.Map → TactileGrains + loops)
    // =====================================================================================

    static void Scenes()
    {
        P();
        P("=== SCENES (rendered through TactileCues.Map + grain engine + loops @48k, master 0.8, inputs held at 20 Hz like FightModel) ===");
        P(Head());
        const float c0 = 110f; // float tackle
        ReelInputs In(bool crank, float load, float slip = 0, float share = 0.4f, float rod = 0.3f, float slack = 0, bool tight = true)
            => new ReelInputs
            {
                Fight = true, C0 = c0, ReelLoad = crank ? load : 0, ReelInCmPerSec = crank ? c0 * (1 - load) : 0,
                IsDragSlipping = slip > 0, SlipCmPerSec = slip, IsCrankingIntoSlip = crank && slip > 0,
                LoadShare = share, SlackCm = tight ? 0 : 100, RodLoadShare = rod,
            };

        Scene("01_reel_idle", 7, t => In(t < 3.0 || t > 3.6, 0.0f, share: 0.05f, tight: false));
        Scene("02_reel_load", 8, t => In(true, (float)(0.62 + 0.33 * Math.Sin(Math.PI * Math.Min(1, t / 6.0))), share: 0.65f));
        Scene("03_reel_gaining", 7, t => In(true, (float)(0.18 + 0.06 * Math.Sin(2 * Math.PI * t / 2.3)), share: 0.45f));
        Scene("04_reel_pump_start_stop", 8, t => In((t % 1.25) < 0.7, 0.45f, share: 0.5f));
        Scene("05_drag_slow", 7, t => In(false, 0, slip: (float)(4 + 8 * t / 7), share: 0.6f), t => t < 0.02);
        Scene("06_drag_fast", 8, t => In(false, 0, slip: (float)(t < 4 ? 25 + 75 * t / 4 : 100 - 15 * (t - 4)), share: 0.75f), t => t < 0.02);
        Scene("07_line_tension", 9, t => In(false, 0, share: (float)(0.6 + 0.4 * Math.Sin(Math.PI * Math.Min(1, t / 7.0)))));
        Scene("08_rod_creak", 8, t => In(false, 0, rod: (float)(0.76 + 0.24 * Math.Min(1, t / 6.0))));
    }

    static void Scene(string name, double seconds, Func<double, ReelInputs> f, Func<double, bool> breakaway = null)
    {
        var cues = new TactileCues();
        var mix = NewMixer(seconds + 1);
        float dt = 1f / 60f;
        double t = 0, held = -1;
        ReelInputs x = default;
        int fires = 0;
        var kinds = new int[5];
        mix.Engine.OnFire = (k, v, g, p, s) => { fires++; kinds[(int)k]++; };
        while (t < seconds)
        {
            if (t - held >= 0.05 || held < 0) { x = f(t); held = t - (t % 0.05); }
            cues.Map(x);
            if (breakaway != null && breakaway(t)) mix.Engine.Trigger(GrainKind.DragAccent);
            mix.Frame(cues, dt, Master);
            t += dt;
        }
        var off = new ReelInputs { Fight = false };
        for (int i = 0; i < 50; i++) { cues.Map(off); mix.Frame(cues, dt, Master); }
        var m = mix.Mix(0, 1, 2);
        double g = Export(name, m, OR);
        double lg = Lufs(mix.Stems[0].ToArray(), OR), lp = Lufs(mix.Stems[1].ToArray(), OR), lh = Lufs(mix.Stems[2].ToArray(), OR);
        P($"{"",34}layer loudness at game level: grains {lg:0.0}, purr {lp:0.0}, hum {lh:0.0} LUFS~ (peak grains {Dsp.Db(Dsp.Peak(mix.Stems[0].ToArray())):0.0} dBFS)");
        P($"{"",34}grains: detent {kinds[0]}, rotor {kinds[4]}, drag {kinds[1]}, strain {kinds[2]}, creak {kinds[3]}; peak voices {mix.Engine.PeakVoices}; loop starts purr {mix.Purr.Starts}, hum {mix.Hum.Starts}");
    }

    // =====================================================================================
    // 4. One-shots
    // =====================================================================================

    static void OneShots()
    {
        P();
        P("=== ONE-SHOTS (22050 Hz clips as generated; exported at −20 LUFS-ish capped to −3 dBFS peak) ===");
        P(Head());
        var names = new (TactileSound s, string f)[]
        {
            (TactileSound.Hookset, "09_hookset"), (TactileSound.TeleRun, "10_tele_run"), (TactileSound.TeleDive, "11_tele_dive"),
            (TactileSound.TeleSurge, "12_tele_surge"), (TactileSound.TeleJerk, "13_tele_jerk"), (TactileSound.TeleJump, "14_tele_jump"),
            (TactileSound.SplashSmall, "15_splash_small"), (TactileSound.SplashBig, "16_splash_big"), (TactileSound.SplashJump, "17_splash_jump"),
            (TactileSound.LineThrum, "18_line_thrum_shock"), (TactileSound.FishFlop, "19_fish_flop"), (TactileSound.ChimeSmall, "20_chime_small"),
            (TactileSound.Chime, "21_chime"), (TactileSound.ChimeBig, "22_chime_big"), (TactileSound.LineSnap, "23_line_snap"),
            (TactileSound.RodCrack, "24_rod_crack"), (TactileSound.Escape, "25_escape"), (TactileSound.CastWhoosh, "26_cast_whoosh"),
            (TactileSound.FloatPlop, "27_float_plop"), (TactileSound.BiteBob, "28_bite_bob"),
        };
        foreach (var (s, f) in names)
        {
            var c = Clips[s];
            var raw = Measure(c, SR);
            var padded = new float[c.Length + SR / 4];
            Array.Copy(c, 0, padded, SR / 20, Math.Min(c.Length, padded.Length - SR / 20));
            Export(f, padded, SR);
        }
        P();
        P("Ambience loop (exported at its exact length so it can be looped; −26 LUFS-ish for a calm preview):");
        Export("30_ambience_morning_64s", AmbClip, SR, -26);
    }

    static void GrainSheets(TactileGrainBank bank)
    {
        P();
        P("Grain sheets (every variant in a row, 48 kHz):");
        float[] Sheet(float[][] set, double every, params float[][][] more)
        {
            var all = set.Concat(more.SelectMany(m => m)).ToList();
            var o = new float[(int)((all.Count * every + 0.6) * OR)];
            for (int k = 0; k < all.Count; k++)
            {
                int at = (int)((0.1 + k * every) * OR);
                for (int i = 0; i < all[k].Length && at + i < o.Length; i++) o[at + i] += all[k][i] * 0.5f;
            }
            return o;
        }
        Export("40_grains_detent_16", Sheet(bank.Detent, 0.25), OR);
        Export("41_grains_drag_16bright_16dark", Sheet(bank.DragBright, 0.2, bank.DragDark), OR);
        Export("42_grains_strain_12", Sheet(bank.Strain, 0.3), OR);
        Export("43_grains_rodcreak_4soft_4hard", Sheet(bank.CreakSoft, 0.7, bank.CreakHard), OR);
    }

    // =====================================================================================
    // 5. Fight demos: FightModel → FightFrame → TactileCues → TactileMixer (6 sources) → 48 kHz
    // =====================================================================================

    sealed class Rec
    {
        public float T; public LakeState State; public bool Cranking, Slipping; public float ReelLoad, Slip, Share, Rod, DetentRate;
        public FishPhase Phase; public int S0, S1;
    }

    static void Demo(string name, Func<FishSpecies> species, int weight, Func<FightTackle> makeTackle, int drag, double start,
        Func<int, FightBot> makeBot, uint seed, double maxSeconds)
    {
        const double tick = 0.05;
        const float dt = 1f / 60f;
        var tackle = makeTackle();
        var model = new FightModel(species(), weight, tackle, start, new SeededRandom(seed), null, 0, 1.0, seed);
        var bot = makeBot(drag);
        var frame = new FightFrame();
        var cues = new TactileCues();
        var mix = NewMixer(maxSeconds + 6);
        var recs = new List<Rec>();
        var fires = new List<(GrainKind k, int v, float g, float p, long s)>();
        mix.Engine.OnFire = (k, v, g, p, s) => fires.Add((k, v, g, p, s));
        float t = 0f;

        void Do(LakeState state)
        {
            t += dt;
            cues.Update(state, frame, tackle, 15f, dt);
            int s0 = mix.Stems[0].Count;
            mix.Frame(cues, dt, Master);
            recs.Add(new Rec
            {
                T = t, State = state, Phase = frame.Phase,
                Cranking = state == LakeState.Fight && (frame.ReelInCmPerSec > 0.5f || frame.IsCrankingIntoSlip || frame.ReelLoad > 0.001f),
                Slipping = state == LakeState.Fight && frame.IsDragSlipping, ReelLoad = frame.ReelLoad, Slip = frame.SlipCmPerSec,
                Share = frame.LoadShare, Rod = frame.RodLoadShare, DetentRate = cues.DetentRate, S0 = s0, S1 = mix.Stems[0].Count,
            });
        }

        for (int f = 0; f < 60; f++) { frame.BeginFrame(); Do(LakeState.Idle); }
        for (int f = 0; f < 54; f++) { frame.BeginFrame(); Do(LakeState.Casting); }
        for (int f = 0; f < 120; f++) { frame.BeginFrame(); Do(LakeState.Waiting); }
        for (int f = 0; f < 40; f++) { frame.BeginFrame(); Do(LakeState.Bite); }
        frame.Start(model, tackle, new Vector3(0.2f, 0, 1f), tick);
        frame.BeginFrame();
        Do(LakeState.Fight);
        float fightStart = t;
        double acc = 0;
        bool ended = false;
        var endState = LakeState.Fight;
        while (!ended && t < maxSeconds)
        {
            frame.BeginFrame();
            acc += dt;
            int steps = 0;
            while (acc >= tick && steps < 10 && model.Outcome == FightOutcome.InProgress)
            {
                model.Step(bot(model));
                frame.AddTick(model);
                acc -= tick; steps++;
            }
            var state = LakeState.Fight;
            if (model.Outcome == FightOutcome.InProgress) frame.Compose((float)(acc / tick));
            else
            {
                frame.End(); ended = true;
                state = model.Outcome == FightOutcome.Landed ? LakeState.Caught : LakeState.Lost;
                endState = state;
            }
            Do(state);
        }
        double fightSec = model.ElapsedSeconds;
        float fightEnd = t;
        for (int f = 0; f < (ended ? 210 : 30); f++) { frame.BeginFrame(); Do(ended ? endState : LakeState.Fight); }

        var full = mix.Mix();
        {
            // 0.5 s fade so the file does not stop dead (fight still in progress, or ambience still running).
            int n = OR / 2;
            for (int i = 0; i < n; i++) full[full.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / n));
        }
        var reel = mix.Mix(0, 1, 2);

        P();
        P($"=== DEMO {name}: {weight} g, seed {seed}; outcome {model.Outcome} after {fightSec:0.0} s of fight; file {full.Length / (double)OR:0.0} s @48k (idle 1 s, cast 0.9 s, wait 2 s, bite 0.67 s, fight, tail) ===");
        double raw = Lufs(full, OR);
        P($"  raw mix (game levels, master 0.8): LUFS~ {raw:0.0}, peak {Dsp.Db(Dsp.Peak(full)):0.0} dBFS, samples ≥ 1.0: {full.Count(v => Math.Abs(v) >= 1f)}");
        P(Head());
        double g = Export(name, full, OR);
        float k = (float)Math.Pow(10, g / 20);
        Export(name + "_reel_only", reel.Select(v => v * k).ToArray(), OR, 0, true);

        var fr = recs.Where(r => r.State == LakeState.Fight).ToList();
        int crank = fr.Count(r => r.Cranking), slip = fr.Count(r => r.Slipping);
        P($"  fight frames {fr.Count}: cranking {100.0 * crank / fr.Count:0}% (ReelLoad p50 {Pct(fr.Where(r => r.Cranking).Select(r => r.ReelLoad), 50):0.00}, detent rate p10/p50/p90 {Pct(fr.Where(r => r.Cranking).Select(r => r.DetentRate), 10):0.0}/{Pct(fr.Where(r => r.Cranking).Select(r => r.DetentRate), 50):0.0}/{Pct(fr.Where(r => r.Cranking).Select(r => r.DetentRate), 90):0.0} /s), slipping {100.0 * slip / fr.Count:0}% (slip p50/p90/max {Pct(fr.Where(r => r.Slipping).Select(r => r.Slip), 50):0}/{Pct(fr.Where(r => r.Slipping).Select(r => r.Slip), 90):0}/{(slip > 0 ? fr.Where(r => r.Slipping).Max(r => r.Slip) : 0):0} cm/s), LoadShare p90 {Pct(fr.Select(r => r.Share), 90):0.00}, RodLoadShare max {fr.Max(r => r.Rod):0.00}");
        P("  one-shots: " + string.Join(" ", mix.Played.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}")));

        // Grain statistics.
        foreach (GrainKind kind in new[] { GrainKind.Detent, GrainKind.Rotor, GrainKind.Drag, GrainKind.Strain, GrainKind.Creak })
        {
            var ev = fires.Where(e => e.k == kind).ToList();
            if (ev.Count == 0) { P($"  {kind,-7}: none"); continue; }
            int repeats = 0;
            for (int i = 1; i < ev.Count; i++) if (ev[i].v == ev[i - 1].v) repeats++;
            int distinct = ev.Select(e => e.v).Distinct().Count();
            string extra = "";
            if (kind == GrainKind.Drag) extra = $", dark {100.0 * ev.Count(e => e.v >= 100) / ev.Count:0}%";
            // Rhythm regularity: consecutive IOI ratio while the rate is steady (both IOIs < 0.3 s and within ±15%).
            var ioi = new List<double>();
            for (int i = 1; i < ev.Count; i++) ioi.Add((ev[i].s - ev[i - 1].s) / (double)OR);
            var ratios = new List<double>(); var ratiosQ = new List<double>();
            for (int i = 1; i < ioi.Count; i++)
                if (ioi[i] < 0.3 && ioi[i - 1] < 0.3 && Math.Abs(Math.Log(ioi[i] / ioi[i - 1])) < 0.15)
                    ratios.Add(Math.Abs(Math.Log(ioi[i] / ioi[i - 1])));
            // The same events fired at frame boundaries (PlayOneShot fallback, 60 fps).
            var q = ev.Select(e => Math.Ceiling(e.s / (OR / 60.0)) * (OR / 60.0)).ToList();
            for (int i = 2; i < q.Count; i++)
            {
                double a = (ev[i - 1].s - ev[i - 2].s) / (double)OR, b = (ev[i].s - ev[i - 1].s) / (double)OR;
                if (a < 0.3 && b < 0.3 && Math.Abs(Math.Log(b / a)) < 0.15)
                {
                    double qa = q[i - 1] - q[i - 2], qb = q[i] - q[i - 1];
                    if (qa > 0 && qb > 0) ratiosQ.Add(Math.Abs(Math.Log(qb / qa)));
                }
            }
            string rhythm = ratios.Count > 10 ? $"; steady IOI change p50/p90 {100 * Pct(ratios, 50):0.0}/{100 * Pct(ratios, 90):0.0}% (frame-quantised fallback would be {100 * Pct(ratiosQ, 50):0.0}/{100 * Pct(ratiosQ, 90):0.0}%)" : "";
            P($"  {kind,-7}: {ev.Count} grains ({ev.Count / (fightEnd - fightStart):0.0}/s of fight), variants used {distinct}, same-variant-twice {repeats}{extra}, gain spread sd {Sd(ev.Select(e => Dsp.Db(e.g))):0.0} dB{rhythm}");
        }
        P($"  peak simultaneous grain voices {mix.Engine.PeakVoices} of {TactileGrains.MaxVoices}; loop starts: purr {mix.Purr.Starts}, hum {mix.Hum.Starts}");

        // Layer levels over the fight span.
        int a0 = recs.First(r => r.State == LakeState.Fight).S0, a1 = recs.Where(r => r.State == LakeState.Fight).Last().S1;
        float[] Span(float[] x) => x.Skip(a0).Take(a1 - a0).Select(v => v * k).ToArray();
        var reelSpan = Span(reel);
        var ambSpan = Span(mix.Stems[3].ToArray());
        var fxSpan = Span(mix.Mix(4, 5));
        var mixSpan = Span(full);
        double lr = Lufs(reelSpan, OR), la = Lufs(ambSpan, OR), lf = Lufs(fxSpan, OR), lm = Lufs(mixSpan, OR);
        P($"  fight span loudness (exported gain): mix {lm:0.0}, reel layers {lr:0.0}, events {lf:0.0}, ambience {la:0.0} LUFS~  → ambience {lr - la:0.0} dB under the reel");
        var ms = Measure(mixSpan, OR); var mr = Measure(reelSpan, OR);
        P($"  fight span spectrum: mix centroid {ms.Cent:0} Hz, 2-5 kHz {ms.H25:0.0}% flat / {ms.H25A:0.0}% A / {ms.H25Ph:0.0}% phone-A, >5k {ms.Above5:0.0}%;  reel layers centroid {mr.Cent:0} Hz, 2-5 kHz {mr.H25:0.0}% / {mr.H25A:0.0}% / {mr.H25Ph:0.0}%");
        // Kind stems shares within the reel layer.
        double tot = 0; var e = new double[5];
        for (int kk = 0; kk < 5; kk++) { e[kk] = mix.KindStems[kk].Take(full.Length).Sum(v => (double)v * v); tot += e[kk]; }
        P($"  grain energy split: detent {100 * e[0] / tot:0.0}%, rotor {100 * e[4] / tot:0.0}%, drag {100 * e[1] / tot:0.0}%, strain {100 * e[2] / tot:0.0}%, creak {100 * e[3] / tot:0.0}%");
        // Per-second loudness profile (short-term), to see the shape.
        var prof = new List<string>();
        for (int s = 0; s + OR <= full.Length; s += OR)
        {
            var seg = full.Skip(s).Take(OR).Select(v => v * k).ToArray();
            prof.Add($"{Dsp.Db(Dsp.Rms(seg)):0}");
        }
        P("  RMS per second (dBFS): " + string.Join(" ", prof));
        var stemPk = new List<string>();
        for (int si = 0; si < 6; si++)
        {
            var st = mix.Stems[si];
            int at = 0; float pk = 0f;
            for (int i = 0; i < st.Count; i++) if (Math.Abs(st[i]) > pk) { pk = Math.Abs(st[i]); at = i; }
            stemPk.Add($"{TactileMixer.StemNames[si]} {Dsp.Db(pk * k):0.0} dBFS @{(double)at / OR:0.0}s");
        }
        P("  stem peaks (exported gain): " + string.Join(", ", stemPk));
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, name + "_frames.csv"),
            new[] { "t,state,phase,crank,slip,reelLoad,slipCmS,loadShare,rodShare,detentRate" }.Concat(recs.Select(r =>
                $"{r.T:0.000},{r.State},{r.Phase},{(r.Cranking ? 1 : 0)},{(r.Slipping ? 1 : 0)},{r.ReelLoad:0.000},{r.Slip:0.0},{r.Share:0.000},{r.Rod:0.000},{r.DetentRate:0.00}")));
    }

    static double Pct(IEnumerable<double> v, double p)
    {
        var a = v.OrderBy(x => x).ToList();
        if (a.Count == 0) return 0;
        return a[Math.Min(a.Count - 1, (int)(a.Count * p / 100.0))];
    }

    static double Pct(IEnumerable<float> v, double p) => Pct(v.Select(x => (double)x), p);
}
