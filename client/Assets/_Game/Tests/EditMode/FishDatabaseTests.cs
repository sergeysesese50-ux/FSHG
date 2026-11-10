using System;
using System.Collections.Generic;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public class FishDatabaseTests
    {
        [Test]
        public void RealFishJsonIsValid()
        {
            var database = TestFish.RealDatabase();

            Assert.That(database.All.Count, Is.GreaterThanOrEqualTo(5));
            Assert.AreEqual("pike", database.Get("pike").id);
        }

        [Test]
        public void UnknownIdThrows()
        {
            var database = TestFish.Database(TestFish.Species());
            Assert.Throws<KeyNotFoundException>(() => database.Get("shark"));
            Assert.IsFalse(database.TryGet("shark", out _));
        }

        [Test]
        public void WrongVersionThrows()
        {
            var file = TestFish.File(TestFish.Species());
            file.version = 1;
            Assert.Throws<FormatException>(() => new FishDatabase(file));
        }

        [Test]
        public void DuplicateIdThrows()
        {
            Assert.Throws<FormatException>(() => TestFish.Database(TestFish.Species("a"), TestFish.Species("a")));
        }

        [Test]
        public void WeightsOutOfOrderThrow()
        {
            var species = TestFish.Species();
            species.weightG.typical = species.weightG.trophy;
            Assert.Throws<FormatException>(() => TestFish.Database(species));
        }

        [Test]
        public void QualifyingWeightMustSitBetweenTypicalAndTrophy()
        {
            var low = TestFish.Species();
            low.weightG.qualifying = low.weightG.typical - 1;
            Assert.Throws<FormatException>(() => TestFish.Database(low));

            var high = TestFish.Species();
            high.weightG.qualifying = high.weightG.trophy + 1;
            Assert.Throws<FormatException>(() => TestFish.Database(high));

            var missing = TestFish.Species();
            missing.weightG.qualifying = 0;
            Assert.Throws<FormatException>(() => TestFish.Database(missing), "поле не задано в данных");
        }

        [Test]
        public void QualifyingWeightsAreTakenFromRussianFishing3()
        {
            var db = TestFish.RealDatabase();
            Assert.AreEqual(450, db.Get("roach").weightG.qualifying);
            Assert.AreEqual(650, db.Get("crucian").weightG.qualifying);
            Assert.AreEqual(650, db.Get("perch").weightG.qualifying);
            Assert.AreEqual(2000, db.Get("bream").weightG.qualifying);
            Assert.AreEqual(3500, db.Get("zander").weightG.qualifying);
            Assert.AreEqual(2000, db.Get("pike").weightG.qualifying);
        }

        [Test]
        public void HourOutOfRangeThrows()
        {
            var species = TestFish.Species();
            species.activeHours = new[] { 24 };
            Assert.Throws<FormatException>(() => TestFish.Database(species));
        }

        [Test]
        public void UnknownFightStyleThrows()
        {
            var species = TestFish.Species();
            species.fightStyle = "dance";
            Assert.Throws<FormatException>(() => TestFish.Database(species));
        }
    }
}
