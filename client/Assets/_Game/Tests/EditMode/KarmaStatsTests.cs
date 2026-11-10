using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class KarmaStatsTests
    {
        // TestFish.Species: обычный вес 300 г, трофей от 1000 г, 100 монет за кг.
        private static FishSpecies Roach => TestFish.Species("roach");
        private static FishSpecies Pike => TestFish.Species("pike");
        private static FishDatabase Database() => TestFish.Database(Roach, Pike);

        [Test]
        public void JuvenileIsLighterThanHalfOfTypical()
        {
            Assert.IsTrue(KarmaRules.IsJuvenile(Roach, 149));
            Assert.IsFalse(KarmaRules.IsJuvenile(Roach, 150));
            Assert.IsFalse(KarmaRules.IsJuvenile(Roach, 0));
        }

        [Test]
        public void ReleasingRaisesKarmaByKindOfFish()
        {
            var profile = new PlayerProfile();
            profile.ReleaseCatch(Roach, 300, false);
            Assert.AreEqual(1, profile.Karma);
            Assert.AreEqual(KarmaReason.ReleasedFish, profile.LastKarmaChange.Reason);

            profile.ReleaseCatch(Roach, 100, false);
            Assert.AreEqual(3, profile.Karma, "малёк +2");
            Assert.AreEqual(KarmaReason.ReleasedJuvenile, profile.LastKarmaChange.Reason);

            profile.ReleaseCatch(Roach, 1200, true);
            Assert.AreEqual(8, profile.Karma, "трофей +5");
            Assert.AreEqual(5, profile.LastKarmaChange.Delta);
            Assert.AreEqual(3, profile.KarmaChangeCount);
            Assert.AreEqual(3, profile.Stats.Find("roach").released);
        }

        [Test]
        public void KeepingJuvenileDropsKarmaAndNormalFishDoesNot()
        {
            var profile = new PlayerProfile();
            profile.Keep(Roach, 300, 25, false, 0);
            Assert.AreEqual(0, profile.Karma);
            Assert.AreEqual(0, profile.KarmaChangeCount);

            profile.Keep(Roach, 100, 15, false, 0);
            Assert.AreEqual(-3, profile.Karma);
            Assert.AreEqual(KarmaReason.KeptJuvenile, profile.LastKarmaChange.Reason);
        }

        [Test]
        public void ReleasingFromKeepnetAndBreakingLine()
        {
            var profile = new PlayerProfile();
            var fish = profile.Keep(Roach, 300, 25, false, 0);
            Assert.IsTrue(profile.Release(fish.id));
            Assert.AreEqual(1, profile.Karma);
            Assert.IsFalse(profile.Release(fish.id));
            Assert.AreEqual(1, profile.Karma, "повторно ту же рыбу не отпустить");

            profile.RecordLoss(FightOutcome.LineBroke);
            Assert.AreEqual(0, profile.Karma);
            profile.RecordLoss(FightOutcome.Escaped);
            profile.RecordLoss(FightOutcome.RodBroke);
            Assert.AreEqual(0, profile.Karma, "сход и поломка удилища карму не трогают");
            Assert.AreEqual(3, profile.Stats.Losses.Total);
            Assert.AreEqual(1, profile.Stats.Losses.lineBroke);
        }

        [Test]
        public void SellingDoesNotChangeKarma()
        {
            var profile = new PlayerProfile();
            profile.Keep(Roach, 300, 25, false, 0);
            profile.SellAll(Database());
            Assert.AreEqual(0, profile.Karma);
        }

        [Test]
        public void KarmaStaysWithinLimits()
        {
            var profile = new PlayerProfile();
            for (int i = 0; i < 60; i++)
                profile.ReleaseCatch(Roach, 1200, true);
            Assert.AreEqual(KarmaRules.Max, profile.Karma);
            for (int i = 0; i < 300; i++)
                profile.RecordLoss(FightOutcome.LineBroke);
            Assert.AreEqual(KarmaRules.Min, profile.Karma);
        }

        [Test]
        public void LevelsCoverTheWholeRange()
        {
            Assert.AreEqual(KarmaLevel.Poacher, KarmaRules.Level(-100));
            Assert.AreEqual(KarmaLevel.Poacher, KarmaRules.Level(-50));
            Assert.AreEqual(KarmaLevel.Grabber, KarmaRules.Level(-49));
            Assert.AreEqual(KarmaLevel.Grabber, KarmaRules.Level(-10));
            Assert.AreEqual(KarmaLevel.Angler, KarmaRules.Level(-9));
            Assert.AreEqual(KarmaLevel.Angler, KarmaRules.Level(0));
            Assert.AreEqual(KarmaLevel.Angler, KarmaRules.Level(9));
            Assert.AreEqual(KarmaLevel.Honest, KarmaRules.Level(10));
            Assert.AreEqual(KarmaLevel.Honest, KarmaRules.Level(49));
            Assert.AreEqual(KarmaLevel.Keeper, KarmaRules.Level(50));
            Assert.AreEqual(KarmaLevel.Keeper, KarmaRules.Level(100));
        }

        [Test]
        public void CatchStatsCountWeightRecordsAndTrophies()
        {
            var profile = new PlayerProfile();
            Assert.IsFalse(profile.RecordCatch(Roach, 300, 25, false).SpeciesRecord, "первая рыба вида — не «новый рекорд»");
            Assert.IsTrue(profile.RecordCatch(Roach, 500, 30, false).SpeciesRecord);
            Assert.IsFalse(profile.RecordCatch(Roach, 400, 28, false).SpeciesRecord);
            Assert.IsTrue(profile.RecordCatch(Roach, 1200, 41, true).SpeciesRecord);
            profile.RecordCatch(Pike, 2000, 60, false);

            var roach = profile.Stats.Find("roach");
            Assert.AreEqual(4, roach.caught);
            Assert.AreEqual(2400, roach.totalWeightG);
            Assert.AreEqual(600, roach.AverageWeightG);
            Assert.AreEqual(1200, roach.bestWeightG);
            Assert.AreEqual(41, roach.bestLengthCm);
            Assert.AreEqual(1, roach.trophies);

            Assert.AreEqual(5, profile.Stats.FishCaught);
            Assert.AreEqual(4400, profile.Stats.TotalWeightG);
            Assert.AreEqual("pike", profile.Stats.Biggest.speciesId);
            Assert.AreEqual("roach", profile.Stats.Species[0].speciesId, "виды — в порядке первой поимки");
        }

        [Test]
        public void SalesGoIntoStats()
        {
            var db = Database();
            var profile = new PlayerProfile();
            profile.Keep(Roach, 1000, 30, false, 0);
            profile.Keep(Pike, 2000, 60, false, 0);
            profile.SellAll(db);
            Assert.AreEqual(300, profile.Stats.MoneyEarned);
            Assert.AreEqual(2, profile.Stats.Sold);
            Assert.AreEqual(1, profile.Stats.Find("pike").sold);
        }

        [Test]
        public void StateRoundTripKeepsKarmaAndStats()
        {
            var db = Database();
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 300, 25, false);
            profile.RecordCatch(Roach, 1200, 41, true);
            profile.ReleaseCatch(Roach, 1200, true);
            profile.RecordLoss(FightOutcome.Escaped);
            profile.Keep(Roach, 300, 25, false, 0);
            profile.SellAll(db);

            var restored = PlayerProfile.FromState(profile.ToState(), db);
            Assert.AreEqual(5, restored.Karma);
            Assert.AreEqual(2, restored.Stats.FishCaught);
            Assert.AreEqual(1200, restored.Stats.Find("roach").bestWeightG);
            Assert.AreEqual(1, restored.Stats.Trophies);
            Assert.AreEqual(1, restored.Stats.Released);
            Assert.AreEqual(1, restored.Stats.Losses.escaped);
            Assert.AreEqual(30, restored.Stats.MoneyEarned);
        }

        [Test]
        public void VersionOneSaveLoadsWithEmptyStats()
        {
            var state = new ProfileState
            {
                version = 1,
                money = 250,
                nextFishId = 4,
                keepnet = new[] { new CaughtFish { id = 3, speciesId = "roach", weightG = 300 } },
            };
            var profile = PlayerProfile.FromState(state, Database());
            Assert.AreEqual(250, profile.Money);
            Assert.AreEqual(1, profile.Keepnet.Count);
            Assert.AreEqual(0, profile.Karma);
            Assert.AreEqual(0, profile.Stats.FishCaught);
            Assert.AreEqual(PlayerProfile.StateVersion, profile.ToState().version, "пересохраняется уже новой версией");
        }

        [Test]
        public void BrokenStatsAreRepaired()
        {
            var state = new ProfileState
            {
                version = PlayerProfile.StateVersion,
                karma = 9999,
                moneyEarned = -5,
                species = new[]
                {
                    new SpeciesStats { speciesId = "roach", caught = -3, totalWeightG = -10, bestWeightG = 500 },
                    null,
                    new SpeciesStats { speciesId = "roach", caught = 7 },
                    new SpeciesStats { speciesId = "", caught = 7 },
                },
                losses = new LossStats { lineBroke = -2, escaped = 4 },
            };
            var profile = PlayerProfile.FromState(state, Database());
            Assert.AreEqual(KarmaRules.Max, profile.Karma);
            Assert.AreEqual(0, profile.Stats.MoneyEarned);
            Assert.AreEqual(1, profile.Stats.Species.Count);
            Assert.AreEqual(0, profile.Stats.Find("roach").caught);
            Assert.AreEqual(500, profile.Stats.Find("roach").bestWeightG);
            Assert.AreEqual(4, profile.Stats.Losses.Total);
        }
    }
}
