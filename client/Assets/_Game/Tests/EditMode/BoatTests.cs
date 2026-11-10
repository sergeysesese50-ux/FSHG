using System;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class BoatTests
    {
        private static FishSpecies Roach => TestFish.Species("roach");

        private static BoatData Boat(int price = 300, int rentPrice = 40, int rentMinutes = 30)
        {
            return new BoatData { id = "rowboat", price = price, rentPrice = rentPrice, rentMinutes = rentMinutes };
        }

        /// <summary>Профиль с 500 монетами: продана рыба 5 кг по 100 за кг.</summary>
        private static PlayerProfile RichProfile()
        {
            var db = TestFish.Database(Roach);
            var profile = new PlayerProfile();
            profile.Keep(Roach, 5000, 60, false, 0);
            profile.SellAll(db);
            Assert.AreEqual(500, profile.Money);
            return profile;
        }

        [Test]
        public void BoughtBoatIsForever()
        {
            var profile = RichProfile();
            Assert.IsFalse(profile.HasBoat);
            Assert.IsTrue(profile.BuyBoat(Boat()));
            Assert.AreEqual(200, profile.Money);
            Assert.IsTrue(profile.OwnsBoat);
            Assert.IsTrue(profile.HasBoat);
            Assert.IsFalse(profile.TickBoatRent(100000), "своя лодка не кончается");
            Assert.IsTrue(profile.HasBoat);

            Assert.IsFalse(profile.BuyBoat(Boat()), "вторую не продают");
            Assert.IsFalse(profile.RentBoat(Boat()), "своя есть — аренда ни к чему");
            Assert.AreEqual(200, profile.Money);
        }

        [Test]
        public void NotEnoughMoneyMeansNoBoat()
        {
            var profile = new PlayerProfile();
            Assert.IsFalse(profile.BuyBoat(Boat()));
            Assert.IsFalse(profile.RentBoat(Boat()));
            Assert.IsFalse(profile.HasBoat);
            Assert.AreEqual(0, profile.Money);
        }

        [Test]
        public void RentedBoatLastsForItsTime()
        {
            var profile = RichProfile();
            Assert.IsTrue(profile.RentBoat(Boat()));
            Assert.AreEqual(460, profile.Money);
            Assert.IsTrue(profile.HasBoat);
            Assert.IsFalse(profile.OwnsBoat);
            Assert.AreEqual(1800.0, profile.BoatRentSecondsLeft, 1e-9);

            Assert.IsFalse(profile.TickBoatRent(1799.5), "ещё полсекунды");
            Assert.IsTrue(profile.HasBoat);
            Assert.IsTrue(profile.TickBoatRent(1.0), "аренда кончилась именно сейчас");
            Assert.IsFalse(profile.HasBoat);
            Assert.AreEqual(0.0, profile.BoatRentSecondsLeft);
            Assert.IsFalse(profile.TickBoatRent(5.0), "второй раз о конце не сообщается");
        }

        [Test]
        public void RentingAgainExtendsTheTimeUpToADay()
        {
            var profile = RichProfile();
            Assert.IsTrue(profile.RentBoat(Boat()));
            profile.TickBoatRent(600);
            Assert.IsTrue(profile.RentBoat(Boat()));
            Assert.AreEqual(1200.0 + 1800.0, profile.BoatRentSecondsLeft, 1e-9);
            Assert.AreEqual(420, profile.Money);

            var day = Boat(rentPrice: 1, rentMinutes: 24 * 60);
            Assert.IsFalse(profile.RentBoat(day), "больше суток не накопить");
            Assert.AreEqual(420, profile.Money);
        }

        [Test]
        public void BuyingEndsTheRent()
        {
            var profile = RichProfile();
            profile.RentBoat(Boat());
            Assert.IsTrue(profile.BuyBoat(Boat()));
            Assert.AreEqual(0.0, profile.BoatRentSecondsLeft);
            Assert.IsTrue(profile.HasBoat);
        }

        [Test]
        public void BoatSurvivesSaveAndLoad()
        {
            var db = TestFish.Database(Roach);
            var renter = RichProfile();
            renter.RentBoat(Boat());
            renter.TickBoatRent(100.4);
            var restored = PlayerProfile.FromState(renter.ToState(), db);
            Assert.IsFalse(restored.OwnsBoat);
            Assert.AreEqual(1700.0, restored.BoatRentSecondsLeft, 1e-9, "остаток сохраняется целыми секундами, вверх");

            var owner = RichProfile();
            owner.BuyBoat(Boat());
            Assert.IsTrue(PlayerProfile.FromState(owner.ToState(), db).OwnsBoat);

            Assert.IsFalse(PlayerProfile.FromState(new ProfileState { version = 3 }, db).HasBoat, "в старом сохранении лодки нет");
            var broken = PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion, boatRentSeconds = -50 }, db);
            Assert.IsFalse(broken.HasBoat);
        }

        [Test]
        public void RentedBoatOpensWaterSpotsWhileItLasts()
        {
            var island = new LakeSpot { id = "island", minLevel = 1, needsBoat = true };
            var profile = RichProfile();
            Assert.AreEqual(SpotAccess.NeedsBoat, island.AccessFor(profile.Level, profile.HasBoat));
            profile.RentBoat(Boat());
            Assert.AreEqual(SpotAccess.Open, island.AccessFor(profile.Level, profile.HasBoat));
            profile.TickBoatRent(1800);
            Assert.AreEqual(SpotAccess.NeedsBoat, island.AccessFor(profile.Level, profile.HasBoat));
        }

        [Test]
        public void BoatIsUsefulFromTheLowestWaterSpotLevel()
        {
            var lake = new LakeLocation
            {
                id = "lake",
                spots = new[]
                {
                    new LakeSpot { id = "maple", minLevel = 1 },
                    new LakeSpot { id = "center", minLevel = 8, needsBoat = true },
                    new LakeSpot { id = "island", minLevel = 6, needsBoat = true },
                },
            };
            Assert.AreEqual(6, lake.BoatUsefulFromLevel(), "раньше шестого уровня плыть некуда — аренду не дают");

            var shoreOnly = new LakeLocation { id = "pond", spots = new[] { new LakeSpot { id = "a", minLevel = 1 } } };
            Assert.AreEqual(0, shoreOnly.BoatUsefulFromLevel(), "мест на воде нет");
            Assert.AreEqual(0, new LakeLocation { id = "empty" }.BoatUsefulFromLevel());
        }

        [Test]
        public void BadTransportDataIsRejected()
        {
            TransportFile File(params BoatData[] boats)
            {
                return new TransportFile { version = TransportDatabase.SupportedVersion, boats = boats };
            }

            Assert.AreEqual(1, new TransportDatabase(File(Boat())).Boats.Count);
            Assert.Throws<FormatException>(() => new TransportDatabase(new TransportFile { version = 99, boats = new[] { Boat() } }));
            Assert.Throws<FormatException>(() => new TransportDatabase(File()), "без лодок");
            Assert.Throws<FormatException>(() => new TransportDatabase(File(Boat(), Boat())), "лодка дважды");
            Assert.Throws<FormatException>(() => new TransportDatabase(File(Boat(price: 0))));
            Assert.Throws<FormatException>(() => new TransportDatabase(File(Boat(rentPrice: 0))));
            Assert.Throws<FormatException>(() => new TransportDatabase(File(Boat(price: 40, rentPrice: 40))), "аренда не дешевле покупки");
            Assert.Throws<FormatException>(() => new TransportDatabase(File(Boat(rentMinutes: 0))));
        }

        [Test]
        public void RealTransportHasABoat()
        {
            var boats = TestFish.RealTransport().Boats;
            Assert.Greater(boats.Count, 0);
            Assert.Greater(boats[0].price, boats[0].rentPrice);
        }
    }
}
