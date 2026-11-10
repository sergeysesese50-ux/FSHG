using System.Collections.Generic;
using System.Linq;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public class BiteModelTests
    {
        private static readonly string[] OneFish = { "testfish" };

        [Test]
        public void IdealConditionsGiveBaseRate()
        {
            var species = TestFish.Species(biteRatePerMin: 2f);
            var model = new BiteModel(TestFish.Database(species));

            Assert.AreEqual(2.0, model.RatePerMinute(species, TestFish.Ideal()), 1e-9);
        }

        [Test]
        public void WrongBaitGivesNoBite()
        {
            var species = TestFish.Species();
            var model = new BiteModel(TestFish.Database(species));
            var conditions = TestFish.Ideal();
            conditions.BaitId = "corn";

            Assert.AreEqual(0.0, model.RatePerMinute(species, conditions));
            Assert.IsFalse(model.TryRollBite(OneFish, conditions, new SeededRandom(1), out _));
        }

        [Test]
        public void OffHoursReduceRate()
        {
            var species = TestFish.Species();
            var settings = new BiteSettings();
            var model = new BiteModel(TestFish.Database(species), settings);
            var night = TestFish.Ideal();
            night.Hour = 23f;

            Assert.AreEqual(settings.OffHoursFactor, model.RatePerMinute(species, night), 1e-9);
        }

        [Test]
        public void WrongDepthHalvesRateAtFalloffDistance()
        {
            var species = TestFish.Species();
            var settings = new BiteSettings();
            var model = new BiteModel(TestFish.Database(species), settings);
            var shallow = TestFish.Ideal();
            shallow.DepthCm = species.depthCm.min - (float)settings.DepthFalloffCm;

            Assert.AreEqual(0.5, model.RatePerMinute(species, shallow), 1e-6);
        }

        [Test]
        public void ThickLineReducesRate()
        {
            var species = TestFish.Species();
            var model = new BiteModel(TestFish.Database(species));
            var thin = TestFish.Ideal();
            var thick = TestFish.Ideal();
            thick.LineDiameterMm = 0.30f;

            Assert.That(model.RatePerMinute(species, thick), Is.LessThan(model.RatePerMinute(species, thin) * 0.2));
        }

        [Test]
        public void SameSeedGivesSameBite()
        {
            var model = new BiteModel(TestFish.Database(TestFish.Species()));

            model.TryRollBite(OneFish, TestFish.Ideal(), new SeededRandom(5), out var a);
            model.TryRollBite(OneFish, TestFish.Ideal(), new SeededRandom(5), out var b);

            Assert.AreEqual(a.SpeciesId, b.SpeciesId);
            Assert.AreEqual(a.WeightG, b.WeightG);
            Assert.AreEqual(a.SecondsToBite, b.SecondsToBite);
        }

        [Test]
        public void MeanTimeToBiteMatchesRate()
        {
            var model = new BiteModel(TestFish.Database(TestFish.Species(biteRatePerMin: 2f)));
            var random = new SeededRandom(2026);

            double sum = 0;
            const int rolls = 20000;
            for (int i = 0; i < rolls; i++)
            {
                model.TryRollBite(OneFish, TestFish.Ideal(), random, out var bite);
                sum += bite.SecondsToBite;
            }

            // 2 поклёвки в минуту — в среднем 30 секунд ожидания.
            Assert.AreEqual(30.0, sum / rolls, 1.0);
        }

        [Test]
        public void SpeciesShareFollowsRates()
        {
            var model = new BiteModel(TestFish.Database(
                TestFish.Species("slow", biteRatePerMin: 1f),
                TestFish.Species("fast", biteRatePerMin: 3f)));
            var random = new SeededRandom(7);
            var ids = new[] { "slow", "fast" };

            int slow = 0;
            const int rolls = 20000;
            for (int i = 0; i < rolls; i++)
            {
                model.TryRollBite(ids, TestFish.Ideal(), random, out var bite);
                if (bite.SpeciesId == "slow")
                    slow++;
            }

            Assert.AreEqual(0.25, (double)slow / rolls, 0.015);
        }

        [Test]
        public void WeightsStayInBoundsAndTrophiesAreRare()
        {
            var species = TestFish.Species(trophyChance: 0.05f);
            var random = new SeededRandom(11);
            var w = species.weightG;

            int trophies = 0;
            const int rolls = 50000;
            for (int i = 0; i < rolls; i++)
            {
                int weight = BiteModel.RollWeight(species, random, out bool isTrophy);
                if (isTrophy)
                {
                    trophies++;
                    Assert.That(weight, Is.InRange(w.trophy, w.max));
                }
                else
                {
                    Assert.That(weight, Is.InRange(w.min, w.trophy - 1));
                }
            }

            Assert.AreEqual(0.05, (double)trophies / rolls, 0.005);
        }

        [Test]
        public void TypicalWeightIsMostCommon()
        {
            var species = TestFish.Species(trophyChance: 0f);
            var random = new SeededRandom(3);
            var w = species.weightG;

            // Делим диапазон min–trophy на 9 корзин и ищем самую полную.
            var buckets = new int[9];
            double size = (w.trophy - w.min) / 9.0;
            for (int i = 0; i < 50000; i++)
            {
                int weight = BiteModel.RollWeight(species, random, out _);
                buckets[System.Math.Min(8, (int)((weight - w.min) / size))]++;
            }

            int fullest = System.Array.IndexOf(buckets, buckets.Max());
            int typicalBucket = (int)((w.typical - w.min) / size);
            Assert.AreEqual(typicalBucket, fullest);
        }

        [Test]
        public void RealLakeMorningWormMostlyCatchesRoach()
        {
            var database = TestFish.RealDatabase();
            var model = new BiteModel(database);
            var ids = database.All.Select(s => s.id).ToList();
            var conditions = new BiteConditions { Hour = 7f, DepthCm = 150f, BaitId = "worm", LineDiameterMm = 0.12f };
            var random = new SeededRandom(1);

            var counts = new Dictionary<string, int>();
            for (int i = 0; i < 5000; i++)
            {
                model.TryRollBite(ids, conditions, random, out var bite);
                counts.TryGetValue(bite.SpeciesId, out int n);
                counts[bite.SpeciesId] = n + 1;
            }

            string top = counts.OrderByDescending(pair => pair.Value).First().Key;
            Assert.AreEqual("roach", top);
            Assert.IsFalse(counts.ContainsKey("zander"), "судак не берёт на червя");
        }
    }
}
