using System;

namespace Fishing.Core.Audio
{
    /// <summary>Зерно, которое надо начать: что, какой вариант, через сколько сэмплов от начала блока, громкость, высота.</summary>
    public struct GrainStart
    {
        public FightGrain Kind;
        public int Variant;
        public int Offset;
        public float Gain;
        public float Pitch;
    }

    /// <summary>
    /// Расписание зёрен вываживания с точностью до сэмпла (звуковой поток, внутри FightAudioMixer). Получает из снимка
    /// скорости (ротор, пробуксовка) и нагрузки, сам копит пройденную леску и ставит зерно ровно в тот сэмпл, где
    /// счётчик перешёл очередную метку, — от частоты кадров ничего не зависит.
    ///
    /// | Зерно        | Когда                                                        | Громкость / высота                        |
    /// | ротор        | каждые 14 см (7,9/с при 110 см/с, 18/с при 250), позже на 0–8% периода | 0,10·(0,6+0,4s) ±1,5 дБ; (0,97+0,08s)(1−0,15·нагрузка) ±1,2% |
    /// | «ток»        | каждые 85 см, первый на 40 см                                | 0,08 ±1,5 дБ; (1−0,1·нагрузка) ±1%        |
    /// | натуга       | нагрузка > 0,4 и крутят: 2→16 срывов/с (×0,4 при пробуксовке), интервалы ×0,5–2 | 0,14·(0,5+0,5k)·√накоплено ±1,5 дБ (×0,6 при пробуксовке), высота 0,86 — мягкий глухой стук, не треск |
    /// | трещотка     | каждые 1,2 см пробуксовки, позже на 0–10% периода             | 0,48, выше 15/с ×√(15/частота) и чаще тёмные; первые 3 серии +2 дБ |
    /// | треск лески  | леска натянута и LoadShare > 0,88: 2→14/с, интервалы ×0,5–2  | 0,3 ±2 дБ, высота ±3% (предупреждение, 1,2–2,5 кГц) |
    /// | скрип        | RodLoadShare > 0,75: раз в 0,9→0,35 с ±30%, позже на 0–50 мс | 0,30→0,60, громкий вариант при k > 0,5    |
    /// Подмотка намеренно тихая (10.11.2026: ротор 0,48 → 0,10, «ток» 0,14 → 0,08, натуга 0,24 → 0,14): на полной
    /// скорости она звучала на −14,5 дБFS и упиралась в лимитер, теперь около −23 дБFS.
    /// s = скорость ротора / C0. Скорости сглажены экспонентой 15 мс (модель меняет их ступеньками по 50 мс).
    /// Вариант = хеш номера события и зерна расписания: никогда не тот же, что в прошлый раз; одинаковые зерно расписания
    /// и параметры по блокам дают одинаковые зёрна. Счётчики обнуляются на подсечке, поэтому зритель с теми же кадрами
    /// слышит те же варианты в том же ритме — с точностью до одного звукового буфера в моменты смены скорости.
    /// Без выделений памяти: зёрна пишутся в массив вызывающего.
    /// </summary>
    public sealed class FightGrainScheduler
    {
        public const double RotorCm = 14.0;
        public const double HandleCm = 85.0;
        public const double HandleOffsetCm = 40.0;
        public const double DragCm = 1.2;

        private const double RateSmoothSeconds = 0.015;
        private const float RotorLevel = 0.10f;
        private const float TockLevel = 0.08f;
        private const float StrainLevel = 0.14f;
        private const float DragLevel = 0.48f;
        private const double DragSoftRate = 15.0;
        private const double DragRunGapSeconds = 0.25;
        private const float CrackleLevel = 0.3f;
        private const float CreakQuiet = 0.30f;
        private const float CreakLoud = 0.60f;
        private const double RotorJitter = 0.04;
        private const double DragJitter = 0.05;
        private const double MaxRotorDelay = 0.06;
        private const double MaxDragDelay = 0.03;
        private const double CreakSpread = 0.05;

        /// <summary>Больше зёрен одного вида на сэмпл не бывает (5000 см/с на 8 кГц — 0,6 см за сэмпл); дальше — счётчик
        /// догоняет метку без зёрен, чтобы испорченная скорость не превратила звуковой поток в бесконечный цикл.</summary>
        private const int MaxGrainsPerSample = 4;

        private readonly uint _seed;
        private double _v, _slip;
        private double _rotorCm;
        private long _rotorIndex, _handleIndex;
        private double _strainPhase, _strainNext = 1;
        private long _strainIndex;
        private double _slipCm;
        private long _slipIndex;
        private double _sinceSlip = 10;
        private int _runTicks;
        private double _cracklePhase, _crackleNext = 1;
        private long _crackleIndex;
        private double _creakTimer;
        private long _creakIndex;
        private int _lastRotor = -1, _lastTock = -1, _lastDrag = -1, _lastStrain = -1, _lastCrackle = -1, _lastCreak = -1;

        /// <summary>Сколько зёрен каждого вида поставлено (индекс — FightGrain).</summary>
        public readonly long[] Counts = new long[6];

        /// <summary>Сколько зёрен не влезло в массив вызывающего.</summary>
        public long Dropped { get; private set; }

        /// <summary>Пройдено лески ротором с подсечки, см (для «волны раскладчика» укладки).</summary>
        public double RotorDistanceCm => _rotorCm;

        public FightGrainScheduler(uint seed)
        {
            _seed = seed;
            Reset();
        }

        /// <summary>Подсечка: все счётчики с нуля.</summary>
        public void Reset()
        {
            _v = 0;
            _slip = 0;
            _rotorCm = 0;
            _rotorIndex = 0;
            _handleIndex = 0;
            _strainPhase = 0;
            _strainIndex = 0;
            _strainNext = GapOf(0, 0);
            _slipCm = 0;
            _slipIndex = 0;
            _sinceSlip = 10;
            _runTicks = 0;
            _cracklePhase = 0;
            _crackleIndex = 0;
            _crackleNext = GapOf(0, 100000);
            _creakTimer = 0;
            _creakIndex = 0;
            _lastRotor = _lastTock = _lastDrag = _lastStrain = _lastCrackle = _lastCreak = -1;
        }

        /// <summary>Фрикцион тронулся: щелчок в начале блока, а следующий — через полные 1,2 см (если серия новая).</summary>
        public void Breakaway(GrainStart[] output, ref int count)
        {
            if (_runTicks > 0 && _sinceSlip < DragRunGapSeconds)
                return;
            _runTicks = 0;
            _sinceSlip = 0;
            _slipCm = _slipIndex * DragCm;
            EmitDrag(0, Math.Max(1, _slip / DragCm), 1, output, ref count);
        }

        /// <summary>
        /// Блок из frames сэмплов на частоте rate: сдвинуть счётчики и дописать зёрна этого блока в output (Offset — от
        /// начала блока, может выходить за блок на задержку разброса).
        /// </summary>
        public void Advance(in FightAudioParams p, int frames, int rate, GrainStart[] output, ref int count)
        {
            if (rate <= 0 || frames <= 0)
                return;
            double dt = 1.0 / rate;
            double a = 1 - Math.Exp(-dt / RateSmoothSeconds);
            bool fight = p.Fight;
            // Скорости — без NaN и бесконечностей и не выше потолка: иначе счётчик лески не догонит метку никогда.
            double vTarget = fight ? SafeMath.Clamp(p.RotorCmPerSec, 0f, FightAudioParams.MaxSpeedCmPerSec) : 0;
            double slipTarget = fight && p.Slipping ? SafeMath.Clamp(p.SlipCmPerSec, 0f, FightAudioParams.MaxSpeedCmPerSec) : 0;
            double c0 = p.C0 > 0 && p.C0 <= FightAudioParams.MaxSpeedCmPerSec ? p.C0 : 110;
            double load = SafeMath.Clamp(p.ReelLoad, 0f, 1f);
            double loadShare = SafeMath.Clamp(p.LoadShare, 0f, 4f);
            double rodShare = SafeMath.Clamp(p.RodLoadShare, 0f, 4f);
            bool strain = fight && vTarget > 0 && load > 0.4;
            double strainK = Math.Min(1, (load - 0.4) / 0.6);
            double strainRate = (2 + 14 * strainK) * (p.Slipping ? 0.4 : 1.0);
            bool crackle = fight && p.Tight && loadShare > 0.88;
            double crackleRate = 2 + 12 * Math.Min(1, (loadShare - 0.88) / 0.12);
            bool creak = fight && rodShare > 0.75;

            if (!strain)
            {
                _strainPhase = 0;
                _strainNext = GapOf(_strainIndex, 0);
            }
            if (!crackle)
            {
                _cracklePhase = 0;
                _crackleNext = GapOf(_crackleIndex, 100000);
            }

            for (int i = 0; i < frames; i++)
            {
                _v += (vTarget - _v) * a;
                if (vTarget <= 0 && _v < 0.05)
                    _v = 0;
                _slip += (slipTarget - _slip) * a;
                if (slipTarget <= 0 && _slip < 0.05)
                    _slip = 0;

                if (_v > 0)
                {
                    _rotorCm += _v * dt;
                    int guard = 0;
                    while ((_rotorIndex + 1) * RotorCm <= _rotorCm)
                    {
                        if (++guard > MaxGrainsPerSample)
                        {
                            _rotorIndex = (long)Math.Floor(_rotorCm / RotorCm);
                            break;
                        }
                        _rotorIndex++;
                        EmitRotor(i, rate, c0, load, output, ref count);
                    }
                    guard = 0;
                    while (HandleOffsetCm + (_handleIndex + 1) * HandleCm <= _rotorCm)
                    {
                        if (++guard > MaxGrainsPerSample)
                        {
                            _handleIndex = Math.Max(_handleIndex, (long)Math.Floor((_rotorCm - HandleOffsetCm) / HandleCm));
                            break;
                        }
                        _handleIndex++;
                        EmitTock(i, load, output, ref count);
                    }
                }

                if (strain)
                {
                    _strainPhase += strainRate * dt;
                    int guard = 0;
                    while (_strainPhase >= _strainNext)
                    {
                        if (++guard > MaxGrainsPerSample)
                        {
                            _strainNext = _strainPhase + GapOf(_strainIndex, 0);
                            break;
                        }
                        _strainIndex++;
                        uint h = H((uint)_strainIndex * 0x85EBCA6Bu + 5u);
                        double built = Math.Sqrt(Math.Max(0.25, Math.Min(2.0, GapOf(_strainIndex - 1, 0))) / 2);
                        bool big = built > 0.8 && strainK > 0.7;
                        double gain = StrainLevel * (0.5 + 0.5 * strainK) * built * Db(1.5 * Signed(h >> 10)) * (p.Slipping ? 0.6 : 1);
                        double pitch = 0.86 * (1 - 0.06 * strainK) * (1 + 0.03 * Signed(h >> 20));
                        int variant = Pick(h, FightSoundBank.StrainVariants, ref _lastStrain) + (big ? FightSoundBank.StrainVariants : 0);
                        Emit(FightGrain.Strain, variant, i, gain, pitch, output, ref count);
                        _strainNext += GapOf(_strainIndex, 0);
                    }
                }

                if (_slip > 0)
                {
                    if (_sinceSlip >= DragRunGapSeconds)
                        _runTicks = 0;
                    _sinceSlip = 0;
                    _slipCm += _slip * dt;
                    double ticksPerSecond = Math.Max(1, _slip / DragCm);
                    int guard = 0;
                    while ((_slipIndex + 1) * DragCm <= _slipCm)
                    {
                        if (++guard > MaxGrainsPerSample)
                        {
                            _slipIndex = (long)Math.Floor(_slipCm / DragCm);
                            break;
                        }
                        _slipIndex++;
                        uint h = H((uint)_slipIndex * 0x9E3779B9u + 3u);
                        double delay = Math.Min(MaxDragDelay, (DragJitter + DragJitter * Signed(h)) / ticksPerSecond);
                        EmitDrag(i + (int)Math.Round(delay * rate), ticksPerSecond, 0, output, ref count);
                    }
                }
                else
                {
                    _sinceSlip += dt;
                }

                if (crackle)
                {
                    _cracklePhase += crackleRate * dt;
                    int guard = 0;
                    while (_cracklePhase >= _crackleNext)
                    {
                        if (++guard > MaxGrainsPerSample)
                        {
                            _crackleNext = _cracklePhase + GapOf(_crackleIndex, 100000);
                            break;
                        }
                        _crackleIndex++;
                        uint h = H((uint)_crackleIndex * 0xC2B2AE35u + 9u);
                        double gain = CrackleLevel * Db(2 * Signed(h >> 10));
                        Emit(FightGrain.Crackle, Pick(h, FightSoundBank.CrackleVariants, ref _lastCrackle), i, gain, 1 + 0.03 * Signed(h >> 20), output, ref count);
                        _crackleNext += GapOf(_crackleIndex, 100000);
                    }
                }

                if (fight)
                {
                    _creakTimer -= dt;
                    if (creak && _creakTimer <= 0)
                    {
                        _creakIndex++;
                        uint h = H((uint)_creakIndex * 0x165667B1u + 13u);
                        double k = Math.Min(1, (rodShare - 0.75) / 0.25);
                        bool loud = k > 0.5;
                        double gain = (CreakQuiet + (CreakLoud - CreakQuiet) * k) * Db(1.5 * Signed(h >> 10));
                        int variant = Pick(h, FightSoundBank.CreakVariants, ref _lastCreak) + (loud ? FightSoundBank.CreakVariants : 0);
                        int offset = i + (int)Math.Round(Unit(h >> 3) * CreakSpread * rate);
                        Emit(FightGrain.Creak, variant, offset, gain, 1 + 0.02 * Signed(h >> 20), output, ref count);
                        _creakTimer = (0.9 + (0.35 - 0.9) * k) * (0.7 + 0.6 * Unit(h >> 14));
                    }
                }
            }
        }

        private void EmitRotor(int i, int rate, double c0, double load, GrainStart[] output, ref int count)
        {
            uint h = H((uint)_rotorIndex * 2654435761u + 11u);
            double s = _v / c0;
            double period = RotorCm / Math.Max(1e-3, _v);
            double delay = Math.Min(MaxRotorDelay, period * (RotorJitter + RotorJitter * Signed(h)));
            double gain = RotorLevel * (0.6 + 0.4 * Math.Min(1, s)) * Db(1.5 * Signed(h >> 10));
            double pitch = (0.97 + 0.08 * Math.Min(1.2, s)) * (1 - 0.15 * load) * (1 + 0.012 * Signed(h >> 20));
            Emit(FightGrain.Rotor, Pick(h, FightSoundBank.RotorVariants, ref _lastRotor), i + (int)Math.Round(delay * rate), gain, pitch, output, ref count);
        }

        private void EmitTock(int i, double load, GrainStart[] output, ref int count)
        {
            uint h = H((uint)_handleIndex * 40503u + 7u);
            double gain = TockLevel * Db(1.5 * Signed(h >> 10));
            double pitch = (1 - 0.1 * load) * (1 + 0.01 * Signed(h >> 20));
            Emit(FightGrain.Tock, Pick(h, FightSoundBank.TockVariants, ref _lastTock), i, gain, pitch, output, ref count);
        }

        private void EmitDrag(int offset, double ticksPerSecond, int extra, GrainStart[] output, ref int count)
        {
            uint h = H((uint)_slipIndex * 0x9E3779B9u + 3u + (uint)_runTicks * 977u + (uint)extra * 0x51ED27u);
            double gain = DragLevel * (ticksPerSecond <= DragSoftRate ? 1 : Math.Sqrt(DragSoftRate / ticksPerSecond));
            if (_runTicks < 3)
                gain *= Db(2);
            gain *= Db(1.5 * Signed(h >> 10));
            double darkChance = Math.Max(0, Math.Min(1, (ticksPerSecond - DragSoftRate) / 45));
            bool dark = Unit(h >> 4) < darkChance;
            int variant = Pick(h >> 7, FightSoundBank.DragVariants, ref _lastDrag) + (dark ? FightSoundBank.DragVariants : 0);
            Emit(FightGrain.Drag, variant, offset, gain, 1 + 0.02 * Signed(h >> 20), output, ref count);
            _runTicks++;
        }

        private void Emit(FightGrain kind, int variant, int offset, double gain, double pitch, GrainStart[] output, ref int count)
        {
            if (gain <= 0)
                return;
            Counts[(int)kind]++;
            if (output == null || count >= output.Length)
            {
                Dropped++;
                return;
            }
            output[count++] = new GrainStart { Kind = kind, Variant = variant, Offset = offset, Gain = (float)gain, Pitch = (float)pitch };
        }

        /// <summary>Интервал до следующего срыва в «средних интервалах»: 0,5–2 (лог-равномерно) по хешу номера.</summary>
        private double GapOf(long index, uint salt)
        {
            uint h = H((uint)index * 0x27D4EB2Fu + 3u + salt * 0x165667B1u);
            return Math.Pow(2, 2 * Unit(h) - 1);
        }

        private uint H(uint x) => SynthKit.Hash(x ^ _seed);

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

    }
}
