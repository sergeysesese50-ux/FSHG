using System;
using System.Collections.Generic;
using Fishing.Core;

namespace Fishing.Unity
{
    /// <summary>Повторяющиеся короткие звуки — идут через GrainMixer с точностью до сэмпла.</summary>
    internal enum NaturalGrain
    {
        Rotor,
        Tock,
        Drag,
        Strain,
        Crackle,
        Creak,
    }

    /// <summary>Зерно: что, какой вариант, когда (часы dsp), громкость (до общей), высота.</summary>
    internal struct GrainCue
    {
        public NaturalGrain Kind;
        public int Variant;
        public double Time;
        public float Gain;
        public float Pitch;
    }

    /// <summary>Разовые звуки (PlayOneShot, высота 1).</summary>
    internal enum NaturalSound
    {
        Cast,
        FloatPlop,
        BiteBloop,
        Hookset,
        TeleRun,
        TeleDive,
        TeleSurge,
        TeleJerk,
        TeleJump,
        SplashSmall,
        SplashBig,
        SplashJump,
        Landed,
        LineSnap,
        RodCrack,
        Escape,
    }

    internal struct NaturalCue
    {
        public NaturalSound Sound;
        public int Variant;
        public bool Far;
        public float Volume;
    }

    /// <summary>Всё, что палитра читает из FightFrame за кадр (одинаково у игрока и у зрителя).</summary>
    internal struct NaturalInput
    {
        public int Ticks;
        public float ReelInCmPerSec;
        public float ReelLoad;
        public bool IsDragSlipping;
        public bool IsCrankingIntoSlip;
        public float SlipCmPerSec;
        public float SlipCmThisFrame;
        public float LoadShare;
        public float RodLoadShare;
        public float SlackCm;
        public float FishDistanceCm;
        public FishPhase Phase;
        public FishPhase NextPhase;
        public FightEvents Events;
        public FightOutcome Outcome;
        public bool EndedThisFrame;
        public int WeightG;
        public int Tick;

        public static NaturalInput Of(FightFrame f) => new NaturalInput
        {
            Ticks = f.TicksThisFrame,
            ReelInCmPerSec = f.ReelInCmPerSec,
            ReelLoad = f.ReelLoad,
            IsDragSlipping = f.IsDragSlipping,
            IsCrankingIntoSlip = f.IsCrankingIntoSlip,
            SlipCmPerSec = f.SlipCmPerSec,
            SlipCmThisFrame = f.SlipCmThisFrame,
            LoadShare = f.LoadShare,
            RodLoadShare = f.RodLoadShare,
            SlackCm = f.SlackCm,
            FishDistanceCm = f.FishDistanceCm,
            Phase = f.Phase,
            NextPhase = f.NextPhase,
            Events = f.Events,
            Outcome = f.Outcome,
            EndedThisFrame = f.EndedThisFrame,
            WeightG = f.WeightG,
            Tick = f.Tick,
        };
    }

    /// <summary>
    /// «Натуральная» палитра: что звучит на озере (замена FightCues по звуку; вибрация — как была).
    /// Раз в кадр по State и FightFrame решает: какие зёрна поставить и на какое время, какие разовые
    /// звуки сыграть, громкость и высоту трёх петель (укладка лески, гул лески, фон озера).
    ///
    /// Катушка — не петля с высотой, а отдельные зёрна по пройденной леске:
    /// | Зерно        | Когда                                                     | Громкость / высота                  |
    /// | ротор        | каждые 14 см оборота ротора (7,9/с при 110 см/с, 18/с при 250) | 0,48·(0,6+0,4·s), высота (0,97+0,08s)(1−0,15·нагрузка) |
    /// | шестерня     | каждые 85 см (оборот ручки), со сдвигом 40 см               | 0,14, высота 1−0,1·нагрузка         |
    /// | натуга       | нагрузка > 0,4: 3→40 срывов/с, интервалы ×0,5–2              | 0,4·(0,5+0,5k)·√(накоплено)         |
    /// | трещотка     | каждые 1,2 см пробуксовки, ±5% по времени                    | 0,48, высота ±2%; выше 15/с ×√(15/частота) и темнее; первые 3 — +2 дБ |
    /// | треск лески  | LoadShare > 0,88 на натянутой: 2→14/с                        | 0,12                                |
    /// | скрип удилища| RodLoadShare > 0,75: раз в 0,9→0,35 с ±30%                   | 0,30→0,60, громкий вариант при k > 0,5 |
    /// s = скорость ротора / C0; скорость ротора — ReelIn, а при «Подмотке» в пробуксовку — C0·(1 − ReelLoad).
    /// Все счётчики (см лески, см пробуксовки, срывы) копятся по шагам модели, вариант = хеш номера события —
    /// зритель с теми же шагами слышит то же самое. Время зерна = окно шага модели на часах dsp + 40 мс.
    ///
    /// Петли: укладка — 0,36·max(s^0,8; 0,35·нагрузка) (и «сход лески» при пробуксовке 0,22·(slip/60)^0,7), высота
    /// (0,82+0,3s)(1−0,12·нагрузка), медленная волна раскладчика ±10% раз в 520 см; гул лески — 0,25·k^1,5 при
    /// LoadShare > 0,7 (k = (x−0,7)/0,3), высота 1+0,5(x−0,7); фон — постоянно 0,09 (≈14 дБ под катушкой).
    /// При отпускании «Подмотки» высота не ныряет — замирает; петли стартуют не с 0, а с хешированного места.
    /// </summary>
    internal sealed class NaturalCues
    {
        // ---------- Варианты в банке (NaturalBank) ----------
        public const int RotorVariants = 16;
        public const int TockVariants = 8;
        public const int DragVariants = 8; // ×2: яркие 0–7, тёмные 8–15
        public const int StrainVariants = 6; // ×2: малые 0–5, большие 6–11
        public const int CrackleVariants = 8;
        public const int CreakVariants = 6; // ×2: тихие 0–5, громкие 6–11
        public const int SplashVariants = 3;

        // ---------- Время ----------
        private const double TickSeconds = 0.05;
        private const double Latency = 0.04;
        private const double FirstSlack = 0.02;
        private const double MaxLead = 0.12;

        // ---------- Катушка ----------
        private const double RotorCm = 14.0;
        private const double HandleCm = 85.0;
        private const double HandleOffsetCm = 40.0;
        private const double StrokeCm = 520.0;
        private const float RotorLevel = 0.48f;
        private const float TockLevel = 0.14f;
        private const float StrainLevel = 0.40f;
        private const float LayLevel = 0.36f;
        private const float PeelLevel = 0.22f;

        // ---------- Фрикцион ----------
        private const double DragCm = 1.2;
        private const float DragLevel = 0.48f;
        private const double DragSoftRate = 15.0;
        private const double DragRunGapSeconds = 0.25;

        // ---------- Леска и удилище ----------
        private const float HumLevel = 0.25f;
        private const float CrackleLevel = 0.12f;
        private const float CreakQuiet = 0.30f;
        private const float CreakLoud = 0.60f;
        private const float TightSlackCm = 30f;

        // ---------- Разовые ----------
        private const float CastLevel = 0.45f;
        private const float PlopLevel = 0.5f;
        private const float BiteLevel = 0.55f;
        private const float HooksetLevel = 0.55f;
        private const float TeleJerkLevel = 0.6f;
        private const float TeleWaterLevel = 0.6f;
        private const float LandedLevel = 0.5f;
        private const float SnapLevel = 0.8f;
        private const float SpoolSnapLevel = 0.55f;
        private const float CrackLevel = 0.85f;
        private const float EscapeLevel = 0.55f;
        private const float FarMeters = 12f;
        private const int HeavySplashG = 1200;

        // ---------- Фон ----------
        private const float AmbienceLevel = 0.09f;
        private const float LoopReleaseFight = 0.15f;
        private const float LoopFadeOut = 0.4f;

        private readonly List<GrainCue> _grains = new List<GrainCue>(32);
        private readonly List<NaturalCue> _sounds = new List<NaturalCue>(8);
        private LakeState _state;
        private FishPhase _lastPhase;
        private double _cursor;
        private double _rotorCm;
        private long _rotorIndex, _handleIndex;
        private double _slipCm;
        private long _slipIndex;
        private double _sinceSlip = 10;
        private int _runTicks;
        private double _strainPhase, _strainNext = 1;
        private long _strainIndex;
        private double _cracklePhase, _crackleNext = 1;
        private long _crackleIndex;
        private double _creakTimer;
        private long _creakIndex;
        private long _eventIndex;
        private int _lastRotor = -1, _lastTock = -1, _lastDrag = -1, _lastStrain = -1, _lastCrackle = -1, _lastCreak = -1;
        private readonly int[] _lastSplash = { -1, -1, -1 };

        public IReadOnlyList<GrainCue> Grains => _grains;
        public IReadOnlyList<NaturalCue> Sounds => _sounds;

        public float LayVolume { get; private set; }
        public float LayPitch { get; private set; } = 1f;
        public float HumVolume { get; private set; }
        public float HumPitch { get; private set; } = 1f;
        public float AmbienceVolume { get; private set; } = AmbienceLevel;
        public float LoopReleaseSeconds { get; private set; } = LoopFadeOut;

        /// <summary>Оборвать петли и зёрна сразу (обрыв, поломка).</summary>
        public bool CutLoops { get; private set; }

        /// <summary>
        /// Ступень награды за поимку: 1 — мелочь, 2 — обычная, 3 — крупная или рекорд. Ставит вызывающий
        /// на подсечке (по весу рыбы против Species.weightG.typical / trophy) — у зрителя то же.
        /// </summary>
        public int RewardTier { get; set; } = 2;

        /// <summary>
        /// Один кадр, после FishingController.Update. audioNow — часы dsp (AudioSettings.dspTime);
        /// c0 — Tackle.ReelFightSpeedCmPerSec (по умолчанию 110).
        /// </summary>
        public void Update(LakeState state, in NaturalInput f, float c0, float floatDistanceM, double audioNow, float dt)
        {
            _grains.Clear();
            _sounds.Clear();
            CutLoops = false;
            if (c0 <= 0f)
                c0 = 110f;

            if (state != _state)
            {
                var from = _state;
                _state = state;
                StateChanged(from, state, f, floatDistanceM, audioNow);
            }

            bool fight = state == LakeState.Fight;
            double w0 = 0, span = 0;
            if ((fight || f.EndedThisFrame) && f.Ticks > 0)
            {
                span = f.Ticks * TickSeconds;
                double min = audioNow + Latency;
                if (_cursor < min)
                    _cursor = min;
                else if (_cursor > min + MaxLead)
                    _cursor = min + MaxLead;
                w0 = _cursor;
                _cursor += span;
            }

            if (fight || f.EndedThisFrame)
            {
                Events(f);
                if (span > 0 && !f.EndedThisFrame)
                {
                    Reel(f, c0, w0, span);
                    Drag(f, w0, span);
                    Tension(f, w0, span);
                }
                if (f.EndedThisFrame)
                    Ended(f);
                _lastPhase = f.Phase;
            }

            Loops(f, c0, fight);
        }

        // ---------- Смены состояния ----------

        private void StateChanged(LakeState from, LakeState to, in NaturalInput f, float floatDistanceM, double audioNow)
        {
            switch (to)
            {
                case LakeState.Casting:
                    Play(NaturalSound.Cast, 0, false, CastLevel);
                    break;
                case LakeState.Waiting when from == LakeState.Casting:
                    Play(NaturalSound.FloatPlop, 0, floatDistanceM > FarMeters, PlopLevel * DistanceFactor(floatDistanceM));
                    break;
                case LakeState.Bite:
                    Play(NaturalSound.BiteBloop, 0, floatDistanceM > FarMeters, BiteLevel * DistanceFactor(floatDistanceM));
                    break;
                case LakeState.Fight:
                    BeginFight(audioNow);
                    break;
            }
        }

        private void BeginFight(double audioNow)
        {
            _cursor = audioNow + Latency + FirstSlack;
            _rotorCm = 0;
            _rotorIndex = 0;
            _handleIndex = 0;
            _slipCm = 0;
            _slipIndex = 0;
            _sinceSlip = 10;
            _runTicks = 0;
            _strainPhase = 0;
            _strainNext = StrainGapOf(0);
            _strainIndex = 0;
            _cracklePhase = 0;
            _crackleNext = StrainGapOf(100000);
            _crackleIndex = 0;
            _creakTimer = 0;
            _creakIndex = 0;
            _eventIndex = 0;
            Play(NaturalSound.Hookset, 0, false, HooksetLevel);
        }

        // ---------- События кадра ----------

        private void Events(in NaturalInput f)
        {
            var e = f.Events;
            if (Has(e, FightEvents.TelegraphStart) && !f.EndedThisFrame)
            {
                float water = TeleWaterLevel * (0.6f + 0.4f * DistanceFactor(f.FishDistanceCm / 100f));
                switch (f.NextPhase)
                {
                    case FishPhase.Run: Play(NaturalSound.TeleRun, 0, false, water); break;
                    case FishPhase.Dive: Play(NaturalSound.TeleDive, 0, false, water); break;
                    case FishPhase.Surge: Play(NaturalSound.TeleSurge, 0, false, water); break;
                    case FishPhase.Jump: Play(NaturalSound.TeleJump, 0, false, water); break;
                    case FishPhase.Jerk: Play(NaturalSound.TeleJerk, 0, false, TeleJerkLevel); break;
                }
            }
            if (Has(e, FightEvents.PhaseStart))
            {
                if (_lastPhase == FishPhase.Jump && f.Phase != FishPhase.Jump)
                    Splash(f, NaturalSound.SplashJump, 0.9f);
                switch (f.Phase)
                {
                    case FishPhase.Run: Splash(f, NaturalSound.SplashSmall, 0.55f); break;
                    case FishPhase.Jerk: Splash(f, NaturalSound.SplashSmall, 0.45f); break;
                    case FishPhase.Dive: Splash(f, NaturalSound.SplashSmall, 0.3f); break;
                }
            }
            if (Has(e, FightEvents.JumpApex))
                Splash(f, f.WeightG >= HeavySplashG ? NaturalSound.SplashBig : NaturalSound.SplashSmall, 0.8f);
            if (Has(e, FightEvents.SurgeStart))
                Splash(f, f.WeightG >= HeavySplashG ? NaturalSound.SplashBig : NaturalSound.SplashSmall, 0.85f);
        }

        private void Ended(in NaturalInput f)
        {
            switch (f.Outcome)
            {
                case FightOutcome.Landed:
                    Play(NaturalSound.Landed, Math.Max(1, Math.Min(3, RewardTier)) - 1, false, LandedLevel);
                    break;
                case FightOutcome.LineBroke:
                    CutLoops = true;
                    Play(NaturalSound.LineSnap, 0, false, SnapLevel);
                    break;
                case FightOutcome.SpoolEmpty:
                    CutLoops = true;
                    Play(NaturalSound.LineSnap, 0, false, SpoolSnapLevel);
                    break;
                case FightOutcome.RodBroke:
                    CutLoops = true;
                    Play(NaturalSound.RodCrack, 0, false, CrackLevel);
                    break;
                case FightOutcome.Escaped:
                    Play(NaturalSound.Escape, 0, false, EscapeLevel);
                    break;
            }
        }

        // ---------- Катушка ----------

        /// <summary>Скорость ротора, см лески в секунду: выбирает — ReelIn; крутят в пробуксовку — C0·(1 − ReelLoad).</summary>
        private static double RotorSpeed(in NaturalInput f, float c0)
        {
            double v = f.ReelInCmPerSec > 0.5f ? f.ReelInCmPerSec : 0;
            if (f.IsCrankingIntoSlip)
                v = Math.Max(v, c0 * (1 - f.ReelLoad));
            return v;
        }

        private void Reel(in NaturalInput f, float c0, double w0, double span)
        {
            double v = RotorSpeed(f, c0);
            double s = v / c0;
            double load = f.ReelLoad;
            double d0 = _rotorCm, d1 = d0 + v * span;
            _rotorCm = d1;

            if (d1 > d0)
            {
                double period = RotorCm / v;
                while ((_rotorIndex + 1) * RotorCm <= d1)
                {
                    _rotorIndex++;
                    uint h = NaturalSynth.Hash((uint)_rotorIndex * 2654435761u + 11u);
                    double t = w0 + (_rotorIndex * RotorCm - d0) / (d1 - d0) * span + period * 0.04 * Signed(h);
                    float gain = (float)(RotorLevel * (0.6 + 0.4 * Math.Min(1, s)) * Db(1.5 * Signed(h >> 10)));
                    float pitch = (float)((0.97 + 0.08 * Math.Min(1.2, s)) * (1 - 0.15 * load) * (1 + 0.012 * Signed(h >> 20)));
                    Grain(NaturalGrain.Rotor, Pick(h, RotorVariants, ref _lastRotor), t, gain, pitch);
                }
                while (HandleOffsetCm + (_handleIndex + 1) * HandleCm <= d1)
                {
                    _handleIndex++;
                    uint h = NaturalSynth.Hash((uint)_handleIndex * 40503u + 7u);
                    double t = w0 + (HandleOffsetCm + _handleIndex * HandleCm - d0) / (d1 - d0) * span;
                    float gain = (float)(TockLevel * Db(1.5 * Signed(h >> 10)));
                    float pitch = (float)((1 - 0.1 * load) * (1 + 0.01 * Signed(h >> 20)));
                    Grain(NaturalGrain.Tock, Pick(h, TockVariants, ref _lastTock), t, gain, pitch);
                }
            }

            // Натуга: срыв-прилипание — тем чаще, чем тяжелее крутить.
            bool cranking = v > 0;
            if (cranking && load > 0.4)
            {
                double k = Math.Min(1, (load - 0.4) / 0.6);
                double rate = (3 + 37 * k) * (f.IsDragSlipping ? 0.4 : 1.0);
                double p0 = _strainPhase;
                _strainPhase += rate * span;
                while (_strainPhase >= _strainNext)
                {
                    _strainIndex++;
                    uint h = NaturalSynth.Hash((uint)_strainIndex * 0x85EBCA6Bu + 5u);
                    double frac = (_strainNext - p0) / (_strainPhase - p0);
                    double t = w0 + Math.Max(0, Math.Min(1, frac)) * span;
                    double built = Math.Sqrt(Math.Max(0.25, Math.Min(2.0, StrainGapOf(_strainIndex - 1))) / 2);
                    bool big = built > 0.8 && k > 0.4;
                    float gain = (float)(StrainLevel * (0.5 + 0.5 * k) * built * Db(1.5 * Signed(h >> 10)) * (f.IsDragSlipping ? 0.6 : 1));
                    float pitch = (float)((1 - 0.06 * k) * (1 + 0.03 * Signed(h >> 20)));
                    int variant = Pick(h, StrainVariants, ref _lastStrain) + (big ? StrainVariants : 0);
                    Grain(NaturalGrain.Strain, variant, t, gain, pitch);
                    p0 = _strainNext;
                    _strainNext += StrainGapOf(_strainIndex);
                }
            }
            else
            {
                _strainPhase = 0;
                _strainNext = StrainGapOf(_strainIndex);
            }
        }

        /// <summary>Интервал до следующего срыва в «средних интервалах»: 0,5–2 (лог-равномерно) по хешу номера.</summary>
        private static double StrainGapOf(long index)
        {
            uint h = NaturalSynth.Hash((uint)index * 0x27D4EB2Fu + 3u);
            return Math.Pow(2, 2 * Unit(h) - 1);
        }

        // ---------- Фрикцион ----------

        private void Drag(in NaturalInput f, double w0, double span)
        {
            double slip = f.IsDragSlipping ? f.SlipCmThisFrame : 0;
            if (slip <= 0)
            {
                _sinceSlip += span;
                return;
            }
            if (_sinceSlip >= DragRunGapSeconds)
                _runTicks = 0;
            _sinceSlip = 0;

            double rate = Math.Max(1, f.SlipCmPerSec / DragCm);
            double s0 = _slipCm, s1 = s0 + slip;
            _slipCm = s1;

            if (Has(f.Events, FightEvents.Breakaway) && _runTicks == 0)
            {
                // Трогание слышно сразу, а следующий щелчок — через полные 1,2 см.
                _slipIndex = (long)Math.Floor(s0 / DragCm);
                _slipCm = _slipIndex * DragCm + (s1 - s0);
                s1 = _slipCm;
                s0 = _slipIndex * DragCm;
                DragTick(w0, rate, 1.0);
            }

            double period = 1 / rate;
            while ((_slipIndex + 1) * DragCm <= s1)
            {
                _slipIndex++;
                double frac = (_slipIndex * DragCm - s0) / Math.Max(1e-9, s1 - s0);
                uint h = NaturalSynth.Hash((uint)_slipIndex * 0x9E3779B9u + 3u);
                DragTick(w0 + Math.Max(0, Math.Min(1, frac)) * span + period * 0.05 * Signed(h), rate, 0);
            }
        }

        private void DragTick(double t, double rate, double extraAccent)
        {
            uint h = NaturalSynth.Hash((uint)_slipIndex * 0x9E3779B9u + 3u + (uint)_runTicks * 977u);
            double gain = DragLevel * (rate <= DragSoftRate ? 1 : Math.Sqrt(DragSoftRate / rate));
            if (_runTicks < 3)
                gain *= Db(2);
            gain *= Db(1.5 * Signed(h >> 10));
            double darkChance = Math.Max(0, Math.Min(1, (rate - DragSoftRate) / 45));
            bool dark = Unit(h >> 4) < darkChance;
            int variant = Pick(h >> 7, DragVariants, ref _lastDrag) + (dark ? DragVariants : 0);
            Grain(NaturalGrain.Drag, variant, t, (float)gain, (float)(1 + 0.02 * Signed(h >> 20)));
            _runTicks++;
        }

        // ---------- Леска и удилище ----------

        private void Tension(in NaturalInput f, double w0, double span)
        {
            bool tight = f.SlackCm <= TightSlackCm;
            double x = f.LoadShare;
            if (tight && x > 0.88)
            {
                double rate = 2 + 12 * Math.Min(1, (x - 0.88) / 0.12);
                double p0 = _cracklePhase;
                _cracklePhase += rate * span;
                while (_cracklePhase >= _crackleNext)
                {
                    _crackleIndex++;
                    uint h = NaturalSynth.Hash((uint)_crackleIndex * 0xC2B2AE35u + 9u);
                    double frac = (_crackleNext - p0) / (_cracklePhase - p0);
                    float gain = (float)(CrackleLevel * Db(2 * Signed(h >> 10)));
                    Grain(NaturalGrain.Crackle, Pick(h, CrackleVariants, ref _lastCrackle), w0 + frac * span, gain, (float)(1 + 0.03 * Signed(h >> 20)));
                    p0 = _crackleNext;
                    _crackleNext += StrainGapOf(_crackleIndex + 100000);
                }
            }
            else
            {
                _cracklePhase = 0;
                _crackleNext = StrainGapOf(_crackleIndex + 100000);
            }

            _creakTimer -= span;
            if (f.RodLoadShare > 0.75f && _creakTimer <= 0)
            {
                _creakIndex++;
                uint h = NaturalSynth.Hash((uint)_creakIndex * 0x165667B1u + 13u);
                double k = Math.Min(1, (f.RodLoadShare - 0.75) / 0.25);
                bool loud = k > 0.5;
                float gain = (float)((CreakQuiet + (CreakLoud - CreakQuiet) * k) * Db(1.5 * Signed(h >> 10)));
                int variant = Pick(h, CreakVariants, ref _lastCreak) + (loud ? CreakVariants : 0);
                Grain(NaturalGrain.Creak, variant, w0 + Unit(h >> 3) * span, gain, (float)(1 + 0.02 * Signed(h >> 20)));
                _creakTimer = (0.9 + (0.35 - 0.9) * k) * (0.7 + 0.6 * Unit(h >> 14));
            }
        }

        // ---------- Петли ----------

        private void Loops(in NaturalInput f, float c0, bool fight)
        {
            LoopReleaseSeconds = fight ? LoopReleaseFight : LoopFadeOut;
            AmbienceVolume = AmbienceLevel;
            if (!fight)
            {
                LayVolume = 0f;
                HumVolume = 0f;
                return;
            }

            double v = RotorSpeed(f, c0);
            double s = Math.Min(1.2, v / c0);
            double load = f.ReelLoad;
            // Под нагрузкой ротор почти стоит, но механизм «шуршит» натугой: пол 0,35·нагрузка, чтобы натуга не звучала в тишине.
            double reel = v > 0 ? LayLevel * Math.Max(Math.Pow(s, 0.8), 0.35 * load) * (1 + 0.10 * Math.Sin(2 * Math.PI * _rotorCm / StrokeCm)) : 0;
            double slip = f.IsDragSlipping ? Math.Min(1.5, f.SlipCmPerSec / 60.0) : 0;
            double peel = PeelLevel * Math.Pow(slip, 0.7);
            double vol = Math.Sqrt(reel * reel + peel * peel);
            LayVolume = (float)vol;
            if (vol > 1e-4)
            {
                double pr = (0.82 + 0.3 * s) * (1 - 0.12 * load);
                double pp = 0.85 + 0.35 * slip;
                LayPitch = (float)((reel * reel * pr + peel * peel * pp) / (vol * vol));
            }

            bool tight = f.SlackCm <= TightSlackCm;
            double x = f.LoadShare;
            double k = Math.Max(0, Math.Min(1, (x - 0.7) / 0.3));
            HumVolume = tight ? (float)(HumLevel * Math.Pow(k, 1.5)) : 0f;
            if (HumVolume > 0)
                HumPitch = (float)(1 + 0.5 * Math.Max(0, Math.Min(0.6, x - 0.7)));
        }

        // ---------- Кирпичики ----------

        private void Splash(in NaturalInput f, NaturalSound sound, float strength)
        {
            float kg = f.WeightG / 1000f;
            float weight = Math.Max(0.3f, Math.Min(1f, 0.3f + 0.7f * (float)Math.Sqrt(kg / 3f)));
            float meters = f.FishDistanceCm / 100f;
            if (sound == NaturalSound.SplashSmall && f.WeightG >= HeavySplashG)
                sound = NaturalSound.SplashBig;
            int slot = sound == NaturalSound.SplashSmall ? 0 : sound == NaturalSound.SplashBig ? 1 : 2;
            int count = sound == NaturalSound.SplashJump ? 2 : SplashVariants;
            _eventIndex++;
            uint h = NaturalSynth.Hash((uint)f.Tick * 0x9E3779B9u + (uint)_eventIndex * 31u + (uint)slot);
            int variant = Pick(h, count, ref _lastSplash[slot]);
            Play(sound, variant, meters > FarMeters, strength * weight * DistanceFactor(meters));
        }

        /// <summary>Тише с расстоянием: до 3 м — полная громкость, на 15 м — половина, на 30 м — треть.</summary>
        private static float DistanceFactor(float meters) => 1f / (1f + Math.Max(0f, meters - 3f) / 12f);

        private void Play(NaturalSound sound, int variant, bool far, float volume)
        {
            if (volume > 0f)
                _sounds.Add(new NaturalCue { Sound = sound, Variant = variant, Far = far, Volume = Math.Min(volume, 1f) });
        }

        private void Grain(NaturalGrain kind, int variant, double time, float gain, float pitch)
        {
            if (gain > 0f)
                _grains.Add(new GrainCue { Kind = kind, Variant = variant, Time = time, Gain = gain, Pitch = pitch });
        }

        /// <summary>Вариант по хешу, никогда не тот же, что в прошлый раз.</summary>
        private static int Pick(uint h, int count, ref int last)
        {
            int v = (int)(h % (uint)count);
            if (v == last && count > 1)
                v = (v + 1 + (int)((h >> 8) % (uint)(count - 1))) % count;
            last = v;
            return v;
        }

        private static double Unit(uint h) => (h & 0xFFFF) / 65535.0;

        private static double Signed(uint h) => 2 * Unit(h) - 1;

        private static double Db(double db) => Math.Pow(10, db / 20);

        private static bool Has(FightEvents events, FightEvents flag) => (events & flag) != 0;
    }
}
