using System;
using System.Globalization;

namespace Fishing.Unity
{
    /// <summary>
    /// Числа для HUD по-русски: запятая вместо точки, без культуры системы. Только чистые функции.
    /// Прототип: строки прямо в коде; в игре они уйдут в таблицы Localization.
    /// </summary>
    internal static class HudText
    {
        /// <summary>Знак минуса для подписей («−32%»): типографский, не дефис.</summary>
        public const string Minus = "−";

        /// <summary>Целое scaled с decimals знаками после запятой: (1226, 2) → «12,26», (−5, 1) → «−0,5».</summary>
        public static string Fixed(int scaled, int decimals)
        {
            if (decimals <= 0)
                return scaled < 0 ? Minus + (-scaled).ToString(CultureInfo.InvariantCulture) : scaled.ToString(CultureInfo.InvariantCulture);

            int pow = Pow10(decimals);
            bool negative = scaled < 0;
            int abs = negative ? -scaled : scaled;
            string whole = (abs / pow).ToString(CultureInfo.InvariantCulture);
            string fraction = (abs % pow).ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0');
            return (negative ? Minus : "") + whole + "," + fraction;
        }

        /// <summary>Значение, умноженное на 10^decimals и округлённое (половина — от нуля).</summary>
        public static int Scaled(double value, int decimals)
        {
            return (int)Math.Round(value * Pow10(decimals), MidpointRounding.AwayFromZero);
        }

        /// <summary>Граммы в килограммах с двумя знаками и единицей: 1260 → «1,26 кг».</summary>
        public static string Kg(double grams) => KgNumber(grams) + " кг";

        /// <summary>Граммы в килограммах с двумя знаками без единицы: 1220 → «1,22».</summary>
        public static string KgNumber(double grams) => Fixed(Scaled(grams / 1000.0, 2), 2);

        /// <summary>Доля в процентах: 0,317 → «32%».</summary>
        public static string Percent(double share) => Scaled(share * 100.0, 0).ToString(CultureInfo.InvariantCulture) + "%";

        /// <summary>Секунды целыми: «47 с», от минуты — «1 мин 12 с».</summary>
        public static string Seconds(double seconds)
        {
            int total = Math.Max(0, Scaled(seconds, 0));
            if (total < 60)
                return total.ToString(CultureInfo.InvariantCulture) + " с";
            int minutes = total / 60;
            int rest = total % 60;
            string head = minutes.ToString(CultureInfo.InvariantCulture) + " мин";
            return rest == 0 ? head : head + " " + rest.ToString(CultureInfo.InvariantCulture) + " с";
        }

        /// <summary>Склонение после числа: 1 пауза, 2 паузы, 5 пауз (one / few / many).</summary>
        public static string Plural(int n, string one, string few, string many)
        {
            int mod100 = Math.Abs(n) % 100;
            int mod10 = mod100 % 10;
            if (mod100 >= 11 && mod100 <= 14)
                return many;
            if (mod10 == 1)
                return one;
            return mod10 >= 2 && mod10 <= 4 ? few : many;
        }

        private static int Pow10(int decimals)
        {
            int pow = 1;
            for (int i = 0; i < decimals; i++)
                pow *= 10;
            return pow;
        }
    }

    /// <summary>
    /// Готовые строки вида «префикс + число + суффикс» по целому ключу (число × 10^decimals): каждая строка
    /// собирается один раз и дальше берётся из массива — без выделений памяти в кадре. Ключи вне массива
    /// собираются заново (редко: только при выходе за ожидаемый диапазон).
    /// </summary>
    internal sealed class TextCache
    {
        private readonly string[] _items;
        private readonly int _decimals;
        private readonly string _prefix;
        private readonly string _suffix;

        public TextCache(int size, int decimals, string prefix, string suffix)
        {
            _items = new string[size];
            _decimals = decimals;
            _prefix = prefix;
            _suffix = suffix;
        }

        public string Get(int scaled)
        {
            if (scaled < 0 || scaled >= _items.Length)
                return _prefix + HudText.Fixed(scaled, _decimals) + _suffix;
            return _items[scaled] ??= _prefix + HudText.Fixed(scaled, _decimals) + _suffix;
        }
    }
}
