using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Fishing.Core;
using Fishing.Core.Audio;
using Fishing.Tests;
using Fishing.Unity;
using UnityEngine;

/// <summary>
/// Звук игры офлайн: тот же код, что в Unity (FightSoundBank → FightAudioDirector → FightAudioMixer → SoftLimiter, вход —
/// FightFrame через FightAudioInput.Of), кадры 60 к/с и звуковые буферы 1024 × 48 кГц идут вперемешку, как два потока.
///   dotnet run -c Release --project tools/audio/AudioIngame -- [папка вывода]
/// Пишет WAV в .previews/audio/ingame, отчёт — в out.txt рядом с проектом, и сравнивает с превью «Натуральной» палитры.
/// </summary>
internal static class Program
{
    const int Rate = 48000;
    const int Buffer = 1024;
    const float Master = 0.8f;
    const float Dt = 1f / 60f;
    const double NaturalDemoTrimDb = 3.4;     // превью natural/40–43 сделаны на +3,4 дБ громче игрового уровня
    const double NaturalScenarioTrimDb = 1.4; // natural/01–07, 27 — на +1,4 дБ
    const double NaturalFightStart = 3.9;     // подсечка в natural/40–43 (234 кадра)

    static string Out;
    static FightSoundBank Bank;
    static readonly StringBuilder Rep = new StringBuilder();
    static readonly List<string> Files = new List<string>();
    static string HarnessDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    static string NaturalDir => RepoPaths.Previews("natural");

    static void Log(string s = "")
    {
        Console.WriteLine(s);
        Rep.AppendLine(s);
    }

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Out = args.FirstOrDefault(a => !a.StartsWith("--")) ?? RepoPaths.Previews("ingame");
        Directory.CreateDirectory(Out);
        Log($"AudioIngame {DateTime.Now:yyyy-MM-dd HH:mm}; out {Out}; mixer makeup {FightAudioMixer.Makeup:0.00} ({M.Db(FightAudioMixer.Makeup):+0.0} dB), master {Master}");

        BankSection();
        ClipSection();
        var perch = FightSection("perch300_holdreel", FightKit.Perch(), 300, d => FightBots.HoldReel(d), 40, "perch300_reel_only");
        var bream = FightSection("bream2kg_technique", FightKit.Bream(), 2000, d => FightBots.Technique(d), 41, "bream2kg_reel_drag_only");
        LoudnessSummary(perch, bream);
        var scen = ScenarioSection();
        AmbienceSection();
        CpuSection(perch, bream);
        CompareSection(perch, bream, scen);

        Log();
        Log("FILES:");
        foreach (var f in Files)
            Log("  " + f);
        File.WriteAllText(Path.Combine(HarnessDir, "out.txt"), Rep.ToString(), new UTF8Encoding(false));
        return 0;
    }

    // =====================================================================================================
    // Симуляция: главный поток (кадры) и звуковой поток (буферы) вперемешку
    // =====================================================================================================

    sealed class Sim
    {
        public readonly FightAudioDirector Director = new FightAudioDirector();
        public readonly FightAudioMixer Mixer;
        public readonly List<float> Mono = new List<float>();
        public readonly List<double> RenderMicros = new List<double>();
        readonly float[] _buf;
        double _tFrame, _tAudio;
        public bool OneShots = true, Ambience = true, Reel = true;
        public float Night, MasterVolume = Master;
        public double Time => _tFrame;

        public Sim(FightSoundBank bank, int bufferFrames = Buffer, uint seed = 0x5EEDu)
        {
            Mixer = new FightAudioMixer(bank, seed) { GrainLog = new GrainLogEntry[100000] };
            _buf = new float[bufferFrames * 2];
        }

        public void Frame(LakeSoundState state, in FightSoundInput f, float c0, float floatM, float dt = Dt)
        {
            Director.Update(state, f, c0, floatM, Night, MasterVolume, Rate);
            var p = Director.Params;
            if (!Ambience)
                p.AmbienceVolume = 0f;
            if (!Reel)
            {
                p.RotorCmPerSec = 0f;
                p.Slipping = false;
                p.LayVolume = 0f;
                p.HumVolume = 0f;
                p.Fight = false;
            }
            Mixer.Publish(p);
            for (int i = 0; i < Director.EventCount; i++)
            {
                var e = Director.Event(i);
                if (!OneShots && e.Kind == FightAudioEventKind.OneShot)
                    continue;
                if (!Reel && e.Kind != FightAudioEventKind.OneShot)
                    continue;
                Mixer.Post(e);
            }
            _tFrame += dt;
            while (_tAudio < _tFrame)
                RenderBuffer();
        }

        void RenderBuffer()
        {
            long t0 = Stopwatch.GetTimestamp();
            Mixer.Render(_buf, 2);
            long t1 = Stopwatch.GetTimestamp();
            RenderMicros.Add((t1 - t0) * 1e6 / Stopwatch.Frequency);
            int frames = _buf.Length / 2;
            for (int i = 0; i < frames; i++)
                Mono.Add(_buf[2 * i]);
            _tAudio += (double)frames / Rate;
        }

        public double[] Samples(double from = 0, double to = double.MaxValue)
        {
            int a = Math.Max(0, (int)(from * Rate)), b = Math.Min(Mono.Count, to >= double.MaxValue / 2 ? Mono.Count : (int)(to * Rate));
            var x = new double[Math.Max(0, b - a)];
            for (int i = 0; i < x.Length; i++)
                x[i] = Mono[a + i];
            return x;
        }
    }

    // =====================================================================================================
    // 1. Банк
    // =====================================================================================================

    static void BankSection()
    {
        Log();
        Log("=== BANK ===");
        var b1 = new FightSoundBank();
        var b2 = new FightSoundBank();
        var b3 = new FightSoundBank();
        Bank = b3;
        bool same = b1.LakeBed.SequenceEqual(b2.LakeBed) && b1.Rotor[5].SequenceEqual(b2.Rotor[5]) && b1.Creak[9].SequenceEqual(b2.Creak[9])
            && b1.Crickets[2].SequenceEqual(b2.Crickets[2]) && b1.OneShot(FightOneShot.Landed, 3, false).SequenceEqual(b2.OneShot(FightOneShot.Landed, 3, false));
        Log($"  build (desktop, Release, one thread): first {b1.BuildMilliseconds:0} ms (incl. JIT), then {b2.BuildMilliseconds:0} / {b3.BuildMilliseconds:0} ms; bit-identical rebuild: {same}");
        Log($"  {b3.TotalSamples} samples = {b3.TotalSamples * 4 / 1048576.0:0.0} MB as float; lake bed {b3.LakeBed.Length / (double)FightSoundBank.SampleRate:0.0} s");
        var sw = Stopwatch.StartNew();
        var bed = LakeSynth.LakeBed(64, out _);
        double bedMs = sw.Elapsed.TotalMilliseconds;
        Log($"  of which the 64 s lake bed alone {bedMs:0} ms");
    }

    // =====================================================================================================
    // 2. Клипы: различимость вариантов, швы петель
    // =====================================================================================================

    static void ClipSection()
    {
        Log();
        Log("=== CLIPS: variant distinctness (AudioCritic Variants: distinct = clusters at similarity 0.97) and loop seams ===");
        var tock = Sheet(Bank.Tock, 0.25);
        var creak = Sheet(Bank.Creak, 0.35);
        var rotor = Sheet(Bank.Rotor, 0.2);
        var drag = Sheet(Bank.Drag, 0.2);
        var strainSmall = Sheet(Bank.Strain.Take(FightSoundBank.StrainVariants).ToArray(), 0.2);
        var strainBig = Sheet(Bank.Strain.Skip(FightSoundBank.StrainVariants).ToArray(), 0.2);
        var crackle = Sheet(Bank.Crackle, 0.2);
        WavF("54_variants_tock_x8", tock, FightSoundBank.SampleRate, 0.5 / Peak(tock));
        WavF("52_variants_creak_quiet6_loud6", creak, FightSoundBank.SampleRate, 0.5 / Peak(creak));
        WavF("50_variants_rotor_x16", rotor, FightSoundBank.SampleRate, 0.5 / Peak(rotor));
        WavF("51_variants_drag_bright8_dark8", drag, FightSoundBank.SampleRate, 0.5 / Peak(drag));
        var strainAll = Sheet(Bank.Strain, 0.2);
        WavF("55_variants_strain_small6_big6", strainAll, FightSoundBank.SampleRate, 0.5 / Peak(strainAll));
        WavF("56_variants_crackle_x8", crackle, FightSoundBank.SampleRate, 0.5 / Peak(crackle));
        void Var(string label, double[] mine, string natural)
        {
            var v = Variants.Analyze(mine, FightSoundBank.SampleRate);
            string nat = "-";
            string np = Path.Combine(NaturalDir, natural + ".wav");
            if (File.Exists(np))
            {
                var a = Wav.Read(np);
                var nv = Variants.Analyze(a.X, a.Fs);
                nat = $"{nv.Clusters} of {nv.N} (nearest p50 {nv.NearestMed:0.00})";
            }
            Log($"  {label,-10} ingame {v.Clusters} distinct of {v.N} (nearest-sibling p50 {v.NearestMed:0.00}, pairwise mean {v.PairMean:0.00})   natural preview {nat}");
        }
        Var("tock", ToD(tock), "54_variants_tock_x8");
        Var("creak", ToD(creak), "52_variants_creak_quiet6_loud6");
        Var("rotor", ToD(rotor), "50_variants_rotor_x16");
        Var("drag", ToD(drag), "51_variants_drag_bright8_dark8");
        Var("strain S", ToD(strainSmall), "-");
        Var("strain B", ToD(strainBig), "-");
        Var("crackle", ToD(crackle), "-");
        // Тихие против громких скрипов попарно: самое большое сходство i-го тихого с i-м громким.
        double maxPair = 0;
        for (int i = 0; i < FightSoundBank.CreakVariants; i++)
            maxPair = Math.Max(maxPair, Sim2(Bank.Creak[i], Bank.Creak[i + FightSoundBank.CreakVariants]));
        Log($"  creak quiet[i] vs loud[i]: max similarity {maxPair:0.00}; centroid quiet {Centroid(Bank.Creak.Take(6))} Hz, loud {Centroid(Bank.Creak.Skip(6))} Hz");
        Log($"  tock fundamentals: {string.Join(" ", Bank.Tock.Select(c => $"{PeakHz(c, 250, 700):0}"))} Hz");

        Log("  loop seams (AudioCritic SeamAt at the wrap; flags: SEAM = audible discontinuity, soft = level step):");
        foreach (var (name, clip) in new[] { ("lake bed", Bank.LakeBed), ("lay bed", Bank.LayBed), ("line hum", Bank.LineHum) })
            Log($"    {name,-9} {M.SeamAt(ToD(clip), FightSoundBank.SampleRate, 0)}");
        var natBed = NaturalSynth.LakeMorning(64);
        Log($"    natural ambience (prototype, for reference) {M.SeamAt(ToD(natBed), FightSoundBank.SampleRate, 0)}");
        var rep = M.FeatureRepeat(ToD(Bank.LakeBed), FightSoundBank.SampleRate, 1.0, true);
        Log($"  lake bed self-similarity (1 s features, circular): max r {rep.r:0.00} at lag {rep.lag:0.0} s (1 = exact repeat)");

        int k = 3 * FightSoundBank.SampleRate;
        var seam = new float[2 * k];
        Array.Copy(Bank.LakeBed, Bank.LakeBed.Length - k, seam, 0, k);
        Array.Copy(Bank.LakeBed, 0, seam, k, k);
        WavF("31_lakebed_seam_check", seam, FightSoundBank.SampleRate, Math.Pow(10, -26.0 / 20) / Rms(Bank.LakeBed));
    }

    static float[] Sheet(float[][] v, double gap)
    {
        var o = new List<float>();
        o.AddRange(new float[(int)(0.05 * FightSoundBank.SampleRate)]);
        foreach (var c in v)
        {
            o.AddRange(c);
            o.AddRange(new float[(int)(gap * FightSoundBank.SampleRate)]);
        }
        return o.ToArray();
    }

    /// <summary>Наибольшая нормированная взаимная корреляция двух клипов (±0,5 мс), как в AudioCritic.</summary>
    static double Sim2(float[] a, float[] b)
    {
        int sh = (int)(0.0005 * FightSoundBank.SampleRate), n = Math.Min(a.Length, b.Length) - 2 * sh;
        double best = 0;
        for (int s = -sh; s <= sh; s++)
        {
            double ab = 0, aa = 0, bb = 0;
            for (int i = sh; i < sh + n; i++)
            {
                double x = a[i], y = b[i + s];
                ab += x * y; aa += x * x; bb += y * y;
            }
            best = Math.Max(best, ab / Math.Sqrt(Math.Max(1e-30, aa * bb)));
        }
        return best;
    }

    static string Centroid(IEnumerable<float[]> clips)
    {
        int seg = 2048;
        double[] acc = null;
        foreach (var c in clips)
        {
            var p = M.Welch(ToD(c), seg);
            acc ??= new double[p.Length];
            for (int i = 0; i < p.Length; i++)
                acc[i] += p[i];
        }
        return $"{M.Bands(acc, (double)FightSoundBank.SampleRate / seg).Cpow:0}";
    }

    static double PeakHz(float[] c, double lo, double hi)
    {
        int seg = 8192;
        var x = new double[seg];
        for (int i = 0; i < Math.Min(seg, c.Length); i++)
            x[i] = c[i];
        var p = M.Welch(x, seg);
        double bin = (double)FightSoundBank.SampleRate / seg, best = 0, hz = 0;
        for (int k = (int)(lo / bin); k <= (int)(hi / bin) && k < p.Length; k++)
            if (p[k] > best)
            {
                best = p[k];
                hz = k * bin;
            }
        return hz;
    }

    // =====================================================================================================
    // 3. Бои (настоящий FightModel)
    // =====================================================================================================

    sealed class Rec
    {
        public double T;
        public LakeSoundState State;
        public bool Cranking, Slipping;
    }

    sealed class FightRun
    {
        public string Name;
        public Sim Full, ReelOnly, AmbOnly;
        public List<Rec> Recs;
        public double FightStart, FightEnd;
        public FightOutcome Outcome;
        public double FightSeconds;
    }

    static Sim RunFight(FishSpecies species, int weight, Func<int, FightBot> makeBot, bool oneShots, bool ambience, bool reel, List<Rec> recs,
        out double fightStart, out double fightEnd, out FightOutcome outcome, out double fightSeconds)
    {
        const double tick = 0.05;
        const uint seed = 3;
        var tackle = FightKit.FloatTackle();
        int drag = tackle.AutoDragG;
        var model = new FightModel(species, weight, tackle, 1500, new SeededRandom(seed), null, 0, 1.0, seed);
        var bot = makeBot(drag);
        var frame = new FightFrame();
        var sim = new Sim(Bank) { OneShots = oneShots, Ambience = ambience, Reel = reel };
        sim.Director.RewardTier = weight >= species.weightG.trophy ? 3 : weight >= species.weightG.typical ? 2 : 1;
        float c0 = (float)tackle.ReelFightSpeedCmPerSec;

        void Do(LakeSoundState state)
        {
            var f = state == LakeSoundState.Fight || frame.EndedThisFrame ? FightAudioInput.Of(frame) : default;
            recs?.Add(new Rec
            {
                T = sim.Time, State = state,
                Cranking = state == LakeSoundState.Fight && (frame.ReelInCmPerSec > 0.5f || frame.IsCrankingIntoSlip),
                Slipping = state == LakeSoundState.Fight && frame.IsDragSlipping,
            });
            sim.Frame(state, f, c0, 15f);
        }

        for (int i = 0; i < 18; i++) { frame.BeginFrame(); Do(LakeSoundState.Idle); }
        for (int i = 0; i < 66; i++) { frame.BeginFrame(); Do(LakeSoundState.Casting); }
        for (int i = 0; i < 120; i++) { frame.BeginFrame(); Do(LakeSoundState.Waiting); }
        for (int i = 0; i < 30; i++) { frame.BeginFrame(); Do(LakeSoundState.Bite); }
        fightStart = sim.Time;
        frame.Start(model, tackle, new Vector3(0.2f, 0, 1f), tick);
        frame.BeginFrame();
        Do(LakeSoundState.Fight);
        double acc = 0;
        bool ended = false;
        var endState = LakeSoundState.Fight;
        while (!ended && sim.Time - fightStart < 26)
        {
            frame.BeginFrame();
            acc += Dt;
            int steps = 0;
            while (acc >= tick && steps < 10 && model.Outcome == FightOutcome.InProgress)
            {
                model.Step(bot(model));
                frame.AddTick(model);
                acc -= tick;
                steps++;
            }
            var state = LakeSoundState.Fight;
            if (model.Outcome == FightOutcome.InProgress)
            {
                frame.Compose((float)(acc / tick));
            }
            else
            {
                frame.End();
                ended = true;
                state = model.Outcome == FightOutcome.Landed ? LakeSoundState.Caught : LakeSoundState.Lost;
                endState = state;
            }
            Do(state);
        }
        fightEnd = sim.Time;
        outcome = model.Outcome;
        fightSeconds = model.ElapsedSeconds;
        for (int i = 0; i < 180; i++) { frame.BeginFrame(); Do(ended ? endState : LakeSoundState.Fight); }
        return sim;
    }

    static FightRun FightSection(string name, FishSpecies species, int weight, Func<int, FightBot> bot, int fileNo, string reelFile)
    {
        var run = new FightRun { Name = name, Recs = new List<Rec>() };
        run.Full = RunFight(species, weight, bot, true, true, true, run.Recs, out run.FightStart, out run.FightEnd, out run.Outcome, out run.FightSeconds);
        run.ReelOnly = RunFight(species, weight, bot, false, false, true, null, out _, out _, out _, out _);
        run.AmbOnly = RunFight(species, weight, bot, false, true, false, null, out _, out _, out _, out _);

        Log();
        Log($"=== FIGHT {name} (seed 3): {run.Outcome} after {run.FightSeconds:0.0} s; fight from {run.FightStart:0.00} s; rendered {run.Full.Mono.Count / (double)Rate:0.0} s @48k, buffers {Buffer} ===");
        var mix = run.Full.Samples();
        var span = run.Full.Samples(run.FightStart, run.FightEnd + 1.0);
        var loud = M.Loudness(span, Rate);
        var phone = M.Loudness(D.Filt(span, Biquad.Hp(Rate, 700, Math.Sqrt(0.5))), Rate);
        Log($"  fight span: integrated {loud.I:0.0} LUFS (phone-speaker model {phone.I:0.0} LUFS), short-term max {loud.Smax:0.0}, LRA {loud.Lra:0.0} LU; sample peak {M.Db(M.Peak(span)):0.0} dBFS, true peak {M.Db(M.TruePeak(span)):0.0} dBTP, |x|>=1: {span.Count(v => Math.Abs(v) >= 1)}");
        var lim = run.Full.Mixer.Limiter;
        Log($"  limiter: deepest {M.Db(lim.MinGain):0.0} dB; gain reduction > 0.1 / 1 / 3 dB on {100.0 * lim.LimitedSamples / mix.Length:0.0} / {100.0 * lim.LimitedOver1Db / mix.Length:0.0} / {100.0 * lim.LimitedOver3Db / mix.Length:0.0}% of samples");
        var sp = Spec(span);
        Log($"  spectrum: centroid {sp.Cpow:0} Hz | FLAT {Sh(sp.Flat)} | A {Sh(sp.A)} | PHONE-A {Sh(sp.Ph)}");

        // Катушка без фона и разовых: уровень, ритм.
        float[] Seg(Sim s, Func<Rec, bool> sel)
        {
            var o = new List<float>();
            var recs = run.Recs;
            for (int i = 0; i < recs.Count; i++)
            {
                if (!sel(recs[i]))
                    continue;
                int a = (int)(recs[i].T * Rate), b = Math.Min(s.Mono.Count, (int)((recs[i].T + Dt) * Rate));
                for (int j = a; j < b; j++)
                    o.Add(s.Mono[j]);
            }
            return o.ToArray();
        }
        var reelC = Seg(run.ReelOnly, r => r.Cranking && !r.Slipping);
        var dragS = Seg(run.ReelOnly, r => r.Slipping);
        var ambF = Seg(run.AmbOnly, r => r.State == LakeSoundState.Fight);
        var ambIdle = Seg(run.AmbOnly, r => r.State == LakeSoundState.Waiting);
        string Lv(float[] x) => x.Length > 0 ? $"{M.Db(Rms(x)):0.0} dBFS RMS / peak {M.Db(Peak(x)):0.0}" : "-";
        Log($"  reel while cranking {Lv(reelC)}; drag while slipping {Lv(dragS)}; ambience in fight {Lv(ambF)}, while waiting {Lv(ambIdle)}");
        if (reelC.Length > 0 && ambF.Length > 0)
            Log($"  ambience sits {M.Db(Rms(reelC)) - M.Db(Rms(ambF)):0.0} dB under the reel while cranking (ducked {M.Db(Rms(ambIdle)) - M.Db(Rms(ambF)):0.0} dB vs waiting)");
        if (dragS.Length > 0 && reelC.Length > 0)
            Log($"  drag peaks {M.Db(Peak(dragS)) - M.Db(Peak(reelC)):+0.0;-0.0} dB over reel peaks");

        // Зёрна.
        var mixer = run.ReelOnly.Mixer;
        var log = mixer.GrainLog.Take(mixer.GrainLogCount).ToList();
        double secs = run.FightEnd - run.FightStart;
        foreach (var grp in log.GroupBy(g => g.Kind).OrderBy(g => g.Key))
        {
            var l = grp.OrderBy(g => g.Frame).ToList();
            int repeats = l.Zip(l.Skip(1), (a, b) => a.Variant == b.Variant ? 1 : 0).Sum();
            var gdb = l.Select(g => 20 * Math.Log10(g.Gain)).ToList();
            Log($"  {grp.Key,-8} {l.Count,5} grains ({l.Count / secs:0.0}/s), variants used {l.Select(g => g.Variant).Distinct().Count()}, same twice in a row {repeats}, gain {gdb.Min():0.0}..{gdb.Max():0.0} dB");
        }
        Log($"  voices: peak grain voices {mixer.PeakGrainVoices} of {FightAudioMixer.GrainVoices}, stolen {mixer.StolenVoices}; one-shots played {run.Full.Mixer.OneShotsPlayed}, stale {run.Full.Mixer.StaleOneShots}; birds {run.Full.Mixer.BirdCalls}");
        Log($"  reel bus ducked under one-shots (more than 1 dB) {100.0 * run.Full.Mixer.DuckedFrames / Math.Max(1, run.Full.Mono.Count):0.0}% of the render, {100.0 * run.Full.Mixer.DuckedFrames / Math.Max(1.0, (run.FightEnd - run.FightStart) * Rate):0.0}% of the fight span length");
        var rot = log.Where(g => g.Kind == FightGrain.Rotor).Select(g => (double)g.Frame / Rate).OrderBy(x => x).ToList();
        var ratios = new List<double>();
        for (int i = 2; i < rot.Count; i++)
        {
            double a = rot[i - 1] - rot[i - 2], b = rot[i] - rot[i - 1];
            if (a < 0.4 && b < 0.4)
                ratios.Add(Math.Abs(Math.Log(b / a)));
        }
        if (ratios.Count > 0)
            Log($"  rotor rhythm (grain log): consecutive-interval change median {100 * (Math.Exp(Pct(ratios, 0.5)) - 1):0.0}%, p90 {100 * (Math.Exp(Pct(ratios, 0.9)) - 1):0.0}%");
        var reelSpan = run.ReelOnly.Samples(run.FightStart, run.FightEnd + 1.0);
        var tk = M.DetectTicks(reelSpan, Rate, false);
        Log($"  onsets (AudioCritic DetectTicks on reel-only fight span): {tk.T.Count} at {tk.Rate:0.0}/s, local IOI change median {tk.JitMed:0.0}%, on 60 fps grid {100 * tk.R60:0}%, identical neighbours {100 * tk.Ident:0}%");

        WavD($"{fileNo}_fight_{name}", mix);
        WavD($"{fileNo + 2}_fight_{reelFile}", run.ReelOnly.Samples());
        WavD($"{fileNo + 4}_fight_{name}_ambience_only", run.AmbOnly.Samples());
        return run;
    }

    static void LoudnessSummary(FightRun a, FightRun b)
    {
        Log();
        Log("=== LOUDNESS (rule 18: about -16 LUFS on the device, true peak <= -1 dBTP) ===");
        foreach (var r in new[] { a, b })
        {
            var span = r.Full.Samples(r.FightStart, r.FightEnd + 1.0);
            var l = M.Loudness(span, Rate);
            var ph = M.Loudness(D.Filt(span, Biquad.Hp(Rate, 700, Math.Sqrt(0.5))), Rate);
            Log($"  {r.Name,-20} master {Master}: {l.I:0.0} LUFS (phone model {ph.I:0.0}), LRA {l.Lra:0.0} LU, true peak {M.Db(M.TruePeak(span)):0.0} dBTP; at master 1.0 ≈ {l.I + M.Db(1 / Master):0.0} LUFS before limiting");
        }
    }

    // =====================================================================================================
    // 4. Ситуации катушки (как natural/01–07, 27): FightSoundInput напрямую, без фона и разовых
    // =====================================================================================================

    struct S
    {
        public double ReelIn, Load, Slip, LoadShare, RodShare;
        public bool IntoSlip;
    }

    sealed class Scen
    {
        public string File;
        public double[] X;
        public Sim Sim;
    }

    static Scen Scenario(string file, double seconds, float c0, Func<double, S> at)
    {
        var sim = new Sim(Bank) { OneShots = false, Ambience = false };
        double t = 0;
        int tick = 0;
        bool wasSlip = false;
        sim.Frame(LakeSoundState.Waiting, default, c0, 15);
        while (t < seconds)
        {
            int ticks = (int)Math.Floor((t + Dt) / 0.05 + 1e-9) - (int)Math.Floor(t / 0.05 + 1e-9);
            var s = at(t);
            bool slipping = s.Slip > 0;
            var ev = FightEvents.None;
            if (slipping && !wasSlip && ticks > 0)
                ev |= FightEvents.Breakaway;
            if (ticks > 0)
                wasSlip = slipping;
            tick += ticks;
            var f = new FightSoundInput
            {
                Ticks = ticks, ReelInCmPerSec = (float)s.ReelIn, ReelLoad = (float)s.Load, IsDragSlipping = slipping,
                IsCrankingIntoSlip = s.IntoSlip, SlipCmPerSec = (float)s.Slip, LoadShare = (float)s.LoadShare,
                RodLoadShare = (float)s.RodShare, SlackCm = 0, FishDistanceCm = 1200, Events = ev, Phase = FishPhase.Rest,
                NextPhase = FishPhase.Rest, WeightG = 800, Tick = tick,
            };
            sim.Frame(LakeSoundState.Fight, f, c0, 15);
            t += Dt;
        }
        for (int i = 0; i < 30; i++)
            sim.Frame(LakeSoundState.Caught, default, c0, 15);
        return new Scen { File = file, X = sim.Samples(), Sim = sim };
    }

    static double Lin(double t, double t0, double t1, double a, double b) => t <= t0 ? a : t >= t1 ? b : a + (b - a) * (t - t0) / (t1 - t0);

    static List<Scen> ScenarioSection()
    {
        var list = new List<Scen>
        {
            Scenario("01_reel_noload", 8, 110, t =>
                t < 0.3 || (t > 3.3 && t < 3.8) || t > 7.3 ? new S { LoadShare = 0.2 } : new S { ReelIn = t < 3.3 ? 88 : 110, Load = 0.05, LoadShare = 0.25, RodShare = 0.3 }),
            Scenario("02_reel_load", 9, 110, t =>
            {
                if (t > 8.3) return new S { LoadShare = 0.4 };
                double load = Lin(t, 0, 8, 0.35, 0.95);
                return new S { ReelIn = 110 * (1 - load), Load = load, LoadShare = Lin(t, 0, 8, 0.45, 0.85), RodShare = Lin(t, 0, 8, 0.45, 0.7) };
            }),
            Scenario("03_reel_fast", 6.5, 250, t =>
                t < 0.3 || t > 6.0 ? new S { LoadShare = 0.1 } : new S { ReelIn = Lin(t, 0.3, 3, 225, 250), Load = 0.03, LoadShare = 0.15, RodShare = 0.2 }),
            Scenario("04_drag_slow", 6.5, 110, t =>
                t > 0.4 && t < 2.6 ? new S { Slip = 9, Load = 0, LoadShare = 0.62, RodShare = 0.5 }
                : t > 3.2 && t < 5.8 ? new S { Slip = 14, LoadShare = 0.64, RodShare = 0.52 } : new S { LoadShare = 0.5 }),
            Scenario("05_drag_fast", 7.5, 110, t =>
            {
                if (t < 0.3 || t > 6.4) return new S { LoadShare = 0.5 };
                double slip = t < 3.3 ? Lin(t, 0.3, 3.3, 20, 95) : t < 4.3 ? 95 : Lin(t, 4.3, 6.4, 95, 25);
                return new S { Slip = slip, LoadShare = 0.68, RodShare = 0.55 };
            }),
            Scenario("06_line_tension", 8.5, 110, t =>
                t > 8.0 ? new S { LoadShare = 0.5 } : new S { ReelIn = 16, Load = 0.85, LoadShare = Lin(t, 0, 7.5, 0.65, 1.0), RodShare = 0.6 }),
            Scenario("07_rod_creak", 8.5, 110, t =>
                t > 8.0 ? new S { LoadShare = 0.5 } : new S { ReelIn = 33, Load = 0.7, LoadShare = 0.8, RodShare = Lin(t, 0, 7.5, 0.72, 1.0) }),
            Scenario("27_crank_into_slip", 6, 110, t =>
                t < 0.3 || t > 5.4 ? new S { LoadShare = 0.5 } : new S { ReelIn = 0, Load = 0.6, IntoSlip = true, Slip = 12, LoadShare = 0.66, RodShare = 0.5 }),
        };
        foreach (var s in list)
            WavD(s.File, s.X);
        // Ритм ротора на постоянной скорости и трещотки на 14 см/с — по журналу зёрен.
        var r01 = list[0].Sim.Mixer;
        var rot = r01.GrainLog.Take(r01.GrainLogCount).Where(g => g.Kind == FightGrain.Rotor && g.Frame > 4.2 * Rate && g.Frame < 7.2 * Rate).Select(g => (double)g.Frame / Rate).OrderBy(x => x).ToList();
        var ioi = rot.Zip(rot.Skip(1), (a, b) => b - a).ToList();
        var r04 = list[3].Sim.Mixer;
        var dr = r04.GrainLog.Take(r04.GrainLogCount).Where(g => g.Kind == FightGrain.Drag && g.Frame > 3.5 * Rate && g.Frame < 5.6 * Rate).Select(g => (double)g.Frame / Rate).OrderBy(x => x).ToList();
        var dioi = dr.Zip(dr.Skip(1), (a, b) => b - a).ToList();
        Log();
        Log("=== SCENARIOS (in-game path, no ambience, no one-shots) ===");
        Log($"  rotor @110 cm/s: mean IOI {1000 * ioi.Average():0.0} ms (expected {1000 * 14 / 110.0:0.0}), IOI CV {100 * Std(ioi) / ioi.Average():0.0}%");
        Log($"  drag @14 cm/s: mean IOI {1000 * dioi.Average():0.0} ms (expected {1000 * 1.2 / 14:0.0}), IOI CV {100 * Std(dioi) / dioi.Average():0.0}%");
        return list;
    }

    // =====================================================================================================
    // 5. Фон: день, ночь, сумерки
    // =====================================================================================================

    static void AmbienceSection()
    {
        Log();
        Log("=== AMBIENCE (in-game mixer, Idle state, master 0.8) ===");
        Sim Run(double seconds, Func<double, float> night)
        {
            var sim = new Sim(Bank);
            double t = 0;
            while (t < seconds)
            {
                sim.Night = night(t);
                sim.Frame(LakeSoundState.Idle, default, 110, 15);
                t += Dt;
            }
            return sim;
        }
        var day = Run(64, _ => 0f);
        var night = Run(40, _ => 1f);
        var dusk = Run(60, t => (float)Math.Min(1, Math.Max(0, (t - 10) / 40)));
        foreach (var (file, sim, what) in new[] { ("30_ambience_day_64s", day, "day"), ("32_ambience_night_40s", night, "night"), ("33_ambience_dusk_60s", dusk, "dusk 0→1 over 10–50 s") })
        {
            var x = sim.Samples(1.0);
            var l = M.Loudness(x, Rate);
            var sp = Spec(x);
            Log($"  {what,-22} {l.I:0.0} LUFS, RMS {M.Db(M.Rms(x)):0.0} dBFS, peak {M.Db(M.Peak(x)):0.0}; centroid {sp.Cpow:0} Hz | FLAT {Sh(sp.Flat)} | A {Sh(sp.A)}; bird calls {sim.Mixer.BirdCalls}, cricket chirps {sim.Mixer.CricketChirps}");
            WavD(file, sim.Samples());
        }
    }

    // =====================================================================================================
    // 6. Процессор
    // =====================================================================================================

    static void CpuSection(FightRun a, FightRun b)
    {
        Log();
        Log("=== CPU per audio buffer (desktop .NET 8 Release; Unity calls Render once per buffer on its audio thread) ===");
        void Line(string what, List<double> us, int frames)
        {
            var s = us.Skip(5).OrderBy(v => v).ToList();
            double budget = 1e6 * frames / Rate;
            Log($"  {what,-44} {frames,4} frames: mean {s.Average():0} us, p99 {Pct(s, 0.99):0} us, max {s.Max():0} us = {100 * s.Average() / budget:0.00}% / {100 * Pct(s, 0.99) / budget:0.00}% of the {budget / 1000:0.0} ms buffer");
        }
        Line("perch fight (full mix)", a.Full.RenderMicros, Buffer);
        Line("bream fight (full mix)", b.Full.RenderMicros, Buffer);

        // Стресс: быстрая трещотка, натуга, треск, скрип, гул, ночь со сверчками и разовый звук каждые 3 кадра.
        foreach (int frames in new[] { 1024, 256 })
        {
            var sim = new Sim(Bank, frames) { Night = 1f };
            var f = new FightSoundInput
            {
                ReelInCmPerSec = 30, ReelLoad = 0.95f, IsDragSlipping = true, IsCrankingIntoSlip = true, SlipCmPerSec = 100, LoadShare = 1f,
                RodLoadShare = 1f, SlackCm = 0, FishDistanceCm = 500, WeightG = 3000, Ticks = 1,
            };
            for (int i = 0; i < 600; i++)
            {
                f.Events = i % 3 == 0 ? FightEvents.PhaseStart : FightEvents.None;
                f.Phase = (i / 3) % 2 == 0 ? FishPhase.Run : FishPhase.Dive;
                f.Tick = i;
                sim.Frame(LakeSoundState.Fight, f, 110, 15);
            }
            var x = sim.Samples();
            Line($"stress (drag 83/s, strain, crackle, creak, 20 one-shots/s, night)", sim.RenderMicros, frames);
            if (frames == 1024)
                Log($"    stress output: peak {M.Db(M.Peak(x)):0.0} dBFS, |x|>=1: {x.Count(v => Math.Abs(v) >= 1)}, peak grain voices {sim.Mixer.PeakGrainVoices}, stolen {sim.Mixer.StolenVoices}, limiter deepest {M.Db(sim.Mixer.Limiter.MinGain):0.0} dB");
        }
    }

    // =====================================================================================================
    // 7. Сравнение с превью «Натуральной» палитры
    // =====================================================================================================

    sealed class Row
    {
        public string What;
        public double Lufs, Lra, Tp, Cent, F25, A25, P25, Low, Jit, Rate, Crest, EnvRange, Mod28, Buzz, Harsh;
    }

    /// <summary>Огибающая 10 мс RMS в дБ: размах p10..p90 там, где звучит (не тише 40 дБ под 99-м процентилем огибающей файла).</summary>
    static double EnvRange(double[] x)
    {
        int w = Rate / 100;
        var all = new List<double>();
        for (int i = 0; i + w <= x.Length; i += w)
        {
            double s = 0;
            for (int j = i; j < i + w; j++)
                s += x[j] * x[j];
            all.Add(10 * Math.Log10(s / w + 1e-20));
        }
        if (all.Count < 10)
            return double.NaN;
        double gate = Pct(all, 0.99) - 40;
        var e = all.Where(v => v > gate).ToList();
        return e.Count < 10 ? double.NaN : Pct(e, 0.9) - Pct(e, 0.1);
    }

    static Row Measure(string what, double[] x, bool ticks)
    {
        var l = M.Loudness(x, Rate);
        var sp = Spec(x);
        var r = new Row
        {
            What = what, Lufs = l.I, Lra = l.Lra, Tp = M.Db(M.TruePeak(x)), Cent = sp.Cpow, F25 = sp.Flat[2], A25 = sp.A[2], P25 = sp.Ph[2], Low = sp.Flat[0],
            Jit = double.NaN, Rate = double.NaN, Crest = M.Db(M.Peak(x) / Math.Max(1e-12, M.ActiveRms(x, Rate))), EnvRange = EnvRange(x),
            Mod28 = M.ModIndex(x, Rate, 0, 0, 2, 8, false).med,
            Buzz = M.FrameStats(x, Rate, true).BuzzFrac,
            Harsh = sp.A[2] * M.ModIndex(x, Rate, 2000, 5000, 20, 150, false).med,
        };
        if (ticks)
        {
            var tk = M.DetectTicks(x, Rate, false);
            r.Jit = tk.JitMed;
            r.Rate = tk.Rate;
        }
        return r;
    }

    static double[] Natural(string file, double fromSeconds, double trimDb, double toSeconds = double.MaxValue)
    {
        string p = Path.Combine(NaturalDir, file + ".wav");
        if (!File.Exists(p))
            return null;
        var a = Wav.Read(p);
        var x = a.X;
        if (a.Fs != Rate)
        {
            // Превью на 22050 Гц (фон) — линейно на 48 кГц: для замеров спектра и громкости хватает.
            var y = new double[(int)((long)x.Length * Rate / a.Fs)];
            for (int i = 0; i < y.Length; i++)
            {
                double pos = (double)i * a.Fs / Rate;
                int k = (int)pos;
                double frac = pos - k;
                y[i] = k + 1 < x.Length ? x[k] + (x[k + 1] - x[k]) * frac : x[x.Length - 1];
            }
            x = y;
        }
        int s0 = (int)(fromSeconds * Rate), s1 = toSeconds >= double.MaxValue / 2 ? x.Length : Math.Min(x.Length, (int)(toSeconds * Rate));
        double g = Math.Pow(10, -trimDb / 20);
        return x.Skip(s0).Take(s1 - s0).Select(v => v * g).ToArray();
    }

    static void CompareSection(FightRun perch, FightRun bream, List<Scen> scen)
    {
        Log();
        Log("=== COMPARISON with the natural previews (natural files brought back to game level: demos -3.4 dB, scenarios -1.4 dB) ===");
        Log("  LUFS/LRA/TP: loudness, range, true peak. Bands: share of energy in 2-5 kHz flat / A-weighted / phone-model; <300 Hz flat.");
        Log("  Jitter: AudioCritic DetectTicks local IOI change (median), onsets per second.");
        Log("  Dynamics: crest = sample peak over active RMS (dB); env = p10..p90 range of the 10 ms RMS envelope (dB); mod = AM index 2-8 Hz.");
        Log("  Defect metrics (AudioCritic): buzz = share of active energy with a harmonic comb >= 8 dB (defect > 20%); harsh = A(2-5k) x AM(20-150) (warn >= 0.05).");
        Log($"  {"what",-34}{"LUFS",7}{"LRA",6}{"TP",7}{"cent",7}{"2-5k F/A/Ph %",17}{"<300%",7}{"jit%",7}{"on/s",7}{"crest",7}{"env",6}{"mod",6}{"buzz%",7}{"harsh",7}");
        void Pair(string what, double[] mine, double[] nat, bool ticks)
        {
            foreach (var (tag, x) in new[] { ("ingame ", mine), ("natural", nat) })
            {
                if (x == null || x.Length < Rate)
                {
                    Log($"  {tag} {what,-26} (missing)");
                    continue;
                }
                var r = Measure(what, x, ticks);
                Log($"  {tag} {what,-26}{r.Lufs,7:0.0}{r.Lra,6:0.0}{r.Tp,7:0.0}{r.Cent,7:0}{$"{100 * r.F25:0.0}/{100 * r.A25:0.0}/{100 * r.P25:0.0}",17}{100 * r.Low,7:0.0}{r.Jit,7:0.0}{r.Rate,7:0.0}{r.Crest,7:0.0}{r.EnvRange,6:0.0}{r.Mod28,6:0.00}{100 * r.Buzz,7:0.0}{r.Harsh,7:0.000}");
            }
        }
        Pair("perch fight (full)", perch.Full.Samples(perch.FightStart, perch.FightEnd + 1), Natural("40_demo_perch300_holdreel", NaturalFightStart, NaturalDemoTrimDb, NaturalFightStart + perch.FightEnd + 1 - perch.FightStart), false);
        Pair("bream fight (full)", bream.Full.Samples(bream.FightStart, bream.FightEnd + 1), Natural("41_demo_bream2kg_technique", NaturalFightStart, NaturalDemoTrimDb, NaturalFightStart + bream.FightEnd + 1 - bream.FightStart), false);
        Pair("perch reel only", perch.ReelOnly.Samples(perch.FightStart, perch.FightEnd + 1), Natural("42_demo_perch300_reel_only", NaturalFightStart, NaturalDemoTrimDb, NaturalFightStart + perch.FightEnd + 1 - perch.FightStart), true);
        Pair("bream reel+drag only", bream.ReelOnly.Samples(bream.FightStart, bream.FightEnd + 1), Natural("43_demo_bream2kg_reel_drag_only", NaturalFightStart, NaturalDemoTrimDb, NaturalFightStart + bream.FightEnd + 1 - bream.FightStart), true);
        foreach (var s in scen)
            Pair(s.File, s.X, Natural(s.File, 0, NaturalScenarioTrimDb), s.File.StartsWith("0") && s.File != "06_line_tension");
        // Фон: у прототипа в файле 30 превью-усиление −8,1 дБ к клипу, а в игре он шёл на 0,09 × 0,8 (−22,8 дБ) — итого −14,7 дБ.
        var dayFile = Path.Combine(Out, "30_ambience_day_64s.wav");
        if (File.Exists(dayFile))
            Pair("ambience day (64 s)", Wav.Read(dayFile).X.Skip(Rate).ToArray(), Natural("30_ambience_morning_64s", 1, 14.7), false);
        Log("  note: ingame levels include the master makeup and limiter; natural previews are the palette at game level (no makeup).");
    }

    // =====================================================================================================
    // Кирпичики
    // =====================================================================================================

    static M.Spec Spec(double[] x)
    {
        int seg = M.SegFor(Rate);
        return M.Bands(M.Welch(x, seg), (double)Rate / seg);
    }

    static string Sh(double[] s) => string.Join(" ", s.Select(v => $"{100 * v,5:0.0}"));

    static double[] ToD(float[] x) => x.Select(v => (double)v).ToArray();

    static double Rms(float[] x)
    {
        double s = 0;
        foreach (var v in x)
            s += (double)v * v;
        return Math.Sqrt(s / Math.Max(1, x.Length));
    }

    static double Peak(float[] x)
    {
        double m = 0;
        foreach (var v in x)
            m = Math.Max(m, Math.Abs(v));
        return m;
    }

    static double Std(List<double> l)
    {
        double m = l.Average();
        return Math.Sqrt(l.Sum(v => (v - m) * (v - m)) / Math.Max(1, l.Count - 1));
    }

    static double Pct(IEnumerable<double> x, double p)
    {
        var l = x.OrderBy(v => v).ToList();
        return l.Count == 0 ? 0 : l[(int)Math.Min(l.Count - 1, Math.Round(p * (l.Count - 1)))];
    }

    static void WavD(string name, double[] x) => WriteFloatWav(Path.Combine(Out, name + ".wav"), x, Rate);

    static void WavF(string name, float[] x, int fs, double gain) => WriteFloatWav(Path.Combine(Out, name + ".wav"), x.Select(v => v * gain).ToArray(), fs);

    /// <summary>Моно WAV, 32-бит float (уровни как есть, без дизера).</summary>
    static void WriteFloatWav(string path, double[] x, int fs)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int n = x.Length;
            w.Write("RIFF".ToCharArray());
            w.Write(36 + n * 4);
            w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray());
            w.Write(16);
            w.Write((short)3);
            w.Write((short)1);
            w.Write(fs);
            w.Write(fs * 4);
            w.Write((short)4);
            w.Write((short)32);
            w.Write("data".ToCharArray());
            w.Write(n * 4);
            foreach (var v in x)
                w.Write((float)v);
        }
        Files.Add(path);
    }
}
