using System;
using System.Collections.Generic;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public class TackleDatabaseTests
    {
        [Test]
        public void RealTackleJsonIsValid()
        {
            var tackle = TestFish.RealTackle();

            Assert.AreEqual("spinning", tackle.GetRod("spinning_rod_basic").type);
            Assert.That(tackle.GetReel("reel_2500").maxDragG, Is.GreaterThan(0));
            Assert.That(tackle.GetLine("line_030").breakingLoadG, Is.GreaterThan(0));
            Assert.AreEqual("float", tackle.GetBait("worm").rodType);
        }

        [Test]
        public void FightTackleIsBuiltFromRealParts()
        {
            var tackle = TestFish.RealTackle();
            var rod = tackle.GetRod("spinning_rod_basic");

            var fight = FightTackle.From(rod, tackle.GetReel("reel_2500"), tackle.GetLine("line_030"));

            var reel = tackle.GetReel("reel_2500");
            Assert.AreEqual(rod.lengthCm * 0.6, fight.RodReachCm, 1e-6);
            Assert.AreEqual(rod.maxLoadG, fight.RodMaxLoadG);
            Assert.AreEqual(rod.maxLoadG * 0.6, fight.RodPowerG, 1e-6);
            Assert.AreEqual(reel.fightSpeedCmPerSec, fight.ReelFightSpeedCmPerSec, 1e-6);
            Assert.AreEqual(reel.powerG, fight.ReelPowerG, 1e-6);
            Assert.AreEqual(150 * 100.0, fight.SpoolCapacityCm, 1e-6);
        }

        [Test]
        public void RealReelsHaveFightNumbers()
        {
            var tackle = TestFish.RealTackle();

            Assert.That(tackle.GetReel("reel_1000").fightSpeedCmPerSec, Is.GreaterThan(0));
            Assert.That(tackle.GetReel("reel_2500").powerG, Is.GreaterThan(0));
            Assert.That(tackle.GetReel("reel_1000").retrieveCmPerSec, Is.GreaterThan(0));
        }

        [Test]
        public void UnknownPartThrows()
        {
            Assert.Throws<KeyNotFoundException>(() => TestFish.RealTackle().GetReel("reel_9000"));
        }

        [Test]
        public void DuplicateIdThrows()
        {
            var file = new TackleFile
            {
                version = TackleDatabase.SupportedVersion,
                rods = new[] { Rod("r"), Rod("r") },
                reels = new[] { Reel("reel") },
                lines = new[] { new LineData { id = "line", diameterMm = 0.2f, breakingLoadG = 4000 } },
                baits = new[] { new BaitData { id = "worm", rodType = "float" } },
            };

            var error = Assert.Throws<FormatException>(() => new TackleDatabase(file));
            StringAssert.Contains("дважды", error.Message);
        }

        [Test]
        public void ReelWithoutFightNumbersThrows()
        {
            var reel = Reel("reel");
            reel.powerG = 0;
            var file = new TackleFile
            {
                version = TackleDatabase.SupportedVersion,
                rods = new[] { Rod("r") },
                reels = new[] { reel },
                lines = new[] { new LineData { id = "line", diameterMm = 0.2f, breakingLoadG = 4000 } },
                baits = new[] { new BaitData { id = "worm", rodType = "float" } },
            };

            var error = Assert.Throws<FormatException>(() => new TackleDatabase(file));
            StringAssert.Contains("powerG", error.Message);
        }

        /// <summary>
        /// Снасти тестов и полос баланса (FightKit) заданы числами явно; они должны совпадать с tackle.json,
        /// иначе полосы проверяют не ту снасть, что в игре.
        /// </summary>
        [Test]
        public void TestTacklesMatchRealTackle()
        {
            var tackle = TestFish.RealTackle();
            var floatRod = tackle.GetRod("float_rod_basic");
            var spinningRod = tackle.GetRod("spinning_rod_basic");

            AssertSameTackle(FightTackle.From(floatRod, tackle.GetReel("reel_1000"), tackle.GetLine("line_012")), FightKit.FloatTackle());
            AssertSameTackle(FightTackle.From(spinningRod, tackle.GetReel("reel_2500"), tackle.GetLine("line_020")), FightKit.SpinningTackle(4000));
            AssertSameTackle(FightTackle.From(spinningRod, tackle.GetReel("reel_2500"), tackle.GetLine("line_030")), FightKit.SpinningTackle(8000));
        }

        private static void AssertSameTackle(FightTackle real, FightTackle test)
        {
            Assert.AreEqual(real.RodMaxLoadG, test.RodMaxLoadG);
            Assert.AreEqual(real.RodFlex, test.RodFlex, 1e-6);
            Assert.AreEqual(real.RodReachCm, test.RodReachCm, 1e-6);
            Assert.AreEqual(real.RodPowerG, test.RodPowerG, 1e-6);
            Assert.AreEqual(real.LineBreakingLoadG, test.LineBreakingLoadG);
            Assert.AreEqual(real.ReelMaxDragG, test.ReelMaxDragG);
            Assert.AreEqual(real.ReelFightSpeedCmPerSec, test.ReelFightSpeedCmPerSec, 1e-6);
            Assert.AreEqual(real.ReelPowerG, test.ReelPowerG, 1e-6);
            Assert.AreEqual(real.SpoolCapacityCm, test.SpoolCapacityCm, 1e-6);
        }

        private static ReelData Reel(string id)
        {
            return new ReelData
            {
                id = id, maxDragG = 1000, retrieveCmPerSec = 50, fightSpeedCmPerSec = 110, powerG = 600, spoolCapacityM = 100,
            };
        }

        private static RodData Rod(string id)
        {
            return new RodData { id = id, type = "float", lengthCm = 400, maxLoadG = 2500, flex = 0.5f, castRangeCm = 2000 };
        }
    }
}
