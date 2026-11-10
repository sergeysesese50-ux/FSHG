using System;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Кирпичики процедурного звука озера (палитра «Натуральная»): шум, фильтры, затухающие моды, пузыри, «эхо».
    /// Моно, 22050 Гц, double внутри и float на выходе. Детерминированно: одно и то же зерно — те же сэмплы.
    /// Только для генерации банка (FightSynth, LakeSynth) — на звуковом потоке не используется.
    /// </summary>
    internal static class SynthKit
    {
        public const int SampleRate = 22050;
        public const double TwoPi = 2.0 * Math.PI;

        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x == 0 ? 1u : x;
        }

        public static int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);

        /// <summary>Дуга 0…1…0 на x ∈ [0, 1] с вершиной в peakAt: sin(π·x^p)^1,5.</summary>
        public static double Arch(double x, double peakAt)
        {
            if (x <= 0 || x >= 1)
                return 0;
            double p = Math.Log(0.5) / Math.Log(peakAt);
            return Math.Pow(Math.Sin(Math.PI * Math.Pow(x, p)), 1.5);
        }

        public struct Mode
        {
            public double F, Tau, A;
        }

        public static Mode M(double f, double tau, double a) => new Mode { F = f, Tau = tau, A = a };

        /// <summary>
        /// Сумма затухающих синусоид a·e^(−t/τ)·sin(2πft), свёрнутая с приподнятым косинусом длиной exciterMs
        /// (чем длиннее — тем мягче «колотушка»), с лёгкой шумовой фактурой возбуждения.
        /// </summary>
        public static float[] Modal(int n, int start, Mode[] modes, double exciterMs, uint seed)
        {
            var raw = new double[n];
            foreach (var m in modes)
            {
                if (m.F >= 0.45 * SampleRate)
                    continue;
                double w = TwoPi * m.F / SampleRate, k = Math.Exp(-1.0 / (m.Tau * SampleRate));
                // Синус рекуррентно (поворот фазора): без Math.Sin на каждый сэмпл.
                double c = Math.Cos(w), s0 = 0, s1 = Math.Sin(w), env = 1;
                for (int i = start; i < n; i++)
                {
                    raw[i] += m.A * env * s0;
                    double s2 = 2 * c * s1 - s0;
                    s0 = s1;
                    s1 = s2;
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
        public static float[] Resonate(float[] exc, double[] freqs, double[] amps, double qMin, double qMax, double freqJitter, ref Rng r)
        {
            int n = exc.Length;
            var s = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                var f = new Svf(freqs[k] * r.J(freqJitter), r.Range(qMin, qMax));
                for (int i = 0; i < n; i++)
                    s[i] += (float)(amps[k] * f.Band(exc[i]) / f.Q);
            }
            return s;
        }

        /// <summary>Импульс — приподнятый косинус длиной seconds (не короче 3 сэмплов).</summary>
        public static void AddPulse(float[] s, int at, double seconds, double amp)
        {
            int len = Math.Max(3, (int)Math.Round(seconds * SampleRate));
            for (int i = 0; i < len && at + i < s.Length; i++)
                s[at + i] += (float)(amp * (0.5 - 0.5 * Math.Cos(TwoPi * (i + 0.5) / len)));
        }

        /// <summary>Шум через полосовой фильтр с меняющейся частотой и огибающей (свисты): длина seconds от start.</summary>
        public static float[] Sweep(int n, int start, double seconds, Func<double, double> hz, double q, Func<double, double> env, ref Rng r)
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
        public static float[] Swirl(int n, Func<double, double> hz, double q, Func<double, double> env, ref Rng r)
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
        public static void AddBubble(float[] s, int start, double rMm, double gain, bool wrap = false)
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
        /// Хранится только сетка через 32 сэмпла (n/32 чисел, а не n): значение в сэмпле — SmoothCurve.At, линейная
        /// интерполяция между узлами (кривая медленнее 1 Гц — так в 32 раза быстрее и в 32 раза меньше памяти).
        /// </summary>
        public static SmoothCurve SmoothRandom(int n, int minCycles, int maxCycles, ref Rng r)
        {
            const int grid = SmoothCurve.Grid;
            int count = Math.Max(1, maxCycles - minCycles + 1);
            var amp = new double[count];
            var phase = new double[count];
            for (int k = 0; k < count; k++)
            {
                amp[k] = Math.Sqrt(-2 * Math.Log(1 - 0.999 * r.U())) / Math.Sqrt(Math.Max(1, minCycles + k));
                phase[k] = r.U() * TwoPi;
            }
            int points = (n + grid - 1) / grid + 1;
            var g = new double[points];
            double max = 1e-9;
            for (int p = 0; p < points; p++)
            {
                double x = Math.Min(1.0, (double)p * grid / n), v = 0;
                for (int k = 0; k < count; k++)
                    v += amp[k] * Math.Sin(TwoPi * (minCycles + k) * x + phase[k]);
                g[p] = v;
                max = Math.Max(max, Math.Abs(v));
            }
            return new SmoothCurve(g, n, max);
        }

        /// <summary>Кривая SmoothRandom: узлы через Grid сэмплов, At(i) — значение в сэмпле i ∈ [0, n).</summary>
        public readonly struct SmoothCurve
        {
            public const int Grid = 32;
            private readonly double[] _g;
            private readonly int _n;
            private readonly double _max;

            public SmoothCurve(double[] grid, int n, double max)
            {
                _g = grid;
                _n = n;
                _max = max;
            }

            public double At(int i)
            {
                int p = i / Grid;
                double a = _g[p], b = p + 1 < _g.Length ? _g[p + 1] : _g[0];
                double end = Math.Min(_n, (p + 1) * Grid);
                double frac = (i - p * Grid) / Math.Max(1.0, end - p * Grid);
                return (a + (b - a) * frac) / _max;
            }
        }

        /// <summary>Радиус по степенному закону p(r) ∝ r^−2,5 на [rMin, rMax].</summary>
        public static double PowerLawRadius(ref Rng r, double rMin, double rMax)
        {
            const double a = -1.5;
            double u = r.U();
            return Math.Pow(Math.Pow(rMin, a) + u * (Math.Pow(rMax, a) - Math.Pow(rMin, a)), 1 / a);
        }

        /// <summary>Короткий хвост «эха» (4 гребёнки 558/594/638/678 сэмплов, обратная связь 0,84, затухание 0,2, два всепропускающих 278/220).</summary>
        public static void AddRoom(float[] s, double wetDb)
        {
            var wet = Room(s, false);
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

        public static void Mix(float[] dst, float[] src, int at, double gain)
        {
            for (int i = 0; i < src.Length && at + i < dst.Length; i++)
                dst[at + i] += (float)(gain * src[i]);
        }

        public static double Peak(float[] s)
        {
            double m = 0;
            foreach (var v in s)
                m = Math.Max(m, Math.Abs(v));
            return m;
        }

        public static void Normalize(float[] s, float peak)
        {
            double m = Peak(s);
            if (m <= 0)
                return;
            float k = (float)(peak / m);
            for (int i = 0; i < s.Length; i++)
                s[i] *= k;
        }

        public static void Scale(float[] s, double gain)
        {
            for (int i = 0; i < s.Length; i++)
                s[i] = (float)(s[i] * gain);
        }

        /// <summary>Вход приподнятым косинусом за fadeIn, выход — за fadeOut (в ноль).</summary>
        public static void Fades(float[] s, double fadeIn, double fadeOut)
        {
            int a = Math.Max(1, Samples(fadeIn)), b = Math.Max(1, Samples(fadeOut));
            for (int i = 0; i < a && i < s.Length; i++)
                s[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / a));
            for (int i = 0; i < b && i < s.Length; i++)
                s[s.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / b));
        }

        public static void LowPass2(float[] s, double hz)
        {
            var f = new Svf(hz, 0.707);
            for (int i = 0; i < s.Length; i++)
                s[i] = (float)f.Low(s[i]);
        }

        public static void HighPass2(float[] s, double hz)
        {
            var f = new Svf(hz, 0.707);
            for (int i = 0; i < s.Length; i++)
                s[i] = (float)f.High(s[i]);
        }

        public enum Band { Low, Band, BandNorm, High }

        /// <summary>Фильтр по кругу: первый проход прогревает состояние концом петли, второй пишет — шва нет.</summary>
        public static void FilterCircular(float[] s, double hz, double q, Band band)
        {
            var f = new Svf(hz, q);
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
        public static void PinkCircular(float[] s)
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
        public struct Rng
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
        public struct Svf
        {
            private double _g, _k, _a1, _ic1, _ic2;
            public double Q;

            public Svf(double hz, double q)
            {
                _g = 0; _k = 0; _a1 = 0; _ic1 = 0; _ic2 = 0; Q = q;
                Set(hz, q);
            }

            public void Set(double hz, double q)
            {
                Q = q;
                _g = Math.Tan(Math.PI * Math.Min(hz, 0.45 * SampleRate) / SampleRate);
                _k = 1.0 / q;
                _a1 = 1.0 / (1 + _g * (_g + _k));
            }

            private void Tick(double x, out double lp, out double bp, out double hp)
            {
                double v3 = x - _ic2;
                double v1 = _a1 * _ic1 + _g * _a1 * v3;
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
