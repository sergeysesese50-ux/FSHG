using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // =====================================================================================================
    // FightFrame — снимок вываживания на кадр: всё, что читают HUD, виды (поплавок, леска, удилище, рыба,
    // камера) и FightFeedback (звук и вибрация). Спецификация: docs/specs/fight-v2.md, §4–§6.
    //
    // ОТКУДА БЕРЁТСЯ
    // FishingController.Update шагает FightModel фиксированными шагами 0,05 с: время кадра копится в
    // накопителе, за кадр делается столько шагов, сколько в нём набралось (не больше 10; остаток сверх этого
    // отбрасывается, чтобы после зависания игра не «догоняла»). После шагов кадра контроллер один раз
    // собирает FightFrame. При 60 к/с шаг бывает примерно раз в три кадра, при 20 к/с — каждый кадр,
    // после рывка — по нескольку за кадр. Поэтому представление читает только FightFrame, а не FightModel:
    // модель меняется ступеньками по 20 Гц, и её Events живут один шаг — в кадре с двумя шагами первый
    // пропал бы, а в кадре без шага последний повторился бы.
    // Для карточек улова/проигрыша — FishingController.Report, для пределов снасти (RodMaxLoadG, C0 катушки
    // и т. п.) — FishingController.Tackle.
    //
    // КОГДА ВАЛИДЕН (смотреть FishingController.State)
    // - Fight: обновляется каждый кадр в Update контроллера.
    // - Кадр, в котором вываживание кончилось: State уже Caught или Lost, EndedThisFrame = true,
    //   Outcome — итог, Events — события последних шагов (Landed, Swung, Broke, HookLoss…), плавные
    //   значения стоят ровно на последнем шаге (Alpha = 1).
    // - Caught / Lost дальше: последний кадр заморожен (можно рисовать рыбу и леску на месте схода),
    //   но покадровые поля пустые: Events = None, SlipCmThisFrame = 0, TicksThisFrame = 0.
    // - Idle / Casting / Waiting / Bite: остаток прошлого вываживания (или нули до первого) — не читать.
    //   Подсечка заполняет снимок заново в том же кадре, ещё до первого шага (Tick = 0).
    //
    // ПОРЯДОК В КАДРЕ
    // LakeHud.Update (порядок −50, читает ввод) → CameraRig.Update (−10, видит снимок прошлого кадра) →
    // FishingController.Update (шаги модели, сборка снимка, обновление видов, RodTip) → Update остальных
    // компонентов (видят снимок этого кадра, но RodTip — только если идут после контроллера) → LateUpdate
    // (LakeHud: стрелка, тренер; FightFeedback: звук и вибрация — всё уже текущее) → OnGUI (LakeHud рисует).
    //
    // ТРИ ВИДА ПОЛЕЙ И КАК ИМИ ПОЛЬЗОВАТЬСЯ
    // (а) Плавные — интерполяция между предыдущим и текущим шагом модели на Alpha = накопитель / шаг.
    //     Ими двигать всё непрерывное: позицию рыбы и поплавка, подъём и изгиб удилища, стрелку натяжения,
    //     счётчик лески и его скорость. Отстают от модели на один шаг (50 мс) — зато без рывков.
    // (б) Дискретные — значения последнего шага как есть. Флаги, фазы и пороги не смешиваются между шагами.
    //     Ими выбирать состояния: позу рыбы, подписи, цвет кнопок, громкость «рычания» и т. п.
    // (в) За кадр — собраны по всем шагам этого кадра (0, 1 или несколько), сбрасываются в начале каждого
    //     кадра в любом состоянии. Только по ним запускать разовые эффекты (звук, вибрацию, всплеск, вспышку
    //     «удара»): флаг виден ровно в одном кадре — не теряется и не повторяется. Не выводить события из
    //     сравнения (б) с прошлым кадром.
    // (г) Мир — посчитано один раз за кадр, метры Unity.
    // Единицы: граммы, сантиметры, секунды; мировые позиции — метры. Снаружи всё только для чтения.
    //
    // ПОЛЯ
    // (а) Плавные:
    //   FishDistanceCm    см   расстояние по воде от берега (начало координат) до рыбы, не меньше 80.
    //   LineOutCm         см   размотано лески: счётчик «14,2 м»; скорость счётчика — по его изменению.
    //   LateralCm         см   уход рыбы вбок: плюс — вправо от Direction; косметика, |x| ≤ 0,3 × дистанции.
    //   RodAngle          0…1  подъём удилища: 0 — опущено, 1 — поднято.
    //   RodAtTop               удилище в самом верху (RodAngle ≥ RodTopAngle): «Подтянуть» его уже не поднимает,
    //                          и модель считает такое удержание простоем (больше 10 с — крючок слабеет).
    //   PeakTensionG     г    натяжение с ударами — по нему рвётся леска (стрелка, яркость лески, «пение»).
    //   TensionG          г    натяжение без ударов.
    //   RodLoadG          г    нагрузка на удилище: PeakTension × (1 + 0,6 × подъём) (изгиб, скрип).
    //   LoadShare         доля PeakTensionG / WeakestLinkG: 1 — на пределе самого слабого звена.
    //   RodLoadShare      доля RodLoadG / предел удилища (Tackle.RodMaxLoadG).
    // (б) Дискретные (последний шаг):
    //   Phase, NextPhase       фаза рыбы и следующая; NextPhase не меняется за 0,6 с до смены
    //                          (кроме броска от удара подсачеком — он без телеграфа).
    //   TelegraphProgress 0…1  телеграф NextPhase: 0 — нет; растёт ступенькой на шаг; 1 — фаза начнётся
    //                          на следующем шаге. Для плавной анимации сгладить у себя.
    //   Stamina           0…1  силы рыбы: 1 — свежая, 0 — выдохлась.
    //   HookHold, HookHoldStart  как держит крючок (≤ 0 — сход) и с чего начал (качество подсечки 1,0 / 0,75);
    //                          кольцо крючка — когда HookHold < HookHoldStart или есть слабина.
    //   LineWear          0…0,4 перекрут лески: прочность = разрывная × (1 − износ).
    //   WeakestLinkG      г    прочность самого слабого звена в пересчёте на натяжение лески.
    //   WeakestIsRod           слабее удилище (подпись «удилище»), иначе леска.
    //   IsDragSlipping         фрикцион пробуксовывает (шпуля крутится назад).
    //   IsCrankingIntoSlip     при этом крутят «Подмотку» — вхолостую, леска перекручивается.
    //   ReelInCmPerSec    см/с сколько выбирает катушка (скорость ручки, высота жужжания).
    //   ReelLoad          0…1  нагрузка катушки 1 − c/C0; 0, если «Подмотка» не нажата.
    //   SlipCmPerSec      см/с скорость пробуксовки (петля щелчков выше 25 щелчков/с).
    //   FishSpeedCmPerSec см/с плюс — рыба идёт к рыбаку, минус — забирает леску (усы на воде).
    //   SlackCm           см   слабина; до 30 см леска считается натянутой. Провис лески — от неё.
    //   CanSwing               «Подтянуть» сейчас выудит рыбу (кнопка «Выудить»).
    //   SwingLimitG       г    предел веса для маха (правило веса из CanSwing): WeightG выше — только подсачек.
    //   InNetReach             рыба в зоне подсачека (кнопка «Подсачек», если WeightG > SwingLimitG).
    //   NetShown               кнопка «Подсачек» на экране: InNetReach и WeightG > SwingLimitG (§2). Лёгкую
    //                          рыбу берут только махом — ни кнопки, ни строки тренера про подсачек.
    //   CanNet                 подсачек сейчас поймает (кнопка горит зелёным).
    //   NetBlock               что мешает подсачеку (леска провисла, удилище опущено, рыба сильна…); None при CanNet.
    //   HeadUp                 голова над водой (поза рыбы).
    //   DragG             г    фрикцион, которым шёл шаг (с учётом предела катушки); 0 до первого шага.
    //                          Насечку фрикциона и подпись «−/+» рисовать по FishingController.DragG —
    //                          это настройка игрока, она уходит в модель следующим шагом.
    //   FishForceG        г    наибольшая тяга рыбы F; постоянна на вываживание.
    //   WeightG           г    вес рыбы; постоянен.
    //   Outcome                итог; InProgress, пока идёт.
    //   Tick, ElapsedSeconds   номер шага модели (0 до первого) и время вываживания, с.
    // (в) За кадр:
    //   Events                    OR флагов FightEvents всех шагов кадра (HookSet — первый шаг после подсечки).
    //   SlipCmThisFrame       см  сколько лески сошло с пробуксовкой за кадр (сумма SlipCmPerSec × шаг):
    //                             щелчок на каждые 1,5 см, вибрация на каждые 10 см — остаток копить у себя.
    //   PeakTensionMaxThisFrameG г  наибольшее PeakTensionG среди шагов кадра; 0, если шагов не было.
    //                             Удар держится один шаг на ×1,0, интерполяция может его срезать — сила
    //                             вибрации и «призрак» на стрелке берутся отсюда.
    //   TicksThisFrame            сколько шагов модели было в этом кадре.
    //   EndedThisFrame            вываживание кончилось в этом кадре (State уже Caught или Lost).
    //   Alpha             0…1     доля шага, на которую интерполированы плавные поля.
    // (г) Мир:
    //   Direction                 единичный горизонтальный вектор от берега (0, 0, 0) к месту подсечки.
    //   Right                     горизонтальный, вправо от Direction: Cross(up, Direction).
    //   FishWorldPosition         Direction × FishDistance + Right × Lateral (y = 0 — поверхность воды):
    //                             сюда ставятся поплавок, рыба, круги и всплески.
    //   RodTip                    кончик удилища в мире; ставит вид удилища (RodView.Tip) в Update контроллера.
    // =====================================================================================================

    /// <summary>
    /// Снимок вываживания на кадр для HUD, видов и FightFeedback. Контракт полей — в комментарии над классом.
    /// Заполняет только FishingController; снаружи всё только для чтения.
    /// </summary>
    public sealed class FightFrame
    {
        /// <summary>Значения, которые интерполируются между шагами.</summary>
        private struct Smooth
        {
            public float FishDistanceCm;
            public float LineOutCm;
            public float LateralCm;
            public float RodAngle;
            public float PeakTensionG;
            public float TensionG;
            public float RodLoadG;

            public static Smooth Of(FightModel m) => new Smooth
            {
                FishDistanceCm = (float)m.FishDistanceCm,
                LineOutCm = (float)m.LineOutCm,
                LateralCm = (float)m.LateralCm,
                RodAngle = (float)m.RodAngle,
                PeakTensionG = (float)m.PeakTensionG,
                TensionG = (float)m.TensionG,
                RodLoadG = (float)m.RodLoadG,
            };
        }

        private Smooth _previous;
        private Smooth _current;
        private float _tickSeconds;
        private float _rodMaxLoadG;

        // ---------- (а) Плавные ----------

        public float FishDistanceCm { get; private set; }
        public float LineOutCm { get; private set; }
        public float LateralCm { get; private set; }
        public float RodAngle { get; private set; }

        /// <summary>С какого подъёма удилище считается в самом верху (модель перестаёт считать «Подтянуть» подъёмом на 1,0).</summary>
        public const float RodTopAngle = 0.99f;

        /// <summary>Удилище в самом верху: удержание «Подтянуть» уже ничего не делает и для модели — простой.</summary>
        public bool RodAtTop => RodAngle >= RodTopAngle;

        public float PeakTensionG { get; private set; }
        public float TensionG { get; private set; }
        public float RodLoadG { get; private set; }
        public float LoadShare { get; private set; }
        public float RodLoadShare { get; private set; }

        // ---------- (б) Дискретные ----------

        public FishPhase Phase { get; private set; }
        public FishPhase NextPhase { get; private set; }
        public float TelegraphProgress { get; private set; }
        public float Stamina { get; private set; }
        public float HookHold { get; private set; }
        public float HookHoldStart { get; private set; }
        public float LineWear { get; private set; }
        public float WeakestLinkG { get; private set; }
        public bool WeakestIsRod { get; private set; }
        public bool IsDragSlipping { get; private set; }
        public bool IsCrankingIntoSlip { get; private set; }
        public float ReelInCmPerSec { get; private set; }
        public float ReelLoad { get; private set; }
        public float SlipCmPerSec { get; private set; }
        public float FishSpeedCmPerSec { get; private set; }
        public float SlackCm { get; private set; }
        public bool CanSwing { get; private set; }
        public float SwingLimitG { get; private set; }
        public bool InNetReach { get; private set; }

        /// <summary>
        /// Кнопка «Подсачек» на экране (§2): рыба в зоне подсачека и тяжелее предела маха. Одно правило для HUD
        /// и тренера — лёгкую рыбу берут махом, подсачека для неё нет.
        /// </summary>
        public bool NetShown => InNetReach && WeightG > SwingLimitG;

        public bool CanNet { get; private set; }

        /// <summary>Что мешает подсачеку: кнопка пишет причину вместо общего «рано».</summary>
        public NetBlock NetBlock { get; private set; }
        public bool HeadUp { get; private set; }
        public float DragG { get; private set; }
        public float FishForceG { get; private set; }
        public int WeightG { get; private set; }
        public FightOutcome Outcome { get; private set; }
        public int Tick { get; private set; }
        public float ElapsedSeconds { get; private set; }

        // ---------- (в) За кадр ----------

        public FightEvents Events { get; private set; }
        public float SlipCmThisFrame { get; private set; }
        public float PeakTensionMaxThisFrameG { get; private set; }
        public int TicksThisFrame { get; private set; }
        public bool EndedThisFrame { get; private set; }
        public float Alpha { get; private set; }

        // ---------- (г) Мир ----------

        public Vector3 Direction { get; private set; } = Vector3.forward;
        public Vector3 Right { get; private set; } = Vector3.right;
        public Vector3 FishWorldPosition { get; private set; }
        public Vector3 RodTip { get; private set; }

        // ---------- Заполнение (только FishingController) ----------

        /// <summary>Новое вываживание: снимок до первого шага, направление от берега к месту подсечки.</summary>
        internal void Start(FightModel model, FightTackle tackle, Vector3 direction, double tickSeconds)
        {
            _tickSeconds = (float)tickSeconds;
            _rodMaxLoadG = tackle.RodMaxLoadG;
            direction.y = 0f;
            Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            Right = Vector3.Cross(Vector3.up, Direction);

            _current = Smooth.Of(model);
            _previous = _current;
            CaptureDiscrete(model);
            BeginFrame();
            Compose(0f);
        }

        /// <summary>Начало кадра в любом состоянии: покадровые поля пустеют.</summary>
        internal void BeginFrame()
        {
            Events = FightEvents.None;
            SlipCmThisFrame = 0f;
            PeakTensionMaxThisFrameG = 0f;
            TicksThisFrame = 0;
            EndedThisFrame = false;
        }

        /// <summary>Сразу после FightModel.Step: сдвиг плавных снимков, дискретные поля, сбор событий кадра.</summary>
        internal void AddTick(FightModel model)
        {
            _previous = _current;
            _current = Smooth.Of(model);
            CaptureDiscrete(model);
            Events |= model.Events;
            SlipCmThisFrame += (float)model.SlipCmPerSec * _tickSeconds;
            if (TicksThisFrame == 0 || _current.PeakTensionG > PeakTensionMaxThisFrameG)
                PeakTensionMaxThisFrameG = _current.PeakTensionG;
            TicksThisFrame++;
        }

        /// <summary>Вываживание кончилось в этом кадре: плавные поля встают на последний шаг.</summary>
        internal void End()
        {
            EndedThisFrame = true;
            Compose(1f);
        }

        /// <summary>Интерполяция плавных полей на долю шага alpha и мировая позиция рыбы.</summary>
        internal void Compose(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            Alpha = alpha;
            FishDistanceCm = Mathf.LerpUnclamped(_previous.FishDistanceCm, _current.FishDistanceCm, alpha);
            LineOutCm = Mathf.LerpUnclamped(_previous.LineOutCm, _current.LineOutCm, alpha);
            LateralCm = Mathf.LerpUnclamped(_previous.LateralCm, _current.LateralCm, alpha);
            RodAngle = Mathf.LerpUnclamped(_previous.RodAngle, _current.RodAngle, alpha);
            PeakTensionG = Mathf.LerpUnclamped(_previous.PeakTensionG, _current.PeakTensionG, alpha);
            TensionG = Mathf.LerpUnclamped(_previous.TensionG, _current.TensionG, alpha);
            RodLoadG = Mathf.LerpUnclamped(_previous.RodLoadG, _current.RodLoadG, alpha);
            LoadShare = WeakestLinkG > 0f ? PeakTensionG / WeakestLinkG : 0f;
            RodLoadShare = _rodMaxLoadG > 0f ? RodLoadG / _rodMaxLoadG : 0f;
            FishWorldPosition = Direction * (FishDistanceCm / 100f) + Right * (LateralCm / 100f);
        }

        /// <summary>Кончик удилища от вида удилища (после его обновления в этом кадре).</summary>
        internal void SetRodTip(Vector3 tip) => RodTip = tip;

        private void CaptureDiscrete(FightModel m)
        {
            Phase = m.Phase;
            NextPhase = m.NextPhase;
            TelegraphProgress = (float)m.TelegraphProgress;
            Stamina = (float)m.Stamina;
            HookHold = (float)m.HookHold;
            HookHoldStart = (float)m.HookHoldStart;
            LineWear = (float)m.LineWear;
            WeakestLinkG = (float)m.WeakestLinkG;
            WeakestIsRod = m.WeakestIsRod;
            IsDragSlipping = m.IsDragSlipping;
            IsCrankingIntoSlip = m.IsCrankingIntoSlip;
            ReelInCmPerSec = (float)m.ReelInCmPerSec;
            ReelLoad = (float)m.ReelLoad;
            SlipCmPerSec = (float)m.SlipCmPerSec;
            FishSpeedCmPerSec = (float)m.FishSpeedCmPerSec;
            SlackCm = (float)m.SlackCm;
            CanSwing = m.CanSwing;
            SwingLimitG = (float)m.SwingLimitG;
            InNetReach = m.InNetReach;
            CanNet = m.CanNet;
            NetBlock = m.NetBlock;
            HeadUp = m.HeadUp;
            DragG = (float)m.DragG;
            FishForceG = (float)m.FishForceG;
            WeightG = m.WeightG;
            Outcome = m.Outcome;
            Tick = m.Tick;
            ElapsedSeconds = (float)m.ElapsedSeconds;
        }
    }
}
