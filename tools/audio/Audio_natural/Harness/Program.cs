using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Fishing.Core;
using Fishing.Tests;
using Fishing.Unity;
using UnityEngine;

/// <summary>
/// Renders and measures the «natural» palette:
///   dotnet run -c Release -- [outDir]
/// Writes one WAV per sound, scenario renders driven through NaturalCues, a 64 s ambience, and two fight demos
/// driven by a real FightModel → FightFrame → NaturalCues → Engine (software model of the Unity playback).
/// </summary>
internal static class Program
{
    const int SR = NaturalSynth.SampleRate;
    static string Out;
    static readonly List<string> Files = new List<string>();
    static NaturalBank Bank;

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Out = args.Length > 0 ? args[0] : RepoPaths.Previews("natural");
        Directory.CreateDirectory(Out);

        var sw = Stopwatch.StartNew();
        Bank = new NaturalBank();
        sw.Stop();
        Console.WriteLine($"Bank generated in {sw.ElapsedMilliseconds} ms (desktop, Release, single thread)");
        var sw2 = Stopwatch.StartNew();
        var bank2 = new NaturalBank();
        sw2.Stop();
        bool same = bank2.Rotor[3].SequenceEqual(Bank.Rotor[3]) && bank2.Ambience.SequenceEqual(Bank.Ambience) && bank2.Drag[11].SequenceEqual(Bank.Drag[11]);
        Console.WriteLine($"Second build {sw2.ElapsedMilliseconds} ms; deterministic (bit-identical rebuild): {same}");
        long samples = Bank.Rotor.Sum(c => c.Length) + Bank.Tock.Sum(c => c.Length) + Bank.Drag.Sum(c => c.Length) + Bank.Strain.Sum(c => c.Length)
            + Bank.Crackle.Sum(c => c.Length) + Bank.Creak.Sum(c => c.Length) + Bank.LayBed.Length + Bank.LineHum.Length + Bank.Ambience.Length;
        Console.WriteLine($"Grain + loop samples: {samples} ({samples * 4 / 1024.0 / 1024.0:0.0} MB as float)");

        Clips();
        Scenarios();
        AmbienceFile();
        Demos();
        Sheets();

        Console.WriteLine();
        Console.WriteLine("FILES:");
        foreach (var f in Files) Console.WriteLine("  " + f);
        return 0;
    }

    // =====================================================================================
    // WAV helpers
    // =====================================================================================

    static void Wav(string name, float[] s, int fs, double gain = 1.0)
    {
        var o = s.Select(v => (float)(v * gain)).ToArray();
        string p = Path.Combine(Out, name + ".wav");
        Dsp.WriteWav(p, o, fs);
        Files.Add(p);
    }

    /// <summary>Peak-normalise to -3 dBFS (one-shot previews).</summary>
    static void WavShot(string name, float[] s) => Wav(name, Pad(s, 0.05, 0.25), SR, 0.7079 / Math.Max(1e-9, Dsp.Peak(s)));

    static float[] Pad(float[] s, double pre, double post)
    {
        int a = (int)(pre * SR), b = (int)(post * SR);
        var o = new float[a + s.Length + b];
        Array.Copy(s, 0, o, a, s.Length);
        return o;
    }

    static float[] Concat(IEnumerable<float[]> parts, double gapSeconds, int fs)
    {
        var o = new List<float>();
        foreach (var p in parts) { o.AddRange(p); o.AddRange(new float[(int)(gapSeconds * fs)]); }
        return o.ToArray();
    }

    // =====================================================================================
    // 1. Clips: measure every recipe, write the one-shots
    // =====================================================================================

    static void Row(string name, float[] s, bool repeated)
    {
        int pad = Math.Max(16384, s.Length);
        var sp = Dsp.Bands(Dsp.Power(s, pad), (double)SR / pad);
        double peak = Dsp.Peak(s), dc = Dsp.Mean(s);
        string flag = repeated && sp.Flat[2] > 0.10 ? "  <-- >10% 2-5k" : "";
        double phone = Meter.PhoneLossDb(Dsp.Power(s, pad), (double)SR / pad);
        Console.WriteLine($"  {name,-22}{1000.0 * s.Length / SR,6:0}{Dsp.Db(peak),7:0.0}{dc,10:0.00000}{sp.CentroidHz,7:0} | {Meter.Shares(sp.Flat)} | {Meter.Shares(sp.A)} | phone {phone,5:0.0} dB{flag}");
    }

    static void Clips()
    {
        Console.WriteLine();
        Console.WriteLine("=== CLIPS (22050 Hz mono as generated) ===");
        Console.WriteLine($"  {"clip",-22}{"ms",6}{"peak",7}{"DC",10}{"cent",7} | FLAT <300 300-2k 2k-5k >5k | A-WEIGHTED <300 300-2k 2k-5k >5k");
        void Group(string name, float[][] v, bool rep)
        {
            // Average spectrum over variants (energy-weighted) + worst variant 2-5k share.
            int pad = 16384;
            var acc = new double[pad / 2 + 1];
            double worst = 0, dcMax = 0, peakMax = 0;
            foreach (var c in v)
            {
                var p = Dsp.Power(c, pad);
                for (int k = 0; k < acc.Length; k++) acc[k] += p[k];
                worst = Math.Max(worst, Dsp.Bands(p, (double)SR / pad).Flat[2]);
                dcMax = Math.Max(dcMax, Math.Abs(Dsp.Mean(c)));
                peakMax = Math.Max(peakMax, Dsp.Peak(c));
            }
            var sp = Dsp.Bands(acc, (double)SR / pad);
            string flag = rep && sp.Flat[2] > 0.10 ? "  <-- >10% 2-5k" : "";
            double phone = Meter.PhoneLossDb(acc, (double)SR / pad);
            Console.WriteLine($"  {name + " x" + v.Length,-22}{1000.0 * v.Average(c => c.Length) / SR,6:0}{Dsp.Db(peakMax),7:0.0}{dcMax,10:0.00000}{sp.CentroidHz,7:0} | {Meter.Shares(sp.Flat)} | {Meter.Shares(sp.A)} | phone {phone,5:0.0} dB  worst-variant 2-5k {100 * worst:0.0}%{flag}");
            if (name == "rotor" || name == "drag bright")
                Console.WriteLine($"      1/3-oct dB: {Meter.ThirdOctaves(acc, (double)SR / pad)}");
        }
        Group("rotor", Bank.Rotor, true);
        Group("tock", Bank.Tock, true);
        Group("drag bright", Bank.Drag.Take(8).ToArray(), true);
        Group("drag dark", Bank.Drag.Skip(8).ToArray(), true);
        Group("strain small", Bank.Strain.Take(6).ToArray(), true);
        Group("strain big", Bank.Strain.Skip(6).ToArray(), true);
        Group("crackle", Bank.Crackle, true);
        Group("creak quiet", Bank.Creak.Take(6).ToArray(), true);
        Group("creak loud", Bank.Creak.Skip(6).ToArray(), true);

        var shots = new List<(string file, string name, float[] s)>
        {
            ("08_hookset", "hookset", Bank.Sound(Cue(NaturalSound.Hookset))),
            ("09_tele_run", "tele run", Bank.Sound(Cue(NaturalSound.TeleRun))),
            ("10_tele_dive", "tele dive", Bank.Sound(Cue(NaturalSound.TeleDive))),
            ("11_tele_surge", "tele surge", Bank.Sound(Cue(NaturalSound.TeleSurge))),
            ("12_tele_jerk", "tele jerk", Bank.Sound(Cue(NaturalSound.TeleJerk))),
            ("13_tele_jump", "tele jump", Bank.Sound(Cue(NaturalSound.TeleJump))),
            ("21_line_snap", "line snap", Bank.Sound(Cue(NaturalSound.LineSnap))),
            ("22_rod_crack", "rod crack", Bank.Sound(Cue(NaturalSound.RodCrack))),
            ("23_escape", "escape", Bank.Sound(Cue(NaturalSound.Escape))),
            ("24_cast", "cast", Bank.Sound(Cue(NaturalSound.Cast))),
            ("25_float_plop", "float plop", Bank.Sound(Cue(NaturalSound.FloatPlop))),
            ("26_bite", "bite bloop", Bank.Sound(Cue(NaturalSound.BiteBloop))),
        };
        foreach (var (file, name, s) in shots)
        {
            Row(name, s, false);
            WavShot(file, s);
        }
        Row("float plop far", Bank.Sound(new NaturalCue { Sound = NaturalSound.FloatPlop, Far = true }), false);
        for (int i = 0; i < 3; i++) Row("splash small " + i, Bank.SplashClip(0, i, false), false);
        for (int i = 0; i < 3; i++) Row("splash big " + i, Bank.SplashClip(1, i, false), false);
        for (int i = 0; i < 2; i++) Row("splash jump " + i, Bank.SplashClip(2, i, false), false);
        Row("splash small far 0", Bank.SplashClip(0, 0, true), false);
        Row("splash big far 0", Bank.SplashClip(1, 0, true), false);
        for (int t = 1; t <= 3; t++) Row("landed tier " + t, Bank.LandedClip(t), false);

        // Splash files: the variants in a row so the variation is audible.
        WavShotRow("14_splash_small_x3", Enumerable.Range(0, 3).Select(i => Bank.SplashClip(0, i, false)));
        WavShotRow("15_splash_big_x3", Enumerable.Range(0, 3).Select(i => Bank.SplashClip(1, i, false)));
        WavShotRow("16_splash_jump_x2", Enumerable.Range(0, 2).Select(i => Bank.SplashClip(2, i, false)));
        WavShotRow("17_splash_far_small_big", new[] { Bank.SplashClip(0, 0, true), Bank.SplashClip(1, 0, true) });
        WavShot("18_landed_small", Bank.LandedClip(1));
        WavShot("19_landed_mid", Bank.LandedClip(2));
        WavShot("20_landed_big", Bank.LandedClip(3));

        Console.WriteLine();
        Console.WriteLine("Loops: seam (wrap point) as percentile of the loop's own sample steps / curvature; strongest AM component in 2-8 Hz and 20-150 Hz");
        foreach (var (name, s) in new[] { ("lay bed", Bank.LayBed), ("line hum", Bank.LineHum), ("ambience", Bank.Ambience) })
        {
            var (step, stepRank, curvRank) = Dsp.Seam(s);
            var am = name == "ambience" ? (0, 0, 0, 0) : Meter.LoopAm(s, SR);
            var sp = Meter.WelchBands(s, SR);
            Console.WriteLine($"  {name,-10} {s.Length / (double)SR:0.0} s  seam step {step:0.0000} = pct {stepRank:0.0}, curvature pct {curvRank:0.0}; DC {Dsp.Mean(s):0.00000}; centroid {sp.CentroidHz:0} Hz; FLAT {Meter.Shares(sp.Flat)} | A {Meter.Shares(sp.A)}"
                + (name == "ambience" ? "" : $"; AM 2-8 Hz max {100 * am.Item2:0.0}% @ {am.Item1:0.00} Hz, 20-150 Hz max {100 * am.Item4:0.0}% @ {am.Item3:0.0} Hz"));
        }
    }

    static NaturalCue Cue(NaturalSound s) => new NaturalCue { Sound = s };

    static void WavShotRow(string name, IEnumerable<float[]> clips)
    {
        var list = clips.ToList();
        double peak = list.Max(c => Dsp.Peak(c));
        Wav(name, Pad(Concat(list, 0.6, SR), 0.05, 0.1), SR, 0.7079 / peak);
    }

    // =====================================================================================
    // 2. Scenarios through NaturalCues (no fight model): what each reel/drag/tension state sounds like
    // =====================================================================================

    struct S
    {
        public double ReelIn, Load, Slip, LoadShare, RodShare;
        public bool IntoSlip;
    }

    static (float[] mix, Engine e) Scenario(double seconds, float c0, Func<double, S> at)
    {
        var cues = new NaturalCues();
        var eng = new Engine(Bank) { PlayOneShots = false, AmbienceOn = false };
        const float dt = 1f / 60f;
        double t = 0;
        int tick = 0;
        bool wasSlip = false;
        cues.Update(LakeState.Waiting, default, c0, 15, 0, dt);
        while (t < seconds)
        {
            int ticks = (int)Math.Floor((t + dt) / 0.05 + 1e-9) - (int)Math.Floor(t / 0.05 + 1e-9);
            var s = at(t);
            bool slipping = s.Slip > 0;
            var ev = FightEvents.None;
            if (slipping && !wasSlip && ticks > 0) ev |= FightEvents.Breakaway;
            if (ticks > 0) wasSlip = slipping;
            tick += ticks;
            var f = new NaturalInput
            {
                Ticks = ticks, ReelInCmPerSec = (float)s.ReelIn, ReelLoad = (float)s.Load, IsDragSlipping = slipping,
                IsCrankingIntoSlip = s.IntoSlip, SlipCmPerSec = (float)s.Slip, SlipCmThisFrame = (float)(s.Slip * 0.05 * ticks),
                LoadShare = (float)s.LoadShare, RodLoadShare = (float)s.RodShare, SlackCm = 0, FishDistanceCm = 1200,
                Events = ev, Phase = FishPhase.Rest, NextPhase = FishPhase.Rest, WeightG = 800, Tick = tick,
            };
            cues.Update(LakeState.Fight, f, c0, 15, t, dt);
            eng.Frame(cues, dt);
            t += dt;
        }
        for (int i = 0; i < 30; i++) { cues.Update(LakeState.Caught, default, c0, 15, t, dt); eng.Frame(cues, dt); t += dt; }
        return (eng.Mix(), eng);
    }

    static double Lin(double t, double t0, double t1, double a, double b) => t <= t0 ? a : t >= t1 ? b : a + (b - a) * (t - t0) / (t1 - t0);

    static void Scenarios()
    {
        Console.WriteLine();
        Console.WriteLine("=== SCENARIOS (NaturalCues -> Engine, 48 kHz, game levels x master 0.8, no ambience) ===");
        var list = new List<(string file, string what, float[] mix, Engine e)>();
        void Add(string file, string what, double sec, float c0, Func<double, S> f)
        {
            var (m, e) = Scenario(sec, c0, f);
            list.Add((file, what, m, e));
        }
        Add("01_reel_noload", "free retrieve, float reel C0 110: 0.8 C0, stop, 1.0 C0", 8, 110, t =>
            t < 0.3 || (t > 3.3 && t < 3.8) || t > 7.3 ? new S { LoadShare = 0.2 } : new S { ReelIn = t < 3.3 ? 88 : 110, Load = 0.05, LoadShare = 0.25, RodShare = 0.3 });
        Add("02_reel_load", "labouring: ReelLoad 0.35 -> 0.95 over 8 s, ReelIn = C0(1-load)", 9, 110, t =>
        {
            if (t > 8.3) return new S { LoadShare = 0.4 };
            double load = Lin(t, 0, 8, 0.35, 0.95);
            return new S { ReelIn = 110 * (1 - load), Load = load, LoadShare = Lin(t, 0, 8, 0.45, 0.85), RodShare = Lin(t, 0, 8, 0.45, 0.7) };
        });
        Add("03_reel_fast", "gaining fast, spinning reel C0 250: 0.9 -> 1.0 C0", 6.5, 250, t =>
            t < 0.3 || t > 6.0 ? new S { LoadShare = 0.1 } : new S { ReelIn = Lin(t, 0.3, 3, 225, 250), Load = 0.03, LoadShare = 0.15, RodShare = 0.2 });
        Add("04_drag_slow", "drag slipping slowly: 9 cm/s (7.5 ticks/s), then 14 cm/s (11.7/s)", 6.5, 110, t =>
            t > 0.4 && t < 2.6 ? new S { Slip = 9, Load = 0, LoadShare = 0.62, RodShare = 0.5 }
            : t > 3.2 && t < 5.8 ? new S { Slip = 14, LoadShare = 0.64, RodShare = 0.52 } : new S { LoadShare = 0.5 });
        Add("05_drag_fast", "run: slip 20 -> 95 cm/s (17 -> 79 ticks/s), hold, back to 25", 7.5, 110, t =>
        {
            if (t < 0.3 || t > 6.4) return new S { LoadShare = 0.5 };
            double slip = t < 3.3 ? Lin(t, 0.3, 3.3, 20, 95) : t < 4.3 ? 95 : Lin(t, 4.3, 6.4, 95, 25);
            return new S { Slip = slip, LoadShare = 0.68, RodShare = 0.55 };
        });
        Add("06_line_tension", "near the limit: slow reel under load 0.85, LoadShare 0.65 -> 1.0", 8.5, 110, t =>
            t > 8.0 ? new S { LoadShare = 0.5 } : new S { ReelIn = 16, Load = 0.85, LoadShare = Lin(t, 0, 7.5, 0.65, 1.0), RodShare = 0.6 });
        Add("07_rod_creak", "rod near its limit: RodLoadShare 0.72 -> 1.0, reel load 0.7", 8.5, 110, t =>
            t > 8.0 ? new S { LoadShare = 0.5 } : new S { ReelIn = 33, Load = 0.7, LoadShare = 0.8, RodShare = Lin(t, 0, 7.5, 0.72, 1.0) });
        Add("27_crank_into_slip", "cranking into a slipping drag (line twists): rotor at C0(1-load)=44 cm/s + ratchet 12 cm/s", 6, 110, t =>
            t < 0.3 || t > 5.4 ? new S { LoadShare = 0.5 } : new S { ReelIn = 0, Load = 0.6, IntoSlip = true, Slip = 12, LoadShare = 0.66, RodShare = 0.5 });

        // One common preview gain so the scenario files keep their relative game levels; peaks <= -3 dBFS.
        double maxPeak = list.Max(x => Dsp.Peak(x.mix));
        double gain = Math.Min(Math.Pow(10, 9.0 / 20), 0.7079 / maxPeak);
        Console.WriteLine($"  common preview gain {Dsp.Db(gain):+0.0;-0.0} dB (relative levels between scenario files = game levels)");
        Console.WriteLine($"  {"file",-18}{"LUFS*",7}{"peak",7}{"cent",7} | FLAT <300 300-2k 2k-5k >5k | A | grains/s by kind; IOI jitter");
        foreach (var (file, what, mix, e) in list)
        {
            double lufs = Meter.Lufs48(mix, out _, out _);
            var sp = Meter.WelchBands(mix, Engine.Rate);
            var kinds = e.GrainLog.GroupBy(g => g.kind).Select(g => $"{g.Key} {g.Count() / (mix.Length / (double)Engine.Rate):0.0}");
            Console.WriteLine($"  {file,-18}{lufs,7:0.0}{Dsp.Db(Dsp.Peak(mix)),7:0.0}{sp.CentroidHz,7:0} | {Meter.Shares(sp.Flat)} | {Meter.Shares(sp.A)} | {string.Join(", ", kinds)}  [{what}]");
            Wav(file, mix, Engine.Rate, gain);
        }
        // Timing regularity of the rotor grains in the steady part of 01 and drag in 04.
        var rot = list[0].e.GrainLog.Where(g => g.kind == NaturalGrain.Rotor && g.t > 4.2 && g.t < 7.2).Select(g => g.t).OrderBy(x => x).ToList();
        var ioi = rot.Zip(rot.Skip(1), (a, b) => b - a).ToList();
        Console.WriteLine($"  rotor grains @110 cm/s: mean IOI {1000 * ioi.Average():0.0} ms (expected {1000 * 14 / 110.0:0.0}), IOI CV {100 * Std(ioi) / ioi.Average():0.0}%");
        var dr = list[3].e.GrainLog.Where(g => g.kind == NaturalGrain.Drag && g.t > 3.5 && g.t < 5.6).Select(g => g.t).OrderBy(x => x).ToList();
        var dioi = dr.Zip(dr.Skip(1), (a, b) => b - a).ToList();
        Console.WriteLine($"  drag ticks @14 cm/s: mean IOI {1000 * dioi.Average():0.0} ms (expected {1000 * 1.2 / 14:0.0}), IOI CV {100 * Std(dioi) / dioi.Average():0.0}%");
        // Fallback without OnAudioFilterRead: the same grains started by PlayOneShot in the frame they were decided (60 fps).
        foreach (var (idx, kind, label) in new[] { (0, NaturalGrain.Rotor, "rotor @110 cm/s (7.9/s)"), (2, NaturalGrain.Rotor, "rotor @250 cm/s (18/s)"), (4, NaturalGrain.Drag, "drag fast run") })
        {
            var ts = list[idx].e.GrainLog.Where(g => g.kind == kind).Select(g => g.t).OrderBy(x => x).ToList();
            var q = ts.Select(x => Math.Floor((x - 0.04) * 60) / 60).ToList();
            var a = ts.Zip(ts.Skip(1), (x, y) => y - x).Where(d => d < 0.3).ToList();
            var b = q.Zip(q.Skip(1), (x, y) => y - x).Where(d => d < 0.3).ToList();
            int zero = b.Count(d => d < 1e-6);
            Console.WriteLine($"  timing {label}: IOI CV sample-accurate {100 * Std(a) / a.Average():0.0}% vs frame-quantised (PlayOneShot @60 fps) {100 * Std(b) / b.Average():0.0}%, grains landing in the same frame {zero}");
        }
    }

    static double Std(IEnumerable<double> x)
    {
        var l = x.ToList();
        double m = l.Average();
        return Math.Sqrt(l.Sum(v => (v - m) * (v - m)) / Math.Max(1, l.Count - 1));
    }

    // =====================================================================================
    // 3. Ambience
    // =====================================================================================

    static void AmbienceFile()
    {
        Console.WriteLine();
        Console.WriteLine("=== AMBIENCE (64 s seamless loop) ===");
        var a = Bank.Ambience;
        double rms = Dsp.Rms(a);
        double gain = Math.Min(Math.Pow(10, -26.0 / 20) / rms, 0.7079 / Dsp.Peak(a));
        Wav("30_ambience_morning_64s", a, SR, gain);
        int k = 3 * SR;
        var seam = new float[2 * k];
        Array.Copy(a, a.Length - k, seam, 0, k);
        Array.Copy(a, 0, seam, k, k);
        Wav("31_ambience_seam_check", seam, SR, gain);
        var (lag, r) = Meter.EnvelopeRepeat(a, SR, 1.0, 30, true);
        var sp = Meter.WelchBands(a, SR);
        Console.WriteLine($"  preview gain {Dsp.Db(gain):+0.0;-0.0} dB -> RMS {Dsp.Db(rms * gain):0.0} dBFS, peak {Dsp.Db(Dsp.Peak(a) * gain):0.0} dBFS");
        Console.WriteLine($"  envelope self-similarity (1-30 s lags, circular): max r = {r:0.00} at {lag:0.0} s (1.0 = exact repeat)");
        Console.WriteLine($"  centroid {sp.CentroidHz:0} Hz; FLAT {Meter.Shares(sp.Flat)} | A {Meter.Shares(sp.A)}");
        // Per-8 s level profile, to show it breathes but never jumps.
        var prof = new List<string>();
        for (int s = 0; s + 8 * SR <= a.Length; s += 8 * SR)
            prof.Add($"{Dsp.Db(Dsp.Rms(a.Skip(s).Take(8 * SR).ToArray()) * gain):0.0}");
        Console.WriteLine($"  RMS per 8 s (preview level): {string.Join(" ", prof)}");
    }

    // =====================================================================================
    // 4. Fight demos (real FightModel)
    // =====================================================================================

    static void Demos()
    {
        int floatDrag = FightKit.FloatTackle().AutoDragG;
        var perch = Demo("perch300_HoldReel", FightKit.Perch(), 300, FightKit.FloatTackle(), floatDrag, d => FightBots.HoldReel(d), 3, 26);
        var bream = Demo("bream2kg_Technique", FightKit.Bream(), 2000, FightKit.FloatTackle(), floatDrag, d => FightBots.Technique(d), 3, 26);

        // One common trim for both demos: the louder lands near -20 LUFS, peaks <= -3 dBFS.
        double l1 = Meter.Lufs48(perch.Mix(), out _, out _), l2 = Meter.Lufs48(bream.Mix(), out _, out _);
        double p = Math.Max(Dsp.Peak(perch.Mix()), Dsp.Peak(bream.Mix()));
        double trimDb = Math.Min(-20 - Math.Max(l1, l2), Dsp.Db(0.7079 / p));
        double g = Math.Pow(10, trimDb / 20);
        Console.WriteLine();
        Console.WriteLine($"=== DEMO FILES: common trim {trimDb:+0.0;-0.0} dB over game level (master 0.8) ===");
        foreach (var (name, e) in new[] { ("40_demo_perch300_holdreel", perch), ("41_demo_bream2kg_technique", bream) })
        {
            var m = e.Mix().Select(v => (float)(v * g)).ToArray();
            double lufs = Meter.Lufs48(m, out double stMax, out double lra);
            double tp = Meter.TruePeak(m);
            var sp = Meter.WelchBands(m, Engine.Rate);
            Console.WriteLine($"  {name}: {m.Length / (double)Engine.Rate:0.0} s, integrated {lufs:0.0} LUFS, short-term max {stMax:0.0}, LRA {lra:0.0} LU, sample peak {Dsp.Db(Dsp.Peak(m)):0.0} dBFS, true peak {Dsp.Db(tp):0.0} dBTP, DC {Dsp.Mean(m):0.00000}, clipped {m.Count(v => Math.Abs(v) >= 1f)}");
            Console.WriteLine($"     centroid {sp.CentroidHz:0} Hz | FLAT {Meter.Shares(sp.Flat)} | A {Meter.Shares(sp.A)} | PHONE-A {Meter.Shares(sp.PhoneA)}");
            Wav(name, m, Engine.Rate);
        }
        Wav("42_demo_perch300_reel_only", perch.Mix(fx: false, amb: false), Engine.Rate, g);
        Wav("43_demo_bream2kg_reel_drag_only", bream.Mix(fx: false, amb: false), Engine.Rate, g);
    }

    sealed class Rec
    {
        public double T;
        public LakeState State;
        public bool Cranking, Slipping;
        public float ReelIn, Load, LoadShare, RodShare, Slip;
        public int S0, S1;
    }

    static Engine Demo(string name, FishSpecies species, int weight, FightTackle tackle, int drag, Func<int, FightBot> makeBot, uint seed, double maxFight)
    {
        const double tick = 0.05;
        const float dt = 1f / 60f;
        var model = new FightModel(species, weight, tackle, 1500, new SeededRandom(seed), null, 0, 1.0, seed);
        var bot = makeBot(drag);
        var frame = new FightFrame();
        var cues = new NaturalCues
        {
            RewardTier = weight >= species.weightG.trophy ? 3 : weight >= species.weightG.typical ? 2 : 1,
        };
        var eng = new Engine(Bank);
        float c0 = (float)tackle.ReelFightSpeedCmPerSec;
        var recs = new List<Rec>();
        double t = 0;

        void Do(LakeState state)
        {
            var f = state == LakeState.Fight || frame.EndedThisFrame ? NaturalInput.Of(frame) : default;
            cues.Update(state, f, c0, 15f, t, dt);
            int s0 = eng.Reel.Count;
            eng.Frame(cues, dt);
            recs.Add(new Rec
            {
                T = t, State = state,
                Cranking = state == LakeState.Fight && (frame.ReelInCmPerSec > 0.5f || frame.IsCrankingIntoSlip),
                Slipping = state == LakeState.Fight && frame.IsDragSlipping, ReelIn = frame.ReelInCmPerSec, Load = frame.ReelLoad,
                LoadShare = frame.LoadShare, RodShare = frame.RodLoadShare, Slip = frame.SlipCmPerSec, S0 = s0, S1 = eng.Reel.Count,
            });
            t += dt;
        }

        for (int i = 0; i < 18; i++) { frame.BeginFrame(); Do(LakeState.Idle); }
        for (int i = 0; i < 66; i++) { frame.BeginFrame(); Do(LakeState.Casting); }
        for (int i = 0; i < 120; i++) { frame.BeginFrame(); Do(LakeState.Waiting); }
        for (int i = 0; i < 30; i++) { frame.BeginFrame(); Do(LakeState.Bite); }
        double fightStart = t;
        frame.Start(model, tackle, new Vector3(0.2f, 0, 1f), tick);
        frame.BeginFrame();
        Do(LakeState.Fight);
        double acc = 0;
        bool ended = false;
        var endState = LakeState.Fight;
        while (!ended && t - fightStart < maxFight)
        {
            frame.BeginFrame();
            acc += dt;
            int steps = 0;
            while (acc >= tick && steps < 10 && model.Outcome == FightOutcome.InProgress)
            {
                model.Step(bot(model));
                frame.AddTick(model);
                acc -= tick;
                steps++;
            }
            var state = LakeState.Fight;
            if (model.Outcome == FightOutcome.InProgress)
                frame.Compose((float)(acc / tick));
            else
            {
                frame.End();
                ended = true;
                state = model.Outcome == FightOutcome.Landed ? LakeState.Caught : LakeState.Lost;
                endState = state;
            }
            Do(state);
        }
        double fightSeconds = model.ElapsedSeconds;
        for (int i = 0; i < 180; i++) { frame.BeginFrame(); Do(ended ? endState : LakeState.Fight); }

        // ---------- Report ----------
        Console.WriteLine();
        Console.WriteLine($"=== DEMO {name} (seed {seed}): outcome {model.Outcome} after {fightSeconds:0.0} s of fight; rendered {eng.Reel.Count / (double)Engine.Rate:0.0} s @48k ===");
        var fight = recs.Where(r => r.State == LakeState.Fight).ToList();
        Console.WriteLine($"  fight frames {fight.Count}: cranking {100.0 * fight.Count(r => r.Cranking) / fight.Count:0}%, slipping {100.0 * fight.Count(r => r.Slipping) / fight.Count:0}%, ReelLoad p50 while cranking {Median(fight.Where(r => r.Cranking).Select(r => (double)r.Load)):0.00}, slip p50 while slipping {Median(fight.Where(r => r.Slipping).Select(r => (double)r.Slip)):0.0} cm/s");
        double secs = fight.Count * dt;
        foreach (var grp in eng.GrainLog.GroupBy(g => g.kind))
        {
            var l = grp.OrderBy(g => g.t).ToList();
            int repeats = l.Zip(l.Skip(1), (a, b) => a.variant == b.variant ? 1 : 0).Sum();
            var gdb = l.Select(g => 20 * Math.Log10(g.gain)).ToList();
            var pitchSt = l.Select(g => 12 * Math.Log(g.pitch, 2)).ToList();
            Console.WriteLine($"  {grp.Key,-8} {l.Count,5} grains ({l.Count / secs:0.0}/s of fight), variants used {l.Select(g => g.variant).Distinct().Count()}, same-variant-twice-in-a-row {repeats}, gain {gdb.Min():0.0}..{gdb.Max():0.0} dB (sd {Std(gdb):0.0}), pitch {pitchSt.Min():+0.0;-0.0}..{pitchSt.Max():+0.0;-0.0} st");
        }
        Console.WriteLine($"  one-shots: {string.Join(", ", eng.ShotLog.GroupBy(s => s.sound + (s.far ? "(far)" : "")).Select(g => $"{g.Key}:{g.Count()}"))}");
        Console.WriteLine($"  grain mixers: peak voices reel {eng.ReelGrains.PeakVoices}, drag {eng.DragGrains.PeakVoices}, tension {eng.TensionGrains.PeakVoices}; stolen {eng.ReelGrains.Stolen + eng.DragGrains.Stolen + eng.TensionGrains.Stolen}; dropped {eng.ReelGrains.Dropped + eng.DragGrains.Dropped + eng.TensionGrains.Dropped}; loop starts lay {eng.Lay.Starts}, hum {eng.Hum.Starts}");

        // Rotor rhythm regularity while cranking steadily.
        var rot = eng.GrainLog.Where(g => g.kind == NaturalGrain.Rotor).Select(g => g.t).OrderBy(x => x).ToList();
        var ratios = new List<double>();
        for (int i = 2; i < rot.Count; i++)
        {
            double a = rot[i - 1] - rot[i - 2], b = rot[i] - rot[i - 1];
            if (a < 0.4 && b < 0.4) ratios.Add(Math.Abs(Math.Log(b / a)));
        }
        if (ratios.Count > 0)
            Console.WriteLine($"  rotor rhythm: consecutive-interval change median {100 * (Math.Exp(Median(ratios)) - 1):0.0}%, p90 {100 * (Math.Exp(Pct(ratios, 0.9)) - 1):0.0}% (0 = machine-regular)");

        // Levels by stem (game level, master 0.8), measured where each is active.
        float[] Seg(List<float> stem, Func<Rec, bool> sel)
        {
            var o = new List<float>();
            foreach (var r in recs.Where(sel)) for (int i = r.S0; i < r.S1 && i < stem.Count; i++) o.Add(stem[i]);
            return o.ToArray();
        }
        var reelC = Seg(eng.Reel, r => r.Cranking && !r.Slipping);
        var dragS = Seg(eng.Drag, r => r.Slipping);
        var amb = eng.Ambience.ToArray();
        var mixAll = eng.Mix();
        string Lv(float[] x) => x.Length > 0 ? $"{Dsp.Db(Dsp.Rms(x)):0.0} dBFS RMS / peak {Dsp.Db(Dsp.Peak(x)):0.0}" : "-";
        Console.WriteLine($"  game level (before demo trim): mix {Lv(mixAll)}, {Meter.Lufs48(mixAll, out _, out _):0.0} LUFS");
        Console.WriteLine($"    reel stem while cranking {Lv(reelC)}; drag stem while slipping {Lv(dragS)}; ambience {Lv(amb)}; fx peak {Dsp.Db(Dsp.Peak(eng.Fx.ToArray())):0.0}");
        if (reelC.Length > 0)
        {
            Console.WriteLine($"    ambience sits {Dsp.Db(Dsp.Rms(reelC)) - Dsp.Db(Dsp.Rms(amb)):0.0} dB under the reel while cranking");
            var sp = Meter.WelchBands(reelC, Engine.Rate);
            Console.WriteLine($"    reel while cranking: centroid {sp.CentroidHz:0} Hz | FLAT {Meter.Shares(sp.Flat)} | A {Meter.Shares(sp.A)} | PHONE-A {Meter.Shares(sp.PhoneA)}");
            var ed = Meter.EnvelopeDepth(reelC, Engine.Rate);
            Console.WriteLine($"    reel envelope (10 ms RMS) p10..p90 range {ed.rangeDb:0.0} dB, modulation depth {100 * ed.depth:0}%; crest {Dsp.Db(Dsp.Peak(reelC) / Dsp.Rms(reelC)):0.0} dB");
            Console.WriteLine($"    reel 1/3-oct dB: {Meter.ThirdOctaves(Dsp.Welch(reelC, 4096), Engine.Rate / 4096.0)}");
        }
        if (dragS.Length > 0)
        {
            var sp = Meter.WelchBands(dragS, Engine.Rate);
            Console.WriteLine($"    drag while slipping: centroid {sp.CentroidHz:0} Hz | FLAT {Meter.Shares(sp.Flat)} | A {Meter.Shares(sp.A)} | PHONE-A {Meter.Shares(sp.PhoneA)}; peak vs reel RMS {Dsp.Db(Dsp.Peak(dragS)) - Dsp.Db(Dsp.Peak(reelC.Length > 0 ? reelC : dragS)):+0.0} dB peak-to-peak");
        }
        // Timeline (1 s): stem RMS
        Console.WriteLine("  timeline (1 s): t | state/phase | crank% slip% | reel drag tension fx amb (RMS dBFS, '.' < -60) | mix");
        int per = Engine.Rate;
        for (int s = 0; s + per <= eng.Reel.Count; s += per)
        {
            string D(List<float> st) { double r = Math.Sqrt(st.Skip(s).Take(per).Average(v => (double)v * v)); return r < 1e-3 ? "     ." : $"{Dsp.Db(r),6:0}"; }
            var rs = recs.Where(r => r.S0 >= s && r.S0 < s + per).ToList();
            string st = rs.Count > 0 ? rs.GroupBy(r => r.State == LakeState.Fight ? "F" : r.State.ToString()).OrderByDescending(g2 => g2.Count()).First().Key : "";
            double mixR = Math.Sqrt(Enumerable.Range(s, per).Average(i => (double)mixAll[i] * mixAll[i]));
            Console.WriteLine($"    {s / per,3} {st,-8} {100.0 * rs.Count(r => r.Cranking) / Math.Max(1, rs.Count),4:0} {100.0 * rs.Count(r => r.Slipping) / Math.Max(1, rs.Count),4:0} |{D(eng.Reel)}{D(eng.Drag)}{D(eng.Tension)}{D(eng.Fx)}{D(eng.Ambience)} | {Dsp.Db(mixR),5:0}");
        }
        return eng;
    }

    static double Median(IEnumerable<double> x) => Pct(x, 0.5);

    static double Pct(IEnumerable<double> x, double p)
    {
        var l = x.OrderBy(v => v).ToList();
        return l.Count == 0 ? 0 : l[(int)Math.Min(l.Count - 1, Math.Round(p * (l.Count - 1)))];
    }

    // =====================================================================================
    // 5. Variant sheets: every variant of the repeated sounds, spaced out
    // =====================================================================================

    static void Sheets()
    {
        void Sheet(string name, float[][] v, double gap)
        {
            Wav(name, Pad(Concat(v, gap, SR), 0.05, 0.1), SR, 0.5 / v.Max(c => Dsp.Peak(c)));
        }
        Sheet("50_variants_rotor_x16", Bank.Rotor, 0.2);
        Sheet("51_variants_drag_bright8_dark8", Bank.Drag, 0.2);
        Sheet("52_variants_creak_quiet6_loud6", Bank.Creak, 0.35);
        Sheet("53_variants_strain_small6_big6", Bank.Strain, 0.25);
        Sheet("54_variants_tock_x8", Bank.Tock, 0.25);
    }
}
