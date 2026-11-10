using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fishing.Unity;

/// <summary>All clips, generated once (what FightFeedback.CreateClips would hold).</summary>
internal sealed class CozyBank
{
    public float[][][] Sounds = new float[(int)CozySound.BirdBlackbird + 1][][];
    public float[] Bed, Strain, Line, Ambience;
    public readonly List<(string what, double ms, int samples)> Timing = new List<(string, double, int)>();

    public static CozyBank Build()
    {
        var b = new CozyBank();
        var sw = new Stopwatch();
        float[][] Many(int count, Func<int, float[]> make)
        {
            var a = new float[count][];
            for (int i = 0; i < count; i++) a[i] = make(i);
            return a;
        }
        void T(string what, Action act, Func<int> samples)
        {
            sw.Restart(); act(); sw.Stop();
            b.Timing.Add((what, sw.Elapsed.TotalMilliseconds, samples()));
        }
        int Count(params CozySound[] s) { int n = 0; foreach (var x in s) foreach (var c in b.Sounds[(int)x]) n += c.Length; return n; }

        T("gear ticks 3x12", () =>
        {
            b.Sounds[(int)CozySound.GearLight] = Many(CozySynth.GearVariants, v => CozySynth.GearTick(0, v));
            b.Sounds[(int)CozySound.Gear] = Many(CozySynth.GearVariants, v => CozySynth.GearTick(1, v));
            b.Sounds[(int)CozySound.GearHeavy] = Many(CozySynth.GearVariants, v => CozySynth.GearTick(2, v));
            b.Sounds[(int)CozySound.HandleTock] = Many(CozySynth.TockVariants, CozySynth.HandleTock);
        }, () => Count(CozySound.GearLight, CozySound.Gear, CozySound.GearHeavy, CozySound.HandleTock));
        T("drag ticks 2x16 + purr 12", () =>
        {
            b.Sounds[(int)CozySound.DragTick] = Many(CozySynth.DragVariants, v => CozySynth.DragTick(false, v));
            b.Sounds[(int)CozySound.DragTickDark] = Many(CozySynth.DragVariants, v => CozySynth.DragTick(true, v));
            b.Sounds[(int)CozySound.DragPurr] = Many(CozySynth.PurrVariants, CozySynth.DragPurr);
        }, () => Count(CozySound.DragTick, CozySound.DragTickDark, CozySound.DragPurr));
        T("rod creak 8", () => b.Sounds[(int)CozySound.RodCreak] = Many(CozySynth.CreakVariants, CozySynth.RodCreak), () => Count(CozySound.RodCreak));
        T("loops bed+strain+line", () => { b.Bed = CozySynth.ReelBed(); b.Strain = CozySynth.StrainBed(); b.Line = CozySynth.LineTension(); },
            () => b.Bed.Length + b.Strain.Length + b.Line.Length);
        T("ambience bed 64 s", () => b.Ambience = CozySynth.AmbienceBed(), () => b.Ambience.Length);
        T("one-shots", () =>
        {
            void One(CozySound s, float[] c) => b.Sounds[(int)s] = new[] { c };
            One(CozySound.Hookset, CozySynth.Hookset());
            One(CozySound.TeleRun, CozySynth.TeleRun());
            One(CozySound.TeleDive, CozySynth.TeleDive());
            One(CozySound.TeleSurge, CozySynth.TeleSurge());
            One(CozySound.TeleJerk, CozySynth.TeleJerk());
            One(CozySound.TeleJump, CozySynth.TeleJump());
            One(CozySound.SplashSmall, CozySynth.Splash(0));
            One(CozySound.SplashBig, CozySynth.Splash(1));
            One(CozySound.SplashJump, CozySynth.Splash(2));
            One(CozySound.LandedSmall, CozySynth.Landed(0));
            One(CozySound.LandedMedium, CozySynth.Landed(1));
            One(CozySound.LandedBig, CozySynth.Landed(2));
            One(CozySound.LineSnap, CozySynth.LineSnap());
            One(CozySound.RodCrack, CozySynth.RodCrack());
            One(CozySound.Escape, CozySynth.Escape());
            One(CozySound.CastWhoosh, CozySynth.CastWhoosh());
            One(CozySound.FloatPlop, CozySynth.FloatPlop());
            One(CozySound.BiteBob, CozySynth.BiteBob());
            b.Sounds[(int)CozySound.BirdCuckoo] = Many(CozySynth.BirdVariants(0), v => CozySynth.BirdCall(0, v));
            b.Sounds[(int)CozySound.BirdPigeon] = Many(CozySynth.BirdVariants(1), v => CozySynth.BirdCall(1, v));
            b.Sounds[(int)CozySound.BirdBlackbird] = Many(CozySynth.BirdVariants(2), v => CozySynth.BirdCall(2, v));
        }, () => { int n = 0; for (int s = (int)CozySound.Hookset; s < b.Sounds.Length; s++) foreach (var c in b.Sounds[s]) n += c.Length; return n; });
        return b;
    }
}

/// <summary>
/// Software model of the proposed Unity playback (6 AudioSources):
///   0..3 — loop sources (reel bed, strain, line, lake), only volume/pitch per frame (attack 60 ms, pitch glide 90 ms,
///          release from CozyCues; the pitch FREEZES while a loop fades out; each start begins at a hashed offset);
///   grains — one clip-less source with an OnAudioFilterRead grain mixer (16 voices), each grain stamped at
///          frame start + At·dt + 40 ms (sample-accurate; what PlayOneShot cannot do);
///   fx — PlayOneShot at the frame (+20 ms).
/// 22050 Hz clips → 48 kHz output by linear interpolation (FMOD-like). Stems: 0 bed, 1 strain, 2 line, 3 lake,
/// 4 reel grains (gear, tock), 5 drag grains (ticks, purr) + creak, 6 fx.
/// </summary>
internal sealed class CozyPlayer
{
    public const int OutRate = 48000;
    public const double GrainLatency = 0.04;
    public const double ShotLatency = 0.02;
    const int SR = CozySynth.SampleRate;
    const double Step = (double)SR / OutRate;

    public static readonly string[] StemNames = { "bed", "strain", "line", "lake", "gear", "drag", "fx" };
    public readonly float[][] Stems;
    readonly CozyBank _bank;
    double _t;
    long _rendered;
    public readonly LoopVoice Bed, Strain, Line, Lake;
    readonly LoopVoice[] _loops;
    public readonly Dictionary<CozySound, int> Played = new Dictionary<CozySound, int>();
    public readonly List<(double t, CozySound s, int v, float vol)> Events = new List<(double, CozySound, int, float)>();
    public int MaxConcurrentGrains;
    readonly List<(long start, long end)> _grainSpans = new List<(long, long)>();

    public CozyPlayer(CozyBank bank, double seconds, bool lake = true)
    {
        _bank = bank;
        int n = (int)(seconds * OutRate) + OutRate;
        Stems = new float[7][];
        for (int i = 0; i < 7; i++) Stems[i] = new float[n];
        Bed = new LoopVoice(bank.Bed, 0x1001u);
        Strain = new LoopVoice(bank.Strain, 0x2002u);
        Line = new LoopVoice(bank.Line, 0x3003u);
        Lake = new LoopVoice(bank.Ambience, 0x4004u) { Attack = 0.5f };
        if (!lake) Lake = new LoopVoice(bank.Ambience, 0x4004u) { Disabled = true };
        _loops = new[] { Bed, Strain, Line, Lake };
    }

    public double Time => _t;
    public int Length => (int)Math.Min(Stems[0].Length, _rendered);

    public void Frame(CozyCues cues, float dt, float master, bool skipShots = false)
    {
        double frameStart = _t, frameEnd = _t + dt;
        foreach (var g in cues.Grains)
        {
            var clip = _bank.Sounds[(int)g.Sound][g.Variant];
            double t0 = frameStart + g.At * dt + GrainLatency;
            int stem = g.Sound <= CozySound.HandleTock ? 4 : 5;
            var span = Add(stem, clip, t0, g.Volume * master);
            _grainSpans.Add(span);
            Count(g.Sound);
            Events.Add((t0, g.Sound, g.Variant, g.Volume));
        }
        if (!skipShots)
            foreach (var s in cues.OneShots)
            {
                Add(6, _bank.Sounds[(int)s.Sound][s.Variant], frameEnd + ShotLatency, s.Volume * master);
                Count(s.Sound);
                Events.Add((frameEnd + ShotLatency, s.Sound, s.Variant, s.Volume));
            }
        if (cues.CutLoops)
        {
            Bed.Cut(); Strain.Cut(); Line.Cut();
        }
        float rel = cues.LoopReleaseSeconds;
        Bed.Drive(cues.BedVolume, cues.BedPitch, rel, dt, master);
        Strain.Drive(cues.StrainVolume, cues.StrainPitch, rel, dt, master);
        Line.Drive(cues.LineVolume, cues.LinePitch, rel, dt, master);
        Lake.Drive(cues.AmbienceVolume, 1f, 1.0f, dt, master);

        long end = (long)Math.Round(frameEnd * OutRate);
        end = Math.Min(end, Stems[0].Length);
        int count = (int)(end - _rendered);
        if (count > 0)
            for (int v = 0; v < 4; v++)
                _loops[v].Render(Stems[v], (int)_rendered, count);
        _rendered = Math.Max(_rendered, end);
        _t = frameEnd;
    }

    void Count(CozySound s)
    {
        Played.TryGetValue(s, out int c);
        Played[s] = c + 1;
    }

    (long, long) Add(int stem, float[] clip, double t0, float gain)
    {
        var o = Stems[stem];
        double pos0 = t0 * OutRate;
        long start = (long)Math.Ceiling(pos0);
        double src = (start - pos0) * Step;
        long i = start;
        for (; i < o.Length; i++)
        {
            int k = (int)src;
            if (k + 1 >= clip.Length) break;
            double fr = src - k;
            o[i] += (float)((clip[k] + (clip[k + 1] - clip[k]) * fr) * gain);
            src += Step;
        }
        return (start, i);
    }

    public float[] Mix(params int[] stems)
    {
        if (stems.Length == 0) stems = new[] { 0, 1, 2, 3, 4, 5, 6 };
        int n = Length;
        var m = new float[n];
        foreach (int k in stems)
            for (int i = 0; i < n; i++) m[i] += Stems[k][i];
        return m;
    }

    /// <summary>Peak number of overlapping grain voices (OnAudioFilterRead mixer budget).</summary>
    public int GrainVoicesPeak()
    {
        var ev = new List<(long t, int d)>();
        foreach (var (s, e) in _grainSpans) { ev.Add((s, 1)); ev.Add((e, -1)); }
        ev.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : a.d.CompareTo(b.d));
        int cur = 0, best = 0;
        foreach (var x in ev) { cur += x.d; best = Math.Max(best, cur); }
        return best;
    }

    internal sealed class LoopVoice
    {
        public float Attack = 0.06f;
        public float PitchSeconds = 0.09f;
        public bool Disabled;
        const float Silence = 0.002f;
        readonly float[] _clip;
        readonly uint _seed;
        double _pos;
        bool _playing;
        float _vol, _volPrev, _pitch = 1f, _pitchPrev = 1f;
        float _volume, _p = 1f;
        public int Starts;

        public LoopVoice(float[] clip, uint seed) { _clip = clip; _seed = seed; }

        public void Drive(float targetVolume, float targetPitch, float release, float dt, float master)
        {
            if (Disabled) return;
            if (_stopAfterRender) return;
            if (targetVolume <= 0f && _volume < Silence)
            {
                if (_playing) Cut();
                return;
            }
            if (!_playing)
            {
                _p = targetPitch;
                _pitch = _pitchPrev = _p;
                _vol = _volPrev = 0f;
                _pos = CozySynth.Hash(_seed + (uint)Starts * 0x9E3779B9u) % (uint)_clip.Length;
                _playing = true;
                Starts++;
            }
            _volume = Approach(_volume, targetVolume, dt, targetVolume > _volume ? Attack : release);
            if (targetVolume > 0f)
                _p = Approach(_p, targetPitch, dt, PitchSeconds);
            _vol = _volume * master;
            _pitch = _p;
        }

        /// <summary>Обрыв: в ноль за один кадр (≈16 мс), а не мгновенно — AudioSource.Stop() посреди волны щёлкает.</summary>
        public void Cut()
        {
            _volume = 0f; _vol = 0f; _stopAfterRender = _playing;
        }

        bool _stopAfterRender;

        public void Render(float[] o, int from, int count)
        {
            if (!_playing || Disabled) { _volPrev = _vol; _pitchPrev = _pitch; return; }
            int len = _clip.Length;
            for (int i = 0; i < count && from + i < o.Length; i++)
            {
                float x = (float)(i + 1) / count;
                float vol = _volPrev + (_vol - _volPrev) * x;
                double step = Step * (_pitchPrev + (_pitch - _pitchPrev) * x);
                int k = (int)_pos;
                double fr = _pos - k;
                float a = _clip[k % len], b = _clip[(k + 1) % len];
                o[from + i] += (float)((a + (b - a) * fr) * vol);
                _pos += step;
                if (_pos >= len) _pos -= len;
            }
            _volPrev = _vol;
            _pitchPrev = _pitch;
            if (_stopAfterRender) { _playing = false; _stopAfterRender = false; }
        }

        static float Approach(float current, float target, float dt, float seconds)
            => current + (target - current) * (1f - (float)Math.Exp(-dt / Math.Max(seconds, 1e-4f)));
    }
}
