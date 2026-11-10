using System;

namespace Fishing.Unity
{
    /// <summary>
    /// «Натуральная» палитра вываживания: хорошо смазанная безынерционная катушка и тихое утреннее озеро,
    /// записанные вблизи. Моно, 22050 Гц, без ассетов и без движка; один и тот же вызов даёт те же сэмплы.
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
    /// </summary>
    internal static class NaturalSynth
    {
        public const int SampleRate = 22050;

        private const double TwoPi = 2.0 * Math.PI;

        // =================================================================================================
        // Катушка
        // =================================================================================================

        /// <summary>
        /// Проход ротора (одно зерно на оборот ротора, 14 см лески): мягкое «ш» лески по ролику —
        /// тёмный шум (белый через однополюсный 1 кГц) в полосе 760 Гц ±10%, Q 0,85, атака 12 мс,
        /// спад τ 20 мс ±15%, срез 1,9 кГц — плюс глухой «тук» дужки
        /// (290/585 Гц, −13 дБ) и тихий щелчок ролика (1180/1760 Гц, −16 дБ, через 3–8 мс). 95 мс.
        /// </summary>
        public static float[] RotorGrain(uint seed)
        {
            var r = new Rng(seed);
            int n = Samples(0.095);
            var swish = new float[n];
            var bp = new Svf(760 * r.J(0.10), 0.85);
            double att = 0.012 * r.J(0.2), tau = 0.020 * r.J(0.15);
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
                s[i] = swish[i] + 0.22f * body[i] + 0.16f * tick[i];
            LowPass2(s, 1900);
            HighPass2(s, 110);
            Fades(s, 0.0004, 0.006);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Такт шестерни — раз на оборот ручки (85 см): пластмассово-деревянный «ток» 410/960/1580 Гц,
        /// τ 16/8/3,5 мс (tg φ ≈ 0,05 — не звенит), мягкое возбуждение 0,8 мс, срез 2,4 кГц. 80 мс.
        /// </summary>
        public static float[] GearTock(uint seed)
        {
            var r = new Rng(seed);
            int n = Samples(0.08);
            var s = Modal(n, 0, new[]
            {
                M(410 * r.J(0.03), 0.016 * r.J(0.15), 1.0),
                M(960 * r.J(0.03), 0.008 * r.J(0.15), 0.5 * r.J(0.12)),
                M(1580 * r.J(0.03), 0.0035 * r.J(0.15), 0.18 * r.J(0.12)),
            }, 0.8, r.NextSeed());
            LowPass2(s, 2400);
            HighPass2(s, 120);
            Fades(s, 0.0003, 0.008);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Постель укладки лески на шпулю (петля 4 с): розовый шум в полосе 950 Гц (Q 0,6), срез 220 Гц
        /// снизу и 2 кГц сверху, плюс редкая «крупка» витков (30 в секунду, 2,5 мс, 1,4 кГц) и медленный
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
            FilterCircular(s, (ref Svf f) => f.Set(950, 0.6), Band.Band);
            FilterCircular(s, (ref Svf f) => f.Set(2000, 0.707), Band.Low);
            FilterCircular(s, (ref Svf f) => f.Set(220, 0.707), Band.High);
            Normalize(s, 1f);

            int grains = (int)(30 * length);
            for (int k = 0; k < grains; k++)
            {
                int at = (int)(r.U() * n);
                double amp = 0.15 + 0.25 * r.U();
                var g = new Svf(1400 * r.J(0.15), 1.5);
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
        /// Натуга (вместо «рычания»): срыв-прилипание в механизме — 1 импульс (малое зерно) или 2–4 импульса
        /// через 8–16 мс (большое) через резонаторы 250/395/560/790/1120 Гц с добротностью 3,5–5
        /// (форманты скрипа, сдвинутые вверх, чтобы их играл динамик телефона). 45/70 мс.
        /// </summary>
        public static float[] StrainGrain(uint seed, bool big)
        {
            var r = new Rng(seed);
            int n = Samples(big ? 0.07 : 0.045);
            var exc = new float[n];
            int pulses = big ? 2 + (int)(r.U() * 3) : 1;
            double t0 = 0.001;
            for (int p = 0; p < pulses; p++)
            {
                double amp = p == 0 ? 1.0 : 0.45 + 0.45 * r.U();
                AddPulse(exc, Samples(t0), 0.0007, amp);
                t0 += r.Range(0.008, 0.016);
            }
            var s = Resonate(exc, new[] { 250.0, 395, 560, 790, 1120 }, new[] { 1.0, 0.85, 0.7, 0.45, 0.28 }, 3.5, 5.0, ref r);
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
            var modes = new System.Collections.Generic.List<Mode>
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
        /// Гул натянутой лески (петля 4 с): шумовые «струны» 330 Гц (Q 14), 662 Гц (Q 10, −7 дБ),
        /// 1320 Гц (Q 6, −18 дБ); «дыхание» 0,25 и 0,5 Гц глубиной 12 и 5%. Звучит только у предела.
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
            FilterCircular(a, (ref Svf f) => f.Set(330, 14), Band.BandNorm);
            FilterCircular(b, (ref Svf f) => f.Set(662, 10), Band.BandNorm);
            FilterCircular(c, (ref Svf f) => f.Set(1320, 6), Band.BandNorm);
            var s = new float[n];
            double p1 = r.U() * TwoPi, p2 = r.U() * TwoPi;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double breath = 1 + 0.12 * Math.Sin(TwoPi * x + p1) + 0.05 * Math.Sin(TwoPi * 2 * x + p2);
                s[i] = (float)((a[i] + 0.45 * b[i] + 0.12 * c[i]) * breath);
            }
            FilterCircular(s, (ref Svf f) => f.Set(150, 0.707), Band.High);
            FilterCircular(s, (ref Svf f) => f.Set(1600, 0.707), Band.Low);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>
        /// Потрескивание лески на кольцах у самого предела: крошечный «тик» 880/1450/2250 Гц, τ 3/2/1 мс,
        /// возбуждение 0,25 мс, срез 300 Гц–3 кГц. 30 мс.
        /// </summary>
        public static float[] LineCrackle(uint seed)
        {
            var r = new Rng(seed);
            int n = Samples(0.03);
            var s = Modal(n, 0, new[]
            {
                M(880 * r.J(0.06), 0.003 * r.J(0.2), 0.6 * r.J(0.15)),
                M(1450 * r.J(0.06), 0.002 * r.J(0.2), 1.0),
                M(2250 * r.J(0.06), 0.001 * r.J(0.2), 0.4 * r.J(0.15)),
            }, 0.25, r.NextSeed());
            LowPass2(s, 3000);
            HighPass2(s, 300);
            Fades(s, 0.0002, 0.004);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>
        /// Скрип удилища: цепочка срывов (18→60→18 в секунду дугой, у громкого 25→90; интервалы ×0,6–1,5,
        /// сила каждого — корень из накопленного) через резонаторы 180/320/520/760/1100 Гц, Q 2,5–4,
        /// срез 125 Гц–2,6 кГц. Тихий 0,25–0,45 с, громкий 0,3–0,5 с и с более яркими верхними формантами.
        /// </summary>
        public static float[] RodCreak(uint seed, bool loud)
        {
            var r = new Rng(seed);
            double length = loud ? r.Range(0.3, 0.5) : r.Range(0.25, 0.45);
            int n = Samples(length + 0.04);
            var exc = new float[n];
            double r0 = loud ? 25 : 18, r1 = loud ? 90 : 60;
            double t = 0.008;
            while (t < length)
            {
                double x = t / length;
                double arch = Math.Sin(Math.PI * x);
                double rate = r0 + (r1 - r0) * arch;
                double gap = r.Range(0.6, 1.5) / rate;
                double amp = Math.Sqrt(Math.Min(2.0, gap * rate)) * (0.25 + 0.75 * Math.Pow(arch, 0.7));
                AddPulse(exc, Samples(t), 0.0006, amp);
                t += gap;
            }
            double up = loud ? 1.4 : 1.0;
            var s = Resonate(exc, new[] { 180.0, 320, 520, 760, 1100 }, new[] { 1.0, 0.8, 0.6, 0.4 * up, 0.25 * up }, 2.5, 4.0, ref r);
            HighPass2(s, 125);
            LowPass2(s, 2600);
            Fades(s, 0.002, 0.015);
            Normalize(s, 0.9f);
            return s;
        }

        // =================================================================================================
        // Подсечка и телеграфы
        // =================================================================================================

        /// <summary>
        /// Подсечка: свист удилища (полоса шума 350→1150→600 Гц, Q 1,3, пик на 55 мс), через 45 мс —
        /// леска встала в струну: «тумк» 160/322/488/980 Гц (τ 70/40/22/8 мс, мягкое возбуждение 3 мс)
        /// и тихий «дзынь» лески 640/1300 Гц. 0,4 с.
        /// </summary>
        public static float[] Hookset()
        {
            var r = new Rng(0x400C5E70u);
            int n = Samples(0.4);
            var s = new float[n];
            var swish = Sweep(n, 0, 0.16, t => t < 0.055 ? 350 * Math.Pow(1150.0 / 350, t / 0.055) : 1150 * Math.Pow(600.0 / 1150, (t - 0.055) / 0.105),
                1.3, t => t < 0.05 ? Math.Pow(Math.Sin(Math.PI / 2 * t / 0.05), 2) : Math.Exp(-(t - 0.05) / 0.035), ref r);
            Mix(s, swish, 0, 0.5);
            var thump = Modal(n, Samples(0.045), new[] { M(160, 0.07, 1.0), M(322, 0.04, 0.6), M(488, 0.022, 0.35), M(980, 0.008, 0.15) }, 3.0, r.NextSeed());
            Normalize(thump, 1f);
            Mix(s, thump, 0, 0.9);
            var zing = Modal(n, Samples(0.047), new[] { M(640, 0.025, 1.0), M(1300, 0.012, 0.5) }, 0.6, r.NextSeed());
            Normalize(zing, 1f);
            Mix(s, zing, 0, 0.12);
            HighPass2(s, 70);
            LowPass2(s, 3000);
            Fades(s, 0.0005, 0.02);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Перед рывком: рыба мотает головой — два стука по бланку удилища «тук-тук» —
        /// полый «ток» 190/420/690/1050/1500 Гц (τ 25/20/12/7/3 мс: слышен и в динамике телефона), второй через 85 мс тише и на 3% ниже.</summary>
        public static float[] TeleJerk()
        {
            int n = Samples(0.26);
            var s = new float[n];
            var a = Modal(n, 0, new[] { M(190, 0.025, 0.5), M(420, 0.02, 1.0), M(690, 0.012, 0.7), M(1050, 0.007, 0.4), M(1500, 0.003, 0.15) }, 1.5, 0x7E1E0001u);
            var b = Modal(n, Samples(0.085), new[] { M(184, 0.025, 0.5), M(407, 0.02, 1.0), M(669, 0.012, 0.7), M(1020, 0.007, 0.4), M(1455, 0.003, 0.15) }, 1.5, 0x7E1E0002u);
            Mix(s, a, 0, 1.0);
            Mix(s, b, 0, 0.65);
            HighPass2(s, 90);
            LowPass2(s, 2400);
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
        /// со свистом 1000→280 Гц, ослабшая леска пару раз шлёпает. 0,6 с.
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
            Mix(s, whoosh, 0, 0.6);
            for (int k = 0; k < 3; k++)
            {
                var slap = Modal(n, Samples(0.18 + 0.07 * k + 0.02 * r.U()), new[] { M(600 * r.J(0.1), 0.004, 1.0), M(1300 * r.J(0.1), 0.002, 0.5) }, 0.5, r.NextSeed());
                Normalize(slap, 1f);
                Mix(s, slap, 0, 0.12 * Math.Pow(0.6, k));
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
        // Утреннее озеро (фон)
        // =================================================================================================

        /// <summary>
        /// Утреннее озеро, бесшовная петля (по умолчанию 64 с):
        /// - плеск: шум со срезом 420 Гц и медленными «накатами» — гладкая случайная кривая из синусов с целым
        ///   числом периодов на петлю (0,08–0,45 Гц, амплитуды по Рэлею), глубина ±40% и отдельные набеги на берег раз в 1,6–4,5 с (полоса 300–650 Гц, атака 60–120 мс,
        ///   спад 150–350 мс, 1–3 пузыря);
        /// - ветер в камыше: полоса 1,4 кГц, срез 500 Гц–2,6 кГц, порывы 0,04–0,2 Гц (та же гладкая случайная кривая),
        ///   шорох листьев — крошечные зёрна 1,1–2,2 кГц, тем гуще (8→48 в секунду), чем сильнее порыв;
        /// - птицы вдали: иволга (флейтовое «фиу-лиу», 1,2–1,7 кГц) трижды, кукушка 690→560 Гц (серия из пяти),
        ///   два тихих «цип»; всё через срез и короткое эхо;
        /// - редкие пузырьки у берега и далёкий всплеск рыбы.
        /// Всё, что выходит за конец, заворачивается в начало; фильтры — по кругу.
        /// </summary>
        public static float[] LakeMorning(double seconds = 64)
        {
            int n = Samples(seconds);
            var r = new Rng(0x1A4E0001u);
            var lap = new float[n];
            for (int i = 0; i < n; i++)
                lap[i] = (float)r.S();
            FilterCircular(lap, (ref Svf f) => f.Set(420, 0.707), Band.Low);
            FilterCircular(lap, (ref Svf f) => f.Set(70, 0.707), Band.High);
            Normalize(lap, 1f);
            var swell = SmoothRandom(n, (int)Math.Round(seconds / 12), (int)Math.Round(seconds / 2.2), ref r);
            for (int i = 0; i < n; i++)
                lap[i] *= (float)(1 + 0.4 * swell[i]);

            var slosh = new float[n];
            double tt = r.Range(0.3, 1.5);
            while (tt < seconds)
            {
                AddSlosh(slosh, Samples(tt), ref r);
                tt += r.Range(1.6, 4.5);
            }

            var breeze = new float[n];
            for (int i = 0; i < n; i++)
                breeze[i] = (float)r.S();
            FilterCircular(breeze, (ref Svf f) => f.Set(1400, 0.7), Band.Band);
            FilterCircular(breeze, (ref Svf f) => f.Set(2600, 0.707), Band.Low);
            FilterCircular(breeze, (ref Svf f) => f.Set(500, 0.707), Band.High);
            Normalize(breeze, 1f);
            var gustShape = SmoothRandom(n, (int)Math.Round(seconds / 25), (int)Math.Round(seconds / 5), ref r);
            var gust = new double[n];
            for (int i = 0; i < n; i++)
            {
                gust[i] = Math.Max(0.12, Math.Min(1, 0.45 + 0.5 * gustShape[i]));
                breeze[i] *= (float)gust[i];
            }
            // Шорох листьев: крошечные шумовые зёрна, тем гуще, чем сильнее порыв.
            double lt = 0;
            while (lt < seconds)
            {
                int at = Samples(lt);
                double g = gust[Math.Min(n - 1, at)];
                var f = new Svf(r.Range(1100, 2200), 2.0);
                int len = Samples(r.Range(0.004, 0.01));
                double amp = r.Range(0.3, 1.0) * g;
                for (int i = 0; i < len * 3; i++)
                {
                    double env = i < len ? 0.5 - 0.5 * Math.Cos(Math.PI * i / len) : Math.Exp(-(i - len) / (0.5 * len));
                    breeze[(at + i) % n] += (float)(0.25 * amp * env * f.Band(r.S()) * 2);
                }
                lt += r.Range(0.5, 1.5) / (8 + 40 * g);
            }

            var birds = new float[n];
            foreach (double at in new[] { 6.5, 29.0, 50.5 })
                AddOriole(birds, Samples(at * seconds / 64), ref r);
            double ck = 15.0 * seconds / 64;
            for (int k = 0; k < 5; k++)
            {
                AddCuckoo(birds, Samples(ck), ref r);
                ck += r.Range(0.82, 0.92);
            }
            foreach (double at in new[] { 23.0, 41.5 })
                AddChirp(birds, Samples(at * seconds / 64), ref r);
            var birdsWet = (float[])birds.Clone();
            AddRoomCircular(birdsWet, -12);

            var near = new float[n];
            double bt = r.Range(0.5, 2);
            while (bt < seconds)
            {
                AddBubble(near, Samples(bt), r.Range(3, 9), r.Range(0.3, 1.0), true);
                bt += r.Range(1.0, 3.0);
            }
            var far = new float[n];
            int fishAt = Samples(37.0 * seconds / 64);
            var fish = Splash(0xF15E0001u, 0, true);
            for (int i = 0; i < fish.Length; i++)
                far[(fishAt + i) % n] += fish[i];

            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = 1.0f * lap[i] + 0.55f * slosh[i] + 0.16f * breeze[i] + 0.12f * birdsWet[i] + 0.05f * near[i] + 0.07f * far[i];
            FilterCircular(s, (ref Svf f) => f.Set(60, 0.707), Band.High);
            Normalize(s, 0.7f);
            return s;
        }

        // =================================================================================================
        // Кирпичики
        // =================================================================================================

        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x == 0 ? 1u : x;
        }

        private static int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);

        /// <summary>Дуга 0…1…0 на x ∈ [0, 1] с вершиной в peakAt: sin(π·x^p)^1,5.</summary>
        private static double Arch(double x, double peakAt)
        {
            if (x <= 0 || x >= 1)
                return 0;
            double p = Math.Log(0.5) / Math.Log(peakAt);
            return Math.Pow(Math.Sin(Math.PI * Math.Pow(x, p)), 1.5);
        }

        private struct Mode
        {
            public double F, Tau, A;
        }

        private static Mode M(double f, double tau, double a) => new Mode { F = f, Tau = tau, A = a };

        /// <summary>
        /// Сумма затухающих синусоид a·e^(−t/τ)·sin(2πft), свёрнутая с приподнятым косинусом длиной exciterMs
        /// (чем длиннее — тем мягче «колотушка»), с лёгкой шумовой фактурой возбуждения.
        /// </summary>
        private static float[] Modal(int n, int start, Mode[] modes, double exciterMs, uint seed)
        {
            var raw = new double[n];
            foreach (var m in modes)
            {
                if (m.F >= 0.45 * SampleRate)
                    continue;
                double w = TwoPi * m.F / SampleRate, k = Math.Exp(-1.0 / (m.Tau * SampleRate));
                double env = 1;
                for (int i = start; i < n; i++)
                {
                    raw[i] += m.A * env * Math.Sin(w * (i - start));
                    env *= k;
                }
            }
            var r = new Rng(seed);
            int e = Math.Max(2, (int)Math.Round(exciterMs * SampleRate / 1000));
            var kernel = new double[e];
            double sum = 0;
            for (int i = 0; i < e; i++)
            {
                kernel[i] = (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / e)) * (0.75 + 0.25 * r.S());
                sum += kernel[i];
            }
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double acc = 0;
                for (int j = 0; j < e && j <= i; j++)
                    acc += kernel[j] * raw[i - j];
                s[i] = (float)(acc / sum);
            }
            return s;
        }

        /// <summary>Возбуждение (импульсы) через параллельные резонаторы (нормированные полосовые) со случайной добротностью.</summary>
        private static float[] Resonate(float[] exc, double[] freqs, double[] amps, double qMin, double qMax, ref Rng r)
        {
            int n = exc.Length;
            var s = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                var f = new Svf(freqs[k] * r.J(0.06), r.Range(qMin, qMax));
                for (int i = 0; i < n; i++)
                    s[i] += (float)(amps[k] * f.Band(exc[i]) / f.Q);
            }
            return s;
        }

        private static void AddPulse(float[] s, int at, double seconds, double amp)
        {
            int len = Math.Max(3, (int)Math.Round(seconds * SampleRate));
            for (int i = 0; i < len && at + i < s.Length; i++)
                s[at + i] += (float)(amp * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / len)));
        }

        /// <summary>Шум через полосовой фильтр с меняющейся частотой и огибающей (для свистов): длина seconds от start.</summary>
        private static float[] Sweep(int n, int start, double seconds, Func<double, double> hz, double q, Func<double, double> env, ref Rng r)
        {
            var s = new float[n];
            var f = new Svf(hz(0), q);
            int len = Math.Min(n - start, Samples(seconds));
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / SampleRate;
                f.Set(hz(t), q);
                s[start + i] = (float)(f.Band(r.S()) * env(t));
            }
            Normalize(s, 1f);
            return s;
        }

        /// <summary>Водоворот: коричневатый шум через полосовой фильтр с меняющейся частотой.</summary>
        private static float[] Swirl(int n, Func<double, double> hz, double q, Func<double, double> env, ref Rng r)
        {
            var s = new float[n];
            var f = new Svf(hz(0), q);
            double brown = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                brown = 0.97 * brown + 0.25 * r.S();
                f.Set(hz(t), q);
                s[i] = (float)(f.Band(brown) * env(t));
            }
            return s;
        }

        /// <summary>
        /// Пузырь Миннарта: f₀ = 3 м/с ÷ r, частота растёт f(t) = f₀(1 + 0,1·t/τ), τ = 4 мс + 3 мс × r(мм),
        /// амплитуда ∝ r. wrap — заворачивать хвост в начало (для петель).
        /// </summary>
        private static void AddBubble(float[] s, int start, double rMm, double gain, bool wrap = false)
        {
            double f0 = 3.0 / (rMm / 1000.0);
            double tau = 0.004 + 0.003 * rMm;
            int len = (int)(tau * 6 * SampleRate);
            double phase = 0, amp = gain * rMm / 8.0;
            int onset = Math.Max(2, Samples(0.0005));
            for (int i = 0; i < len; i++)
            {
                int at = start + i;
                if (wrap)
                    at %= s.Length;
                else if (at >= s.Length)
                    break;
                double t = (double)i / SampleRate;
                double f = f0 * (1 + 0.1 * t / tau);
                if (f >= 0.45 * SampleRate)
                    break;
                phase += TwoPi * f / SampleRate;
                double a = i < onset ? 0.5 - 0.5 * Math.Cos(Math.PI * i / onset) : 1.0;
                s[at] += (float)(amp * a * Math.Sin(phase) * Math.Exp(-t / tau));
            }
        }

        /// <summary>
        /// Гладкая случайная периодическая кривая (≈ −1…1): синусы с целым числом периодов k ∈ [minCycles, maxCycles]
        /// на длину n, амплитуды по Рэлею ÷ √k, случайные фазы. Периодична (петля без шва), но без заметного периода внутри.
        /// </summary>
        private static double[] SmoothRandom(int n, int minCycles, int maxCycles, ref Rng r)
        {
            int count = maxCycles - minCycles + 1;
            var amp = new double[count];
            var phase = new double[count];
            for (int k = 0; k < count; k++)
            {
                amp[k] = Math.Sqrt(-2 * Math.Log(1 - 0.999 * r.U())) / Math.Sqrt(minCycles + k);
                phase[k] = r.U() * TwoPi;
            }
            var y = new double[n];
            double max = 1e-9;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n, v = 0;
                for (int k = 0; k < count; k++)
                    v += amp[k] * Math.Sin(TwoPi * (minCycles + k) * x + phase[k]);
                y[i] = v;
                max = Math.Max(max, Math.Abs(v));
            }
            for (int i = 0; i < n; i++)
                y[i] /= max;
            return y;
        }

        /// <summary>Радиус по степенному закону p(r) ∝ r^−2,5 на [rMin, rMax].</summary>
        private static double PowerLawRadius(ref Rng r, double rMin, double rMax)
        {
            const double a = -1.5;
            double u = r.U();
            return Math.Pow(Math.Pow(rMin, a) + u * (Math.Pow(rMax, a) - Math.Pow(rMin, a)), 1 / a);
        }

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

        /// <summary>Иволга вдали: флейтовые ноты с глиссандо «фиу-лиу-лиу» 1,2–1,7 кГц.</summary>
        private static void AddOriole(float[] s, int at, ref Rng r)
        {
            var notes = new (double f0, double f1, double len, double gap)[]
            {
                (1450, 1650, 0.12, 0.03), (1250, 1250, 0.09, 0.02), (1650, 1330, 0.16, 0.0),
            };
            double t0 = 0;
            double k = r.J(0.04);
            foreach (var nt in notes)
            {
                AddWhistle(s, at + Samples(t0), nt.f0 * k, nt.f1 * k, nt.len, 1.0, ref r);
                t0 += nt.len + nt.gap;
            }
        }

        /// <summary>Кукушка вдали: «ку» 700→680 Гц 150 мс, пауза 120 мс, «ку» 580→550 Гц 230 мс.</summary>
        private static void AddCuckoo(float[] s, int at, ref Rng r)
        {
            AddWhistle(s, at, 700, 680, 0.15, 0.9, ref r, 0.12, 0.02);
            AddWhistle(s, at + Samples(0.27), 580, 550, 0.23, 0.8, ref r, 0.12, 0.02);
        }

        /// <summary>Тихое «цип» мелкой птицы: 2,4→2,0 кГц, 45 мс.</summary>
        private static void AddChirp(float[] s, int at, ref Rng r)
        {
            AddWhistle(s, at, 2400, 2000, 0.045, 0.25, ref r);
            AddWhistle(s, at + Samples(0.09), 2350, 1950, 0.04, 0.2, ref r);
        }

        /// <summary>Свист: синус с глиссандо, 2-я гармоника h2, лёгкий «дых» (шум в той же полосе), огибающая — приподнятый синус.</summary>
        private static void AddWhistle(float[] s, int at, double f0, double f1, double len, double gain, ref Rng r, double h2 = 0.06, double attack = 0.015)
        {
            int n = s.Length, m = Samples(len);
            double phase = 0;
            var breath = new Svf(f0, 8);
            for (int i = 0; i < m; i++)
            {
                double t = (double)i / SampleRate, x = (double)i / m;
                double f = f0 * Math.Pow(f1 / f0, x);
                phase += TwoPi * f / SampleRate;
                double env = Math.Min(1, t / attack) * Math.Min(1, (len - t) / 0.02) * (1 - 0.3 * x);
                breath.Set(f, 8);
                double v = Math.Sin(phase) + h2 * Math.Sin(2 * phase) + 0.15 * breath.Band(r.S()) * 8;
                s[(at + i) % n] += (float)(gain * env * v);
            }
        }

        /// <summary>Короткий хвост «эха» (4 гребёнки 558/594/638/678 сэмплов, обратная связь 0,84, затухание 0,2, два всепропускающих 278/220).</summary>
        private static void AddRoom(float[] s, double wetDb)
        {
            var wet = Room(s, false);
            double g = Math.Pow(10, wetDb / 20);
            Normalize(wet, (float)Peak(s));
            for (int i = 0; i < s.Length; i++)
                s[i] += (float)(g * wet[i]);
        }

        private static void AddRoomCircular(float[] s, double wetDb)
        {
            var wet = Room(s, true);
            double g = Math.Pow(10, wetDb / 20);
            Normalize(wet, (float)Peak(s));
            for (int i = 0; i < s.Length; i++)
                s[i] += (float)(g * wet[i]);
        }

        private static float[] Room(float[] x, bool circular)
        {
            int n = x.Length;
            int[] combs = { 558, 594, 638, 678 };
            int[] alls = { 278, 220 };
            var y = new float[n];
            int passes = circular ? 2 : 1;
            foreach (int d in combs)
            {
                var buf = new double[d];
                int idx = 0;
                double store = 0;
                for (int p = 0; p < passes; p++)
                    for (int i = 0; i < n; i++)
                    {
                        double o = buf[idx];
                        store = o * 0.8 + store * 0.2;
                        buf[idx] = x[i] + store * 0.84;
                        idx = (idx + 1) % d;
                        if (p == passes - 1)
                            y[i] += (float)(o * 0.25);
                    }
            }
            foreach (int d in alls)
            {
                var buf = new double[d];
                int idx = 0;
                var z = new float[n];
                for (int p = 0; p < passes; p++)
                    for (int i = 0; i < n; i++)
                    {
                        double b = buf[idx];
                        double o = -y[i] + b;
                        buf[idx] = y[i] + b * 0.5;
                        idx = (idx + 1) % d;
                        if (p == passes - 1)
                            z[i] = (float)o;
                    }
                y = z;
            }
            return y;
        }

        private static void Mix(float[] dst, float[] src, int at, double gain)
        {
            for (int i = 0; i < src.Length && at + i < dst.Length; i++)
                dst[at + i] += (float)(gain * src[i]);
        }

        private static double Peak(float[] s)
        {
            double m = 0;
            foreach (var v in s)
                m = Math.Max(m, Math.Abs(v));
            return m;
        }

        private static void Normalize(float[] s, float peak)
        {
            double m = Peak(s);
            if (m <= 0)
                return;
            float k = (float)(peak / m);
            for (int i = 0; i < s.Length; i++)
                s[i] *= k;
        }

        /// <summary>Вход приподнятым косинусом за fadeIn, выход — за fadeOut (в ноль).</summary>
        private static void Fades(float[] s, double fadeIn, double fadeOut)
        {
            int a = Math.Max(1, Samples(fadeIn)), b = Math.Max(1, Samples(fadeOut));
            for (int i = 0; i < a && i < s.Length; i++)
                s[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / a));
            for (int i = 0; i < b && i < s.Length; i++)
                s[s.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / b));
        }

        private static void LowPass2(float[] s, double hz)
        {
            var f = new Svf(hz, 0.707);
            for (int i = 0; i < s.Length; i++)
                s[i] = (float)f.Low(s[i]);
        }

        private static void HighPass2(float[] s, double hz)
        {
            var f = new Svf(hz, 0.707);
            for (int i = 0; i < s.Length; i++)
                s[i] = (float)f.High(s[i]);
        }

        private enum Band { Low, Band, BandNorm, High }

        private delegate void Setup(ref Svf f);

        /// <summary>Фильтр по кругу: первый проход прогревает состояние концом петли, второй пишет — шва нет.</summary>
        private static void FilterCircular(float[] s, Setup setup, Band band)
        {
            var f = new Svf(1000, 0.707);
            setup(ref f);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < s.Length; i++)
                {
                    double y = band == Band.Low ? f.Low(s[i]) : band == Band.High ? f.High(s[i])
                        : band == Band.BandNorm ? f.Band(s[i]) / f.Q : f.Band(s[i]);
                    if (pass == 1)
                        s[i] = (float)y;
                }
        }

        /// <summary>Розовый шум из белого (фильтр Келлета), по кругу.</summary>
        private static void PinkCircular(float[] s)
        {
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            var o = new float[s.Length];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < s.Length; i++)
                {
                    double w = s[i];
                    b0 = 0.99886 * b0 + w * 0.0555179;
                    b1 = 0.99332 * b1 + w * 0.0750759;
                    b2 = 0.96900 * b2 + w * 0.1538520;
                    b3 = 0.86650 * b3 + w * 0.3104856;
                    b4 = 0.55000 * b4 + w * 0.5329522;
                    b5 = -0.7616 * b5 - w * 0.0168980;
                    double y = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362;
                    b6 = w * 0.115926;
                    if (pass == 1)
                        o[i] = (float)y;
                }
            Array.Copy(o, s, s.Length);
        }

        /// <summary>Шум и случайности: xorshift32, зерно перемешивается хешем.</summary>
        internal struct Rng
        {
            private uint _s;

            public Rng(uint seed)
            {
                _s = Hash(seed ^ 0x9E3779B9u);
            }

            public uint Next()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return _s;
            }

            public uint NextSeed() => Hash(Next());

            /// <summary>[0, 1).</summary>
            public double U() => (Next() >> 8) * (1.0 / 16777216.0);

            /// <summary>[−1, 1).</summary>
            public double S() => U() * 2 - 1;

            /// <summary>1 ± frac.</summary>
            public double J(double frac) => 1 + frac * S();

            public double Range(double a, double b) => a + (b - a) * U();
        }

        /// <summary>Фильтр с переменным состоянием (TPT, Задавалин): нижние, полоса, верхние; Q — добротность.</summary>
        private struct Svf
        {
            private double _g, _k, _ic1, _ic2;
            public double Q;

            public Svf(double hz, double q)
            {
                _g = 0; _k = 0; _ic1 = 0; _ic2 = 0; Q = q;
                Set(hz, q);
            }

            public void Set(double hz, double q)
            {
                Q = q;
                _g = Math.Tan(Math.PI * Math.Min(hz, 0.45 * SampleRate) / SampleRate);
                _k = 1.0 / q;
            }

            private void Tick(double x, out double lp, out double bp, out double hp)
            {
                double a1 = 1.0 / (1 + _g * (_g + _k));
                double v3 = x - _ic2;
                double v1 = a1 * _ic1 + _g * a1 * v3;
                double v2 = _ic2 + _g * v1;
                _ic1 = 2 * v1 - _ic1;
                _ic2 = 2 * v2 - _ic2;
                lp = v2;
                bp = v1;
                hp = x - _k * v1 - v2;
            }

            public double Low(double x) { Tick(x, out var lp, out _, out _); return lp; }
            public double Band(double x) { Tick(x, out _, out var bp, out _); return bp; }
            public double High(double x) { Tick(x, out _, out _, out var hp); return hp; }
        }
    }
}
