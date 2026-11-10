using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    /// <summary>
    /// Полосы баланса вываживания (fight-v2, §3.7): каждая — 200 seed одного бота на одной рыбе.
    /// Долгие, поэтому в категории Balance. Те же полосы печатает симулятор баланса (FightBands.Run).
    /// Полосы с FightBand.Deviation проверяют задокументированное отклонение, а не цель спецификации, и падают,
    /// если результат уже попал в цель спецификации (отклонение пора убрать). Вываживание, не кончившееся
    /// за FightBands.FightLimitSeconds, роняет любую полосу.
    /// </summary>
    [Category("Balance")]
    public class FightBalanceTests
    {
        [Test] public void RoachHoldReel() => Check(0);
        [Test] public void PerchHoldReel() => Check(1);
        [Test] public void PerchLiftReleaseReel() => Check(2);
        [Test] public void BreamHoldReel() => Check(3);
        [Test] public void BreamStopOnClicks() => Check(4);
        [Test] public void BreamTechnique() => Check(5);
        [Test] public void Pike4HoldReel() => Check(6);
        [Test] public void Pike4StopOnClicks() => Check(7);
        [Test] public void Pike4Technique() => Check(8);
        [Test] public void Pike7Line020TechniqueDeviation() => Check(9);
        [Test] public void Pike7Line020TechniqueLateDeviation() => Check(10);
        [Test] public void Pike7Line020HoldReel() => Check(11);
        [Test] public void Pike7Line030Technique() => Check(12);
        [Test] public void Pike12FloatTechnique() => Check(13);
        [Test] public void Pike12FloatHoldReel() => Check(14);

        [Test]
        public void BreamHoldReelBreaksByTwist()
        {
            var result = FightBands.Run(FightBands.All()[3]);

            Assert.That(result.Causes[(int)FightCause.WornByCranking], Is.GreaterThanOrEqualTo(result.Runs * 8 / 10),
                "лещ на «просто кручу» должен рваться от перекрута: " + result);
        }

        private static void Check(int index)
        {
            var band = FightBands.All()[index];
            var result = FightBands.Run(band);
            string note = band.Deviation != null ? $" Отклонение от спецификации: {band.Deviation}." : "";
            Assert.IsTrue(result.Passed, result + note);
            Assert.IsFalse(result.DeviationStale,
                result + " — полоса уже в цели спецификации: убери отклонение в FightBands.All().");
        }
    }

    /// <summary>Правила проверки полос (быстрые, без прогонов).</summary>
    public class FightBandRulesTests
    {
        [Test]
        public void StuckFightsFailTheBand()
        {
            var band = new FightBand { MinLanded = 0.0, MaxLanded = 0.10 };
            var result = new FightBandResult { Band = band, Runs = 10, Landed = 0 };
            Assert.IsTrue(result.Passed);

            result.Outcomes[(int)FightOutcome.InProgress] = 1;
            Assert.IsFalse(result.Passed, "вываживание, не кончившееся за предел времени, — не «не поймал», а тупик");
        }

        [Test]
        public void DeviationBandFlagsResultThatMeetsTheSpec()
        {
            var band = new FightBand { MinLanded = 0.20, MaxLanded = 0.40, Deviation = "проверка", DeviationMinLanded = 0.90 };

            var asDocumented = new FightBandResult { Band = band, Runs = 10, Landed = 10 };
            Assert.IsTrue(asDocumented.Passed);
            Assert.IsFalse(asDocumented.DeviationStale);

            var meetsSpec = new FightBandResult { Band = band, Runs = 10, Landed = 3 };
            Assert.IsFalse(meetsSpec.Passed);
            Assert.IsTrue(meetsSpec.DeviationStale, "результат в цели спецификации — отклонение пора убрать");
        }
    }
}
