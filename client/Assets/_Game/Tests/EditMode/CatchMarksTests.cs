using System;
using Fishing.Core;
using Fishing.Core.Audio;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class CatchMarksTests
    {
        // TestFish.Species: обычный вес 300 г, зачётный 600 г, трофей от 1000 г.
        private static FishSpecies Roach => TestFish.Species("roach");
        private static FishSpecies Pike => TestFish.Species("pike");

        [Test]
        public void GradesGoFromJuvenileToTrophy()
        {
            Assert.AreEqual(FishGrade.Juvenile, FishGrades.Of(Roach, 149, false));
            Assert.AreEqual(FishGrade.Regular, FishGrades.Of(Roach, 150, false));
            Assert.AreEqual(FishGrade.Regular, FishGrades.Of(Roach, 300, false), "обычный вес ещё не зачётный");
            Assert.AreEqual(FishGrade.Regular, FishGrades.Of(Roach, 599, false));
            Assert.AreEqual(FishGrade.Qualifying, FishGrades.Of(Roach, 600, false));
            Assert.AreEqual(FishGrade.Qualifying, FishGrades.Of(Roach, 900, false));
            Assert.AreEqual(FishGrade.Trophy, FishGrades.Of(Roach, 1200, true));
        }

        [Test]
        public void FirstFishOfSpeciesIsNotARecord()
        {
            var profile = new PlayerProfile();
            var first = profile.RecordCatch(Roach, 300, 25, false);
            Assert.IsTrue(first.FirstOfSpecies);
            Assert.IsFalse(first.SpeciesRecord);
            Assert.IsFalse(first.BiggestEver, "самая первая рыба не «личный рекорд»");
            Assert.AreEqual(0, first.PreviousBestG);
        }

        [Test]
        public void RecordRemembersPreviousBest()
        {
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 300, 25, false);
            var better = profile.RecordCatch(Roach, 450, 29, false);
            Assert.IsTrue(better.SpeciesRecord);
            Assert.AreEqual(300, better.PreviousBestG);
            Assert.IsFalse(better.FirstOfSpecies);
            Assert.IsTrue(better.BiggestEver);

            var smaller = profile.RecordCatch(Roach, 400, 28, false);
            Assert.IsFalse(smaller.SpeciesRecord);
            Assert.IsFalse(smaller.BiggestEver);
            Assert.AreEqual(450, smaller.PreviousBestG);
        }

        [Test]
        public void BiggestEverLooksAcrossSpecies()
        {
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 500, 30, false);
            var smallPike = profile.RecordCatch(Pike, 400, 40, false);
            Assert.IsTrue(smallPike.FirstOfSpecies);
            Assert.IsFalse(smallPike.BiggestEver, "плотва в 500 г тяжелее");

            var bigPike = profile.RecordCatch(Pike, 2000, 60, false);
            Assert.IsTrue(bigPike.SpeciesRecord);
            Assert.IsTrue(bigPike.BiggestEver);
        }

        [Test]
        public void MoreFishSoldMeansLongerCoinSoundUpToALimit()
        {
            Assert.Greater(UiSynth.Coin(4).Length, UiSynth.Coin(1).Length);
            Assert.AreEqual(UiSynth.Coin(UiSynth.MaxCoins).Length, UiSynth.Coin(100).Length);
            Assert.AreEqual(UiSynth.Coin(1).Length, UiSynth.Coin(0).Length);
            Assert.Less(UiSynth.Coin(100).Length, 1.3 * UiSynth.SampleRate, "звук продажи короче 1,3 с");
        }

        [Test]
        public void UiSoundsAreFiniteAndAudible()
        {
            foreach (var clip in new[] { UiSynth.Keep(), UiSynth.Release(), UiSynth.Coin(), UiSynth.Coin(4) })
            {
                double peak = 0;
                foreach (float v in clip)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
                    peak = Math.Max(peak, Math.Abs(v));
                }

                Assert.Greater(peak, 0.05, "звук слышен");
                Assert.Less(peak, 0.95, "звук не упирается в потолок");
                Assert.Less(Math.Abs(clip[clip.Length - 1]), 0.01, "хвост затухает к концу");
            }
        }
    }
}
