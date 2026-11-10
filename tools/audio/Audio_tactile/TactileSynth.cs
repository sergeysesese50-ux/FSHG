using System;
using static Fishing.Unity.TDsp;

namespace Fishing.Unity
{
    /// <summary>
    /// «Тактильная» палитра — рецепты. Моно, детерминированно (свой генератор, постоянные зёрна), без ассетов.
    /// Петли и разовые звуки — 22050 Гц (AudioClip.Create). Зёрна (щелчки, тики, скрип) — в частоте вывода
    /// (AudioSettings.outputSampleRate), их проигрывает TactileGrains на аудиопотоке.
    ///
    /// Правила палитры:
    /// - каждый звук — затухающие моды с плавной (raised-cosine) атакой; ни одного скачка в один сэмпл;
    /// - затухание tanφ = 1/(π·f·τ) ≥ 0,03 у всего повторяющегося (дерево/пластик, не металл);
    /// - повторяющееся: центр тяжести 500–1400 Гц, моды выше 2 кГц живут 0,5–2 мс (хруст атаки, не звон);
    /// - петли только для шумовых подложек, ≥ 3 с, фильтры по кругу, медленные изменения — целое число периодов.
    /// </summary>
    internal static class TactileSynth
    {
        public const int SampleRate = 22050;

        public const int DetentVariants = 16;
        public const int DragVariants = 16;
        public const int StrainVariants = 12;
        public const int CreakVariants = 4;
        public const int RotorVariants = 12;

        // =====================================================================================
        // Зёрна (ручка, фрикцион, натуга, скрип удилища)
        // =====================================================================================

        /// <summary>
        /// Щелчок ручки катушки («thock» механической клавиатуры + щелчок заводной головки часов), 75 мс:
        /// корпус 300/620/1040 Гц (τ 8/13/8 мс), щелчок 1560/2450/3400 Гц (τ 3,4/1,6/0,8 мс, атака 0,25 мс),
        /// через 2,2–3,8 мс — «досадка» собачки на −12 дБ; шумовой толчок 0,5 мс. ВЧ 110 Гц, НЧ 3,8 кГц.
        /// Разброс вариантов: частоты ±3%, τ ±12%, амплитуды ±10%.
        /// </summary>
        public static float[] Detent(int variant, int sr)
        {
            var r = new TRng(0xDE7E0000u + (uint)variant * 7919u);
            var s = Buf(0.09, sr);
            Mode(s, sr, 0, 300 * r.J(0.03), 0.008 * r.J(0.12), 0.20 * r.J(0.1), 0.0008);
            Mode(s, sr, 0, 620 * r.J(0.03), 0.013 * r.J(0.12), 1.00 * r.J(0.1), 0.0006);
            Mode(s, sr, 0, 1040 * r.J(0.03), 0.008 * r.J(0.12), 0.60 * r.J(0.1), 0.0005);
            double fc = 1560 * r.J(0.03), fd = 2450 * r.J(0.03);
            Mode(s, sr, 0, fc, 0.0034 * r.J(0.12), 0.70 * r.J(0.1), 0.00025);
            Mode(s, sr, 0, fd, 0.0016 * r.J(0.12), 0.42 * r.J(0.1), 0.00025);
            Mode(s, sr, 0, 3400 * r.J(0.03), 0.0008 * r.J(0.12), 0.18 * r.J(0.1), 0.00025);
            double settle = r.R(0.0022, 0.0038);
            Mode(s, sr, settle, fc * 1.015, 0.0024, 0.15 * r.J(0.2), 0.0003);
            Mode(s, sr, settle, fd * 1.01, 0.0011, 0.07 * r.J(0.2), 0.0003);
            Burst(s, sr, 0, 0.0005, 0.10, ref r);
            Filter(s, sr, FType.Hp, 110);
            Filter(s, sr, FType.Lp, 3800);
            return Finish(s, sr, 0.9, 0, 0.004);
        }

        /// <summary>
        /// Проход ротора (леска ложится на шпулю под натяжением): 75 мс белого шума через полосу 620 Гц (Q 1,1) и
        /// 1150 Гц (Q 2, −10 дБ), атака 6 мс (raised cosine), спад τ 14 мс ±15%, НЧ 2,2 кГц. Мягкое «ффф» между щелчками.
        /// </summary>
        public static float[] RotorSwish(int variant, int sr)
        {
            var r = new TRng(0x5011u + (uint)variant * 40503u);
            var s = Buf(0.075, sr);
            double att = 0.006, tau = 0.014 * r.J(0.15);
            var a = new Svf(sr, 620 * r.J(0.1), 1.1);
            var b = new Svf(sr, 1150 * r.J(0.08), 2.0);
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / sr;
                double e = t < att ? 0.5 - 0.5 * Math.Cos(Math.PI * t / att) : Math.Exp(-(t - att) / tau);
                double x = r.S();
                s[i] = (float)(e * (a.Bp(x) + 0.32 * b.Bp(x)));
            }
            Filter(s, sr, FType.Lp, 2200);
            Filter(s, sr, FType.Lp, 2200);
            return Finish(s, sr, 0.9, 120, 0.01);
        }

        /// <summary>
        /// Тик фрикциона (собачка трещотки по зубчатому диску): тело 560 Гц (τ 5 мс), основной 1250 Гц (τ 3,8 мс),
        /// 1870/2700/3600 Гц (τ 2,3/1,1/0,5 мс). Яркий — НЧ 4,2 кГц; тёмный (для быстрой пробуксовки) —
        /// частоты ×0,92, верх ×0,5, атака мягче, НЧ 2,2 кГц. 45 мс.
        /// </summary>
        public static float[] DragTick(int variant, bool dark, int sr)
        {
            var r = new TRng((dark ? 0xDA2C0000u : 0xD2A60000u) + (uint)variant * 104729u);
            var s = Buf(0.045, sr);
            double fs = dark ? 0.92 : 1.0, top = dark ? 0.5 : 1.0, att = dark ? 0.0005 : 0.00025;
            Mode(s, sr, 0, 360 * fs * r.J(0.04), 0.007 * r.J(0.15), 0.15 * r.J(0.12), 0.0007);
            Mode(s, sr, 0, 560 * fs * r.J(0.04), 0.005 * r.J(0.15), 0.40 * r.J(0.12), 0.0005);
            Mode(s, sr, 0, 1250 * fs * r.J(0.04), 0.0038 * r.J(0.15), 1.00 * r.J(0.12), att);
            Mode(s, sr, 0, 1870 * fs * r.J(0.04), 0.0023 * r.J(0.15), 0.55 * top * r.J(0.12), att);
            Mode(s, sr, 0, 2700 * fs * r.J(0.04), 0.0011 * r.J(0.15), 0.30 * top * r.J(0.12), att);
            Mode(s, sr, 0, 3600 * fs * r.J(0.04), 0.0005 * r.J(0.15), 0.12 * top * r.J(0.12), att);
            Burst(s, sr, 0, 0.00035, 0.10 * top, ref r);
            Filter(s, sr, FType.Hp, 140);
            Filter(s, sr, FType.Lp, dark ? 2200 : 4200);
            return Finish(s, sr, 0.9, 0, 0.004);
        }

        /// <summary>
        /// Натуга механизма (stick-slip): 4–10 толчков с неровным шагом 6,5–13 мс (сила толчка = √накопленного),
        /// через резонаторы 300/470/690/980/1350 Гц (Q 3), огибающая-«вздох». 60–120 мс.
        /// </summary>
        public static float[] Strain(int variant, int sr)
        {
            var r = new TRng(0x57A10000u + (uint)variant * 15485863u);
            double len = r.R(0.06, 0.12);
            var ex = Buf(len + 0.03, sr);
            double t = 0.002;
            while (t < len)
            {
                double gap = r.R(0.0065, 0.013);
                double x = t / len;
                double env = Math.Pow(Math.Sin(Math.PI * Math.Min(1.0, x)), 0.7);
                Bump(ex, sr, t, 0.00045, Math.Sqrt(gap / 0.013) * env * r.J(0.3));
                t += gap;
            }
            var s = new float[ex.Length];
            double[] fq = { 300, 470, 690, 980, 1350 };
            double[] g = { 1.0, 0.85, 0.6, 0.38, 0.2 };
            for (int k = 0; k < fq.Length; k++)
            {
                var f = new Svf(sr, fq[k] * r.J(0.05), 3.0);
                for (int i = 0; i < ex.Length; i++) s[i] += (float)(g[k] * f.Bp(ex[i]));
            }
            Filter(s, sr, FType.Hp, 150);
            Filter(s, sr, FType.Lp, 2400);
            return Finish(s, sr, 0.9, 0, 0.012);
        }

        /// <summary>
        /// Скрип удилища (бланк под нагрузкой): 0,25–0,5 с толчков stick-slip, частота толчков скользит 55–75 → 90–130 Гц
        /// (±12% шаг, ±35% сила), резонаторы 180/320/520/760/1100 Гц (Q 2–4). Мягкий — НЧ 2,2 кГц; сильный — 2,8 кГц, толчки острее.
        /// </summary>
        public static float[] RodCreak(int variant, bool hard, int sr)
        {
            var r = new TRng((hard ? 0xC4EA0000u : 0xC4EB0000u) + (uint)variant * 2654435761u);
            double len = r.R(0.25, 0.5);
            var ex = Buf(len + 0.06, sr);
            double r0 = r.R(55, 75), r1 = r.R(90, 130);
            bool fall = (variant & 1) == 1;
            double t = 0.004;
            while (t < len)
            {
                double x = t / len;
                double rate = fall ? r1 + (r0 - r1) * x : r0 + (r1 - r0) * x;
                double env = Math.Min(1.0, t / 0.04) * Math.Min(1.0, (len - t) / 0.09) * (0.85 + 0.15 * Math.Sin(Math.PI * x));
                Bump(ex, sr, t, hard ? 0.0003 : 0.0005, env * r.J(0.35));
                t += r.J(0.12) / rate;
            }
            var s = new float[ex.Length];
            double[] fq = { 180, 320, 520, 760, 1100 };
            double[] q = { 2, 2.5, 3, 3.5, 4 };
            double[] g = hard ? new[] { 0.7, 1.0, 0.9, 0.6, 0.4 } : new[] { 0.8, 1.0, 0.8, 0.45, 0.25 };
            for (int k = 0; k < fq.Length; k++)
            {
                var f = new Svf(sr, fq[k] * r.J(0.05), q[k]);
                for (int i = 0; i < ex.Length; i++) s[i] += (float)(g[k] * f.Bp(ex[i]));
            }
            Filter(s, sr, FType.Hp, 125);
            Filter(s, sr, FType.Lp, hard ? 2800 : 2200);
            return Finish(s, sr, 0.9, 0, 0.02);
        }

        // =====================================================================================
        // Петли (22050 Гц)
        // =====================================================================================

        /// <summary>
        /// Шорох шпули (подшипник и леска на шпуле), петля 3 с: шум через полосу 620 Гц (Q 0,9) + 1200 Гц (Q 1,4, ×0,3),
        /// ВЧ 160 Гц, НЧ 1,7 кГц 4-го порядка — всё по кругу; медленный дрейф ±5% один раз за петлю (0,33 Гц). Без тремоло.
        /// </summary>
        public static float[] SpoolPurr()
        {
            int sr = SampleRate, n = N(3.0, sr);
            var r = new TRng(0x5B00u);
            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = r.N();
            var a = (float[])w.Clone();
            var b = (float[])w.Clone();
            Filter(a, sr, FType.Bp, 620, 0.9, true);
            Filter(b, sr, FType.Bp, 1200, 1.4, true);
            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (float)((a[i] + 0.3 * b[i]) * (1 + 0.05 * Math.Sin(TwoPi * i / n)));
            Filter(s, sr, FType.Hp, 160, 0.7071, true);
            Filter(s, sr, FType.Lp, 1700, 0.7071, true);
            Filter(s, sr, FType.Lp, 1700, 0.7071, true);
            RemoveDc(s);
            Normalize(s, 0.7);
            return s;
        }

        /// <summary>
        /// Гул натянутой лески, петля 4 с: узкие полосы шума 440 Гц (Q 6), 880 Гц (Q 5, −7 дБ), 1320 Гц (Q 4, −18 дБ),
        /// каждая — два каскада (крутые склоны), по кругу; НЧ 1,5 кГц; «дыхание» ±12% один раз за петлю.
        /// Высоту поднимает натяжение (до ×1,36).
        /// </summary>
        public static float[] LineHum()
        {
            int sr = SampleRate, n = N(4.0, sr);
            var r = new TRng(0x11AEu);
            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = r.N();
            double[] fq = { 440, 880, 1320 }, q = { 6, 5, 4 }, g = { 1.0, 0.45, 0.125 };
            var s = new float[n];
            for (int k = 0; k < 3; k++)
            {
                var c = (float[])w.Clone();
                Filter(c, sr, FType.Bp, fq[k], q[k], true);
                Filter(c, sr, FType.Bp, fq[k], q[k], true);
                Normalize(c, 1.0);
                for (int i = 0; i < n; i++) s[i] += (float)(g[k] * c[i]);
            }
            Filter(s, sr, FType.Lp, 1500, 0.7071, true);
            for (int i = 0; i < n; i++) s[i] *= (float)(1 + 0.12 * Math.Sin(TwoPi * i / n + 1.0));
            RemoveDc(s);
            Normalize(s, 0.7);
            return s;
        }

        /// <summary>
        /// Утро на озере, бесшовная петля (по умолчанию 64 с):
        /// - плеск у берега: шум НЧ 600 Гц / ВЧ 150 Гц, медленные волны 11/17/26 циклов на петлю (0,17–0,41 Гц), глубина 40%;
        /// - накаты: ~0,6/с, полоса 300–750 Гц (Q 1,2), подъём 60–150 мс, спад 150–400 мс, в конце 1–3 пузырька 2,5–6 мм;
        /// - ветер в камыше: шум полосой 1100 Гц (Q 0,5) + НЧ 2,2 кГц, порывы 3/5/8 циклов на петлю; в порывах — шелест
        ///   (зёрна 40–90 мс, 1,3–2,4 кГц, Q 2,5) до 4 в секунду;
        /// - далёкие птицы: 6 фраз зяблика (трели 3,4→2,6 кГц) и кукушка (700→590 Гц, три «ку-ку»), −30 дБ, с ревербом.
        /// Всё, что пересекает конец, заворачивается в начало; реверб прогоняется по кругу.
        /// </summary>
        public static float[] Ambience(double seconds = 64.0)
        {
            int sr = SampleRate, n = N(seconds, sr);
            var r = new TRng(0xA3B1u);

            // Плеск — подложка.
            var lap = new float[n];
            for (int i = 0; i < n; i++) lap[i] = r.N();
            Filter(lap, sr, FType.Lp, 600, 0.7071, true);
            Filter(lap, sr, FType.Hp, 150, 0.7071, true);
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double sw = 0.5 * Math.Sin(TwoPi * 11 * x) + 0.3 * Math.Sin(TwoPi * 17 * x + 1.3) + 0.2 * Math.Sin(TwoPi * 26 * x + 2.1);
                lap[i] *= (float)(1 + 0.4 * sw);
            }
            Normalize(lap, 1.0);

            // Накаты у берега — события (по кругу).
            var laps = new float[n];
            double t = 0.3;
            while (t < seconds)
            {
                double dur = r.R(0.2, 0.55), att = r.R(0.06, 0.15), amp = r.R(0.35, 1.0);
                double fc = r.R(300, 750);
                int i0 = N(t, sr), m = N(att + dur, sr);
                var f = new Svf(sr, fc, 1.2);
                for (int k = 0; k < m; k++)
                {
                    double tt = (double)k / sr;
                    double e = tt < att ? Math.Sin(0.5 * Math.PI * tt / att) : Math.Exp(-(tt - att) / (dur * 0.45));
                    laps[(i0 + k) % n] += (float)(amp * e * f.Bp(r.S()));
                }
                int nb = 1 + (int)(r.U() * 3);
                for (int b = 0; b < nb; b++)
                    Bubble(laps, sr, t + att + r.R(0.02, dur), r.R(2.5, 6.0), amp * r.R(0.08, 0.2), 0.1, 1.0, true);
                t += r.R(0.6, 2.6);
            }
            Normalize(laps, 1.0);

            // Ветер в камыше.
            var wind = new float[n];
            for (int i = 0; i < n; i++) wind[i] = r.N();
            Filter(wind, sr, FType.Bp, 1100, 0.5, true);
            Filter(wind, sr, FType.Lp, 2200, 0.7071, true);
            var gust = new double[n];
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double v = 0.5 + 0.5 * (0.55 * Math.Sin(TwoPi * 3 * x + 0.4) + 0.3 * Math.Sin(TwoPi * 5 * x + 2.0) + 0.15 * Math.Sin(TwoPi * 8 * x + 4.1));
                gust[i] = 0.2 + 0.8 * Math.Pow(Math.Max(0, Math.Min(1, v)), 2);
                wind[i] *= (float)gust[i];
            }
            Normalize(wind, 1.0);
            var rustle = new float[n];
            t = 0.1;
            while (t < seconds)
            {
                int i0 = N(t, sr) % n;
                double gv = gust[i0];
                if (r.U() < gv)
                {
                    double dur = r.R(0.04, 0.09);
                    int m = N(dur, sr);
                    var f = new Svf(sr, r.R(1300, 2400), 2.5);
                    double amp = gv * r.R(0.3, 1.0);
                    for (int k = 0; k < m; k++)
                    {
                        double w = Math.Sin(Math.PI * k / m);
                        rustle[(i0 + k) % n] += (float)(amp * w * w * f.Bp(r.S()));
                    }
                }
                t += r.R(0.08, 0.5);
            }
            Normalize(rustle, 1.0);

            // Птицы вдали.
            var birds = new float[n];
            for (int p = 0; p < 6; p++)
            {
                double at = (p + 0.15 + 0.6 * r.U()) * seconds / 6.0;
                int notes = 6 + (int)(r.U() * 6);
                double tt = at, gap = r.R(0.07, 0.09), fTop = r.R(3200, 3600), fEnd = r.R(2500, 2800);
                double amp = r.R(0.5, 1.0);
                for (int k = 0; k < notes; k++)
                {
                    double x = (double)k / notes;
                    double f0 = fTop + (fEnd - fTop) * x;
                    Chirp(birds, sr, tt, 0.035, f0 * 1.06, f0 * 0.94, amp * (0.7 + 0.3 * Math.Sin(Math.PI * x)));
                    tt += gap * (1 - 0.35 * x);
                }
                Chirp(birds, sr, tt + 0.03, 0.12, r.R(2900, 3100), r.R(2100, 2300), amp * 0.9);
            }
            double cuckoo = seconds * 0.58;
            for (int k = 0; k < 3; k++)
            {
                double c0 = cuckoo + k * 0.85;
                Hoot(birds, sr, c0, 0.17, 700, 680, 0.55);
                Hoot(birds, sr, c0 + 0.24, 0.22, 590, 570, 0.5);
            }
            Filter(birds, sr, FType.Lp, 4200, 0.7071, true);
            var birdsRev = Reverb(birds, sr, -4, 0, 0.86, 0.35, true);
            Normalize(birdsRev, 1.0);

            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (float)(0.40 * lap[i] + 0.50 * laps[i] + 0.16 * wind[i] + 0.10 * rustle[i] + 0.07 * birdsRev[i]);
            Filter(s, sr, FType.Hp, 70, 0.7071, true);
            RemoveDc(s);
            Normalize(s, 0.5);
            return s;
        }

        private static void Chirp(float[] s, int sr, double t0, double dur, double f0, double f1, double amp)
        {
            int i0 = N(t0, sr), m = N(dur, sr);
            double ph = 0;
            for (int k = 0; k < m; k++)
            {
                double x = (double)k / m;
                double f = f0 * Math.Pow(f1 / f0, x);
                ph += TwoPi * f / sr;
                double e = Math.Pow(Math.Sin(Math.PI * x), 2);
                s[(i0 + k) % s.Length] += (float)(amp * e * Math.Sin(ph));
            }
        }

        private static void Hoot(float[] s, int sr, double t0, double dur, double f0, double f1, double amp)
        {
            int i0 = N(t0, sr), m = N(dur, sr);
            double ph = 0;
            for (int k = 0; k < m; k++)
            {
                double x = (double)k / m;
                double f = f0 + (f1 - f0) * x;
                ph += TwoPi * f / sr;
                double e = Math.Min(1.0, x / 0.18) * Math.Min(1.0, (1 - x) / 0.35);
                e = e * e * (3 - 2 * e);
                s[(i0 + k) % s.Length] += (float)(amp * e * (Math.Sin(ph) + 0.15 * Math.Sin(2 * ph)));
            }
        }

        // =====================================================================================
        // Разовые (22050 Гц)
        // =====================================================================================

        /// <summary>Подсечка: свист удилища 500→1600→700 Гц (0–110 мс, НЧ 2 кГц 4-го порядка), затем леска встаёт в струну — «тунг» 165 Гц
        /// (8 гармоник, τ 140 мс) и глухой удар 110/220/330 Гц (τ 35 мс); лёгкая комната −20 дБ. 0,5 с.</summary>
        public static float[] Hookset()
        {
            int sr = SampleRate;
            var r = new TRng(0x400Cu);
            var s = Buf(0.5, sr);
            var swish = Buf(0.12, sr);
            SweptNoise(swish, sr, 0, 0.11, x => x < 0.5 ? 500 * Math.Pow(3.2, x / 0.5) : 1600 * Math.Pow(700.0 / 1600, (x - 0.5) / 0.5), 1.2,
                x => Math.Pow(Math.Sin(Math.PI * x), 1.5), 0.45, ref r);
            Filter(swish, sr, FType.Lp, 2000);
            Filter(swish, sr, FType.Lp, 2000);
            for (int i = 0; i < swish.Length; i++) s[i] += swish[i];
            Pluck(s, sr, 0.045, 165, 8, 0.14, 0.75, 1.0, 0.2, 0.985, 0.08);
            Mode(s, sr, 0.045, 110, 0.035, 0.55, 0.002);
            Mode(s, sr, 0.045, 220, 0.03, 0.35, 0.0015);
            Mode(s, sr, 0.045, 330, 0.02, 0.2, 0.0012);
            Burst(s, sr, 0.045, 0.0008, 0.12, ref r);
            var o = Reverb(s, sr, -20, 0.3);
            return Finish(o, sr, 0.9, 70, 0.05);
        }

        /// <summary>Телеграф ухода: «закипание» — 22 пузырька 2–6 мм всё гуще к концу и полоса шума 320→820→520 Гц, 0,55 с.</summary>
        public static float[] TeleRun()
        {
            int sr = SampleRate;
            var r = new TRng(0x7E1Au);
            var s = Buf(0.6, sr);
            SweptNoise(s, sr, 0, 0.55, x => x < 0.6 ? 320 + 500 * x / 0.6 : 820 - 300 * (x - 0.6) / 0.4, 1.1,
                x => Math.Pow(Math.Sin(Math.PI * x), 1.5), 0.5, ref r);
            for (int k = 0; k < 22; k++)
            {
                double at = 0.48 * Math.Sqrt(r.U());
                Bubble(s, sr, 0.02 + at, r.R(2.0, 6.0), r.R(0.15, 0.35) * (0.4 + at / 0.48));
            }
            var o = Reverb(s, sr, -20, 0.3);
            return Finish(o, sr, 0.9, 120, 0.04);
        }

        /// <summary>Телеграф ухода вглубь: «глоток» — два больших пузыря с падающей высотой (430→ и 333→), полоса шума 900→280 Гц,
        /// мягкий низкий «вумп» 120/240/360 Гц. 0,6 с.</summary>
        public static float[] TeleDive()
        {
            int sr = SampleRate;
            var r = new TRng(0x7E1Bu);
            var s = Buf(0.65, sr);
            SweptNoise(s, sr, 0, 0.55, x => 900 * Math.Pow(280.0 / 900, x), 1.3, x => Math.Pow(Math.Sin(Math.PI * x), 1.2), 0.45, ref r);
            Bubble(s, sr, 0.05, 7.0, 0.9, -0.12, 1.6);
            Bubble(s, sr, 0.2, 9.0, 0.7, -0.10, 1.6);
            Mode(s, sr, 0.15, 120, 0.09, 0.35, 0.01);
            Mode(s, sr, 0.15, 240, 0.07, 0.25, 0.01);
            Mode(s, sr, 0.15, 360, 0.05, 0.12, 0.008);
            var o = Reverb(s, sr, -20, 0.3);
            return Finish(o, sr, 0.9, 80, 0.04);
        }

        /// <summary>Телеграф броска: нарастающий «напор» — полоса 380→1100 Гц (Q 0,8), 30 пузырьков с растущей высотой,
        /// в конце толчок 95/190/285 Гц. 0,65 с.</summary>
        public static float[] TeleSurge()
        {
            int sr = SampleRate;
            var r = new TRng(0x7E1Cu);
            var s = Buf(0.7, sr);
            SweptNoise(s, sr, 0, 0.56, x => 380 * Math.Pow(1100.0 / 380, x), 0.8, x => Math.Pow(x, 1.5) * Math.Min(1.0, (1 - x) / 0.1), 0.6, ref r);
            for (int k = 0; k < 30; k++)
            {
                double x = r.U();
                Bubble(s, sr, 0.03 + 0.5 * x, 6.0 - 3.5 * x * r.J(0.2), r.R(0.1, 0.25) * (0.3 + x), 0.15);
            }
            Mode(s, sr, 0.5, 95, 0.05, 0.6, 0.004);
            Mode(s, sr, 0.5, 190, 0.04, 0.4, 0.003);
            Mode(s, sr, 0.5, 285, 0.03, 0.2, 0.003);
            var o = Reverb(s, sr, -20, 0.3);
            return Finish(o, sr, 0.9, 70, 0.04);
        }

        /// <summary>Телеграф рывка: твёрдый «тук» костяшкой по дереву — 190/380/570 Гц (τ 22/14/9 мс), щелчок 1150/2100 Гц
        /// (τ 3/1,2 мс), толчок 0,8 мс. 0,14 с.</summary>
        public static float[] TeleJerk()
        {
            int sr = SampleRate;
            var r = new TRng(0x7E1Du);
            var s = Buf(0.14, sr);
            Mode(s, sr, 0, 190, 0.022, 1.0, 0.0009);
            Mode(s, sr, 0, 380, 0.014, 0.7, 0.0007);
            Mode(s, sr, 0, 570, 0.009, 0.45, 0.0006);
            Mode(s, sr, 0, 1150, 0.003, 0.35, 0.0003);
            Mode(s, sr, 0, 2100, 0.0012, 0.12, 0.0003);
            Burst(s, sr, 0, 0.0008, 0.25, ref r);
            Filter(s, sr, FType.Lp, 3000);
            return Finish(s, sr, 0.9, 70, 0.02);
        }

        /// <summary>Телеграф свечки: восходящий «вжу-уп» — полоса 280→1700 Гц (Q 1,6), огибающая x², цепочка из 8 пузырьков
        /// всё выше (6→2,5 мм). 0,6 с.</summary>
        public static float[] TeleJump()
        {
            int sr = SampleRate;
            var r = new TRng(0x7E1Eu);
            var s = Buf(0.62, sr);
            SweptNoise(s, sr, 0, 0.55, x => 280 * Math.Pow(1700.0 / 280, x), 1.6, x => x * x * Math.Min(1.0, (1 - x) / 0.1), 0.6, ref r);
            for (int k = 0; k < 8; k++)
            {
                double x = k / 7.0;
                Bubble(s, sr, 0.18 + 0.33 * x, 6.0 - 3.5 * x, 0.12 + 0.2 * x, 0.2);
            }
            var o = Reverb(s, sr, -20, 0.3);
            return Finish(o, sr, 0.9, 120, 0.04);
        }

        /// <summary>
        /// Всплеск: удар — шум 2 мс атака, τ 35 мс (большой: 60 мс), полоса 300–2200 Гц (большой: НЧ 1600); облако пузырьков —
        /// 11 (18) штук, начало по экспоненте τ 120 мс, радиусы p(r) ∝ r^−2,5 в 1,6–7 мм (3–12 мм). Большой — удар 90/180/270 Гц.
        /// Свечка (jump) — большой + шлепок корпусом 8 мс и вторая волна пузырьков. Комната −18 дБ.
        /// </summary>
        public static float[] Splash(int kind)
        {
            int sr = SampleRate;
            bool big = kind >= 1, jump = kind == 2;
            var r = new TRng(0x5A50u + (uint)kind);
            var s = Buf(jump ? 1.0 : big ? 0.85 : 0.6, sr);
            double tau = big ? 0.06 : 0.035;
            var burst = new float[s.Length];
            for (int i = 0; i < N(tau * 6, sr) && i < burst.Length; i++)
            {
                double t = (double)i / sr;
                burst[i] = (float)(r.S() * Math.Min(1.0, t / 0.002) * Math.Exp(-t / tau));
            }
            Filter(burst, sr, FType.Hp, 300);
            Filter(burst, sr, FType.Lp, big ? 1600 : 2200);
            Normalize(burst, 1.0);
            for (int i = 0; i < s.Length; i++) s[i] += 0.55f * burst[i];

            int count = big ? 18 : 11;
            double rMin = big ? 3 : 1.6, rMax = big ? 12 : 7;
            for (int k = 0; k < count; k++)
            {
                double at = -0.12 * Math.Log(1 - 0.95 * r.U());
                double u = r.U();
                double rr = Math.Pow(Math.Pow(rMin, -1.5) - u * (Math.Pow(rMin, -1.5) - Math.Pow(rMax, -1.5)), -1 / 1.5);
                Bubble(s, sr, 0.005 + at, rr, 0.35 * r.J(0.3) * Math.Sqrt(rr / rMax));
            }
            if (big)
            {
                Mode(s, sr, 0.002, 90, 0.06, 0.5, 0.003);
                Mode(s, sr, 0.002, 180, 0.045, 0.3, 0.002);
                Mode(s, sr, 0.002, 270, 0.03, 0.15, 0.002);
            }
            if (jump)
            {
                var slap = Buf(0.03, sr);
                Burst(slap, sr, 0, 0.0015, 1.0, ref r);
                for (int i = 0; i < slap.Length; i++) slap[i] += (float)(r.S() * Math.Exp(-(double)i / sr / 0.008));
                Filter(slap, sr, FType.Lp, 2500);
                for (int i = 0; i < slap.Length; i++) s[i] += 0.4f * slap[i];
                for (int k = 0; k < 10; k++)
                    Bubble(s, sr, 0.25 + 0.3 * r.U(), r.R(3, 9), 0.2 * r.J(0.3));
            }
            var o = Reverb(s, sr, -18, 0.5);
            return Finish(o, sr, 0.9, 80, 0.06);
        }

        /// <summary>Леска «тунг» при ударе рыбы: струна 147 Гц, 8 гармоник, τ 110 мс, высота −1,5% за 60 мс; щелчок о кольцо. 0,6 с.</summary>
        public static float[] LineThrum()
        {
            int sr = SampleRate;
            var r = new TRng(0x7A2Bu);
            var s = Buf(0.6, sr);
            Pluck(s, sr, 0, 147, 8, 0.11, 0.8, 1.0, 0.22, 0.985, 0.06);
            Mode(s, sr, 0, 1600, 0.0015, 0.12, 0.0003);
            Mode(s, sr, 0, 2500, 0.0008, 0.05, 0.0003);
            Burst(s, sr, 0, 0.0005, 0.05, ref r);
            return Finish(s, sr, 0.9, 100, 0.05);
        }

        /// <summary>Рыба на берегу: три мокрых шлепка (0 / 0,14 / 0,27 с; 1 / 0,7 / 0,45) — толчок 1,5 мс + шум τ 14 мс
        /// полосой 700 Гц + пузырь 5–7 мм. 0,55 с.</summary>
        public static float[] FishFlop()
        {
            int sr = SampleRate;
            var r = new TRng(0xF10Fu);
            var s = Buf(0.6, sr);
            double[] at = { 0, 0.14, 0.27 }, amp = { 1, 0.7, 0.45 };
            for (int k = 0; k < 3; k++)
            {
                var b = Buf(0.08, sr);
                Burst(b, sr, 0, 0.0015, 0.8, ref r);
                for (int i = 0; i < b.Length; i++) b[i] += (float)(r.S() * Math.Exp(-(double)i / sr / 0.014));
                Filter(b, sr, FType.Bp, 700 * r.J(0.1), 0.7);
                Filter(b, sr, FType.Lp, 2200);
                Bubble(b, sr, 0.004, r.R(5, 7), 0.3);
                int i0 = N(at[k] * r.J(0.1), sr);
                for (int i = 0; i < b.Length && i0 + i < s.Length; i++) s[i0 + i] += (float)(amp[k] * b[i]);
            }
            var o = Reverb(s, sr, -20, 0.3);
            return Finish(o, sr, 0.9, 90, 0.04);
        }

        /// <summary>
        /// Награда за поимку. Сначала тактильный «щелчок замка» (420/760/1350/2200 Гц), через 60 мс — мягкая маримба
        /// (парциалы 1 / 2 / 3,93, молоточек 1,2 мс) по нотам соль-мажорной пентатоники с шагом 75 мс:
        /// tier 0 — G4 D5; tier 1 — G4 D5 G5; tier 2 — G4 B4 D5 G5 B5 и тихое «сияние» G6. Комната −16 дБ.
        /// </summary>
        public static float[] Chime(int tier)
        {
            int sr = SampleRate;
            var r = new TRng(0xC4130u + (uint)tier);
            double[] notes = tier == 0 ? new[] { 392.0, 587.33 } : tier == 1 ? new[] { 392.0, 587.33, 783.99 }
                : new[] { 392.0, 493.88, 587.33, 783.99, 987.77 };
            double len = tier == 2 ? 2.4 : tier == 1 ? 1.8 : 1.4;
            var s = Buf(len, sr);
            Mode(s, sr, 0, 420, 0.014, 0.35, 0.0006);
            Mode(s, sr, 0, 760, 0.009, 0.3, 0.0005);
            Mode(s, sr, 0, 1350, 0.004, 0.22, 0.0003);
            Mode(s, sr, 0, 2200, 0.0015, 0.08, 0.0003);
            for (int k = 0; k < notes.Length; k++)
            {
                double t0 = 0.06 + 0.075 * k, f = notes[k];
                double tau = 0.55 * Math.Sqrt(392.0 / f) * (tier == 2 ? 1.25 : 1.0);
                double a = 0.55 * r.J(0.05);
                Mode(s, sr, t0, f, tau, a, 0.0012);
                Mode(s, sr, t0, 2 * f, tau * 0.35, a * 0.06, 0.0012);
                Mode(s, sr, t0, 3.93 * f, 0.06, a * 0.1, 0.0008);
            }
            if (tier == 2)
                Mode(s, sr, 0.36, 1567.98, 0.7, 0.05, 0.03);
            var o = Reverb(s, sr, -16, 0.6);
            return Finish(o, sr, 0.85, 90, 0.15);
        }

        /// <summary>Обрыв (редкий — можно ярче): треск 0,7 мс, «дзынь» 620→560 Гц (6 гармоник, τ 90 мс),
        /// хлыст ослабшей лески 1500→400 Гц (τ 120 мс). Комната −16 дБ. 0,6 с.</summary>
        public static float[] LineSnap()
        {
            int sr = SampleRate;
            var r = new TRng(0xB00Bu);
            var s = Buf(0.6, sr);
            var crack = Buf(0.01, sr);
            Burst(crack, sr, 0, 0.0007, 1.0, ref r);
            Filter(crack, sr, FType.Hp, 500);
            Filter(crack, sr, FType.Lp, 6000);
            for (int i = 0; i < crack.Length; i++) s[i] += crack[i];
            Pluck(s, sr, 0.0005, 620, 6, 0.09, 0.6, 1.2, 0.3, 0.9, 0.15);
            SweptNoise(s, sr, 0.02, 0.4, x => 1500 * Math.Pow(400.0 / 1500, x), 1.0, x => Math.Exp(-x * 0.4 / 0.12) * Math.Min(1, x / 0.05), 0.35, ref r);
            var o = Reverb(s, sr, -16, 0.4);
            return Finish(o, sr, 0.9, 100, 0.05);
        }

        /// <summary>Поломка удилища: треск, редеющие щепки (14 щелчков, НЧ 3 кГц), глухой удар 110/220/330 Гц. 0,6 с.</summary>
        public static float[] RodCrack()
        {
            int sr = SampleRate;
            var r = new TRng(0xC00Cu);
            var s = Buf(0.6, sr);
            Burst(s, sr, 0, 0.002, 1.0, ref r);
            for (int i = 0; i < N(0.03, sr); i++) s[i] += (float)(0.6 * r.S() * Math.Exp(-(double)i / sr / 0.004));
            for (int k = 0; k < 14; k++)
            {
                double x = k / 14.0;
                Burst(s, sr, 0.01 + 0.3 * Math.Pow(x, 1.6) + 0.006 * r.S(), 0.0015, 0.7 * (1 - x) + 0.1, ref r);
                Mode(s, sr, 0.01 + 0.3 * Math.Pow(x, 1.6), 900 * r.J(0.2), 0.004, 0.15 * (1 - x), 0.0003);
            }
            Filter(s, sr, FType.Lp, 3000);
            Mode(s, sr, 0, 110, 0.05, 0.6, 0.002);
            Mode(s, sr, 0, 220, 0.04, 0.35, 0.002);
            Mode(s, sr, 0, 330, 0.03, 0.15, 0.002);
            var o = Reverb(s, sr, -16, 0.4);
            return Finish(o, sr, 0.9, 80, 0.05);
        }

        /// <summary>Сход: ослабшая леска «фьють» 900→300 Гц, «плинь» пузыря 3,3 мм (900 Гц), два уходящих пузыря 4,5 и 5,5 мм
        /// с чуть падающей высотой. Без праздника. 0,75 с.</summary>
        public static float[] Escape()
        {
            int sr = SampleRate;
            var r = new TRng(0xE5CAu);
            var s = Buf(0.75, sr);
            SweptNoise(s, sr, 0, 0.14, x => 900 * Math.Pow(300.0 / 900, x), 1.0, x => Math.Pow(Math.Sin(Math.PI * x), 1.5), 0.25, ref r);
            Bubble(s, sr, 0.13, 3.3, 0.6, 0.1);
            Bubble(s, sr, 0.30, 4.5, 0.35, -0.05);
            Bubble(s, sr, 0.45, 5.5, 0.2, -0.05);
            var o = Reverb(s, sr, -16, 0.4);
            return Finish(o, sr, 0.85, 120, 0.05);
        }

        /// <summary>Заброс: щелчок дужки (900/1600/2400 Гц), свист удилища 350→1300→500 Гц (пик на 0,17 с, Q 1,3),
        /// затем шелест сходящей лески 1300→800 Гц (Q 1,8), гаснет к 0,95 с; НЧ 2,4 кГц 4-го порядка.</summary>
        public static float[] CastWhoosh()
        {
            int sr = SampleRate;
            var r = new TRng(0xCA57u);
            var s = Buf(1.0, sr);
            Mode(s, sr, 0, 900, 0.006, 0.35, 0.0004);
            Mode(s, sr, 0, 1600, 0.003, 0.25, 0.0003);
            Mode(s, sr, 0, 2400, 0.0012, 0.1, 0.0003);
            SweptNoise(s, sr, 0.04, 0.38, x => x < 0.35 ? 350 * Math.Pow(1300.0 / 350, x / 0.35) : 1300 * Math.Pow(500.0 / 1300, (x - 0.35) / 0.65), 1.3,
                x => Math.Pow(Math.Sin(Math.PI * Math.Pow(x, 0.8)), 1.6), 0.8, ref r);
            SweptNoise(s, sr, 0.15, 0.8, x => 1300 - 500 * x, 1.8, x => Math.Min(1, x / 0.1) * Math.Exp(-x * 2.5), 0.14, ref r);
            Filter(s, sr, FType.Lp, 2400);
            Filter(s, sr, FType.Lp, 2400);
            return Finish(s, sr, 0.9, 120, 0.08);
        }

        /// <summary>Поплавок на воду: «блоп» — пузырь 6,5 мм (460 Гц, высота вверх), шумовой удар 12 мс НЧ 1,4 кГц, 4 мелких
        /// пузырька. Комната −17 дБ. 0,7 с.</summary>
        public static float[] FloatPlop()
        {
            int sr = SampleRate;
            var r = new TRng(0xF1A7u);
            var s = Buf(0.6, sr);
            var b = Buf(0.03, sr);
            for (int i = 0; i < b.Length; i++)
                b[i] = (float)(r.S() * Math.Min(1.0, i / (0.001 * sr)) * Math.Exp(-(double)i / sr / 0.004));
            Filter(b, sr, FType.Lp, 1400);
            for (int i = 0; i < b.Length; i++) s[i] += 0.3f * b[i];
            Bubble(s, sr, 0.004, 6.5, 1.0, 0.1);
            for (int k = 0; k < 4; k++) Bubble(s, sr, r.R(0.04, 0.2), r.R(2.5, 4.0), r.R(0.15, 0.3));
            var o = Reverb(s, sr, -17, 0.4);
            return Finish(o, sr, 0.9, 120, 0.05);
        }

        /// <summary>Поклёвка: поплавок притоплен — «плип» 3,8 мм (790 Гц) и пузырёк 2,8 мм через 70 мс, рябь 600 Гц. 0,4 с.</summary>
        public static float[] BiteBob()
        {
            int sr = SampleRate;
            var r = new TRng(0xB17Eu);
            var s = Buf(0.4, sr);
            for (int i = 0; i < N(0.15, sr); i++) s[i] += (float)(0.2 * r.S() * Math.Min(1.0, i / (0.002 * sr)) * Math.Exp(-(double)i / sr / 0.03));
            Filter(s, sr, FType.Bp, 600, 1.0);
            Bubble(s, sr, 0.003, 3.8, 0.7, 0.1);
            Bubble(s, sr, 0.07, 2.8, 0.25, 0.1);
            var o = Reverb(s, sr, -18, 0.3);
            return Finish(o, sr, 0.85, 150, 0.04);
        }
    }
}
