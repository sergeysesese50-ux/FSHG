using System;

namespace Fishing.Unity
{
    /// <summary>
    /// «Уютная ASMR» — процедурная палитра рыбалки. Кандидат на замену FightSynth: моно, 22050 Гц, без ассетов и без движка.
    /// Чистые функции: один и тот же вызов даёт те же сэмплы (свой xorshift с постоянным зерном).
    ///
    /// Характер: тёплый, мягкий, «близкий» — округлые деревянно-пластиковые тики (мягкий молоточек 1,2–1,8 мс, демпфирование
    /// tanφ 0,03–0,07), шуршащая леска, мурчащий фрикцион, капля маленькой комнаты; всё чуть приглушено.
    /// Спектральный бюджет повторяющихся слоёв: центроид 400–1300 Гц, 2–5 кГц ≤ 10% энергии, выше 5 кГц — только первые
    /// миллисекунды атак; всё режется снизу на 90–150 Гц, «вес» — гармониками 180–600 Гц (телефон их играет).
    /// Никакой периодической амплитудной модуляции 2–8 Гц и 20–150 Гц на непрерывных слоях: только дрейф ниже 0,5 Гц
    /// и ритм из отдельных событий.
    ///
    /// Петли (кровать катушки, натуга, леска, озеро) замкнуты без шва: шум и фильтры прогоняются по кругу, хвосты событий
    /// заворачиваются в начало, медленные модуляции имеют целое число периодов на петлю.
    /// Разовые звуки начинаются с нуля (приподнятый косинус 0,3+ мс) и уходят в ноль (3–20 мс), пик 0,9.
    /// </summary>
    internal static class CozySynth
    {
        public const int SampleRate = 22050;
        private const double TwoPi = 2.0 * Math.PI;

        public const int GearVariants = 12;  // на каждую из 3 «тяжестей»
        public const int TockVariants = 6;
        public const int DragVariants = 16;  // светлые и тёмные — по 16
        public const int PurrVariants = 12;
        public const int CreakVariants = 8;

        /// <summary>Хвост комнаты у зёрен: 0,16 с (−50 дБ от пика) — чтобы микшеру зёрен хватало 24 голосов на 60 зёрнах в секунду.</summary>
        private const double GrainTail = 0.16;

        // =================================================================================================
        // Зёрна (играет микшер зёрен, по событию на каждые N см)
        // =================================================================================================

        /// <summary>
        /// Тик шестерни: 4 моды (тело 300 Гц, 690, 1240, 1980 Гц; τ 12/8,5/4/1,8 мс), мягкий молоточек 1,4 мс (приподнятый
        /// косинус — сам гасит всё выше ~1,4 кГц), НЧ 2,4 кГц, ВЧ 120 Гц, комната −17 дБ.
        /// tier: 0 — лёгкий (быстро выбирает: ×1,08 по частоте, τ ×0,8), 1 — обычный, 2 — тяжёлый (натуга: ×0,84, τ ×1,35,
        /// тело громче, молоточек 1,8 мс — «ток»); лёгкий — молоточек 1,2 мс.
        /// </summary>
        public static float[] GearTick(int tier, int variant)
        {
            var r = new Rng(Hash(0x6EA10000u + (uint)(tier * 131 + variant)));
            double fk = tier == 0 ? 1.08 : tier == 2 ? 0.84 : 1.0;
            double tk = tier == 0 ? 0.8 : tier == 2 ? 1.35 : 1.0;
            double body = tier == 0 ? 0.25 : tier == 2 ? 0.75 : 0.45;
            var modes = new[]
            {
                new Mode(300 * fk, 0.012 * tk, body),
                new Mode(690 * fk, 0.0085 * tk, 1.0),
                new Mode(1240 * fk, 0.004 * tk, 0.45),
                new Mode(1980 * fk, 0.0018 * tk, 0.12),
            };
            var s = Modal(ref r, 0.12, modes, 0.03, 0.15, 0.2, tier == 2 ? 1.8 : tier == 0 ? 1.2 : 1.4, 0.45);
            AddContact(s, ref r, 1200 * fk, 0.0015, 0.16);
            LowPass(s, 2400, 0.6);
            HighPass(s, 120, 0.7);
            FadeEdges(s, 0.0003, 0.01);
            Normalize(s, 0.9f);
            return Room(s, RoomKind.Small, -17, GrainTail);
        }

        /// <summary>
        /// «Ток» ручки — раз на оборот (каждый 8-й тик): дерево/пластик 240/520/1150/1700 Гц, τ 16/12/5/2 мс,
        /// молоточек 1,5 мс, НЧ 2 кГц, комната −15 дБ. tanφ = 1/(π f τ) ≈ 0,05 — не звенит металлом.
        /// </summary>
        public static float[] HandleTock(int variant)
        {
            var r = new Rng(Hash(0x70C40000u + (uint)variant));
            var modes = new[]
            {
                new Mode(240, 0.016, 0.6),
                new Mode(520, 0.012, 1.0),
                new Mode(1150, 0.005, 0.35),
                new Mode(1700, 0.002, 0.1),
            };
            var s = Modal(ref r, 0.14, modes, 0.03, 0.15, 0.2, 1.5, 0.4);
            AddContact(s, ref r, 900, 0.002, 0.12);
            LowPass(s, 2000, 0.6);
            HighPass(s, 110, 0.7);
            FadeEdges(s, 0.0003, 0.012);
            Normalize(s, 0.9f);
            return Room(s, RoomKind.Small, -15, GrainTail);
        }

        /// <summary>
        /// Щелчок фрикциона (собачка по зубчатому диску): 480/1050/1650/2400 Гц, τ 7/4,5/2,5/1 мс, амплитуды 0,35/1/0,55/0,18,
        /// разброс частот ±4%, τ ±15%, амплитуд ±12%. Светлый: молоточек 0,5 мс, НЧ 3,2 кГц; тёмный (быстрая пробуксовка):
        /// частоты ×0,92, молоточек 0,9 мс, НЧ 1,7 кГц. ВЧ 150 Гц, комната −18 дБ.
        /// </summary>
        public static float[] DragTick(bool dark, int variant)
        {
            var r = new Rng(Hash((dark ? 0xD4A60000u : 0xD4A50000u) + (uint)variant));
            double fk = dark ? 0.92 : 1.0;
            var modes = new[]
            {
                new Mode(480 * fk, 0.007, 0.35),
                new Mode(1050 * fk, 0.0045, 1.0),
                new Mode(1650 * fk, 0.0025, 0.55),
                new Mode(2400 * fk, 0.0010, 0.18),
            };
            var s = Modal(ref r, 0.07, modes, 0.04, 0.15, 0.15, dark ? 0.9 : 0.5, 0.45);
            AddContact(s, ref r, dark ? 1100 : 1500, 0.0012, 0.14);
            LowPass(s, dark ? 1700 : 3200, 0.6);
            HighPass(s, 150, 0.7);
            FadeEdges(s, 0.0003, 0.008);
            Normalize(s, 0.9f);
            return Room(s, RoomKind.Small, -18, GrainTail);
        }

        /// <summary>
        /// «Мурк» быстрой пробуксовки: шумовой пуфф — полоса 650 Гц ±10% (Q 1,2), атака 2 мс, спад τ 9 мс ±15%, НЧ 1,8 кГц.
        /// На 25+ щелчках в секунду часть щелчков заменяется им — трещотка сливается в мягкое «мрррр», а не в пулемёт.
        /// </summary>
        public static float[] DragPurr(int variant)
        {
            var r = new Rng(Hash(0x9C4C0000u + (uint)variant));
            int n = Samples(0.07);
            var s = new float[n];
            var bp = new Svf();
            bp.Set(650 * r.J(0.10), 1.2);
            double tau = 0.009 * r.J(0.15);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.002 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.002) : Math.Exp(-(t - 0.002) / tau);
                bp.Tick(r.S(), out _, out double b, out _);
                s[i] = (float)(b * env);
            }
            LowPass(s, 1800, 0.6);
            HighPass(s, 150, 0.7);
            FadeEdges(s, 0.0003, 0.01);
            Normalize(s, 0.9f);
            return Room(s, RoomKind.Small, -20, GrainTail);
        }

        /// <summary>
        /// Скрип удилища (только у предела): «трение с прилипанием» — импульсы 30→80→30 в секунду (интервалы ×0,6–1,6),
        /// сила импульса √(накопленного), через резонаторы 180/320/520/760/1100 Гц (Q 3,5), ВЧ 120 Гц, НЧ 1,5 кГц.
        /// Длина 0,25–0,5 с, огибающая sin^0,7, комната −15 дБ.
        /// </summary>
        public static float[] RodCreak(int variant)
        {
            var r = new Rng(Hash(0xC4EA0000u + (uint)variant));
            double len = 0.25 + 0.25 * r.U();
            int n = Samples(len);
            var pulses = new float[n];
            int ex = Samples(0.0008);
            double t = 0.01 * r.U();
            double rateTop = 65 + 25 * r.U();
            while (t < len - 0.01)
            {
                double x = t / len;
                double rate = 30 + (rateTop - 30) * Math.Sin(Math.PI * x);
                double interval = (0.6 + 1.0 * r.U()) / rate;
                double amp = Math.Sqrt(interval * rate) * Math.Pow(Math.Sin(Math.PI * x), 0.7) * r.J(0.15);
                int at = Samples(t);
                for (int i = 0; i < ex && at + i < n; i++)
                    pulses[at + i] += (float)(amp * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / ex)));
                t += interval;
            }
            var s = new float[n];
            double[] f = { 180, 320, 520, 760, 1100 };
            double[] g = { 0.8, 1.0, 0.8, 0.5, 0.25 };
            for (int k = 0; k < f.Length; k++)
            {
                double fk = f[k] * r.J(0.04);
                var res = new Resonator(fk, 3.5 / (Math.PI * fk));
                for (int i = 0; i < n; i++)
                    s[i] += (float)(g[k] * res.Tick(pulses[i]));
            }
            HighPass(s, 120, 0.7);
            LowPass(s, 1500, 0.6);
            FadeEdges(s, 0.003, 0.02);
            Normalize(s, 0.9f);
            return Room(s, RoomKind.Small, -15);
        }

        // =================================================================================================
        // Петли
        // =================================================================================================

        /// <summary>
        /// Кровать катушки, 6 с: «фррр» — шум в полосах 420 Гц (Q 0,9) + 210 Гц (Q 1,2, ×0,5) + шёпот укладки лески
        /// 1150 Гц (Q 1,3, ×0,28) и мелкая «сетка» подшипника — случайные (не периодичные!) микро-тики 900 Гц ±15%, ~45 в секунду.
        /// Дрейф громкости 1 и 2 периода на петлю (0,17 и 0,33 Гц, ±10% и ±6%). НЧ 2,2 кГц, ВЧ 110 Гц — по кругу.
        /// Высота 0,86–1,15 двигает полосы, гармоник нет — «бззз» не возникает ни на какой высоте.
        /// </summary>
        public static float[] ReelBed()
        {
            int n = Samples(6.0);
            var r = new Rng(0xBED00001u);
            var body = BandCircular(WhiteNoise(n, ref r), 420, 0.9);
            var low = BandCircular(WhiteNoise(n, ref r), 210, 1.2);
            var whisper = BandCircular(WhiteNoise(n, ref r), 1150, 1.3);
            double rb = RmsOf(body), rl = RmsOf(low), rw = RmsOf(whisper);

            // Сетка подшипника: пуассоновские микро-тики, хвосты заворачиваются в начало.
            var mesh = new float[n];
            double t = 0;
            while (true)
            {
                t += -Math.Log(1 - 0.999 * r.U()) / 45.0;
                if (t >= 6.0) break;
                double f = 900 * r.J(0.15), tau = 0.002 * r.J(0.3), amp = 0.2 + 0.8 * r.U();
                AddDecayingSine(mesh, Samples(t), f, tau, amp, 0.0006, true);
            }
            double rm = RmsOf(mesh);

            var s = new float[n];
            double p1 = TwoPi * r.U(), p2 = TwoPi * r.U();
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double drift = 1 + 0.10 * Math.Sin(TwoPi * x + p1) + 0.06 * Math.Sin(TwoPi * 2 * x + p2);
                double v = body[i] / rb + 0.5 * low[i] / rl + 0.18 * whisper[i] / rw + 0.25 * mesh[i] / rm;
                s[i] = (float)(v * drift);
            }
            Circular(s, a => { LowPass(a, 1800, 0.6); HighPass(a, 110, 0.7); });
            SoftKnee(s, 3.0);
            NormalizeLoop(s, LoopRms);
            return s;
        }

        /// <summary>
        /// Натуга катушки (вместо «рычания»), 5 с: глухое мурчание тела катушки. Резонансный шум 240 Гц (Q 2,5), 360 Гц (Q 3, ×0,8),
        /// 520 Гц (Q 3, ×0,5), 760 Гц (Q 3, ×0,25) под огибающей из нерегулярных «зёрен трения»: ~18 в секунду, интервалы ×0,6–1,6
        /// (периода нет), окно — приподнятый косинус 40–60 мс, сила √(интервал/среднее) ±20%; пол огибающей 0,5. Плюс подушка 300 Гц
        /// (Q 0,7) на −10 дБ и дрейф 1 период на петлю ±10%. НЧ 1,5 кГц, ВЧ 120 Гц. Всё по кругу.
        /// </summary>
        public static float[] StrainBed()
        {
            const double L = 5.0;
            int n = Samples(L);
            var r = new Rng(0x57A10002u);
            double[] f = { 240, 360, 520, 760 };
            double[] q = { 2.5, 3.0, 3.0, 3.0 };
            double[] g = { 1.0, 0.8, 0.5, 0.25 };
            var body = new float[n];
            for (int k = 0; k < f.Length; k++)
            {
                var b = BandCircular(WhiteNoise(n, ref r), f[k], q[k]);
                double rb = RmsOf(b);
                for (int i = 0; i < n; i++) body[i] += (float)(g[k] * b[i] / rb);
            }
            var env = new double[n];
            double t = 0, mean = 1.0 / 18;
            while (t < L)
            {
                double interval = mean * (0.6 + 1.0 * r.U());
                double amp = Math.Sqrt(interval / mean) * r.J(0.2);
                int w = Samples(0.04 + 0.02 * r.U()), at = Samples(t);
                for (int i = 0; i < w; i++)
                    env[(at + i) % n] += amp * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / w));
                t += interval;
            }
            double em = 0;
            for (int i = 0; i < n; i++) em += env[i];
            em /= n;
            var cushion = BandCircular(WhiteNoise(n, ref r), 300, 0.7);
            double rbody = RmsOf(body), rc = RmsOf(cushion);
            double ph = TwoPi * r.U();
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double drift = 1 + 0.1 * Math.Sin(TwoPi * i / n + ph);
                double e = 0.5 + 0.5 * env[i] / em;
                s[i] = (float)((e * body[i] / rbody + 0.32 * cushion[i] / rc) * drift);
            }
            Circular(s, a => { LowPass(a, 1500, 0.6); HighPass(a, 120, 0.7); });
            SoftKnee(s, 3.0);
            NormalizeLoop(s, LoopRms);
            return s;
        }

        /// <summary>
        /// Натянутая леска у предела, 7 с: «гул + шёпот» — шум в узких полосах 420 Гц (Q 6), 840 Гц (Q 5, ×0,4) и широкий шёпот
        /// 1400 Гц (Q 1, ×0,16). Дрейф 1 и 3 периода на петлю (0,14 и 0,43 Гц). Никакого тремоло и чистого тона. НЧ 2,5 кГц, ВЧ 150 Гц.
        /// </summary>
        public static float[] LineTension()
        {
            int n = Samples(7.0);
            var r = new Rng(0x11E50001u);
            var hum = BandCircular(WhiteNoise(n, ref r), 420, 6);
            var oct = BandCircular(WhiteNoise(n, ref r), 840, 5);
            var wh = BandCircular(WhiteNoise(n, ref r), 1400, 1);
            double a = RmsOf(hum), b = RmsOf(oct), c = RmsOf(wh);
            double p1 = TwoPi * r.U(), p2 = TwoPi * r.U();
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double drift = 1 + 0.15 * Math.Sin(TwoPi * x + p1) + 0.08 * Math.Sin(TwoPi * 3 * x + p2);
                s[i] = (float)((hum[i] / a + 0.4 * oct[i] / b + 0.16 * wh[i] / c) * drift);
            }
            Circular(s, x => { LowPass(x, 2500, 0.6); HighPass(x, 150, 0.7); });
            NormalizeLoop(s, LoopRms);
            return s;
        }

        /// <summary>
        /// Утреннее озеро, 64 с, без шва. Птиц здесь нет — их ставит расписание (BirdCall), чтобы за 2 часа ничего
        /// узнаваемо не повторялось.
        /// - Плеск у берега: 24 «наката» (2,7 с ±0,9 с), каждый — шум через НЧ, срез идёт за огибающей 250→900 Гц
        ///   (подъём 0,35–0,6 с, спад τ 0,5–0,9 с); на гребне 0–3 пузырька r 2,5–6 мм.
        /// - Подложка воды: шум НЧ 300 Гц, дрейф 3/5/7 периодов на петлю (≤ 0,11 Гц).
        /// - Ветерок в камыше: шум в полосах 850 Гц (Q 0,8) и 1500 Гц (Q 1); порывы — периодический «гладкий шум» из гармоник
        ///   2–10 периодов на петлю, в квадрате; шорох — редкие микро-щелчки 1,3–2,4 кГц, 4 + 40·порыв² в секунду. −9 дБ под водой.
        /// - Дальняя рыбка: 2 «плинька» с дальним хвостом.
        /// Всё по кругу, плюс дальний «воздух» (реверберация берега) тоже по кругу. НЧ 3 кГц, ВЧ 60 Гц.
        /// </summary>
        public static float[] AmbienceBed()
        {
            const double L = 64.0;
            int n = Samples(L);
            var r = new Rng(0xA4B10001u);

            // Плеск.
            var water = new float[n];
            const int laps = 24;
            for (int k = 0; k < laps; k++)
            {
                double at = (k + 0.5 + 0.35 * r.S()) * L / laps;
                double rise = 0.35 + 0.25 * r.U(), fall = 0.5 + 0.4 * r.U(), amp = 0.65 + 0.35 * r.U();
                int start = Samples(at), m = Samples(rise + fall * 5);
                var lp = new Svf();
                for (int i = 0; i < m; i++)
                {
                    double tt = (double)i / SampleRate;
                    double env = tt < rise ? 0.5 - 0.5 * Math.Cos(Math.PI * tt / rise) : Math.Exp(-(tt - rise) / fall);
                    lp.Set(250 + 650 * env, 0.7);
                    lp.Tick(r.S(), out double low, out _, out _);
                    water[(start + i) % n] += (float)(amp * env * low);
                }
                int bubbles = (int)(r.U() * 4);
                for (int b = 0; b < bubbles; b++)
                    AddBubble(water, Samples(at + rise * (0.6 + 0.8 * r.U())), 2.5 + 3.5 * r.U(), 0.08 * r.J(0.3), true);
            }
            double rw = RmsOf(water);

            var floor = LowCircular(WhiteNoise(n, ref r), 300, 0.7);
            double rf = RmsOf(floor);
            double f1 = TwoPi * r.U(), f2 = TwoPi * r.U(), f3 = TwoPi * r.U();

            // Ветерок.
            var breezeA = BandCircular(WhiteNoise(n, ref r), 850, 0.8);
            var breezeB = BandCircular(WhiteNoise(n, ref r), 1500, 1.0);
            double ra = RmsOf(breezeA), rb = RmsOf(breezeB);
            var gustAmp = new double[11];
            var gustPh = new double[11];
            for (int k = 2; k <= 10; k++) { gustAmp[k] = (1.0 / k) * (0.5 + r.U()); gustPh[k] = TwoPi * r.U(); }
            var gust = new double[n];
            double gMin = double.MaxValue, gMax = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n, v = 0;
                for (int k = 2; k <= 10; k++) v += gustAmp[k] * Math.Sin(TwoPi * k * x + gustPh[k]);
                gust[i] = v;
                gMin = Math.Min(gMin, v); gMax = Math.Max(gMax, v);
            }
            for (int i = 0; i < n; i++)
            {
                double g = (gust[i] - gMin) / (gMax - gMin);
                gust[i] = 0.12 + 0.88 * g * g;
            }
            var rustle = new float[n];
            {
                double tt = 0;
                while (true)
                {
                    // Неоднородный пуассоновский поток: прореживание по плотности порыва.
                    tt += -Math.Log(1 - 0.999 * r.U()) / 44.0;
                    if (tt >= L) break;
                    double g = gust[Math.Min(n - 1, Samples(tt))];
                    double density = 4 + 40 * g * g;
                    if (r.U() * 44.0 > density) continue;
                    AddDecayingSine(rustle, Samples(tt), 1300 + 1100 * r.U(), 0.0015 * r.J(0.3), (0.3 + 0.7 * r.U()) * g, 0.0004, true);
                }
            }
            double rr = RmsOf(rustle);

            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double drift = 1 + 0.15 * Math.Sin(TwoPi * 3 * x + f1) + 0.1 * Math.Sin(TwoPi * 5 * x + f2) + 0.06 * Math.Sin(TwoPi * 7 * x + f3);
                double w = water[i] / rw + 0.8 * drift * floor[i] / rf;
                double br = gust[i] * (breezeA[i] / ra + 0.5 * breezeB[i] / rb) + 0.25 * rustle[i] / rr;
                s[i] = (float)(w + 0.36 * br);
            }

            // Дальняя рыбка: 2 «плинька».
            for (int k = 0; k < 2; k++)
            {
                double at = (k + 0.3 + 0.4 * r.U()) * L / 2;
                AddBubble(s, Samples(at), 4 + 1.5 * r.U(), 0.5, true);
            }

            Circular(s, a => { LowPass(a, 3000, 0.6); HighPass(a, 60, 0.7); });
            s = RoomCircular(s, RoomKind.Far, -14);
            NormalizeLoop(s, 0.1);
            return s;
        }

        // =================================================================================================
        // Разовые
        // =================================================================================================

        /// <summary>
        /// Подсечка: мягкое «фвумп» — свист удилища (полоса 300→900 Гц за 70 мс, Q 1,5), глухой удар 125→80 Гц с 2-й и 3-й
        /// гармониками (слышен на телефоне), «тунн» лески 410/820 Гц (τ 90/40 мс) и маленький «бульк» r 7 мм. НЧ 2,5 кГц, комната −12 дБ.
        /// </summary>
        public static float[] Hookset()
        {
            var r = new Rng(0x400C0001u);
            int n = Samples(0.45);
            var s = new float[n];
            AddSweptNoise(s, ref r, 0.0, 0.08, 300, 900, 1.5, 0.5, 1.5);
            AddTone(s, 0.012, 0.3, 125, 80, 1.0, 0.003, 0.05, 0.55, 0.3);
            AddModal(s, ref r, 0.015, 0.3, new[] { new Mode(410, 0.09, 0.35), new Mode(820, 0.04, 0.12) }, 2.0);
            AddBubble(s, Samples(0.035), 7, 0.25, false);
            LowPass(s, 2500, 0.6);
            HighPass(s, 60, 0.7);
            FadeEdges(s, 0.0003, 0.02);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -12));
        }

        /// <summary>Телеграф ухода: водоворот — полоса шума 300→750→450 Гц (Q 2,2), огибающая sin^1,5 на 0,55 с, 4 пузырька r 4–7 мм.</summary>
        public static float[] TeleRun() => Swirl(0x7E1E0001u, 0.55, 300, 750, 450, 2.2, 4, 4, 7, -14);

        /// <summary>Телеграф ухода вглубь: «глуп» вниз — полоса 600→420→260 Гц (Q 2,5), 0,6 с, 3 крупных пузыря r 8–11 мм (270–375 Гц).</summary>
        public static float[] TeleDive() => Swirl(0x7E1E0002u, 0.6, 600, 420, 260, 2.5, 3, 8, 11, -14);

        /// <summary>Телеграф броска: быстрый напор — полоса 350→1100→700 Гц (Q 1,2), 0,38 с, 6 пузырьков r 3–6 мм.</summary>
        public static float[] TeleSurge() => Swirl(0x7E1E0003u, 0.38, 350, 1100, 700, 1.2, 6, 3, 6, -14);

        /// <summary>
        /// Телеграф рывка: «ту-дум», как сердце — два глухих удара 150→120 Гц с гармониками (второй через 120 мс, −4 дБ),
        /// на каждом деревянная мода 600 Гц. НЧ 1,8 кГц, комната −14 дБ.
        /// </summary>
        public static float[] TeleJerk()
        {
            var r = new Rng(0x7E1E0004u);
            int n = Samples(0.32);
            var s = new float[n];
            AddTone(s, 0.0, 0.16, 150, 120, 1.0, 0.004, 0.035, 0.6, 0.35);
            AddTone(s, 0.12, 0.16, 145, 118, 0.63, 0.004, 0.035, 0.6, 0.35);
            AddModal(s, ref r, 0.0, 0.08, new[] { new Mode(600, 0.012, 0.25) }, 1.5);
            AddModal(s, ref r, 0.12, 0.08, new[] { new Mode(590, 0.012, 0.16) }, 1.5);
            LowPass(s, 1800, 0.6);
            HighPass(s, 70, 0.7);
            FadeEdges(s, 0.0003, 0.02);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -14));
        }

        /// <summary>Телеграф свечки: воздушный подъём — полоса 280→1500 Гц (Q 1,4), огибающая x², 0,5 с, мягкий конец; НЧ 2,6 кГц.</summary>
        public static float[] TeleJump()
        {
            var r = new Rng(0x7E1E0005u);
            int n = Samples(0.5);
            var s = new float[n];
            var bp = new Svf();
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                bp.Set(280 * Math.Pow(1500.0 / 280.0, x), 1.4);
                bp.Tick(r.S(), out _, out double b, out _);
                double env = x * x * Math.Min(1.0, (1.0 - x) / 0.12);
                s[i] = (float)(b * env);
            }
            AddBubble(s, Samples(0.36), 6, 0.12, false);
            LowPass(s, 2600, 0.6);
            HighPass(s, 120, 0.7);
            FadeEdges(s, 0.0003, 0.01);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -14));
        }

        /// <summary>
        /// Всплески: корона (шум, атака 2 мс) + облако пузырей (радиусы ∝ r^−2,5, начала по экспоненте), дальний хвост берега.
        /// small — рыбка: корона τ 30 мс, НЧ 1,8 кГц, 9 пузырей r 2–6 мм; big — крупная: τ 55 мс, НЧ 1,3 кГц, 16 пузырей r 3–12 мм,
        /// «тумп» 90/180/270 Гц; jump — выход на свечке: воздушная корона τ 80 мс (НЧ 2 кГц) и капли, падающие обратно 0,2–0,9 с.
        /// </summary>
        public static float[] Splash(int kind)
        {
            var r = new Rng(0x5A1A0000u + (uint)kind);
            bool big = kind == 1, jump = kind == 2;
            double len = big ? 0.75 : jump ? 1.0 : 0.5;
            int n = Samples(len);
            var s = new float[n];
            double cTau = big ? 0.055 : jump ? 0.08 : 0.03;
            var crown = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.002 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.002) : Math.Exp(-(t - 0.002) / cTau);
                crown[i] = (float)(r.S() * env);
            }
            LowPass(crown, big ? 1300 : jump ? 2000 : 1800, 0.7);
            HighPass(crown, big ? 120 : 200, 0.7);
            Normalize(crown, 1f);
            for (int i = 0; i < n; i++) s[i] += 0.55f * crown[i];

            int count = big ? 16 : jump ? 10 : 9;
            double rMin = big ? 3 : 2, rMax = big ? 12 : jump ? 8 : 6, onset = big ? 0.13 : 0.1;
            for (int k = 0; k < count; k++)
            {
                double t = 0.004 - onset * Math.Log(1 - 0.95 * r.U());
                double rad = PowerLawRadius(r.U(), rMin, rMax);
                AddBubble(s, Samples(t), rad, 0.32 * r.J(0.3), false);
            }
            if (jump)
                for (int k = 0; k < 8; k++)
                    AddBubble(s, Samples(0.2 + 0.7 * r.U()), 2 + 2 * r.U(), 0.14 * r.J(0.3), false);
            if (big)
                AddTone(s, 0.0, 0.2, 90, 80, 0.4, 0.003, 0.04, 0.6, 0.35);

            LowPass(s, 2600, 0.6);
            HighPass(s, 70, 0.7);
            FadeEdges(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Far, -16));
        }

        /// <summary>
        /// Награда за улов: 3/4/5 мокрых шлепков (≈7 в секунду, мягкий шум НЧ 1,1 кГц + 160 Гц), затем тёплый «маримба»-арпеджио
        /// по пентатонике соль мажор с шагом 110 мс (обертоны 1, 2 (×0,12), 3,9 (×0,08 коротко)), последняя нота длиннее.
        /// tier 0: си–ре–соль; 1: соль–си–ре–соль; 2: ре–соль–си–ре–соль + низкая соль 196 Гц «цветёт» под концом.
        /// Комната −10 дБ (теплее и ближе, чем всё остальное).
        /// </summary>
        public static float[] Landed(int tier)
        {
            var r = new Rng(0x1A4D0000u + (uint)tier);
            int flops = 3 + tier;
            double[][] notes =
            {
                new[] { 493.88, 587.33, 783.99 },
                new[] { 392.00, 493.88, 587.33, 783.99 },
                new[] { 293.66, 392.00, 493.88, 587.33, 783.99 },
            };
            var chord = notes[tier];
            double flopEnd = 0.02 + flops * 0.14;
            double len = flopEnd + 0.1 + chord.Length * 0.11 + 1.3 + 0.2 * tier;
            int n = Samples(len);
            var s = new float[n];
            double t = 0.01;
            for (int k = 0; k < flops; k++)
            {
                double amp = 0.55 * (1 - 0.12 * k) * r.J(0.15);
                AddFlop(s, ref r, t, amp);
                t += 0.14 * r.J(0.15);
            }
            double c0 = t + 0.08;
            for (int k = 0; k < chord.Length; k++)
            {
                bool last = k == chord.Length - 1;
                AddMallet(s, c0 + 0.11 * k, chord[k], last ? 0.85 : 0.7, last ? 0.75 + 0.15 * tier : 0.45);
            }
            if (tier == 2)
                AddPad(s, c0 + 0.11 * (chord.Length - 2), 1.4, 196.0, 0.22);
            LowPass(s, 3000, 0.6);
            HighPass(s, 80, 0.7);
            FadeEdges(s, 0.0003, 0.05);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -10));
        }

        /// <summary>
        /// Обрыв: короткий глухой щелчок (3 мс шума, НЧ 3,5 кГц) + «тунн» ослабшей струны 520/1040/1560 Гц (τ 120/60/30 мс)
        /// с провалом высоты на 3% + шёпот падающей лески (полоса 900 Гц, τ 180 мс). Редкий звук — ему можно чуть ярче.
        /// </summary>
        public static float[] LineSnap()
        {
            var r = new Rng(0x5AA90001u);
            int n = Samples(0.7);
            var s = new float[n];
            int m = Samples(0.003);
            var burst = new float[m * 4];
            for (int i = 0; i < burst.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.0004 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.0004) : Math.Exp(-(t - 0.0004) / 0.0015);
                burst[i] = (float)(r.S() * env);
            }
            LowPass(burst, 3500, 0.7);
            Normalize(burst, 0.6f);
            for (int i = 0; i < burst.Length; i++) s[i] += burst[i];
            AddGlideString(s, 0.002, 0.6, 520, 0.97, new[] { 1.0, 0.4, 0.15 }, new[] { 0.12, 0.06, 0.03 }, 0.8);
            AddSweptNoise(s, ref r, 0.05, 0.6, 900, 800, 0.8, 0.2, 0, 0.02, 0.18);
            LowPass(s, 3500, 0.6);
            HighPass(s, 90, 0.7);
            FadeEdges(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -14));
        }

        /// <summary>Поломка удилища: глухой деревянный треск (шум τ 5 мс, НЧ 2,5 кГц), 10 редеющих щепок, удар 110→75 Гц с гармониками.</summary>
        public static float[] RodCrack()
        {
            var r = new Rng(0xC4AC0001u);
            int n = Samples(0.5);
            var s = new float[n];
            for (int i = 0; i < Samples(0.04); i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.0005 ? t / 0.0005 : Math.Exp(-(t - 0.0005) / 0.005);
                s[i] += (float)(r.S() * env);
            }
            for (int k = 0; k < 10; k++)
            {
                double x = (double)k / 10;
                double at = 0.012 + 0.25 * Math.Pow(x, 1.5) + 0.005 * r.S();
                double amp = 0.15 + 0.45 * (1 - x);
                AddModal(s, ref r, Math.Max(0, at), 0.04, new[] { new Mode(700 * r.J(0.2), 0.004, amp), new Mode(1400 * r.J(0.2), 0.002, amp * 0.4) }, 0.6);
            }
            LowPass(s, 2500, 0.6);
            AddTone(s, 0.0, 0.25, 110, 75, 0.6, 0.002, 0.05, 0.5, 0.25);
            HighPass(s, 70, 0.7);
            FadeEdges(s, 0.0003, 0.02);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -13));
        }

        /// <summary>Сход: тихий «блуп» (пузырь r 5,5 мм, подъём высоты ξ 0,15), второй поменьше через 90 мс, рябь (шум НЧ 700 Гц, τ 0,25 с). Без радости.</summary>
        public static float[] Escape()
        {
            var r = new Rng(0xE5CA0001u);
            int n = Samples(0.8);
            var s = new float[n];
            AddBubble(s, Samples(0.003), 5.5, 1.0, false, 0.15);
            AddBubble(s, Samples(0.093), 4.0, 0.45, false, 0.12);
            AddSweptNoise(s, ref r, 0.02, 0.7, 700, 500, 0.7, 0.18, 0, 0.03, 0.25, lowPassMode: true);
            LowPass(s, 2200, 0.6);
            HighPass(s, 90, 0.7);
            FadeEdges(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Far, -14));
        }

        /// <summary>
        /// Заброс: свист удилища (полоса 260→800→350 Гц, Q 1,6, 0,3 с) и сходящая со шпули леска — тихое «шшш» 1300→800 Гц
        /// с затуханием τ 0,45 с и редеющими микро-тиками шпули (60→10 в секунду). Комната −16 дБ.
        /// </summary>
        public static float[] CastWhoosh()
        {
            var r = new Rng(0xCA570001u);
            int n = Samples(1.4);
            var s = new float[n];
            var bp = new Svf();
            int w = Samples(0.3);
            for (int i = 0; i < w; i++)
            {
                double x = (double)i / w;
                double fc = x < 0.5 ? 260 * Math.Pow(800.0 / 260.0, x / 0.5) : 800 * Math.Pow(350.0 / 800.0, (x - 0.5) / 0.5);
                bp.Set(fc, 1.6);
                bp.Tick(r.S(), out _, out double b, out _);
                s[i] += (float)(b * Math.Pow(Math.Sin(Math.PI * x), 1.5));
            }
            Normalize(s, 1f);
            AddSweptNoise(s, ref r, 0.12, 1.25, 1300, 800, 1.0, 0.22, 0, 0.04, 0.45);
            double t = 0.13;
            while (t < 1.3)
            {
                double x = (t - 0.13) / 1.17;
                double rate = 60 * Math.Pow(10.0 / 60.0, x);
                AddDecayingSine(s, Samples(t), 950 * r.J(0.12), 0.0018, 0.06 * (1 - x) * r.J(0.3), 0.0005, false);
                t += (0.7 + 0.6 * r.U()) / rate;
            }
            LowPass(s, 2400, 0.6);
            HighPass(s, 120, 0.7);
            FadeEdges(s, 0.0003, 0.05);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, -16));
        }

        /// <summary>Поплавок лёг: «плоп» — пузырь r 6,5 мм (460 Гц), корона 12 мс (НЧ 1,5 кГц) и два мелких пузырька, дальний хвост.</summary>
        public static float[] FloatPlop()
        {
            var r = new Rng(0xF10A0001u);
            int n = Samples(0.5);
            var s = new float[n];
            AddBubble(s, Samples(0.005), 6.5, 1.0, false);
            var crown = new float[Samples(0.03)];
            for (int i = 0; i < crown.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.0015 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.0015) : Math.Exp(-(t - 0.0015) / 0.006);
                crown[i] = (float)(r.S() * env);
            }
            LowPass(crown, 1500, 0.7);
            Normalize(crown, 0.35f);
            for (int i = 0; i < crown.Length; i++) s[i] += crown[i];
            AddBubble(s, Samples(0.04 + 0.03 * r.U()), 2.5 + r.U(), 0.25, false);
            AddBubble(s, Samples(0.09 + 0.03 * r.U()), 2.5 + r.U(), 0.2, false);
            LowPass(s, 2200, 0.6);
            HighPass(s, 90, 0.7);
            FadeEdges(s, 0.0003, 0.02);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Far, -14));
        }

        /// <summary>Поклёвка: «плинь-плинь» поплавка — пузырьки r 3,5 и 4,2 мм через 110 мс и тихая рябь.</summary>
        public static float[] BiteBob()
        {
            var r = new Rng(0xB17E0001u);
            int n = Samples(0.45);
            var s = new float[n];
            AddBubble(s, Samples(0.003), 3.5, 0.6, false);
            AddBubble(s, Samples(0.113), 4.2, 0.45, false);
            AddSweptNoise(s, ref r, 0.0, 0.3, 900, 700, 0.7, 0.08, 0, 0.004, 0.04, lowPassMode: true);
            LowPass(s, 2200, 0.6);
            HighPass(s, 100, 0.7);
            FadeEdges(s, 0.0003, 0.02);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Far, -16));
        }

        /// <summary>
        /// Дальние птицы (расписание, не петля; 2/3/6 вариантов). kind 0 — кукушка (4–6 «ку-ку» 700/570 Гц, период 0,85 с); 1 — вяхирь
        /// («ху-ХУУ-ху, ху-ху» ~420 Гц с придыханием); 2, 3 — чёрный дрозд (флейтовые ноты 1,5–2,5 кГц с глиссандо и тихий росчерк).
        /// Даль: НЧ 1,4–3 кГц и дальний хвост −8 дБ (чем дальше — тем «мокрее»).
        /// </summary>
        /// <summary>Вариантов птиц: кукушка 2, вяхирь 3, дрозд 6.</summary>
        public static int BirdVariants(int kind) => kind == 0 ? 2 : kind == 1 ? 3 : 6;

        public static float[] BirdCall(int kind, int variant)
        {
            var r = new Rng(Hash(0xB14D0000u + (uint)(kind * 16 + variant)));
            float[] s;
            switch (kind)
            {
                case 0:
                {
                    int calls = 4 + (int)(r.U() * 3);
                    double k = r.J(0.03);
                    s = new float[Samples(calls * 0.85 + 0.4)];
                    for (int c = 0; c < calls; c++)
                    {
                        double at = c * 0.85 * r.J(0.02);
                        double amp = 1 - 0.06 * c;
                        AddHoot(s, at, 0.16, 700 * k, 680 * k, amp, 0.12, 0.04, 0.0);
                        AddHoot(s, at + 0.26, 0.24, 575 * k, 555 * k, amp * 0.9, 0.12, 0.04, 0.0);
                    }
                    LowPass(s, 1600, 0.6);
                    break;
                }
                case 1:
                {
                    s = new float[Samples(3.2)];
                    double[] at = { 0.0, 0.38, 0.98, 1.6, 1.98 };
                    double[] dur = { 0.26, 0.45, 0.28, 0.3, 0.32 };
                    double[] amp = { 0.7, 1.0, 0.65, 0.8, 0.7 };
                    double tempo = 0.9 + 0.25 * r.U(), pitch = 380 + 60 * r.U();
                    int hoots = variant == 2 ? 3 : 5;
                    for (int c = 0; c < hoots; c++)
                    {
                        double f = pitch * r.J(0.03);
                        at[c] *= tempo;
                        AddHoot(s, at[c], dur[c], f * 0.97, f * 1.06, amp[c], 0.3, 0.08, 0.12, r);
                    }
                    LowPass(s, 1400, 0.6);
                    break;
                }
                default:
                {
                    s = new float[Samples(2.2)];
                    double t = 0.0;
                    int notes = 5 + (int)(r.U() * 4);
                    for (int c = 0; c < notes; c++)
                    {
                        double d = 0.06 + 0.12 * r.U();
                        double f0 = 1300 + 700 * r.U(), f1 = f0 * (0.8 + 0.4 * r.U());
                        AddWhistle(s, t, d, f0, f1, 0.6 + 0.4 * r.U());
                        t += d + 0.03 + 0.05 * r.U();
                    }
                    for (int c = 0; c < 4; c++)
                    {
                        AddWhistle(s, t, 0.03, 2000 - 60 * c, 1850 - 60 * c, 0.25);
                        t += 0.045;
                    }
                    LowPass(s, 2600, 0.6);
                    break;
                }
            }
            HighPass(s, 150, 0.7);
            FadeEdges(s, 0.0003, 0.03);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Far, -8));
        }

        // =================================================================================================
        // Кирпичики разовых
        // =================================================================================================

        private static float[] Swirl(uint seed, double len, double f0, double fm, double f1, double q, int bubbles, double rMin, double rMax, double roomDb)
        {
            var r = new Rng(seed);
            int n = Samples(len);
            var s = new float[n];
            var bp = new Svf();
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double fc = x < 0.5 ? f0 * Math.Pow(fm / f0, x / 0.5) : fm * Math.Pow(f1 / fm, (x - 0.5) / 0.5);
                bp.Set(fc, q);
                bp.Tick(r.S(), out _, out double b, out _);
                s[i] = (float)(b * Math.Pow(Math.Sin(Math.PI * x), 1.5));
            }
            Normalize(s, 1f);
            for (int k = 0; k < bubbles; k++)
                AddBubble(s, Samples(len * (0.15 + 0.7 * r.U())), rMin + (rMax - rMin) * r.U(), 0.3 * r.J(0.3), false);
            LowPass(s, 2200, 0.6);
            HighPass(s, 100, 0.7);
            FadeEdges(s, 0.0003, 0.02);
            Normalize(s, 0.9f);
            return Finish(Room(s, RoomKind.Small, roomDb));
        }

        /// <summary>Мокрый шлепок рыбы по траве/подсачеку: шум 25 мс (τ 10 мс, НЧ 1,1 кГц) + «тук» 160 Гц с 2-й гармоникой.</summary>
        private static void AddFlop(float[] s, ref Rng r, double at, double amp)
        {
            int m = Samples(0.05);
            var b = new float[m];
            var lp = new Svf();
            lp.Set(1100 * r.J(0.15), 0.8);
            for (int i = 0; i < m; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.0015 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.0015) : Math.Exp(-(t - 0.0015) / 0.01);
                lp.Tick(r.S(), out double low, out _, out _);
                b[i] = (float)(low * env);
            }
            Normalize(b, 1f);
            int from = Samples(at);
            for (int i = 0; i < m && from + i < s.Length; i++) s[from + i] += (float)(amp * b[i]);
            AddTone(s, at, 0.06, 165 * r.J(0.05), 140, amp * 0.5, 0.002, 0.015, 0.5, 0.2);
            AddBubble(s, Samples(at + 0.01), 3 + 2 * r.U(), 0.12 * amp, false);
        }

        /// <summary>«Маримба»: обертоны 1 (τ decay), 2 (×0,12, τ/2), 3,9 (×0,08, τ/6), атака 4 мс приподнятым косинусом.</summary>
        private static void AddMallet(float[] s, double at, double f, double amp, double decay)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(decay * 6), s.Length - from);
            int att = Samples(0.004);
            for (int i = 0; i < m; i++)
            {
                double t = (double)i / SampleRate;
                double a = i < att ? 0.5 - 0.5 * Math.Cos(Math.PI * i / att) : 1.0;
                double v = Math.Sin(TwoPi * f * t) * Math.Exp(-t / decay)
                         + 0.12 * Math.Sin(TwoPi * 2 * f * t) * Math.Exp(-t / (decay / 2))
                         + 0.08 * Math.Sin(TwoPi * 3.9 * f * t) * Math.Exp(-t / (decay / 6));
                if (i > m - Samples(0.01)) a *= (double)(m - i) / Samples(0.01);
                s[from + i] += (float)(amp * a * v);
            }
        }

        /// <summary>Мягкая «подушка» низкой ноты: синус + 2-я гармоника, атака 80 мс, спад τ = dur/2.</summary>
        private static void AddPad(float[] s, double at, double dur, double f, double amp)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(dur * 2.5), s.Length - from);
            for (int i = 0; i < m; i++)
            {
                double t = (double)i / SampleRate;
                double a = t < 0.08 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.08) : Math.Exp(-(t - 0.08) / (dur / 2));
                if (i > m - Samples(0.02)) a *= (double)(m - i) / Samples(0.02);
                s[from + i] += (float)(amp * a * (Math.Sin(TwoPi * f * t) + 0.35 * Math.Sin(TwoPi * 2 * f * t)));
            }
        }

        /// <summary>Струна с провалом высоты: обертоны k·f, частота уходит к f·drop за dur; τ на обертон.</summary>
        private static void AddGlideString(float[] s, double at, double dur, double f, double drop, double[] amps, double[] taus, double gain)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(dur), s.Length - from);
            var ph = new double[amps.Length];
            int att = Samples(0.0008);
            for (int i = 0; i < m; i++)
            {
                double t = (double)i / SampleRate;
                double fi = f * Math.Pow(drop, Math.Min(1.0, t / 0.2));
                double v = 0;
                for (int k = 0; k < amps.Length; k++)
                {
                    ph[k] += TwoPi * fi * (k + 1) / SampleRate;
                    v += amps[k] * Math.Sin(ph[k]) * Math.Exp(-t / taus[k]);
                }
                double a = i < att ? 0.5 - 0.5 * Math.Cos(Math.PI * i / att) : 1.0;
                if (i > m - Samples(0.01)) a *= (double)(m - i) / Samples(0.01);
                s[from + i] += (float)(gain * a * v);
            }
        }

        /// <summary>Шум в полосе (или НЧ при lowPassMode), срез идёт f0→f1 экспоненциально; огибающая — sin^shape или атака/спад.</summary>
        private static void AddSweptNoise(float[] s, ref Rng r, double at, double dur, double f0, double f1, double q, double amp,
            double shape, double attack = 0, double decay = 0, bool lowPassMode = false)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(dur), s.Length - from);
            var f = new Svf();
            var tmp = new float[m];
            for (int i = 0; i < m; i++)
            {
                double x = (double)i / m;
                double t = (double)i / SampleRate;
                f.Set(f0 * Math.Pow(f1 / f0, x), q);
                f.Tick(r.S(), out double lp, out double bp, out _);
                double env = shape > 0
                    ? Math.Pow(Math.Sin(Math.PI * x), shape)
                    : (t < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * t / attack) : Math.Exp(-(t - attack) / decay)) * Math.Min(1.0, (1 - x) / 0.05);
                tmp[i] = (float)((lowPassMode ? lp : bp) * env);
            }
            Normalize(tmp, 1f);
            for (int i = 0; i < m; i++) s[from + i] += (float)(amp * tmp[i]);
        }

        /// <summary>Уханье птицы: синус + 2-я и 3-я гармоники, частота f0→f1 (дугой), атака/спад; breath — доля полосового шума.</summary>
        private static void AddHoot(float[] s, double at, double dur, double f0, double f1, double amp, double h2, double h3, double breath, Rng? noise = null)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(dur), s.Length - from);
            double ph = 0;
            var r = noise ?? new Rng(Hash((uint)(at * 1000) + 7));
            var bp = new Svf();
            for (int i = 0; i < m; i++)
            {
                double x = (double)i / m;
                double f = f0 + (f1 - f0) * Math.Sin(Math.PI * 0.5 * x);
                ph += TwoPi * f / SampleRate;
                double env = Math.Min(1.0, x / 0.15);
                env = 0.5 - 0.5 * Math.Cos(Math.PI * env);
                env *= Math.Min(1.0, (1 - x) / 0.25);
                bp.Set(f, 4);
                bp.Tick(r.S(), out _, out double b, out _);
                double v = Math.Sin(ph) + h2 * Math.Sin(2 * ph) + h3 * Math.Sin(3 * ph) + breath * 4 * b;
                s[from + i] += (float)(amp * env * v);
            }
        }

        /// <summary>Флейтовая нота дрозда: синус с плавным глиссандо f0→f1, огибающая sin, 2-я гармоника 0,05.</summary>
        private static void AddWhistle(float[] s, double at, double dur, double f0, double f1, double amp)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(dur), s.Length - from);
            double ph = 0;
            for (int i = 0; i < m; i++)
            {
                double x = (double)i / m;
                double f = f0 * Math.Pow(f1 / f0, x);
                ph += TwoPi * f / SampleRate;
                double env = Math.Sin(Math.PI * x);
                s[from + i] += (float)(amp * env * env * (Math.Sin(ph) + 0.05 * Math.Sin(2 * ph)));
            }
        }

        // =================================================================================================
        // Общие кирпичики
        // =================================================================================================

        private struct Mode
        {
            public double F, Tau, A;
            public Mode(double f, double tau, double a) { F = f; Tau = tau; A = a; }
        }

        /// <summary>Модальный удар: возбудитель — приподнятый косинус exciterMs (с долей шума), моды — двухполюсные резонаторы.</summary>
        private static float[] Modal(ref Rng r, double seconds, Mode[] modes, double fJit, double tauJit, double aJit, double exciterMs, double exciterNoise)
        {
            var s = new float[Samples(seconds)];
            AddModalCore(s, 0, ref r, modes, fJit, tauJit, aJit, exciterMs, exciterNoise);
            return s;
        }

        private static void AddModal(float[] s, ref Rng r, double at, double seconds, Mode[] modes, double exciterMs)
        {
            int from = Samples(at);
            int m = Math.Min(Samples(seconds), s.Length - from);
            if (m <= 0) return;
            var tmp = new float[m];
            AddModalCore(tmp, 0, ref r, modes, 0.02, 0.1, 0.05, exciterMs, 0.2);
            for (int i = 0; i < m; i++) s[from + i] += tmp[i];
        }

        private static void AddModalCore(float[] s, int from, ref Rng r, Mode[] modes, double fJit, double tauJit, double aJit, double exciterMs, double exciterNoise)
        {
            int n = s.Length - from;
            int ex = Math.Max(3, Samples(exciterMs / 1000.0));
            var e = new double[ex];
            double area = 0;
            for (int i = 0; i < ex; i++)
            {
                e[i] = (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / ex)) * (1 - exciterNoise + exciterNoise * r.S());
                area += Math.Abs(e[i]);
            }
            for (int i = 0; i < ex; i++) e[i] /= area;
            foreach (var mode in modes)
            {
                double f = mode.F * r.J(fJit), tau = mode.Tau * r.J(tauJit), a = mode.A * r.J(aJit);
                if (f >= 0.45 * SampleRate) continue;
                var res = new Resonator(f, tau);
                for (int i = 0; i < n; i++)
                    s[from + i] += (float)(a * res.Tick(i < ex ? e[i] : 0.0));
            }
        }

        /// <summary>
        /// Пузырь (Миннарт): f0 = 3 м/с / r, f(t) = f0(1 + ξβt), β = 1/τ, τ = 6 мс + 4 мс × r(мм), амплитуда ∝ r; атака 0,8 мс.
        /// wrap — хвост заворачивается в начало (для петель).
        /// </summary>
        private static void AddBubble(float[] s, int start, double rMm, double gain, bool wrap, double xi = 0.1)
        {
            double f0 = 3.0 / (rMm / 1000.0);
            double tau = 0.006 + 0.004 * rMm;
            double beta = 1.0 / tau;
            int m = (int)(tau * 6 * SampleRate);
            double ph = 0, amp = gain * rMm / 8.0;
            for (int i = 0; i < m; i++)
            {
                int k = start + i;
                if (k >= s.Length) { if (!wrap) break; k %= s.Length; }
                double t = (double)i / SampleRate;
                double f = Math.Min(f0 * (1 + xi * beta * t), 0.4 * SampleRate);
                ph += TwoPi * f / SampleRate;
                double att = t < 0.0008 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.0008) : 1.0;
                double tail = i > m - 40 ? (m - i) / 40.0 : 1.0;
                s[k] += (float)(amp * att * tail * Math.Sin(ph) * Math.Exp(-t / tau));
            }
        }

        /// <summary>
        /// «Касание» — у каждого варианта свой короткий шумовой штрих (полоса fc, Q 0,8; огибающая — приподнятый косинус dur):
        /// живая разница между вариантами при тех же модах. level — доля от пика модального удара.
        /// </summary>
        private static void AddContact(float[] s, ref Rng r, double fc, double dur, double level)
        {
            double peak = 0;
            for (int i = 0; i < s.Length; i++) peak = Math.Max(peak, Math.Abs(s[i]));
            int m = Math.Max(3, Samples(dur));
            var bp = new Svf();
            bp.Set(fc * r.J(0.15), 0.8);
            var tmp = new float[m + Samples(0.004)];
            for (int i = 0; i < tmp.Length; i++)
            {
                double x = i < m ? r.S() * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / m)) : 0;
                bp.Tick(x, out _, out double b, out _);
                tmp[i] = (float)b;
            }
            Normalize(tmp, (float)(peak * level * r.J(0.3)));
            for (int i = 0; i < tmp.Length && i < s.Length; i++) s[i] += tmp[i];
        }

        /// <summary>Затухающий синус с атакой (микро-тик). wrap — для петель.</summary>
        private static void AddDecayingSine(float[] s, int start, double f, double tau, double amp, double attack, bool wrap)
        {
            int m = (int)(tau * 7 * SampleRate) + Samples(attack);
            int att = Math.Max(1, Samples(attack));
            double w = TwoPi * f / SampleRate;
            for (int i = 0; i < m; i++)
            {
                int k = start + i;
                if (k >= s.Length) { if (!wrap) break; k %= s.Length; }
                double t = (double)i / SampleRate;
                double a = i < att ? 0.5 - 0.5 * Math.Cos(Math.PI * i / att) : Math.Exp(-(t - attack) / tau);
                s[k] += (float)(amp * a * Math.Sin(w * i));
            }
        }

        /// <summary>Тон со скольжением f0→f1, атака, спад exp(−t/decay), доли 2-й и 3-й гармоник; последние 3 мс — в ноль.</summary>
        private static void AddTone(float[] s, double start, double duration, double f0, double f1, double amp,
            double attack, double decay, double h2 = 0, double h3 = 0)
        {
            int from = Samples(start);
            int count = Math.Min(Samples(duration), s.Length - from);
            if (count <= 0) return;
            int fade = Math.Min(Samples(0.003), count);
            double ratio = f1 / f0, phase = 0;
            for (int k = 0; k < count; k++)
            {
                double t = (double)k / SampleRate;
                phase += TwoPi * f0 * Math.Pow(ratio, t / duration) / SampleRate;
                double v = Math.Sin(phase) + h2 * Math.Sin(2 * phase) + h3 * Math.Sin(3 * phase);
                double env = (attack > 0 ? (t < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * t / attack) : 1.0) : 1.0) * Math.Exp(-t / decay);
                if (k >= count - fade) env *= (double)(count - k) / fade;
                s[from + k] += (float)(amp * v * env);
            }
        }

        private static double PowerLawRadius(double u, double rMin, double rMax)
        {
            const double a = -1.5; // p(r) ∝ r^−2,5
            return Math.Pow(Math.Pow(rMin, a) + u * (Math.Pow(rMax, a) - Math.Pow(rMin, a)), 1 / a);
        }

        // ---------- Комнаты ----------

        private enum RoomKind { Small, Far }

        /// <summary>
        /// Маленькая комната (близко, «в ладонях»): ранние отражения 5,1/8,7/11,3/15,9/21,4 мс, 4 гребёнки 263/293/331/367 сэмплов
        /// (12–17 мс) с обратной связью 0,7 (RT60 ≈ 0,27 с), затухание 0,35, два фазовращателя 142/107 (g 0,5); мокрое НЧ 3 кГц, ВЧ 150 Гц.
        /// Дальний берег: предзадержка 30 мс, гребёнки 557/617/683/743 (RT60 ≈ 0,8 с), затухание 0,6, фазовращатели 225/341; мокрое НЧ 1,4 кГц.
        /// Хвост добавляется в конец клипа (0,35 / 1,1 с) и плавно уходит в ноль.
        /// </summary>
        private static float[] Room(float[] dry, RoomKind kind, double wetDb, double tailSeconds = 0)
        {
            int tail = Samples(tailSeconds > 0 ? tailSeconds : kind == RoomKind.Small ? 0.35 : 1.1);
            var x = new float[dry.Length + tail];
            Array.Copy(dry, x, dry.Length);
            var wet = Reverb(x, kind, false);
            float g = (float)Math.Pow(10, wetDb / 20);
            for (int i = 0; i < x.Length; i++) x[i] += g * wet[i];
            FadeEdges(x, 0, tailSeconds > 0 ? tailSeconds * 0.5 : kind == RoomKind.Small ? 0.06 : 0.2);
            return x;
        }

        /// <summary>Та же комната для петли: прогон по кругу (две длины, берётся вторая) — хвост конца звучит в начале.</summary>
        private static float[] RoomCircular(float[] loop, RoomKind kind, double wetDb)
        {
            int n = loop.Length;
            var two = new float[2 * n];
            Array.Copy(loop, two, n);
            Array.Copy(loop, 0, two, n, n);
            var wet = Reverb(two, kind, true);
            float g = (float)Math.Pow(10, wetDb / 20);
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = loop[i] + g * wet[n + i];
            return o;
        }

        private static float[] Reverb(float[] x, RoomKind kind, bool _)
        {
            bool small = kind == RoomKind.Small;
            int[] combs = small ? new[] { 263, 293, 331, 367 } : new[] { 557, 617, 683, 743 };
            double fb = small ? 0.7 : 0.78, damp = small ? 0.35 : 0.6;
            int[] aps = small ? new[] { 142, 107 } : new[] { 225, 341 };
            int pre = small ? 0 : Samples(0.03);
            double[] erMs = { 5.1, 8.7, 11.3, 15.9, 21.4 };
            double[] erG = { 0.6, 0.45, 0.4, 0.3, 0.2 };
            int n = x.Length;
            var input = new double[n];
            for (int i = 0; i < n; i++) input[i] = i - pre >= 0 ? x[i - pre] : 0;

            var late = new double[n];
            foreach (int d in combs)
            {
                var buf = new double[d];
                int p = 0;
                double lp = 0;
                for (int i = 0; i < n; i++)
                {
                    double y = buf[p];
                    lp = y * (1 - damp) + lp * damp;
                    buf[p] = input[i] + fb * lp;
                    p = (p + 1) % d;
                    late[i] += y * 0.25;
                }
            }
            foreach (int d in aps)
            {
                var buf = new double[d];
                int p = 0;
                for (int i = 0; i < n; i++)
                {
                    double b = buf[p];
                    double y = -0.5 * late[i] + b;
                    buf[p] = late[i] + 0.5 * y;
                    p = (p + 1) % d;
                    late[i] = y;
                }
            }
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                double er = 0;
                if (small)
                    for (int k = 0; k < erMs.Length; k++)
                    {
                        int j = i - Samples(erMs[k] / 1000.0);
                        if (j >= 0) er += erG[k] * x[j];
                    }
                o[i] = (float)(0.5 * er + late[i]);
            }
            LowPass(o, small ? 3000 : 1400, 0.6);
            HighPass(o, 150, 0.7);
            return o;
        }

        // ---------- Фильтры ----------

        /// <summary>Фильтр состояний TPT (Задавалин): НЧ/полоса/ВЧ, частота и добротность на ходу.</summary>
        private struct Svf
        {
            private double _ic1, _ic2, _g, _k;

            public void Set(double fc, double q)
            {
                _g = Math.Tan(Math.PI * Math.Min(fc, SampleRate * 0.45) / SampleRate);
                _k = 1.0 / q;
            }

            public void Tick(double x, out double lp, out double bp, out double hp)
            {
                double a1 = 1.0 / (1 + _g * (_g + _k));
                double v3 = x - _ic2;
                double v1 = a1 * _ic1 + _g * a1 * v3;
                double v2 = _ic2 + _g * v1;
                _ic1 = 2 * v1 - _ic1;
                _ic2 = 2 * v2 - _ic2;
                lp = v2; bp = v1; hp = x - _k * v1 - v2;
            }
        }

        /// <summary>Двухполюсный резонатор: импульсный отклик ≈ sin(2πft)·e^(−t/τ) с амплитудой 1.</summary>
        private struct Resonator
        {
            private readonly double _b1, _b2, _g;
            private double _y1, _y2;

            public Resonator(double f, double tau)
            {
                double rad = Math.Exp(-1.0 / (tau * SampleRate)), w = TwoPi * f / SampleRate;
                _b1 = 2 * rad * Math.Cos(w);
                _b2 = -rad * rad;
                _g = Math.Sin(w);
                _y1 = _y2 = 0;
            }

            public double Tick(double x)
            {
                double y = _g * x + _b1 * _y1 + _b2 * _y2;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }

        private static void LowPass(float[] s, double fc, double q)
        {
            var f = new Svf();
            f.Set(fc, q);
            for (int i = 0; i < s.Length; i++) { f.Tick(s[i], out double lp, out _, out _); s[i] = (float)lp; }
        }

        private static void HighPass(float[] s, double fc, double q)
        {
            var f = new Svf();
            f.Set(fc, q);
            for (int i = 0; i < s.Length; i++) { f.Tick(s[i], out _, out _, out double hp); s[i] = (float)hp; }
        }

        /// <summary>Обработка петли по кругу: две длины подряд, берётся вторая (состояние фильтров «прогрето» её же концом).</summary>
        private static void Circular(float[] s, Action<float[]> process)
        {
            int n = s.Length;
            var two = new float[2 * n];
            Array.Copy(s, two, n);
            Array.Copy(s, 0, two, n, n);
            process(two);
            Array.Copy(two, n, s, 0, n);
        }

        private static float[] BandCircular(float[] noise, double fc, double q)
        {
            var f = new Svf();
            f.Set(fc, q);
            var o = new float[noise.Length];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < noise.Length; i++)
                {
                    f.Tick(noise[i], out _, out double bp, out _);
                    if (pass == 1) o[i] = (float)bp;
                }
            return o;
        }

        private static float[] LowCircular(float[] noise, double fc, double q)
        {
            var f = new Svf();
            f.Set(fc, q);
            var o = new float[noise.Length];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < noise.Length; i++)
                {
                    f.Tick(noise[i], out double lp, out _, out _);
                    if (pass == 1) o[i] = (float)lp;
                }
            return o;
        }

        // ---------- Уровни и края ----------

        private static int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);

        private static float[] WhiteNoise(int n, ref Rng r)
        {
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)r.S();
            return s;
        }

        private static double RmsOf(float[] s)
        {
            double sum = 0;
            for (int i = 0; i < s.Length; i++) sum += (double)s[i] * s[i];
            return Math.Max(1e-12, Math.Sqrt(sum / Math.Max(1, s.Length)));
        }

        private static void Normalize(float[] s, float peak)
        {
            float max = 0f;
            for (int i = 0; i < s.Length; i++) max = Math.Max(max, Math.Abs(s[i]));
            if (max <= 0f) return;
            float k = peak / max;
            for (int i = 0; i < s.Length; i++) s[i] *= k;
        }

        /// <summary>
        /// Мягкое «плечо» для шумовых петель: y = c·tanh(x/c), c = k × RMS. Срезает редкие пики шума (пик-фактор 15–17 → ~11 дБ),
        /// добавляя чуть тёплых нечётных гармоник; поточечно — шов петли не трогает.
        /// </summary>
        private static void SoftKnee(float[] s, double k)
        {
            double c = k * RmsOf(s);
            for (int i = 0; i < s.Length; i++) s[i] = (float)(c * Math.Tanh(s[i] / c));
        }

        /// <summary>RMS петель (громкость задаёт CozyCues); пик не выше 0,9 — если не влезает, вся петля тише.</summary>
        public const double LoopRms = 0.15;

        private static void NormalizeLoop(float[] s, double rms)
        {
            NormalizeRms(s, rms);
            float max = 0f;
            for (int i = 0; i < s.Length; i++) max = Math.Max(max, Math.Abs(s[i]));
            if (max > 0.9f)
                for (int i = 0; i < s.Length; i++) s[i] *= 0.9f / max;
        }

        private static void NormalizeRms(float[] s, double rms)
        {
            double k = rms / RmsOf(s);
            for (int i = 0; i < s.Length; i++) s[i] = (float)(s[i] * k);
        }

        /// <summary>Края приподнятым косинусом: вход inSeconds (0 — не трогать), выход outSeconds.</summary>
        private static void FadeEdges(float[] s, double inSeconds, double outSeconds)
        {
            int a = Samples(inSeconds), b = Math.Max(1, Samples(outSeconds));
            for (int i = 0; i < a && i < s.Length; i++) s[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / a));
            for (int i = 0; i < b && i < s.Length; i++) s[s.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / b));
        }

        /// <summary>Разовый звук после комнаты: пик 0,9.</summary>
        private static float[] Finish(float[] s)
        {
            Normalize(s, 0.9f);
            return s;
        }

        // ---------- Случайность ----------

        /// <summary>Перемешивание 32 бит (lowbias32): номер события → независимое зерно.</summary>
        public static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x == 0 ? 1u : x;
        }

        /// <summary>xorshift32: U() — 0…1, S() — −1…1, J(f) — 1 ± f.</summary>
        internal struct Rng
        {
            private uint _s;

            public Rng(uint seed)
            {
                _s = seed == 0 ? 1u : seed;
                Next(); Next();
            }

            public uint Next()
            {
                _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
                return _s;
            }

            public double U() => (Next() >> 8) * (1.0 / 16777216.0);
            public double S() => U() * 2 - 1;
            public double J(double frac) => 1 + frac * S();
        }
    }
}
