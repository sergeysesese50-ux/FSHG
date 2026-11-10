namespace Fishing.Core
{
    /// <summary>
    /// Источник случайных чисел для логики рыбалки. Вся случайность идёт только через него,
    /// чтобы тесты были повторяемыми, а сервер мог воспроизвести результат по seed.
    /// </summary>
    public interface IRandom
    {
        /// <summary>Следующее 32-битное число.</summary>
        uint NextUInt();

        /// <summary>Следующее число в диапазоне [0, 1).</summary>
        double NextDouble();
    }
}
