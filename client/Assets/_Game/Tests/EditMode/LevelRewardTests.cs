using System;
using System.Collections.Generic;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class LevelRewardTests
    {
        // TestFish.Species: зачётный вес 600 г, цена 100 за кг. Рыба 500 г — 50 опыта и ещё 50 за первую рыбу вида.
        private static FishSpecies Roach => TestFish.Species("roach");
        private static FishSpecies Pike => TestFish.Species("pike");

        private static TackleDatabase Tackle()
        {
            return new TackleDatabase(new TackleFile
            {
                version = TackleDatabase.SupportedVersion,
                rods = new[] { new RodData { id = "rod_gift", type = RodKinds.Float, lengthCm = 500, maxLoadG = 3000, flex = 0.5f, castRangeCm = 2800, price = 180 } },
                reels = new[] { new ReelData { id = "reel_gift", maxDragG = 4000, retrieveCmPerSec = 60, fightSpeedCmPerSec = 150, powerG = 900, spoolCapacityM = 120, price = 300 } },
                lines = new[] { new LineData { id = "line_gift", diameterMm = 0.16f, breakingLoadG = 2800, price = 90 } },
                baits = new[]
                {
                    new BaitData { id = "worm", rodType = RodKinds.Float, kind = BaitKinds.Animal, consumable = true, price = 5 },
                    new BaitData { id = "jig", rodType = RodKinds.Spinning, kind = BaitKinds.Soft, run = LureRuns.Bottom, consumable = false, price = 100 },
                },
            });
        }

        /// <summary>Таблица наград: у каждого уровня 10 монет, у перечисленных — свои.</summary>
        private static LevelRewardDatabase Rewards(TackleDatabase tackle, params LevelReward[] special)
        {
            var levels = new List<LevelReward>();
            for (int level = 2; level <= LevelRules.MaxLevel; level++)
            {
                var own = Array.Find(special, r => r.level == level);
                levels.Add(own ?? new LevelReward { level = level, coins = 10 });
            }

            return new LevelRewardDatabase(new LevelRewardFile { version = LevelRewardDatabase.SupportedVersion, levels = levels.ToArray() }, tackle);
        }

        private static PlayerProfile AtLevelTwo()
        {
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 500, 30, false);
            Assert.AreEqual(2, profile.Level);
            return profile;
        }

        [Test]
        public void NewLevelGivesCoinsAndGifts()
        {
            var tackle = Tackle();
            var rewards = Rewards(tackle, new LevelReward
            {
                level = 2, coins = 25, baits = new[] { new BaitStock { id = "worm", count = 10 } }, lures = new[] { "jig" },
                tackle = new[] { "line_gift" }, boatMinutes = 30,
            });
            var profile = AtLevelTwo();
            int worms = profile.Baits.Count("worm");

            var receipt = profile.ClaimLevelRewards(rewards, tackle);
            Assert.IsNotNull(receipt);
            Assert.AreEqual(1, receipt.FromLevel);
            Assert.AreEqual(2, receipt.ToLevel);
            Assert.AreEqual(25, receipt.Coins);
            Assert.AreEqual(25, profile.Money);
            Assert.AreEqual(worms + 10, profile.Baits.Count("worm"));
            Assert.AreEqual(1, profile.Baits.Count("jig"));
            Assert.IsTrue(profile.Tackle.Owns("line_gift"));
            Assert.AreEqual(TackleRules.StarterLine, profile.Tackle.LineId, "подаренное само не надевается");
            Assert.AreEqual(1800.0, profile.BoatRentSecondsLeft, 1e-9);
            Assert.AreEqual(30, receipt.BoatMinutes);
            Assert.AreEqual(0, receipt.CompensationCoins);
            Assert.AreEqual(2, profile.RewardedLevel);
        }

        [Test]
        public void RewardIsGivenOnlyOnce()
        {
            var tackle = Tackle();
            var rewards = Rewards(tackle);
            var profile = AtLevelTwo();
            Assert.IsNotNull(profile.ClaimLevelRewards(rewards, tackle));
            Assert.IsNull(profile.ClaimLevelRewards(rewards, tackle));
            Assert.AreEqual(10, profile.Money);

            Assert.IsNull(new PlayerProfile().ClaimLevelRewards(rewards, tackle), "на первом уровне наград нет");
        }

        [Test]
        public void SeveralLevelsAtOnceGiveAllTheirRewards()
        {
            var tackle = Tackle();
            var rewards = Rewards(tackle);
            var profile = new PlayerProfile();
            // Трофейная щука 2 кг: 200 × 6 + 50 = 1250 опыта — сразу седьмой уровень.
            profile.RecordCatch(Pike, 2000, 70, true);
            Assert.AreEqual(7, profile.Level);

            var receipt = profile.ClaimLevelRewards(rewards, tackle);
            Assert.AreEqual(1, receipt.FromLevel);
            Assert.AreEqual(7, receipt.ToLevel);
            Assert.AreEqual(60, receipt.Coins, "по 10 монет за уровни со второго по седьмой");
            Assert.AreEqual(60, profile.Money);
        }

        [Test]
        public void ThingsAlreadyOwnedTurnIntoCoins()
        {
            var tackle = Tackle();
            var rewards = Rewards(tackle, new LevelReward
            {
                level = 2, coins = 20, lures = new[] { "jig" }, tackle = new[] { "rod_gift", "reel_gift" }, boatMinutes = 30,
            });
            var profile = AtLevelTwo();
            profile.Baits.Add("jig", 1);
            profile.Tackle.Add("rod_gift");

            var receipt = profile.ClaimLevelRewards(rewards, tackle);
            Assert.AreEqual(50 + 90, receipt.CompensationCoins, "половина цены джига и удилища");
            Assert.AreEqual(0, receipt.Lures.Count);
            CollectionAssert.AreEqual(new[] { "reel_gift" }, receipt.Tackle);
            Assert.AreEqual(20 + 140, profile.Money);
            Assert.AreEqual(1, profile.Baits.Count("jig"), "вторую приманку не дают");
        }

        [Test]
        public void BoatOwnerGetsCoinsInsteadOfRentTime()
        {
            var tackle = Tackle();
            var rewards = Rewards(tackle, new LevelReward { level = 2, coins = 1000 }, new LevelReward { level = 3, coins = 0, boatMinutes = 45 });
            var profile = AtLevelTwo();
            profile.ClaimLevelRewards(rewards, tackle);
            Assert.IsTrue(profile.BuyBoat(new BoatData { id = "rowboat", price = 600, rentPrice = 40, rentMinutes = 30 }));
            profile.RecordCatch(Roach, 590, 31, false);      // +59 и рекорд вида +15 → 174… ещё рыба
            profile.RecordCatch(Roach, 595, 31, false);
            Assert.GreaterOrEqual(profile.Level, 3);

            long before = profile.Money;
            var receipt = profile.ClaimLevelRewards(rewards, tackle);
            Assert.AreEqual(0, receipt.BoatMinutes);
            Assert.GreaterOrEqual(receipt.CompensationCoins, 45, "монета за каждую минуту аренды");
            Assert.AreEqual(0.0, profile.BoatRentSecondsLeft);
            Assert.AreEqual(before + receipt.Coins + receipt.CompensationCoins, profile.Money);
        }

        [Test]
        public void RewardedLevelSurvivesSaveAndOldSavesGetRewardsLater()
        {
            var db = TestFish.Database(Roach);
            var tackle = Tackle();
            var rewards = Rewards(tackle);
            var profile = AtLevelTwo();
            profile.ClaimLevelRewards(rewards, tackle);

            var restored = PlayerProfile.FromState(profile.ToState(), db);
            Assert.AreEqual(2, restored.RewardedLevel);
            Assert.IsNull(restored.ClaimLevelRewards(rewards, tackle), "после загрузки награда второй раз не выдаётся");

            // Сохранение до появления наград: уровень уже пятый, наград ещё не было.
            var old = PlayerProfile.FromState(new ProfileState { version = 5, xp = 600 }, db);
            Assert.AreEqual(5, old.Level);
            Assert.AreEqual(1, old.RewardedLevel);
            var late = old.ClaimLevelRewards(rewards, tackle);
            Assert.AreEqual(40, late.Coins, "за уровни со второго по пятый");

            // Испорченное значение не даёт «занять» награды будущих уровней.
            var broken = PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion, xp = 100, rewardedLevel = 20 }, db);
            Assert.AreEqual(2, broken.RewardedLevel);
        }

        [Test]
        public void ReceiptsAddUp()
        {
            var first = new RewardReceipt { FromLevel = 1, ToLevel = 2, Coins = 25 };
            first.AddBait("worm", 10);
            var second = new RewardReceipt { FromLevel = 2, ToLevel = 3, Coins = 30, BoatMinutes = 30 };
            second.AddBait("worm", 5);
            second.Lures.Add("jig");
            first.Add(second);
            Assert.AreEqual(1, first.FromLevel);
            Assert.AreEqual(3, first.ToLevel);
            Assert.AreEqual(55, first.Coins);
            Assert.AreEqual(1, first.Baits.Count);
            Assert.AreEqual(15, first.Baits[0].count);
            Assert.AreEqual(30, first.BoatMinutes);
            CollectionAssert.AreEqual(new[] { "jig" }, first.Lures);
        }

        [Test]
        public void BadRewardTablesAreRejected()
        {
            var tackle = Tackle();
            Assert.Throws<FormatException>(() => new LevelRewardDatabase(
                new LevelRewardFile { version = LevelRewardDatabase.SupportedVersion, levels = new[] { new LevelReward { level = 2, coins = 10 } } }, tackle),
                "награды должны быть у всех уровней");
            Assert.Throws<FormatException>(() => Rewards(tackle, new LevelReward { level = 3, coins = -1 }));
            Assert.Throws<FormatException>(() => Rewards(tackle, new LevelReward { level = 3, baits = new[] { new BaitStock { id = "caviar", count = 5 } } }),
                "неизвестная наживка");
            Assert.Throws<FormatException>(() => Rewards(tackle, new LevelReward { level = 3, baits = new[] { new BaitStock { id = "jig", count = 1 } } }),
                "приманка — не расходная наживка");
            Assert.Throws<FormatException>(() => Rewards(tackle, new LevelReward { level = 3, lures = new[] { "worm" } }), "червь — не приманка");
            Assert.Throws<FormatException>(() => Rewards(tackle, new LevelReward { level = 3, tackle = new[] { "rod_unknown" } }));
            Assert.Throws<FormatException>(() => new LevelRewardDatabase(new LevelRewardFile { version = 99, levels = new LevelReward[0] }, tackle));
        }

        [Test]
        public void RealRewardsCoverEveryLevelAndGrowWithIt()
        {
            var rewards = TestFish.RealLevelRewards();
            int previous = 0;
            for (int level = 2; level <= LevelRules.MaxLevel; level++)
            {
                var reward = rewards.For(level);
                Assert.IsNotNull(reward, "уровень " + level);
                Assert.Greater(reward.coins, 0, "монеты есть у каждого уровня");
                if (level % 5 != 0 && (level - 1) % 5 != 0)
                    Assert.GreaterOrEqual(reward.coins, previous, "между круглыми уровнями монет не становится меньше: " + level);
                previous = reward.coins;
            }

            Assert.IsNull(rewards.For(1));
        }
    }
}
