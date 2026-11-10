using System;
using System.Collections.Generic;
using static Fishing.Core.Audio.SynthKit;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Рецепты звуков рыбалки, палитра «Натуральная» (tools/audio/Audio_natural): хорошо смазанная безынерционная
    /// катушка и тихое утреннее озеро, записанные вблизи. Моно, 22050 Гц, без ассетов и без движка; один и тот же
    /// вызов даёт те же сэмплы. Фон озера, птицы и сверчки — в LakeSynth.
    ///
    /// Правила для всего, что повторяется или звучит долго (катушка, фрикцион, гул лески, фон):
    /// - центр тяжести спектра 400–1300 Гц, в полосе 2–5 кГц не больше ~10% энергии, срез снизу 90–150 Гц;
    /// - никакой периодической модуляции громкости 2–8 Гц и 20–150 Гц: ритм — только отдельные зёрна,
    ///   медленный «дрейф» петель — ниже 2 Гц и целым числом периодов на петлю;
    /// - петли — только шумовые постели не короче 4 с; фильтры прогоняются по кругу, хвосты событий
    ///   заворачиваются в начало, поэтому шва нет;
    /// - удары — затухающие моды (резонаторы) с демпфированием дерева и пластика (tg φ ≥ 0,02, не металл),
    ///   возбуждение — приподнятый косинус не короче 0,2 мс; никаких «наивных» пил и прямоугольников.
    /// Яркость разрешена только редким разовым звукам (обрыв, поломка).
    ///
    /// Отличия от прототипа палитры (по замечаниям AudioCritic):
    /// - «ток» шестерни: основная мода вариантов разнесена равномерно на ±9% (а не ±3% случайно), соотношения мод,
    ///   затухания и мягкость колотушки свои у каждого — 8 различимых звуков вместо 5;
    /// - скрип удилища: тихий и громкий — разные «инструменты» (громкий — напряжённый бланк: моды выше, уже и ярче,
    ///   срывы чаще), срывы без пауз длиннее ~30 мс — скрип звучит одной фразой, а не россыпью одинаковых щелчков.
    /// </summary>
    public static class FightSynth
    {
        public const int SampleRate = SynthKit.SampleRate;

        /// <summary>Число вариантов «тока» (основные моды — равномерно на ±9%).</summary>
        public const int TockVariants = 8;

        // =================================================================================================
        // Катушка
        // =================================================================================================

        /// <summary>
        /// Проход ротора (одно зерно на 14 см лески): мягкое «ш» лески по ролику — тёмный шум (белый через
        /// однополюсный 1 кГц) в полосе 560 Гц ±10%, Q 0,7, атака 24 мс, спад τ 32 мс ±15%, срез 1,3 кГц — плюс
        /// глухой «тук» дужки (290/585 Гц, −15 дБ) и едва слышный щелчок ролика (1180/1760 Гц, −24 дБ). 125 мс:
        /// соседние зёрна перекрываются, и подмотка звучит ровным шелестом, а не «чух-чух».
        /// </summary>
        public static float[] RotorGrain(uint seed)
        {
            var r = new Rng(seed);
            int n = Samples(0.125);
            var swish = new float[n];
            var bp = new Svf(560 * r.J(0.10), 0.7);
            double att = 0.024 * r.J(0.2), tau = 0.032 * r.J(0.15);
            double dark = 0, a1k = 1 - Math.Exp(-TwoPi * 1000 / SampleRate);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < att ? 0.5 - 0.5 * Math.Cos(Math.PI * t / att) : Math.Exp(-(t - att) / tau);
                dark += a1k * (r.S() - dark);
                swish[i] = (float)(bp.Band(dark) * env);
            }
            Normalize(swish, 1f);

            var body = Modal(n, 0, new[] { M(290 * r.J(0.05), 0.011, 1.0), M(585 * r.J(0.05), 0.006, 0.45) }, 1.5, r.NextSeed());
            Normalize(body, 1f);
            var tick = Modal(n, Samples(r.Range(0.003, 0.008)),
                new[] { M(1180 * r.J(0.04), 0.004 * r.J(0.15), 1.0), M(1760 * r.J(0.04), 0.002 * r.J(0.15), 0.45) }, 0.4, r.NextSeed());
            Normalize(tick, 1f);

            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = swish[i] + 0.18f * body[i] + 0.06f * tick[i];
            LowPass2(s, 1300);
            HighPass2(s, 110);
            Fades(s, 0.0004, 0.006);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Такт шестерни — раз на оборот ручки (85 см): пластмассово-деревянный «ток» из трёх мод (около 410/960/1580 Гц,
        /// τ ≈ 16/8/3,5 мс — не звенит), мягкое возбуждение 0,6–1,1 мс, срез 2,4 кГц. 80 мс.
        /// Основная мода варианта variant стоит на своей ступени равномерной сетки 0,91…1,09 × 410 Гц (ступень 2,6%:
        /// соседние варианты похожи меньше чем на 0,9), вторая и третья — свои соотношения ±5%, затухания ±20%.
        /// </summary>
        public static float[] GearTock(int variant, uint seed)
        {
            // Перестановка ступеней: соседние номера вариантов не соседние по высоте.
            int[] order = { 3, 7, 0, 5, 2, 6, 1, 4 };
            int step = order[((variant % TockVariants) + TockVariants) % TockVariants];
            var r = new Rng(seed);
            double f1 = 410 * (0.91 + 0.18 * step / (TockVariants - 1));
            int n = Samples(0.08);
            var s = Modal(n, 0, new[]
            {
                M(f1, 0.016 * r.J(0.2), 1.0),
                M(f1 * 2.34 * r.J(0.05), 0.008 * r.J(0.2), 0.5 * r.J(0.2)),
                M(f1 * 3.85 * r.J(0.05), 0.0035 * r.J(0.2), 0.18 * r.J(0.25)),
            }, r.Range(0.6, 1.1), r.NextSeed());
            LowPass2(s, 2400);
            HighPass2(s, 120);
            Fades(s, 0.0003, 0.008);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Постель укладки лески на шпулю (петля 4 с): розовый шум в полосе 680 Гц (Q 0,6), срез 220 Гц
        /// снизу и 1,4 кГц сверху, плюс редкая тихая «крупка» витков (30 в секунду, 2,5 мс, 1 кГц) и медленный
        /// дрейф громкости 0,25/0,75/1,75 Гц глубиной 7/5/3%. Высота — скорость катушки (0,8–1,2).
        /// </summary>
        public static float[] LayBed()
        {
            const double length = 4.0;
            int n = Samples(length);
            var r = new Rng(0x1A7B0001u);
            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (float)r.S();
            PinkCircular(s);
            FilterCircular(s, 680, 0.6, Band.Band);
            FilterCircular(s, 1400, 0.707, Band.Low);
            FilterCircular(s, 220, 0.707, Band.High);
            Normalize(s, 1f);

            int grains = (int)(30 * length);
            for (int k = 0; k < grains; k++)
            {
                int at = (int)(r.U() * n);
                double amp = 0.06 + 0.10 * r.U();
                var g = new Svf(1000 * r.J(0.15), 1.5);
                int len = Samples(0.0025);
                for (int i = 0; i < len * 3; i++)
                {
                    double env = i < len ? 0.5 - 0.5 * Math.Cos(Math.PI * i / len) : Math.Exp(-(i - len) / (0.4 * len));
                    s[(at + i) % n] += (float)(amp * env * g.Band(r.S()) * 2.5);
                }
            }

            double p1 = r.U() * TwoPi, p2 = r.U() * TwoPi, p3 = r.U() * TwoPi;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                s[i] *= (float)(1 + 0.07 * Math.Sin(TwoPi * x + p1) + 0.05 * Math.Sin(TwoPi * 3 * x + p2)
                    + 0.03 * Math.Sin(TwoPi * 7 * x + p3));
            }
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>
        /// Натуга (вместо «рычания»): срыв-прилипание в механизме через резонаторы около 250/395/560/790/1120 Гц
        /// (форманты скрипа, сдвинутые вверх, чтобы их играл динамик телефона).
        /// Большое зерно (70 мс): 2–4 импульса через 8–16 мс, резонаторы ±6%, добротность 3,5–5.
        /// Малое (45 мс): 2–3 микросрыва через 3–12 мс и шумовой «шорох» 2–4 мс, а резонаторы — свои у каждого варианта
        /// (частоты ±12%, амплитуды ±30%, добротность 2,5–6): с одним импульсом через общий набор все шесть малых
        /// звучали одним звуком (сходство 0,95–0,99), и натуга под нагрузкой казалась механической.
        /// </summary>
        public static float[] StrainGrain(uint seed, bool big)
        {
            var r = new Rng(seed);
            int n = Samples(big ? 0.07 : 0.045);
            var exc = new float[n];
            var freqs = new[] { 250.0, 395, 560, 790, 1120 };
            if (!big)
            {
                int micro = 2 + (r.U() < 0.5 ? 1 : 0);
                double tm = 0.001;
                for (int p = 0; p < micro; p++)
                {
                    double amp = p == 0 ? 1.0 : 0.35 + 0.5 * r.U();
                    AddPulse(exc, Samples(tm), 0.0007 * r.J(0.3), amp);
                    tm += r.Range(0.003, 0.012);
                }
                int at = Samples(r.Range(0.0005, 0.004)), len = Samples(r.Range(0.002, 0.004));
                for (int i = 0; i < len && at + i < n; i++)
                    exc[at + i] += (float)(0.3 * r.S() * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / len)));
                var amps = new[] { 1.0 * r.J(0.3), 0.85 * r.J(0.3), 0.7 * r.J(0.3), 0.45 * r.J(0.3), 0.28 * r.J(0.3) };
                var small = Resonate(exc, freqs, amps, 2.5, 6.0, 0.12, ref r);
                LowPass2(small, 2400);
                HighPass2(small, 140);
                Fades(small, 0.0003, 0.006);
                Normalize(small, 0.9f);
                return small;
            }
            int pulses = 2 + (int)(r.U() * 3);
            double t0 = 0.001;
            for (int p = 0; p < pulses; p++)
            {
                double amp = p == 0 ? 1.0 : 0.45 + 0.45 * r.U();
                AddPulse(exc, Samples(t0), 0.0007, amp);
                t0 += r.Range(0.008, 0.016);
            }
            var s = Resonate(exc, freqs, new[] { 1.0, 0.85, 0.7, 0.45, 0.28 }, 3.5, 5.0, 0.06, ref r);
            LowPass2(s, 2400);
            HighPass2(s, 140);
            Fades(s, 0.0003, 0.006);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Щелчок трещотки фрикциона (собачка по зубчатому колесу в пластиковой шпуле): моды 420/1150/1720/2600 Гц,
        /// τ 8/5/3,5/2,5 мс, амплитуды 0,4/1/0,8/0,45 (частота ±7%, τ ±15%, амплитуда ±25% — чтобы
        /// на быстрой пробуксовке не стояла одна «поющая» форманта); у ярких — ещё
        /// тихий «дзинь» шпули 2950 Гц (τ 6 мс, 0,1). Возбуждение 0,3 мс + 10% шумового щелчка.
        /// Срез 4 кГц у ярких и 2,2 кГц у тёмных (тёмные — для быстрой пробуксовки). 45 мс.
        /// </summary>
        public static float[] DragTick(uint seed, bool dark)
        {
            var r = new Rng(seed);
            int n = Samples(0.045);
            var modes = new List<Mode>
            {
                M(420 * r.J(0.07), 0.008 * r.J(0.15), 0.40 * r.J(0.25)),
                M(1150 * r.J(0.07), 0.005 * r.J(0.15), 1.00 * r.J(0.25)),
                M(1720 * r.J(0.07), 0.0035 * r.J(0.15), 0.80 * r.J(0.25)),
                M(2600 * r.J(0.07), 0.0025 * r.J(0.15), 0.45 * r.J(0.25)),
            };
            if (!dark)
                modes.Add(M(2950 * r.J(0.03), 0.006 * r.J(0.15), 0.10));
            var s = Modal(n, 0, modes.ToArray(), 0.3, r.NextSeed());
            Normalize(s, 1f);
            int ex = Samples(0.0003);
            for (int i = 0; i < ex * 2; i++)
                s[i] += (float)(0.10 * (0.5 - 0.5 * Math.Cos(Math.PI * i / ex)) * r.S());
            LowPass2(s, dark ? 2200 : 4000);
            HighPass2(s, 150);
            Fades(s, 0.0002, 0.004);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Гул натянутой лески (петля 4 с): шумовые «струны» 330 Гц (Q 14), 662 Гц (Q 10, −7 дБ), 990 Гц (Q 8, −8 дБ),
        /// 1320 Гц (Q 6, −10 дБ); «дыхание» 0,25 и 0,5 Гц глубиной 12 и 5%; срез 2,6 кГц. Звучит у предела — это
        /// предупреждение, поэтому верхние «струны» 990/1320 Гц сильные: их играет и динамик телефона (на 330 Гц он глух).
        /// </summary>
        public static float[] LineHum()
        {
            const double length = 4.0;
            int n = Samples(length);
            var r = new Rng(0x11E40002u);
            var noise = new float[n];
            for (int i = 0; i < n; i++)
                noise[i] = (float)r.S();
            var a = (float[])noise.Clone();
            var b = (float[])noise.Clone();
            var c = (float[])noise.Clone();
            FilterCircular(a, 330, 14, Band.BandNorm);
            FilterCircular(b, 662, 10, Band.BandNorm);
            FilterCircular(c, 1320, 6, Band.BandNorm);
            FilterCircular(noise, 990, 8, Band.BandNorm);
            var s = new float[n];
            double p1 = r.U() * TwoPi, p2 = r.U() * TwoPi;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double breath = 1 + 0.12 * Math.Sin(TwoPi * x + p1) + 0.05 * Math.Sin(TwoPi * 2 * x + p2);
                s[i] = (float)((a[i] + 0.45 * b[i] + 0.4 * noise[i] + 0.32 * c[i]) * breath);
            }
            FilterCircular(s, 150, 0.707, Band.High);
            FilterCircular(s, 2600, 0.707, Band.Low);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Число вариантов треска лески (основные моды — равномерно на ±20%).</summary>
        public const int CrackleVariants = 8;

        /// <summary>
        /// Потрескивание лески на кольцах у самого предела (предупреждение — в полосе, которую слышно и в телефоне):
        /// крошечный «тик» из трёх мод около 1800 Гц (τ 2/3/1,2 мс ±35%, амплитуды ±30%), возбуждение 0,2–0,35 мс,
        /// срез 800 Гц–3,2 кГц, 30 мс. Основная мода варианта variant стоит на своей ступени сетки 0,8…1,2 × 1800 Гц
        /// (как у «тока»: случайный разброс у щелчков в 2 мс давал пары со сходством 0,9+), нижняя и верхняя — свои
        /// соотношения ±8%; у нечётных ступеней — двойной «тик» через 1,5–4 мс (второй тише).
        /// </summary>
        public static float[] LineCrackle(int variant, uint seed)
        {
            int[] order = { 2, 6, 0, 4, 7, 1, 5, 3 };
            int step = order[((variant % CrackleVariants) + CrackleVariants) % CrackleVariants];
            var r = new Rng(seed);
            int n = Samples(0.03);
            double f1 = 1800 * (0.8 + 0.4 * step / (CrackleVariants - 1));
            var modes = new[]
            {
                M(f1 * 0.69 * r.J(0.08), 0.003 * r.J(0.35), 0.7 * r.J(0.3)),
                M(f1, 0.002 * r.J(0.35), 1.0),
                M(f1 * 1.33 * r.J(0.08), 0.0012 * r.J(0.35), 0.5 * r.J(0.3)),
            };
            var s = Modal(n, 0, modes, r.Range(0.2, 0.35), r.NextSeed());
            if (step % 2 == 1)
            {
                var echo = Modal(n, Samples(r.Range(0.0015, 0.004)), modes, r.Range(0.2, 0.35), r.NextSeed());
                Mix(s, echo, 0, r.Range(0.45, 0.7));
            }
            LowPass2(s, 3200);
            HighPass2(s, 800);
            Fades(s, 0.0002, 0.004);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Скрип удилища — цепочка срывов (интервалы ×0,6–1,4, сила — корень из накопленного) по дуге частоты через
        /// резонаторы бланка, срез 125 Гц снизу.
        /// Тихий (удилище нагружено): 40→75→40 срывов в секунду, 0,25–0,45 с, моды 180/320/520/760/1100 Гц ±8%,
        /// Q 2,5–4, срез 2,2 кГц — глухой деревянный «скрррип».
        /// Громкий (у предела): 55→140→55 в секунду, 0,35–0,55 с, моды выше и уже (205/365/600/880/1280 Гц ±8%,
        /// Q 3,5–5,5), верхние сильнее, импульсы короче, срез 2,8 кГц — напряжённый, более «голосистый» стон.
        /// Пауз между срывами длиннее ~30 мс нет: скрип звучит одной фразой.
        /// </summary>
        public static float[] RodCreak(uint seed, bool loud)
        {
            var r = new Rng(seed);
            double length = loud ? r.Range(0.35, 0.55) : r.Range(0.25, 0.45);
            int n = Samples(length + 0.04);
            var exc = new float[n];
            double r0 = loud ? 55 : 40, r1 = loud ? 140 : 75;
            double pulse = loud ? 0.0004 : 0.0007;
            double t = 0.006;
            while (t < length)
            {
                double x = t / length;
                double arch = Math.Sin(Math.PI * x);
                double rate = r0 + (r1 - r0) * arch;
                double gap = r.Range(0.6, 1.4) / rate;
                double amp = Math.Sqrt(Math.Min(2.0, gap * rate)) * (0.3 + 0.7 * Math.Pow(arch, 0.7));
                AddPulse(exc, Samples(t), pulse, amp);
                t += gap;
            }
            float[] s = loud
                ? Resonate(exc, new[] { 205.0, 365, 600, 880, 1280 }, new[] { 0.8, 0.9, 0.8, 0.6, 0.4 }, 3.5, 5.5, 0.08, ref r)
                : Resonate(exc, new[] { 180.0, 320, 520, 760, 1100 }, new[] { 1.0, 0.8, 0.6, 0.35, 0.2 }, 2.5, 4.0, 0.08, ref r);
            HighPass2(s, 125);
            LowPass2(s, loud ? 2800 : 2200);
            Fades(s, 0.002, 0.015);
            Normalize(s, 0.9f);
            return s;
        }

        // =================================================================================================
        // Подсечка и телеграфы
        // =================================================================================================

        /// <summary>
        /// Подсечка: сначала свист удилища (полоса шума 350→1300→650 Гц, Q 1,3, пик на 35 мс), потом, через 70 мс, —
        /// леска встала в струну: «тумк» 160/322/488/980 Гц (τ 70/40/22/8 мс, мягкое возбуждение 3 мс), тихий «дзынь»
        /// лески 640/1300 Гц и сухой «тик» натянувшейся лески 1250/1800/2300 Гц (τ 5/3/2 мс) — его слышно и в динамике
        /// телефона, где «тумк» 160 Гц пропадает. Свист и удар не накладываются вершинами: при том же пике (клип
        /// нормирован по нему) оба звучат громче. 0,4 с.
        /// </summary>
        public static float[] Hookset()
        {
            var r = new Rng(0x400C5E70u);
            int n = Samples(0.4);
            var s = new float[n];
            var swish = Sweep(n, 0, 0.12, t => t < 0.035 ? 350 * Math.Pow(1300.0 / 350, t / 0.035) : 1300 * Math.Pow(650.0 / 1300, Math.Min(1, (t - 0.035) / 0.085)),
                1.3, t => t < 0.035 ? Math.Pow(Math.Sin(Math.PI / 2 * t / 0.035), 2) : Math.Exp(-(t - 0.035) / 0.025), ref r);
            Mix(s, swish, 0, 0.85);
            int hit = Samples(0.07);
            var thump = Modal(n, hit, new[] { M(160, 0.07, 1.0), M(322, 0.04, 0.6), M(488, 0.022, 0.35), M(980, 0.008, 0.15) }, 3.0, r.NextSeed());
            Normalize(thump, 1f);
            Mix(s, thump, 0, 0.6);
            var zing = Modal(n, hit + Samples(0.002), new[] { M(640, 0.025, 1.0), M(1300, 0.012, 0.5) }, 0.6, r.NextSeed());
            Normalize(zing, 1f);
            Mix(s, zing, 0, 0.15);
            var tick = Modal(n, hit, new[] { M(1250, 0.005, 1.0), M(1800, 0.003, 0.7), M(2300, 0.002, 0.4) }, 0.4, r.NextSeed());
            Normalize(tick, 1f);
            Mix(s, tick, 0, 0.9);
            HighPass2(s, 70);
            LowPass2(s, 3000);
            Fades(s, 0.0005, 0.02);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Перед рывком: рыба мотает головой — два стука по бланку удилища «тук-тук» —
        /// полый «ток» 190/420/690/1050/1500 Гц (τ 25/20/12/7/3 мс) с сухим «щелчком» бланка 1350/1900/2450 Гц
        /// (τ 4/3/2 мс) — его и слышно в динамике телефона; второй стук через 85 мс тише и на 3% ниже.</summary>
        public static float[] TeleJerk()
        {
            int n = Samples(0.26);
            var s = new float[n];
            var a = Modal(n, 0, new[] { M(190, 0.025, 0.5), M(420, 0.02, 1.0), M(690, 0.012, 0.7), M(1050, 0.007, 0.4), M(1500, 0.003, 0.15) }, 1.5, 0x7E1E0001u);
            var b = Modal(n, Samples(0.085), new[] { M(184, 0.025, 0.5), M(407, 0.02, 1.0), M(669, 0.012, 0.7), M(1020, 0.007, 0.4), M(1455, 0.003, 0.15) }, 1.5, 0x7E1E0002u);
            var ka = Modal(n, 0, new[] { M(1350, 0.004, 1.0), M(1900, 0.003, 0.8), M(2450, 0.002, 0.45) }, 0.4, 0x7E1E0003u);
            var kb = Modal(n, Samples(0.085), new[] { M(1310, 0.004, 1.0), M(1845, 0.003, 0.8), M(2380, 0.002, 0.45) }, 0.4, 0x7E1E0004u);
            Normalize(ka, (float)Peak(a));
            Normalize(kb, (float)Peak(b));
            Mix(s, a, 0, 1.0);
            Mix(s, b, 0, 0.65);
            Mix(s, ka, 0, 1.0);
            Mix(s, kb, 0, 0.65);
            HighPass2(s, 90);
            LowPass2(s, 3200);
            Fades(s, 0.0005, 0.02);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Перед уходом: рыба разворачивается — водоворот (полоса 260→720 Гц, Q 2,2, нарастает) и пять пузырей 4–8 мм во второй половине. 0,62 с.</summary>
        public static float[] TeleRun()
        {
            var r = new Rng(0x7E1E0010u);
            int n = Samples(0.62);
            var s = Swirl(n, t => 260 * Math.Pow(720.0 / 260, t / 0.62), 2.2, t => Arch(t / 0.62, 0.6), ref r);
            Normalize(s, 1f);
            for (int k = 0; k < 5; k++)
                AddBubble(s, Samples(r.Range(0.25, 0.52)), r.Range(4, 8), r.Range(0.15, 0.3));
            LowPass2(s, 1800);
            HighPass2(s, 90);
            Fades(s, 0.002, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Перед уходом вглубь: глубокое «бульк» — два крупных пузыря 6 и 7,5 мм (500/400 Гц, растут вверх) и водоворот 700→330 Гц вниз. 0,6 с.</summary>
        public static float[] TeleDive()
        {
            var r = new Rng(0x7E1E0020u);
            int n = Samples(0.6);
            var s = Swirl(n, t => 700 * Math.Pow(330.0 / 700, t / 0.6), 2.5, t => Arch(t / 0.6, 0.35), ref r);
            Normalize(s, 0.6f);
            AddBubble(s, Samples(0.12), 6, 0.9);
            AddBubble(s, Samples(0.28), 7.5, 0.8);
            for (int k = 0; k < 3; k++)
                AddBubble(s, Samples(r.Range(0.15, 0.4)), r.Range(3, 5), 0.12);
            LowPass2(s, 1500);
            HighPass2(s, 90);
            Fades(s, 0.002, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Перед броском: вода закипает — 14 пузырей 3–9 мм всё гуще и шум 400→900 Гц нарастает. 0,65 с.</summary>
        public static float[] TeleSurge()
        {
            var r = new Rng(0x7E1E0030u);
            int n = Samples(0.65);
            var s = Swirl(n, t => 400 * Math.Pow(900.0 / 400, t / 0.65), 1.2, t => Math.Pow(Math.Min(1, t / 0.5), 2) * Math.Min(1, (0.65 - t) / 0.08), ref r);
            Normalize(s, 0.5f);
            for (int k = 0; k < 14; k++)
            {
                double at = 0.05 + 0.5 * Math.Sqrt(r.U());
                AddBubble(s, Samples(at), r.Range(3, 9), r.Range(0.15, 0.35));
            }
            LowPass2(s, 2000);
            HighPass2(s, 100);
            Fades(s, 0.002, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Перед свечкой: вода вспучивается — шум 300→1500 Гц нарастает к поверхности и рвётся «фуп» из трёх мелких пузырей. 0,55 с.</summary>
        public static float[] TeleJump()
        {
            var r = new Rng(0x7E1E0040u);
            int n = Samples(0.55);
            var s = Swirl(n, t => 300 * Math.Pow(1500.0 / 300, Math.Min(1, t / 0.47)), 1.6,
                t => t < 0.47 ? Math.Pow(t / 0.47, 2) : Math.Exp(-(t - 0.47) / 0.03), ref r);
            Normalize(s, 0.8f);
            for (int k = 0; k < 3; k++)
                AddBubble(s, Samples(0.465 + 0.012 * k), r.Range(2.5, 4), 0.35);
            LowPass2(s, 2400);
            HighPass2(s, 110);
            Fades(s, 0.002, 0.02);
            Normalize(s, 0.9f);
            return s;
        }

        // =================================================================================================
        // Вода
        // =================================================================================================

        /// <summary>
        /// Всплеск из пузырей (Миннарт: f₀·r ≈ 3 м/с): корона шума (атака 2 мс, спад 30/55 мс) и облако
        /// пузырей с радиусами p(r) ∝ r^−2,5 и моментами по экспоненте τ 90 мс, потом капли «питтер».
        /// size 0 — малая рыба (10 пузырей 2,2–7 мм, корона до 1,8 кГц, 0,5 с), 1 — крупная (18 пузырей 3–14 мм, «тумп» 85 Гц
        /// с гармониками, 0,8 с), 2 — падение после свечки (шлепок телом + крупная, 0,9 с).
        /// far — даль: срез 1,1 кГц и короткий хвост «эха» (−14 дБ).
        /// </summary>
        public static float[] Splash(uint seed, int size, bool far)
        {
            var r = new Rng(seed);
            bool heavy = size >= 1;
            double length = size == 0 ? 0.5 : size == 1 ? 0.8 : 0.9;
            int n = Samples(length);
            var s = new float[n];

            var crown = new float[n];
            double cTau = heavy ? 0.055 : 0.03;
            double cStart = size == 2 ? 0.012 : 0.0;
            for (int i = Samples(cStart); i < n; i++)
            {
                double t = (double)i / SampleRate - cStart;
                double env = t < 0.002 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.002) : Math.Exp(-(t - 0.002) / cTau);
                crown[i] = (float)(r.S() * env);
            }
            LowPass2(crown, heavy ? 1500 : 1800);
            HighPass2(crown, 300);
            Normalize(crown, 1f);
            Mix(s, crown, 0, heavy ? 0.6 : 0.5);

            if (size == 2)
            {
                var slap = Modal(n, 0, new[] { M(220, 0.02, 1.0), M(470, 0.01, 0.5), M(900, 0.004, 0.2) }, 1.5, r.NextSeed());
                Normalize(slap, 1f);
                Mix(s, slap, 0, 0.5);
            }

            int count = size == 0 ? 10 : 18;
            double rMin = heavy ? 3 : 2.2, rMax = heavy ? 14 : 7;
            for (int k = 0; k < count; k++)
            {
                double t = cStart + 0.004 - 0.09 * Math.Log(1 - 0.95 * r.U());
                AddBubble(s, Samples(t), PowerLawRadius(ref r, rMin, rMax), 0.35 * r.J(0.3));
            }
            int drops = size == 0 ? 4 : 8;
            for (int k = 0; k < drops; k++)
                AddBubble(s, Samples(r.Range(0.15, length - 0.15)), r.Range(1.5, 3), 0.12 * r.J(0.3));

            if (heavy)
            {
                double g = size == 2 ? 0.45 : 0.35;
                var thump = Modal(n, Samples(cStart), new[] { M(85, 0.045, 1.0), M(170, 0.04, 0.6), M(255, 0.03, 0.35) }, 3.0, r.NextSeed());
                Normalize(thump, 1f);
                Mix(s, thump, 0, g);
            }

            HighPass2(s, 70);
            if (far)
            {
                LowPass2(s, 1100);
                AddRoom(s, -14);
            }
            Fades(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Поплавок падает на воду: шлепок 10 мс (500–1800 Гц), пузырь 6,5 мм (460 Гц), два мелких и мягкий плеск ряби. 0,45 с.</summary>
        public static float[] FloatPlop(bool far)
        {
            var r = new Rng(0xF1047001u);
            int n = Samples(0.45);
            var s = new float[n];
            var hit = new float[n];
            for (int i = 0; i < Samples(0.03); i++)
            {
                double t = (double)i / SampleRate;
                hit[i] = (float)(r.S() * (t < 0.001 ? t / 0.001 : Math.Exp(-(t - 0.001) / 0.005)));
            }
            HighPass2(hit, 500);
            LowPass2(hit, 1800);
            Normalize(hit, 1f);
            Mix(s, hit, 0, 0.4);
            AddBubble(s, Samples(0.004), 6.5, 0.8);
            AddBubble(s, Samples(0.04), 2.5, 0.25);
            AddBubble(s, Samples(0.07), 3.5, 0.25);
            var slosh = Swirl(n, t => 350, 1.5, t => t < 0.06 ? 0 : Math.Sin(Math.PI * Math.Min(1, (t - 0.06) / 0.29)), ref r);
            Normalize(slosh, 1f);
            Mix(s, slosh, 0, 0.15);
            HighPass2(s, 90);
            if (far)
            {
                LowPass2(s, 1300);
                AddRoom(s, -15);
            }
            Fades(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Поклёвка: поплавок притоплен — «блуп» пузыря 6 мм (500 Гц), крошечный 2,2 мм и тихий плеск. 0,35 с.</summary>
        public static float[] BiteBloop(bool far)
        {
            var r = new Rng(0xB17E0001u);
            int n = Samples(0.35);
            var s = new float[n];
            AddBubble(s, Samples(0.003), 6, 0.8);
            AddBubble(s, Samples(0.06), 2.2, 0.2);
            var slosh = Swirl(n, t => 300, 1.4, t => Math.Sin(Math.PI * Math.Min(1, t / 0.3)), ref r);
            Normalize(slosh, 1f);
            Mix(s, slosh, 0, 0.12);
            HighPass2(s, 90);
            if (far)
            {
                LowPass2(s, 1300);
                AddRoom(s, -15);
            }
            Fades(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        // =================================================================================================
        // Финалы
        // =================================================================================================

        /// <summary>
        /// Поимка: рыба выходит из воды (облако пузырей), 2/3/5 влажных шлепков с шагом 110–160 мс (6–9 Гц),
        /// затем деревянные «маримба»-ноты (моды 1 : 3,93 : 9,2, мягкая колотушка 2,5 мс):
        /// tier 1 — соль-ре (392, 587 Гц), 2 — соль-си-ре, 3 — соль-си-ре-соль и низкая соль 196 Гц, длиннее.
        /// </summary>
        public static float[] Landed(int tier)
        {
            tier = Math.Max(1, Math.Min(3, tier));
            var r = new Rng(0x1A4D0000u + (uint)tier);
            double length = tier == 1 ? 1.1 : tier == 2 ? 1.5 : 2.4;
            int n = Samples(length);
            var s = new float[n];

            for (int k = 0; k < 8; k++)
                AddBubble(s, Samples(0.004 - 0.06 * Math.Log(1 - 0.9 * r.U())), PowerLawRadius(ref r, 2, 8), 0.22 * r.J(0.3));
            int flops = tier == 1 ? 2 : tier == 2 ? 3 : 5;
            double at = 0.12;
            for (int k = 0; k < flops; k++)
            {
                var flop = Flop(n, Samples(at), r.NextSeed());
                Mix(s, flop, 0, 0.3 * Math.Pow(0.82, k));
                at += r.Range(0.11, 0.16);
            }

            double[] notes = tier == 1 ? new[] { 392.0, 587.33 }
                : tier == 2 ? new[] { 392.0, 493.88, 587.33 }
                : new[] { 392.0, 493.88, 587.33, 783.99 };
            double start = 0.22;
            for (int k = 0; k < notes.Length; k++)
            {
                bool last = k == notes.Length - 1;
                double sustain = last ? (tier == 3 ? 0.9 : 0.6) : 0.4;
                AddBar(s, Samples(start + 0.11 * k), notes[k], sustain, 0.32 * (last ? 1.0 : 0.85));
            }
            if (tier == 3)
                AddBar(s, Samples(start + 0.11 * notes.Length), 196.0, 1.1, 0.22);

            HighPass2(s, 80);
            LowPass2(s, 4000);
            Fades(s, 0.0003, 0.08);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Обрыв (редкий — яркость можно): сухой «тщ» лески 900/1900/3100 Гц за 2–6 мс, удилище распрямляется
        /// со свистом 1000→280 Гц (громко: свист и щелчок не совпадают вершинами, поэтому при пике у порога лимитера
        /// обрыв звучит плотнее, а не только щелчком), ослабшая леска пару раз шлёпает. 0,6 с.
        /// </summary>
        public static float[] LineSnap()
        {
            var r = new Rng(0x5A4B0001u);
            int n = Samples(0.6);
            var s = new float[n];
            var crack = Modal(n, 0, new[] { M(1900, 0.004, 1.0), M(3100, 0.002, 0.6), M(900, 0.006, 0.5) }, 0.25, r.NextSeed());
            Normalize(crack, 1f);
            for (int i = 0; i < Samples(0.0015); i++)
                crack[i] += (float)(0.3 * r.S() * (1 - (double)i / Samples(0.0015)));
            Mix(s, crack, 0, 0.8);
            var whoosh = Sweep(n, Samples(0.005), 0.3, t => 1000 * Math.Pow(280.0 / 1000, Math.Min(1, t / 0.25)), 1.3,
                t => t < 0.04 ? Math.Sin(Math.PI / 2 * t / 0.04) : Math.Exp(-(t - 0.04) / 0.08), ref r);
            Mix(s, whoosh, 0, 1.1);
            for (int k = 0; k < 3; k++)
            {
                var slap = Modal(n, Samples(0.18 + 0.07 * k + 0.02 * r.U()), new[] { M(600 * r.J(0.1), 0.004, 1.0), M(1300 * r.J(0.1), 0.002, 0.5) }, 0.5, r.NextSeed());
                Normalize(slap, 1f);
                Mix(s, slap, 0, 0.2 * Math.Pow(0.6, k));
            }
            HighPass2(s, 90);
            LowPass2(s, 4500);
            Fades(s, 0.0002, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Поломка удилища: треск 1400/2300/3600 Гц, 12 редеющих щепок, полый стук 260/610/1050 Гц и свист обломка. 0,55 с.</summary>
        public static float[] RodCrack()
        {
            var r = new Rng(0xC4AC0001u);
            int n = Samples(0.55);
            var s = new float[n];
            var crack = Modal(n, 0, new[] { M(1400, 0.006, 1.0), M(2300, 0.003, 0.7), M(3600, 0.0015, 0.4) }, 0.3, r.NextSeed());
            Normalize(crack, 1f);
            Mix(s, crack, 0, 0.8);
            double t = 0.01;
            for (int k = 0; k < 12; k++)
            {
                var sp = Modal(n, Samples(t), new[] { M(1200 * r.J(0.3), 0.003, 1.0), M(2400 * r.J(0.3), 0.0015, 0.5) }, 0.3, r.NextSeed());
                Normalize(sp, 1f);
                Mix(s, sp, 0, 0.45 * (1 - k / 12.0) * r.J(0.3));
                t += 0.008 * Math.Pow(1.25, k) * r.J(0.3);
            }
            var knock = Modal(n, Samples(0.004), new[] { M(260, 0.03, 1.0), M(610, 0.015, 0.6), M(1050, 0.007, 0.3) }, 1.5, r.NextSeed());
            Normalize(knock, 1f);
            Mix(s, knock, 0, 0.6);
            var whoosh = Sweep(n, Samples(0.01), 0.25, x => 900 * Math.Pow(300.0 / 900, Math.Min(1, x / 0.2)), 1.2,
                x => x < 0.03 ? x / 0.03 : Math.Exp(-(x - 0.03) / 0.07), ref r);
            Mix(s, whoosh, 0, 0.35);
            HighPass2(s, 80);
            LowPass2(s, 4500);
            Fades(s, 0.0002, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Сход: удилище разгружается «фьюх» (750→260 Гц вниз), леска провисает мягким «тп» 140 Гц,
        /// потом тихо — пузырь 900 Гц и 600 Гц и водоворот уходящей рыбы. Всё вниз по высоте, без «награды». 0,9 с.
        /// </summary>
        public static float[] Escape()
        {
            var r = new Rng(0xE5CA0001u);
            int n = Samples(0.9);
            var s = new float[n];
            var fwip = Sweep(n, 0, 0.22, t => 750 * Math.Pow(260.0 / 750, Math.Min(1, t / 0.2)), 1.4,
                t => t < 0.03 ? Math.Sin(Math.PI / 2 * t / 0.03) : Math.Exp(-(t - 0.03) / 0.06), ref r);
            Mix(s, fwip, 0, 0.6);
            var slack = Modal(n, Samples(0.01), new[] { M(140, 0.025, 1.0), M(290, 0.012, 0.4) }, 2.5, r.NextSeed());
            Normalize(slack, 1f);
            Mix(s, slack, 0, 0.3);
            AddBubble(s, Samples(0.34), 3.3, 0.35);
            AddBubble(s, Samples(0.41), 5.0, 0.3);
            var swirl = Swirl(n, t => 380 * Math.Pow(0.7, Math.Max(0, t - 0.3) / 0.5), 2.0,
                t => t < 0.3 ? 0 : Math.Sin(Math.PI * Math.Min(1, (t - 0.3) / 0.55)), ref r);
            Normalize(swirl, 1f);
            Mix(s, swirl, 0, 0.12);
            HighPass2(s, 80);
            LowPass2(s, 2500);
            Fades(s, 0.0005, 0.05);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// «Дзинь» лески — удар рыбы близко к пределу (редкий, яркость можно): короткий «вжик» шума 1,6–3 кГц (12 мс)
        /// и натянутая струна — 5 гармоник основного тона ≈ 560 Гц (вариант 0), 627 Гц (1) или 504 Гц (2), ±3%,
        /// с лёгкой негармоничностью, τ 110/75/50/32/20 мс, высота за 50 мс опускается на 4% (рывок отпустил). 0,3 с.
        /// </summary>
        public static float[] LineZing(int variant, uint seed)
        {
            var r = new Rng(seed);
            int n = Samples(0.3);
            var s = new float[n];
            double[] steps = { 1.0, 1.12, 0.9 };
            double f0 = 560 * steps[((variant % 3) + 3) % 3] * r.J(0.03);
            int attack = Samples(0.0015);
            for (int k = 1; k <= 5; k++)
            {
                double f = f0 * k * (1 + 0.002 * k * k);
                double amp = Math.Pow(k, -0.8) * r.J(0.15);
                double tau = 0.11 * Math.Pow(k, -0.7) * r.J(0.15);
                double phase = r.U() * TwoPi;
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / SampleRate;
                    phase += TwoPi * f * (1 - 0.04 * (1 - Math.Exp(-t / 0.05))) / SampleRate;
                    double env = (i < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * i / attack) : 1.0) * Math.Exp(-t / tau);
                    s[i] += (float)(amp * env * Math.Sin(phase));
                }
            }
            Normalize(s, 1f);
            var zip = new Svf(r.Range(1600, 3000), 1.5);
            int zl = Samples(0.012);
            for (int i = 0; i < zl; i++)
                s[i] += (float)(0.35 * zip.Band(r.S()) * 1.5 * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / zl)));
            HighPass2(s, 300);
            LowPass2(s, 4000);
            Fades(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return s;
        }

        // =================================================================================================
        // Заброс
        // =================================================================================================

        /// <summary>
        /// Заброс: свист удилища (полоса 250→1250→450 Гц, Q 1,2, 0,32 с), затем леска сходит со шпули —
        /// «з-з-з» из зёрен-витков, частота 140→25 в секунду за 1,1 с, плюс мягкое шипение 1,5 кГц. 1,3 с.
        /// </summary>
        public static float[] Cast()
        {
            var r = new Rng(0xCA570001u);
            int n = Samples(1.3);
            var s = new float[n];
            var whip = Sweep(n, 0, 0.34, t => t < 0.16 ? 250 * Math.Pow(1250.0 / 250, t / 0.16) : 1250 * Math.Pow(450.0 / 1250, Math.Min(1, (t - 0.16) / 0.16)), 1.2,
                t => Math.Pow(Math.Sin(Math.PI * Math.Min(1, t / 0.32)), 2), ref r);
            Mix(s, whip, 0, 0.8);

            double tt = 0.2;
            while (tt < 1.25)
            {
                double x = (tt - 0.2) / 1.05;
                double rate = 25 + 115 * Math.Exp(-x * 3);
                double amp = (1 - x) * (1 - x) * r.Range(0.5, 1.0);
                int at = Samples(tt);
                var g = new Svf(1300 * r.J(0.2), 2.0);
                int len = Samples(0.003);
                for (int i = 0; i < len * 3 && at + i < n; i++)
                {
                    double env = i < len ? 0.5 - 0.5 * Math.Cos(Math.PI * i / len) : Math.Exp(-(i - len) / (0.5 * len));
                    s[at + i] += (float)(0.12 * amp * env * g.Band(r.S()) * 3);
                }
                tt += r.Range(0.7, 1.3) / rate;
            }
            var hiss = Swirl(n, t => 1500, 0.8, t => t < 0.2 ? 0 : Math.Min(1, (t - 0.2) / 0.03) * Math.Exp(-(t - 0.2) / 0.4), ref r);
            Normalize(hiss, 1f);
            Mix(s, hiss, 0, 0.1);
            HighPass2(s, 120);
            LowPass2(s, 2500);
            Fades(s, 0.002, 0.05);
            Normalize(s, 0.9f);
            return s;
        }

        // =================================================================================================
        // Кирпичики финалов
        // =================================================================================================

        /// <summary>Влажный шлепок рыбы: шум 25 мс в полосе 250–1400 Гц и тело 230 Гц.</summary>
        private static float[] Flop(int n, int at, uint seed)
        {
            var r = new Rng(seed);
            var s = new float[n];
            var f = new Svf(r.Range(500, 800), 0.9);
            for (int i = 0; i < Samples(0.06) && at + i < n; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.003 ? t / 0.003 : Math.Exp(-(t - 0.003) / 0.012);
                s[at + i] = (float)(f.Band(r.S()) * env);
            }
            Normalize(s, 1f);
            var body = Modal(n, at, new[] { M(230 * r.J(0.08), 0.02, 1.0), M(520 * r.J(0.08), 0.008, 0.4) }, 2.0, r.NextSeed());
            Normalize(body, 1f);
            for (int i = 0; i < n; i++)
                s[i] += 0.6f * body[i];
            return s;
        }

        /// <summary>Деревянная пластина (маримба): моды f, 3,93f (τ 60 мс), 9,2f (τ 15 мс), мягкая колотушка 2,5 мс.</summary>
        private static void AddBar(float[] s, int at, double f, double sustain, double gain)
        {
            var bar = Modal(s.Length, at, new[] { M(f, sustain, 1.0), M(3.93 * f, 0.06, 0.22), M(9.2 * f, 0.015, 0.05) }, 2.5, (uint)(f * 100));
            Normalize(bar, 1f);
            for (int i = 0; i < s.Length; i++)
                s[i] += (float)(gain * bar[i]);
        }
    }
}
