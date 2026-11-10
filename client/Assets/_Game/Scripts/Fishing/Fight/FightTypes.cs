using System;

namespace Fishing.Core
{
    /// <summary>Что игрок делает в этот шаг: две кнопки, подсачек и положение фрикциона.</summary>
    public struct FightInput
    {
        /// <summary>«Подмотка» — крутить катушку (мотор катушки).</summary>
        public bool Reel;

        /// <summary>«Подтянуть» — поднимать удилище (мотор подъёма); при CanSwing — выудить рыбу.</summary>
        public bool Lift;

        /// <summary>«Подсачек» — одно нажатие: true ровно на один шаг (фронт кнопки).</summary>
        public bool Net;

        /// <summary>Фрикцион: при каком натяжении катушка отдаёт леску, граммы.</summary>
        public int DragG;
    }

    public enum FightOutcome
    {
        InProgress,
        Landed,
        LineBroke,
        RodBroke,
        SpoolEmpty,
        Escaped,
    }

    /// <summary>
    /// Фаза поведения рыбы: отдых, уход, рывок, уход в глубину, свечка, бросок от берега.
    /// Порядок значений не менять: по нему индексируются таблицы модели и строки HUD.
    /// </summary>
    public enum FishPhase
    {
        Rest,
        Run,
        Jerk,
        Dive,
        Jump,
        Surge,
    }

    /// <summary>
    /// Почему подсачек сейчас не возьмёт рыбу (FightModel.NetBlock). Причины идут в том порядке, в каком их
    /// стоит устранять игроку; None — можно брать (CanNet).
    /// </summary>
    public enum NetBlock
    {
        None,
        /// <summary>Рыба дальше, чем достаёт подсачек.</summary>
        OutOfReach,
        /// <summary>Только что промахнулся подсачеком: пауза перед новой попыткой.</summary>
        Cooldown,
        /// <summary>Леска провисла: рыбу нечем подвести.</summary>
        Slack,
        /// <summary>Удилище опущено: голову рыбы надо поднять к поверхности.</summary>
        RodLow,
        /// <summary>Рыба ещё сильна: её надо утомить.</summary>
        FishStrong,
        /// <summary>Рыба сейчас рвётся или уходит: брать её можно только в паузе.</summary>
        FishFighting,
    }

    /// <summary>Что случилось за шаг. Несколько флагов за один шаг — нормально.</summary>
    [Flags]
    public enum FightEvents
    {
        None = 0,
        /// <summary>Первый шаг вываживания: подсечка состоялась.</summary>
        HookSet = 1 << 0,
        /// <summary>Началась новая фаза (Phase уже новая).</summary>
        PhaseStart = 1 << 1,
        /// <summary>Пошёл телеграф следующей фазы (TelegraphProgress стал больше 0).</summary>
        TelegraphStart = 1 << 2,
        /// <summary>Фрикцион сорвался с места (трогание), в натяжение добавлен удар 0,2 × фрикцион.</summary>
        Breakaway = 1 << 3,
        /// <summary>Удар от рыбы: начало рывка, ухода в глубину, броска или вершина свечки на натянутой леске.</summary>
        Shock = 1 << 4,
        /// <summary>Вершина свечки (средний шаг фазы Jump).</summary>
        JumpApex = 1 << 5,
        /// <summary>Крючок держит хуже: HookHold уменьшился в этот шаг.</summary>
        HookLoss = 1 << 6,
        /// <summary>Начался бросок (Surge).</summary>
        SurgeStart = 1 << 7,
        /// <summary>Подсачек задел неготовую рыбу.</summary>
        NetBump = 1 << 8,
        /// <summary>Рыбу выудили махом удилища.</summary>
        Swung = 1 << 9,
        /// <summary>Рыба поймана (махом или подсачеком).</summary>
        Landed = 1 << 10,
        /// <summary>Обрыв лески или поломка удилища.</summary>
        Broke = 1 << 11,
    }

    /// <summary>Снасть в том виде, в каком она нужна вываживанию. Граммы и сантиметры.</summary>
    public sealed class FightTackle
    {
        public int RodMaxLoadG;
        public double RodFlex;
        public double RodReachCm;

        /// <summary>Сила подъёма удилища: при такой нагрузке «Подтянуть» встаёт, г. Обычно 0,6 × RodMaxLoadG.</summary>
        public double RodPowerG;

        public int LineBreakingLoadG;
        public int ReelMaxDragG;

        /// <summary>Скорость подмотки на вываживании без нагрузки, см/с (C0).</summary>
        public double ReelFightSpeedCmPerSec;

        /// <summary>Сила катушки: при такой нагрузке подмотка падает до 10% скорости, г (Tr).</summary>
        public double ReelPowerG;

        public double SpoolCapacityCm;

        /// <summary>
        /// Фрикцион, который ставится сам при подсечке: floor50(0,6 × min(леска, удилище / 1,3)),
        /// не больше фрикциона катушки.
        /// </summary>
        public int AutoDragG
        {
            get
            {
                double weakest = Math.Min(LineBreakingLoadG, RodMaxLoadG / 1.3);
                int drag = (int)(Math.Floor(0.6 * weakest / 50.0) * 50.0);
                return Math.Max(0, Math.Min(drag, ReelMaxDragG));
            }
        }

        /// <summary>Подъём удилища выбирает примерно 60% его длины; сила подъёма — 60% предельной нагрузки.</summary>
        public static FightTackle From(RodData rod, ReelData reel, LineData line)
        {
            return new FightTackle
            {
                RodMaxLoadG = rod.maxLoadG,
                RodFlex = rod.flex,
                RodReachCm = rod.lengthCm * 0.6,
                RodPowerG = rod.maxLoadG * 0.6,
                LineBreakingLoadG = line.breakingLoadG,
                ReelMaxDragG = reel.maxDragG,
                ReelFightSpeedCmPerSec = reel.fightSpeedCmPerSec,
                ReelPowerG = reel.powerG,
                SpoolCapacityCm = reel.spoolCapacityM * 100.0,
            };
        }
    }

    /// <summary>
    /// Общие настройки вываживания. Здесь только ручки баланса из спецификации (docs/specs/fight-v2.md, §3.7)
    /// и шаг времени; остальные числа — константы модели. Подобраны прогонами; меняются вместе с тестами.
    /// </summary>
    public sealed class FightSettings
    {
        public double TickSeconds = 0.05;

        /// <summary>Сопротивление тела рыбы: b = BodyDragFactor × F / V, г на см/с.</summary>
        public double BodyDragFactor = 0.7;

        /// <summary>
        /// Запас сил: St = StaminaBaseSeconds + StaminaPerPoint × stamina + StaminaPerKg × кг.
        /// В спецификации 3 + 1,5 + 1,5: с ними щука 4 кг на технике не укладывается в 60–120 с.
        /// Основа 2 (а не 3) — после того как трогание фрикциона перестало складываться с ударом рыбы,
        /// «просто кручу» на щуке 4 кг держится только на перекруте, и катушке спиннинга пришлось дать
        /// меньше силы (Tr 1600); без ускорения утомления техника на щуке 4 кг выходит за 120 с.
        /// </summary>
        public double StaminaBaseSeconds = 2.0;
        public double StaminaPerPoint = 1.0;
        public double StaminaPerKg = 0.75;

        /// <summary>
        /// Доля тяги рыбы по фазам (PF), по порядку FishPhase: Rest, Run, Jerk, Dive, Jump, Surge.
        /// В спецификации 0,15; 1,0; 1,6; 1,25; −0,5; 1,4. Пауза слабее — быстрее подтягивание тяжёлой рыбы;
        /// уход слабее — щука 4 кг меньше уводит леску; свечка резче — одной катушкой её не догнать.
        /// </summary>
        public double[] PhaseForce = { 0.1, 0.9, 1.6, 1.25, -0.7, 1.4 };

        /// <summary>
        /// Сила ударов (k): начало рывка, ухода в глубину, вершина свечки, начало броска.
        /// Рывок 0,35 вместо 0,4 из спецификации: на 0,30 автофрикцион 2750 г, а при удилище, поднятом
        /// на рывок (как учит техника), предел удилища 6000 / 1,6 = 3750 г; удар свежей щуки 7 кг при k = 0,4
        /// (≈1180 г) ломал удилище, при 0,35 (≈1030 г) — нет.
        /// </summary>
        public double JerkShock = 0.35;
        public double DiveShock = 0.25;
        public double JumpShock = 0.35;
        public double SurgeShock = 0.3;
    }
}
