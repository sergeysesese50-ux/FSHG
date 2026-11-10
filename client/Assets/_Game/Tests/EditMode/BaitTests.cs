using System;
using System.Collections.Generic;
using System.Linq;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class BaitTests
    {
        private static FishSpecies Roach => TestFish.Species("roach");

        private static BaitData Bait(string id, int price = 5, bool consumable = true)
        {
            return new BaitData { id = id, rodType = "float", kind = BaitKinds.Animal, consumable = consumable, price = price };
        }

        /// <summary>Профиль с деньгами: продана рыба на 500 монет (5 кг по 100 за кг).</summary>
        private static PlayerProfile RichProfile()
        {
            var db = TestFish.Database(Roach);
            var profile = new PlayerProfile();
            profile.Keep(Roach, 5000, 60, false, 0);
            profile.SellAll(db);
            Assert.AreEqual(500, profile.Money);
            return profile;
        }

        // ---------- Вкус наживки и клёв ----------

        [Test]
        public void TasteScalesBiteRate()
        {
            var species = TestFish.Species(biteRatePerMin: 2f);
            species.baits = new[]
            {
                new BaitTaste { id = "worm", taste = 100 },
                new BaitTaste { id = "bread", taste = 50 },
            };
            var model = new BiteModel(TestFish.Database(species));
            var conditions = TestFish.Ideal();

            Assert.AreEqual(2.0, model.RatePerMinute(species, conditions), 1e-9);
            conditions.BaitId = "bread";
            Assert.AreEqual(1.0, model.RatePerMinute(species, conditions), 1e-9, "вкус 50 — половина клёва");
            conditions.BaitId = "peas";
            Assert.AreEqual(0.0, model.RatePerMinute(species, conditions), "наживки нет в списке — не берёт");
            conditions.BaitId = null;
            Assert.AreEqual(0.0, model.RatePerMinute(species, conditions), "пустой крючок");
        }

        [Test]
        public void BetterBaitGetsMoreOfThatFish()
        {
            var roach = TestFish.Species("roach");
            roach.baits = new[] { new BaitTaste { id = "dough", taste = 100 }, new BaitTaste { id = "worm", taste = 50 } };
            var perch = TestFish.Species("perch");
            perch.baits = new[] { new BaitTaste { id = "worm", taste = 100 } };
            var model = new BiteModel(TestFish.Database(roach, perch));
            var ids = new List<string> { "roach", "perch" };

            int RoachShare(string bait)
            {
                var conditions = TestFish.Ideal();
                conditions.BaitId = bait;
                var random = new SeededRandom(7);
                int count = 0;
                for (int i = 0; i < 3000; i++)
                {
                    Assert.IsTrue(model.TryRollBite(ids, conditions, random, out var bite));
                    if (bite.SpeciesId == "roach")
                        count++;
                }

                return count;
            }

            Assert.AreEqual(3000, RoachShare("dough"), "на тесто окунь не берёт вовсе");
            int onWorm = RoachShare("worm");
            Assert.That(onWorm, Is.InRange(850, 1150), "на червя плотвы около трети: вкус 50 против 100 у окуня");
        }

        [Test]
        public void BadBaitListsAreRejected()
        {
            var outOfRange = TestFish.Species();
            outOfRange.baits = new[] { new BaitTaste { id = "worm", taste = 101 } };
            Assert.Throws<FormatException>(() => TestFish.Database(outOfRange));

            var zero = TestFish.Species();
            zero.baits = new[] { new BaitTaste { id = "worm", taste = 0 } };
            Assert.Throws<FormatException>(() => TestFish.Database(zero));

            var twice = TestFish.Species();
            twice.baits = new[] { new BaitTaste { id = "worm", taste = 100 }, new BaitTaste { id = "worm", taste = 50 } };
            Assert.Throws<FormatException>(() => TestFish.Database(twice));

            var noId = TestFish.Species();
            noId.baits = new[] { new BaitTaste { taste = 100 } };
            Assert.Throws<FormatException>(() => TestFish.Database(noId));
        }

        // ---------- Коробка с наживкой ----------

        [Test]
        public void NewPlayerStartsWithWormOnTheHook()
        {
            var profile = new PlayerProfile();
            Assert.AreEqual(BaitRules.WormId, profile.Baits.SelectedId);
            Assert.AreEqual(30, profile.Baits.SelectedCount);
            Assert.AreEqual(20, profile.Baits.Count("bread"));
            Assert.AreEqual(0, profile.Baits.Count("peas"));
        }

        [Test]
        public void EachBiteEatsOneBaitUntilNoneIsLeft()
        {
            var box = BaitBox.FromState(new[] { new BaitStock { id = "worm", count = 2 } }, "worm");
            Assert.IsTrue(box.UseSelected());
            Assert.IsTrue(box.UseSelected());
            Assert.AreEqual(0, box.SelectedCount);
            Assert.IsFalse(box.UseSelected(), "наживка кончилась");
            Assert.AreEqual("worm", box.SelectedId, "выбор остаётся — интерфейс покажет «кончилась»");
        }

        [Test]
        public void OnlyBaitInStockCanBeSelected()
        {
            var box = BaitBox.Starter();
            Assert.IsFalse(box.Select("peas"));
            Assert.IsFalse(box.Select(null));
            Assert.AreEqual("worm", box.SelectedId);
            Assert.IsTrue(box.Select("bread"));
            Assert.AreEqual("bread", box.SelectedId);
        }

        [Test]
        public void AddStopsAtTheLimitAndPicksBaitForEmptyHook()
        {
            var box = BaitBox.FromState(null, null);
            Assert.IsNull(box.SelectedId);
            Assert.AreEqual(5, box.Add("peas", 5));
            Assert.AreEqual("peas", box.SelectedId, "первая наживка сразу на крючке");
            Assert.AreEqual(BaitRules.MaxCount - 5, box.Add("peas", 5000));
            Assert.AreEqual(0, box.Add("peas", 1));
            Assert.AreEqual(0, box.Add("worm", 0));
            Assert.AreEqual(0, box.Add(null, 3));
        }

        [Test]
        public void BrokenBaitStateIsRepaired()
        {
            var box = BaitBox.FromState(new[]
            {
                null,
                new BaitStock { id = "", count = 5 },
                new BaitStock { id = "worm", count = -4 },
                new BaitStock { id = "worm", count = 9 },
                new BaitStock { id = "bread", count = 100000 },
            }, "worm");

            Assert.AreEqual(2, box.Items.Count);
            Assert.AreEqual(0, box.Count("worm"), "повторная запись пропущена, отрицательное число — ноль");
            Assert.AreEqual(BaitRules.MaxCount, box.Count("bread"));
            Assert.AreEqual("bread", box.SelectedId, "червей нет — на крючке первое, что есть");
        }

        // ---------- Профиль: покупка, черви, живец, сохранение ----------

        [Test]
        public void BuyingBaitCostsMoneyAndAddsAPack()
        {
            var profile = RichProfile();
            Assert.IsTrue(profile.BuyBait(Bait("peas", 40)));
            Assert.AreEqual(460, profile.Money);
            Assert.AreEqual(BaitRules.PackSize, profile.Baits.Count("peas"));

            Assert.IsFalse(profile.BuyBait(Bait("livebait", 461)), "не хватает монет");
            var late = Bait("frog", 5);
            late.minLevel = LevelRules.MaxLevel;
            Assert.IsFalse(profile.BuyBait(late), "наживка продаётся с уровня");
            Assert.IsFalse(profile.BuyBait(Bait("spinner", 10, consumable: false)), "блесну упаковками не продают");
            Assert.AreEqual(460, profile.Money);
            Assert.AreEqual(0, profile.Baits.Count("livebait"));
        }

        [Test]
        public void WormsCanBeDugOnlyWhenAlmostOut()
        {
            var profile = new PlayerProfile();
            Assert.IsFalse(profile.CanDigWorms);
            Assert.IsFalse(profile.DigWorms(), "червей 30 — копать незачем");

            for (int i = 0; i < 21; i++)
                Assert.IsTrue(profile.Baits.UseSelected());
            Assert.AreEqual(9, profile.Baits.Count(BaitRules.WormId));
            Assert.IsTrue(profile.DigWorms());
            Assert.AreEqual(9 + BaitRules.DigWormsCount, profile.Baits.Count(BaitRules.WormId));
            Assert.IsFalse(profile.DigWorms());
            Assert.AreEqual(0, profile.Money, "копать — бесплатно");
        }

        [Test]
        public void SmallFishBecomesLiveBait()
        {
            var profile = new PlayerProfile();
            var small = profile.Keep(Roach, BaitRules.LiveBaitMaxWeightG, 12, false, 0);
            var big = profile.Keep(Roach, BaitRules.LiveBaitMaxWeightG + 1, 13, false, 0);

            Assert.IsFalse(profile.MakeLiveBait(big.id), "крупновата для живца");
            Assert.IsFalse(profile.MakeLiveBait(999));
            Assert.IsTrue(profile.MakeLiveBait(small.id));
            Assert.AreEqual(1, profile.Baits.Count(BaitRules.LiveBaitId));
            Assert.AreEqual(1, profile.Keepnet.Count, "рыба ушла из садка");
            Assert.IsFalse(profile.MakeLiveBait(small.id));
            Assert.AreEqual(BaitRules.WormId, profile.Baits.SelectedId, "живец сам на крючок не садится");
        }

        [Test]
        public void FreshCatchCanGoStraightToLiveBait()
        {
            // TestFish.Species: обычный вес 300 г, малёк — легче 150 г: живец из такой рыбы роняет карму, как «забрал малька».
            var profile = new PlayerProfile();
            Assert.IsTrue(profile.MakeLiveBaitFromCatch(Roach, 90));
            Assert.AreEqual(1, profile.Baits.Count(BaitRules.LiveBaitId));
            Assert.AreEqual(0, profile.Keepnet.Count, "в садок рыба не попадает");
            Assert.AreEqual(-3, profile.Karma);
            Assert.AreEqual(KarmaReason.KeptJuvenile, profile.LastKarmaChange.Reason);

            Assert.IsFalse(profile.MakeLiveBaitFromCatch(Roach, 101), "тяжелее 100 г — не живец");
            Assert.AreEqual(1, profile.Baits.Count(BaitRules.LiveBaitId));

            var small = TestFish.Species("bleak");
            small.weightG = new WeightRange { min = 20, typical = 80, qualifying = 150, trophy = 200, max = 300 };
            Assert.IsTrue(profile.MakeLiveBaitFromCatch(small, 90));
            Assert.AreEqual(-3, profile.Karma, "взрослая мелкая рыба карму не роняет");
        }

        [Test]
        public void BaitSurvivesSaveAndLoad()
        {
            var db = TestFish.Database(Roach);
            var profile = RichProfile();
            profile.BuyBait(Bait("dough", 3));
            profile.Baits.Select("dough");
            profile.Baits.UseSelected();

            var restored = PlayerProfile.FromState(profile.ToState(), db);
            Assert.AreEqual("dough", restored.Baits.SelectedId);
            Assert.AreEqual(BaitRules.PackSize - 1, restored.Baits.SelectedCount);
            Assert.AreEqual(30, restored.Baits.Count(BaitRules.WormId));
            Assert.AreEqual(497, restored.Money);
        }

        [Test]
        public void OldSaveWithoutBaitGetsStarterStock()
        {
            var db = TestFish.Database(Roach);
            var old = PlayerProfile.FromState(new ProfileState { version = 2, money = 12 }, db);
            Assert.AreEqual(BaitRules.WormId, old.Baits.SelectedId);
            Assert.AreEqual(30, old.Baits.SelectedCount);
            Assert.AreEqual(12, old.Money);

            var emptied = PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion }, db);
            Assert.AreEqual(0, emptied.Baits.Items.Count, "новая версия без наживки — коробка пуста");
            Assert.IsTrue(emptied.CanDigWorms, "но червей всегда можно накопать");
        }

        // ---------- Настоящие данные ----------

        [Test]
        public void RealFishTakeOnlyBaitsFromTackle()
        {
            var fish = TestFish.RealDatabase();
            var tackle = TestFish.RealTackle();
            var eaten = new HashSet<string>();
            foreach (var species in fish.All)
            {
                foreach (var bait in species.baits)
                {
                    Assert.IsTrue(tackle.TryGetBait(bait.id, out _), species.id + ": наживки «" + bait.id + "» нет в tackle.json");
                    eaten.Add(bait.id);
                }

                Assert.IsTrue(species.baits.Any(b => tackle.GetBait(b.id).rodType == "float"),
                    species.id + ": должна ловиться на поплавочную удочку");
            }

            foreach (var bait in tackle.Baits)
                Assert.IsTrue(eaten.Contains(bait.id), "на «" + bait.id + "» никто не клюёт");
            foreach (var starter in BaitRules.StarterStock())
                Assert.IsTrue(tackle.TryGetBait(starter.id, out var data) && data.consumable, starter.id);
            Assert.IsTrue(tackle.TryGetBait(BaitRules.LiveBaitId, out _));
        }
    }
}
