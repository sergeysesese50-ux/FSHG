using System;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class LevelTests
    {
        // TestFish.Species: обычный вес 300 г, зачётный 600 г, трофей от 1000 г, цена 100 за кг.
        private static FishSpecies Roach => TestFish.Species("roach");
        private static FishSpecies Pike => TestFish.Species("pike");

        [Test]
        public void LevelsFollowThresholds()
        {
            Assert.AreEqual(1, LevelRules.LevelFor(0));
            Assert.AreEqual(1, LevelRules.LevelFor(-5));
            Assert.AreEqual(1, LevelRules.LevelFor(99));
            Assert.AreEqual(2, LevelRules.LevelFor(100));
            Assert.AreEqual(3, LevelRules.LevelFor(200));
            Assert.AreEqual(4, LevelRules.LevelFor(599));
            Assert.AreEqual(5, LevelRules.LevelFor(600));
            Assert.AreEqual(10, LevelRules.LevelFor(3700));
            Assert.AreEqual(LevelRules.MaxLevel, LevelRules.LevelFor(long.MaxValue));

            for (int level = 2; level <= LevelRules.MaxLevel; level++)
            {
                Assert.Greater(LevelRules.XpForLevel(level), LevelRules.XpForLevel(level - 1), "порог уровня " + level);
                Assert.AreEqual(level, LevelRules.LevelFor(LevelRules.XpForLevel(level)));
                Assert.AreEqual(level - 1, LevelRules.LevelFor(LevelRules.XpForLevel(level) - 1));
            }
        }

        [Test]
        public void ProgressShowsWayToNextLevel()
        {
            LevelRules.Progress(250, out int level, out long into, out long span);
            Assert.AreEqual(3, level);
            Assert.AreEqual(50, into);
            Assert.AreEqual(150, span, "от 200 до 350");

            LevelRules.Progress(LevelRules.XpForLevel(LevelRules.MaxLevel) + 7, out level, out into, out span);
            Assert.AreEqual(LevelRules.MaxLevel, level);
            Assert.AreEqual(0, span, "выше некуда");
        }

        [Test]
        public void RanksGroupLevels()
        {
            Assert.AreEqual(PlayerRank.Novice, LevelRules.RankFor(1));
            Assert.AreEqual(PlayerRank.Novice, LevelRules.RankFor(4));
            Assert.AreEqual(PlayerRank.Amateur, LevelRules.RankFor(5));
            Assert.AreEqual(PlayerRank.Amateur, LevelRules.RankFor(9));
            Assert.AreEqual(PlayerRank.Angler, LevelRules.RankFor(10));
            Assert.AreEqual(PlayerRank.Experienced, LevelRules.RankFor(15));
            Assert.AreEqual(PlayerRank.Master, LevelRules.RankFor(20));
            Assert.AreEqual(PlayerRank.Master, LevelRules.RankFor(LevelRules.MaxLevel));
        }

        [Test]
        public void BetterFishGivesMoreExperience()
        {
            var none = default(CatchMarks);
            Assert.AreEqual(30, LevelRules.ForCatch(Roach, 300, false, none, 0).Total, "обычная: 300 г × 100 за кг");
            Assert.AreEqual(59, LevelRules.ForCatch(Roach, 599, false, none, 0).Total);
            Assert.AreEqual(180, LevelRules.ForCatch(Roach, 600, false, none, 0).Total, "зачётная — втрое");
            Assert.AreEqual(720, LevelRules.ForCatch(Roach, 1200, true, none, 0).Total, "трофей — вшестеро");
            Assert.AreEqual(10, LevelRules.ForCatch(Roach, 100, false, none, 0).Total, "малёк — без множителя");
            Assert.AreEqual(1, LevelRules.ForCatch(Roach, 3, false, none, 0).Total, "не меньше 1");
            Assert.AreEqual(0, LevelRules.BaseXp(Roach, 0));
        }

        [Test]
        public void FirstFishAndRecordsAddBonus()
        {
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 300, 25, false);
            Assert.AreEqual(30 + LevelRules.FirstOfSpeciesBonus, profile.Xp, "первая рыба вида");
            Assert.AreEqual(LevelRules.FirstOfSpeciesBonus, profile.LastXpGain.Bonus);

            profile.RecordCatch(Roach, 400, 27, false);
            Assert.AreEqual(40 + LevelRules.SpeciesRecordBonus, profile.LastXpGain.Total, "личный рекорд вида");

            profile.RecordCatch(Roach, 200, 22, false);
            Assert.AreEqual(20, profile.LastXpGain.Total, "ни первой, ни рекорда");
            Assert.AreEqual(80 + 55 + 20, profile.Xp);
            Assert.AreEqual(3, profile.XpGainCount);
        }

        [Test]
        public void ExperienceRaisesLevelAndRank()
        {
            var profile = new PlayerProfile();
            Assert.AreEqual(1, profile.Level);
            Assert.AreEqual(PlayerRank.Novice, profile.Rank);

            profile.RecordCatch(Roach, 300, 25, false);
            Assert.IsFalse(profile.LastXpGain.LeveledUp, "80 опыта — ещё первый уровень");

            // Трофейная щука: 2 кг × 100 × 6 = 1200, плюс первая рыба вида.
            profile.RecordCatch(Pike, 2000, 70, true);
            Assert.AreEqual(80 + 1250, profile.Xp);
            Assert.IsTrue(profile.LastXpGain.LeveledUp);
            Assert.AreEqual(1, profile.LastXpGain.LevelBefore);
            Assert.AreEqual(7, profile.LastXpGain.LevelAfter);
            Assert.AreEqual(7, profile.Level);
            Assert.AreEqual(PlayerRank.Amateur, profile.Rank);
        }

        [Test]
        public void ReleasedFishStillCounts()
        {
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 300, 25, false);
            long xp = profile.Xp;
            profile.ReleaseCatch(Roach, 300, false);
            Assert.AreEqual(xp, profile.Xp, "опыт дан за поимку и при отпускании не пропадает");
            Assert.Greater(xp, 0);
        }

        [Test]
        public void ExperienceSurvivesSaveAndLoad()
        {
            var db = TestFish.Database(Roach);
            var profile = new PlayerProfile();
            profile.RecordCatch(Roach, 900, 35, false);
            var restored = PlayerProfile.FromState(profile.ToState(), db);
            Assert.AreEqual(profile.Xp, restored.Xp);
            Assert.AreEqual(profile.Level, restored.Level);

            var old = PlayerProfile.FromState(new ProfileState { version = 3, xp = 5000 }, db);
            Assert.AreEqual(0, old.Xp, "в версии 3 опыта не было");
            Assert.AreEqual(1, old.Level);
            var broken = PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion, xp = -10 }, db);
            Assert.AreEqual(0, broken.Xp);
        }

        [Test]
        public void SpotsNeedALevelAndOneIsOpenFromTheStart()
        {
            var fish = TestFish.Database(Roach);

            LakeSpot Spot(string id, int minLevel)
            {
                return new LakeSpot { id = id, minLevel = minLevel, shoreDepthCm = 40, slopeCmPerM = 8, maxDepthCm = 250, fish = new[] { "roach" } };
            }

            LocationFile File(params LakeSpot[] spots)
            {
                return new LocationFile
                {
                    version = LocationDatabase.SupportedVersion,
                    locations = new[] { new LakeLocation { id = "lake", spots = spots } },
                };
            }

            Assert.AreEqual(5, new LocationDatabase(File(Spot("a", 1), Spot("b", 5)), fish).FindSpot("lake", "b").minLevel);
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", 0)), fish), "уровень не задан");
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", LevelRules.MaxLevel + 1), Spot("b", 1)), fish));
            Assert.Throws<FormatException>(() => new LocationDatabase(File(Spot("a", 2), Spot("b", 3)), fish), "новичку некуда пойти");
        }

        [Test]
        public void WaterSpotsNeedABoatAsWellAsALevel()
        {
            var island = new LakeSpot { id = "island", minLevel = 6, needsBoat = true };
            Assert.AreEqual(SpotAccess.NeedsLevel, island.AccessFor(5, false));
            Assert.AreEqual(SpotAccess.NeedsLevel, island.AccessFor(5, true), "лодка без уровня не поможет");
            Assert.AreEqual(SpotAccess.NeedsBoat, island.AccessFor(6, false));
            Assert.AreEqual(SpotAccess.Open, island.AccessFor(6, true));

            var shore = new LakeSpot { id = "maple", minLevel = 1 };
            Assert.AreEqual(SpotAccess.Open, shore.AccessFor(1, false));
        }

        [Test]
        public void StartingSpotMustBeReachableOnFoot()
        {
            var fish = TestFish.Database(Roach);
            var onlyByBoat = new LocationFile
            {
                version = LocationDatabase.SupportedVersion,
                locations = new[]
                {
                    new LakeLocation
                    {
                        id = "lake",
                        spots = new[]
                        {
                            new LakeSpot { id = "a", minLevel = 1, needsBoat = true, shoreDepthCm = 40, slopeCmPerM = 8, maxDepthCm = 250, fish = new[] { "roach" } },
                        },
                    },
                },
            };
            Assert.Throws<FormatException>(() => new LocationDatabase(onlyByBoat, fish));
        }

        [Test]
        public void RealLakeHasAnOpenSpotAndLockedOnes()
        {
            var spots = TestFish.RealLocations().Get("lake_start").spots;
            Assert.AreEqual(1, spots[0].minLevel, "первое место открыто сразу");
            int locked = 0;
            foreach (var spot in spots)
            {
                if (spot.minLevel > 1)
                    locked++;
            }

            Assert.Greater(locked, 0, "есть ради чего расти");

            var center = TestFish.RealLocations().FindSpot("lake_start", "center");
            Assert.IsNotNull(center);
            Assert.IsTrue(center.needsBoat, "в центр озера — только на лодке");
            Assert.Greater(center.minLevel, 1);
        }
    }
}
