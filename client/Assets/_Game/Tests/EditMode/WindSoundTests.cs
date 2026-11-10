using System;
using Fishing.Core.Audio;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class WindSoundTests
    {
        private const int Rate = 44100;

        /// <summary>Громкость фона озера (RMS) при силе ветра wind 0…1, после того как слой ветра установился.</summary>
        private static double AmbienceRms(float wind)
        {
            var mixer = new FightAudioMixer(new FightSoundBank(12.0), 11);
            var buffer = new float[2 * 1024];
            mixer.Publish(new FightAudioParams { OutputRate = Rate, Master = 0.8f, AmbienceVolume = 0.16f, Wind = wind });
            double sum = 0;
            long count = 0;
            int buffers = 12 * Rate / 1024;
            for (int k = 0; k < buffers; k++)
            {
                mixer.Render(buffer, 2);
                if (k < buffers / 2)
                    continue;                   // первые 6 с — ветер набирает силу
                for (int i = 0; i < buffer.Length; i += 2)
                {
                    sum += (double)buffer[i] * buffer[i];
                    count++;
                }
            }
            return Math.Sqrt(sum / count);
        }

        [Test]
        public void StrongerWindIsLouder()
        {
            double calm = AmbienceRms(0f);
            double light = AmbienceRms(0.25f);
            double strong = AmbienceRms(1f);
            Assert.That(calm, Is.GreaterThan(1e-4), "в штиль озеро не молчит: плеск остаётся");
            Assert.That(light, Is.GreaterThan(calm * 1.05), $"2 м/с слышнее штиля: {calm:0.0000} → {light:0.0000}");
            Assert.That(strong, Is.GreaterThan(light * 2.0), $"8 м/с намного громче 2 м/с: {light:0.0000} → {strong:0.0000}");
        }

        [Test]
        public void WindBedIsSeamlessAndFinite()
        {
            var bed = LakeSynth.WindBed(8);
            double largest = 0;
            for (int i = 1; i < bed.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(bed[i]) || float.IsInfinity(bed[i]));
                largest = Math.Max(largest, Math.Abs(bed[i] - bed[i - 1]));
            }

            Assert.LessOrEqual(Math.Abs(bed[0] - bed[bed.Length - 1]), largest * 1.5, "на стыке петли нет скачка");
        }

        [Test]
        public void DirectorPassesWindToTheMixer()
        {
            var director = new FightAudioDirector { Wind = 0.6f };
            director.Update(LakeSoundState.Idle, default, 110f, 0f, 0f, 0.8f, 48000);
            Assert.AreEqual(0.6f, director.Params.Wind, 1e-6f);
            director.Wind = 7f;
            director.Update(LakeSoundState.Idle, default, 110f, 0f, 0f, 0.8f, 48000);
            Assert.AreEqual(1f, director.Params.Wind, 1e-6f, "сила ветра ограничена единицей");
        }
    }
}
