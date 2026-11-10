using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Все клипы палитры «Натуральная», созданные один раз и детерминированно (22050 Гц, моно, float).
    /// Строится в конструкторе — долго (на ПК ~0,5 с, на телефоне — секунды), поэтому FightFeedback строит его
    /// в фоновом потоке; после постройки банк только читается (в том числе звуковым потоком) и не меняется.
    ///
    /// Память ≈ 9,7 МБ (2,53 млн сэмплов float): фон 64 с — 1,41 млн (5,4 МБ), всё остальное — 1,12 млн. Пока строится,
    /// сверх этого живёт ≈ 6 МБ (один черновой слой фона, LakeSynth.LakeBed) и мелкие временные массивы рецептов:
    /// всего за постройку выделяется ≈ 43 МБ (замер AudioSafetyProbe; раньше 102 МБ при пике 70 МБ).
    /// Номера вариантов: Drag 0–7 яркие, 8–15 тёмные; Strain 0–5 малые, 6–11 большие; Creak 0–5 тихие, 6–11 громкие.
    /// </summary>
    public sealed class FightSoundBank
    {
        public const int SampleRate = SynthKit.SampleRate;
        public const int RotorVariants = 16;
        public const int TockVariants = FightSynth.TockVariants;
        public const int DragVariants = 8;
        public const int StrainVariants = 6;
        public const int CrackleVariants = FightSynth.CrackleVariants;
        public const int CreakVariants = 6;
        public const int SplashVariants = 3;
        public const int JumpSplashVariants = 2;
        public const int ZingVariants = 3;

        /// <summary>Пик каждого разового клипа (Normalize 0,9): под него выставлен FightAudioDirector.OneShotMax.</summary>
        public const float OneShotPeak = 0.9f;

        /// <summary>Самое короткое «тело» разового звука для приглушения катушки, с (BodyLength).</summary>
        public const double MinBodySeconds = 0.4;
        public const double DefaultAmbienceSeconds = 64;

        /// <summary>Птицы — как в прототипе (0,12 к постели до нормировки).</summary>
        private const double BirdLevel = 0.12;

        /// <summary>Сверчки — вдали, но слышны: около 2–3% энергии ночного фона по слуху (A).</summary>
        private const double CricketLevel = 0.2;

        public readonly float[][] Rotor = new float[RotorVariants][];
        public readonly float[][] Tock = new float[TockVariants][];
        public readonly float[][] Drag = new float[DragVariants * 2][];
        public readonly float[][] Strain = new float[StrainVariants * 2][];
        public readonly float[][] Crackle = new float[CrackleVariants][];
        public readonly float[][] Creak = new float[CreakVariants * 2][];
        public readonly float[] LayBed;
        public readonly float[] LineHum;
        public readonly float[] LakeBed;

        /// <summary>Ветер над озером (LakeSynth.WindBed): звучит по силе ветра, в штиль молчит.</summary>
        public readonly float[] WindBed;
        public const double WindSeconds = 40;
        public readonly float[][] Orioles = new float[3][];
        public readonly float[][] Cuckoos = new float[2][];
        public readonly float[][] Chirps = new float[2][];
        public readonly float[][] Crickets = new float[4][];

        private readonly float[][] _oneShots;
        private readonly float[][] _farShots;
        private readonly float[][] _splashSmall = new float[SplashVariants * 2][];
        private readonly float[][] _splashBig = new float[SplashVariants * 2][];
        private readonly float[][] _splashJump = new float[JumpSplashVariants * 2][];
        private readonly float[][] _landed = new float[3][];
        private readonly float[][] _zing = new float[ZingVariants][];
        private readonly Dictionary<float[], int> _body = new Dictionary<float[], int>();

        /// <summary>Сколько строился банк, мс (на этом устройстве, в этом потоке).</summary>
        public readonly double BuildMilliseconds;

        /// <summary>Сколько всего сэмплов во всех клипах.</summary>
        public readonly long TotalSamples;

        public FightSoundBank(double ambienceSeconds = DefaultAmbienceSeconds)
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < Rotor.Length; i++)
                Rotor[i] = FightSynth.RotorGrain(0x50700000u + (uint)i * 7919u);
            for (int i = 0; i < Tock.Length; i++)
                Tock[i] = FightSynth.GearTock(i, 0x70C00000u + (uint)i * 104729u);
            for (int i = 0; i < Drag.Length; i++)
                Drag[i] = FightSynth.DragTick(0xD4A60000u + (uint)i * 15485863u, i >= DragVariants);
            for (int i = 0; i < Strain.Length; i++)
                Strain[i] = FightSynth.StrainGrain(0x57A10000u + (uint)i * 32452843u, i >= StrainVariants);
            for (int i = 0; i < Crackle.Length; i++)
                Crackle[i] = FightSynth.LineCrackle(i, 0xC4AC0000u + (uint)i * 49979687u);
            for (int i = 0; i < Creak.Length; i++)
                Creak[i] = FightSynth.RodCreak(0xC4EA0000u + (uint)i * 86028121u, i >= CreakVariants);
            LayBed = FightSynth.LayBed();
            LineHum = FightSynth.LineHum();

            for (int i = 0; i < SplashVariants; i++)
            {
                _splashSmall[i] = FightSynth.Splash(0x5B1A0000u + (uint)i, 0, false);
                _splashSmall[i + SplashVariants] = FightSynth.Splash(0x5B1A0000u + (uint)i, 0, true);
                _splashBig[i] = FightSynth.Splash(0x5B1B0000u + (uint)i, 1, false);
                _splashBig[i + SplashVariants] = FightSynth.Splash(0x5B1B0000u + (uint)i, 1, true);
            }
            for (int i = 0; i < JumpSplashVariants; i++)
            {
                _splashJump[i] = FightSynth.Splash(0x5B1C0000u + (uint)i, 2, false);
                _splashJump[i + JumpSplashVariants] = FightSynth.Splash(0x5B1C0000u + (uint)i, 2, true);
            }
            for (int i = 0; i < 3; i++)
                _landed[i] = FightSynth.Landed(i + 1);
            for (int i = 0; i < ZingVariants; i++)
                _zing[i] = FightSynth.LineZing(i, 0x2149A000u + (uint)i * 2654435761u);

            int count = Enum.GetValues(typeof(FightOneShot)).Length;
            _oneShots = new float[count][];
            _farShots = new float[count][];
            _oneShots[(int)FightOneShot.Cast] = FightSynth.Cast();
            _oneShots[(int)FightOneShot.FloatPlop] = FightSynth.FloatPlop(false);
            _farShots[(int)FightOneShot.FloatPlop] = FightSynth.FloatPlop(true);
            _oneShots[(int)FightOneShot.BiteBloop] = FightSynth.BiteBloop(false);
            _farShots[(int)FightOneShot.BiteBloop] = FightSynth.BiteBloop(true);
            _oneShots[(int)FightOneShot.Hookset] = FightSynth.Hookset();
            _oneShots[(int)FightOneShot.TeleRun] = FightSynth.TeleRun();
            _oneShots[(int)FightOneShot.TeleDive] = FightSynth.TeleDive();
            _oneShots[(int)FightOneShot.TeleSurge] = FightSynth.TeleSurge();
            _oneShots[(int)FightOneShot.TeleJerk] = FightSynth.TeleJerk();
            _oneShots[(int)FightOneShot.TeleJump] = FightSynth.TeleJump();
            _oneShots[(int)FightOneShot.LineSnap] = FightSynth.LineSnap();
            _oneShots[(int)FightOneShot.RodCrack] = FightSynth.RodCrack();
            _oneShots[(int)FightOneShot.Escape] = FightSynth.Escape();

            LakeBed = LakeSynth.LakeBed(ambienceSeconds, out double scale);
            WindBed = LakeSynth.WindBed(Math.Min(WindSeconds, ambienceSeconds));
            for (int i = 0; i < Orioles.Length; i++)
                Orioles[i] = Scaled(LakeSynth.Oriole(0x0B1E0000u + (uint)i * 2654435761u), BirdLevel * scale);
            for (int i = 0; i < Cuckoos.Length; i++)
                Cuckoos[i] = Scaled(LakeSynth.Cuckoo(0xC0C00000u + (uint)i * 2246822519u), BirdLevel * scale);
            for (int i = 0; i < Chirps.Length; i++)
                Chirps[i] = Scaled(LakeSynth.Chirp(0xC41B0000u + (uint)i * 3266489917u), BirdLevel * scale);
            for (int i = 0; i < Crickets.Length; i++)
                Crickets[i] = Scaled(LakeSynth.CricketChirp(0xC41C0000u + (uint)i * 668265263u), CricketLevel * scale);

            foreach (var set in new[] { _oneShots, _farShots, _splashSmall, _splashBig, _splashJump, _landed, _zing })
                foreach (var clip in set)
                    if (clip != null)
                        _body[clip] = Body(clip);

            BuildMilliseconds = watch.Elapsed.TotalMilliseconds;
            TotalSamples = Sum(Rotor) + Sum(Tock) + Sum(Drag) + Sum(Strain) + Sum(Crackle) + Sum(Creak) + LayBed.Length + LineHum.Length
                + LakeBed.Length + WindBed.Length + Sum(Orioles) + Sum(Cuckoos) + Sum(Chirps) + Sum(Crickets) + Sum(_oneShots) + Sum(_farShots)
                + Sum(_splashSmall) + Sum(_splashBig) + Sum(_splashJump) + Sum(_landed) + Sum(_zing);
        }

        /// <summary>Зерно kind варианта variant (номера — как в описании класса).</summary>
        public float[] Grain(FightGrain kind, int variant)
        {
            switch (kind)
            {
                case FightGrain.Rotor: return Rotor[variant];
                case FightGrain.Tock: return Tock[variant];
                case FightGrain.Drag: return Drag[variant];
                case FightGrain.Strain: return Strain[variant];
                case FightGrain.Crackle: return Crackle[variant];
                default: return Creak[variant];
            }
        }

        /// <summary>Разовый звук; для всплесков variant — номер варианта, для Landed — ступень 1–3; far — дальний вариант, если есть.</summary>
        public float[] OneShot(FightOneShot sound, int variant, bool far)
        {
            int f = far ? 1 : 0;
            switch (sound)
            {
                case FightOneShot.SplashSmall: return _splashSmall[Clamp(variant, SplashVariants) + f * SplashVariants];
                case FightOneShot.SplashBig: return _splashBig[Clamp(variant, SplashVariants) + f * SplashVariants];
                case FightOneShot.SplashJump: return _splashJump[Clamp(variant, JumpSplashVariants) + f * JumpSplashVariants];
                case FightOneShot.Landed: return _landed[Math.Max(1, Math.Min(3, variant)) - 1];
                case FightOneShot.LineZing: return _zing[Clamp(variant, ZingVariants)];
                default:
                    int i = (int)sound;
                    if (i < 0 || i >= _oneShots.Length)
                        return null;
                    return far && _farShots[i] != null ? _farShots[i] : _oneShots[i];
            }
        }

        /// <summary>
        /// Тело разового клипа — до сэмпла, где набралось 90% его энергии (дальше тихий хвост: капли, эхо), но не короче
        /// 0,4 с (время, за которое ухо складывает громкость: короткий удар выделяется, только если 0,4 с вокруг него
        /// катушка тише): столько FightAudioMixer держит катушку приглушённой. Для чужого клипа — вся длина.
        /// Только чтение — можно со звукового потока.
        /// </summary>
        public int BodyLength(float[] clip) => clip != null && _body.TryGetValue(clip, out int n) ? n : clip?.Length ?? 0;

        private static int Body(float[] clip)
        {
            double total = 0;
            foreach (var v in clip)
                total += (double)v * v;
            double sum = 0;
            for (int i = 0; i < clip.Length; i++)
            {
                sum += (double)clip[i] * clip[i];
                if (sum >= 0.9 * total)
                    return Math.Min(clip.Length, Math.Max(i + 1, (int)(MinBodySeconds * SampleRate)));
            }
            return clip.Length;
        }

        private static int Clamp(int variant, int count) => Math.Max(0, Math.Min(count - 1, variant));

        private static float[] Scaled(float[] s, double gain)
        {
            SynthKit.Scale(s, gain);
            return s;
        }

        private static long Sum(float[][] clips)
        {
            long n = 0;
            foreach (var c in clips)
                if (c != null)
                    n += c.Length;
            return n;
        }
    }
}
