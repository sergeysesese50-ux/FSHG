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
    // LakeState lives in FishingController.cs (MonoBehaviour) in the project; copy here.
    public enum LakeState { Idle, Casting, Waiting, Bite, Fight, Caught, Lost }
}

internal static class Program
{
    const int SR = FightSynth.SampleRate;
    const float Master = 0.8f; // FightFeedback.DefaultMasterVolume
    static string OutDir;
    static readonly List<string> Wavs = new List<string>();

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        OutDir = args.Length > 0 ? args[0] : RepoPaths.Previews("current");
        Directory.CreateDirectory(OutDir);
        Clips();
        WhirAnatomy();
        OtherAnatomy();
        Demos();
        Console.WriteLine();
        Console.WriteLine("WAVS:");
        foreach (var w in Wavs) Console.WriteLine("  " + w);
        return 0;
    }

    static void Wav(string name, float[] s, int fs, int repeat = 1)
    {
        string p = Path.Combine(OutDir, name + ".wav");
        Dsp.WriteWav(p, s, fs, repeat);
        Wavs.Add(p);
    }

    // =====================================================================================
    // 1. Every clip
    // =====================================================================================

    static void Clips()
    {
        var loops = new (string name, float[] s, double f0)[]
        {
            ("Whir", FightSynth.Whir(), 220), ("Growl", FightSynth.Growl(), 0),
            ("ClickLoop", FightSynth.ClickLoop(), 40), ("LineSing", FightSynth.LineSing(), 600),
        };
        var shots = new List<(string name, float[] s)>();
        for (int i = 0; i < 3; i++) shots.Add(("DragClick" + i, FightSynth.DragClick(0xC11Cu + (uint)i * 7919u)));
        for (int i = 0; i < 3; i++) shots.Add(("RodCreak" + i, FightSynth.RodCreak(0xC4EAu + (uint)i * 104729u)));
        shots.Add(("Splash", FightSynth.Splash(false)));
        shots.Add(("SplashHeavy", FightSynth.Splash(true)));
        shots.Add(("TelegraphSwirl", FightSynth.TelegraphSwirl()));
        shots.Add(("JerkThunk", FightSynth.JerkThunk()));
        shots.Add(("JumpWhoosh", FightSynth.JumpWhoosh()));
        shots.Add(("Hookset", FightSynth.HooksetThunk()));
        shots.Add(("LineSnap", FightSynth.LineSnap()));
        shots.Add(("RodCrack", FightSynth.RodCrack()));
        shots.Add(("EscapePlip", FightSynth.EscapePlip()));
        shots.Add(("LandedChime", FightSynth.LandedChime()));

        Console.WriteLine("=== CLIPS (22050 Hz mono, values as generated, before source volume) ===");
        Console.WriteLine("crest = peak/RMS over the whole clip; stCrest = peak / loudest-20ms RMS; maxStep = largest 1-sample jump / peak;");
        Console.WriteLine("p999Step = 99.9th-pct 1-sample jump / RMS; imp = 2nd-difference outliers > 6 robust sigma; kurt = excess kurtosis");
        Console.WriteLine($"{"clip",-15}{"ms",6}{"peak",7}{"rms",7}{"crest",6}{"stCr",6}{"DC",9}{"cent",7}{"centA",7}{"kurt",7}{"maxStp",7}{"p999",6}{"imp",5}");
        var specRows = new List<(string, Dsp.Spectrum)>();
        foreach (var (name, s, f0) in loops)
        {
            var sp = Dsp.Bands(Dsp.Power(s), (double)SR / s.Length);
            Row(name, s, sp, true);
            specRows.Add((name, sp));
            Wav("clip_" + name + "_loop", s, SR, (int)Math.Ceiling(4.0 * SR / s.Length));
        }
        foreach (var (name, s) in shots)
        {
            int pad = Math.Max(16384, s.Length);
            var sp = Dsp.Bands(Dsp.Power(s, pad), (double)SR / pad);
            Row(name, s, sp, false);
            specRows.Add((name, sp));
            Wav("clip_" + name, s, SR);
        }

        // As heard in game: the 3 click variants in rotation at 12/s, the 3 creak variants every 0.35 s.
        Wav("clip_DragClick_rotation_12ps", Train(Enumerable.Range(0, 3).Select(i => FightSynth.DragClick(0xC11Cu + (uint)i * 7919u)).ToArray(), 1.0 / 12, 2.5, 0.3f), SR);
        Wav("clip_RodCreak_rotation", Train(Enumerable.Range(0, 3).Select(i => FightSynth.RodCreak(0xC4EAu + (uint)i * 104729u)).ToArray(), 0.35, 2.5, 0.45f), SR);

        Console.WriteLine();
        Console.WriteLine("Spectral energy share (%), bands <300 | 300-2k | 2k-5k | >5k. FLAT = raw; A = A-weighted (what the ear weighs);");
        Console.WriteLine("PHONE-A = A-weighted after a 2nd-order 700 Hz high-pass (rough phone loudspeaker).");
        Console.WriteLine($"{"clip",-15}{"FLAT",-26}{"A-WEIGHTED",-26}{"PHONE-A",-26}");
        foreach (var (name, sp) in specRows)
            Console.WriteLine($"{name,-15}{Shares(sp.Flat),-26}{Shares(sp.A),-26}{Shares(sp.PhoneA),-26}");

        Console.WriteLine();
        Console.WriteLine("Loops: seam (wrap point) and periodicity");
        foreach (var (name, s, f0) in loops)
        {
            var (step, stepRank, curvRank) = Dsp.Seam(s);
            string harm = "";
            if (f0 > 0)
            {
                var p = Dsp.Power(s);
                double binsPerH = f0 * s.Length / SR;
                double h = 0, tot = 0;
                for (int k = 1; k < p.Length; k++)
                {
                    tot += p[k];
                    if (Math.Abs(k / binsPerH - Math.Round(k / binsPerH)) < 1e-9) h += p[k];
                }
                harm = $" harmonic(of {f0} Hz) energy {100 * h / tot:0.0}%, inharmonic {100 * (1 - h / tot):0.00}%";
            }
            var mod = Dsp.Modulation(s, SR);
            string mods = string.Join(", ", mod.Take(4).Select(m => $"{m.hz:0.#} Hz depth {m.depth * 100:0}%"));
            Console.WriteLine($"  {name,-10} length {1000.0 * s.Length / SR:0} ms (repeats {SR / (double)s.Length:0.##}x/s at pitch 1); seam step {step:0.0000} = pct {stepRank:0.0} of steps, curvature pct {curvRank:0.0};{harm}");
            Console.WriteLine($"             AM (envelope) components 1-40 Hz: {mods}");
        }
    }

    static float[] Train(float[][] variants, double every, double seconds, float vol)
    {
        var o = new float[(int)(seconds * SR)];
        int k = 0;
        for (double t = 0.05; t < seconds - 0.4; t += every, k++)
        {
            var c = variants[k % variants.Length];
            int at = (int)(t * SR);
            for (int i = 0; i < c.Length && at + i < o.Length; i++) o[at + i] += c[i] * vol;
        }
        return o;
    }

    static string Shares(double[] s) => string.Join(" ", s.Select(v => $"{100 * v,5:0.0}"));

    static void Row(string name, float[] s, Dsp.Spectrum sp, bool circular)
    {
        double peak = Dsp.Peak(s), rms = Dsp.Rms(s), st = Dsp.MaxShortRms(s, SR / 50);
        var (imp, maxStep, p999) = Dsp.Edges(s, circular);
        Console.WriteLine($"{name,-15}{1000.0 * s.Length / SR,6:0}{Dsp.Db(peak),7:0.0}{Dsp.Db(rms),7:0.0}{Dsp.Db(peak / rms),6:0.0}{Dsp.Db(peak / st),6:0.0}{Dsp.Mean(s),9:0.00000}{sp.CentroidHz,7:0}{sp.CentroidAHz,7:0}{Dsp.ExcessKurtosis(s),7:0.0}{maxStep,7:0.00}{p999,6:0.0}{imp,5}");
    }

    // =====================================================================================
    // 2. Whir anatomy (the "reel" sound)
    // =====================================================================================

    static double Frac(double x) => x - Math.Floor(x);

    static void WhirAnatomy()
    {
        Console.WriteLine();
        Console.WriteLine("=== WHIR ANATOMY (FightSynth.cs:23-43) ===");
        int n = (int)Math.Round(0.5 * SR);
        // Exact copy of the naive pulse train, FightSynth.cs:27-28.
        var pulses = new float[n];
        for (int i = 0; i < n; i++) pulses[i] = Frac(i * 220.0 / SR) < 0.2 ? 1f : 0f;
        var widths = new Dictionary<int, int>();
        var periods = new Dictionary<int, int>();
        int run = 0, lastRise = -1;
        for (int i = 0; i < n; i++)
        {
            bool rise = pulses[i] > 0 && (i == 0 || pulses[i - 1] == 0);
            if (rise) { if (lastRise >= 0) { periods.TryGetValue(i - lastRise, out int c); periods[i - lastRise] = c + 1; } lastRise = i; }
            if (pulses[i] > 0) run++;
            else if (run > 0) { widths.TryGetValue(run, out int c); widths[run] = c + 1; run = 0; }
        }
        Console.WriteLine($"Naive pulse train: period {SR / 220.0:0.000} samples -> periods {string.Join(", ", periods.OrderBy(k => k.Key).Select(k => $"{k.Key} x{k.Value}"))}; widths {string.Join(", ", widths.OrderBy(k => k.Key).Select(k => $"{k.Key} x{k.Value}"))}");
        var pp = Dsp.Power(pulses);
        double h = 0, tot = 0, worst = 0, worstHz = 0;
        for (int k = 1; k < pp.Length; k++)
        {
            tot += pp[k];
            if (k % 110 == 0) h += pp[k];
            else if (pp[k] > worst) { worst = pp[k]; worstHz = k * 2.0; }
        }
        Console.WriteLine($"  raw pulses: aliasing/jitter (inharmonic) energy {100 * (1 - h / tot):0.00}% ; strongest inharmonic line {10 * Math.Log10(worst / pp[110]):0.0} dB re fundamental at {worstHz:0} Hz");

        var w = FightSynth.Whir();
        var p = Dsp.Power(w);
        double h1 = p[110];
        Console.WriteLine("Whir() harmonic levels at pitch 1 (dB re 1st harmonic; nulls at every 5th harmonic from the 20% duty cycle):");
        var lv = new List<double>();
        for (int k = 1; k <= 25; k++) lv.Add(10 * Math.Log10(p[110 * k] / h1));
        Console.WriteLine("  h1..h25: " + string.Join(" ", lv.Select(v => $"{v:0}")));
        // Slope over non-null harmonics 1..16.
        var xs = new List<double>(); var ys = new List<double>();
        for (int k = 1; k <= 16; k++) if (k % 5 != 0) { xs.Add(Math.Log(k, 2)); ys.Add(lv[k - 1]); }
        double mx = xs.Average(), my = ys.Average();
        double slope = xs.Zip(ys, (a, b) => (a - mx) * (b - my)).Sum() / xs.Sum(a => (a - mx) * (a - mx));
        Console.WriteLine($"  harmonic roll-off h1..h16 = {slope:0.0} dB/octave (a soft, 'warm' tone would be -12 dB/oct or steeper)");
        double harm = 0, all = 0;
        for (int k = 1; k < p.Length; k++) { all += p[k]; if (k % 110 == 0) harm += p[k]; }
        Console.WriteLine($"  whole clip: harmonic {100 * harm / all:0.0}%, inharmonic (white noise 15% + aliasing) {100 * (1 - harm / all):0.0}% of energy");
        // Noise part alone: its spectrum is white up to 11 kHz -> share above 5 kHz.
        double nhi = 0, nall = 0;
        for (int k = 1; k < p.Length; k++) if (k % 110 != 0) { nall += p[k]; if (k * 2.0 > 5000) nhi += p[k]; }
        Console.WriteLine($"  of the inharmonic (noise) part, {100 * nhi / nall:0}% lies above 5 kHz (unfiltered white noise, FightSynth.cs:39)");

        Console.WriteLine("Which harmonics the ear gets, by Whir pitch (0.6 = loaded slow crank, 1.4 = free spin). Columns: f0, A-weighted % in");
        Console.WriteLine("  <300|300-2k|2k-5k|>5k (headphones), same through phone speaker, loudest harmonic (phone+A):");
        foreach (double pitch in new[] { 0.6, 0.8, 1.0, 1.2, 1.4 })
        {
            double[] a = new double[4], ph = new double[4];
            double best = 0; int bestH = 0;
            for (int k = 1; k < p.Length; k++)
            {
                double f = k * 2.0 * pitch;
                int b = f < 300 ? 0 : f < 2000 ? 1 : f < 5000 ? 2 : 3;
                double aw = p[k] * Dsp.AWeight(f);
                a[b] += aw;
                ph[b] += aw * Dsp.PhoneWeight(f);
                if (k % 110 == 0 && aw * Dsp.PhoneWeight(f) > best) { best = aw * Dsp.PhoneWeight(f); bestH = k / 110; }
            }
            double ta = a.Sum(), tp = ph.Sum();
            Console.WriteLine($"  pitch {pitch:0.0}: f0 {220 * pitch,4:0} Hz  A: {string.Join(" ", a.Select(v => $"{100 * v / ta,5:0.0}"))}   phone+A: {string.Join(" ", ph.Select(v => $"{100 * v / tp,5:0.0}"))}   loudest h{bestH} = {220 * pitch * bestH:0} Hz");
        }
        Console.WriteLine("1/3-octave profile of Whir (dB re loudest band), flat | phone+A:");
        ThirdOct(p, 2.0);
    }

    static void ThirdOct(double[] p, double binHz)
    {
        var centers = new List<double>();
        for (double f = 63; f < 10500; f *= Math.Pow(2, 1.0 / 3)) centers.Add(f);
        var flat = new double[centers.Count];
        var phone = new double[centers.Count];
        for (int c = 0; c < centers.Count; c++)
        {
            double lo = centers[c] / Math.Pow(2, 1.0 / 6), hi = centers[c] * Math.Pow(2, 1.0 / 6);
            for (int k = 1; k < p.Length; k++)
            {
                double f = k * binHz;
                if (f >= lo && f < hi) { flat[c] += p[k]; phone[c] += p[k] * Dsp.AWeight(f) * Dsp.PhoneWeight(f); }
            }
        }
        double mf = flat.Max(), mp = phone.Max();
        Console.WriteLine("  Hz    " + string.Join("", centers.Select(f => $"{(f < 1000 ? f.ToString("0") : (f / 1000).ToString("0.#") + "k"),6}")));
        Console.WriteLine("  flat  " + string.Join("", flat.Select(v => $"{10 * Math.Log10(Math.Max(v, 1e-30) / mf),6:0}")));
        Console.WriteLine("  ph+A  " + string.Join("", phone.Select(v => $"{10 * Math.Log10(Math.Max(v, 1e-30) / mp),6:0}")));
    }

    static void OtherAnatomy()
    {
        Console.WriteLine();
        Console.WriteLine("=== GROWL (FightSynth.cs:46-57) 1/3-octave profile ===");
        var g = FightSynth.Growl();
        ThirdOct(Dsp.Power(g), 1.0);
        Console.WriteLine("=== LINE SING (FightSynth.cs:78-91): pitch 1+1.3x (FightCues.cs:497) -> fundamental/2nd harmonic ===");
        foreach (double x in new[] { 0.6, 0.8, 1.0, 1.3 })
            Console.WriteLine($"  LoadShare {x:0.0}: pitch {1 + 1.3 * x:0.00} -> {600 * (1 + 1.3 * x):0} Hz + {1200 * (1 + 1.3 * x):0} Hz, A-weight {10 * Math.Log10(Dsp.AWeight(600 * (1 + 1.3 * x))):+0.0;-0.0} dB");
        Console.WriteLine("=== CLICK LOOP (FightSynth.cs:60-75) ===");
        var starts = Enumerable.Range(0, 20).Select(k => (int)Math.Round(k * SR / 40.0)).ToList();
        Console.WriteLine($"  click spacing samples: {string.Join(",", starts.Zip(starts.Skip(1), (a, b) => b - a).Distinct())} -> strictly periodic 40 Hz x pitch (0.25..3 -> 10..120 clicks/s)");
        var c = FightSynth.ClickLoop();
        ThirdOct(Dsp.Power(c), 2.0);
        Console.WriteLine("=== DRAG CLICK (FightSynth.cs:96-111) 1/3-octave (single click, padded) ===");
        ThirdOct(Dsp.Power(FightSynth.DragClick(0xC11Cu), 22050), 1.0);
        Console.WriteLine("=== ROD CREAK (FightSynth.cs:114-137) 1/3-octave ===");
        ThirdOct(Dsp.Power(FightSynth.RodCreak(0xC4EAu), 22050), 1.0);
    }

    // =====================================================================================
    // 3. Fight demos: FightModel -> FightFrame -> FightCues -> FightFeedback model -> 48 kHz mix
    // =====================================================================================

    sealed class FrameRec
    {
        public float T;
        public LakeState State;
        public bool Cranking, Slipping;
        public float ReelIn, ReelLoad, LoadShare, SlipPerSec, RodShare;
        public float WhirT, WhirP, GrowlT, SingT, SingP, ClickT;
        public float WhirV, WhirPA, GrowlV, SingV, SingPA, ClickV;
        public int SampleStart, SampleEnd;
        public FishPhase Phase;
    }

    static void Demos()
    {
        int floatDrag = FightKit.FloatTackle().AutoDragG;
        int drag020 = FightKit.SpinningTackle(4000).AutoDragG;
        Demo("perch300_HoldReel", FightKit.Perch, 300, FightKit.FloatTackle, floatDrag, 1500, d => FightBots.HoldReel(d), 3, 30);
        Demo("bream2kg_Technique", FightKit.Bream, 2000, FightKit.FloatTackle, floatDrag, 1500, d => FightBots.Technique(d), 3, 30);
        Demo("pike4kg_Technique", FightKit.Pike, 4000, () => FightKit.SpinningTackle(4000), drag020, 3000, d => FightBots.Technique(d), 3, 30);
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
        var cues = new FightCues();
        var mix = new Mixer();
        var recs = new List<FrameRec>();
        float now = 10f;
        float t = 0f;

        void Do(LakeState state)
        {
            now += dt; t += dt;
            cues.Update(state, frame, tackle, 15f, now, dt);
            int s0 = mix.Stems[0].Count;
            mix.Frame(cues, dt, Master);
            cues.Pulses.TryTake(now, out _, out _);
            recs.Add(new FrameRec
            {
                T = t, State = state, Phase = frame.Phase,
                Cranking = state == LakeState.Fight && (frame.ReelInCmPerSec > 0.5f || frame.IsCrankingIntoSlip),
                Slipping = frame.IsDragSlipping, ReelIn = frame.ReelInCmPerSec, ReelLoad = frame.ReelLoad,
                LoadShare = frame.LoadShare, SlipPerSec = frame.SlipCmPerSec, RodShare = frame.RodLoadShare,
                WhirT = cues.WhirVolume, WhirP = cues.WhirPitch, GrowlT = cues.GrowlVolume, SingT = cues.SingVolume,
                SingP = cues.SingPitch, ClickT = cues.ClickLoopVolume,
                WhirV = mix.Whir.AppliedVolume, WhirPA = mix.Whir.AppliedPitch, GrowlV = mix.Growl.AppliedVolume,
                SingV = mix.Sing.AppliedVolume, SingPA = mix.Sing.AppliedPitch, ClickV = mix.ClickLoop.AppliedVolume,
                SampleStart = s0, SampleEnd = mix.Stems[0].Count,
            });
        }

        for (int f = 0; f < 12; f++) { frame.BeginFrame(); Do(LakeState.Waiting); }
        for (int f = 0; f < 36; f++) { frame.BeginFrame(); Do(LakeState.Bite); }
        frame.Start(model, tackle, new Vector3(0.2f, 0, 1f), tick);
        frame.BeginFrame();
        Do(LakeState.Fight);
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
        double fightSeconds = model.ElapsedSeconds;
        for (int f = 0; f < 150 && t < maxSeconds + 2.5; f++) { frame.BeginFrame(); Do(ended ? endState : LakeState.Fight); }

        var m = mix.Mix();
        var bed = mix.Sum(0, 1, 2, 3);
        Wav("fightdemo_" + name, m, Mixer.OutRate);
        Wav("fightdemo_" + name + "_reelbed", bed, Mixer.OutRate);
        Wav("fightdemo_" + name + "_whir", mix.Stems[0].ToArray(), Mixer.OutRate);
        Wav("fightdemo_" + name + "_growl", mix.Stems[1].ToArray(), Mixer.OutRate);

        Console.WriteLine();
        Console.WriteLine($"=== DEMO {name} seed {seed}: outcome {model.Outcome} after {fightSeconds:0.0} s of fight; rendered {m.Length / (double)Mixer.OutRate:0.0} s @48k, master {Master} ===");
        var fight = recs.Where(r => r.State == LakeState.Fight).ToList();
        double fs = fight.Count * dt;
        int crank = fight.Count(r => r.Cranking);
        Console.WriteLine($"  fight frames {fight.Count} ({fs:0.0} s): cranking {100.0 * crank / fight.Count:0}%  slipping {100.0 * fight.Count(r => r.Slipping) / fight.Count:0}%");
        void LoopStat(string ln, Func<FrameRec, float> vol, Func<FrameRec, float> target, Mixer.Voice v)
        {
            var on = fight.Where(r => vol(r) > 0.002f).ToList();
            var onCrank = fight.Where(r => r.Cranking).Count(r => vol(r) > 0.002f);
            string mean = on.Count > 0 ? $"{Dsp.Db(on.Average(r => (double)vol(r))):0.0} dB src vol" : "-";
            Console.WriteLine($"  {ln,-9} audible {100.0 * on.Count / fight.Count,3:0}% of fight ({(crank > 0 ? 100.0 * onCrank / crank : 0),3:0}% of cranking), mean {mean}; starts {v.Starts} ({v.Starts / Math.Max(fs, 1e-9) * 60:0}/min), loud hard cuts {v.LoudCuts}");
        }
        LoopStat("whir", r => r.WhirV, r => r.WhirT, mix.Whir);
        LoopStat("growl", r => r.GrowlV, r => r.GrowlT, mix.Growl);
        LoopStat("sing", r => r.SingV, r => r.SingT, mix.Sing);
        LoopStat("clickloop", r => r.ClickV, r => r.ClickT, mix.ClickLoop);

        var whirOn = fight.Where(r => r.WhirV > 0.002f).ToList();
        if (whirOn.Count > 10)
        {
            var ps = whirOn.Select(r => (double)r.WhirPA).OrderBy(v => v).ToList();
            Console.WriteLine($"  whir pitch while audible p5/p50/p95 = {Pct(ps, 5):0.00}/{Pct(ps, 50):0.00}/{Pct(ps, 95):0.00} -> pulse fundamental {220 * Pct(ps, 5):0}/{220 * Pct(ps, 50):0}/{220 * Pct(ps, 95):0} Hz, loop repeats every {500 / Pct(ps, 50):0} ms");
            // Wobble: applied pitch around its 0.5 s centred moving average.
            Console.WriteLine($"  whir pitch wobble (deviation from 0.5 s moving average): RMS {Wobble(fight, r => r.WhirV > 0.002f, r => 1200 * Math.Log(r.WhirPA, 2)):0} cents; target changes {Changes(fight, r => r.WhirV > 0.002f, r => r.WhirP):0.0}/s");
            var stepsW = TickSteps(fight, r => r.WhirV > 0.002f, r => r.WhirP);
            if (stepsW.Count > 0) Console.WriteLine($"  whir target pitch per-change |step| p50/p90/max = {Pct(stepsW, 50) * 1200:0}/{Pct(stepsW, 90) * 1200:0}/{stepsW.Max() * 1200:0} cents");
        }
        var growlOn = fight.Where(r => r.GrowlV > 0.002f).ToList();
        if (growlOn.Count > 10)
        {
            Console.WriteLine($"  growl volume wobble: RMS {Wobble(fight, r => r.GrowlV > 0.002f, r => Dsp.Db(r.GrowlV)):0.0} dB around 0.5 s average; target changes {Changes(fight, r => r.GrowlV > 0.002f, r => r.GrowlT):0.0}/s; ReelLoad p50 {Pct(growlOn.Select(r => (double)r.ReelLoad).OrderBy(v => v).ToList(), 50):0.00}");
            var gl = growlOn.Select(r => (double)r.GrowlV / Master).OrderBy(v => v).ToList();
            Console.WriteLine($"  growl source volume (pre-master) p50/p95 {Pct(gl, 50):0.00}/{Pct(gl, 95):0.00} vs whir p50 {Pct(whirOn.Select(r => (double)r.WhirV / Master).OrderBy(v => v).ToList(), 50):0.00}");
        }
        var singOn = fight.Where(r => r.SingV > 0.002f).ToList();
        if (singOn.Count > 0)
        {
            var sp = singOn.Select(r => (double)r.SingPA).OrderBy(v => v).ToList();
            Console.WriteLine($"  sing pitch p5/p50/p95 {Pct(sp, 5):0.00}/{Pct(sp, 50):0.00}/{Pct(sp, 95):0.00} -> {600 * Pct(sp, 5):0}/{600 * Pct(sp, 50):0}/{600 * Pct(sp, 95):0} Hz; wobble {Wobble(fight, r => r.SingV > 0.002f, r => 1200 * Math.Log(r.SingPA, 2)):0} cents RMS");
        }
        Console.WriteLine("  one-shots played: " + string.Join(" ", mix.Played.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")));

        // Mix and stem measurements.
        var all = Dsp.Welch(m);
        var sAll = Dsp.Bands(all, (double)Mixer.OutRate / 8192);
        int clipped = m.Count(v => Math.Abs(v) > 1f);
        Console.WriteLine($"  MIX: peak {Dsp.Db(Dsp.Peak(m)):0.0} dBFS, RMS {Dsp.Db(Dsp.Rms(m)):0.0} dBFS, crest {Dsp.Db(Dsp.Peak(m) / Dsp.Rms(m)):0.0} dB, DC {Dsp.Mean(m):0.00000}, samples >1.0: {clipped}");
        Console.WriteLine($"       FLAT {Shares(sAll.Flat)} | A {Shares(sAll.A)} | PHONE-A {Shares(sAll.PhoneA)} | centroid {sAll.CentroidHz:0} Hz (A {sAll.CentroidAHz:0}) | >11.025 kHz (resampler images) {100 * sAll.Above11kShare:0.00}%");
        double totalA = 0;
        var stemA = new double[6];
        var stemP = new double[6];
        for (int k = 0; k < 6; k++)
        {
            var arr = mix.Stems[k].ToArray();
            var w = Dsp.Welch(arr);
            double bin = (double)Mixer.OutRate / 8192;
            for (int j = 1; j < w.Length; j++) { stemA[k] += w[j] * Dsp.AWeight(j * bin); stemP[k] += w[j] * Dsp.AWeight(j * bin) * Dsp.PhoneWeight(j * bin); }
            totalA += stemA[k];
        }
        double totalP = stemP.Sum();
        Console.WriteLine("  stem share of A-weighted energy (headphones / phone): " + string.Join("  ", Enumerable.Range(0, 6).Select(k => $"{Mixer.StemNames[k]} {100 * stemA[k] / totalA:0}/{100 * stemP[k] / totalP:0}%")));

        // Reel bed only while cranking (what plays under the thumb on "Reel").
        var crankSamples = new List<float>();
        var crankWhir = new List<float>();
        foreach (var r in fight.Where(r => r.Cranking))
            for (int i = r.SampleStart; i < r.SampleEnd; i++) { crankSamples.Add(bed[i]); crankWhir.Add(mix.Stems[0][i]); }
        if (crankSamples.Count > 8192 * 2)
        {
            var cb = crankSamples.ToArray();
            var sb = Dsp.Bands(Dsp.Welch(cb), (double)Mixer.OutRate / 8192);
            Console.WriteLine($"  REEL BED while cranking ({cb.Length / (double)Mixer.OutRate:0.0} s): RMS {Dsp.Db(Dsp.Rms(cb)):0.0} dBFS, peak {Dsp.Db(Dsp.Peak(cb)):0.0}, crest {Dsp.Db(Dsp.Peak(cb) / Dsp.Rms(cb)):0.0} dB, kurtosis {Dsp.ExcessKurtosis(cb):0.0}");
            Console.WriteLine($"       FLAT {Shares(sb.Flat)} | A {Shares(sb.A)} | PHONE-A {Shares(sb.PhoneA)} | centroid {sb.CentroidHz:0} Hz (A {sb.CentroidAHz:0})");
            var cw = crankWhir.ToArray();
            var sw = Dsp.Bands(Dsp.Welch(cw), (double)Mixer.OutRate / 8192);
            Console.WriteLine($"       whir stem alone while cranking: RMS {Dsp.Db(Dsp.Rms(cw)):0.0} dBFS | A {Shares(sw.A)} | PHONE-A {Shares(sw.PhoneA)} | centroid A {sw.CentroidAHz:0} Hz");
        }

        // Timeline per second.
        Console.WriteLine("  timeline (1 s): t | phase | crank% | whir growl sing clkloop ticks fx  (stem RMS dBFS, '.' = silent) | mix");
        int perSec = Mixer.OutRate;
        int secs = m.Length / perSec;
        for (int sIdx = 0; sIdx < secs; sIdx++)
        {
            var rs = recs.Where(r => r.T > sIdx && r.T <= sIdx + 1).ToList();
            string phase = rs.Count > 0 ? rs.GroupBy(r => r.State == LakeState.Fight ? r.Phase.ToString() : r.State.ToString()).OrderByDescending(g => g.Count()).First().Key : "";
            double cr = rs.Count > 0 ? 100.0 * rs.Count(r => r.Cranking) / rs.Count : 0;
            string Lv(int k)
            {
                double sum = 0;
                for (int i = sIdx * perSec; i < (sIdx + 1) * perSec; i++) sum += (double)mix.Stems[k][i] * mix.Stems[k][i];
                double r = Math.Sqrt(sum / perSec);
                return r < 1e-4 ? "     ." : $"{Dsp.Db(r),6:0}";
            }
            double ms2 = 0;
            for (int i = sIdx * perSec; i < (sIdx + 1) * perSec; i++) ms2 += (double)m[i] * m[i];
            Console.WriteLine($"   {sIdx,2} {phase,-7} {cr,4:0} |{Lv(0)}{Lv(1)}{Lv(2)}{Lv(3)}{Lv(4)}{Lv(5)} | {Dsp.Db(Math.Sqrt(ms2 / perSec)),5:0}");
        }

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, name + "_frames.csv"),
            new[] { "t,state,phase,crank,slip,reelIn,reelLoad,loadShare,rodShare,slipPerSec,whirT,whirP,whirV,whirPA,growlT,growlV,singT,singP,singV,clickT,clickV" }
            .Concat(recs.Select(r => string.Join(",", r.T, r.State, r.Phase, r.Cranking ? 1 : 0, r.Slipping ? 1 : 0, r.ReelIn, r.ReelLoad, r.LoadShare, r.RodShare, r.SlipPerSec, r.WhirT, r.WhirP, r.WhirV, r.WhirPA, r.GrowlT, r.GrowlV, r.SingT, r.SingP, r.SingV, r.ClickT, r.ClickV))));
    }

    static double Pct(List<double> sorted, double pct) => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * pct / 100))];

    static double Wobble(List<FrameRec> recs, Func<FrameRec, bool> on, Func<FrameRec, double> val)
    {
        const int half = 15;
        double sum = 0; int n = 0;
        for (int i = half; i < recs.Count - half; i++)
        {
            bool all = true;
            for (int j = i - half; j <= i + half && all; j++) all = on(recs[j]);
            if (!all) continue;
            double avg = 0;
            for (int j = i - half; j <= i + half; j++) avg += val(recs[j]);
            avg /= 2 * half + 1;
            double d = val(recs[i]) - avg;
            sum += d * d; n++;
        }
        return n > 0 ? Math.Sqrt(sum / n) : double.NaN;
    }

    static double Changes(List<FrameRec> recs, Func<FrameRec, bool> on, Func<FrameRec, float> val)
    {
        int c = 0, n = 0;
        for (int i = 1; i < recs.Count; i++)
        {
            if (!on(recs[i]) || !on(recs[i - 1])) continue;
            n++;
            if (Math.Abs(val(recs[i]) - val(recs[i - 1])) > 1e-4f) c++;
        }
        return n > 0 ? c / (n / 60.0) : 0;
    }

    static List<double> TickSteps(List<FrameRec> recs, Func<FrameRec, bool> on, Func<FrameRec, float> val)
    {
        var l = new List<double>();
        for (int i = 1; i < recs.Count; i++)
        {
            if (!on(recs[i]) || !on(recs[i - 1])) continue;
            float a = val(recs[i - 1]), b = val(recs[i]);
            if (Math.Abs(b - a) > 1e-4f && a > 0 && b > 0) l.Add(Math.Abs(Math.Log(b / a, 2)));
        }
        l.Sort();
        return l;
    }
}
