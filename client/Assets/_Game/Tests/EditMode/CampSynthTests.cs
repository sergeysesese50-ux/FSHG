using System;
using Fishing.Core.Audio;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class CampSynthTests
    {
        [Test]
        public void BedIsFiniteQuietEnoughAndNotSilent()
        {
            var bed = CampSynth.CampBed(12);
            Assert.AreEqual(12 * CampSynth.SampleRate, bed.Length);
            double peak = 0, sum = 0;
            foreach (float v in bed)
            {
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
                peak = Math.Max(peak, Math.Abs(v));
                sum += (double)v * v;
            }

            double rms = Math.Sqrt(sum / bed.Length);
            Assert.Less(peak, 0.9, "фон не должен упираться в потолок");
            Assert.Greater(rms, 0.002, "фон не должен быть тишиной");
            Assert.Less(rms, 0.12, "фон — не громче самой игры");
        }

        [Test]
        public void LakeAmbienceGoesSilentOnCamp()
        {
            var director = new FightAudioDirector();
            director.Update(LakeSoundState.Idle, default, 110f, 0f, 0f, 0.8f, 48000);
            Assert.Greater(director.Params.AmbienceVolume, 0f, "на озере фон озера звучит");

            director.AmbienceScale = 0f;
            director.Update(LakeSoundState.Idle, default, 110f, 0f, 0f, 0.8f, 48000);
            Assert.AreEqual(0f, director.Params.AmbienceVolume, "на турбазе фон озера молчит");
        }

        [Test]
        public void BedIsTheSameEveryTime()
        {
            var a = CampSynth.CampBed(6);
            var b = CampSynth.CampBed(6);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void LoopSeamHasNoJump()
        {
            var bed = CampSynth.CampBed(12);
            double largest = 0;
            for (int i = 1; i < bed.Length; i++)
                largest = Math.Max(largest, Math.Abs(bed[i] - bed[i - 1]));
            double seam = Math.Abs(bed[0] - bed[bed.Length - 1]);
            Assert.LessOrEqual(seam, largest, "скачок на стыке петли не больше обычного шага внутри неё");
        }
    }
}
