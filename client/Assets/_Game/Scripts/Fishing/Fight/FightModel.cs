using System;

namespace Fishing.Core
{
    /// <summary>
    /// Вываживание v2 (docs/specs/fight-v2.md, §3). Один вызов Step — шаг TickSeconds; счётчик шагов Tick — целый.
    ///
    /// Баланс сил. Рыба тянет с силой P (может быть меньше нуля на свечке), её тело тормозит с силой
    /// b × скорость. Две кнопки — два мотора: катушка c(T) и подъём удилища l(T), оба слабеют под нагрузкой
    /// (FightSolver). Режимы натянутой лески: (a) моторы тянут рыбу; (b) «Подмотка» с поднятым удилищем
    /// опускает его, рыба стоит; (c) без кнопок леска стоит, удилище уступает под нагрузкой и никогда не даёт
    /// слабины. Фрикцион с трогания: срывается выше 1,2 × фрикцион, дальше держит ровно фрикцион; подмотка
    /// в пробуксовку перекручивает леску. Слабина больше 30 см — натяжения нет, рыба плывёт свободно,
    /// крючок держит всё хуже (HookHold). Удары (рывок, уход в глубину, бросок, вершина свечки, трогание
    /// фрикциона) размазаны на 3 шага. Ловят махом удилища (CanSwing + Lift) или подсачеком (CanNet + Net).
    ///
    /// Арифметика — только + − × ÷ floor min max abs: тот же расчёт потом повторит сервер (Go/TS).
    /// Боковое смещение LateralCm — косметика: считается от visualSeed целочисленным хешем и в физику
    /// не попадает; генератор IRandom тратится только на поведение рыбы.
    ///
    /// Порядок вызовов генератора (ReplayVersion = 2):
    /// 1. Конструктор — ни одного. Вываживание начинается фиксированной паузой Rest 0,8 с (размах
    ///    мотания головой ×2), следующая фаза — Run.
    /// 2. В начале каждой фазы: одно число на длительность (у Surge длительность фиксирована — ни одного);
    ///    если фаза Rest — ещё одно число на выбор следующей фазы по таблице стиля, иначе следующая — Rest.
    /// 3. В первый шаг, когда до конца фазы осталось не больше 0,6 с, — детерминированные поправки без
    ///    генератора: силы меньше 0,2 → следующая Rest; иначе в паузе, если рыба ближе max(450 см, 2 × вылет)
    ///    и броска от берега ещё не было, → следующая Surge. После этого следующая фаза не меняется.
    /// 4. Удар подсачеком по неготовой рыбе сразу запускает Surge без телеграфа (генератор не тратится).
    /// Длительности фаз переводятся в целые шаги: floor(секунды / шаг + 0,5).
    /// </summary>
    public sealed class FightModel
    {
        /// <summary>Версия правил для повтора на сервере: меняется вместе с порядком генератора и формулами.</summary>
        public const int ReplayVersion = 2;

        // Таблицы по FishPhase: Rest, Run, Jerk, Dive, Jump, Surge.
        private static readonly double[] PhaseTau = { 0.4, 0.3, 0.05, 0.4, 0.1, 0.2 };
        private static readonly double[] PhaseMinSeconds = { 1.2, 1.2, 0.3, 1.5, 0.7, 1.5 };
        private static readonly double[] PhaseMaxSeconds = { 2.8, 3.0, 0.5, 3.5, 1.0, 1.5 };
        private static readonly double[] PhaseLeadSeconds = { 0.3, 0.5, 0.5, 0.5, 0.6, 0.6 };

        private const double OpeningRestSeconds = 0.8;
        private const double LockNextSeconds = 0.6;
        private const double TelegraphPreload = 0.3;
        private const double SlackToleranceCm = 30;
        private const double LiftSeconds = 0.8;
        private const double MinFishDistanceCm = 80;
        private const double RodSnapEpsilon = 1e-9;
        private const double RodLeverFactor = 0.6;
        private const double ShortLineCm = 800;
        private const double ShortLineSpanCm = 600;
        private const double ShortLineBoost = 0.6;
        private const double BreakawayShockShare = 0.2;
        private const double MaxLineWear = 0.4;
        private const double LineWearPerSecReelingOnSlip = 0.02;
        private const double LateralShare = 0.35;
        private const double LateralDecayCmPerSec = 10;
        private const double LateralMaxShare = 0.3;

        private const double LandingExtraCm = 50;
        private const double SwingMaxWeightG = 450;
        private const double SwingLineShare = 0.25;
        private const double SwingMaxStamina = 0.6;
        private const double NetMaxStamina = 0.3;
        private const double NetMinAngle = 0.4;
        private const double NetBumpHook = 0.1;
        private const double NetBumpStamina = 0.05;
        private const double NetCooldownSeconds = 2.0;
        private const double HeadUpAngle = 0.6;
        private const double HeadUpStamina = 0.3;
        private const double HeadUpDistanceCm = 800;

        private const double ExhaustedStamina = 0.2;
        private const double RecoveryForceShare = 0.25;
        private const double RecoveryDragMultiple = 2.0;
        private const double RecoveryPerSec = 0.03;
        private const double SlackRecoveryPerSec = 0.05;
        private const double ShoreSurgeCm = 450;
        private const double JerkOnSlackHook = 0.25;
        private const double JumpOnSlackHook = 0.45;
        private const double JumpTightShare = 0.05;
        private const double IdleSeconds = 10;
        private const double IdleHookPerSec = 0.05;
        private const double HookRecoveryPerSec = 0.1;

        private readonly FightTackle _tackle;
        private readonly FightSettings _settings;
        private readonly IRandom _random;
        private readonly FishPhase[] _activePhases;
        private readonly double[] _activeWeights;
        private readonly double _slackShakePerSec;
        private readonly double _shakeHz;
        private readonly double _shakeAmplitude;
        private readonly double _bodyDrag;
        private readonly double _staminaSeconds;
        private readonly uint _visualSeed;
        private readonly double _dt;
        private readonly int _lockTicks;
        private readonly int[] _leadTicks = new int[6];

        private readonly double[] _shocks = new double[3];
        private readonly FightReport _report = new FightReport();

        private double _pull;
        private int _phaseTicksLeft;
        private int _phaseTicksTotal;
        private bool _nextLocked;
        private bool _telegraphStarted;
        private bool _shoreSurgeScheduled;
        private int _side;
        private int _shockTicks;
        private int _breakawayTicks;
        private double _idleSeconds;
        private int _netCooldownTicks;
        private bool _restLifted;
        private FightCause _lastHookCause;
        private double _tickHookLoss;

        public FightModel(FishSpecies species, int weightG, FightTackle tackle, double startDistanceCm,
            IRandom random, FightSettings settings = null, double initialSlackCm = 0,
            double hookHold = 1, uint visualSeed = 0)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            _tackle = tackle ?? throw new ArgumentNullException(nameof(tackle));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _settings = settings ?? new FightSettings();
            _visualSeed = visualSeed;
            _dt = _settings.TickSeconds;

            WeightG = weightG;
            FishForceG = Math.Max(1, weightG) * (0.5 + 0.1 * species.strength);
            FishSpeedLimitCmPerSec = 30 + 12 * species.strength;
            _bodyDrag = _settings.BodyDragFactor * FishForceG / FishSpeedLimitCmPerSec;
            _staminaSeconds = _settings.StaminaBaseSeconds + _settings.StaminaPerPoint * species.stamina
                              + _settings.StaminaPerKg * Math.Max(1, weightG) / 1000.0;
            StyleTable(species.fightStyle, out _activePhases, out _activeWeights, out _slackShakePerSec,
                out _shakeHz, out _shakeAmplitude);

            _lockTicks = Ticks(LockNextSeconds);
            for (int i = 0; i < _leadTicks.Length; i++)
                _leadTicks[i] = Math.Max(1, Ticks(PhaseLeadSeconds[i]));

            FishDistanceCm = Math.Max(MinFishDistanceCm, startDistanceCm);
            RodAngle = 0.5;
            LineOutCm = FishDistanceCm + RodAngle * _tackle.RodReachCm + Math.Max(0.0, initialSlackCm);
            Stamina = 1.0;
            HookHoldStart = hookHold;
            HookHold = hookHold;
            Outcome = FightOutcome.InProgress;

            Phase = FishPhase.Rest;
            NextPhase = FishPhase.Run;
            PhaseIndex = 0;
            _phaseTicksTotal = Math.Max(1, Ticks(OpeningRestSeconds));
            _phaseTicksLeft = _phaseTicksTotal;
            NextPhaseInSeconds = _phaseTicksLeft * _dt;
            _side = Side(0);
            _pull = PullTarget();
            PullG = _pull;
            _report.RestsTotal = 1;
            _report.Phase = Phase;
            _report.RodAngle = RodAngle;
            UpdateDerived();
        }

        // ---------- Состояние только для чтения ----------

        public int WeightG { get; }

        /// <summary>Наибольшая тяга рыбы F = вес × (0,5 + 0,1 × сила), г.</summary>
        public double FishForceG { get; }

        /// <summary>Характерная скорость рыбы V = 30 + 12 × сила, см/с.</summary>
        public double FishSpeedLimitCmPerSec { get; }

        public FightOutcome Outcome { get; private set; }

        /// <summary>Номер шага: 0 до первого Step.</summary>
        public int Tick { get; private set; }

        public double ElapsedSeconds { get; private set; }

        /// <summary>Что случилось за последний шаг.</summary>
        public FightEvents Events { get; private set; }

        /// <summary>Итог и история; окончательный, когда Outcome не InProgress.</summary>
        public FightReport Report => _report;

        public FishPhase Phase { get; private set; }

        /// <summary>Следующая фаза. Не меняется, когда до неё осталось не больше 0,6 с (кроме удара подсачеком).</summary>
        public FishPhase NextPhase { get; private set; }

        public double NextPhaseInSeconds { get; private set; }

        /// <summary>Телеграф следующей фазы: 0 — ещё нет, 1 — фаза начнётся на следующем шаге.</summary>
        public double TelegraphProgress { get; private set; }

        /// <summary>Номер фазы с начала вываживания: 0 — стартовая пауза.</summary>
        public int PhaseIndex { get; private set; }

        /// <summary>Тяга рыбы P в этот шаг, г; меньше нуля — рыба идёт к рыбаку (свечка).</summary>
        public double PullG { get; private set; }

        /// <summary>Натяжение лески без ударов, г.</summary>
        public double TensionG { get; private set; }

        /// <summary>Натяжение с ударами — по нему рвётся леска, г.</summary>
        public double PeakTensionG { get; private set; }

        /// <summary>Нагрузка на удилище: PeakTension × (1 + 0,6 × подъём), г.</summary>
        public double RodLoadG { get; private set; }

        /// <summary>Прочность самого слабого звена в пересчёте на натяжение лески, г.</summary>
        public double WeakestLinkG { get; private set; }

        /// <summary>Самое слабое звено — удилище (иначе леска).</summary>
        public bool WeakestIsRod { get; private set; }

        /// <summary>Фрикцион, которым шёл последний шаг (с учётом предела катушки), г.</summary>
        public double DragG { get; private set; }

        public bool IsDragSlipping { get; private set; }

        /// <summary>Скорость пробуксовки фрикциона, см/с (для щелчков и вибрации).</summary>
        public double SlipCmPerSec { get; private set; }

        /// <summary>Сколько лески выбирает катушка, см/с.</summary>
        public double ReelInCmPerSec { get; private set; }

        /// <summary>Нагрузка на катушку: 1 − c/C0, 0 — крутится свободно; 0, если «Подмотка» не нажата.</summary>
        public double ReelLoad { get; private set; }

        /// <summary>Крутит катушку, а фрикцион пробуксовывает: леска перекручивается.</summary>
        public bool IsCrankingIntoSlip { get; private set; }

        /// <summary>Режим лески в последний шаг.</summary>
        public FightMode Mode { get; private set; }

        /// <summary>Скорость рыбы, см/с: плюс — к рыбаку, минус — забирает леску.</summary>
        public double FishSpeedCmPerSec { get; private set; }

        /// <summary>Расстояние до рыбы, см. Не меньше 80.</summary>
        public double FishDistanceCm { get; private set; }

        /// <summary>Сколько лески размотано, см.</summary>
        public double LineOutCm { get; private set; }

        /// <summary>Боковое смещение рыбы, см (косметика, в физику не входит).</summary>
        public double LateralCm { get; private set; }

        /// <summary>Подъём удилища: 0 — опущено, 1 — поднято полностью.</summary>
        public double RodAngle { get; private set; }

        /// <summary>Слабина лески, см; до 30 см леска считается натянутой.</summary>
        public double SlackCm => LineOutCm - (FishDistanceCm + RodAngle * _tackle.RodReachCm);

        /// <summary>Силы рыбы: 1 — свежая, 0 — выбилась из сил.</summary>
        public double Stamina { get; private set; }

        /// <summary>Как держит крючок: 0 и меньше — сход.</summary>
        public double HookHold { get; private set; }

        /// <summary>С какого значения HookHold начал (качество подсечки); выше него не восстанавливается.</summary>
        public double HookHoldStart { get; }

        /// <summary>Износ лески (перекрут): прочность = разрывная × (1 − износ).</summary>
        public double LineWear { get; private set; }

        /// <summary>
        /// Рыбу можно выудить махом: лёгкая, у берега, леска натянута и рыба устала (силы меньше 0,6).
        /// Свежую рыбу «Подтянуть» просто подтягивает: махом её не пробуют, поэтому кнопка «Выудить»
        /// и подсказка показываются только тогда, когда мах удастся.
        /// </summary>
        public bool CanSwing { get; private set; }

        /// <summary>
        /// Предел веса для маха, г: min(450, 0,25 × прочность лески с износом) — то же правило веса, что в CanSwing.
        /// Рыбу тяжелее берут только подсачеком (HUD по нему решает, показывать ли «Подсачек»). Только чтение.
        /// </summary>
        public double SwingLimitG => Math.Min(SwingMaxWeightG, SwingLineShare * _tackle.LineBreakingLoadG * (1.0 - LineWear));

        /// <summary>Рыба в зоне подсачека: не дальше вылета удилища + 50 см.</summary>
        public bool InNetReach { get; private set; }

        /// <summary>Подсачек сейчас поймает: в зоне, пауза, силы не больше 0,3, удилище не ниже 0,4, леска натянута.</summary>
        public bool CanNet { get; private set; }

        /// <summary>Что мешает подсачеку прямо сейчас; None — ровно тогда, когда CanNet.</summary>
        public NetBlock NetBlock { get; private set; }

        /// <summary>Голова над водой: рыба готова к подсачеку или почти готова.</summary>
        public bool HeadUp { get; private set; }

        /// <summary>Докуда достаёт подсачек, см.</summary>
        public double NetReachCm => _tackle.RodReachCm + LandingExtraCm;

        // ---------- Шаг ----------

        public FightOutcome Step(in FightInput input)
        {
            Events = FightEvents.None;
            if (Outcome != FightOutcome.InProgress)
                return Outcome;

            var s = _settings;
            double dt = _dt;
            double reach = _tackle.RodReachCm;
            Tick++;
            ElapsedSeconds = Tick * dt;
            if (Tick == 1)
                Events |= FightEvents.HookSet;

            double drag = Math.Max(0, Math.Min(input.DragG, _tackle.ReelMaxDragG));
            DragG = drag;
            double hookBefore = HookHold;
            _tickHookLoss = 0.0;
            // Подсачек судится по той же паузе, по которой в прошлом шаге считался CanNet: иначе в шаг, когда пауза
            // кончается, нажатие уже принималось, а CanNet ещё был false, и готовую рыбу задевали подсачеком.
            bool netReady = _netCooldownTicks == 0;
            if (_netCooldownTicks > 0)
                _netCooldownTicks--;

            // 1. Вываживание кончается по тому состоянию, которое игрок видел в прошлом шаге.
            if (input.Lift && CanSwing)
            {
                Land(FightLanding.Swung);
                return Outcome;
            }

            bool phaseStarted = false;
            if (input.Net && InNetReach && netReady)
            {
                if (CanNet)
                {
                    Land(FightLanding.Netted);
                    return Outcome;
                }

                LoseHook(NetBumpHook, FightCause.NetBump);
                Stamina = Math.Min(1.0, Stamina + NetBumpStamina);
                _netCooldownTicks = Ticks(NetCooldownSeconds);
                Events |= FightEvents.NetBump;
                StartPhase(FishPhase.Surge);
                phaseStarted = true;
            }

            // 2. Часы фаз, поправки, телеграф.
            if (!phaseStarted)
            {
                _phaseTicksLeft--;
                if (_phaseTicksLeft <= 0)
                {
                    StartPhase(NextPhase);
                    phaseStarted = true;
                }
            }

            if (!_nextLocked && _phaseTicksLeft <= _lockTicks)
            {
                if (Stamina < ExhaustedStamina)
                {
                    NextPhase = FishPhase.Rest;
                }
                else if (Phase == FishPhase.Rest && !_shoreSurgeScheduled
                                                  && FishDistanceCm < Math.Max(ShoreSurgeCm, 2 * reach))
                {
                    NextPhase = FishPhase.Surge;
                    _shoreSurgeScheduled = true;
                }

                _nextLocked = true;
            }

            int lead = _leadTicks[(int)NextPhase];
            if (_phaseTicksLeft <= lead)
            {
                TelegraphProgress = Math.Min(1.0, (double)(lead - _phaseTicksLeft + 1) / lead);
                if (!_telegraphStarted)
                {
                    _telegraphStarted = true;
                    Events |= FightEvents.TelegraphStart;
                }
            }
            else
            {
                TelegraphProgress = 0.0;
            }

            NextPhaseInSeconds = _phaseTicksLeft * dt;

            // 3. Тяга рыбы плавно идёт к цели своей фазы.
            double target = PullTarget();
            _pull += (target - _pull) * Math.Min(1.0, dt / PhaseTau[(int)Phase]);
            PullG = _pull;

            // 4. Натяжение и движение.
            double slack0 = SlackCm;
            bool slack = slack0 > SlackToleranceCm;
            bool lifting = input.Lift && RodAngle < 1.0;
            var sol = slack
                ? SolveSlack(input)
                : FightSolver.SolveTight(_tackle, LiftSeconds, _pull, _bodyDrag, input.Reel, input.Lift, RodAngle,
                    drag, IsDragSlipping, Phase == FishPhase.Rest);
            Move(sol, slack, slack0);

            TensionG = sol.TensionG;
            IsDragSlipping = sol.Slipping;
            SlipCmPerSec = sol.SlipCmPerSec;
            Mode = sol.Mode;
            double c0 = _tackle.ReelFightSpeedCmPerSec;
            ReelLoad = input.Reel && c0 > 0 ? Math.Max(0.0, 1.0 - sol.ReelMotorCmPerSec / c0) : 0.0;
            IsCrankingIntoSlip = input.Reel && sol.Slipping;
            double u = FishSpeedCmPerSec;
            double force = FishForceG;

            // 5. Удары: размазаны на 3 шага (×1; ×0,6; ×0,3).
            if (phaseStarted && Phase == FishPhase.Jerk && slack)
                LoseHook(JerkOnSlackHook, FightCause.SlackShaking);
            if (phaseStarted && !slack)
            {
                double k = Phase == FishPhase.Jerk ? s.JerkShock
                    : Phase == FishPhase.Dive ? s.DiveShock
                    : Phase == FishPhase.Surge ? s.SurgeShock
                    : 0.0;
                if (k > 0)
                {
                    QueueShock(Hit(k));
                    _shockTicks = 3;
                    Events |= FightEvents.Shock;
                }
            }

            if (Phase == FishPhase.Jump && _phaseTicksTotal - _phaseTicksLeft == _phaseTicksTotal / 2)
            {
                Events |= FightEvents.JumpApex;
                if (slack || TensionG < JumpTightShare * force)
                {
                    LoseHook(JumpOnSlackHook, FightCause.JumpOnSlack);
                }
                else
                {
                    QueueShock(Hit(s.JumpShock));
                    _shockTicks = 3;
                    Events |= FightEvents.Shock;
                }
            }

            if (sol.Breakaway)
            {
                QueueBreakawayShock(BreakawayShockShare * drag);
                _breakawayTicks = 3;
                Events |= FightEvents.Breakaway;
            }

            PeakTensionG = TensionG + (slack ? 0.0 : _shocks[0]);

            // 6. Силы рыбы, перекрут лески, крючок.
            // Рыба отдыхает, когда леска почти не держит: T < 0,25 × min(F, 2 × фрикцион). Порог от фрикциона нужен
            // рыбе намного сильнее снасти: снасть не даёт больше ~фрикциона, и при пороге 0,25 F такая рыба
            // отдыхала бы даже под полной нагрузкой — вываживание не кончалось бы никогда.
            double drain = Math.Min(1.2, TensionG / force)
                           * (0.5 + 0.5 * Math.Min(1.0, Math.Abs(u) / FishSpeedLimitCmPerSec)) / _staminaSeconds;
            double stamina = Stamina - drain * dt;
            if (slack)
                stamina += SlackRecoveryPerSec * dt;
            else if (TensionG < RecoveryForceShare * Math.Min(force, RecoveryDragMultiple * drag))
                stamina += RecoveryPerSec * dt;
            Stamina = Math.Max(0.0, Math.Min(1.0, stamina));

            if (input.Reel && IsDragSlipping)
            {
                LineWear = Math.Min(MaxLineWear, LineWear + LineWearPerSecReelingOnSlip * dt);
                _report.SecondsCrankedOnSlip += dt;
            }

            if (slack)
                LoseHook(_slackShakePerSec * Math.Min(1.0, (slack0 - SlackToleranceCm) / 60.0) * dt, FightCause.SlackShaking);
            // Простой: ни подмотки, ни подъёма (удержание «Подтянуть» с удилищем в самом верху ничего не делает).
            // Время пробуксовки в 10 с простоя не входит, но и не обнуляет счёт; набрав 10 с, крючок слабеет до
            // первого действия игрока, и на пробуксовке тоже. Иначе выдохшаяся рыба, изредка срывающая фрикцион,
            // держала бы бездействующего игрока бесконечно (крючок на натянутой леске восстанавливается быстрее).
            if (input.Reel || lifting)
                _idleSeconds = 0.0;
            else if (!IsDragSlipping)
                _idleSeconds += dt;
            if (_idleSeconds > IdleSeconds)
                LoseHook(IdleHookPerSec * dt, FightCause.Idle);

            if (HookHold < hookBefore)
                Events |= FightEvents.HookLoss;
            else if (!slack)
                HookHold = Math.Min(HookHoldStart, HookHold + HookRecoveryPerSec * dt);

            // 7. Косметика и учёт пауз.
            if (Phase == FishPhase.Run || Phase == FishPhase.Dive || Phase == FishPhase.Surge)
                LateralCm += _side * LateralShare * Math.Abs(u) * dt;
            else if (Phase == FishPhase.Rest)
                LateralCm = LateralCm > 0
                    ? Math.Max(0.0, LateralCm - LateralDecayCmPerSec * dt)
                    : Math.Min(0.0, LateralCm + LateralDecayCmPerSec * dt);
            double maxLateral = LateralMaxShare * FishDistanceCm;
            LateralCm = Math.Max(-maxLateral, Math.Min(maxLateral, LateralCm));

            if (Phase == FishPhase.Rest && input.Lift && !slack && !_restLifted)
            {
                _restLifted = true;
                _report.RestsLifted++;
            }

            // 8. Нагрузки, обрывы, сход.
            UpdateDerived();
            Judge(drag);
            Record(drag);

            _shocks[0] = _shocks[1];
            _shocks[1] = _shocks[2];
            _shocks[2] = 0.0;
            if (_shockTicks > 0)
                _shockTicks--;
            if (_breakawayTicks > 0)
                _breakawayTicks--;
            return Outcome;
        }

        // ---------- Движение ----------

        private FightSolution SolveSlack(in FightInput input)
        {
            double reach = _tackle.RodReachCm;
            var sol = new FightSolution
            {
                Mode = FightMode.Slack,
                TensionG = 0.0,
                Negative = _pull < 0,
                FishCmPerSec = _bodyDrag > 0 ? -_pull / _bodyDrag : 0.0,
                LineCmPerSec = input.Reel ? -_tackle.ReelFightSpeedCmPerSec : 0.0,
                RodCmPerSec = input.Lift && RodAngle < 1.0 ? reach / LiftSeconds : 0.0,
                ReelMotorCmPerSec = input.Reel ? _tackle.ReelFightSpeedCmPerSec : 0.0,
            };
            return sol;
        }

        /// <summary>Переводит скорости решения в перемещения шага с ограничениями удилища и берега.</summary>
        private void Move(in FightSolution sol, bool slack, double slack0)
        {
            double dt = _dt;
            double reach = _tackle.RodReachCm;
            double dLine = sol.LineCmPerSec * dt;
            double dRod = sol.RodCmPerSec * dt;
            double dFish = -sol.FishCmPerSec * dt;

            // Удилище не выходит за 0…1; связанные с ним перемещения пересчитываются.
            dRod = Math.Max(-RodAngle * reach, Math.Min((1.0 - RodAngle) * reach, dRod));
            bool coupled = !slack && !sol.Slipping;
            if (coupled && sol.Mode == FightMode.PumpReturn)
                dLine = dRod;
            else if (coupled && !sol.Negative && sol.Mode == FightMode.Pull)
                dFish = dLine - dRod;
            else if (coupled && !sol.Negative && sol.Mode == FightMode.Hold)
                dFish = -dRod;

            // Ближе 80 см рыба не подходит: катушка встаёт, а подъём удилища поднимает голову рыбы,
            // и леска на столько же сходит со шпули (иначе у берега удилище не поднять под подсачек).
            if (FishDistanceCm + dFish < MinFishDistanceCm)
            {
                double allowed = Math.Max(0.0, FishDistanceCm - MinFishDistanceCm);
                dFish = -allowed;
                if (coupled && !sol.Negative && sol.Mode == FightMode.Pull)
                    dLine = dFish + dRod;
            }

            // Упор удилища — ровно 0 или 1: после ограничения dRod сумма RodAngle + dRod / reach может оставить
            // остаток в последних разрядах (~1e-19), и тогда следующая «Подмотка» шла бы режимом (b) и шаг ничего не выбирала.
            double angle = Math.Max(0.0, Math.Min(1.0, RodAngle + (reach > 0 ? dRod / reach : 0.0)));
            if (angle < RodSnapEpsilon)
                angle = 0.0;
            else if (angle > 1.0 - RodSnapEpsilon)
                angle = 1.0;
            RodAngle = angle;
            LineOutCm = Math.Max(0.0, LineOutCm + dLine);
            FishDistanceCm += dFish;
            FishSpeedCmPerSec = -dFish / dt;
            ReelInCmPerSec = Math.Max(0.0, -dLine / dt);

            // Натянутая леска выбирает остаток слабины: тянущая рыба её распрямляет.
            if (!slack && slack0 > 0 && sol.TensionG > 0)
            {
                double rest = SlackCm;
                if (rest > 0)
                    FishDistanceCm += rest;
            }

            // Леска не короче расстояния до рыбы.
            double geometry = FishDistanceCm + RodAngle * reach;
            if (LineOutCm < geometry)
                LineOutCm = geometry;
        }

        // ---------- Фазы ----------

        private void StartPhase(FishPhase phase)
        {
            Phase = phase;
            PhaseIndex++;
            Events |= FightEvents.PhaseStart;
            if (phase == FishPhase.Surge)
                Events |= FightEvents.SurgeStart;

            double seconds = phase == FishPhase.Surge ? PhaseMinSeconds[(int)phase] : Duration(phase);
            _phaseTicksTotal = Math.Max(1, Ticks(seconds));
            _phaseTicksLeft = _phaseTicksTotal;

            if (phase == FishPhase.Rest)
            {
                NextPhase = PickActive();
                _report.RestsTotal++;
            }
            else
            {
                NextPhase = FishPhase.Rest;
            }

            _nextLocked = false;
            _telegraphStarted = false;
            _restLifted = false;
            _side = Side(PhaseIndex);
        }

        private double Duration(FishPhase phase)
        {
            double min = PhaseMinSeconds[(int)phase];
            double max = PhaseMaxSeconds[(int)phase];
            return min + (max - min) * _random.NextDouble();
        }

        private FishPhase PickActive()
        {
            double pick = _random.NextDouble();
            double sum = 0.0;
            for (int i = 0; i < _activePhases.Length; i++)
            {
                sum += _activeWeights[i];
                if (pick < sum)
                    return _activePhases[i];
            }

            return _activePhases[_activePhases.Length - 1];
        }

        /// <summary>
        /// Цель тяги: P* = F × PF × (0,3 + 0,7 × S) × (1 + A × tri(f × Tick × шаг)), tri(x) = 4|x − floor(x) − 0,5| − 1.
        /// PF подмешивает следующую фазу на 0,3 × TelegraphProgress.
        /// </summary>
        private double PullTarget()
        {
            var force = _settings.PhaseForce;
            double pf = force[(int)Phase];
            pf += (force[(int)NextPhase] - pf) * TelegraphPreload * TelegraphProgress;
            double amplitude = _shakeAmplitude * (PhaseIndex == 0 ? 2.0 : 1.0);
            double x = _shakeHz * Tick * _dt;
            double tri = 4.0 * Math.Abs(x - Math.Floor(x) - 0.5) - 1.0;
            return FishForceG * pf * (0.3 + 0.7 * Stamina) * (1.0 + amplitude * tri);
        }

        // ---------- Удары, крючок, итог ----------

        private double Hit(double k)
        {
            double shortLine = Math.Max(0.0, Math.Min(1.0, (ShortLineCm - LineOutCm) / ShortLineSpanCm));
            return k * FishForceG * (0.3 + 0.7 * Stamina)
                   * (1.0 - _tackle.RodFlex * (0.5 + 0.5 * RodAngle))
                   * (IsDragSlipping ? 0.5 : 0.8)
                   * (1.0 + ShortLineBoost * shortLine);
        }

        private void QueueShock(double hit)
        {
            _shocks[0] += hit;
            _shocks[1] += 0.6 * hit;
            _shocks[2] += 0.3 * hit;
        }

        /// <summary>
        /// Удар трогания фрикциона (0,2 × фрикцион, те же ×1; ×0,6; ×0,3) не складывается с ударом рыбы, который
        /// ещё идёт: фрикцион срывает сам рывок, и пик — большее из двух, а не сумма. Иначе на 0,30 с автофрикционом
        /// рывок при поднятом удилище (как учит техника) всегда ломал бы удилище.
        /// </summary>
        private void QueueBreakawayShock(double hit)
        {
            _shocks[0] = Math.Max(_shocks[0], hit);
            _shocks[1] = Math.Max(_shocks[1], 0.6 * hit);
            _shocks[2] = Math.Max(_shocks[2], 0.3 * hit);
        }

        /// <summary>
        /// Крючок держит хуже. Причина схода — самая большая потеря последнего шага, где HookHold уменьшался:
        /// вершина свечки (−0,45) или удар подсачеком (−0,1) важнее тряски на слабине и простоя в том же шаге.
        /// </summary>
        private void LoseHook(double amount, FightCause cause)
        {
            if (amount <= 0)
                return;
            HookHold -= amount;
            if (amount > _tickHookLoss)
            {
                _tickHookLoss = amount;
                _lastHookCause = cause;
            }
        }

        private void UpdateDerived()
        {
            double lineLimit = _tackle.LineBreakingLoadG * (1.0 - LineWear);
            double lever = 1.0 + RodLeverFactor * RodAngle;
            double rodLimit = _tackle.RodMaxLoadG / lever;
            WeakestIsRod = rodLimit < lineLimit;
            WeakestLinkG = Math.Min(lineLimit, rodLimit);
            RodLoadG = PeakTensionG * lever;

            bool tight = SlackCm <= SlackToleranceCm;
            InNetReach = FishDistanceCm <= NetReachCm;
            CanSwing = InNetReach && tight && Stamina < SwingMaxStamina
                       && WeightG <= Math.Min(SwingMaxWeightG, SwingLineShare * lineLimit);
            CanNet = InNetReach && tight && _netCooldownTicks == 0 && Phase == FishPhase.Rest
                     && Stamina <= NetMaxStamina && RodAngle >= NetMinAngle;
            NetBlock = !InNetReach ? NetBlock.OutOfReach
                : _netCooldownTicks != 0 ? NetBlock.Cooldown
                : !tight ? NetBlock.Slack
                : RodAngle < NetMinAngle ? NetBlock.RodLow
                : Stamina > NetMaxStamina ? NetBlock.FishStrong
                : Phase != FishPhase.Rest ? NetBlock.FishFighting
                : NetBlock.None;
            HeadUp = CanNet || (Phase == FishPhase.Rest && RodAngle >= HeadUpAngle && Stamina < HeadUpStamina
                                && FishDistanceCm <= HeadUpDistanceCm);
        }

        private void Judge(double drag)
        {
            double lineLimit = _tackle.LineBreakingLoadG * (1.0 - LineWear);
            if (PeakTensionG > lineLimit)
            {
                FightCause cause;
                if (_breakawayTicks > 0 && drag > 0.75 * _tackle.LineBreakingLoadG)
                    cause = FightCause.DragTooTight;
                else if (LineWear >= 0.15)
                    cause = FightCause.WornByCranking;
                else if (_shockTicks > 0 && RodAngle < 0.3)
                    cause = FightCause.ShockRodLow;
                else
                    cause = FightCause.Overload;
                Finish(FightOutcome.LineBroke, cause);
                _report.PeakG = PeakTensionG;
                _report.LimitG = lineLimit;
                Events |= FightEvents.Broke;
            }
            else if (RodLoadG > _tackle.RodMaxLoadG)
            {
                Finish(FightOutcome.RodBroke, RodAngle >= 0.3 ? FightCause.RodHighTightDrag : FightCause.Overload);
                _report.PeakG = RodLoadG;
                _report.LimitG = _tackle.RodMaxLoadG;
                Events |= FightEvents.Broke;
            }
            else if (LineOutCm > _tackle.SpoolCapacityCm)
            {
                Finish(FightOutcome.SpoolEmpty, FightCause.SpoolEmptyDragLoose);
            }
            else if (HookHold <= 0)
            {
                Finish(FightOutcome.Escaped, _lastHookCause);
            }
        }

        private void Land(FightLanding landing)
        {
            Outcome = FightOutcome.Landed;
            Events |= FightEvents.Landed;
            if (landing == FightLanding.Swung)
                Events |= FightEvents.Swung;
            _report.Landing = landing;
            _report.Outcome = Outcome;
            _report.Cause = FightCause.None;
            _report.ElapsedSeconds = ElapsedSeconds;
            _report.Wear = LineWear;
            _report.Phase = Phase;
            _report.RodAngle = RodAngle;
        }

        private void Finish(FightOutcome outcome, FightCause cause)
        {
            Outcome = outcome;
            _report.Outcome = outcome;
            _report.Cause = cause;
        }

        private void Record(double drag)
        {
            var r = _report;
            r.Record(new FightSample { TensionG = TensionG, PeakG = PeakTensionG, DragG = drag, LimitG = WeakestLinkG });
            double share = WeakestLinkG > 0 ? PeakTensionG / WeakestLinkG : 0.0;
            if (share > r.MaxLoadShare)
                r.MaxLoadShare = share;
            if (Outcome == FightOutcome.InProgress && PeakTensionG > r.PeakG)
            {
                r.PeakG = PeakTensionG;
                r.LimitG = WeakestLinkG;
            }

            r.ElapsedSeconds = ElapsedSeconds;
            r.Wear = LineWear;
            r.Phase = Phase;
            r.RodAngle = RodAngle;
        }

        // ---------- Вспомогательное ----------

        private int Ticks(double seconds) => (int)Math.Floor(seconds / _dt + 0.5);

        /// <summary>Сторона ухода в сторону для фазы: целочисленный хеш visualSeed и номера фазы, без IRandom.</summary>
        private int Side(int phaseIndex)
        {
            unchecked
            {
                uint h = _visualSeed ^ ((uint)phaseIndex * 0x9E3779B9u);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (h & 1u) != 0 ? 1 : -1;
            }
        }

        private static void StyleTable(string style, out FishPhase[] phases, out double[] weights,
            out double slackShakePerSec, out double shakeHz, out double shakeAmplitude)
        {
            switch (style)
            {
                case "jerky":
                    phases = new[] { FishPhase.Run, FishPhase.Jerk };
                    weights = new[] { 0.4, 0.6 };
                    slackShakePerSec = 0.5;
                    shakeHz = 3.0;
                    shakeAmplitude = 0.3;
                    break;
                case "heavy":
                    phases = new[] { FishPhase.Dive, FishPhase.Run };
                    weights = new[] { 0.7, 0.3 };
                    slackShakePerSec = 0.3;
                    shakeHz = 0.8;
                    shakeAmplitude = 0.1;
                    break;
                case "deep":
                    phases = new[] { FishPhase.Dive, FishPhase.Run };
                    weights = new[] { 0.6, 0.4 };
                    slackShakePerSec = 0.4;
                    shakeHz = 1.2;
                    shakeAmplitude = 0.2;
                    break;
                case "jumps":
                    phases = new[] { FishPhase.Run, FishPhase.Jump, FishPhase.Jerk };
                    weights = new[] { 0.5, 0.35, 0.15 };
                    slackShakePerSec = 0.6;
                    shakeHz = 1.8;
                    shakeAmplitude = 0.4;
                    break;
                default:
                    phases = new[] { FishPhase.Run, FishPhase.Jerk };
                    weights = new[] { 0.8, 0.2 };
                    slackShakePerSec = 0.35;
                    shakeHz = 2.5;
                    shakeAmplitude = 0.15;
                    break;
            }
        }
    }
}
