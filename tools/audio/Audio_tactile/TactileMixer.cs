using System;
using System.Collections.Generic;
using Fishing.Unity;

/// <summary>
/// Software model of the proposed TactileFeedback (6 AudioSources):
///  0 grains   — AudioSource without clip + OnAudioFilterRead → TactileGrains (sample-accurate, at output rate)
///  1 purr     — loop SpoolPurr (attack 60 ms, release from cues, pitch smoothing 120 ms)
///  2 hum      — loop LineHum (attack/release 300 ms, pitch smoothing 150 ms)
///  3 ambience — loop Ambience 64 s (attack/release 1.5 s)
///  4 fx       — PlayOneShot, dry
///  5 water    — PlayOneShot + AudioLowPassFilter (Q 1) at the cue's distance cutoff
/// Loops (re)start at a hashed offset (AudioSource.timeSamples) so no two onsets are identical.
/// 22050 Hz clips are read with linear interpolation at 48 kHz (FMOD-like); volume ramps linearly across a frame.
/// </summary>
internal sealed class TactileMixer
{
    public const int OutRate = 48000;
    const int SR = TactileSynth.SampleRate;

    public readonly TactileGrainBank Bank;
    public readonly TactileGrains Engine;
    public readonly Dictionary<TactileSound, float[]> Clips;
    public readonly Dictionary<TactileSound, int> Played = new Dictionary<TactileSound, int>();
    public readonly List<float>[] Stems = { new List<float>(), new List<float>(), new List<float>(), new List<float>(), new List<float>(), new List<float>() };
    public static readonly string[] StemNames = { "grains", "purr", "hum", "ambience", "fx", "water" };
    public readonly float[][] KindStems;
    public readonly Loop Purr, Hum, Amb;
    readonly List<Shot> _fx = new List<Shot>(), _water = new List<Shot>();
    Svf _waterLp = new Svf(OutRate, 6000, 1.0);
    double _carry;
    float[] _tmp = new float[4096];

    public sealed class Loop
    {
        public float[] Clip;
        public double Pos;
        public bool Playing;
        public float Vol, VolPrev, Pitch = 1f;
        public float Attack, PitchTau;
        public uint Seed;
        public int Starts;
        float _v, _p = 1f;

        public void Drive(float tv, float tp, float release, float dt, float master)
        {
            if (tv <= 0f && _v < 0.0005f)
            {
                if (Playing) Cut();
                return;
            }
            if (!Playing)
            {
                _p = tp;
                Pitch = tp;
                Vol = VolPrev = 0f;
                Pos = THash.Of(Seed, (uint)Starts) % (uint)Clip.Length;
                Playing = true;
                Starts++;
            }
            _v = Approach(_v, tv, dt, tv > _v ? Attack : release);
            _p = Approach(_p, tp, dt, PitchTau);
            Vol = _v * master;
            Pitch = _p;
        }

        public void Cut() { _v = 0f; Vol = VolPrev = 0f; Playing = false; }

        static float Approach(float c, float t, float dt, float s) => c + (t - c) * (1f - (float)Math.Exp(-dt / Math.Max(s, 1e-4f)));

        public void Render(List<float> stem, int n)
        {
            int start = stem.Count;
            for (int i = 0; i < n; i++) stem.Add(0f);
            if (!Playing) return;
            int len = Clip.Length;
            double step = (double)SR / OutRate * Pitch;
            for (int i = 0; i < n; i++)
            {
                float vol = VolPrev + (Vol - VolPrev) * (i + 1) / n;
                int i0 = (int)Pos;
                double fr = Pos - i0;
                float a = Clip[i0 % len], b = Clip[(i0 + 1) % len];
                stem[start + i] = (float)((a + (b - a) * fr) * vol);
                Pos += step;
                if (Pos >= len) Pos -= len;
            }
            VolPrev = Vol;
        }
    }

    sealed class Shot { public float[] Clip; public double Pos; public float Vol; }

    public TactileMixer(int maxSamples, Dictionary<TactileSound, float[]> clips, float[] purr, float[] hum, float[] amb)
    {
        Bank = new TactileGrainBank(OutRate);
        Engine = new TactileGrains(Bank);
        KindStems = new float[5][];
        for (int k = 0; k < 5; k++) KindStems[k] = new float[maxSamples];
        Engine.KindStems = KindStems;
        Clips = clips;
        Purr = new Loop { Clip = purr, Attack = 0.06f, PitchTau = 0.12f, Seed = 0x1111u };
        Hum = new Loop { Clip = hum, Attack = 0.3f, PitchTau = 0.15f, Seed = 0x2222u };
        Amb = new Loop { Clip = amb, Attack = 1.5f, PitchTau = 0.05f, Seed = 0x3333u };
    }

    /// <summary>One LateUpdate of the feedback component, then the audio of this frame.</summary>
    public void Frame(TactileCues cues, float dt, float master)
    {
        foreach (var c in cues.Sounds)
        {
            Played.TryGetValue(c.Sound, out int k);
            Played[c.Sound] = k + 1;
            var shot = new Shot { Clip = Clips[c.Sound], Vol = c.Volume * master };
            if (c.Water)
            {
                _waterLp.Set(c.LowPassHz, 1.0);
                _water.Add(shot);
            }
            else _fx.Add(shot);
        }
        if (cues.DragAccent) Engine.Trigger(GrainKind.DragAccent);
        // Grain targets (master applied here — in Unity the grain source volume = master).
        Engine.DetentRate = cues.DetentRate;
        Engine.DetentHeavy = cues.DetentHeavy;
        Engine.DetentGain = cues.DetentGain * master;
        Engine.RotorGain = cues.RotorGain * master;
        Engine.DragRate = cues.DragRate;
        Engine.DragGain = cues.DragGain * master;
        Engine.StrainRate = cues.StrainRate;
        Engine.StrainGain = cues.StrainGain * master;
        Engine.CreakRate = cues.CreakRate;
        Engine.CreakGain = cues.CreakGain * master;
        Engine.CreakHard = cues.CreakHard;

        if (cues.CutLoops) { Purr.Cut(); Hum.Cut(); }
        Purr.Drive(cues.PurrVolume, cues.PurrPitch, cues.LoopReleaseSeconds, dt, master);
        Hum.Drive(cues.HumVolume, cues.HumPitch, Math.Max(0.3f, cues.LoopReleaseSeconds), dt, master);
        Amb.Drive(cues.AmbienceVolume, 1f, 1.5f, dt, master);
        Render(dt);
    }

    void Render(float dt)
    {
        _carry += dt * OutRate;
        int n = (int)Math.Floor(_carry);
        _carry -= n;
        if (_tmp.Length < n) _tmp = new float[n];
        Array.Clear(_tmp, 0, n);
        int s0 = Stems[0].Count;
        Engine.RenderMono(_tmp, 0, n, s0);
        for (int i = 0; i < n; i++) Stems[0].Add(_tmp[i]);
        Purr.Render(Stems[1], n);
        Hum.Render(Stems[2], n);
        Amb.Render(Stems[3], n);
        RenderShots(_fx, Stems[4], n, false);
        RenderShots(_water, Stems[5], n, true);
    }

    void RenderShots(List<Shot> shots, List<float> stem, int n, bool lp)
    {
        int start = stem.Count;
        for (int i = 0; i < n; i++) stem.Add(0f);
        double step = (double)SR / OutRate;
        for (int k = shots.Count - 1; k >= 0; k--)
        {
            var sh = shots[k];
            int len = sh.Clip.Length;
            for (int i = 0; i < n; i++)
            {
                int i0 = (int)sh.Pos;
                if (i0 >= len) break;
                double fr = sh.Pos - i0;
                float a = sh.Clip[i0], b = i0 + 1 < len ? sh.Clip[i0 + 1] : 0f;
                stem[start + i] += (float)((a + (b - a) * fr) * sh.Vol);
                sh.Pos += step;
            }
            if ((int)sh.Pos >= len) shots.RemoveAt(k);
        }
        if (lp)
            for (int i = 0; i < n; i++) stem[start + i] = (float)_waterLp.Lp(stem[start + i]);
    }

    public float[] Mix(params int[] which)
    {
        int n = Stems[0].Count;
        var m = new float[n];
        var list = which.Length == 0 ? new[] { 0, 1, 2, 3, 4, 5 } : which;
        foreach (int k in list)
            for (int i = 0; i < n; i++) m[i] += Stems[k][i];
        return m;
    }
}
