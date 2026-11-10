namespace Fishing.Core
{
    /// <summary>
    /// Генератор Mulberry32: быстрый, с 32-битным состоянием и одинаковым результатом
    /// в C# и в TypeScript (там умножение через Math.imul). Эталонные значения для
    /// сверки двух реализаций — в SeededRandomTests.
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        private uint _state;

        public SeededRandom(uint seed)
        {
            _state = seed;
        }

        /// <summary>Текущее состояние: его можно сохранить и потом продолжить с того же места.</summary>
        public uint State => _state;

        public uint NextUInt()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

        public double NextDouble()
        {
            return NextUInt() / 4294967296.0;
        }
    }
}
