// Прежний звук вываживания (до «Натуральной» палитры): копия client/Assets/_Game/Scripts/Unity/Lake/FightSynth.cs
// на момент замены. В игре его больше нет; здесь — только для AudioDiag и LoopDump (сравнение «было/стало»).
using System;

namespace Fishing.Unity
{
    /// <summary>
    /// Процедурные звуки рыбалки (docs/specs/fight-v2.md, §6): моно, 22050 Гц, без ассетов и без движка.
    /// Чистые функции: один и тот же вызов даёт те же сэмплы (шум — свой генератор с постоянным зерном).
    ///
    /// Петли (жужжание, рычание, щелчки фрикциона, пение лески) замкнуты без шва: на длину петли приходится
    /// целое число периодов, а фильтры шума прогоняются по кругу (сначала прогрев на той же петле).
    /// Разовые звуки начинаются и кончаются в нуле, пик — не больше 0,95.
    /// Телефонный динамик почти не играет ниже 150 Гц, поэтому у низких «тумков» есть 2-я и 3-я гармоники.
    /// </summary>
    internal static class FightSynth
    {
        public const int SampleRate = 22050;

        private const double TwoPi = 2.0 * Math.PI;

        // ---------- Петли ----------

        /// <summary>Жужжание катушки: петля 0,5 с — импульсы 220 Гц и 15% шума, «шестерня» 4 Гц.</summary>
        public static float[] Whir()
        {
            int n = Samples(0.5);
            var pulses = new float[n];
            for (int i = 0; i < n; i++)
                pulses[i] = Frac(i * 220.0 / SampleRate) < 0.2 ? 1f : 0f;
            RemoveDc(pulses);
            LowPass(pulses, 3500, true);
            Normalize(pulses, 1f);

            var noise = new Noise(0x1001u);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float gear = 0.8f + 0.2f * Tri(t * 4.0);
                s[i] = (0.85f * pulses[i] + 0.15f * noise.Next()) * gear;
            }
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Рычание катушки под нагрузкой: петля 1 с — шум через однополюсный фильтр 150 Гц, скрежет 11 Гц.</summary>
        public static float[] Growl()
        {
            int n = Samples(1.0);
            var s = WhiteNoise(n, 0x2002u);
            LowPass(s, 150, true);
            RemoveDc(s);
            Normalize(s, 1f);
            for (int i = 0; i < n; i++)
                s[i] *= 0.7f + 0.3f * Tri((double)i / SampleRate * 11.0);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Трещотка фрикциона: петля 0,5 с — 20 щелчков (40 в секунду при высоте 1).</summary>
        public static float[] ClickLoop()
        {
            int n = Samples(0.5);
            var s = new float[n];
            var jitter = new Noise(0x3003u);
            for (int k = 0; k < 20; k++)
            {
                int start = (int)Math.Round(k * SampleRate / 40.0);
                var click = DragClick(0x3100u + (uint)k);
                float amp = 0.7f + 0.3f * Math.Abs(jitter.Next());
                for (int i = 0; i < click.Length && start + i < n; i++)
                    s[start + i] += click[i] * amp;
            }
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>Пение лески: петля 0,5 с — синус 600 Гц и 0,3 второй гармоники, лёгкое дрожание 6 Гц.</summary>
        public static float[] LineSing()
        {
            int n = Samples(0.5);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double a = TwoPi * 600.0 * t;
                double tremolo = 1.0 + 0.08 * Math.Sin(TwoPi * 6.0 * t);
                s[i] = (float)((Math.Sin(a) + 0.3 * Math.Sin(2.0 * a)) * tremolo);
            }
            Normalize(s, 0.7f);
            return s;
        }

        // ---------- Короткие звуки, которые повторяются ----------

        /// <summary>Щелчок фрикциона: 6 мс шума, спад 1 мс, срез ниже 2 кГц. Разные зёрна — разные щелчки.</summary>
        public static float[] DragClick(uint seed)
        {
            int n = Samples(0.006);
            var s = new float[n];
            var noise = new Noise(seed);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                s[i] = noise.Next() * (float)Math.Exp(-t / 0.001);
            }
            s[0] += 1f;
            HighPass(s, 2000);
            FadeOut(s, 0.001);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Скрип удилища: 0,3 с пилы 70 Гц, сила скачет от периода к периоду (то трётся, то срывается).</summary>
        public static float[] RodCreak(uint seed)
        {
            int n = Samples(0.3);
            const int period = SampleRate / 70; // 315 сэмплов — ровно 70 Гц
            var s = new float[n];
            var noise = new Noise(seed);
            float amp = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i % period == 0)
                {
                    float u = Math.Abs(noise.Next());
                    amp = u < 0.2f ? 0.1f : 0.4f + 0.6f * Math.Abs(noise.Next());
                }
                double t = (double)i / SampleRate;
                float saw = 2f * (i % period) / period - 1f;
                float env = Attack(t, 0.02) * (float)Math.Min(1.0, (0.3 - t) / 0.08);
                s[i] = saw * amp * env;
            }
            LowPass(s, 2500, false);
            FadeOut(s, 0.005);
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>
        /// Всплеск: 0,35 с шума, спад 80 мс, фильтр 1,5 кГц и несколько капель. Тяжёлый — 0,5 с, глуше
        /// (800 Гц, спад 120 мс) и с низким «бульком».
        /// </summary>
        public static float[] Splash(bool heavy)
        {
            double length = heavy ? 0.5 : 0.35;
            double decay = heavy ? 0.12 : 0.08;
            double cutoff = heavy ? 800 : 1500;
            int n = Samples(length);
            var s = new float[n];
            var noise = new Noise(heavy ? 0x6006u : 0x5005u);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                s[i] = noise.Next() * Attack(t, 0.004) * (float)Math.Exp(-t / decay);
            }
            LowPass(s, cutoff, false);
            LowPass(s, cutoff, false);
            Normalize(s, 1f);

            var drops = new Noise(heavy ? 0x6106u : 0x5105u);
            int count = heavy ? 7 : 5;
            for (int k = 0; k < count; k++)
            {
                double start = 0.03 + (length - 0.1) * Math.Abs(drops.Next());
                double f0 = 900 + 900 * Math.Abs(drops.Next());
                double amp = 0.12 + 0.12 * Math.Abs(drops.Next());
                AddTone(s, start, 0.025, f0, f0 * 1.6, amp, 0.001, 0.008);
            }
            if (heavy)
                AddTone(s, 0, 0.25, 85, 55, 0.8, 0.003, 0.06, 0.5, 0.25);

            FadeOut(s, 0.01);
            Normalize(s, 0.9f);
            return s;
        }

        // ---------- Разовые сигналы ----------

        /// <summary>Телеграф ухода, ухода вглубь и броска: «вихрь» — шум через полосовой фильтр 350→1100→500 Гц, 0,45 с.</summary>
        public static float[] TelegraphSwirl()
        {
            int n = Samples(0.45);
            var s = new float[n];
            var noise = new Noise(0x7007u);
            var filter = new BandPass();
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double x = (double)i / n;
                double fc = 350 + 750 * Math.Sin(Math.PI * x);
                float band = filter.Next(noise.Next(), fc, 1.0 / 3.0);
                double env = Math.Pow(Math.Sin(Math.PI * x), 1.5) * (0.7 + 0.3 * Math.Sin(TwoPi * 12.0 * t));
                s[i] = (float)(band * env);
            }
            FadeOut(s, 0.005);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Телеграф рывка: «тумк» 80 Гц, 60 мс.</summary>
        public static float[] JerkThunk()
        {
            int n = Samples(0.06);
            var s = new float[n];
            AddTone(s, 0, 0.06, 90, 75, 1.0, 0.002, 0.02, 0.5, 0.25);
            var noise = new Noise(0x8008u);
            int click = Samples(0.003);
            var hit = new float[click];
            for (int i = 0; i < click; i++)
                hit[i] = noise.Next() * 0.3f * (1f - (float)i / click) * Math.Min(1f, i / 22f);
            LowPass(hit, 2000, false);
            for (int i = 0; i < click; i++)
                s[i] += hit[i];
            FadeOut(s, 0.004);
            Normalize(s, 0.95f);
            return s;
        }

        /// <summary>Телеграф свечки: нарастающий «вжух» — полоса шума поднимается 250→2800 Гц за 0,5 с.</summary>
        public static float[] JumpWhoosh()
        {
            int n = Samples(0.5);
            var s = new float[n];
            var noise = new Noise(0x9009u);
            var filter = new BandPass();
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double fc = 250 * Math.Pow(2800.0 / 250.0, x);
                float band = filter.Next(noise.Next(), fc, 1.0 / 1.2);
                double env = x * x * Math.Min(1.0, (1.0 - x) / 0.08);
                s[i] = (float)(band * env);
            }
            FadeOut(s, 0.003);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Подсечка: глухой «тумк» 140→55 Гц и свист удилища в начале, 0,2 с.</summary>
        public static float[] HooksetThunk()
        {
            int n = Samples(0.2);
            var s = new float[n];
            AddTone(s, 0, 0.2, 140, 55, 1.0, 0.002, 0.05, 0.6, 0.3);
            var noise = new Noise(0xA00Au);
            int m = Samples(0.04);
            var swish = new float[m];
            for (int i = 0; i < m; i++)
                swish[i] = noise.Next() * 0.35f * (float)Math.Exp(-(double)i / SampleRate / 0.015);
            HighPass(swish, 3000);
            for (int i = 0; i < m; i++)
                s[i] += swish[i];
            FadeOut(s, 0.01);
            Normalize(s, 0.95f);
            return s;
        }

        /// <summary>Обрыв лески: щелчок и «дзынь» 2 кГц, гаснущий за 150 мс.</summary>
        public static float[] LineSnap()
        {
            int n = Samples(0.2);
            var s = new float[n];
            var noise = new Noise(0xB00Bu);
            int m = Samples(0.004);
            var click = new float[m];
            for (int i = 0; i < m; i++)
                click[i] = noise.Next() * (float)Math.Exp(-(double)i / SampleRate / 0.0008);
            click[0] += 1f;
            HighPass(click, 2000);
            for (int i = 0; i < m; i++)
                s[i] += click[i];
            AddTone(s, 0.001, 0.199, 2100, 1950, 0.7, 0.0005, 0.045);
            FadeOut(s, 0.005);
            Normalize(s, 0.95f);
            return s;
        }

        /// <summary>Поломка удилища: резкий треск, щепки (редеющие щелчки) и глухой удар, 0,4 с.</summary>
        public static float[] RodCrack()
        {
            int n = Samples(0.4);
            var s = new float[n];
            var noise = new Noise(0xC00Cu);
            for (int i = 0; i < Samples(0.03); i++)
                s[i] += noise.Next() * (float)Math.Exp(-(double)i / SampleRate / 0.004);

            var timing = new Noise(0xC10Cu);
            const int splinters = 14;
            for (int k = 0; k < splinters; k++)
            {
                double x = (double)k / splinters;
                double at = 0.01 + 0.3 * Math.Pow(x, 1.6) + 0.006 * timing.Next();
                int from = Samples(Math.Max(0.0, at));
                float amp = 0.1f + 0.6f * (1f - (float)x);
                for (int i = 0; i < Samples(0.012) && from + i < n; i++)
                    s[from + i] += noise.Next() * amp * (float)Math.Exp(-(double)i / SampleRate / 0.002);
            }
            LowPass(s, 3500, false);
            AddTone(s, 0, 0.2, 110, 70, 0.6, 0.002, 0.05, 0.5, 0.2);
            FadeOut(s, 0.01);
            Normalize(s, 0.95f);
            return s;
        }

        /// <summary>Сход: «плинь» 900 Гц (850→1000), 40 мс.</summary>
        public static float[] EscapePlip()
        {
            int n = Samples(0.04);
            var s = new float[n];
            AddTone(s, 0, 0.04, 850, 1000, 1.0, 0.002, 0.015);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Поимка: колокольчик — до-ми-соль второй октавы с шагом 90 мс, звон до 1,2 с.</summary>
        public static float[] LandedChime()
        {
            const double length = 1.2;
            int n = Samples(length);
            var s = new float[n];
            double[] notes = { 523.25, 659.25, 783.99 };
            for (int k = 0; k < notes.Length; k++)
            {
                double start = 0.09 * k;
                AddTone(s, start, length - start, notes[k], notes[k], 1.0, 0.003, 0.35, 0.3, 0.12);
                AddTone(s, start, 0.4, notes[k] * 2.76, notes[k] * 2.76, 0.06, 0.002, 0.12);
            }
            FadeOut(s, 0.03);
            Normalize(s, 0.8f);
            return s;
        }

        // ---------- Кирпичики ----------

        private static int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);

        private static double Frac(double x) => x - Math.Floor(x);

        /// <summary>Треугольник −1…1 с периодом 1 (в нуле — 1).</summary>
        private static float Tri(double x) => (float)(4.0 * Math.Abs(Frac(x) - 0.5) - 1.0);

        private static float Attack(double t, double seconds) => t >= seconds ? 1f : (float)(t / seconds);

        private static float[] WhiteNoise(int n, uint seed)
        {
            var noise = new Noise(seed);
            var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = noise.Next();
            return s;
        }

        /// <summary>Однополюсный фильтр нижних частот. circular — петля: состояние прогревается её же концом.</summary>
        private static void LowPass(float[] s, double hz, bool circular)
        {
            float a = (float)(1.0 - Math.Exp(-TwoPi * hz / SampleRate));
            float y = 0f;
            if (circular)
                for (int i = 0; i < s.Length; i++)
                    y += a * (s[i] - y);
            for (int i = 0; i < s.Length; i++)
            {
                y += a * (s[i] - y);
                s[i] = y;
            }
        }

        /// <summary>Однополюсный фильтр верхних частот.</summary>
        private static void HighPass(float[] s, double hz)
        {
            double rc = 1.0 / (TwoPi * hz);
            float a = (float)(rc / (rc + 1.0 / SampleRate));
            float y = 0f;
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float x = s[i];
                y = a * (y + x - previous);
                previous = x;
                s[i] = y;
            }
        }

        private static void RemoveDc(float[] s)
        {
            double sum = 0;
            for (int i = 0; i < s.Length; i++)
                sum += s[i];
            float mean = (float)(sum / s.Length);
            for (int i = 0; i < s.Length; i++)
                s[i] -= mean;
        }

        private static void Normalize(float[] s, float peak)
        {
            float max = 0f;
            for (int i = 0; i < s.Length; i++)
                max = Math.Max(max, Math.Abs(s[i]));
            if (max <= 0f)
                return;
            float k = peak / max;
            for (int i = 0; i < s.Length; i++)
                s[i] *= k;
        }

        /// <summary>Линейно в ноль за последние seconds.</summary>
        private static void FadeOut(float[] s, double seconds)
        {
            int m = Math.Min(s.Length, Math.Max(1, Samples(seconds)));
            for (int i = 0; i < m; i++)
                s[s.Length - 1 - i] *= (float)i / m;
        }

        /// <summary>
        /// Добавить тон: частота плавно (по экспоненте) идёт f0→f1 за duration, атака attack, спад exp(−t/decay),
        /// h2 и h3 — доли 2-й и 3-й гармоник. Последние 3 мс уводятся в ноль.
        /// </summary>
        private static void AddTone(float[] s, double start, double duration, double f0, double f1, double amp,
            double attack, double decay, double h2 = 0, double h3 = 0)
        {
            int from = Samples(start);
            int count = Math.Min(Samples(duration), s.Length - from);
            if (count <= 0)
                return;
            int fade = Math.Min(Samples(0.003), count);
            double ratio = f1 / f0;
            double phase = 0;
            for (int k = 0; k < count; k++)
            {
                double t = (double)k / SampleRate;
                phase += TwoPi * f0 * Math.Pow(ratio, t / duration) / SampleRate;
                double v = Math.Sin(phase) + h2 * Math.Sin(2 * phase) + h3 * Math.Sin(3 * phase);
                double env = (attack > 0 ? Math.Min(1.0, t / attack) : 1.0) * Math.Exp(-t / decay);
                if (k >= count - fade)
                    env *= (double)(count - k) / fade;
                s[from + k] += (float)(amp * v * env);
            }
        }

        /// <summary>Шум −1…1: xorshift32 с постоянным зерном (локальная переменная, не static).</summary>
        private struct Noise
        {
            private uint _state;

            public Noise(uint seed)
            {
                _state = seed != 0 ? seed : 1u;
            }

            public float Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (_state >> 8) * (1f / 8388608f) - 1f;
            }
        }

        /// <summary>Полосовой фильтр Чемберлина с меняющейся частотой (q = 1/добротность).</summary>
        private struct BandPass
        {
            private float _low;
            private float _band;

            public float Next(float x, double hz, double q)
            {
                float f = (float)(2.0 * Math.Sin(Math.PI * hz / SampleRate));
                _low += f * _band;
                float high = x - _low - (float)q * _band;
                _band += f * high;
                return _band;
            }
        }
    }
}
