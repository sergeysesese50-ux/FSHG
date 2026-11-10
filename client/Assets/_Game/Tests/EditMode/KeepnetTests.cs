using System;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class KeepnetTests
    {
        private static FishDatabase Database() => TestFish.Database(TestFish.Species("roach"), TestFish.Species("pike"));

        private static FishSpecies Roach => TestFish.Species("roach");
        private static FishSpecies Pike => TestFish.Species("pike");

        private static CaughtFish Fish(int id, int weightG) =>
            new CaughtFish { id = id, speciesId = "roach", weightG = weightG, lengthCm = 20 };

        [Test]
        public void KeepnetAcceptsFishWhileThereIsRoom()
        {
            var net = new Keepnet(1000);
            Assert.IsTrue(net.TryAdd(Fish(1, 600)));
            Assert.IsTrue(net.TryAdd(Fish(2, 400)));
            Assert.AreEqual(1000, net.TotalWeightG);
            Assert.AreEqual(0, net.FreeG);
            Assert.IsFalse(net.Fits(1));
            Assert.IsFalse(net.TryAdd(Fish(3, 1)));
            Assert.AreEqual(2, net.Count);
        }

        [Test]
        public void EmptyKeepnetTakesFishHeavierThanCapacity()
        {
            var net = new Keepnet(1000);
            Assert.IsTrue(net.TryAdd(Fish(1, 5000)), "трофей тяжелее садка должен влезть в пустой садок");
            Assert.AreEqual(0, net.FreeG);
            Assert.IsFalse(net.Fits(10));
        }

        [Test]
        public void KeepnetRejectsWeightlessFish()
        {
            var net = new Keepnet(1000);
            Assert.IsFalse(net.Fits(0));
            Assert.IsFalse(net.TryAdd(Fish(1, 0)));
            Assert.IsFalse(net.TryAdd(Fish(1, -5)));
        }

        [Test]
        public void RemoveFreesRoom()
        {
            var net = new Keepnet(1000);
            net.TryAdd(Fish(1, 600));
            net.TryAdd(Fish(2, 400));
            Assert.IsNull(net.Remove(7));
            Assert.AreEqual(600, net.Remove(1).weightG);
            Assert.AreEqual(400, net.TotalWeightG);
            Assert.AreEqual(1, net.RemoveAll().Count);
            Assert.AreEqual(0, net.TotalWeightG);
            Assert.AreEqual(0, net.Count);
        }

        [Test]
        public void PriceIsWeightTimesPricePerKgRoundedDown()
        {
            var species = TestFish.Species("roach"); // 100 монет за кг
            Assert.AreEqual(12, FishPricing.Price(species, 120, false));
            Assert.AreEqual(12, FishPricing.Price(species, 129, false));
            Assert.AreEqual(24, FishPricing.Price(species, 120, true), "трофей вдвое дороже");
            Assert.AreEqual(1, FishPricing.Price(species, 3, false), "любая рыба стоит хотя бы монету");
            Assert.AreEqual(0, FishPricing.Price(species, 0, false));
        }

        [Test]
        public void KeepGivesGrowingIdsAndRefusesWhenFull()
        {
            var profile = new PlayerProfile();
            var first = profile.Keep(Roach, 6000, 40, false, 400);
            var second = profile.Keep(Roach, 4000, 35, false, 410);
            Assert.AreEqual(1, first.id);
            Assert.AreEqual(2, second.id);
            Assert.IsNull(profile.Keep(Roach, 100, 10, false, 420), "садок на 10 кг полон");
            Assert.AreEqual(2, profile.Keepnet.Count);

            Assert.IsTrue(profile.Release(1));
            Assert.IsFalse(profile.Release(1));
            Assert.AreEqual(3, profile.Keep(Roach, 100, 10, false, 430).id, "номер отпущенной рыбы не выдаётся снова");
        }

        [Test]
        public void SellAllPaysForEveryFishAndEmptiesKeepnet()
        {
            var db = Database();
            var profile = new PlayerProfile();
            profile.Keep(Roach, 1000, 30, false, 400);
            profile.Keep(Pike, 2000, 60, true, 410);
            Assert.AreEqual(100 + 400, profile.KeepnetValue(db));

            var receipt = profile.SellAll(db);
            Assert.AreEqual(2, receipt.Count);
            Assert.AreEqual(3000, receipt.WeightG);
            Assert.AreEqual(500, receipt.Money);
            Assert.AreEqual(500, profile.Money);
            Assert.AreEqual(0, profile.Keepnet.Count);

            var empty = profile.SellAll(db);
            Assert.AreEqual(0, empty.Count);
            Assert.AreEqual(500, profile.Money);
        }

        [Test]
        public void SellOneFishLeavesTheRest()
        {
            var db = Database();
            var profile = new PlayerProfile();
            var a = profile.Keep(Roach, 1000, 30, false, 400);
            profile.Keep(Roach, 500, 25, false, 410);

            var receipt = profile.Sell(a.id, db);
            Assert.AreEqual(100, receipt.Money);
            Assert.AreEqual(100, profile.Money);
            Assert.AreEqual(1, profile.Keepnet.Count);
            Assert.AreEqual(0, profile.Sell(a.id, db).Count, "ту же рыбу нельзя продать дважды");
            Assert.AreEqual(100, profile.Money);
        }

        [Test]
        public void SellAllWithUnknownSpeciesChangesNothing()
        {
            var profile = new PlayerProfile();
            profile.Keep(Roach, 1000, 30, false, 400);
            profile.Keep(TestFish.Species("ghost"), 1000, 30, false, 410);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => profile.SellAll(Database()));
            Assert.AreEqual(0, profile.Money);
            Assert.AreEqual(2, profile.Keepnet.Count);
        }

        [Test]
        public void StateRoundTripKeepsMoneyFishAndIds()
        {
            var db = Database();
            var profile = new PlayerProfile();
            profile.Keep(Roach, 1000, 30, false, 400);
            profile.Keep(Pike, 2000, 60, true, 410);
            profile.Sell(1, db);

            var restored = PlayerProfile.FromState(profile.ToState(), db);
            Assert.AreEqual(100, restored.Money);
            Assert.AreEqual(1, restored.Keepnet.Count);
            Assert.AreEqual("pike", restored.Keepnet.Fish[0].speciesId);
            Assert.IsTrue(restored.Keepnet.Fish[0].trophy);
            Assert.AreEqual(3, restored.Keep(Roach, 100, 10, false, 500).id);
        }

        [Test]
        public void FishRemembersWhereAndOnWhatItWasCaught()
        {
            var db = Database();
            var profile = new PlayerProfile();
            profile.Keep(Roach, 300, 25, false, 404, new CatchContext("stream", "worm", 120, "float_rod_basic", "line_012"));
            profile.Keep(Roach, 400, 27, false, 410);

            var restored = PlayerProfile.FromState(profile.ToState(), db);
            var first = restored.Keepnet.Fish[0];
            Assert.AreEqual("stream", first.spotId);
            Assert.AreEqual("worm", first.baitId);
            Assert.AreEqual(120, first.baitDepthCm);
            Assert.AreEqual("float_rod_basic", first.rodId);
            Assert.AreEqual("line_012", first.lineId);
            Assert.AreEqual(404, first.caughtAtMinute);

            var second = restored.Keepnet.Fish[1];
            Assert.IsTrue(string.IsNullOrEmpty(second.spotId), "без обстоятельств — поля пустые, как у рыбы из старого сохранения");
            Assert.AreEqual(0, second.baitDepthCm);
        }

        [Test]
        public void BrokenStateIsRepairedNotTrusted()
        {
            var state = new ProfileState
            {
                version = PlayerProfile.StateVersion,
                money = -50,
                nextFishId = 1,
                keepnet = new[]
                {
                    new CaughtFish { id = 5, speciesId = "roach", weightG = 300 },
                    null,
                    new CaughtFish { id = 5, speciesId = "roach", weightG = 300 },
                    new CaughtFish { id = 6, speciesId = "ghost", weightG = 300 },
                    new CaughtFish { id = 7, speciesId = "roach", weightG = 0 },
                    new CaughtFish { id = 0, speciesId = "roach", weightG = 300 },
                },
            };

            var profile = PlayerProfile.FromState(state, Database());
            Assert.AreEqual(0, profile.Money);
            Assert.AreEqual(1, profile.Keepnet.Count);
            Assert.AreEqual(6, profile.Keep(Roach, 100, 10, false, 0).id, "новый номер — после самого большого из сохранённых");
        }

        [Test]
        public void EmptyAndForeignStates()
        {
            var db = Database();
            var blank = PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion }, db);
            Assert.AreEqual(0, blank.Money);
            Assert.AreEqual(0, blank.Keepnet.Count);
            Assert.AreEqual(1, blank.Keep(Roach, 100, 10, false, 0).id);
            Assert.Throws<FormatException>(() => PlayerProfile.FromState(new ProfileState { version = 99 }, db));
        }

        [Test]
        public void RealFishHavePrices()
        {
            foreach (var species in TestFish.RealDatabase().All)
                Assert.Greater(FishPricing.Price(species, species.weightG.typical, false), 0, species.id);
        }
    }
}
