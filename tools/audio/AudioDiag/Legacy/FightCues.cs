// Прежний звук вываживания (до «Натуральной» палитры): копия client/Assets/_Game/Scripts/Unity/Lake/FightCues.cs
// на момент замены. В игре его больше нет; здесь — только для AudioDiag и LoopDump (сравнение «было/стало»).
using System.Collections.Generic;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>Разовые звуки из банка FightFeedback.</summary>
    internal enum FightSound
    {
        DragClick,
        RodCreak,
        Splash,
        SplashHeavy,
        TelegraphSwirl,
        JerkThunk,
        JumpWhoosh,
        Hookset,
        LineSnap,
        RodCrack,
        EscapePlip,
        LandedChime,
    }

    /// <summary>Разовый звук этого кадра: что и с какой громкостью (0…1, до общей громкости).</summary>
    internal struct SoundCue
    {
        public FightSound Sound;
        public float Volume;
    }

    /// <summary>
    /// Что звучит и вибрирует на озере (docs/specs/fight-v2.md, §6). Раз в кадр по State и Frame контроллера
    /// решает громкость и высоту петель, какие разовые звуки сыграть и какие импульсы поставить в очередь.
    /// Сам ничего не играет и не вибрирует — это делает FightFeedback; здесь нет движковых объектов,
    /// поэтому логику можно гонять вне Unity.
    ///
    /// Источники (контракт — в FightFrame.cs):
    /// - разовое — только покадровые поля Frame (Events, SlipCmThisFrame, PeakTensionMaxThisFrameG,
    ///   EndedThisFrame) и смены State (поклёвка, подсечка, заброс);
    /// - петли — плавные и дискретные поля, пока State == Fight; вне вываживания их громкость уходит в 0.
    ///
    /// Звук:
    /// | Петля        | Громкость                                     | Высота                         |
    /// | жужжание     | крутят «Подмотку» (выбирает или вхолостую)    | 0,6 + 0,8 × ReelIn / C0         |
    /// | рычание      | (ReelLoad − 0,4) / 0,5; при пробуксовке ×0,3  | 0,9 + 0,2 × ReelLoad            |
    /// | трещотка     | пробуксовка быстрее 25 щелчков/с               | SlipCmPerSec / 60               |
    /// | пение лески  | (x − 0,6) / 0,4, x = Peak / Weakest; леска натянута | 1 + 1,3x                   |
    /// Щелчок — на каждые 1,5 см пробуксовки (медленнее 25 в секунду; выше — петля трещотки, обратно ниже 22).
    /// Скрип — выше 75% нагрузки удилища, раз в 0,6 → 0,2 с. Всплеск — по весу рыбы и расстоянию.
    ///
    /// Вибрация (импульс: мс / сила 1–255):
    /// | Событие                      | Импульс                                                   |
    /// | тик фрикциона                | 8 / 70 на каждые 10 см пробуксовки, не чаще 12 в секунду   |
    /// | телеграф ухода, вглубь, броска, свечки | 8 / 120 дважды с промежутком 60 мс                |
    /// | телеграф рывка               | 15 / 180                                                  |
    /// | удар (Shock)                 | 30 / 100–255 по PeakTensionMaxThisFrameG / WeakestLinkG    |
    /// | опасность выше 85%           | 10 / 110–200 тиками 3 → 10 Гц                             |
    /// | крючок слабеет (HookLoss)    | 20 / 150 не чаще раза в 0,4 с                             |
    /// | подсечка                     | 25 / 255, через 60 мс 15 / 160                             |
    /// | поимка                       | 3 × 20 / 200 через 100 мс                                  |
    /// | обрыв, поломка, кончилась леска | 70 / 255                                               |
    /// | поклёвка (нет в таблице §6)  | 20 / 170 дважды через 110 мс — вместо Handheld.Vibrate     |
    /// «Без вибрации, пока леска натянута и выбирается» (§6) относится к фону — тикам опасности: на натянутой
    /// выбираемой леске их нет. Тики фрикциона там невозможны сами (при пробуксовке леска уходит), а события
    /// (телеграф, удар, подсечка, поимка) — сигналы, которые нельзя пропустить, они идут всегда.
    /// Всё вместе — не больше 15 импульсов в секунду (HapticSchedule).
    /// </summary>
    internal sealed class FightCues
    {
        // ---------- Звук: петли (громкость до общей) ----------
        private const float WhirLevel = 0.28f;
        private const float GrowlLevel = 0.5f;
        private const float GrowlWhileSlipping = 0.3f;
        private const float ClickLoopLevel = 0.38f;
        private const float SingLevel = 0.3f;
        private const float LoopReleaseFightSeconds = 0.12f;
        private const float LoopFadeOutSeconds = 0.3f;

        // ---------- Звук: щелчки, скрип, всплески, сигналы ----------
        private const float ClickSlipCm = 1.5f;
        private const float ClickLoopOnPerSec = 25f;
        private const float ClickLoopOffPerSec = 22f;
        private const float MaxOwedClicks = 2f;
        private const float ClickLevel = 0.3f;
        private const float CreakRodShare = 0.75f;
        private const float CreakSlowSeconds = 0.6f;
        private const float CreakFastSeconds = 0.2f;
        private const float CreakQuietLevel = 0.2f;
        private const float CreakLoudLevel = 0.45f;
        private const int HeavySplashG = 1200;
        private const float CastSplash = 0.35f;
        private const float BiteSplash = 0.25f;
        private const float RunBoilSplash = 0.6f;
        private const float JerkSplash = 0.5f;
        private const float DiveSplash = 0.35f;
        private const float JumpApexSplash = 1f;
        private const float JumpReentrySplash = 0.8f;
        private const float SurgeSplash = 0.9f;
        private const float LandedSplash = 0.6f;
        private const float SwirlLevel = 0.45f;
        private const float JerkThunkLevel = 0.75f;
        private const float WhooshLevel = 0.5f;
        private const float HooksetLevel = 0.85f;
        private const float SnapLevel = 0.9f;
        private const float SpoolSnapLevel = 0.6f;
        private const float CrackLevel = 0.9f;
        private const float PlipLevel = 0.55f;
        private const float ChimeLevel = 0.6f;

        // ---------- Вибрация ----------
        private const float DragTickSlipCm = 10f;
        private const float DragTickMaxPerSec = 12f;
        private const int DragTickMs = 8;
        private const int DragTickAmplitude = 70;
        private const int TelegraphMs = 8;
        private const int TelegraphAmplitude = 120;
        private const float TelegraphGapSeconds = 0.06f;
        private const int JerkMs = 15;
        private const int JerkAmplitude = 180;
        private const int ShockMs = 30;
        private const int ShockMinAmplitude = 100;
        private const int ShockMaxAmplitude = 255;
        private const float DangerShare = 0.85f;
        private const float DangerSlowHz = 3f;
        private const float DangerFastHz = 10f;
        private const int DangerMs = 10;
        private const int DangerMinAmplitude = 110;
        private const int DangerMaxAmplitude = 200;
        private const float HookLossEverySeconds = 0.4f;
        private const int HookLossMs = 20;
        private const int HookLossAmplitude = 150;
        private const int HooksetMs = 25;
        private const int HooksetAmplitude = 255;
        private const int HooksetTailMs = 15;
        private const int HooksetTailAmplitude = 160;
        private const float HooksetGapSeconds = 0.06f;
        private const int LandedMs = 20;
        private const int LandedAmplitude = 200;
        private const float LandedEverySeconds = 0.1f;
        private const int BreakMs = 70;
        private const int BreakAmplitude = 255;
        private const int BiteMs = 20;
        private const int BiteAmplitude = 170;
        private const float BiteEverySeconds = 0.11f;

        // ---------- Общее ----------
        private const float TightSlackCm = 30f;
        private const float GainingCmPerSec = 3f;

        private readonly List<SoundCue> _sounds = new List<SoundCue>(8);
        private LakeState _state;
        private FishPhase _lastPhase;
        private bool _clickLoop;
        private float _clickOwed;
        private float _tickSlipCm;
        private float _lastDragTick = float.NegativeInfinity;
        private float _lastHookLossPulse = float.NegativeInfinity;
        private bool _danger;
        private float _dangerPhase;
        private float _creakTimer;

        /// <summary>Разовые звуки этого кадра (очищаются в начале Update).</summary>
        public IReadOnlyList<SoundCue> Sounds => _sounds;

        /// <summary>Очередь импульсов вибрации: FightFeedback забирает из неё каждый кадр.</summary>
        public HapticSchedule Pulses { get; } = new HapticSchedule();

        public float WhirVolume { get; private set; }
        public float WhirPitch { get; private set; } = 1f;
        public float GrowlVolume { get; private set; }
        public float GrowlPitch { get; private set; } = 1f;
        public float ClickLoopVolume { get; private set; }
        public float ClickLoopPitch { get; private set; } = 1f;
        public float SingVolume { get; private set; }
        public float SingPitch { get; private set; } = 1f;

        /// <summary>За сколько петли затухают: в вываживании быстро, после него — плавно.</summary>
        public float LoopReleaseSeconds { get; private set; } = LoopFadeOutSeconds;

        /// <summary>Оборвать петли сразу, без затухания (обрыв, поломка: щелчок обрыва — последнее, что слышно).</summary>
        public bool CutLoops { get; private set; }

        /// <summary>Работает ли сейчас трещотка-петля (пробуксовка быстрее 25 щелчков в секунду).</summary>
        public bool ClickLoopActive => _clickLoop;

        /// <summary>
        /// Один кадр. Вызывать после FishingController.Update (из LateUpdate).
        /// </summary>
        /// <param name="tackle">Снасть вываживания (C0 катушки); может быть null — тогда жужжание по 100 см/с.</param>
        /// <param name="floatDistanceM">Расстояние до поплавка (всплеск заброса и поклёвки).</param>
        /// <param name="now">Часы вибрации, с (те же, что для HapticSchedule.TryTake).</param>
        /// <param name="dt">Время кадра, с.</param>
        public void Update(LakeState state, FightFrame frame, FightTackle tackle, float floatDistanceM, float now, float dt)
        {
            _sounds.Clear();
            CutLoops = false;

            if (state != _state)
            {
                var from = _state;
                _state = state;
                StateChanged(from, state, frame, floatDistanceM, now);
            }

            bool fight = state == LakeState.Fight;
            if (fight || frame.EndedThisFrame)
                ReadFrame(frame, now);

            _creakTimer = Mathf.Max(0f, _creakTimer - dt);
            if (fight)
            {
                Danger(frame, now, dt);
                Creak(frame);
            }
            else
            {
                _danger = false;
            }

            Loops(frame, tackle, fight);
        }

        // ---------- Смены состояния ----------

        private void StateChanged(LakeState from, LakeState to, FightFrame frame, float floatDistanceM, float now)
        {
            switch (to)
            {
                case LakeState.Waiting when from == LakeState.Casting:
                    Play(FightSound.Splash, CastSplash * DistanceFactor(floatDistanceM));
                    break;
                case LakeState.Bite:
                    Play(FightSound.Splash, BiteSplash * DistanceFactor(floatDistanceM));
                    Pulses.Add(now, BiteMs, BiteAmplitude, true);
                    Pulses.Add(now + BiteEverySeconds, BiteMs, BiteAmplitude, true);
                    break;
                case LakeState.Fight:
                    BeginFight(frame, now);
                    break;
            }
        }

        /// <summary>
        /// Подсечка: звук и вибрация сразу по нажатию, не дожидаясь HookSet первого шага (до 50 мс позже).
        /// </summary>
        private void BeginFight(FightFrame frame, float now)
        {
            _lastPhase = frame.Phase;
            _clickLoop = false;
            _clickOwed = 0f;
            _tickSlipCm = 0f;
            _danger = false;
            _creakTimer = 0f;
            _lastHookLossPulse = float.NegativeInfinity;
            Pulses.Clear();
            Hookset(now);
        }

        private void Hookset(float now)
        {
            Play(FightSound.Hookset, HooksetLevel);
            Pulses.Add(now, HooksetMs, HooksetAmplitude, true);
            Pulses.Add(now + HooksetMs / 1000f + HooksetGapSeconds, HooksetTailMs, HooksetTailAmplitude, true);
        }

        // ---------- События кадра ----------

        private void ReadFrame(FightFrame frame, float now)
        {
            var events = frame.Events;
            // HookSet не нужен: подсечка уже прозвучала при переходе в Fight (BeginFight).
            // Телеграф в кадре конца ни о чём не предупреждает — фазы уже не будет.
            if (Has(events, FightEvents.TelegraphStart) && !frame.EndedThisFrame)
                Telegraph(frame.NextPhase, now);
            if (Has(events, FightEvents.PhaseStart))
                PhaseStarted(frame);
            if (Has(events, FightEvents.JumpApex))
                Splash(frame, JumpApexSplash);
            if (Has(events, FightEvents.SurgeStart))
                Splash(frame, SurgeSplash);
            if (Has(events, FightEvents.Shock))
                Shock(frame, now);
            if (Has(events, FightEvents.HookLoss) && now - _lastHookLossPulse >= HookLossEverySeconds)
            {
                _lastHookLossPulse = now;
                Pulses.Add(now, HookLossMs, HookLossAmplitude, true);
            }

            DragClicks(frame, events);
            DragTicks(frame, now);

            if (frame.EndedThisFrame)
                Ended(frame, now);
            _lastPhase = frame.Phase;
        }

        private void Telegraph(FishPhase next, float now)
        {
            switch (next)
            {
                case FishPhase.Run:
                case FishPhase.Dive:
                case FishPhase.Surge:
                    Play(FightSound.TelegraphSwirl, SwirlLevel);
                    DoubleTick(now);
                    break;
                case FishPhase.Jump:
                    Play(FightSound.JumpWhoosh, WhooshLevel);
                    DoubleTick(now);
                    break;
                case FishPhase.Jerk:
                    Play(FightSound.JerkThunk, JerkThunkLevel);
                    Pulses.Add(now, JerkMs, JerkAmplitude, true);
                    break;
            }
        }

        private void DoubleTick(float now)
        {
            Pulses.Add(now, TelegraphMs, TelegraphAmplitude, true);
            Pulses.Add(now + TelegraphMs / 1000f + TelegraphGapSeconds, TelegraphMs, TelegraphAmplitude, true);
        }

        /// <summary>Всплески начала фаз (§4): «бурун» ухода, рывок, уход вглубь и падение после свечки.</summary>
        private void PhaseStarted(FightFrame frame)
        {
            if (_lastPhase == FishPhase.Jump && frame.Phase != FishPhase.Jump)
                Splash(frame, JumpReentrySplash);
            switch (frame.Phase)
            {
                case FishPhase.Run: Splash(frame, RunBoilSplash); break;
                case FishPhase.Jerk: Splash(frame, JerkSplash); break;
                case FishPhase.Dive: Splash(frame, DiveSplash); break;
            }
        }

        private void Shock(FightFrame frame, float now)
        {
            float share = frame.WeakestLinkG > 0f ? frame.PeakTensionMaxThisFrameG / frame.WeakestLinkG : 0f;
            int amplitude = Mathf.RoundToInt(Mathf.Lerp(ShockMinAmplitude, ShockMaxAmplitude, Mathf.Clamp01(share)));
            Pulses.Add(now, ShockMs, amplitude, true);
        }

        /// <summary>Щелчок на каждые 1,5 см пробуксовки; быстрее 25 в секунду — петля трещотки.</summary>
        private void DragClicks(FightFrame frame, FightEvents events)
        {
            float perSecond = frame.IsDragSlipping ? frame.SlipCmPerSec / ClickSlipCm : 0f;
            if (_clickLoop ? perSecond < ClickLoopOffPerSec : perSecond > ClickLoopOnPerSec)
                _clickLoop = !_clickLoop;

            if (_clickLoop)
            {
                _clickOwed = 0f;
                return;
            }

            if (Has(events, FightEvents.Breakaway))
            {
                // Трогание фрикциона слышно сразу, не через 1,5 см.
                Play(FightSound.DragClick, ClickLevel);
                _clickOwed = 0f;
                return;
            }

            // Не больше щелчка за кадр: при низкой частоте кадров лишние отбрасываются, а не играются пачкой.
            _clickOwed = Mathf.Min(_clickOwed + frame.SlipCmThisFrame / ClickSlipCm, MaxOwedClicks);
            if (_clickOwed >= 1f)
            {
                Play(FightSound.DragClick, ClickLevel);
                _clickOwed -= 1f;
            }
        }

        /// <summary>Тик фрикциона в руку: на каждые 10 см пробуксовки, не чаще 12 в секунду.</summary>
        private void DragTicks(FightFrame frame, float now)
        {
            _tickSlipCm += frame.SlipCmThisFrame;
            if (_tickSlipCm < DragTickSlipCm)
                return;
            _tickSlipCm = Mathf.Min(_tickSlipCm - DragTickSlipCm, DragTickSlipCm);
            if (now - _lastDragTick < 1f / DragTickMaxPerSec)
                return;
            _lastDragTick = now;
            Pulses.Add(now, DragTickMs, DragTickAmplitude, false);
        }

        private void Ended(FightFrame frame, float now)
        {
            switch (frame.Outcome)
            {
                case FightOutcome.Landed:
                    Splash(frame, LandedSplash);
                    Play(FightSound.LandedChime, ChimeLevel);
                    Pulses.Clear();
                    for (int i = 0; i < 3; i++)
                        Pulses.Add(now + i * LandedEverySeconds, LandedMs, LandedAmplitude, true);
                    break;
                case FightOutcome.LineBroke:
                    Break(now, FightSound.LineSnap, SnapLevel);
                    break;
                case FightOutcome.SpoolEmpty:
                    Break(now, FightSound.LineSnap, SpoolSnapLevel);
                    break;
                case FightOutcome.RodBroke:
                    Break(now, FightSound.RodCrack, CrackLevel);
                    break;
                case FightOutcome.Escaped:
                    Play(FightSound.EscapePlip, PlipLevel);
                    break;
            }
        }

        private void Break(float now, FightSound sound, float volume)
        {
            CutLoops = true;
            Play(sound, volume);
            Pulses.Clear();
            Pulses.Add(now, BreakMs, BreakAmplitude, true);
        }

        // ---------- Непрерывное ----------

        /// <summary>
        /// Тики опасности выше 85% предела слабого звена, 3 → 10 Гц. Фон: молчат, пока леска натянута
        /// и выбирается (§6), и уступают событиям в HapticSchedule.
        /// </summary>
        private void Danger(FightFrame frame, float now, float dt)
        {
            bool tight = frame.SlackCm <= TightSlackCm;
            bool gaining = tight && !frame.IsDragSlipping
                && (frame.ReelInCmPerSec > GainingCmPerSec || frame.FishSpeedCmPerSec > GainingCmPerSec);
            float share = frame.LoadShare;
            if (share <= DangerShare || gaining)
            {
                _danger = false;
                return;
            }

            float k = Mathf.Clamp01((share - DangerShare) / (1f - DangerShare));
            if (!_danger)
            {
                _danger = true;
                _dangerPhase = 1f;
            }
            else
            {
                _dangerPhase += Mathf.Lerp(DangerSlowHz, DangerFastHz, k) * dt;
            }

            if (_dangerPhase < 1f)
                return;
            _dangerPhase -= 1f;
            if (_dangerPhase >= 1f)
                _dangerPhase = 0f;
            Pulses.Add(now, DangerMs, Mathf.RoundToInt(Mathf.Lerp(DangerMinAmplitude, DangerMaxAmplitude, k)), false);
        }

        /// <summary>Скрип выше 75% нагрузки удилища: раз в 0,6 с, к пределу — раз в 0,2 с, громче.</summary>
        private void Creak(FightFrame frame)
        {
            if (frame.RodLoadShare <= CreakRodShare || _creakTimer > 0f)
                return;
            float k = Mathf.Clamp01((frame.RodLoadShare - CreakRodShare) / (1f - CreakRodShare));
            Play(FightSound.RodCreak, Mathf.Lerp(CreakQuietLevel, CreakLoudLevel, k));
            _creakTimer = Mathf.Lerp(CreakSlowSeconds, CreakFastSeconds, k);
        }

        private void Loops(FightFrame frame, FightTackle tackle, bool fight)
        {
            LoopReleaseSeconds = fight ? LoopReleaseFightSeconds : LoopFadeOutSeconds;
            if (!fight)
            {
                WhirVolume = 0f;
                GrowlVolume = 0f;
                ClickLoopVolume = 0f;
                SingVolume = 0f;
                return;
            }

            float c0 = tackle != null && tackle.ReelFightSpeedCmPerSec > 0 ? (float)tackle.ReelFightSpeedCmPerSec : 100f;
            float reel = Mathf.Clamp(frame.ReelInCmPerSec / c0, 0f, 1.25f);
            bool cranking = frame.ReelInCmPerSec > 0.5f || frame.IsCrankingIntoSlip;
            WhirVolume = cranking ? WhirLevel * (0.6f + 0.4f * Mathf.Min(reel, 1f)) : 0f;
            WhirPitch = 0.6f + 0.8f * reel;

            // Рычание — «тянет с трудом». При пробуксовке ReelLoad тоже высокий, но там главное — трещотка,
            // поэтому рычание приглушено: три ответа катушки (выбирает / тянет / буксует) слышны раздельно.
            float growl = Mathf.Clamp01((frame.ReelLoad - 0.4f) / 0.5f);
            GrowlVolume = GrowlLevel * growl * (frame.IsDragSlipping ? GrowlWhileSlipping : 1f);
            GrowlPitch = 0.9f + 0.2f * frame.ReelLoad;

            ClickLoopVolume = _clickLoop ? ClickLoopLevel : 0f;
            ClickLoopPitch = Mathf.Clamp(frame.SlipCmPerSec / 60f, 0.25f, 3f);

            float x = frame.LoadShare;
            SingVolume = frame.SlackCm <= TightSlackCm ? SingLevel * Mathf.Clamp01((x - 0.6f) / 0.4f) : 0f;
            SingPitch = 1f + 1.3f * Mathf.Clamp(x, 0f, 1.3f);
        }

        // ---------- Кирпичики ----------

        private void Splash(FightFrame frame, float strength)
        {
            float kg = frame.WeightG / 1000f;
            float weight = Mathf.Clamp(0.3f + 0.7f * Mathf.Sqrt(kg / 3f), 0.3f, 1f);
            float volume = strength * weight * DistanceFactor(frame.FishDistanceCm / 100f);
            Play(frame.WeightG >= HeavySplashG ? FightSound.SplashHeavy : FightSound.Splash, volume);
        }

        /// <summary>Тише с расстоянием: до 3 м — полная громкость, на 15 м — половина, на 30 м — треть.</summary>
        private static float DistanceFactor(float meters) => 1f / (1f + Mathf.Max(0f, meters - 3f) / 12f);

        private void Play(FightSound sound, float volume)
        {
            if (volume > 0f)
                _sounds.Add(new SoundCue { Sound = sound, Volume = Mathf.Min(volume, 1f) });
        }

        private static bool Has(FightEvents events, FightEvents flag) => (events & flag) != 0;
    }
}
