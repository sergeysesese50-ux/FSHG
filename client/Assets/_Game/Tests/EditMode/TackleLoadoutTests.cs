using System;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class TackleLoadoutTests
    {
        private static FishSpecies Roach => TestFish.Species("roach");
        private static FishSpecies Pike => TestFish.Species("pike");

        /// <summary>Профиль с 500 монетами (продана рыба 5 кг по 100 за кг) и первым уровнем… почти: опыт за неё тоже дан.</summary>
        private static PlayerProfile RichProfile()
        {
            var db = TestFish.Database(Roach);
            var profile = new PlayerProfile();
            profile.Keep(Roach, 5000, 60, false, 0);
            profile.SellAll(db);
            Assert.AreEqual(500, profile.Money);
            return profile;
        }

        private static RodData Rod(string type, int castRangeCm = 2500)
        {
            return new RodData { id = "rod", type = type, lengthCm = 400, maxLoadG = 2500, flex = 0.6f, castRangeCm = castRangeCm };
        }

        [Test]
        public void NewPlayerHasStarterTackleOnTheRod()
        {
            var profile = new PlayerProfile();
            Assert.AreEqual(TackleRules.StarterRod, profile.Tackle.RodId);
            Assert.AreEqual(TackleRules.StarterReel, profile.Tackle.ReelId);
            Assert.AreEqual(TackleRules.StarterLine, profile.Tackle.LineId);
            Assert.IsTrue(profile.Tackle.Owns(TackleRules.StarterRod));
            Assert.IsFalse(profile.Tackle.Owns("feeder_rod"));
        }

        [Test]
        public void TackleIsBoughtOnceForMoneyAndLevel()
        {
            var profile = RichProfile();
            Assert.IsFalse(profile.BuyTackle("feeder_rod", 100, 6), "уровень ниже шестого");
            Assert.IsFalse(profile.BuyTackle("bottom_rod_basic", 501, 1), "не хватает монет");
            Assert.AreEqual(500, profile.Money);

            Assert.IsTrue(profile.BuyTackle("bottom_rod_basic", 250, 1));
            Assert.AreEqual(250, profile.Money);
            Assert.IsTrue(profile.Tackle.Owns("bottom_rod_basic"));
            Assert.AreEqual(TackleRules.StarterRod, profile.Tackle.RodId, "купленное само не надевается");
            Assert.IsFalse(profile.BuyTackle("bottom_rod_basic", 250, 1), "второй раз не продают");
            Assert.AreEqual(250, profile.Money);
            Assert.IsTrue(profile.BuyTackle("line_016", 0, 0), "minLevel 0 — с первого уровня");
        }

        [Test]
        public void OnlyOwnedTackleCanBeEquipped()
        {
            var profile = RichProfile();
            Assert.IsFalse(profile.Tackle.EquipRod("bottom_rod_basic"));
            profile.BuyTackle("bottom_rod_basic", 250, 1);
            Assert.IsTrue(profile.Tackle.EquipRod("bottom_rod_basic"));
            Assert.AreEqual("bottom_rod_basic", profile.Tackle.RodId);
            Assert.IsFalse(profile.Tackle.EquipReel("reel_4000"));
            Assert.IsFalse(profile.Tackle.EquipLine(null));
        }

        [Test]
        public void LureIsBoughtOnceAndNeverRunsOut()
        {
            var profile = RichProfile();
            var spinner = new BaitData { id = "spinner", rodType = RodKinds.Spinning, kind = BaitKinds.Spinner, run = LureRuns.Mid, consumable = false, price = 150, minLevel = 1 };
            Assert.IsTrue(profile.BuyLure(spinner));
            Assert.AreEqual(350, profile.Money);
            Assert.AreEqual(1, profile.Baits.Count("spinner"));
            Assert.IsFalse(profile.BuyLure(spinner), "одна приманка — навсегда");
            Assert.AreEqual(350, profile.Money);

            var worm = new BaitData { id = "worm", rodType = RodKinds.Float, kind = BaitKinds.Animal, consumable = true, price = 5 };
            Assert.IsFalse(profile.BuyLure(worm), "расходная наживка — упаковками, а не как приманка");
            var late = new BaitData { id = "wobbler", rodType = RodKinds.Spinning, kind = BaitKinds.Wobbler, run = LureRuns.Surface, consumable = false, price = 10, minLevel = LevelRules.MaxLevel };
            Assert.IsFalse(profile.BuyLure(late), "уровень мал");
        }

        [Test]
        public void TackleSurvivesSaveAndLoad()
        {
            var db = TestFish.Database(Roach);
            var profile = RichProfile();
            profile.BuyTackle("bottom_rod_basic", 250, 1);
            profile.BuyTackle("line_016", 90, 1);
            profile.Tackle.EquipRod("bottom_rod_basic");
            profile.Tackle.EquipLine("line_016");

            var restored = PlayerProfile.FromState(profile.ToState(), db);
            Assert.AreEqual("bottom_rod_basic", restored.Tackle.RodId);
            Assert.AreEqual("line_016", restored.Tackle.LineId);
            Assert.AreEqual(TackleRules.StarterReel, restored.Tackle.ReelId);
            Assert.IsTrue(restored.Tackle.Owns("line_016"));
            Assert.AreEqual(160, restored.Money);

            var old = PlayerProfile.FromState(new ProfileState { version = 4, rodId = "feeder_rod", tackleOwned = new[] { "feeder_rod" } }, db);
            Assert.AreEqual(TackleRules.StarterRod, old.Tackle.RodId, "до версии 5 снастей в сохранении не было");
            Assert.IsFalse(old.Tackle.Owns("feeder_rod"));
        }

        [Test]
        public void BrokenLoadoutFallsBackToStarter()
        {
            var loadout = Loadout.FromState(new[] { null, "", "feeder_rod", "feeder_rod" }, "spinning_rod_basic", null, "line_999");
            Assert.AreEqual(4, loadout.Owned.Count, "стартовый набор и одна купленная вещь");
            Assert.AreEqual(TackleRules.StarterRod, loadout.RodId, "удилище не куплено — стартовое");
            Assert.AreEqual(TackleRules.StarterReel, loadout.ReelId);
            Assert.AreEqual(TackleRules.StarterLine, loadout.LineId);
            Assert.IsTrue(loadout.EquipRod("feeder_rod"));
        }

        [Test]
        public void EachRodKindTakesItsOwnBait()
        {
            var worm = new BaitData { id = "worm", rodType = RodKinds.Float, kind = BaitKinds.Animal, consumable = true, price = 5 };
            var jig = new BaitData { id = "jig", rodType = RodKinds.Spinning, kind = BaitKinds.Soft, run = LureRuns.Bottom, consumable = false, price = 100 };
            Assert.IsTrue(TackleRules.BaitFits(worm, Rod(RodKinds.Float)));
            Assert.IsTrue(TackleRules.BaitFits(worm, Rod(RodKinds.Bottom)), "донка ловит на ту же наживку, что поплавочная");
            Assert.IsFalse(TackleRules.BaitFits(worm, Rod(RodKinds.Spinning)));
            Assert.IsTrue(TackleRules.BaitFits(jig, Rod(RodKinds.Spinning)));
            Assert.IsFalse(TackleRules.BaitFits(jig, Rod(RodKinds.Bottom)));
            Assert.IsFalse(TackleRules.BaitFits(null, Rod(RodKinds.Float)));
        }

        [Test]
        public void BaitDepthDependsOnRodKind()
        {
            // Вода 300 см, спуск поплавка 100 см.
            Assert.AreEqual(100f, TackleRules.BaitDepthCm(RodKinds.Float, null, 100, 300f));
            Assert.AreEqual(60f, TackleRules.BaitDepthCm(RodKinds.Float, null, 100, 60f), "поплавочная — не глубже дна");
            Assert.AreEqual(300f, TackleRules.BaitDepthCm(RodKinds.Bottom, null, 100, 300f), "донка — всегда дно");
            Assert.AreEqual(280f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Bottom, 100, 300f), "джиг — у дна");
            Assert.AreEqual(120f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Surface, 100, 300f), "воблер — у поверхности");
            Assert.AreEqual(30f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Top, 100, 300f), "поппер — поверху");
            Assert.AreEqual(150f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Mid, 100, 300f), "блесна — в полводы");
            Assert.AreEqual(50f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Mid, 100, 50f), "на мели — по дну");

            Assert.IsTrue(TackleRules.OnBottom(RodKinds.Bottom, null, 100, 300f));
            Assert.IsTrue(TackleRules.OnBottom(RodKinds.Spinning, LureRuns.Bottom, 100, 300f));
            Assert.IsFalse(TackleRules.OnBottom(RodKinds.Spinning, LureRuns.Surface, 100, 300f));
            Assert.IsFalse(TackleRules.OnBottom(RodKinds.Float, null, 100, 300f));
        }

        [Test]
        public void RetrieveSpeedMovesTheLureUpAndDown()
        {
            Assert.AreEqual(LureRetrieve.DefaultPercent, LureRetrieve.Clamp(0), "не выставляли — обычная");
            Assert.AreEqual(LureRetrieve.MinPercent, LureRetrieve.Clamp(3));
            Assert.AreEqual(LureRetrieve.MaxPercent, LureRetrieve.Clamp(250));
            Assert.AreEqual(70, LureRetrieve.Clamp(74));
            Assert.AreEqual(1f, LureRetrieve.SpeedFactor(50), 1e-6f);
            Assert.Less(LureRetrieve.SpeedFactor(10), 0.6f);
            Assert.Greater(LureRetrieve.SpeedFactor(100), 1.5f);

            // Вода 300 см. На обычной скорости — те же глубины, что без ползунка.
            foreach (string run in new[] { LureRuns.Bottom, LureRuns.Surface, LureRuns.Mid, LureRuns.Top })
            {
                Assert.AreEqual(TackleRules.BaitDepthCm(RodKinds.Spinning, run, 100, 300f),
                    TackleRules.BaitDepthCm(RodKinds.Spinning, run, 100, 300f, 50), 1e-3f, run);
            }

            float slow = TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Mid, 100, 300f, 10);
            float fast = TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Mid, 100, 300f, 100);
            Assert.AreEqual(198f, slow, 1e-3f, "медленно — глубже: 66% глубины");
            Assert.AreEqual(90f, fast, 1e-3f, "быстро — мельче: 30% глубины");
            Assert.AreEqual(280f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Bottom, 100, 300f, 10), 1e-3f, "медленный джиг — у дна");
            Assert.AreEqual(205f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Bottom, 100, 300f, 100), 1e-3f, "быстрый отрывается на четверть глубины");
            Assert.AreEqual(30f, TackleRules.BaitDepthCm(RodKinds.Spinning, LureRuns.Top, 100, 300f, 100), 1e-3f, "поппер всегда поверху");
            Assert.AreEqual(100f, TackleRules.BaitDepthCm(RodKinds.Float, null, 100, 300f, 100), "поплавочной скорость проводки не касается");
        }

        [Test]
        public void RetrieveSpeedIsRememberedInTheProfile()
        {
            var db = TestFish.Database(Roach);
            var profile = new PlayerProfile { RetrievePercent = 80 };
            Assert.AreEqual(80, PlayerProfile.FromState(profile.ToState(), db).RetrievePercent);
            Assert.AreEqual(0, PlayerProfile.FromState(new ProfileState { version = 5 }, db).RetrievePercent, "в старом сохранении — не выставляли");
            Assert.AreEqual(LureRetrieve.MaxPercent,
                PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion, retrievePercent = 900 }, db).RetrievePercent);
        }

        [Test]
        public void CastRangeComesFromTheRodButNotFurtherThanTheLake()
        {
            Assert.AreEqual(25f, TackleRules.MaxCastM(Rod(RodKinds.Float, 2500)));
            Assert.AreEqual(40f, TackleRules.MaxCastM(Rod(RodKinds.Bottom, 4000)));
            Assert.AreEqual(TackleRules.LongestCastM, TackleRules.MaxCastM(Rod(RodKinds.Bottom, 9000)));
            Assert.AreEqual(24f, TackleRules.MaxCastM(null));
        }

        [Test]
        public void TackleFileAcceptsThreeRodKindsAndChecksLevels()
        {
            TackleFile File(RodData rod, BaitData bait)
            {
                return new TackleFile
                {
                    version = TackleDatabase.SupportedVersion,
                    rods = new[] { rod },
                    reels = new[] { new ReelData { id = "reel", maxDragG = 3000, retrieveCmPerSec = 55, fightSpeedCmPerSec = 110, powerG = 600, spoolCapacityM = 100 } },
                    lines = new[] { new LineData { id = "line", diameterMm = 0.12f, breakingLoadG = 1800 } },
                    baits = new[] { bait },
                };
            }

            var worm = new BaitData { id = "worm", rodType = RodKinds.Float, kind = BaitKinds.Animal, consumable = true, price = 5 };
            Assert.AreEqual(1, new TackleDatabase(File(Rod(RodKinds.Bottom), worm)).Rods.Count);
            Assert.Throws<FormatException>(() => new TackleDatabase(File(Rod("net"), worm)), "неизвестный вид удилища");

            var deep = Rod(RodKinds.Float);
            deep.minLevel = LevelRules.MaxLevel + 1;
            Assert.Throws<FormatException>(() => new TackleDatabase(File(deep, worm)), "уровень выше предела");

            var forBottom = new BaitData { id = "worm", rodType = RodKinds.Bottom, kind = BaitKinds.Animal, consumable = true, price = 5 };
            Assert.Throws<FormatException>(() => new TackleDatabase(File(Rod(RodKinds.Float), forBottom)), "у наживки нет вида «донная»");

            var noKind = new BaitData { id = "worm", rodType = RodKinds.Float, consumable = true, price = 5 };
            Assert.Throws<FormatException>(() => new TackleDatabase(File(Rod(RodKinds.Float), noKind)), "род наживки обязателен");
            var wrongKind = new BaitData { id = "worm", rodType = RodKinds.Float, kind = BaitKinds.Spoon, consumable = true, price = 5 };
            Assert.Throws<FormatException>(() => new TackleDatabase(File(Rod(RodKinds.Float), wrongKind)), "колебалка — не наживка");
            var noRun = new BaitData { id = "jig", rodType = RodKinds.Spinning, kind = BaitKinds.Soft, consumable = false, price = 5 };
            Assert.Throws<FormatException>(() => new TackleDatabase(File(Rod(RodKinds.Spinning), noRun)), "приманке нужен ход");
        }

        [Test]
        public void RealTackleHasAllRodKindsAndFreeStarterSet()
        {
            var tackle = TestFish.RealTackle();
            bool floatRod = false, bottom = false, spinning = false;
            foreach (var rod in tackle.Rods)
            {
                floatRod |= rod.type == RodKinds.Float;
                bottom |= rod.type == RodKinds.Bottom;
                spinning |= rod.type == RodKinds.Spinning;
            }

            Assert.IsTrue(floatRod && bottom && spinning, "есть все три вида удилищ");
            Assert.AreEqual(0, tackle.GetRod(TackleRules.StarterRod).price);
            Assert.AreEqual(0, tackle.GetReel(TackleRules.StarterReel).price);
            Assert.AreEqual(0, tackle.GetLine(TackleRules.StarterLine).price);

            bool lure = false;
            foreach (var bait in tackle.Baits)
                lure |= !bait.consumable && bait.rodType == RodKinds.Spinning;
            Assert.IsTrue(lure, "спиннингу есть на что ловить");
        }
    }
}
