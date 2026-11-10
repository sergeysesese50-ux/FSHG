using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Когда вибрирует телефон на озере (docs/specs/fight-v2.md, §6). Раз в кадр по State и Frame контроллера ставит
    /// импульсы в очередь HapticSchedule. Сам не вибрирует — это делает FightFeedback через Haptics; здесь нет
    /// движковых объектов, поэтому логику можно гонять вне Unity. Звук решает FightAudioDirector (Fishing.Core/Audio).
    ///
    /// Источники (контракт — в FightFrame.cs): только покадровые поля Frame (Events, SlipCmThisFrame,
    /// PeakTensionMaxThisFrameG, EndedThisFrame) и смены State (поклёвка, подсечка); фон — плавные поля, пока State == Fight.
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
        private const float TightSlackCm = 30f;
        private const float GainingCmPerSec = 3f;

        private LakeState _state;
        private float _tickSlipCm;
        private float _lastDragTick = float.NegativeInfinity;
        private float _lastHookLossPulse = float.NegativeInfinity;
        private bool _danger;
        private float _dangerPhase;

        /// <summary>Очередь импульсов вибрации: FightFeedback забирает из неё каждый кадр.</summary>
        public HapticSchedule Pulses { get; } = new HapticSchedule();

        /// <summary>
        /// Один кадр. Вызывать после FishingController.Update (из LateUpdate).
        /// </summary>
        /// <param name="now">Часы вибрации, с (те же, что для HapticSchedule.TryTake).</param>
        /// <param name="dt">Время кадра, с.</param>
        public void Update(LakeState state, FightFrame frame, float now, float dt)
        {
            if (state != _state)
            {
                _state = state;
                StateChanged(state, now);
            }

            bool fight = state == LakeState.Fight;
            if (fight || frame.EndedThisFrame)
                ReadFrame(frame, now);

            if (fight)
                Danger(frame, now, dt);
            else
                _danger = false;
        }

        // ---------- Смены состояния ----------

        private void StateChanged(LakeState to, float now)
        {
            switch (to)
            {
                case LakeState.Bite:
                    Pulses.Add(now, BiteMs, BiteAmplitude, true);
                    Pulses.Add(now + BiteEverySeconds, BiteMs, BiteAmplitude, true);
                    break;
                case LakeState.Fight:
                    BeginFight(now);
                    break;
            }
        }

        /// <summary>
        /// Подсечка: вибрация сразу по нажатию, не дожидаясь HookSet первого шага (до 50 мс позже).
        /// </summary>
        private void BeginFight(float now)
        {
            _tickSlipCm = 0f;
            _danger = false;
            _lastHookLossPulse = float.NegativeInfinity;
            Pulses.Clear();
            Pulses.Add(now, HooksetMs, HooksetAmplitude, true);
            Pulses.Add(now + HooksetMs / 1000f + HooksetGapSeconds, HooksetTailMs, HooksetTailAmplitude, true);
        }

        // ---------- События кадра ----------

        private void ReadFrame(FightFrame frame, float now)
        {
            var events = frame.Events;
            // HookSet не нужен: подсечка уже отработана при переходе в Fight (BeginFight).
            // Телеграф в кадре конца ни о чём не предупреждает — фазы уже не будет.
            if (Has(events, FightEvents.TelegraphStart) && !frame.EndedThisFrame)
                Telegraph(frame.NextPhase, now);
            if (Has(events, FightEvents.Shock))
                Shock(frame, now);
            if (Has(events, FightEvents.HookLoss) && now - _lastHookLossPulse >= HookLossEverySeconds)
            {
                _lastHookLossPulse = now;
                Pulses.Add(now, HookLossMs, HookLossAmplitude, true);
            }

            DragTicks(frame, now);

            if (frame.EndedThisFrame)
                Ended(frame, now);
        }

        private void Telegraph(FishPhase next, float now)
        {
            switch (next)
            {
                case FishPhase.Run:
                case FishPhase.Dive:
                case FishPhase.Surge:
                case FishPhase.Jump:
                    Pulses.Add(now, TelegraphMs, TelegraphAmplitude, true);
                    Pulses.Add(now + TelegraphMs / 1000f + TelegraphGapSeconds, TelegraphMs, TelegraphAmplitude, true);
                    break;
                case FishPhase.Jerk:
                    Pulses.Add(now, JerkMs, JerkAmplitude, true);
                    break;
            }
        }

        private void Shock(FightFrame frame, float now)
        {
            float share = frame.WeakestLinkG > 0f ? frame.PeakTensionMaxThisFrameG / frame.WeakestLinkG : 0f;
            int amplitude = Mathf.RoundToInt(Mathf.Lerp(ShockMinAmplitude, ShockMaxAmplitude, Mathf.Clamp01(share)));
            Pulses.Add(now, ShockMs, amplitude, true);
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
                    Pulses.Clear();
                    for (int i = 0; i < 3; i++)
                        Pulses.Add(now + i * LandedEverySeconds, LandedMs, LandedAmplitude, true);
                    break;
                case FightOutcome.LineBroke:
                case FightOutcome.SpoolEmpty:
                case FightOutcome.RodBroke:
                    Pulses.Clear();
                    Pulses.Add(now, BreakMs, BreakAmplitude, true);
                    break;
            }
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

        private static bool Has(FightEvents events, FightEvents flag) => (events & flag) != 0;
    }
}
