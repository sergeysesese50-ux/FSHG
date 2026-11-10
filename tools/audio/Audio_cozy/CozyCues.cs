using System;
using System.Collections.Generic;
using Fishing.Core;

namespace Fishing.Unity
{
    /// <summary>Звуки «уютной» палитры. Зёрна идут в микшер зёрен (точное время внутри кадра), разовые — PlayOneShot.</summary>
    internal enum CozySound
    {
        // Зёрна
        GearLight,
        Gear,
        GearHeavy,
        HandleTock,
        DragTick,
        DragTickDark,
        DragPurr,
        RodCreak,
        // Разовые
        Hookset,
        TeleRun,
        TeleDive,
        TeleSurge,
        TeleJerk,
        TeleJump,
        SplashSmall,
        SplashBig,
        SplashJump,
        LandedSmall,
        LandedMedium,
        LandedBig,
        LineSnap,
        RodCrack,
        Escape,
        CastWhoosh,
        FloatPlop,
        BiteBob,
        BirdCuckoo,
        BirdPigeon,
        BirdBlackbird,
    }

    /// <summary>
    /// Звук кадра. Grain: At — доля интервала кадра 0…1 (момент внутри [now − dt, now]; разброс может чуть вывести за края),
    /// FightFeedback ставит зерно на dspTime кадра + At × dt + задержка ~40 мс. Разовые: At = 0, играются сразу.
    /// </summary>
    internal struct CozyCue
    {
        public CozySound Sound;
        public int Variant;
        public float Volume;
        public float At;
    }

    /// <summary>Поля FightFrame, которые читает CozyCues (копия за кадр; CozyFrame.Of — переходник).</summary>
    internal struct CozyFrame
    {
        public float ReelInCmPerSec;
        public float ReelLoad;
        public bool IsDragSlipping;
        public bool IsCrankingIntoSlip;
        public float SlipCmPerSec;
        public float LoadShare;
        public float RodLoadShare;
        public float SlackCm;
        public FishPhase Phase;
        public FishPhase NextPhase;
        public FightEvents Events;
        public bool EndedThisFrame;
        public FightOutcome Outcome;
        public int WeightG;
        public float FishDistanceCm;
        /// <summary>Номер шага модели и доля шага (FightFrame.Tick, FightFrame.Alpha) — часы зёрен.</summary>
        public int Tick;
        public float Alpha;
    }

    /// <summary>
    /// «Уютная ASMR»: что звучит на озере — кандидат на звуковую половину FightCues (вибрация остаётся как есть).
    /// Раз в кадр по State и полям FightFrame решает: громкость и высоту 4 петель, зёрна с точным временем внутри кадра
    /// и разовые звуки. Без движка; всё, что зависит от вываживания, выводится из полей кадра и номеров событий,
    /// поэтому зритель-друг слышит ту же последовательность (те же варианты на тех же сантиметрах).
    ///
    /// | Слой          | Что двигает                                                                                   |
    /// | тики шестерни | 1 тик на C0/16 см хода ручки (16/с при C0); ход = max(ReelIn, C0·(1−ReelLoad)), пока крутят;  |
    /// |               | сантиметры — интеграл по времени модели (Tick, Alpha), не по dt кадра: у зрителя те же тики      |
    /// |               | «тяжесть» тика: 1 + h − l, h = (ReelLoad−0,2)/0,6, l = (ход/C0−0,8)/0,3·(1−h): лёгкий/обычный/тяжёлый |
    /// |               | каждый 8-й — «ток» ручки; громкость 0,55+0,45·ход/C0, выше 14/с — ×√(14/частота)               |
    /// | кровать       | max(√(ход/C0), 0,8·(Slip/70)^0,7) × 0,45; высота 0,86+0,24·ход/C0 (шпуля назад: 0,8+0,35·Slip/90) |
    /// | натуга        | smoothstep((ReelLoad−0,35)/0,55) × 0,60, при пробуксовке ×0,45; высота 1,04 − 0,1·ReelLoad      |
    /// | щелчки фрикц. | 1 на 2 см пробуксовки (∫SlipCmPerSec по времени модели); выше 14/с ×√(14/частота), тёмные с вер. (f−10)/20,     |
    /// |               | «мурк» вместо щелчка с вер. (f−20)/30 (до 0,85); разброс ±4% → ±12%; первые 3 щелчка +2 дБ     |
    /// | леска         | натянута и LoadShare > 0,68: smoothstep((x−0,68)/0,28) × 0,30; высота 1 + 0,2·(x−0,68)/0,32     |
    /// | скрип         | RodLoadShare > 0,78: 1→3,5 скрипа в секунду, интервалы ×0,7–1,4; громкость 0,35→0,8             |
    /// | озеро         | всегда; в вываживании ×0,6; птицы — каждые 8–24 с, вне петли: 11 вариантов (дрозд 6, вяхирь 3,  |
    /// |               | кукушка 2), громкость 0,45–1 × 0,10 — за 2 часа ни одна фраза не становится «знакомой»          |
    /// Отпускание: петли гаснут за 0,18 с (после вываживания 0,5 с), высота при этом замирает — без «пиу».
    /// </summary>
    internal sealed class CozyCues
    {
        // ---------- Уровни (громкость источника до общей) ----------
        public const float BedLevel = 0.45f;
        public const float StrainLevel = 0.6f;
        public const float LineLevel = 0.30f;
        public const float AmbienceLevel = 0.31f;
        private const float AmbienceInFight = 0.6f;
        private const float GearLevel = 0.6f;
        private const float TockLevel = 0.65f;
        private const float DragLevel = 0.52f;
        private const float PurrLevel = 0.45f;
        private const float CreakLevel = 0.45f;
        private const float TeleLevel = 0.5f;
        private const float HooksetLevel = 0.5f;
        private const float SplashLevel = 0.6f;
        private const float LandedLevel = 0.5f;
        private const float SnapLevel = 0.6f;
        private const float CrackLevel = 0.6f;
        private const float EscapeLevel = 0.45f;
        private const float CastLevel = 0.4f;
        private const float PlopLevel = 0.5f;
        private const float BobLevel = 0.4f;
        private const float BirdLevel = 0.10f;

        // ---------- Механика ----------
        public const float GearTicksAtC0 = 16f;
        private const int TockEvery = 8;
        private const float DragTickCm = 2.0f;
        private const float DenseTicksPerSec = 14f;
        private const float CreakShare = 0.78f;
        private const float LineShare = 0.68f;
        private const float TightSlackCm = 30f;
        private const int HeavySplashG = 1200;
        private const float LoopReleaseFight = 0.18f;
        private const float LoopReleaseAfter = 0.5f;

        private readonly List<CozyCue> _grains = new List<CozyCue>(32);
        private readonly List<CozyCue> _oneShots = new List<CozyCue>(8);
        private LakeState _state;
        private FishPhase _lastPhase;

        private double _gearCm;
        private long _gearIndex;
        private double _slipCm;
        private long _slipIndex;
        private int _dragRun;
        private float _notSlippingFor;
        private float _creakPhase;
        private float _creakStretch = 1f;
        private int _creakIndex;
        private readonly int[] _last = new int[(int)CozySound.BirdBlackbird + 1];

        private uint _ambientRng = 0xA3B1E5u;
        private float _nextBird = -1f;
        private int _lastBird = -1;

        public IReadOnlyList<CozyCue> Grains => _grains;
        public IReadOnlyList<CozyCue> OneShots => _oneShots;

        public float BedVolume { get; private set; }
        public float BedPitch { get; private set; } = 1f;
        public float StrainVolume { get; private set; }
        public float StrainPitch { get; private set; } = 1f;
        public float LineVolume { get; private set; }
        public float LinePitch { get; private set; } = 1f;
        public float AmbienceVolume { get; private set; } = AmbienceLevel;
        public float LoopReleaseSeconds { get; private set; } = LoopReleaseAfter;
        public bool CutLoops { get; private set; }

        /// <summary>Птицы озера (выключить — например, в меню или для отдельной записи звука).</summary>
        public bool BirdsOn { get; set; } = true;

        public CozyCues()
        {
            for (int i = 0; i < _last.Length; i++) _last[i] = -1;
        }

        /// <param name="c0">Скорость катушки C0, см/с (Tackle.ReelFightSpeedCmPerSec; 0 — 100).</param>
        /// <param name="floatDistanceM">Расстояние до поплавка (заброс, поклёвка).</param>
        /// <param name="now">Время, с (только для птиц — им совпадать у зрителя не нужно).</param>
        public void Update(LakeState state, in CozyFrame f, float c0, float floatDistanceM, float now, float dt)
        {
            _grains.Clear();
            _oneShots.Clear();
            CutLoops = false;
            if (c0 <= 0f) c0 = 100f;

            if (state != _state)
            {
                var from = _state;
                _state = state;
                StateChanged(from, state, f, floatDistanceM);
            }

            bool fight = state == LakeState.Fight;
            if (fight || f.EndedThisFrame)
                ReadEvents(f);

            float handle = 0f;
            bool cranking = fight && (f.ReelInCmPerSec > 0.5f || f.IsCrankingIntoSlip);
            if (cranking)
                handle = Math.Max(f.ReelInCmPerSec, c0 * (1f - f.ReelLoad));
            if (fight)
                Streams(f, handle, c0);
            else
                _clock = false;

            Loops(f, fight, cranking, handle, c0);
            if (BirdsOn)
                Birds(state, now);
        }

        // ---------- Смены состояния ----------

        private void StateChanged(LakeState from, LakeState to, in CozyFrame f, float floatDistanceM)
        {
            switch (to)
            {
                case LakeState.Casting:
                    Shot(CozySound.CastWhoosh, CastLevel);
                    break;
                case LakeState.Waiting when from == LakeState.Casting:
                    Shot(CozySound.FloatPlop, PlopLevel * DistanceFactor(floatDistanceM));
                    break;
                case LakeState.Bite:
                    Shot(CozySound.BiteBob, BobLevel * DistanceFactor(floatDistanceM));
                    break;
                case LakeState.Fight:
                    // Новое вываживание — счётчики событий с нуля: у зрителя, подключившегося к этому бою, те же номера и варианты.
                    _lastPhase = f.Phase;
                    _dragRun = 0;
                    _creakPhase = 0f;
                    _creakStretch = 1f;
                    _gearCm = 0; _gearIndex = 0; _slipCm = 0; _slipIndex = 0; _creakIndex = 0;
                    _clock = false;
                    for (int i = 0; i <= (int)CozySound.RodCreak; i++) _last[i] = -1;
                    Shot(CozySound.Hookset, HooksetLevel);
                    break;
            }
        }

        // ---------- События ----------

        private void ReadEvents(in CozyFrame f)
        {
            var e = f.Events;
            if (Has(e, FightEvents.TelegraphStart) && !f.EndedThisFrame)
            {
                switch (f.NextPhase)
                {
                    case FishPhase.Run: Shot(CozySound.TeleRun, TeleLevel); break;
                    case FishPhase.Dive: Shot(CozySound.TeleDive, TeleLevel); break;
                    case FishPhase.Surge: Shot(CozySound.TeleSurge, TeleLevel); break;
                    case FishPhase.Jump: Shot(CozySound.TeleJump, TeleLevel); break;
                    case FishPhase.Jerk: Shot(CozySound.TeleJerk, TeleLevel); break;
                }
            }
            if (Has(e, FightEvents.PhaseStart))
            {
                if (_lastPhase == FishPhase.Jump && f.Phase != FishPhase.Jump)
                    Splash(f, CozySound.SplashBig, 0.85f);
                switch (f.Phase)
                {
                    case FishPhase.Run: Splash(f, null, 0.55f); break;
                    case FishPhase.Jerk: Splash(f, null, 0.5f); break;
                    case FishPhase.Dive: Splash(f, null, 0.35f); break;
                }
            }
            if (Has(e, FightEvents.JumpApex))
                Splash(f, CozySound.SplashJump, 1f);
            if (Has(e, FightEvents.SurgeStart))
                Splash(f, null, 0.9f);
            if (Has(e, FightEvents.Breakaway))
            {
                // Трогание фрикциона слышно сразу — подчёркнутый щелчок, серия начинается заново.
                _dragRun = 0;
                DragGrain(0f, 0f, Hash((uint)_slipIndex * 0x2545F491u ^ 0xB4EAu));
                // Следующий щелчок — через полные 2 см, а не сразу за этим (иначе два щелчка слипаются в один громкий).
                _slipCm = _slipIndex * DragTickCm;
            }

            if (f.EndedThisFrame)
            {
                switch (f.Outcome)
                {
                    case FightOutcome.Landed:
                        var tier = f.WeightG < 500 ? CozySound.LandedSmall : f.WeightG < 1500 ? CozySound.LandedMedium : CozySound.LandedBig;
                        Shot(tier, LandedLevel);
                        break;
                    case FightOutcome.LineBroke:
                        CutLoops = true;
                        Shot(CozySound.LineSnap, SnapLevel);
                        break;
                    case FightOutcome.SpoolEmpty:
                        CutLoops = true;
                        Shot(CozySound.LineSnap, SnapLevel * 0.7f);
                        break;
                    case FightOutcome.RodBroke:
                        CutLoops = true;
                        Shot(CozySound.RodCrack, CrackLevel);
                        break;
                    case FightOutcome.Escaped:
                        Shot(CozySound.Escape, EscapeLevel);
                        break;
                }
            }
            _lastPhase = f.Phase;
        }

        // ---------- Зёрна: всё считается во времени модели ----------
        //
        // Плавное время модели τ = (Tick − 1 + Alpha) × шаг (так FightFrame интерполирует плавные поля). Дискретные скорости
        // шага k (ReelIn, ReelLoad, SlipCmPerSec, RodLoadShare) действуют на отрезке ((k−1)·шаг, k·шаг]. Пройденные сантиметры —
        // интеграл кусочно-постоянной скорости по τ, поэтому k-й тик приходится на один и тот же τ у игрока и у зрителя при
        // любой частоте кадров (пока в кадре не больше одного шага модели, то есть от 20 к/с). Момент события внутри кадра:
        // At = (τ_события − τ_начала кадра) / (τ_конца − τ_начала).

        /// <summary>Шаг модели, с (FightSettings.TickSeconds).</summary>
        private const double StepSeconds = 0.05;

        private bool _clock;
        private int _clockTick;
        private double _tau;
        private float _gearPrev, _gearCur, _slipPrev, _slipCur, _creakPrev, _creakCur;

        private void Streams(in CozyFrame f, float handle, float c0)
        {
            float slip = f.IsDragSlipping ? f.SlipCmPerSec : 0f;
            float creak = f.RodLoadShare > CreakShare ? 1f + 2.5f * Clamp01((f.RodLoadShare - CreakShare) / (1f - CreakShare)) : 0f;
            double tau1 = (f.Tick - 1 + f.Alpha) * StepSeconds;
            if (!_clock)
            {
                _clock = true;
                _tau = tau1;
                _clockTick = f.Tick;
                _gearCur = handle; _slipCur = slip; _creakCur = creak;
                return;
            }
            double boundary = double.NegativeInfinity;
            if (f.Tick != _clockTick)
            {
                _gearPrev = _gearCur; _slipPrev = _slipCur; _creakPrev = _creakCur;
                boundary = (f.Tick - 1) * StepSeconds;
                _clockTick = f.Tick;
            }
            _gearCur = handle; _slipCur = slip; _creakCur = creak;

            double t0 = _tau;
            if (tau1 <= t0)
                return;
            _tau = tau1;
            double span = tau1 - t0;
            double mid = Math.Min(Math.Max(boundary, t0), tau1);
            if (mid > t0)
                Segment(f, c0, t0, mid, _gearPrev, _slipPrev, _creakPrev, t0, span);
            Segment(f, c0, mid, tau1, _gearCur, _slipCur, _creakCur, t0, span);
        }

        private void Segment(in CozyFrame f, float c0, double a, double b, float gear, float slip, float creak, double t0, double span)
        {
            Gear(f, c0, a, b, gear, t0, span);
            Drag(a, b, slip, t0, span);
            Creak(f, a, b, creak, t0, span);
        }

        /// <summary>Тики шестерни: событие на каждые C0/16 см хода ручки, разброс ±4% периода.</summary>
        private void Gear(in CozyFrame f, float c0, double a, double b, float handle, double t0, double span)
        {
            if (handle <= 0f)
                return;
            double spacing = c0 / GearTicksAtC0;
            double d0 = _gearCm, d1 = d0 + handle * (b - a);
            _gearCm = d1;
            float rate = (float)(handle / spacing);
            float speed = handle / c0;
            float h = Clamp01((f.ReelLoad - 0.2f) / 0.6f);
            float l = Clamp01((speed - 0.8f) / 0.3f) * (1f - h);
            float tierCont = 1f + h - l;
            float density = rate > DenseTicksPerSec ? (float)Math.Sqrt(DenseTicksPerSec / rate) : 1f;
            float baseGain = (0.55f + 0.45f * Math.Min(1f, speed)) * density;

            while (_gearIndex + 1 <= d1 / spacing)
            {
                _gearIndex++;
                uint hsh = Hash((uint)_gearIndex * 0x9E3779B1u ^ 0x6EA1u);
                double tau = a + (_gearIndex * spacing - d0) / handle + 0.04 * Sym(hsh, 0) / rate;
                float at = (float)((tau - t0) / span);
                float gain = baseGain * Db(1.5f * Sym(hsh, 1));
                if (_gearIndex % TockEvery == TockEvery / 2)
                {
                    Grain(CozySound.HandleTock, CozySynth.TockVariants, hsh, TockLevel * gain, at);
                    continue;
                }
                int tier = (int)tierCont;
                if (Unit(hsh, 2) < tierCont - tier) tier++;
                tier = Math.Min(2, tier);
                var sound = tier == 0 ? CozySound.GearLight : tier == 2 ? CozySound.GearHeavy : CozySound.Gear;
                float tierGain = tier == 0 ? Db(-1f) : tier == 2 ? Db(1f) : 1f;
                Grain(sound, CozySynth.GearVariants, hsh, GearLevel * gain * tierGain, at);
            }
        }

        /// <summary>Щелчки фрикциона: на каждые 2 см пробуксовки, разброс ±4% периода, на быстрой — до ±12%.</summary>
        private void Drag(double a, double b, float slip, double t0, double span)
        {
            if (slip <= 0f)
            {
                _notSlippingFor += (float)(b - a);
                if (_notSlippingFor > 0.2f) _dragRun = 0;
                return;
            }
            _notSlippingFor = 0f;
            double s0 = _slipCm, s1 = s0 + slip * (b - a);
            _slipCm = s1;
            float rate = Math.Max(1f, slip / DragTickCm);
            float spread = 0.04f + 0.08f * Clamp01((rate - DenseTicksPerSec) / 30f);
            while (_slipIndex + 1 <= s1 / DragTickCm)
            {
                _slipIndex++;
                uint hsh = Hash((uint)_slipIndex * 0x2545F491u ^ 0xD4A5u);
                double tau = a + (_slipIndex * DragTickCm - s0) / slip + spread * Sym(hsh, 0) / rate;
                DragGrain((float)((tau - t0) / span), rate, hsh);
            }
        }

        private void DragGrain(float at, float rate, uint hsh)
        {
            float density = rate > DenseTicksPerSec ? (float)Math.Sqrt(DenseTicksPerSec / rate) : 1f;
            float accent = _dragRun < 3 ? Db(2f) : 1f;
            _dragRun++;
            float gain = density * accent * Db(1.5f * Sym(hsh, 1));
            float purr = Math.Min(0.85f, Clamp01((rate - 20f) / 30f));
            float dark = Clamp01((rate - 10f) / 20f);
            if (Unit(hsh, 2) < purr)
                Grain(CozySound.DragPurr, CozySynth.PurrVariants, hsh, PurrLevel * gain, at);
            else if (Unit(hsh, 3) < dark)
                Grain(CozySound.DragTickDark, CozySynth.DragVariants, hsh, DragLevel * gain, at);
            else
                Grain(CozySound.DragTick, CozySynth.DragVariants, hsh, DragLevel * gain, at);
        }

        /// <summary>Скрип удилища выше 78% его предела: 1 → 3,5 в секунду (времени модели), интервалы ×0,7–1,4 по номеру скрипа.</summary>
        private void Creak(in CozyFrame f, double a, double b, float rate, double t0, double span)
        {
            if (rate <= 0f)
            {
                _creakPhase = 0.7f;
                return;
            }
            double phase = _creakPhase + rate * (b - a) / _creakStretch;
            if (phase < 1.0)
            {
                _creakPhase = (float)phase;
                return;
            }
            double tau = a + (1.0 - _creakPhase) * _creakStretch / rate;
            _creakPhase = 0f;
            float k = Clamp01((f.RodLoadShare - CreakShare) / (1f - CreakShare));
            uint hsh = Hash((uint)++_creakIndex * 0x68E31DA4u ^ 0xC4EAu);
            _creakStretch = 0.7f + 0.7f * Unit(hsh, 4);
            Grain(CozySound.RodCreak, CozySynth.CreakVariants, hsh, CreakLevel * (0.35f + 0.45f * k) * Db(1.5f * Sym(hsh, 1)), (float)((tau - t0) / span));
        }

        // ---------- Петли ----------

        private void Loops(in CozyFrame f, bool fight, bool cranking, float handle, float c0)
        {
            LoopReleaseSeconds = fight ? LoopReleaseFight : LoopReleaseAfter;
            AmbienceVolume = AmbienceLevel * (fight ? AmbienceInFight : 1f);
            if (!fight)
            {
                BedVolume = 0f;
                StrainVolume = 0f;
                LineVolume = 0f;
                return;
            }

            float speed = Math.Min(1.2f, handle / c0);
            float crank = cranking ? Math.Min(1f, (float)Math.Sqrt(speed)) : 0f;
            float slip = f.IsDragSlipping ? 0.8f * (float)Math.Pow(Clamp01(f.SlipCmPerSec / 70f), 0.7) : 0f;
            BedVolume = BedLevel * Math.Max(crank, slip);
            if (crank + slip > 0f)
            {
                float w = crank / (crank + slip);
                BedPitch = w * (0.86f + 0.24f * speed) + (1f - w) * (0.8f + 0.35f * Clamp01(f.SlipCmPerSec / 90f));
            }

            float strain = Smooth((f.ReelLoad - 0.35f) / 0.55f);
            StrainVolume = cranking ? StrainLevel * strain * (f.IsDragSlipping ? 0.45f : 1f) : 0f;
            if (StrainVolume > 0f)
                StrainPitch = 1.04f - 0.1f * f.ReelLoad;

            bool tight = f.SlackCm <= TightSlackCm;
            float x = f.LoadShare;
            LineVolume = tight ? LineLevel * Smooth((x - LineShare) / 0.28f) : 0f;
            if (LineVolume > 0f)
                LinePitch = 1f + 0.2f * Clamp01((x - LineShare) / 0.32f);
        }

        // ---------- Птицы ----------

        /// <summary>Дальние птицы на разовом источнике: каждые 8–24 с, вид — случайный, но не тот же подряд; в вываживании тише.</summary>
        private void Birds(LakeState state, float now)
        {
            if (_nextBird < 0f)
                _nextBird = now + 3f + 5f * NextAmbient();
            if (now < _nextBird)
                return;
            float u = NextAmbient();
            _nextBird = now + 8f + 16f * (float)Math.Pow(NextAmbient(), 1.3);
            // Вид: дрозд 50%, вяхирь 30%, кукушка 20%, но не тот же вид подряд; вариант — не тот же, что в прошлый раз у этого вида.
            float pick = NextAmbient();
            int kind = pick < 0.5f ? 2 : pick < 0.8f ? 1 : 0;
            if (kind == _lastBird) kind = (kind + 1 + (int)(NextAmbient() * 2)) % 3;
            _lastBird = kind;
            var sound = kind == 0 ? CozySound.BirdCuckoo : kind == 1 ? CozySound.BirdPigeon : CozySound.BirdBlackbird;
            int variants = CozySynth.BirdVariants(kind);
            int v = (int)(NextAmbient() * variants) % variants;
            if (v == _last[(int)sound]) v = (v + 1) % variants;
            _last[(int)sound] = v;
            float duck = state == LakeState.Fight ? AmbienceInFight : 1f;
            _oneShots.Add(new CozyCue { Sound = sound, Variant = v, Volume = BirdLevel * (0.45f + 0.55f * u) * duck, At = 0f });
        }

        private float NextAmbient()
        {
            _ambientRng ^= _ambientRng << 13;
            _ambientRng ^= _ambientRng >> 17;
            _ambientRng ^= _ambientRng << 5;
            return (_ambientRng >> 8) * (1f / 16777216f);
        }

        // ---------- Кирпичики ----------

        private void Splash(in CozyFrame f, CozySound? sound, float strength)
        {
            float kg = f.WeightG / 1000f;
            float weight = Math.Max(0.3f, Math.Min(1f, 0.3f + 0.7f * (float)Math.Sqrt(kg / 3f)));
            var s = sound ?? (f.WeightG >= HeavySplashG ? CozySound.SplashBig : CozySound.SplashSmall);
            Shot(s, SplashLevel * strength * weight * DistanceFactor(f.FishDistanceCm / 100f));
        }

        /// <summary>Тише с расстоянием: до 3 м — полная громкость, на 15 м — половина, на 30 м — треть.</summary>
        private static float DistanceFactor(float meters) => 1f / (1f + Math.Max(0f, meters - 3f) / 12f);

        private void Shot(CozySound sound, float volume)
        {
            if (volume > 0f)
                _oneShots.Add(new CozyCue { Sound = sound, Variant = 0, Volume = Math.Min(volume, 1f), At = 0f });
        }

        /// <summary>Зерно: вариант по хэшу номера события, но никогда не тот же, что в прошлый раз (случайно-исключающий выбор).</summary>
        private void Grain(CozySound sound, int variants, uint hsh, float volume, float at)
        {
            int v = (int)((hsh >> 7) % (uint)variants);
            int last = _last[(int)sound];
            if (v == last)
                v = (v + 1 + (int)((hsh >> 19) % (uint)(variants - 1))) % variants;
            _last[(int)sound] = v;
            _grains.Add(new CozyCue { Sound = sound, Variant = v, Volume = Math.Min(volume, 1f), At = at });
        }

        private static uint Hash(uint x) => CozySynth.Hash(x);

        /// <summary>−1…1 из хэша события (salt — независимые «кубики» одного события).</summary>
        private static float Sym(uint h, int salt) => Unit(h, salt) * 2f - 1f;

        private static float Unit(uint h, int salt) => (CozySynth.Hash(h + (uint)salt * 0x85EBCA6Bu) >> 8) * (1f / 16777216f);

        private static float Db(float db) => (float)Math.Pow(10, db / 20);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        private static float Smooth(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static bool Has(FightEvents events, FightEvents flag) => (events & flag) != 0;
    }
}
