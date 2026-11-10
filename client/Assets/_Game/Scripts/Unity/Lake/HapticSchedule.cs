using System.Collections.Generic;

namespace Fishing.Unity
{
    /// <summary>
    /// Очередь импульсов вибрации с общим ограничением частоты (docs/specs/fight-v2.md, §6: не больше
    /// 15 импульсов в секунду). Без движка: время передаёт вызывающий (секунды, одни и те же часы).
    ///
    /// Правила:
    /// - Импульс ставится на момент времени: двойные импульсы и «каждые 0,4 с» — несколько записей.
    /// - За вызов TryTake выходит не больше одного импульса: на телефоне новый импульс обрывает текущий,
    ///   поэтому из созревших к этому кадру берётся самый важный (главный раньше фонового, потом длиннее
    ///   и сильнее), остальные созревшие отбрасываются.
    /// - Фоновый импульс (тики фрикциона и опасности) не обрывает идущий и не выходит, если за последнюю
    ///   секунду было 12 импульсов; главный (события) обрывает что угодно и упирается только в 15 — так
    ///   фон не съедает место у удара, обрыва и поимки.
    /// </summary>
    internal sealed class HapticSchedule
    {
        /// <summary>Общий предел, импульсов в секунду.</summary>
        public const int MaxPerSecond = 15;

        /// <summary>Предел для фоновых импульсов: остаток до MaxPerSecond оставлен главным.</summary>
        public const int BackgroundPerSecond = 12;

        private struct Pending
        {
            public float At;
            public int Ms;
            public int Amplitude;
            public bool Major;
        }

        private readonly List<Pending> _pending = new List<Pending>(16);
        private readonly float[] _fired = new float[MaxPerSecond];
        private int _firedNext;
        private float _busyUntil = float.NegativeInfinity;

        public HapticSchedule()
        {
            for (int i = 0; i < _fired.Length; i++)
                _fired[i] = float.NegativeInfinity;
        }

        /// <summary>Сколько импульсов ждёт своего времени.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Поставить импульс на момент at (секунды тех же часов, что в TryTake).</summary>
        /// <param name="major">Главный — событие (удар, телеграф, подсечка…); иначе фоновый тик.</param>
        public void Add(float at, int ms, int amplitude, bool major)
        {
            _pending.Add(new Pending { At = at, Ms = ms, Amplitude = amplitude, Major = major });
        }

        /// <summary>Забыть всё, что ещё не вышло (новое вываживание, обрыв, поимка).</summary>
        public void Clear() => _pending.Clear();

        /// <summary>Импульсов за последнюю секунду до now включительно.</summary>
        public int FiredInLastSecond(float now)
        {
            int count = 0;
            for (int i = 0; i < _fired.Length; i++)
                if (_fired[i] > now - 1f)
                    count++;
            return count;
        }

        /// <summary>Импульс, который надо дать сейчас; false — ничего (не пора, отброшен или упёрся в предел).</summary>
        public bool TryTake(float now, out int ms, out int amplitude)
        {
            ms = 0;
            amplitude = 0;
            int best = -1;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].At <= now && (best < 0 || Outranks(_pending[i], _pending[best])))
                    best = i;
            if (best < 0)
                return false;

            var pulse = _pending[best];
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].At <= now)
                    _pending.RemoveAt(i);

            if (!pulse.Major && now < _busyUntil)
                return false;
            if (FiredInLastSecond(now) >= (pulse.Major ? MaxPerSecond : BackgroundPerSecond))
                return false;

            _fired[_firedNext] = now;
            _firedNext = (_firedNext + 1) % _fired.Length;
            _busyUntil = now + pulse.Ms / 1000f;
            ms = pulse.Ms;
            amplitude = pulse.Amplitude;
            return true;
        }

        private static bool Outranks(in Pending a, in Pending b)
        {
            if (a.Major != b.Major)
                return a.Major;
            return a.Ms * a.Amplitude > b.Ms * b.Amplitude;
        }
    }
}
