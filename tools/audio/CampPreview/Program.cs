using System;
using System.IO;
using Fishing.Core.Audio;

internal static class Program
{
    private static void Main()
    {
        var bed = CampSynth.CampBed();
        int rate = SynthKit.SampleRate;
        int n = bed.Length;

        double peak = 0, sum = 0;
        foreach (float v in bed)
        {
            peak = Math.Max(peak, Math.Abs(v));
            sum += (double)v * v;
        }

        double rms = Math.Sqrt(sum / n);
        Console.WriteLine($"длина {n / (double)rate:0.0} с, пик {Db(peak):0.0} дБFS, RMS {Db(rms):0.0} дБFS, пик-фактор {Db(peak / rms):0.0} дБ");

        // Уровни по секундам: самая тихая и самая громкая — видно, нет ли провалов и выбросов.
        double quiet = double.MaxValue, loud = 0;
        for (int s = 0; s + rate <= n; s += rate)
        {
            double e = 0;
            for (int i = 0; i < rate; i++)
                e += (double)bed[s + i] * bed[s + i];
            e = Math.Sqrt(e / rate);
            quiet = Math.Min(quiet, e);
            loud = Math.Max(loud, e);
        }

        Console.WriteLine($"по секундам: тише всего {Db(quiet):0.0} дБFS, громче всего {Db(loud):0.0} дБFS");
        Console.WriteLine($"шов петли: скачок {Math.Abs(bed[0] - bed[n - 1]):0.0000} (соседние сэмплы внутри — до {MaxStep(bed):0.0000})");

        // Петля дважды подряд: на стыке слышно, бесшовная ли она.
        var twice = new double[2 * n];
        for (int i = 0; i < twice.Length; i++)
            twice[i] = bed[i % n];
        string path = Path.Combine(RepoPaths.Previews("camp"), "camp_bed.wav");
        Wav.Write16(path, twice, rate);
        Console.WriteLine(path);

        // Фон озера при разном ветре: тот же микшер, что в игре; 12 с, из них измеряются последние 6.
        foreach (var (name, wind) in new[] { ("штиль", 0f), ("2 м/с", 0.25f), ("5 м/с", 0.625f), ("8 м/с", 1f) })
        {
            var mixer = new FightAudioMixer(new FightSoundBank(), 11);
            var windBuffer = new float[2 * 1024];
            mixer.Publish(new FightAudioParams { OutputRate = 48000, Master = 0.8f, AmbienceVolume = 0.16f, Wind = wind });
            int buffers = 12 * 48000 / 1024;
            var mono = new double[buffers * 1024];
            double windSum = 0;
            long windCount = 0;
            for (int k = 0; k < buffers; k++)
            {
                mixer.Render(windBuffer, 2);
                for (int i = 0; i < 1024; i++)
                {
                    mono[k * 1024 + i] = windBuffer[2 * i];
                    if (k >= buffers / 2)
                    {
                        windSum += (double)windBuffer[2 * i] * windBuffer[2 * i];
                        windCount++;
                    }
                }
            }

            string file = Path.Combine(RepoPaths.Previews("wind"), "lake_wind_" + ((int)Math.Round(wind * 8)) + "ms.wav");
            Wav.Write16(file, mono, 48000);
            Console.WriteLine($"фон озера, {name}: RMS {Db(Math.Sqrt(windSum / windCount)):0.0} дБFS → {file}");
        }

        // Звуки карточки улова: «в садок» и «бульк».
        WriteClip("ui_keep.wav", UiSynth.Keep());
        WriteClip("ui_release.wav", UiSynth.Release());
        WriteClip("ui_coin.wav", UiSynth.Coin());
        WriteClip("ui_coins.wav", UiSynth.Coin(4));

        // Подмотка в вываживании: тот же микшер и те же зёрна, что в игре. Катушка 110 см/с, 6 с.
        // Громкость укладки — как её считает FightAudioDirector.Loops (LayLevel × скорость^0,8).
        foreach (var (name, speed, load) in new[] { ("полная скорость", 1.0, 0.15), ("под нагрузкой", 0.45, 0.6) })
            Reel(name, speed, load);
    }

    private static void Reel(string name, double speed, double load)
    {
        const int rate = 48000;
        const float c0 = 110f;
        var mixer = new FightAudioMixer(new FightSoundBank(), 11);
        var buffer = new float[2 * 1024];
        float lay = (float)(FightAudioDirector.LayLevel * Math.Max(Math.Pow(speed, 0.8), 0.35 * load));
        var p = new FightAudioParams
        {
            OutputRate = rate, Master = 0.8f, Fight = true, C0 = c0, RotorCmPerSec = (float)(c0 * speed), ReelLoad = (float)load,
            LayVolume = lay, LayPitch = (float)((0.82 + 0.3 * speed) * (1 - 0.12 * load)), LoopReleaseSeconds = 0.15f,
            AmbienceVolume = 0.09f, Tight = true, LoadShare = (float)(0.3 + 0.4 * load),
        };
        mixer.Publish(p);
        int buffers = 6 * rate / 1024;
        var mono = new double[buffers * 1024];
        for (int k = 0; k < buffers; k++)
        {
            mixer.Render(buffer, 2);
            for (int i = 0; i < 1024; i++)
                mono[k * 1024 + i] = buffer[2 * i];
        }

        // Меряем последние 4 с: уровень, пик и «резкость» — насколько громкие всплески выше среднего (по 20 мс).
        int from = mono.Length - 4 * rate;
        double sum = 0, peak = 0, loudest = 0;
        int win = rate / 50;
        for (int i = from; i + win <= mono.Length; i += win)
        {
            double e = 0;
            for (int j = 0; j < win; j++)
            {
                double v = mono[i + j];
                e += v * v;
                peak = Math.Max(peak, Math.Abs(v));
            }

            sum += e;
            loudest = Math.Max(loudest, Math.Sqrt(e / win));
        }

        double rms = Math.Sqrt(sum / (mono.Length - from));
        string file = Path.Combine(RepoPaths.Previews("reel"), speed >= 1.0 ? "reel_full.wav" : "reel_loaded.wav");
        Wav.Write16(file, mono, rate);
        Console.WriteLine($"подмотка, {name}: RMS {Db(rms):0.0} дБFS, пик {Db(peak):0.0}, всплески над средним {Db(loudest / rms):0.0} дБ → {file}");
    }

    private static void WriteClip(string name, float[] clip)
    {
        var x = new double[clip.Length];
        double peak = 0;
        for (int i = 0; i < clip.Length; i++)
        {
            x[i] = clip[i];
            peak = Math.Max(peak, Math.Abs(clip[i]));
        }

        string path = Path.Combine(RepoPaths.Previews("ui"), name);
        Wav.Write16(path, x, UiSynth.SampleRate);
        Console.WriteLine($"{path}: {clip.Length / (double)UiSynth.SampleRate:0.00} с, пик {Db(peak):0.0} дБFS");
    }

    private static double Db(double x) => 20 * Math.Log10(Math.Max(x, 1e-9));

    private static double MaxStep(float[] s)
    {
        double m = 0;
        for (int i = 1; i < s.Length; i++)
            m = Math.Max(m, Math.Abs(s[i] - s[i - 1]));
        return m;
    }
}
