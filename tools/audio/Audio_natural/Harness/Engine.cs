using System;
using System.Collections.Generic;
using Fishing.Unity;

/// <summary>
/// Software model of the proposed NaturalFeedback in Unity: 48 kHz output, GrainMixer (one per stem here —
/// one shared 16-voice mixer in Unity), three looping AudioSources (lay bed, line hum, ambience) whose
/// volume/pitch move once per frame (linear ramp across the frame, like FMOD's volume ramp), and PlayOneShot
/// voices. Clips are 22050 Hz and resampled with linear interpolation.
/// </summary>
internal sealed class Engine
{
    public const int Rate = 48000;
    public const float Master = 0.8f;

    private readonly NaturalBank _bank;
    public readonly GrainMixer ReelGrains = new GrainMixer(NaturalSynth.SampleRate);
    public readonly GrainMixer DragGrains = new GrainMixer(NaturalSynth.SampleRate);
    public readonly GrainMixer TensionGrains = new GrainMixer(NaturalSynth.SampleRate);
    public readonly Loop Lay, Hum, Amb;
    private readonly List<Shot> _shots = new List<Shot>();
    public readonly List<float> Reel = new List<float>(), Drag = new List<float>(), Tension = new List<float>(),
        Fx = new List<float>(), Ambience = new List<float>();
    public bool PlayOneShots = true;
    public bool AmbienceOn = true;
    private double _time;
    private long _rendered;
    public readonly List<(double t, NaturalGrain kind, int variant, float gain, float pitch)> GrainLog = new List<(double, NaturalGrain, int, float, float)>();
    public readonly List<(double t, NaturalSound sound, int variant, bool far, float vol)> ShotLog = new List<(double, NaturalSound, int, bool, float)>();

    public double Time => _time;

    public Engine(NaturalBank bank)
    {
        _bank = bank;
        Lay = new Loop(bank.LayBed, 0x1A70u);
        Hum = new Loop(bank.LineHum, 0x4C40u);
        Amb = new Loop(bank.Ambience, 0xA3B0u);
    }

    private sealed class Shot
    {
        public float[] Clip;
        public double Pos;
        public float Gain;
    }

    /// <summary>One looping AudioSource as FightFeedback.LoopVoice drives it, but restarting at a hashed offset (not sample 0) and never diving in pitch.</summary>
    internal sealed class Loop
    {
        private const float AttackSeconds = 0.04f;
        private const float PitchSeconds = 0.08f;
        private const float Silence = 0.002f;
        private readonly float[] _clip;
        private uint _seed;
        public bool Playing;
        public double Pos;
        public float Volume, Pitch = 1f;
        public float PrevVolume, PrevPitch = 1f;
        public int Starts;

        public Loop(float[] clip, uint seed)
        {
            _clip = clip;
            _seed = seed;
        }

        public void Drive(float target, float pitch, float release, float dt)
        {
            PrevVolume = Playing ? Volume : 0f;
            PrevPitch = Pitch;
            if (target <= 0f && Volume < Silence)
            {
                Playing = false;
                Volume = 0f;
                return;
            }
            if (!Playing)
            {
                Playing = true;
                Starts++;
                _seed = NaturalSynth.Hash(_seed + 0x9E37u);
                Pos = _seed % (uint)_clip.Length;
                Pitch = pitch;
                PrevPitch = pitch;
                Volume = 0f;
                PrevVolume = 0f;
            }
            Volume = Approach(Volume, target, dt, target > Volume ? AttackSeconds : release);
            Pitch = Approach(Pitch, pitch, dt, PitchSeconds);
        }

        public void Cut()
        {
            Playing = false;
            Volume = 0f;
            PrevVolume = 0f;
        }

        public void Render(float[] dst, float master)
        {
            if (!Playing && PrevVolume <= 0f)
                return;
            int n = dst.Length;
            for (int i = 0; i < n; i++)
            {
                double a = (i + 1.0) / n;
                float vol = (float)((PrevVolume + (Volume - PrevVolume) * a) * master);
                double step = (double)NaturalSynth.SampleRate / Rate * (PrevPitch + (Pitch - PrevPitch) * a);
                int k = (int)Pos;
                int k1 = k + 1 >= _clip.Length ? 0 : k + 1;
                float frac = (float)(Pos - k);
                dst[i] += (_clip[k] + (_clip[k1] - _clip[k]) * frac) * vol;
                Pos += step;
                if (Pos >= _clip.Length)
                    Pos -= _clip.Length;
            }
        }

        private static float Approach(float current, float target, float dt, float seconds) =>
            current + (target - current) * (1f - (float)Math.Exp(-dt / Math.Max(seconds, 1e-4f)));
    }

    /// <summary>One frame: take the cues' decisions, then render the frame's audio.</summary>
    public void Frame(NaturalCues cues, float dt)
    {
        foreach (var g in cues.Grains)
        {
            var clip = _bank.Grain(g.Kind, g.Variant);
            var mixer = g.Kind == NaturalGrain.Drag ? DragGrains
                : g.Kind == NaturalGrain.Crackle || g.Kind == NaturalGrain.Creak ? TensionGrains : ReelGrains;
            mixer.Schedule(clip, g.Time, g.Gain, g.Pitch);
            GrainLog.Add((g.Time, g.Kind, g.Variant, g.Gain, g.Pitch));
        }
        foreach (var s in cues.Sounds)
        {
            ShotLog.Add((_time, s.Sound, s.Variant, s.Far, s.Volume));
            if (PlayOneShots)
                _shots.Add(new Shot { Clip = _bank.Sound(s), Pos = 0, Gain = s.Volume });
        }
        if (cues.CutLoops)
        {
            Lay.Cut();
            Hum.Cut();
            ReelGrains.Clear();
            DragGrains.Clear();
            TensionGrains.Clear();
        }
        Lay.Drive(cues.LayVolume, cues.LayPitch, cues.LoopReleaseSeconds, dt);
        Hum.Drive(cues.HumVolume, cues.HumPitch, cues.LoopReleaseSeconds, dt);
        Amb.Drive(AmbienceOn ? cues.AmbienceVolume : 0f, 1f, cues.LoopReleaseSeconds, dt);
        Render(dt);
    }

    /// <summary>Render dt of audio with whatever is playing (no new decisions).</summary>
    public void Render(double dt)
    {
        long target = (long)Math.Round((_time + dt) * Rate);
        int n = (int)(target - _rendered);
        if (n <= 0)
        {
            _time += dt;
            return;
        }
        double start = (double)_rendered / Rate;
        var reel = new float[n];
        var drag = new float[n];
        var tension = new float[n];
        var fx = new float[n];
        var amb = new float[n];
        ReelGrains.Render(reel, 1, Rate, start, Master);
        Lay.Render(reel, Master);
        DragGrains.Render(drag, 1, Rate, start, Master);
        TensionGrains.Render(tension, 1, Rate, start, Master);
        Hum.Render(tension, Master);
        Amb.Render(amb, Master);
        double step = (double)NaturalSynth.SampleRate / Rate;
        for (int s = _shots.Count - 1; s >= 0; s--)
        {
            var shot = _shots[s];
            int last = shot.Clip.Length - 1;
            for (int i = 0; i < n; i++)
            {
                int k = (int)shot.Pos;
                if (k >= last)
                    break;
                float frac = (float)(shot.Pos - k);
                fx[i] += (shot.Clip[k] + (shot.Clip[k + 1] - shot.Clip[k]) * frac) * shot.Gain * Master;
                shot.Pos += step;
            }
            if ((int)shot.Pos >= last)
                _shots.RemoveAt(s);
        }
        Reel.AddRange(reel);
        Drag.AddRange(drag);
        Tension.AddRange(tension);
        Fx.AddRange(fx);
        Ambience.AddRange(amb);
        _rendered = target;
        _time += dt;
    }

    public float[] Mix(bool reel = true, bool drag = true, bool tension = true, bool fx = true, bool amb = true)
    {
        int n = Reel.Count;
        var o = new float[n];
        for (int i = 0; i < n; i++)
            o[i] = (reel ? Reel[i] : 0) + (drag ? Drag[i] : 0) + (tension ? Tension[i] : 0) + (fx ? Fx[i] : 0) + (amb ? Ambience[i] : 0);
        return o;
    }
}
