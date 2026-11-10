using System;
using static Fishing.Core.Audio.SynthKit;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Фон турбазы — одна бесшовная петля (моно, SampleRate), намеренно непохожая на фон озера: там вода и редкие
    /// лесные птицы, здесь жилое место. Ни одного клипа озера не используется.
    /// - птицы: редкая мягкая перекличка, 2–4 «чив» на 2,2–3,0 кГц раз в 6–12 с;
    /// - куры: «ко-ко-ко» раз в 6–10 с, иногда с «кудах» в конце;
    /// - мастерская: серии из 3–6 ударов молотка раз в 7–11 с и дважды за петлю — пила «вжик-вжик»;
    /// - магазин: дверной колокольчик — сразу при входе на турбазу (0,5 с от начала петли) и раз в 12–18 с;
    /// - шаги по гравию: три прохода по 6–9 шагов, подходят и уходят.
    /// Голосов нет намеренно: синтезированная речь звучит неприятно.
    /// Ветра и шелеста листвы нет (убраны по решению автора игры): между звуками — тишина.
    /// События заворачиваются через конец петли.
    /// </summary>
    public static class CampSynth
    {
        public const double DefaultSeconds = 48;

        /// <summary>Частота петли, Гц — для того, кто её проигрывает (SynthKit снаружи сборки не виден).</summary>
        public const int SampleRate = SynthKit.SampleRate;

        /// <summary>Общий подъём слоёв до уровня фона озера.</summary>
        private const double MasterGain = 2.0;

        private const double TweetGain = 0.055;
        private const double CluckGain = 0.10;
        private const double KnockGain = 0.16;
        private const double SawGain = 0.075;
        private const double BellGain = 0.07;
        private const double StepGain = 0.055;

        public static float[] CampBed(double seconds = DefaultSeconds, uint seed = 0xCA4B0002u)
        {
            int n = Samples(seconds);
            var r = new Rng(seed);

            // Все события — с общим лёгким эхом двора. Шума ветра и листвы нет: между звуками — тишина.
            var events = new float[n];
            AddSparrows(events, seconds, ref r);
            AddChickens(events, seconds, ref r);
            AddWorkshop(events, seconds, ref r);
            AddDoorBell(events, seconds, ref r);
            AddSteps(events, seconds, ref r);
            AddRoom(events, -16);

            Scale(events, MasterGain);
            return events;
        }

        /// <summary>
        /// Птицы у домов: редкая короткая перекличка — 2–4 мягких «чив» раз в 6–12 с. Тихо и невысоко (2,2–3,0 кГц):
        /// высокие частые трели резали слух.
        /// </summary>
        private static void AddSparrows(float[] dst, double seconds, ref Rng r)
        {
            double t = r.Range(3.0, 5.0);
            while (t < seconds - 1)
            {
                int count = 2 + (int)(r.U() * 3);
                double gain = TweetGain * r.Range(0.5, 1.0);
                double pitch = r.Range(2200, 3000);
                double at = t;
                for (int k = 0; k < count; k++)
                {
                    double f0 = pitch * r.J(0.05);
                    AddTweet(dst, Samples(at), f0, f0 * r.Range(0.85, 1.08), r.Range(0.05, 0.08), gain * r.Range(0.7, 1.0));
                    at += r.Range(0.18, 0.30);
                }

                t += r.Range(6.0, 12.0);
            }
        }

        /// <summary>Куры: 3–6 «ко» подряд, иногда с протяжным «кудах» в конце.</summary>
        private static void AddChickens(float[] dst, double seconds, ref Rng r)
        {
            double t = r.Range(3, 5);
            while (t < seconds - 2)
            {
                int count = 3 + (int)(r.U() * 4);
                double gain = CluckGain * r.Range(0.6, 1.0);
                double pitch = r.Range(390, 460);
                double at = t;
                for (int k = 0; k < count; k++)
                {
                    AddCluck(dst, Samples(at), pitch * r.J(0.05), pitch * 0.8, r.Range(0.06, 0.08), gain);
                    at += r.Range(0.22, 0.30);
                }

                if (r.U() < 0.35)
                    AddCluck(dst, Samples(at + 0.05), pitch * 1.1, pitch * 1.65, 0.20, gain * 1.1);
                t += r.Range(6, 10);
            }
        }

        private static void AddWorkshop(float[] dst, double seconds, ref Rng r)
        {
            double t = r.Range(2.5, 4);
            while (t < seconds - 3)
            {
                int count = 3 + (int)(r.U() * 4);
                double step = r.Range(0.32, 0.40);
                double gain = KnockGain * r.Range(0.6, 1.0);
                for (int k = 0; k < count; k++)
                {
                    double k1 = r.J(0.03);
                    var knock = Modal(Samples(0.45), 0, new[]
                    {
                        M(230 * k1, 0.035, 1.0), M(410 * k1, 0.025, 0.7), M(890 * k1, 0.012, 0.35), M(1700 * k1, 0.006, 0.2),
                    }, 1.2, r.NextSeed());
                    LowPass2(knock, 2500);
                    Fades(knock, 0.0005, 0.05);
                    Normalize(knock, 0.9f);
                    MixWrap(dst, knock, Samples(t + k * step * r.J(0.04)), gain * (k == 0 ? 0.8 : r.Range(0.85, 1.0)));
                }

                t += r.Range(7, 11);
            }

            // Пила: ход вперёд громче и выше, обратно — тише и глуше.
            for (int pass = 0; pass < 2; pass++)
            {
                double start = seconds * (0.30 + 0.42 * pass) + r.Range(-1.5, 1.5);
                int strokes = 6 + (int)(r.U() * 4);
                double at = start;
                for (int k = 0; k < strokes; k++)
                {
                    AddStroke(dst, Samples(at), 0.28, r.Range(1600, 1900), SawGain, ref r);
                    AddStroke(dst, Samples(at + 0.30), 0.20, r.Range(1200, 1400), SawGain * 0.5, ref r);
                    at += r.Range(0.54, 0.60);
                }
            }
        }

        private static void AddDoorBell(float[] dst, double seconds, ref Rng r)
        {
            // Первый — у самого начала петли: игрок только пришёл на турбазу и слышит «динь-динь».
            double t = 0.5;
            while (t < seconds - 2)
            {
                double k1 = r.J(0.01);
                var bell = Modal(Samples(1.2), 0, new[]
                {
                    M(2093 * k1, 0.30, 1.0), M(3140 * k1, 0.20, 0.5), M(4190 * k1, 0.11, 0.3), M(5600 * k1, 0.06, 0.15),
                }, 0.6, r.NextSeed());
                Fades(bell, 0.0005, 0.08);
                Normalize(bell, 0.9f);
                double gain = BellGain * r.Range(0.75, 1.0);
                MixWrap(dst, bell, Samples(t), gain);
                MixWrap(dst, bell, Samples(t + 0.19), gain * 0.6);
                t += r.Range(12, 18);
            }
        }

        private static void AddSteps(float[] dst, double seconds, ref Rng r)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                double start = seconds * (0.12 + 0.31 * pass) + r.Range(-1.5, 1.5);
                int count = 6 + (int)(r.U() * 4);
                double interval = r.Range(0.50, 0.56);
                for (int k = 0; k < count; k++)
                {
                    int m = Samples(0.14);
                    var step = new float[m];
                    var band = new Svf(r.Range(1900, 3000), 1.2);
                    for (int i = 0; i < m; i++)
                    {
                        double time = (double)i / SynthKit.SampleRate;
                        double env = Math.Exp(-time / 0.018) + (time > 0.045 ? 0.6 * Math.Exp(-(time - 0.045) / 0.02) : 0);
                        step[i] = (float)(band.Band(r.S()) * env);
                    }

                    Fades(step, 0.001, 0.02);
                    Normalize(step, 0.9f);
                    // Проход: шаги подходят и уходят.
                    double near = Math.Sin(Math.PI * (k + 0.5) / count);
                    MixWrap(dst, step, Samples(start + k * interval * r.J(0.05)), StepGain * (0.35 + 0.65 * near));
                }
            }
        }

        /// <summary>Короткий свист с глиссандо f0→f1: одно «чив» воробья.</summary>
        private static void AddTweet(float[] dst, int at, double f0, double f1, double len, double gain)
        {
            int m = Samples(len);
            double phase = 0;
            for (int i = 0; i < m; i++)
            {
                double x = (double)i / m;
                phase += TwoPi * f0 * Math.Pow(f1 / f0, x) / SynthKit.SampleRate;
                // Плавные края и чистый тон без призвуков — мягче на слух.
                double env = Math.Sin(Math.PI * x);
                env *= env;
                Add(dst, at + i, gain * env * Math.Sin(phase));
            }
        }

        /// <summary>Одно «ко»: гармонический тон с падением (или подъёмом) высоты и формантой около 1,1 кГц.</summary>
        private static void AddCluck(float[] dst, int at, double f0, double f1, double len, double gain)
        {
            int m = Samples(len);
            double phase = 0;
            for (int i = 0; i < m; i++)
            {
                double x = (double)i / m, time = (double)i / SynthKit.SampleRate;
                double f = f0 + (f1 - f0) * x;
                phase += TwoPi * f / SynthKit.SampleRate;
                double v = 0;
                for (int k = 1; k <= 6; k++)
                {
                    double d = (k * f - 1100) / 520;
                    v += (Math.Exp(-d * d) + 0.12) * Math.Sin(k * phase);
                }

                double env = Math.Min(1, time / 0.006) * Math.Min(1, (len - time) / 0.012) * Math.Exp(-time / (len * 0.6));
                Add(dst, at + i, gain * 0.45 * env * v);
            }
        }

        /// <summary>Один ход пилы: полоса шума вокруг hz с плавным нарастанием и спадом.</summary>
        private static void AddStroke(float[] dst, int at, double len, double hz, double gain, ref Rng r)
        {
            int m = Samples(len);
            var band = new Svf(hz, 0.9);
            for (int i = 0; i < m; i++)
                Add(dst, at + i, gain * band.Band(r.S()) * Arch((double)i / m, 0.4));
        }

        private static void Add(float[] dst, int index, double value)
        {
            int n = dst.Length;
            index %= n;
            if (index < 0)
                index += n;
            dst[index] += (float)value;
        }

        /// <summary>dst += gain · src с позиции at, с заворотом через конец петли.</summary>
        private static void MixWrap(float[] dst, float[] src, int at, double gain)
        {
            for (int i = 0; i < src.Length && i < dst.Length; i++)
                Add(dst, at + i, gain * src[i]);
        }
    }
}
