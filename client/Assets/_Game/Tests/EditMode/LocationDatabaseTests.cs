using System;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class LocationDatabaseTests
    {
        private static FishDatabase Fish() => TestFish.Database(TestFish.Species("roach"), TestFish.Species("pike"));

        private static LakeSpot Spot(string id = "maple", int shore = 40, int slope = 8, int max = 250, params string[] fish)
        {
            return new LakeSpot { id = id, minLevel = 1, shoreDepthCm = shore, slopeCmPerM = slope, maxDepthCm = max, fish = fish.Length > 0 ? fish : new[] { "roach" } };
        }

        private static LocationFile File(params LakeSpot[] spots)
        {
            return new LocationFile
            {
                version = LocationDatabase.SupportedVersion,
                locations = new[] { new LakeLocation { id = "lake", unlockPrice = 0, spots = spots } },
            };
        }

        [Test]
        public void ValidFileGivesSpotsInOrder()
        {
            var db = new LocationDatabase(File(Spot("maple"), Spot("pit", 120, 24, 650, "pike")), Fish());
            Assert.AreEqual(2, db.Get("lake").spots.Length);
            Assert.AreEqual("maple", db.Get("lake").spots[0].id);
            Assert.AreEqual("pit", db.FindSpot("lake", "pit").id);
            Assert.IsNull(db.FindSpot("lake", "nowhere"));
            Assert.IsNull(db.FindSpot("lake", null));
            Assert.IsNull(db.FindSpot("sea", "pit"));
        }

        [Test]
        public void DepthGrowsFromShoreAndStopsAtMax()
        {
            var spot = Spot("maple", 40, 8, 250);
            Assert.AreEqual(40f, spot.DepthCm(0f));
            Assert.AreEqual(40f, spot.DepthCm(-3f), "на берегу — глубина у кромки");
            Assert.AreEqual(120f, spot.DepthCm(10f));
            Assert.AreEqual(250f, spot.DepthCm(100f));
        }

        [Test]
        public void BrokenFilesAreRejected()
        {
            var fish = Fish();
            Assert.Throws<FormatException>(() => new LocationDatabase(new LocationFile { version = 1 }, fish));
            Assert.Throws<FormatException>(() => new LocationDatabase(File(), fish), "без мест");
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a"), Spot("a")), fish), "место дважды");
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", 0)), fish), "глубина у берега 0");
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", 40, 0)), fish), "уклон 0");
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", 300, 8, 250)), fish), "предел мельче берега");
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", 40, 8, 250, "shark")), fish), "неизвестная рыба");
        }

        [Test]
        public void ProfileRemembersSpot()
        {
            var fish = Fish();
            var profile = new PlayerProfile { SpotId = "pit" };
            var restored = PlayerProfile.FromState(profile.ToState(), fish);
            Assert.AreEqual("pit", restored.SpotId);
            Assert.IsNull(PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion }, fish).SpotId);
        }

        [Test]
        public void RealLocationsAreValid()
        {
            Assert.GreaterOrEqual(TestFish.RealLocations().Get("lake_start").spots.Length, 5);
        }
    }
}
