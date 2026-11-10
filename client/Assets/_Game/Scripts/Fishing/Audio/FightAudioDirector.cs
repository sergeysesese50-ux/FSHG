using System;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Всё, что звук читает из FightFrame за кадр (одинаково у игрока и у зрителя). Заполняет FightAudioInput.Of
    /// в Fishing.Unity; смысл полей — в FightFrame.cs.
    /// </summary>
    public struct FightSoundInput
    {
        public int Ticks;
        public float ReelInCmPerSec;
        public float ReelLoad;
        public bool IsDragSlipping;
        public bool IsCrankingIntoSlip;
        public float SlipCmPerSec;
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

        /// <summary>Самый сильный рывок кадра к пределу лески: PeakTensionMaxThisFrameG / WeakestLinkG (0 — шагов не было).
        /// Обрывы случаются на ударах (Shock) — по нему звучит «дзинь» лески.</summary>
        public float PeakShare;
    }

    /// <summary>
    /// Что звучит на озере (главный поток, раз в кадр после шагов модели): по состоянию и FightFrame собирает снимок
    /// для FightAudioMixer (скорости и нагрузки для зёрен, громкость и высота петель, фон, ночь, общая громкость)
    /// и события кадра (разовые звуки, подсечка, обрыв, трогание фрикциона). Сам ничего не играет; без движка.
    /// Вибрация — отдельно (FightCues в Fishing.Unity), здесь её нет.
    ///
    /// | Что               | Когда                                                     | Громкость                         |
    /// | ротор, «ток»      | скорость ротора: ReelIn, а в «Подмотку» при пробуксовке — C0·(1 − ReelLoad) | — (FightGrainScheduler) |
    /// | укладка лески     | крутят                                                    | 0,36·max(s^0,8; 0,35·нагрузка), сход лески при пробуксовке 0,22·(slip/60)^0,7 |
    /// | гул лески         | натянута и LoadShare > 0,6: k = (x − 0,6)/0,4             | HumLevel·k^HumCurve, высота 1 + 0,4(x − 0,6) |
    /// | «дзинь» лески     | удар (Shock), рывок кадра к пределу PeakShare > 0,8       | 0,55→1 × OneShotMax (к PeakShare 1) |
    /// | заброс, плюх, поклёвка | смена состояния (дальше 12 м — «далёкий» вариант)     | 0,45 / 0,5 / 0,55 × даль            |
    /// | подсечка          | переход в Fight (сразу, не ждёт первого шага)            | OneShotMax                        |
    /// | телеграфы         | TelegraphStart по NextPhase                              | вода 0,6·(0,6+0,4·даль), рывок 0,6 |
    /// | всплески          | начало фазы, вершина свечки, бросок (по весу и дали)     | 0,45–0,9 × вес (≥ 0,65) × даль (≥ 0,7) |
    /// | поимка            | ступень RewardTier: 1 мелочь, 2 обычная, 3 трофей         | 0,5                               |
    /// | обрыв / поломка   | итог + Cut (зёрна и петли катушки замолкают сразу)        | OneShotMax (кончилась леска 0,3)  |
    /// | сход              | итог Escaped — только «вниз», без награды                 | 0,55                              |
    /// | фон озера         | всегда; в вываживании приглушён на 5 дБ                  | 0,16 (в бою 0,09 — тише катушки)        |
    /// s = скорость ротора / C0. При отпускании «Подмотки» высота укладки не «ныряет» — остаётся прежней.
    ///
    /// Разовые звуки не громче OneShotMax: при общей громкости по умолчанию пик клипа (0,9) встаёт ровно под порог
    /// лимитера (−1 дБFS), поэтому лимитер их не давит и щелчок обрыва остаётся щелчком. Выделяться над катушкой
    /// им помогает не уровень, а приглушение катушки на время звука (FightAudioMixer, «сайдчейн»).
    /// Вход проверяется: NaN и бесконечности → 0 (слабина — «провисла»), скорость подмотки не выше 3·C0,
    /// пробуксовка не выше 2000 см/с, доли 0…2.
    /// Без выделений памяти: события — в массиве фиксированной длины.
    /// </summary>
    public sealed class FightAudioDirector
    {
        public const int MaxEventsPerFrame = 16;

        /// <summary>
        /// Предел громкости разового звука: при общей громкости FightAudioMixer.DefaultMaster пик клипа
        /// (FightSoundBank.OneShotPeak) после Makeup — ровно порог лимитера SoftLimiter.Ceiling (≈ 0,39).
        /// </summary>
        public const float OneShotMax = SoftLimiter.Ceiling / (FightSoundBank.OneShotPeak * FightAudioMixer.Makeup * FightAudioMixer.DefaultMaster);

        // ---------- Петли катушки ----------
        public const float LayLevel = 0.13f;
        private const float PeelLevel = 0.22f;
        private const float HumStart = 0.6f;
        private const float HumLevel = 0.75f;
        private const double HumCurve = 1.4;
        private const float TightSlackCm = 30f;
        private const float LoopReleaseFight = 0.15f;
        private const float LoopFadeOut = 0.4f;

        // ---------- Разовые ----------
        private const float CastLevel = 0.45f;
        private const float PlopLevel = 0.5f;
        private const float BiteLevel = 0.55f;
        private const float HooksetLevel = OneShotMax;
        private const float TeleJerkLevel = 0.6f;
        private const float TeleWaterLevel = 0.6f;
        private const float LandedLevel = 0.5f;
        private const float SnapLevel = OneShotMax;
        private const float SpoolSnapLevel = 0.3f;
        private const float CrackLevel = OneShotMax;
        private const float EscapeLevel = 0.55f;
        private const float ZingShare = 0.8f;
        private const float FarMeters = 12f;
        private const int HeavySplashG = 1200;
        private const float SplashWeightFloor = 0.65f;
        private const float SplashDistanceFloor = 0.7f;

        // ---------- Пределы входа ----------
        private const float DefaultC0 = 110f;
        private const float MaxSlipCmPerSec = 2000f;
        private const float MaxShare = 2f;

        // ---------- Фон ----------
        /// <summary>Доля фона озера 0…1: на турбазе он уходит в ноль, там звучит свой фон (CampSynth). Микшер сглаживает за 0,6 с.</summary>
        public float AmbienceScale { get; set; } = 1f;

        /// <summary>Сила ветра 0…1 (скорость / 8 м/с) — от неё зависит слой ветра в фоне озера.</summary>
        public float Wind { get; set; }

        private const float AmbienceLevel = 0.16f;
        private const float FightDuck = 0.56f;

        private readonly FightAudioEvent[] _events = new FightAudioEvent[MaxEventsPerFrame];
        private int _eventCount;
        private FightAudioParams _params;
        private LakeSoundState _state;
        private FishPhase _lastPhase;
        private long _eventIndex;
        private readonly int[] _lastVariant = { -1, -1, -1, -1 };

        /// <summary>
        /// Ступень награды за поимку: 1 — мельче типичной, 2 — от типичной, 3 — трофей (или рекорд). Ставит вызывающий
        /// на подсечке — у зрителя то же.
        /// </summary>
        public int RewardTier { get; set; } = 2;

        /// <summary>Снимок этого кадра для FightAudioMixer.Publish.</summary>
        public FightAudioParams Params => _params;

        /// <summary>Событий в этом кадре (отправлять по порядку: Cut раньше звука обрыва).</summary>
        public int EventCount => _eventCount;

        public FightAudioEvent Event(int index) => _events[index];

        /// <summary>
        /// Один кадр. c0 — Tackle.ReelFightSpeedCmPerSec; floatDistanceM — до поплавка (заброс, поклёвка); night — 0…1
        /// (LakeClock.Night); master — общая громкость (0 — выключен); outputRate — AudioSettings.outputSampleRate.
        /// </summary>
        public void Update(LakeSoundState state, in FightSoundInput input, float c0, float floatDistanceM, float night, float master, int outputRate)
        {
            _eventCount = 0;
            if (!(c0 > 0f && c0 <= FightAudioParams.MaxSpeedCmPerSec))
                c0 = DefaultC0;
            floatDistanceM = SafeMath.Clamp(floatDistanceM, 0f, 1e4f);
            var f = Clean(input, c0);

            if (state != _state)
            {
                var from = _state;
                _state = state;
                StateChanged(from, state, floatDistanceM);
            }

            bool fight = state == LakeSoundState.Fight;
            if (fight || f.EndedThisFrame)
            {
                Events(f);
                if (f.EndedThisFrame)
                    Ended(f);
                _lastPhase = f.Phase;
            }

            bool live = fight && !f.EndedThisFrame;
            var p = new FightAudioParams
            {
                OutputRate = outputRate,
                Master = SafeMath.Clamp(master, 0f, 1f),
                Fight = live,
                C0 = c0,
                Night = SafeMath.Clamp(night, 0f, 1f),
                Wind = SafeMath.Clamp(Wind, 0f, 1f),
                LoopReleaseSeconds = fight ? LoopReleaseFight : LoopFadeOut,
                AmbienceVolume = AmbienceLevel * (fight ? FightDuck : 1f) * SafeMath.Clamp(AmbienceScale, 0f, 1f),
                LayPitch = _params.LayPitch > 0 ? _params.LayPitch : 1f,
                HumPitch = _params.HumPitch > 0 ? _params.HumPitch : 1f,
            };
            if (live)
            {
                double v = RotorSpeed(f, c0);
                p.RotorCmPerSec = (float)v;
                p.ReelLoad = f.ReelLoad;
                p.Slipping = f.IsDragSlipping;
                p.SlipCmPerSec = f.IsDragSlipping ? f.SlipCmPerSec : 0f;
                p.LoadShare = f.LoadShare;
                p.Tight = f.SlackCm <= TightSlackCm;
                p.RodLoadShare = f.RodLoadShare;
                Loops(f, v, c0, ref p);
                if (Has(f.Events, FightEvents.Breakaway) && f.IsDragSlipping)
                    Add(new FightAudioEvent { Kind = FightAudioEventKind.DragBreakaway });
            }
            _params = p;
        }

        /// <summary>
        /// Вход без NaN и бесконечностей: скорость подмотки 0…3·C0, пробуксовка 0…2000 см/с, нагрузка 0…1, доли 0…2,
        /// слабина NaN → «провисла», дистанция NaN → 0, вес ≥ 0.
        /// </summary>
        private static FightSoundInput Clean(in FightSoundInput input, float c0)
        {
            var f = input;
            f.ReelInCmPerSec = SafeMath.Clamp(f.ReelInCmPerSec, 0f, 3f * c0);
            f.ReelLoad = SafeMath.Clamp(f.ReelLoad, 0f, 1f);
            f.SlipCmPerSec = SafeMath.Clamp(f.SlipCmPerSec, 0f, MaxSlipCmPerSec);
            f.LoadShare = SafeMath.Clamp(f.LoadShare, 0f, MaxShare);
            f.RodLoadShare = SafeMath.Clamp(f.RodLoadShare, 0f, MaxShare);
            f.PeakShare = SafeMath.Clamp(f.PeakShare, 0f, MaxShare);
            f.SlackCm = float.IsNaN(f.SlackCm) ? 1e6f : SafeMath.Clamp(f.SlackCm, 0f, 1e6f);
            f.FishDistanceCm = SafeMath.Clamp(f.FishDistanceCm, 0f, 1e6f);
            f.WeightG = Math.Max(0, f.WeightG);
            return f;
        }

        // ---------- Смены состояния ----------

        private void StateChanged(LakeSoundState from, LakeSoundState to, float floatDistanceM)
        {
            switch (to)
            {
                case LakeSoundState.Casting:
                    Play(FightOneShot.Cast, 0, false, CastLevel);
                    break;
                case LakeSoundState.Waiting when from == LakeSoundState.Casting:
                    Play(FightOneShot.FloatPlop, 0, floatDistanceM > FarMeters, PlopLevel * DistanceFactor(floatDistanceM));
                    break;
                case LakeSoundState.Bite:
                    Play(FightOneShot.BiteBloop, 0, floatDistanceM > FarMeters, BiteLevel * DistanceFactor(floatDistanceM));
                    break;
                case LakeSoundState.Fight:
                    _eventIndex = 0;
                    _lastPhase = FishPhase.Rest;
                    Add(new FightAudioEvent { Kind = FightAudioEventKind.FightStart });
                    Play(FightOneShot.Hookset, 0, false, HooksetLevel);
                    break;
            }
        }

        // ---------- События кадра ----------

        private void Events(in FightSoundInput f)
        {
            var e = f.Events;
            if (Has(e, FightEvents.TelegraphStart) && !f.EndedThisFrame)
            {
                float water = TeleWaterLevel * (0.6f + 0.4f * DistanceFactor(f.FishDistanceCm / 100f));
                switch (f.NextPhase)
                {
                    case FishPhase.Run: Play(FightOneShot.TeleRun, 0, false, water); break;
                    case FishPhase.Dive: Play(FightOneShot.TeleDive, 0, false, water); break;
                    case FishPhase.Surge: Play(FightOneShot.TeleSurge, 0, false, water); break;
                    case FishPhase.Jump: Play(FightOneShot.TeleJump, 0, false, water); break;
                    case FishPhase.Jerk: Play(FightOneShot.TeleJerk, 0, false, TeleJerkLevel); break;
                }
            }
            // Обрывы случаются на ударах, а не на ровной нагрузке: удар близко к пределу — короткий «дзинь» лески.
            if (Has(e, FightEvents.Shock) && !f.EndedThisFrame && f.PeakShare > ZingShare)
            {
                float k = Math.Min(1f, (f.PeakShare - ZingShare) / (1f - ZingShare));
                Play(FightOneShot.LineZing, Variant(f, 3, FightSoundBank.ZingVariants), false, OneShotMax * (0.55f + 0.45f * k));
            }
            if (Has(e, FightEvents.PhaseStart))
            {
                if (_lastPhase == FishPhase.Jump && f.Phase != FishPhase.Jump)
                    Splash(f, FightOneShot.SplashJump, 0.9f);
                switch (f.Phase)
                {
                    case FishPhase.Run: Splash(f, FightOneShot.SplashSmall, 0.7f); break;
                    case FishPhase.Jerk: Splash(f, FightOneShot.SplashSmall, 0.6f); break;
                    case FishPhase.Dive: Splash(f, FightOneShot.SplashSmall, 0.45f); break;
                }
            }
            if (Has(e, FightEvents.JumpApex))
                Splash(f, f.WeightG >= HeavySplashG ? FightOneShot.SplashBig : FightOneShot.SplashSmall, 0.8f);
            if (Has(e, FightEvents.SurgeStart))
                Splash(f, f.WeightG >= HeavySplashG ? FightOneShot.SplashBig : FightOneShot.SplashSmall, 0.85f);
        }

        private void Ended(in FightSoundInput f)
        {
            switch (f.Outcome)
            {
                case FightOutcome.Landed:
                    Play(FightOneShot.Landed, Math.Max(1, Math.Min(3, RewardTier)), false, LandedLevel);
                    break;
                case FightOutcome.LineBroke:
                    Add(new FightAudioEvent { Kind = FightAudioEventKind.Cut });
                    Play(FightOneShot.LineSnap, 0, false, SnapLevel);
                    break;
                case FightOutcome.SpoolEmpty:
                    Add(new FightAudioEvent { Kind = FightAudioEventKind.Cut });
                    Play(FightOneShot.LineSnap, 0, false, SpoolSnapLevel);
                    break;
                case FightOutcome.RodBroke:
                    Add(new FightAudioEvent { Kind = FightAudioEventKind.Cut });
                    Play(FightOneShot.RodCrack, 0, false, CrackLevel);
                    break;
                case FightOutcome.Escaped:
                    Play(FightOneShot.Escape, 0, false, EscapeLevel);
                    break;
            }
        }

        // ---------- Катушка ----------

        /// <summary>Скорость ротора, см лески в секунду: выбирает — ReelIn; крутят в пробуксовку — C0·(1 − ReelLoad).</summary>
        private static double RotorSpeed(in FightSoundInput f, float c0)
        {
            double v = f.ReelInCmPerSec > 0.5f ? f.ReelInCmPerSec : 0;
            if (f.IsCrankingIntoSlip)
                v = Math.Max(v, c0 * (1 - f.ReelLoad));
            return v;
        }

        private void Loops(in FightSoundInput f, double v, float c0, ref FightAudioParams p)
        {
            double s = Math.Min(1.2, v / c0);
            double load = f.ReelLoad;
            // Под нагрузкой ротор почти стоит, но механизм «шуршит» натугой: пол 0,35·нагрузка, чтобы натуга не звучала в тишине.
            double reel = v > 0 ? LayLevel * Math.Max(Math.Pow(s, 0.8), 0.35 * load) : 0;
            double slip = f.IsDragSlipping ? Math.Min(1.5, f.SlipCmPerSec / 60.0) : 0;
            double peel = PeelLevel * Math.Pow(slip, 0.7);
            double vol = Math.Sqrt(reel * reel + peel * peel);
            p.LayVolume = (float)vol;
            if (vol > 1e-4)
            {
                double pr = (0.82 + 0.3 * s) * (1 - 0.12 * load);
                double pp = 0.85 + 0.35 * slip;
                p.LayPitch = (float)((reel * reel * pr + peel * peel * pp) / (vol * vol));
            }

            // Гул лески — предупреждение о пределе: с 0,6 доли (как «пение» лески до палитры), у предела — громче укладки.
            bool tight = f.SlackCm <= TightSlackCm;
            double x = f.LoadShare;
            double k = Math.Max(0, Math.Min(1, (x - HumStart) / (1 - HumStart)));
            p.HumVolume = tight ? (float)(HumLevel * Math.Pow(k, HumCurve)) : 0f;
            if (p.HumVolume > 0)
                p.HumPitch = (float)(1 + 0.4 * Math.Max(0, Math.Min(0.7, x - HumStart)));
        }

        // ---------- Кирпичики ----------

        private void Splash(in FightSoundInput f, FightOneShot sound, float strength)
        {
            float kg = f.WeightG / 1000f;
            float weight = Math.Max(SplashWeightFloor, Math.Min(1f, 0.3f + 0.7f * (float)Math.Sqrt(kg / 3f)));
            float meters = f.FishDistanceCm / 100f;
            if (sound == FightOneShot.SplashSmall && f.WeightG >= HeavySplashG)
                sound = FightOneShot.SplashBig;
            int slot = sound == FightOneShot.SplashSmall ? 0 : sound == FightOneShot.SplashBig ? 1 : 2;
            int count = sound == FightOneShot.SplashJump ? FightSoundBank.JumpSplashVariants : FightSoundBank.SplashVariants;
            int variant = Variant(f, slot, count);
            Play(sound, variant, meters > FarMeters, strength * weight * Math.Max(SplashDistanceFloor, DistanceFactor(meters)));
        }

        /// <summary>Вариант по хешу тика и номера события — никогда не тот же, что в прошлый раз в этом слоте.</summary>
        private int Variant(in FightSoundInput f, int slot, int count)
        {
            _eventIndex++;
            uint h = SynthKit.Hash((uint)f.Tick * 0x9E3779B9u + (uint)_eventIndex * 31u + (uint)slot);
            int variant = (int)(h % (uint)count);
            if (variant == _lastVariant[slot] && count > 1)
                variant = (variant + 1 + (int)((h >> 8) % (uint)(count - 1))) % count;
            _lastVariant[slot] = variant;
            return variant;
        }

        /// <summary>Тише с расстоянием: до 3 м — полная громкость, на 15 м — половина, на 30 м — треть.</summary>
        private static float DistanceFactor(float meters) => 1f / (1f + Math.Max(0f, meters - 3f) / 12f);

        /// <summary>Разовый звук; громкость не выше OneShotMax (NaN и ≤ 0 — не играть).</summary>
        private void Play(FightOneShot sound, int variant, bool far, float volume)
        {
            if (volume > 0f)
                Add(new FightAudioEvent { Kind = FightAudioEventKind.OneShot, Sound = sound, Variant = variant, Far = far, Volume = Math.Min(volume, OneShotMax) });
        }

        private void Add(in FightAudioEvent e)
        {
            if (_eventCount < _events.Length)
                _events[_eventCount++] = e;
        }

        private static bool Has(FightEvents events, FightEvents flag) => (events & flag) != 0;
    }
}
