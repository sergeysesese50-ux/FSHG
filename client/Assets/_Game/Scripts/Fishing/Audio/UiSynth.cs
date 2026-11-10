using System;
using static Fishing.Core.Audio.SynthKit;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Короткие звуки карточки улова (моно, SampleRate):
    /// - Keep — рыба в садке: мягкий мокрый шлепок, плеск сетки и два тихих тёплых «динь» (награда);
    /// - Release — отпустил: «бульк» — крупный пузырь, за ним два мельче, короткий всплеск и капли;
    /// - Coin — продажа: монета звякает о монеты и подпрыгивает, под звоном тихий тёплый «динь».
    /// </summary>
    public static class UiSynth
    {
        /// <summary>Частота клипов, Гц — для того, кто их проигрывает (SynthKit снаружи сборки не виден).</summary>
        public const int SampleRate = SynthKit.SampleRate;

        public static float[] Keep(uint seed = 0x4EE90001u)
        {
            var r = new Rng(seed);
            var s = new float[Samples(1.0)];

            // Шлепок: глухой низкий удар о воду в садке.
            var thud = Modal(Samples(0.3), 0, new[] { M(150, 0.05, 1.0), M(235, 0.035, 0.55), M(410, 0.02, 0.25) }, 4.0, r.NextSeed());
            Normalize(thud, 0.9f);
            Mix(s, thud, Samples(0.005), 0.42);
            AddBubble(s, Samples(0.03), 7.0, 0.30);
            AddBubble(s, Samples(0.10), 4.0, 0.18);

            // Плеск сетки.
            int m = Samples(0.16);
            var band = new Svf(1500, 0.8);
            for (int i = 0; i < m; i++)
                s[Samples(0.02) + i] += (float)(0.10 * band.Band(r.S()) * Arch((double)i / m, 0.25));

            // Два тихих «динь»: квинта вверх, мягкая колотушка.
            var low = Modal(Samples(0.6), 0, new[] { M(1175, 0.20, 1.0), M(2350, 0.09, 0.22) }, 2.5, r.NextSeed());
            var high = Modal(Samples(0.7), 0, new[] { M(1760, 0.26, 1.0), M(3520, 0.10, 0.18) }, 2.5, r.NextSeed());
            Normalize(low, 0.9f);
            Normalize(high, 0.9f);
            Mix(s, low, Samples(0.16), 0.16);
            Mix(s, high, Samples(0.27), 0.18);

            AddRoom(s, -18);
            Fades(s, 0.001, 0.15);
            return s;
        }

        /// <summary>
        /// Щелчок шага настройки (спуск поплавка): короткий мягкий деревянный «ток» — один тон 1,3 кГц с быстрым
        /// затуханием и тихим обертоном. Высоту меняет тот, кто проигрывает (глубже — ниже).
        /// </summary>
        public static float[] Tick(uint seed = 0x71C40001u)
        {
            var s = Modal(Samples(0.09), 0, new[] { M(1300, 0.014, 1.0), M(2650, 0.007, 0.25), M(640, 0.010, 0.3) }, 1.5, seed);
            Fades(s, 0.0005, 0.03);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Сколько монет слышно в звуке продажи самое большее — дальше звук не удлиняется.</summary>
        public const int MaxCoins = 5;

        /// <summary>
        /// Продажа рыбы: монета падает на монеты. Одна рыба — один звон с двумя затихающими отскоками;
        /// несколько рыб (coins до MaxCoins) — монеты сыплются одна за другой, каждая своей высоты.
        /// Звон мягкий: основной тон 2,6 кГц, верхние призвуки тихие, чтобы не резало слух.
        /// </summary>
        public static float[] Coin(int coins = 1, uint seed = 0xC0190001u)
        {
            var r = new Rng(seed);
            int count = Math.Max(1, Math.Min(coins, MaxCoins));
            const double step = 0.085;
            double last = 0.004 + step * (count - 1);
            var s = new float[Samples(last + 0.75)];
            double[] pitches = { 1.0, 1.12, 0.94, 1.19, 1.06 };

            for (int k = 0; k < count; k++)
                AddClink(s, 0.004 + step * k + (k == 0 ? 0 : 0.012 * r.U()), pitches[k], 0.30, r.NextSeed());

            // Последняя монета подпрыгивает: два отскока, тише и чуть выше.
            double top = pitches[count - 1];
            AddClink(s, last + 0.072, top * 1.07, 0.16, r.NextSeed());
            AddClink(s, last + 0.122, top * 1.11, 0.08, r.NextSeed());

            // Тёплый «динь» под звоном — приятный хвост.
            var ding = Modal(Samples(0.6), 0, new[] { M(1568, 0.22, 1.0), M(3136, 0.09, 0.2) }, 2.5, r.NextSeed());
            Normalize(ding, 0.9f);
            Mix(s, ding, Samples(last + 0.01), 0.10);

            AddRoom(s, -20);
            Fades(s, 0.001, 0.15);
            Normalize(s, 0.32f);    // пик около −10 дБFS: звон лежит в 2–5 кГц, где ухо чувствительнее, — на 4 дБ ниже «в садок»
            return s;
        }

        /// <summary>Один звон монеты: четыре негармонических призвука, верхние гаснут первыми.</summary>
        private static void AddClink(float[] s, double atSeconds, double pitch, double gain, uint seed)
        {
            var clink = Modal(Samples(0.45), 0, new[]
            {
                M(2637 * pitch, 0.110, 1.0), M(4190 * pitch, 0.080, 0.55),
                M(6270 * pitch, 0.045, 0.26), M(8900 * pitch, 0.025, 0.10),
            }, 0.9, seed);
            Normalize(clink, 0.9f);
            Mix(s, clink, Samples(atSeconds), gain);
        }

        public static float[] Release(uint seed = 0x4E1E0001u)
        {
            var r = new Rng(seed);
            var s = new float[Samples(0.9)];

            // «Бульк»: пузыри от крупного к мелким.
            AddBubble(s, Samples(0.010), 12.0, 0.55);
            AddBubble(s, Samples(0.095), 6.5, 0.32);
            AddBubble(s, Samples(0.170), 4.0, 0.22);

            // Короткий мягкий всплеск вокруг первого пузыря.
            int m = Samples(0.22);
            var band = new Svf(620, 0.9);
            for (int i = 0; i < m; i++)
                s[i] += (float)(0.16 * band.Band(r.S()) * Arch((double)i / m, 0.18));

            // Капли вдогонку.
            for (int k = 0; k < 4; k++)
                AddBubble(s, Samples(0.26 + 0.07 * k + 0.03 * r.U()), r.Range(2.0, 3.2), 0.10 * (1 - 0.15 * k));

            AddRoom(s, -18);
            Fades(s, 0.001, 0.12);
            Scale(s, 0.6);          // вровень с «в садок»: пик около −6 дБFS
            return s;
        }
    }
}
