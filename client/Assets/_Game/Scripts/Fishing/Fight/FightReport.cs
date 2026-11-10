using System;

namespace Fishing.Core
{
    /// <summary>Почему вываживание кончилось неудачей — для карточки проигрыша и подсказки «Как надо».</summary>
    public enum FightCause
    {
        None,
        /// <summary>Леску перекрутил и ослабил, подматывая в пробуксовку фрикциона.</summary>
        WornByCranking,
        /// <summary>Фрикцион зажат: удар трогания фрикциона порвал леску.</summary>
        DragTooTight,
        /// <summary>Удар рыбы пришёлся на опущенное удилище.</summary>
        ShockRodLow,
        /// <summary>Удилище поднято, фрикцион зажат — сломалось удилище.</summary>
        RodHighTightDrag,
        /// <summary>Рыба просто сильнее снасти.</summary>
        Overload,
        /// <summary>Фрикцион слишком слабый — рыба смотала всю леску.</summary>
        SpoolEmptyDragLoose,
        /// <summary>Рыба стряхнула крючок на слабине.</summary>
        SlackShaking,
        /// <summary>Свечка на слабой леске.</summary>
        JumpOnSlack,
        /// <summary>Подсачек задел неготовую рыбу.</summary>
        NetBump,
        /// <summary>
        /// Пытался выудить свежую рыбу. Модель эту причину больше не выдаёт: CanSwing требует усталой рыбы,
        /// и «Подтянуть» у свежей рыбы просто подтягивает. Значение оставлено, чтобы не сдвигать номера причин.
        /// </summary>
        SwingFresh,
        /// <summary>Долго ничего не делал — рыба сошла.</summary>
        Idle,
    }

    /// <summary>Как рыбу вытащили.</summary>
    public enum FightLanding
    {
        None,
        Swung,
        Netted,
    }

    /// <summary>Один шаг истории натяжения для графика на карточке.</summary>
    public struct FightSample
    {
        public double TensionG;
        public double PeakG;
        public double DragG;
        /// <summary>Самое слабое звено в этот шаг (WeakestLinkG).</summary>
        public double LimitG;
    }

    /// <summary>
    /// Итог вываживания: причина, числа и история последних 200 шагов. Обновляется каждый шаг,
    /// окончательный — когда Outcome перестал быть InProgress. Снаружи только читается.
    /// </summary>
    public sealed class FightReport
    {
        public const int HistoryCapacity = 200;

        private readonly FightSample[] _history = new FightSample[HistoryCapacity];
        private int _historyStart;

        public FightOutcome Outcome { get; internal set; }
        public FightCause Cause { get; internal set; }
        public FightLanding Landing { get; internal set; }
        public double ElapsedSeconds { get; internal set; }

        /// <summary>
        /// Нагрузка: при обрыве или поломке — та, что порвала (для удилища — нагрузка на удилище),
        /// иначе — наибольшее натяжение с ударами за вываживание, г.
        /// </summary>
        public double PeakG { get; internal set; }

        /// <summary>Предел, с которым сравнивалась PeakG: прочность лески с износом или удилища, г.</summary>
        public double LimitG { get; internal set; }

        /// <summary>Наибольшая доля от самого слабого звена за вываживание: 1 — на пределе.</summary>
        public double MaxLoadShare { get; internal set; }

        /// <summary>Износ лески к концу.</summary>
        public double Wear { get; internal set; }

        /// <summary>Сколько секунд крутил катушку, пока фрикцион пробуксовывал.</summary>
        public double SecondsCrankedOnSlip { get; internal set; }

        public FishPhase Phase { get; internal set; }
        public double RodAngle { get; internal set; }

        /// <summary>Сколько было пауз (фаз Rest), включая стартовую.</summary>
        public int RestsTotal { get; internal set; }

        /// <summary>В скольких паузах игрок подтягивал на натянутой леске.</summary>
        public int RestsLifted { get; internal set; }

        /// <summary>Сколько шагов истории записано (не больше HistoryCapacity).</summary>
        public int HistoryCount { get; private set; }

        /// <summary>Шаг истории: 0 — самый старый из сохранённых, HistoryCount − 1 — последний.</summary>
        public FightSample History(int index)
        {
            if (index < 0 || index >= HistoryCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _history[(_historyStart + index) % HistoryCapacity];
        }

        internal void Record(in FightSample sample)
        {
            if (HistoryCount < HistoryCapacity)
            {
                _history[HistoryCount] = sample;
                HistoryCount++;
            }
            else
            {
                _history[_historyStart] = sample;
                _historyStart = (_historyStart + 1) % HistoryCapacity;
            }
        }
    }
}
