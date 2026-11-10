using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Fishing.Core;
using Fishing.Tests;
using Fishing.Unity;
using UnityEngine;

// "Cozy ASMR" palette: renders every sound, the ambience and two FightModel-driven demos to F:\fishing\.previews\audio\cozy,
// and measures them. Usage: dotnet run -c Release [-- search]
internal static class Program
{
    const int SR = CozySynth.SampleRate;
    const int OR = CozyPlayer.OutRate;
    const float Master = 0.8f; // FightFeedback.DefaultMasterVolume
    const float Dt = 1f / 60f;
    static string OutDir = RepoPaths.Previews("cozy");
    static CozyBank Bank;
    static readonly StringBuilder Rep = new StringBuilder();
    static readonly List<string> Files = new List<string>();

    static void Log(string s = "") { Console.WriteLine(s); Rep.AppendLine(s); }

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length > 0 && args[0] == "search") { Search(); return 0; }
        if (args.Length > 0) OutDir = args[0];
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(Path.Combine(OutDir, "parts"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Bank = CozyBank.Build();
        sw.Stop();
        Log($"=== BANK: generated in {sw.Elapsed.TotalMilliseconds:0} ms on this PC (first call includes JIT) ===");
        long total = 0;
        foreach (var (what, ms, n) in Bank.Timing) { Log($"  {what,-28} {ms,7:0} ms  {n,9} samples ({n / (double)SR:0.0} s, {n * 4 / 1048576.0:0.0} MB float)"); total += n; }
        Log($"  total {total} samples = {total / (double)SR:0.0} s of audio, {total * 4 / 1048576.0:0.0} MB as float (x0.5 as 16-bit)");

        Clips();
        Parts();
        Scenarios();
        OneShots();
        Ambience();
        Demos();

        Log();
        Log("FILES:");
        foreach (var f in Files) Log("  " + f);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out.txt"), Rep.ToString());
        return 0;
    }

    // =====================================================================================
    // Clip table (raw clips, 22050 Hz, before any source volume)
    // =====================================================================================

    static void Clips()
    {
        Log();
        Log("=== CLIPS (raw, 22050 Hz). cent = spectral centroid; bands = % energy <300 | 300-2k | 2-5k | >5k (FLAT / A-weighted / PHONE-A) ===");
        Log($"{"clip",-16}{"n",4}{"ms",6}{"peak",7}{"rms",7}{"DC",9}{"cent",6}{"centA",6}  {"FLAT",-22}{"A-WEIGHTED",-22}{"PHONE-A",-22}");
        foreach (CozySound s in Enum.GetValues(typeof(CozySound)))
        {
            var vs = Bank.Sounds[(int)s];
            // Measure each variant and average the shares.
            var sp = vs.Select(v => Measure.Spectrum(v, SR)).ToList();
            double[] Avg(Func<Dsp.Spectrum, double[]> f) => Enumerable.Range(0, 4).Select(b => sp.Average(x => f(x)[b])).ToArray();
            double peak = vs.Max(v => Dsp.Peak(v)), rms = vs.Average(v => Dsp.Rms(v)), dc = vs.Max(v => Math.Abs(Dsp.Mean(v)));
            Log($"{s,-16}{vs.Length,4}{vs.Average(v => v.Length) * 1000.0 / SR,6:0}{Dsp.Db(peak),7:0.0}{Dsp.Db(rms),7:0.0}{dc,9:0.00000}{sp.Average(x => x.CentroidHz),6:0}{sp.Average(x => x.CentroidAHz),6:0}  {Sh(Avg(x => x.Flat)),-22}{Sh(Avg(x => x.A)),-22}{Sh(Avg(x => x.PhoneA)),-22}");
        }
        foreach (var (name, loop) in new[] { ("loop ReelBed", Bank.Bed), ("loop Strain", Bank.Strain), ("loop Line", Bank.Line), ("loop Ambience", Bank.Ambience) })
        {
            var sp = Measure.Spectrum(loop, SR);
            Log($"{name,-16}{1,4}{loop.Length * 1000.0 / SR,6:0}{Dsp.Db(Dsp.Peak(loop)),7:0.0}{Dsp.Db(Dsp.Rms(loop)),7:0.0}{Math.Abs(Dsp.Mean(loop)),9:0.00000}{sp.CentroidHz,6:0}{sp.CentroidAHz,6:0}  {Sh(sp.Flat),-22}{Sh(sp.A),-22}{Sh(sp.PhoneA),-22}");
        }

        Log();
        Log("Loops: seam (wrap point: |jump| and curvature as percentile of the loop's own sample-to-sample values; <99 = invisible),");
        Log("       periodic AM (exact DFT of the circular envelope): largest single-component depth per band. Rule: <10% at 2-8 Hz and 20-150 Hz.");
        foreach (var (name, loop) in new[] { ("ReelBed", Bank.Bed), ("Strain", Bank.Strain), ("Line", Bank.Line), ("Ambience", Bank.Ambience) })
        {
            var (step, sr, cr) = Dsp.Seam(loop);
            var mod = Measure.Modulation(loop, SR, true);
            var top = mod.OrderByDescending(m => m.depth).Take(3).Select(m => $"{m.hz:0.00} Hz {100 * m.depth:0.0}%");
            Log($"  {name,-9} {loop.Length / (double)SR:0.0} s  seam step {step:0.0000} (pct {sr:0.0}), curvature pct {cr:0.0} | AM max: 0.1-2 Hz {100 * Measure.MaxDepth(mod, 0.1, 2):0.0}%  2-8 Hz {100 * Measure.MaxDepth(mod, 2, 8):0.0}%  8-20 Hz {100 * Measure.MaxDepth(mod, 8, 20):0.0}%  20-150 Hz {100 * Measure.MaxDepth(mod, 20, 150):0.0}% | top: {string.Join(", ", top)}");
        }

        Log();
        Log("Variant sets: centroid spread and how alike variants are (max normalized x-corr between any two, lag +-2 ms; 1 = identical).");
        foreach (var s in new[] { CozySound.GearLight, CozySound.Gear, CozySound.GearHeavy, CozySound.HandleTock, CozySound.DragTick, CozySound.DragTickDark, CozySound.DragPurr, CozySound.RodCreak })
        {
            var vs = Bank.Sounds[(int)s];
            var c = vs.Select(v => Measure.Spectrum(v, SR).CentroidHz).ToList();
            double maxX = 0, sumX = 0; int nX = 0;
            for (int i = 0; i < vs.Length; i++)
                for (int j = i + 1; j < vs.Length; j++)
                {
                    double x = Measure.XCorr(vs[i].Take(Math.Min(vs[i].Length, SR / 10)).ToArray(), vs[j].Take(Math.Min(vs[j].Length, SR / 10)).ToArray(), SR / 500);
                    maxX = Math.Max(maxX, x); sumX += x; nX++;
                }
            Log($"  {s,-13} {vs.Length,2} variants: centroid {c.Min():0}-{c.Max():0} Hz (mean {c.Average():0}); x-corr mean {sumX / nX:0.00}, max {maxX:0.00}; >5 kHz share {100 * vs.Average(v => Measure.ShareAbove(v, SR, 5000)):0.0}%");
        }
    }

    static string Sh(double[] s) => string.Join(" ", s.Select(v => $"{100 * v,4:0.0}"));

    // =====================================================================================
    // Parts: raw loops (2 tiles to expose any seam) and grain sets
    // =====================================================================================

    static void Parts()
    {
        WriteRaw("parts/loop_reel_bed_x2", Tile(Bank.Bed, 2), SR);
        WriteRaw("parts/loop_strain_x2", Tile(Bank.Strain, 2), SR);
        WriteRaw("parts/loop_line_tension_x2", Tile(Bank.Line, 2), SR);
        WriteRaw("parts/grains_gear_light", Train(Bank.Sounds[(int)CozySound.GearLight], 0.3), SR);
        WriteRaw("parts/grains_gear_normal", Train(Bank.Sounds[(int)CozySound.Gear], 0.3), SR);
        WriteRaw("parts/grains_gear_heavy", Train(Bank.Sounds[(int)CozySound.GearHeavy], 0.3), SR);
        WriteRaw("parts/grains_handle_tock", Train(Bank.Sounds[(int)CozySound.HandleTock], 0.4), SR);
        WriteRaw("parts/grains_drag_bright_then_dark", Train(Bank.Sounds[(int)CozySound.DragTick].Concat(Bank.Sounds[(int)CozySound.DragTickDark]).ToArray(), 0.25), SR);
        WriteRaw("parts/grains_drag_purr", Train(Bank.Sounds[(int)CozySound.DragPurr], 0.25), SR);
        WriteRaw("parts/grains_rod_creak", Train(Bank.Sounds[(int)CozySound.RodCreak], 0.8), SR);
    }

    static float[] Tile(float[] s, int times)
    {
        var o = new float[s.Length * times];
        for (int k = 0; k < times; k++) Array.Copy(s, 0, o, k * s.Length, s.Length);
        return o;
    }

    static float[] Train(float[][] clips, double every)
    {
        int n = (int)((clips.Length * every + 0.6) * SR);
        var o = new float[n];
        for (int k = 0; k < clips.Length; k++)
        {
            int at = (int)((0.1 + k * every) * SR);
            for (int i = 0; i < clips[k].Length && at + i < n; i++) o[at + i] += clips[k][i];
        }
        return o;
    }

    // =====================================================================================
    // Scenarios: synthetic FightFrame-like input through CozyCues and the playback model
    // =====================================================================================

    static CozyFrame Tight(float loadShare = 0.45f) => new CozyFrame { SlackCm = 0, LoadShare = loadShare, RodLoadShare = loadShare * 0.7f, WeightG = 800, FishDistanceCm = 1200 };

    static void Scenarios()
    {
        Log();
        Log("=== SCENARIOS (synthetic frames at 60 fps, float tackle C0 = 110 cm/s; files normalised to -20 LUFS, peak <= -3 dBFS) ===");
        const float c0 = 110f;

        Scenario("01_reel_noload", 8, c0, t =>
        {
            var f = Tight();
            bool crank = (t > 0.4 && t < 3.4) || (t > 3.9 && t < 6.6);
            if (crank)
            {
                f.ReelLoad = (float)(0.1 + 0.05 * Math.Sin(2 * Math.PI * 0.31 * t));
                f.ReelInCmPerSec = c0 * (1 - f.ReelLoad) * (float)(0.8 + 0.04 * Math.Sin(2 * Math.PI * 0.23 * t));
            }
            return f;
        }, "free retrieve ~0.72 C0 (11-12 ticks/s), release-and-restart at 3.4-3.9 s, stop at 6.6 s");

        Scenario("02_reel_labour", 8, c0, t =>
        {
            var f = Tight(0.6f);
            if (t > 0.3 && t < 7.0)
            {
                f.ReelLoad = (float)(0.86 + 0.06 * Math.Sin(2 * Math.PI * 0.21 * t) + 0.02 * Math.Sin(2 * Math.PI * 0.53 * t));
                f.ReelInCmPerSec = c0 * (1 - f.ReelLoad);
            }
            return f;
        }, "labouring: ReelLoad 0.78-0.94 (handle 7-24 cm/s -> 1-3.5 heavy ticks/s), strain loop on");

        Scenario("03_reel_fast", 8, c0, t =>
        {
            var f = Tight(0.2f);
            f.SlackCm = 10;
            if (t > 0.3 && t < 7.0)
            {
                double x = Math.Min(1, (t - 0.3) / 5.0);
                f.ReelLoad = 0f;
                f.ReelInCmPerSec = (float)(c0 * (0.9 + 0.3 * x));
            }
            return f;
        }, "gaining fast: fish swims in, no load, 0.9 -> 1.2 C0 (14 -> 19 light ticks/s)");

        Scenario("04_reel_load_sweep", 12, c0, t =>
        {
            var f = Tight(0.5f);
            if (t > 0.3 && t < 11.6)
            {
                double x = (t - 0.3) / 11.3;
                f.ReelLoad = (float)(0.05 + 0.9 * Math.Pow(Math.Sin(Math.PI * x), 2));
                f.ReelInCmPerSec = c0 * (1 - f.ReelLoad);
                f.LoadShare = 0.3f + 0.35f * f.ReelLoad;
            }
            return f;
        }, "one continuous crank, ReelLoad 0.05 -> 0.95 -> 0.05: light -> normal -> heavy ticks and the strain layer fading in/out");

        Scenario("05_drag_slow", 7, c0, t =>
        {
            var f = Tight(0.55f);
            if ((t > 0.5 && t < 3.2) || (t > 3.7 && t < 6.4))
            {
                f.IsDragSlipping = true;
                f.SlipCmPerSec = (float)(14 + 4 * Math.Sin(2 * Math.PI * 0.4 * t));
            }
            return f;
        }, "slow slip 10-18 cm/s (6-11 ticks/s, countable), two runs (accent on the first 3 ticks of each)");

        Scenario("06_drag_fast", 7, c0, t =>
        {
            var f = Tight(0.62f);
            if (t > 0.4 && t < 6.6)
            {
                double x = (t - 0.4) / 6.2;
                f.IsDragSlipping = true;
                f.SlipCmPerSec = (float)(25 + 75 * Math.Sin(Math.PI * Math.Min(1, x * 1.3)));
            }
            return f;
        }, "run: slip 25 -> 100 -> 40 cm/s (16 -> 62 -> 25 grains/s; darker ticks + purr grains + spool whirr)");

        Scenario("07_line_tension", 8, c0, t =>
        {
            double x = Math.Sin(Math.PI * Math.Min(1, t / 7.6));
            var f = Tight((float)(0.6 + 0.4 * x));
            return f;
        }, "line only: LoadShare 0.6 -> 1.0 -> 0.6, tight, nothing else playing");

        Scenario("08_rod_creak", 8, c0, t =>
        {
            double x = Math.Sin(Math.PI * Math.Min(1, t / 7.6));
            var f = Tight(0.55f);
            f.RodLoadShare = (float)(0.74 + 0.26 * x);
            return f;
        }, "rod only: RodLoadShare 0.74 -> 1.0 -> 0.74 (creaks from 0.78, 1 -> 3.5 per second)");
    }

    static void Scenario(string name, double seconds, float c0, Func<double, CozyFrame> gen, string note)
    {
        var cues = new CozyCues { BirdsOn = false };
        var player = new CozyPlayer(Bank, seconds, lake: false);
        var first = gen(0);
        first.Tick = 1;
        cues.Update(LakeState.Fight, first, c0, 10, 0, 0); // enter Fight silently (no hookset in these files)
        player.Frame(cues, 0, Master, skipShots: true);
        bool was = false;
        int frames = (int)(seconds / Dt);
        for (int i = 0; i < frames; i++)
        {
            double t = i * Dt;
            var f = gen(t);
            // Model clock as FightFrame would give it: 0.05 s steps, Alpha = fraction of the step.
            double steps = (t + Dt) / 0.05;
            f.Tick = (int)Math.Floor(steps) + 1;
            f.Alpha = (float)(steps - Math.Floor(steps));
            if (f.IsDragSlipping && !was)
                f.Events |= FightEvents.Breakaway;
            was = f.IsDragSlipping;
            cues.Update(LakeState.Fight, f, c0, 10, (float)t, Dt);
            player.Frame(cues, Dt, Master);
        }
        var mix = player.Mix();
        Log($"-- {name}: {note}");
        Describe(mix, OR, player, "   ");
        WriteLufs(name, mix, OR, -20);
    }

    static void Describe(float[] mix, int fs, CozyPlayer player, string pad)
    {
        var (lufs, st, lra) = Measure.Loudness(mix, fs);
        var sp = Measure.Spectrum(mix, fs);
        var mod = Measure.Modulation(mix, fs, false);
        var p28 = Measure.Peak(mod, 2, 8);
        var p8 = Measure.Peak(mod, 8, 20);
        var p20 = Measure.Peak(mod, 20, 150);
        Log($"{pad}game level: {lufs:0.0} LUFS (max short-term {st:0.0}, LRA {lra:0.0} LU), peak {Dsp.Db(Dsp.Peak(mix)):0.0} dBFS, DC {Dsp.Mean(mix):0.00000}, clipped {Measure.Clipped(mix)}");
        Log($"{pad}centroid {sp.CentroidHz:0} Hz (A {sp.CentroidAHz:0}); FLAT {Sh(sp.Flat)} | A {Sh(sp.A)} | PHONE-A {Sh(sp.PhoneA)}");
        Log($"{pad}envelope AM peaks: 2-8 Hz {p28.hz:0.0} Hz {100 * p28.depth:0}%, 8-20 Hz {p8.hz:0.0} Hz {100 * p8.depth:0}%, 20-150 Hz {p20.hz:0.0} Hz {100 * p20.depth:0}%");
        if (player != null)
        {
            var grains = player.Events.Where(e => e.s <= CozySound.RodCreak).ToList();
            if (grains.Count > 0)
            {
                int repeats = 0;
                foreach (var g in grains.GroupBy(e => e.s))
                {
                    var l = g.ToList();
                    for (int i = 1; i < l.Count; i++) if (l[i].v == l[i - 1].v) repeats++;
                }
                Log($"{pad}grains: {string.Join(" ", grains.GroupBy(e => e.s).Select(g => $"{g.Key}:{g.Count()}"))}; same variant twice in a row: {repeats}; peak grain voices {player.GrainVoicesPeak()}");
                Timing(grains.Where(e => e.s <= CozySound.HandleTock).Select(e => e.t).ToList(), "gear", pad);
                Timing(grains.Where(e => e.s >= CozySound.DragTick && e.s <= CozySound.DragPurr).Select(e => e.t).ToList(), "drag", pad);
            }
        }
    }

    // =====================================================================================
    // One-shots
    // =====================================================================================

    static void OneShots()
    {
        Log();
        Log("=== ONE-SHOTS (files: the raw clip, peak -3 dBFS) ===");
        var list = new (string file, CozySound s, string what)[]
        {
            ("09_hookset", CozySound.Hookset, "hookset: rod swish + soft thump 125->80 Hz w/ harmonics + line 'tunn' + small bloop"),
            ("10_tele_run", CozySound.TeleRun, "telegraph Run: sideways swirl 300->750->450 Hz + bubbles"),
            ("11_tele_dive", CozySound.TeleDive, "telegraph Dive: downward 'gloop' 600->260 Hz + big low bubbles"),
            ("12_tele_surge", CozySound.TeleSurge, "telegraph Surge: quick rush 350->1100 Hz + bubble burst"),
            ("13_tele_jerk", CozySound.TeleJerk, "telegraph Jerk: heartbeat 'tu-dum' 150 Hz knocks"),
            ("14_tele_jump", CozySound.TeleJump, "telegraph Jump: airy rise 280->1500 Hz"),
            ("15_splash_small", CozySound.SplashSmall, "splash, small fish"),
            ("16_splash_big", CozySound.SplashBig, "splash, big fish / jump re-entry"),
            ("17_splash_jump", CozySound.SplashJump, "jump apex: airy spray + droplets falling back"),
            ("18_landed_small", CozySound.LandedSmall, "landed < 500 g: 3 flops + B-D-G"),
            ("19_landed_medium", CozySound.LandedMedium, "landed 0.5-1.5 kg: 4 flops + G-B-D-G"),
            ("20_landed_big", CozySound.LandedBig, "landed >= 1.5 kg: 5 flops + D-G-B-D-G + low G bloom"),
            ("21_line_snap", CozySound.LineSnap, "line snap: muffled snap + slack string 'tunn' + falling line whisper"),
            ("22_rod_crack", CozySound.RodCrack, "rod broke: muffled wood crack"),
            ("23_escape", CozySound.Escape, "fish escaped: quiet bloop + ripple"),
            ("24_cast_whoosh", CozySound.CastWhoosh, "cast: rod swish + line paying off the spool"),
            ("25_float_plop", CozySound.FloatPlop, "float lands: plop"),
            ("26_bite_bob", CozySound.BiteBob, "bite: float bob plip-plip"),
        };
        Log($"{"file",-18}{"ms",6}{"peak",7}{"LUFS",7}{"cent",6}  {"FLAT",-22}{"A-WEIGHTED",-22}{"PHONE-A",-22} >5k%  start/end |x|");
        foreach (var (file, s, what) in list)
        {
            var c = Bank.Sounds[(int)s][0];
            var sp = Measure.Spectrum(c, SR);
            var (l, _, _) = Measure.Loudness(Pad(c, SR), SR);
            Log($"{file,-18}{c.Length * 1000.0 / SR,6:0}{Dsp.Db(Dsp.Peak(c)),7:0.0}{l,7:0.0}{sp.CentroidHz,6:0}  {Sh(sp.Flat),-22}{Sh(sp.A),-22}{Sh(sp.PhoneA),-22}{100 * Measure.ShareAbove(c, SR, 5000),5:0.0}  {Math.Abs(c[0]):0.0000}/{Math.Abs(c[c.Length - 1]):0.0000}   {what}");
            WritePeak(file, c, SR, -3);
        }
        // Birds, one after another.
        var birds = new[] { CozySound.BirdCuckoo, CozySound.BirdPigeon, CozySound.BirdBlackbird };
        var parts = new List<float>();
        foreach (var b in birds) foreach (var c in Bank.Sounds[(int)b]) { parts.AddRange(c); parts.AddRange(new float[SR / 2]); }
        var bs = parts.ToArray();
        var bsp = Measure.Spectrum(bs, SR);
        Log($"{"27_birds",-18}{bs.Length * 1000.0 / SR,6:0}{Dsp.Db(Dsp.Peak(bs)),7:0.0}{"",7}{bsp.CentroidHz,6:0}  {Sh(bsp.Flat),-22}{Sh(bsp.A),-22}{Sh(bsp.PhoneA),-22}       all 11 bird variants: cuckoo x2, wood pigeon x3, blackbird x6 (0.5 s gaps)");
        WritePeak("27_birds", bs, SR, -3);
    }

    static float[] Pad(float[] c, int fs)
    {
        var o = new float[Math.Max(c.Length, fs / 2) + fs / 2];
        Array.Copy(c, o, c.Length);
        return o;
    }

    // =====================================================================================
    // Ambience
    // =====================================================================================

    static void Ambience()
    {
        Log();
        Log("=== AMBIENCE ===");
        var a = Bank.Ambience;
        var (l, st, lra) = Measure.Loudness(Tile(a, 2), SR);
        Log($"  28_ambience_loop_64s: {a.Length / (double)SR:0.0} s, {l:0.0} LUFS raw (file normalised to -24 LUFS), LRA {lra:0.0} LU, peak {Dsp.Db(Dsp.Peak(a)):0.0} dBFS");
        WriteLufs("28_ambience_loop_64s", a, SR, -24);
        // Seam check also in the written file context: last 5 ms vs first 5 ms RMS.
        double e0 = Dsp.Rms(a.Take(SR / 200).ToArray()), e1 = Dsp.Rms(a.Skip(a.Length - SR / 200).ToArray());
        Log($"  seam: RMS last 5 ms {Dsp.Db(e1):0.0} dB vs first 5 ms {Dsp.Db(e0):0.0} dB; jump {Math.Abs(a[0] - a[a.Length - 1]):0.0000} (median step {Median(a):0.0000})");

        // Morning preview: lake loop + the bird schedule, as in Idle/Waiting (no fight).
        var cues = new CozyCues();
        var player = new CozyPlayer(Bank, 60.5);
        var idle = new CozyFrame();
        int frames = (int)(60.0 / Dt);
        for (int i = 0; i < frames; i++)
        {
            cues.Update(LakeState.Waiting, idle, 110, 10, 100f + i * Dt, Dt);
            player.Frame(cues, Dt, Master);
        }
        var mix = player.Mix();
        Log("  29_ambience_morning_60s (loop + scheduled birds, Waiting state, master 0.8):");
        Describe(mix, OR, player, "    ");
        Log($"    birds: {string.Join(" ", player.Events.Where(e => e.s >= CozySound.BirdCuckoo).Select(e => $"{e.t:0.0}s {e.s}#{e.v}"))}");
        WriteLufs("29_ambience_morning_60s", mix, OR, -24);
    }

    static double Median(float[] a)
    {
        var d = new List<double>();
        for (int i = 1; i < a.Length; i += 7) d.Add(Math.Abs(a[i] - a[i - 1]));
        d.Sort();
        return d[d.Count / 2];
    }

    // =====================================================================================
    // Fight demos (FightModel -> FightFrame -> CozyCues -> playback model)
    // =====================================================================================

    /// <summary>
    /// Inter-onset regularity of a grain stream: as scheduled (sample-accurate, sub-frame) vs. what PlayOneShot would give
    /// (onsets snapped to the next 60 fps / 30 fps frame). Only gaps shorter than 0.2 s (steady runs) count.
    /// </summary>
    static void Timing(List<double> t, string label, string pad)
    {
        if (t.Count < 12) return;
        t.Sort();
        string Cv(Func<double, double> map)
        {
            var q = t.Select(map).ToList();
            // Local CV: each gap against the mean of its 6 neighbours (speed changes slowly; jitter does not).
            var gaps = q.Zip(q.Skip(1), (a, b) => b - a).ToList();
            var dev = new List<double>();
            for (int i = 3; i < gaps.Count - 3; i++)
            {
                if (gaps.Skip(i - 3).Take(7).Any(g => g > 0.2 || g <= 0)) continue;
                double m = gaps.Skip(i - 3).Take(7).Average();
                dev.Add((gaps[i] - m) / m);
            }
            return dev.Count > 5 ? $"{100 * Math.Sqrt(dev.Average(x => x * x)):0.0}%" : "-";
        }
        Log($"{pad}{label} onset jitter (local CV of gaps): scheduled {Cv(x => x)}, PlayOneShot@60fps {Cv(x => Math.Ceiling(x * 60) / 60)}, @30fps {Cv(x => Math.Ceiling(x * 30) / 30)}");
    }

    /// <summary>
    /// Spectator check: the same fight replayed at another frame rate (37 fps, as a friend's phone might run) must give the
    /// same sequence of grains (sound + variant) — the event indices come from distances integrated from FightFrame.
    /// </summary>
    static void SpectatorCheck()
    {
        List<(double t, CozySound s, int v)> Run(float dt)
        {
            var tackle = FightKit.FloatTackle();
            var model = new FightModel(FightKit.Bream(), 2000, tackle, 1500, new SeededRandom(BreamSeed), null, 0, 1.0, BreamSeed);
            var bot = FightBots.Technique(tackle.AutoDragG);
            var frame = new FightFrame();
            var cues = new CozyCues { BirdsOn = false };
            var list = new List<(double, CozySound, int)>();
            frame.Start(model, tackle, new Vector3(0, 0, 1), 0.05);
            frame.BeginFrame();
            double acc = 0, t = 0;
            cues.Update(LakeState.Fight, CozyFrameAdapter.Of(frame), 110, 12, 0, dt);
            while (t < 25 && model.Outcome == FightOutcome.InProgress)
            {
                frame.BeginFrame();
                acc += dt; t += dt;
                int steps = 0;
                while (acc >= 0.05 && steps < 10) { model.Step(bot(model)); frame.AddTick(model); acc -= 0.05; steps++; }
                frame.Compose((float)(acc / 0.05));
                cues.Update(LakeState.Fight, CozyFrameAdapter.Of(frame), 110, 12, (float)t, dt);
                foreach (var g in cues.Grains) list.Add((t - dt + g.At * dt, g.Sound, g.Variant));
            }
            return list;
        }
        var a = Run(1f / 60f);
        var b = Run(1f / 37f);
        Log();
        Log($"=== SPECTATOR CHECK (bream fight, 25 s, 60 fps vs 37 fps) === grains {a.Count} vs {b.Count}");
        foreach (var (label, pick) in new (string, Func<CozySound, bool>)[] { ("gear", s => s <= CozySound.HandleTock), ("drag", s => s >= CozySound.DragTick && s <= CozySound.DragPurr), ("creak", s => s == CozySound.RodCreak) })
        {
            var x = a.Where(e => pick(e.s)).ToList();
            var y = b.Where(e => pick(e.s)).ToList();
            int n = Math.Min(x.Count, y.Count), same = 0;
            var dts = new List<double>();
            for (int i = 0; i < n; i++)
            {
                if (x[i].s == y[i].s && x[i].v == y[i].v) same++;
                dts.Add(Math.Abs(x[i].t - y[i].t));
            }
            dts.Sort();
            if (n == 0) { Log($"  {label}: none"); continue; }
            Log($"  {label}: {x.Count} vs {y.Count} grains; same sound+variant at the same index {same}/{n} ({100.0 * same / n:0.0}%); onset difference p50 {1000 * dts[n / 2]:0.00} ms, max {1000 * dts.Last():0.00} ms");
        }
    }

    static void Demos()
    {
        SpectatorCheck();
        int floatDrag = FightKit.FloatTackle().AutoDragG;
        Demo("30_demo_perch300_holdreel", FightKit.Perch, 300, FightKit.FloatTackle, floatDrag, 1500, d => FightBots.HoldReel(d), 3, 4.0, 30);
        Demo("31_demo_bream2kg_technique", FightKit.Bream, 2000, FightKit.FloatTackle, floatDrag, 1500, d => FightBots.Technique(d), BreamSeed, 2.0, 30);
    }

    static uint BreamSeed = 3;

    sealed class Rec
    {
        public double T;
        public LakeState State;
        public bool Cranking, Slipping;
        public float ReelIn, ReelLoad, LoadShare, RodShare, Slip;
        public int S0, S1;
    }

    static void Demo(string name, Func<FishSpecies> species, int weight, Func<FightTackle> makeTackle, int drag, double start,
        Func<int, FightBot> makeBot, uint seed, double waitSeconds, double maxTotal)
    {
        const double tick = 0.05;
        var tackle = makeTackle();
        float c0 = (float)tackle.ReelFightSpeedCmPerSec;
        var model = new FightModel(species(), weight, tackle, start, new SeededRandom(seed), null, 0, 1.0, seed);
        var bot = makeBot(drag);
        var frame = new FightFrame();
        var cues = new CozyCues();
        var player = new CozyPlayer(Bank, maxTotal + 1);
        var recs = new List<Rec>();
        double t = 0;
        float now = 100f;

        void Do(LakeState state)
        {
            var cf = (state == LakeState.Fight || frame.EndedThisFrame) ? CozyFrameAdapter.Of(frame) : new CozyFrame();
            cues.Update(state, cf, c0, 12f, now, Dt);
            int s0 = player.Length;
            player.Frame(cues, Dt, Master);
            recs.Add(new Rec
            {
                T = t, State = state,
                Cranking = state == LakeState.Fight && (cf.ReelInCmPerSec > 0.5f || cf.IsCrankingIntoSlip),
                Slipping = state == LakeState.Fight && cf.IsDragSlipping, ReelIn = cf.ReelInCmPerSec, ReelLoad = cf.ReelLoad,
                LoadShare = cf.LoadShare, RodShare = cf.RodLoadShare, Slip = cf.SlipCmPerSec, S0 = s0, S1 = player.Length,
            });
            t += Dt; now += Dt;
        }

        for (int f = 0; f < (int)(0.4 / Dt); f++) { frame.BeginFrame(); Do(LakeState.Idle); }
        for (int f = 0; f < (int)(1.0 / Dt); f++) { frame.BeginFrame(); Do(LakeState.Casting); }
        for (int f = 0; f < (int)(waitSeconds / Dt); f++) { frame.BeginFrame(); Do(LakeState.Waiting); }
        for (int f = 0; f < (int)(0.6 / Dt); f++) { frame.BeginFrame(); Do(LakeState.Bite); }
        double fightStart = t;
        frame.Start(model, tackle, new Vector3(0.2f, 0, 1f), tick);
        frame.BeginFrame();
        Do(LakeState.Fight);
        double acc = 0;
        bool ended = false;
        var endState = LakeState.Fight;
        while (!ended && t < maxTotal - 0.2)
        {
            frame.BeginFrame();
            acc += Dt;
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
        while (t < maxTotal) { frame.BeginFrame(); Do(ended ? endState : LakeState.Fight); }

        var mix = player.Mix();
        if (!ended)
        {
            // The fight is still on at the end of the demo: 1.5 s raised-cosine fade so the file does not stop dead.
            int fade = (int)(1.5 * OR);
            for (int i = 0; i < fade && i < mix.Length; i++)
                mix[mix.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / fade));
        }
        Log();
        Log($"=== DEMO {name}: {species().id} {weight} g, seed {seed}, outcome {model.Outcome} after {model.ElapsedSeconds:0.0} s of fight (fight starts at {fightStart:0.0} s); {mix.Length / (double)OR:0.0} s @48k, master {Master} ===");
        var fight = recs.Where(r => r.State == LakeState.Fight).ToList();
        Log($"  fight frames {fight.Count}: cranking {100.0 * fight.Count(r => r.Cranking) / fight.Count:0}%, slipping {100.0 * fight.Count(r => r.Slipping) / fight.Count:0}%, LoadShare max {fight.Max(r => r.LoadShare):0.00}, RodLoadShare max {fight.Max(r => r.RodShare):0.00}");
        var cr = fight.Where(r => r.Cranking).ToList();
        if (cr.Count > 0)
        {
            var sp = cr.Select(r => (double)Math.Max(r.ReelIn, c0 * (1 - r.ReelLoad)) / c0 * CozyCues.GearTicksAtC0).OrderBy(v => v).ToList();
            Log($"  gear tick rate while cranking p10/p50/p90 = {sp[sp.Count / 10]:0.0}/{sp[sp.Count / 2]:0.0}/{sp[sp.Count * 9 / 10]:0.0} per s; ReelLoad p50 {cr.Select(r => (double)r.ReelLoad).OrderBy(v => v).ElementAt(cr.Count / 2):0.00}");
        }
        var sl = fight.Where(r => r.Slipping).ToList();
        if (sl.Count > 0)
        {
            var rr = sl.Select(r => (double)r.Slip / 2.0).OrderBy(v => v).ToList();
            Log($"  drag grain rate while slipping p10/p50/p90/max = {rr[rr.Count / 10]:0}/{rr[rr.Count / 2]:0}/{rr[rr.Count * 9 / 10]:0}/{rr.Last():0} per s");
        }
        Describe(mix, OR, player, "  ");
        Log("  one-shots: " + string.Join(" ", player.Played.Where(k => k.Key > CozySound.RodCreak).OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")));
        Log($"  loop starts: bed {player.Bed.Starts}, strain {player.Strain.Starts}, line {player.Line.Starts}");

        // Stem shares (A-weighted energy, headphones / phone).
        var stemA = new double[7]; var stemP = new double[7];
        for (int k = 0; k < 7; k++)
        {
            var arr = player.Stems[k].Take(player.Length).ToArray();
            var w = Dsp.Welch(arr);
            double bin = (double)OR / 8192;
            for (int j = 1; j < w.Length; j++) { stemA[k] += w[j] * Dsp.AWeight(j * bin); stemP[k] += w[j] * Dsp.AWeight(j * bin) * Dsp.PhoneWeight(j * bin); }
        }
        Log("  stem share of A-weighted energy (headphones/phone): " + string.Join("  ", Enumerable.Range(0, 7).Select(k => $"{CozyPlayer.StemNames[k]} {100 * stemA[k] / stemA.Sum():0}/{100 * stemP[k] / stemP.Sum():0}%")));
        Log("  stem peaks dBFS: " + string.Join("  ", Enumerable.Range(0, 7).Select(k => $"{CozyPlayer.StemNames[k]} {Dsp.Db(Dsp.Peak(player.Stems[k])):0.0}")));
        {
            int at = 0; float pk = 0;
            for (int i = 0; i < mix.Length; i++) if (Math.Abs(mix[i]) > pk) { pk = Math.Abs(mix[i]); at = i; }
            var near = player.Events.Where(e => e.t <= at / (double)OR && e.t > at / (double)OR - 0.3).Select(e => $"{e.s}@{e.t:0.00}");
            Log($"  mix peak at {at / (double)OR:0.00} s; events in the 0.3 s before: {string.Join(" ", near)}");
        }

        // While cranking: the reel sound (bed + strain + line + gear grains) — directly comparable to the old whir+growl+sing bed.
        var reel = player.Mix(0, 1, 2, 4);
        var crankS = new List<float>();
        foreach (var r in cr) for (int i = r.S0; i < r.S1 && i < reel.Length; i++) crankS.Add(reel[i]);
        if (crankS.Count > 8192 * 2)
        {
            var cb = crankS.ToArray();
            var sb = Measure.Spectrum(cb, OR);
            var mod = Measure.Modulation(cb, OR, false);
            var p28 = Measure.Peak(mod, 2, 8); var p20 = Measure.Peak(mod, 20, 150);
            Log($"  REEL while cranking ({cb.Length / (double)OR:0.0} s): RMS {Dsp.Db(Dsp.Rms(cb)):0.0} dBFS, centroid {sb.CentroidHz:0} Hz (A {sb.CentroidAHz:0}) | FLAT {Sh(sb.Flat)} | A {Sh(sb.A)} | PHONE-A {Sh(sb.PhoneA)}");
            Log($"       envelope AM: 2-8 Hz peak {p28.hz:0.0} Hz {100 * p28.depth:0}%, 20-150 Hz peak {p20.hz:0.0} Hz {100 * p20.depth:0}%");
        }
        // Gear tick timing regularity while cranking steadily.
        var gear = player.Events.Where(e => e.s <= CozySound.HandleTock).Select(e => e.t).OrderBy(x => x).ToList();
        if (gear.Count > 10)
        {
            var ioi = gear.Zip(gear.Skip(1), (a, b) => b - a).Where(d => d < 0.2).ToList();
            if (ioi.Count > 5)
            {
                double m = ioi.Average(), sd = Math.Sqrt(ioi.Average(d => (d - m) * (d - m)));
                Log($"  gear inter-onset (gaps < 0.2 s): n {ioi.Count}, mean {1000 * m:0} ms, CV {100 * sd / m:0.0}% (from +-4% jitter plus speed changes)");
            }
        }
        // Timeline.
        Log("  timeline (1 s): t | state/phase | crank% slip% | bed strain line lake gear drag fx (RMS dBFS) | mix");
        int secs = player.Length / OR;
        for (int s = 0; s < secs; s++)
        {
            var rs = recs.Where(r => r.T >= s && r.T < s + 1).ToList();
            string st = rs.GroupBy(r => r.State).OrderByDescending(g => g.Count()).First().Key.ToString();
            string Lv(int k)
            {
                double sum = 0;
                for (int i = s * OR; i < (s + 1) * OR; i++) sum += (double)player.Stems[k][i] * player.Stems[k][i];
                double r = Math.Sqrt(sum / OR);
                return r < 1e-4 ? "     ." : $"{Dsp.Db(r),6:0}";
            }
            double ms2 = 0;
            for (int i = s * OR; i < (s + 1) * OR; i++) ms2 += (double)mix[i] * mix[i];
            Log($"   {s,2} {st,-8} {100.0 * rs.Count(r => r.Cranking) / rs.Count,4:0} {100.0 * rs.Count(r => r.Slipping) / rs.Count,4:0} |{Lv(0)}{Lv(1)}{Lv(2)}{Lv(3)}{Lv(4)}{Lv(5)}{Lv(6)} | {Dsp.Db(Math.Sqrt(ms2 / OR)),5:0}");
        }
        WriteLufs(name, mix, OR, -20);
    }

    static void Search()
    {
        int floatDrag = FightKit.FloatTackle().AutoDragG;
        foreach (var (label, sp, w, bot) in new (string, Func<FishSpecies>, int, Func<int, FightBot>)[]
        {
            ("perch300 HoldReel", FightKit.Perch, 300, d => FightBots.HoldReel(d)),
            ("bream2000 Technique", FightKit.Bream, 2000, d => FightBots.Technique(d)),
        })
        {
            Console.WriteLine(label);
            for (uint seed = 1; seed <= 60; seed++)
            {
                var tackle = FightKit.FloatTackle();
                var m = new FightModel(sp(), w, tackle, 1500, new SeededRandom(seed), null, 0, 1.0, seed);
                var b = bot(floatDrag);
                double slip = 0, crank = 0, labour = 0, maxLs = 0, maxRod = 0;
                int jumps = 0, phases = 0;
                while (m.Outcome == FightOutcome.InProgress && m.ElapsedSeconds < 60)
                {
                    m.Step(b(m));
                    if (m.IsDragSlipping) slip += 0.05;
                    if (m.ReelInCmPerSec > 0.5) crank += 0.05;
                    if (m.ReelLoad > 0.6) labour += 0.05;
                    maxLs = Math.Max(maxLs, m.PeakTensionG / Math.Max(1, m.WeakestLinkG));
                    if ((m.Events & FightEvents.PhaseStart) != 0) phases++;
                }
                Console.WriteLine($"  seed {seed,2}: {m.Outcome,-10} {m.ElapsedSeconds,5:0.0} s  slip {slip,4:0.0} crank {crank,4:0.0} labour {labour,4:0.0} phases {phases,2} maxLS {maxLs:0.00}");
            }
        }
    }

    // =====================================================================================
    // WAV output
    // =====================================================================================

    static void WriteRaw(string name, float[] s, int fs) => WritePeak(name, s, fs, -3);

    static void WritePeak(string name, float[] s, int fs, double peakDb)
    {
        double k = Math.Pow(10, peakDb / 20) / Math.Max(1e-9, Dsp.Peak(s));
        Write(name, s.Select(v => (float)(v * k)).ToArray(), fs);
    }

    /// <summary>Normalise to a LUFS target but never above peak -3 dBFS; logs the gain used.</summary>
    static void WriteLufs(string name, float[] s, int fs, double target)
    {
        if (!name.Contains("_loop_"))
        {
            // Not a loop: 20 ms raised-cosine tail so playback does not end on a step.
            s = (float[])s.Clone();
            int fade = fs / 50;
            for (int i = 0; i < fade && i < s.Length; i++) s[s.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / fade));
        }
        var (l, _, _) = Measure.Loudness(s, fs);
        double gainDb = target - l;
        double peakAfter = Dsp.Db(Dsp.Peak(s)) + gainDb;
        if (peakAfter > -3) gainDb -= peakAfter + 3;
        double k = Math.Pow(10, gainDb / 20);
        var o = s.Select(v => (float)(v * k)).ToArray();
        var (l2, _, _) = Measure.Loudness(o, fs);
        Log($"   -> {name}.wav: gain {gainDb:+0.0;-0.0} dB -> {l2:0.0} LUFS, peak {Dsp.Db(Dsp.Peak(o)):0.0} dBFS");
        Write(name, o, fs);
    }

    static void Write(string name, float[] s, int fs)
    {
        string p = Path.Combine(OutDir, name.Replace('/', Path.DirectorySeparatorChar) + ".wav");
        Dsp.WriteWav(p, s, fs);
        Files.Add(p);
    }
}
