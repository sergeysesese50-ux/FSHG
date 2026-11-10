using System;
using static Fishing.Core.Audio.SynthKit;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Фон озера, палитра «Натуральная»: постель утреннего озера (бесшовная петля), голоса птиц и сверчков отдельными
    /// клипами — их расставляет FightAudioMixer по своему расписанию (днём птицы, ночью сверчки), поэтому петля
    /// не повторяется узнаваемо каждые 64 с и ночью птицы молчат. Моно, 22050 Гц, детерминированно.
    /// </summary>
    public static class LakeSynth
    {
        public const int SampleRate = SynthKit.SampleRate;

        /// <summary>Сколько секунд у шва петли не начинается ни одно событие (плеск, пузырь, шорох): хвосты заворачиваются,
        /// а атаки остаются внутри — на стыке нет скачка громкости.</summary>
        public const double SeamGuardSeconds = 0.5;

        /// <summary>
        /// Спокойное озеро, бесшовная петля (по умолчанию 64 с). Ветра в ней нет — он в WindBed и зависит от силы ветра:
        /// - плеск: шум со срезом 420 Гц и медленными «накатами» — гладкая случайная кривая из синусов с целым
        ///   числом периодов на петлю (0,08–0,45 Гц, амплитуды по Рэлею), глубина ±40%, и набеги на берег раз в 1,6–4,5 с
        ///   (полоса 300–650 Гц, атака 60–120 мс, спад 150–350 мс, 1–3 пузыря);
        /// - редкие пузырьки у берега и далёкий всплеск рыбы.
        /// Всё, что выходит за конец, заворачивается в начало; фильтры — по кругу; в SeamGuardSeconds по обе стороны
        /// шва события не начинаются. scale — множитель уровня (BedScale): голоса птиц и сверчков умножаются на него,
        /// чтобы встать в тот же уровень, что в прототипе палитры (там птицы были внутри петли).
        ///
        /// Память: два массива длины петли — итог и один черновой слой, который переиспользуется для каждого слоя
        /// (сгенерировать → отфильтровать на месте → прибавить с весом); «накаты» и порывы — кривые SmoothCurve
        /// (только узлы сетки), шов поворачивается на месте. Пик ≈ 2 × 5,4 МБ на 64 с вместо ~70 МБ, а сэмплы те же
        /// до бита: слои прибавляются в том же порядке и с теми же весами, что сумма в прототипе.
        /// </summary>
        /// <summary>
        /// Множитель уровня фона. Раньше считался нормировкой пика всей петли (с ветром) к 0,7 и выходил 0,5446;
        /// закреплён числом, чтобы спокойное озеро и ветер по отдельности остались в прежних соотношениях с птицами.
        /// </summary>
        public const double BedScale = 0.5446;

        /// <summary>Доля ровного шума воды, которая остаётся в спокойном озере; остальное ушло в WindBed.</summary>
        private const double CalmWaterWeight = 0.12;

        public static float[] LakeBed(double seconds, out double scale)
        {
            int n = Samples(seconds);
            double from = SeamGuardSeconds, to = seconds - SeamGuardSeconds;
            var r = new Rng(0x1A4E0001u);

            // Плеск (вес 1) — сразу в итог.
            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (float)r.S();
            FilterCircular(s, 420, 0.707, Band.Low);
            FilterCircular(s, 70, 0.707, Band.High);
            Normalize(s, 1f);
            var swell = SmoothRandom(n, Math.Max(1, (int)Math.Round(seconds / 12)), Math.Max(2, (int)Math.Round(seconds / 2.2)), ref r);
            for (int i = 0; i < n; i++)
                s[i] *= (float)((1 + 0.4 * swell.At(i)) * CalmWaterWeight);

            // Набеги на берег (0,55).
            var layer = new float[n];
            double tt = from + r.Range(0.0, 1.0);
            while (tt < to)
            {
                AddSlosh(layer, Samples(tt), ref r);
                tt += r.Range(1.6, 4.5);
            }
            AddLayer(s, layer, 0.55f);

            // Пузырьки у берега (0,05).
            Array.Clear(layer, 0, n);
            double bt = from + r.Range(0.0, 1.5);
            while (bt < to)
            {
                AddBubble(layer, Samples(bt), r.Range(3, 9), r.Range(0.3, 1.0), true);
                bt += r.Range(1.0, 3.0);
            }
            AddLayer(s, layer, 0.05f);

            // Далёкий всплеск рыбы (0,07).
            Array.Clear(layer, 0, n);
            int fishAt = Samples(37.0 * seconds / 64);
            var fish = FightSynth.Splash(0xF15E0001u, 0, true);
            for (int i = 0; i < fish.Length; i++)
                layer[(fishAt + i) % n] += fish[i];
            AddLayer(s, layer, 0.07f);
            layer = null;

            FilterCircular(s, 60, 0.707, Band.High);
            RotateToCalmSeam(s, Samples(SeamGuardSeconds - 0.1));
            scale = BedScale;
            Scale(s, scale);
            return s;
        }

        /// <summary>
        /// Ветер над озером — отдельная петля, её громкость микшер ведёт по силе ветра (в штиль молчит):
        /// гул воды и воздуха (шум до 420 Гц с медленными накатами) и ветер в камыше с шорохом листьев.
        /// Это те же слои, что раньше были зашиты в LakeBed и звучали всегда на полную — как при сильном ветре.
        /// Уровень — тот же множитель BedScale: при ветре 8 м/с озеро звучит как прежде.
        /// </summary>
        public static float[] WindBed(double seconds)
        {
            int n = Samples(seconds);
            double from = SeamGuardSeconds, to = seconds - SeamGuardSeconds;
            var r = new Rng(0x1A4E0002u);

            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (float)r.S();
            FilterCircular(s, 420, 0.707, Band.Low);
            FilterCircular(s, 70, 0.707, Band.High);
            Normalize(s, 1f);
            var swell = SmoothRandom(n, Math.Max(1, (int)Math.Round(seconds / 12)), Math.Max(2, (int)Math.Round(seconds / 2.2)), ref r);
            for (int i = 0; i < n; i++)
                s[i] *= (float)((1 + 0.4 * swell.At(i)) * (1 - CalmWaterWeight));

            var layer = new float[n];
            // Ветер в камыше и шорох листьев (0,16).
            for (int i = 0; i < n; i++)
                layer[i] = (float)r.S();
            FilterCircular(layer, 1400, 0.7, Band.Band);
            FilterCircular(layer, 2600, 0.707, Band.Low);
            FilterCircular(layer, 500, 0.707, Band.High);
            Normalize(layer, 1f);
            var gustShape = SmoothRandom(n, Math.Max(1, (int)Math.Round(seconds / 25)), Math.Max(2, (int)Math.Round(seconds / 5)), ref r);
            for (int i = 0; i < n; i++)
                layer[i] *= (float)Gust(gustShape, i);
            // Шорох листьев: крошечные шумовые зёрна, тем гуще, чем сильнее порыв.
            double lt = from;
            while (lt < to)
            {
                int at = Samples(lt);
                double g = Gust(gustShape, Math.Min(n - 1, at));
                var f = new Svf(r.Range(1100, 2200), 2.0);
                int len = Samples(r.Range(0.004, 0.01));
                double amp = r.Range(0.3, 1.0) * g;
                for (int i = 0; i < len * 3; i++)
                {
                    double env = i < len ? 0.5 - 0.5 * Math.Cos(Math.PI * i / len) : Math.Exp(-(i - len) / (0.5 * len));
                    layer[(at + i) % n] += (float)(0.25 * amp * env * f.Band(r.S()) * 2);
                }
                lt += r.Range(0.5, 1.5) / (8 + 40 * g);
            }
            AddLayer(s, layer, 0.16f);

            FilterCircular(s, 60, 0.707, Band.High);
            Scale(s, BedScale);
            return s;
        }

        /// <summary>Сила порыва 0,12…1 в сэмпле i.</summary>
        private static double Gust(in SmoothCurve shape, int i) => Math.Max(0.12, Math.Min(1, 0.45 + 0.5 * shape.At(i)));

        /// <summary>dst += gain · layer (float, по сэмплу — тот же порядок сложения, что у суммы слоёв).</summary>
        private static void AddLayer(float[] dst, float[] layer, float gain)
        {
            for (int i = 0; i < dst.Length; i++)
                dst[i] += gain * layer[i];
        }

        /// <summary>
        /// Шов петли — в самое ровное место: из сдвигов ±range сэмплов (там нет начал событий) берётся тот, где громкость
        /// 20 мс до и после шва различается меньше всего (и меньше скачок сэмпла), и петля поворачивается на него
        /// (на месте, тремя разворотами — без второго массива).
        /// Петля круговая, поэтому поворот ничего не рвёт — только убирает случайный «накат» плеска ровно на стыке
        /// (в прототипе там было +5 дБ за 20 мс).
        /// </summary>
        private static void RotateToCalmSeam(float[] s, int range)
        {
            int n = s.Length, w = Samples(0.02), step = Math.Max(1, Samples(0.001));
            double rms = 0;
            for (int i = 0; i < n; i++)
                rms += (double)s[i] * s[i];
            rms = Math.Sqrt(rms / n) + 1e-12;
            int best = 0;
            double bestCost = double.MaxValue;
            for (int o = -range; o <= range; o += step)
            {
                double before = 0, after = 0;
                for (int i = 1; i <= w; i++)
                {
                    double v = s[Wrap(o - i, n)];
                    before += v * v;
                }
                for (int i = 0; i < w; i++)
                {
                    double v = s[Wrap(o + i, n)];
                    after += v * v;
                }
                double jump = Math.Abs(10 * Math.Log10((after + 1e-20) / (before + 1e-20)));
                double cost = jump + 2 * Math.Abs(s[Wrap(o, n)] - s[Wrap(o - 1, n)]) / rms;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = o;
                }
            }
            // Поворот влево на k: новый s[i] = старый s[(i + k) mod n].
            int k = Wrap(best, n);
            if (k == 0)
                return;
            Array.Reverse(s, 0, k);
            Array.Reverse(s, k, n - k);
            Array.Reverse(s);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        /// <summary>Иволга вдали: флейтовые ноты с глиссандо «фиу-лиу-лиу» 1,2–1,7 кГц и короткое эхо (−12 дБ).</summary>
        public static float[] Oriole(uint seed)
        {
            var r = new Rng(seed);
            var s = new float[Samples(0.45 + 0.6)];
            var notes = new (double f0, double f1, double len, double gap)[]
            {
                (1450, 1650, 0.12, 0.03), (1250, 1250, 0.09, 0.02), (1650, 1330, 0.16, 0.0),
            };
            double t0 = 0.01;
            double k = r.J(0.04);
            foreach (var nt in notes)
            {
                AddWhistle(s, Samples(t0), nt.f0 * k, nt.f1 * k, nt.len, 1.0, ref r);
                t0 += nt.len + nt.gap;
            }
            return Finish(s, -12, false);
        }

        /// <summary>Кукушка вдали, одно «ку-ку»: 700→680 Гц 150 мс, пауза 120 мс, 580→550 Гц 230 мс, эхо −12 дБ.
        /// Серию из пяти расставляет микшер с шагом 0,82–0,92 с.</summary>
        public static float[] Cuckoo(uint seed)
        {
            var r = new Rng(seed);
            var s = new float[Samples(0.52 + 0.6)];
            double k = r.J(0.01);
            AddWhistle(s, Samples(0.01), 700 * k, 680 * k, 0.15, 0.9, ref r, 0.12, 0.02);
            AddWhistle(s, Samples(0.28), 580 * k, 550 * k, 0.23, 0.8, ref r, 0.12, 0.02);
            return Finish(s, -12, false);
        }

        /// <summary>Тихое «цип-цип» мелкой птицы: 2,4→2,0 кГц, 45 и 40 мс, эхо −12 дБ.</summary>
        public static float[] Chirp(uint seed)
        {
            var r = new Rng(seed);
            var s = new float[Samples(0.15 + 0.5)];
            double k = r.J(0.04);
            AddWhistle(s, Samples(0.01), 2400 * k, 2000 * k, 0.045, 0.25, ref r);
            AddWhistle(s, Samples(0.10), 2350 * k, 1950 * k, 0.04, 0.2, ref r);
            return Finish(s, -12, false);
        }

        /// <summary>
        /// Сверчок вдали (полевой): одна трель — 3–4 слога по 14–18 мс через 32–36 мс на несущей 4,1–4,6 кГц с лёгким
        /// спадом высоты и «шорохом» крыла (шум в той же полосе), мягкие края; срез 6 кГц и эхо −14 дБ.
        /// Трели расставляет микшер (2–3 в секунду на сверчка, со сбоями и паузами).
        /// </summary>
        public static float[] CricketChirp(uint seed)
        {
            var r = new Rng(seed);
            double f = 4350 * r.J(0.05);
            int syllables = 3 + (r.U() < 0.4 ? 1 : 0);
            double interval = 0.034 * r.J(0.06);
            var s = new float[Samples(syllables * interval + 0.35)];
            double t0 = 0.005;
            for (int k = 0; k < syllables; k++)
            {
                double len = 0.016 * r.J(0.12);
                double amp = (k == 0 ? 0.85 : 1.0) * (1 - 0.06 * k) * r.J(0.08);
                int at = Samples(t0), m = Samples(len);
                double phase = r.U() * TwoPi;
                var scrape = new Svf(f, 6);
                for (int i = 0; i < m && at + i < s.Length; i++)
                {
                    double x = (double)i / m;
                    double env = Math.Pow(Math.Sin(Math.PI * x), 1.5);
                    phase += TwoPi * f * (1 - 0.03 * x) / SampleRate;
                    double v = Math.Sin(phase) + 0.12 * scrape.Band(r.S()) * 6;
                    s[at + i] += (float)(amp * env * v);
                }
                t0 += interval * r.J(0.04);
            }
            LowPass2(s, 6000);
            HighPass2(s, 1500);
            return Finish(s, -14, true);
        }

        // ---------- Кирпичики ----------

        /// <summary>Эхо, края; normalize — пик 0,9 (сверчки), иначе громкость как задана (птицы: иволга 1, кукушка 0,9, «цип» 0,25 —
        /// те же соотношения, что в прототипе).</summary>
        private static float[] Finish(float[] s, double roomDb, bool normalize)
        {
            AddRoom(s, roomDb);
            Fades(s, 0.0005, 0.05);
            if (normalize)
                Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Набег воды на берег: полоса 300–650 Гц, атака 60–120 мс, спад 150–350 мс и 1–3 пузыря (с заворотом в начало).</summary>
        private static void AddSlosh(float[] s, int at, ref Rng r)
        {
            int n = s.Length;
            double att = r.Range(0.06, 0.12), tau = r.Range(0.15, 0.35), gain = r.Range(0.35, 1.0);
            var f = new Svf(r.Range(300, 650), 1.2);
            double brown = 0;
            int len = Samples(att + tau * 5);
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < att ? 0.5 - 0.5 * Math.Cos(Math.PI * t / att) : Math.Exp(-(t - att) / tau);
                brown = 0.95 * brown + 0.3 * r.S();
                s[(at + i) % n] += (float)(gain * env * f.Band(brown) * 1.6);
            }
            int bubbles = 1 + (int)(r.U() * 3);
            for (int k = 0; k < bubbles; k++)
                AddBubble(s, at + Samples(att + r.Range(0, 0.25)), r.Range(4, 9), 0.25 * gain, true);
        }

        /// <summary>Свист: синус с глиссандо, 2-я гармоника h2, лёгкий «дых» (шум в той же полосе), огибающая — приподнятый синус.</summary>
        private static void AddWhistle(float[] s, int at, double f0, double f1, double len, double gain, ref Rng r, double h2 = 0.06, double attack = 0.015)
        {
            int m = Samples(len);
            double phase = 0;
            var breath = new Svf(f0, 8);
            for (int i = 0; i < m && at + i < s.Length; i++)
            {
                double t = (double)i / SampleRate, x = (double)i / m;
                double f = f0 * Math.Pow(f1 / f0, x);
                phase += TwoPi * f / SampleRate;
                double env = Math.Min(1, t / attack) * Math.Min(1, (len - t) / 0.02) * (1 - 0.3 * x);
                breath.Set(f, 8);
                double v = Math.Sin(phase) + h2 * Math.Sin(2 * phase) + 0.15 * breath.Band(r.S()) * 8;
                s[at + i] += (float)(gain * env * v);
            }
        }
    }
}
