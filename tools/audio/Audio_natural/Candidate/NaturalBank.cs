using System;

namespace Fishing.Unity
{
    /// <summary>
    /// Все клипы «натуральной» палитры, созданные один раз (детерминированно). Номера вариантов — как в NaturalCues.
    /// Память: ~1,5 млн сэмплов (фон 64 с — 1,41 млн; всё остальное ~0,1 млн).
    /// </summary>
    internal sealed class NaturalBank
    {
        public readonly float[][] Rotor = new float[NaturalCues.RotorVariants][];
        public readonly float[][] Tock = new float[NaturalCues.TockVariants][];
        public readonly float[][] Drag = new float[NaturalCues.DragVariants * 2][];
        public readonly float[][] Strain = new float[NaturalCues.StrainVariants * 2][];
        public readonly float[][] Crackle = new float[NaturalCues.CrackleVariants][];
        public readonly float[][] Creak = new float[NaturalCues.CreakVariants * 2][];
        public readonly float[] LayBed;
        public readonly float[] LineHum;
        public readonly float[] Ambience;
        private readonly float[][] _oneShots;
        private readonly float[][] _farShots;
        private readonly float[][] _splashSmall = new float[NaturalCues.SplashVariants * 2][];
        private readonly float[][] _splashBig = new float[NaturalCues.SplashVariants * 2][];
        private readonly float[][] _splashJump = new float[4][];
        private readonly float[][] _landed = new float[3][];

        public NaturalBank(double ambienceSeconds = 64)
        {
            for (int i = 0; i < Rotor.Length; i++)
                Rotor[i] = NaturalSynth.RotorGrain(0x50700000u + (uint)i * 7919u);
            for (int i = 0; i < Tock.Length; i++)
                Tock[i] = NaturalSynth.GearTock(0x70C00000u + (uint)i * 104729u);
            for (int i = 0; i < Drag.Length; i++)
                Drag[i] = NaturalSynth.DragTick(0xD4A60000u + (uint)i * 15485863u, i >= NaturalCues.DragVariants);
            for (int i = 0; i < Strain.Length; i++)
                Strain[i] = NaturalSynth.StrainGrain(0x57A10000u + (uint)i * 32452843u, i >= NaturalCues.StrainVariants);
            for (int i = 0; i < Crackle.Length; i++)
                Crackle[i] = NaturalSynth.LineCrackle(0xC4AC0000u + (uint)i * 49979687u);
            for (int i = 0; i < Creak.Length; i++)
                Creak[i] = NaturalSynth.RodCreak(0xC4EA0000u + (uint)i * 86028121u, i >= NaturalCues.CreakVariants);
            LayBed = NaturalSynth.LayBed();
            LineHum = NaturalSynth.LineHum();
            Ambience = NaturalSynth.LakeMorning(ambienceSeconds);

            for (int i = 0; i < NaturalCues.SplashVariants; i++)
            {
                _splashSmall[i] = NaturalSynth.Splash(0x5B1A0000u + (uint)i, 0, false);
                _splashSmall[i + NaturalCues.SplashVariants] = NaturalSynth.Splash(0x5B1A0000u + (uint)i, 0, true);
                _splashBig[i] = NaturalSynth.Splash(0x5B1B0000u + (uint)i, 1, false);
                _splashBig[i + NaturalCues.SplashVariants] = NaturalSynth.Splash(0x5B1B0000u + (uint)i, 1, true);
            }
            for (int i = 0; i < 2; i++)
            {
                _splashJump[i] = NaturalSynth.Splash(0x5B1C0000u + (uint)i, 2, false);
                _splashJump[i + 2] = NaturalSynth.Splash(0x5B1C0000u + (uint)i, 2, true);
            }
            for (int i = 0; i < 3; i++)
                _landed[i] = NaturalSynth.Landed(i + 1);

            int count = Enum.GetValues(typeof(NaturalSound)).Length;
            _oneShots = new float[count][];
            _farShots = new float[count][];
            _oneShots[(int)NaturalSound.Cast] = NaturalSynth.Cast();
            _oneShots[(int)NaturalSound.FloatPlop] = NaturalSynth.FloatPlop(false);
            _farShots[(int)NaturalSound.FloatPlop] = NaturalSynth.FloatPlop(true);
            _oneShots[(int)NaturalSound.BiteBloop] = NaturalSynth.BiteBloop(false);
            _farShots[(int)NaturalSound.BiteBloop] = NaturalSynth.BiteBloop(true);
            _oneShots[(int)NaturalSound.Hookset] = NaturalSynth.Hookset();
            _oneShots[(int)NaturalSound.TeleRun] = NaturalSynth.TeleRun();
            _oneShots[(int)NaturalSound.TeleDive] = NaturalSynth.TeleDive();
            _oneShots[(int)NaturalSound.TeleSurge] = NaturalSynth.TeleSurge();
            _oneShots[(int)NaturalSound.TeleJerk] = NaturalSynth.TeleJerk();
            _oneShots[(int)NaturalSound.TeleJump] = NaturalSynth.TeleJump();
            _oneShots[(int)NaturalSound.LineSnap] = NaturalSynth.LineSnap();
            _oneShots[(int)NaturalSound.RodCrack] = NaturalSynth.RodCrack();
            _oneShots[(int)NaturalSound.Escape] = NaturalSynth.Escape();
        }

        public float[] Grain(NaturalGrain kind, int variant)
        {
            switch (kind)
            {
                case NaturalGrain.Rotor: return Rotor[variant];
                case NaturalGrain.Tock: return Tock[variant];
                case NaturalGrain.Drag: return Drag[variant];
                case NaturalGrain.Strain: return Strain[variant];
                case NaturalGrain.Crackle: return Crackle[variant];
                default: return Creak[variant];
            }
        }

        public float[] Sound(in NaturalCue cue)
        {
            int far = cue.Far ? 1 : 0;
            switch (cue.Sound)
            {
                case NaturalSound.SplashSmall: return _splashSmall[cue.Variant + far * NaturalCues.SplashVariants];
                case NaturalSound.SplashBig: return _splashBig[cue.Variant + far * NaturalCues.SplashVariants];
                case NaturalSound.SplashJump: return _splashJump[cue.Variant + far * 2];
                case NaturalSound.Landed: return _landed[cue.Variant];
                default:
                    return cue.Far && _farShots[(int)cue.Sound] != null ? _farShots[(int)cue.Sound] : _oneShots[(int)cue.Sound];
            }
        }

        public float[] SplashClip(int size, int variant, bool far) =>
            size == 0 ? _splashSmall[variant + (far ? NaturalCues.SplashVariants : 0)]
            : size == 1 ? _splashBig[variant + (far ? NaturalCues.SplashVariants : 0)]
            : _splashJump[variant + (far ? 2 : 0)];

        public float[] LandedClip(int tier) => _landed[tier - 1];
    }
}
