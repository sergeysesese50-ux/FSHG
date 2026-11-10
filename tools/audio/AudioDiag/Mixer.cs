using System;
using System.Collections.Generic;
using Fishing.Unity;

/// <summary>
/// Software model of FightFeedback (client/Assets/_Game/Scripts/Unity/Lake/FightFeedback.cs): the same clips, 6 sources,
/// LoopVoice smoothing (attack 40 ms, release from FightCues, pitch 50 ms), PlayOneShot volume x master, linear-interpolation
/// resampling of the 22050 Hz clips to a 48 kHz output (FMOD-like: no anti-alias filter on pitch changes),
/// volume ramped linearly across each frame, pitch held per frame.
/// </summary>
internal sealed class Mixer
{
    public const int OutRate = 48000;
    const int SR = FightSynth.SampleRate;

    internal sealed class Src
    {
        public float[] Clip;
        public bool Loop;
        public bool Playing;
        public double Pos;
        public float Vol;      // value applied for the current frame end
        public float VolPrev;  // value at the start of the frame
        public float Pitch = 1f;
    }

    public sealed class Voice
    {
        const float AttackSeconds = 0.04f;
        const float PitchSeconds = 0.05f;
        const float Silence = 0.002f;

        internal Src Source;
        float _volume;
        float _pitch = 1f;
        public int Starts;
        public int LoudCuts;
        public float AppliedVolume => Source.Playing ? Source.Vol : 0f;
        public float AppliedPitch => Source.Pitch;

        public void Drive(float targetVolume, float targetPitch, float releaseSeconds, float dt, float master)
        {
            if (targetVolume <= 0f && _volume < Silence)
            {
                if (Source.Playing) Cut();
                return;
            }
            if (!Source.Playing)
            {
                _pitch = targetPitch;
                Source.Pitch = _pitch;
                Source.Vol = 0f;
                Source.VolPrev = 0f;
                Source.Pos = 0;
                Source.Playing = true;
                Starts++;
            }
            _volume = Approach(_volume, targetVolume, dt, targetVolume > _volume ? AttackSeconds : releaseSeconds);
            _pitch = Approach(_pitch, targetPitch, dt, PitchSeconds);
            Source.Vol = _volume * master;
            Source.Pitch = _pitch;
        }

        public void Cut()
        {
            if (Source.Vol > 0.01f) LoudCuts++;
            _volume = 0f;
            Source.Vol = 0f;
            Source.VolPrev = 0f;
            Source.Playing = false;
            Source.Pos = 0;
        }

        static float Approach(float current, float target, float dt, float seconds)
            => current + (target - current) * (1f - (float)Math.Exp(-dt / Math.Max(seconds, 1e-4f)));
    }

    sealed class Shot
    {
        public float[] Clip;
        public double Pos;
        public float Vol;
        public int Bus; // 4 = ticks, 5 = fx
    }

    public readonly Voice Whir, Growl, ClickLoop, Sing;
    readonly Voice[] _voices;
    readonly float[][] _clicks, _creaks, _oneShots;
    int _nextClick, _nextCreak;
    readonly List<Shot> _shots = new List<Shot>();
    double _carry;

    /// <summary>Stems: 0 whir, 1 growl, 2 click loop, 3 line sing, 4 ticks (drag clicks + creaks), 5 fx one-shots.</summary>
    public readonly List<float>[] Stems = { new List<float>(), new List<float>(), new List<float>(), new List<float>(), new List<float>(), new List<float>() };
    public static readonly string[] StemNames = { "whir", "growl", "clickloop", "sing", "ticks", "fx" };
    public readonly Dictionary<FightSound, int> Played = new Dictionary<FightSound, int>();

    public Mixer()
    {
        // Same clips and seeds as FightFeedback.CreateClips (FightFeedback.cs:174-205).
        Whir = new Voice { Source = new Src { Clip = FightSynth.Whir(), Loop = true } };
        Growl = new Voice { Source = new Src { Clip = FightSynth.Growl(), Loop = true } };
        ClickLoop = new Voice { Source = new Src { Clip = FightSynth.ClickLoop(), Loop = true } };
        Sing = new Voice { Source = new Src { Clip = FightSynth.LineSing(), Loop = true } };
        _voices = new[] { Whir, Growl, ClickLoop, Sing };
        _clicks = new float[3][];
        for (int i = 0; i < 3; i++) _clicks[i] = FightSynth.DragClick(0xC11Cu + (uint)i * 7919u);
        _creaks = new float[3][];
        for (int i = 0; i < 3; i++) _creaks[i] = FightSynth.RodCreak(0xC4EAu + (uint)i * 104729u);
        _oneShots = new float[(int)FightSound.LandedChime + 1][];
        _oneShots[(int)FightSound.Splash] = FightSynth.Splash(false);
        _oneShots[(int)FightSound.SplashHeavy] = FightSynth.Splash(true);
        _oneShots[(int)FightSound.TelegraphSwirl] = FightSynth.TelegraphSwirl();
        _oneShots[(int)FightSound.JerkThunk] = FightSynth.JerkThunk();
        _oneShots[(int)FightSound.JumpWhoosh] = FightSynth.JumpWhoosh();
        _oneShots[(int)FightSound.Hookset] = FightSynth.HooksetThunk();
        _oneShots[(int)FightSound.LineSnap] = FightSynth.LineSnap();
        _oneShots[(int)FightSound.RodCrack] = FightSynth.RodCrack();
        _oneShots[(int)FightSound.EscapePlip] = FightSynth.EscapePlip();
        _oneShots[(int)FightSound.LandedChime] = FightSynth.LandedChime();
    }

    /// <summary>One LateUpdate of FightFeedback (FightFeedback.cs:94-125) followed by the audio for this frame.</summary>
    public void Frame(FightCues cues, float dt, float master)
    {
        foreach (var cue in cues.Sounds)
        {
            Played.TryGetValue(cue.Sound, out int c);
            Played[cue.Sound] = c + 1;
            switch (cue.Sound)
            {
                case FightSound.DragClick:
                    _shots.Add(new Shot { Clip = _clicks[_nextClick], Vol = cue.Volume * master, Bus = 4 });
                    _nextClick = (_nextClick + 1) % _clicks.Length;
                    break;
                case FightSound.RodCreak:
                    _shots.Add(new Shot { Clip = _creaks[_nextCreak], Vol = cue.Volume * master, Bus = 4 });
                    _nextCreak = (_nextCreak + 1) % _creaks.Length;
                    break;
                default:
                    _shots.Add(new Shot { Clip = _oneShots[(int)cue.Sound], Vol = cue.Volume * master, Bus = 5 });
                    break;
            }
        }
        if (cues.CutLoops)
            foreach (var v in _voices) v.Cut();
        float release = cues.LoopReleaseSeconds;
        Whir.Drive(cues.WhirVolume, cues.WhirPitch, release, dt, master);
        Growl.Drive(cues.GrowlVolume, cues.GrowlPitch, release, dt, master);
        ClickLoop.Drive(cues.ClickLoopVolume, cues.ClickLoopPitch, release, dt, master);
        Sing.Drive(cues.SingVolume, cues.SingPitch, release, dt, master);
        Render(dt);
    }

    void Render(float dt)
    {
        _carry += dt * OutRate;
        int n = (int)Math.Floor(_carry);
        _carry -= n;
        double baseStep = (double)SR / OutRate;
        for (int v = 0; v < 4; v++)
        {
            var src = _voices[v].Source;
            var stem = Stems[v];
            int start = stem.Count;
            for (int i = 0; i < n; i++) stem.Add(0f);
            if (!src.Playing) continue;
            var clip = src.Clip;
            int len = clip.Length;
            double step = baseStep * src.Pitch;
            for (int i = 0; i < n; i++)
            {
                float vol = src.VolPrev + (src.Vol - src.VolPrev) * (i + 1) / n;
                int i0 = (int)src.Pos;
                double frac = src.Pos - i0;
                float a = clip[i0 % len], b = clip[(i0 + 1) % len];
                stem[start + i] = (float)((a + (b - a) * frac) * vol);
                src.Pos += step;
                if (src.Pos >= len) src.Pos -= len;
            }
            src.VolPrev = src.Vol;
        }
        {
            int s4 = Stems[4].Count, s5 = Stems[5].Count;
            for (int i = 0; i < n; i++) { Stems[4].Add(0f); Stems[5].Add(0f); }
            for (int k = _shots.Count - 1; k >= 0; k--)
            {
                var sh = _shots[k];
                var stem = Stems[sh.Bus];
                int start = sh.Bus == 4 ? s4 : s5;
                int len = sh.Clip.Length;
                for (int i = 0; i < n; i++)
                {
                    int i0 = (int)sh.Pos;
                    if (i0 >= len) break;
                    double frac = sh.Pos - i0;
                    float a = sh.Clip[i0], b = i0 + 1 < len ? sh.Clip[i0 + 1] : 0f;
                    stem[start + i] += (float)((a + (b - a) * frac) * sh.Vol);
                    sh.Pos += baseStep;
                }
                if ((int)sh.Pos >= len) _shots.RemoveAt(k);
            }
        }
    }

    public float[] Mix()
    {
        int n = Stems[0].Count;
        var m = new float[n];
        foreach (var s in Stems)
            for (int i = 0; i < n; i++) m[i] += s[i];
        return m;
    }

    public float[] Sum(params int[] stems)
    {
        int n = Stems[0].Count;
        var m = new float[n];
        foreach (int k in stems)
            for (int i = 0; i < n; i++) m[i] += Stems[k][i];
        return m;
    }
}
