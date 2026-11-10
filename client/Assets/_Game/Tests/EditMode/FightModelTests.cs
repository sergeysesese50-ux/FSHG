using System;
using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    /// <summary>
    /// Сценарии вываживания v2 (docs/specs/fight-v2.md, §3.7). Снасти, рыбы и боты — в FightBots.cs:
    /// числа заданы там явно, чтобы тесты проверяли модель, а не текущий tackle.json. Полосы баланса на
    /// 200 seed — в FightBalanceTests (категория Balance).
    /// </summary>
    public class FightModelTests
    {
        private const double Tick = 0.05;
        private static readonly double[] LeadSeconds = { 0.3, 0.5, 0.5, 0.5, 0.6, 0.6 };

        private static int FloatDrag => FightKit.FloatTackle().AutoDragG;
        private static int Drag020 => FightKit.SpinningTackle(4000).AutoDragG;

        // ---------- Прежние сценарии ----------

        [Test]
        public void SmallRoachLandsByJustReeling()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle());

            Assert.AreEqual(FightOutcome.Landed, FightBots.Play(fight, FightBots.HoldReel(FloatDrag)));
            Assert.That(fight.ElapsedSeconds, Is.LessThan(30));
            Assert.AreEqual(FightLanding.Swung, fight.Report.Landing);
        }

        [Test]
        public void HugePikeOnFloatTackleBreaksLine()
        {
            var fight = FightKit.Fight(FightKit.Pike(), 12000, FightKit.FloatTackle());

            Assert.AreEqual(FightOutcome.LineBroke, FightBots.Play(fight, FightBots.HoldReel(3000)));
            Assert.AreEqual(FightCause.Overload, fight.Report.Cause);
            Assert.That((fight.Events & FightEvents.Broke) != 0);
        }

        [Test]
        public void LockedDragAndLiftingBreaksRod()
        {
            var fight = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(8000));

            Assert.AreEqual(FightOutcome.RodBroke, FightBots.Play(fight, FightBots.ReelAndLift(6000)));
            Assert.AreEqual(FightCause.RodHighTightDrag, fight.Report.Cause);
            Assert.That(fight.Report.PeakG, Is.GreaterThan(fight.Report.LimitG));
        }

        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(4u)]
        [TestCase(5u)]
        public void CarefulPumpingLandsFourKiloPike(uint seed)
        {
            var fight = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), seed, startCm: 3000);

            Assert.AreEqual(FightOutcome.Landed, FightBots.Play(fight, FightBots.Technique(Drag020)));
        }

        [Test]
        public void ReelingIntoSlippingDragWearsLineUntilItBreaks()
        {
            var mindless = FightKit.Fight(FightKit.Bream(), 2000, FightKit.FloatTackle(), startCm: 1500);
            var careful = FightKit.Fight(FightKit.Bream(), 2000, FightKit.FloatTackle(), startCm: 1500);

            Assert.AreEqual(FightOutcome.LineBroke, FightBots.Play(mindless, FightBots.HoldReel(FloatDrag)));
            Assert.AreEqual(FightCause.WornByCranking, mindless.Report.Cause);
            Assert.That(mindless.LineWear, Is.GreaterThan(0.15));
            Assert.That(mindless.Report.SecondsCrankedOnSlip, Is.GreaterThan(7.5));
            Assert.AreEqual(FightOutcome.Landed, FightBots.Play(careful, FightBots.Technique(FloatDrag)));
        }

        [Test]
        public void SlackLineLetsFishEscape()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle(), slackCm: 5000);

            Assert.AreEqual(FightOutcome.Escaped, FightBots.Play(fight, FightBots.DoNothing(FloatDrag)));
            Assert.That(fight.ElapsedSeconds, Is.InRange(2.5, 3.5));
            Assert.AreEqual(FightCause.SlackShaking, fight.Report.Cause);
        }

        [Test]
        public void ZeroDragLetsFishEmptyTheSpool()
        {
            var steadyPike = FightKit.Species("pike", 8, 7, "steady");
            var fight = FightKit.Fight(steadyPike, 4000, FightKit.SpinningTackle(8000));

            Assert.AreEqual(FightOutcome.SpoolEmpty, FightBots.Play(fight, FightBots.DoNothing(0)));
            Assert.AreEqual(FightCause.SpoolEmptyDragLoose, fight.Report.Cause);
        }

        [Test]
        public void SameSeedGivesSameFight()
        {
            var a = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 7, startCm: 3000);
            var b = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 7, startCm: 3000);

            FightBots.Play(a, FightBots.Technique(Drag020));
            FightBots.Play(b, FightBots.Technique(Drag020));

            Assert.AreEqual(a.Outcome, b.Outcome);
            Assert.AreEqual(a.Tick, b.Tick);
            Assert.AreEqual(a.ElapsedSeconds, b.ElapsedSeconds);
            Assert.AreEqual(a.LineOutCm, b.LineOutCm);
            AssertSameReport(a.Report, b.Report);
        }

        [Test]
        public void DragCapsTension()
        {
            const int drag = 900;
            var fight = FightKit.Fight(FightKit.Bream(), 2000, FightKit.FloatTackle());
            int slipping = 0;

            while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 120)
            {
                fight.Step(new FightInput { Reel = true, DragG = drag });
                Assert.That(fight.TensionG, Is.LessThanOrEqualTo(1.2 * drag + 1e-9));
                if (fight.IsDragSlipping)
                {
                    slipping++;
                    Assert.AreEqual(drag, fight.TensionG, 1e-9);
                }
            }

            Assert.That(slipping, Is.GreaterThan(0), "лещ 2 кг должен срывать фрикцион 900 г");
        }

        [Test]
        public void RaisedRodCarriesMoreLoadThanLine()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle());
            for (int i = 0; i < 10; i++)
                fight.Step(new FightInput { Lift = true, DragG = 1000 });

            Assert.That(fight.RodAngle, Is.GreaterThan(0));
            Assert.That(fight.TensionG, Is.GreaterThan(0));
            Assert.That(fight.RodLoadG, Is.GreaterThan(fight.TensionG));
        }

        [Test]
        public void FishTiresUnderTension()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle());
            for (int i = 0; i < 100; i++)
                fight.Step(new FightInput { DragG = 1000 });

            Assert.That(fight.Stamina, Is.LessThan(1.0));
        }

        // ---------- Начало, данные, отчёт ----------

        [Test]
        public void FightOpensWithShortRestAndRunAnnounced()
        {
            var tackle = FightKit.FloatTackle();
            var fight = FightKit.Fight(FightKit.Perch(), 300, tackle, startCm: 1500, slackCm: 40);

            Assert.AreEqual(FishPhase.Rest, fight.Phase);
            Assert.AreEqual(FishPhase.Run, fight.NextPhase);
            Assert.AreEqual(0.8, fight.NextPhaseInSeconds, 1e-9);
            Assert.AreEqual(0.5, fight.RodAngle, 1e-12);
            Assert.AreEqual(1500 + 0.5 * tackle.RodReachCm + 40, fight.LineOutCm, 1e-9);
            Assert.AreEqual(40, fight.SlackCm, 1e-9);
            Assert.AreEqual(0, fight.Tick);

            fight.Step(new FightInput { DragG = FloatDrag });
            Assert.That((fight.Events & FightEvents.HookSet) != 0);
            Assert.AreEqual(1, fight.Tick);

            for (int i = 1; i < 16; i++)
                fight.Step(new FightInput { DragG = FloatDrag });
            Assert.AreEqual(FishPhase.Run, fight.Phase);
            Assert.That((fight.Events & FightEvents.PhaseStart) != 0);
        }

        [Test]
        public void ConstructorDoesNotTouchRandom()
        {
            var random = new SeededRandom(5);
            new FightModel(FightKit.Pike(), 4000, FightKit.SpinningTackle(), 3000, random);

            Assert.AreEqual(new SeededRandom(5).State, random.State);
            Assert.AreEqual(2, FightModel.ReplayVersion);
        }

        [Test]
        public void AutoDragMatchesSpec()
        {
            Assert.AreEqual(1050, FightKit.FloatTackle().AutoDragG);
            Assert.AreEqual(2400, FightKit.SpinningTackle(4000).AutoDragG);
            Assert.AreEqual(2750, FightKit.SpinningTackle(8000).AutoDragG);
        }

        [Test]
        public void ReportKeepsLast200Ticks()
        {
            var fight = FightKit.Fight(FightKit.Bream(), 2000, FightKit.FloatTackle());
            for (int i = 0; i < 300 && fight.Outcome == FightOutcome.InProgress; i++)
                fight.Step(new FightInput { Reel = true, DragG = FloatDrag });

            var report = fight.Report;
            Assert.AreEqual(FightReport.HistoryCapacity, report.HistoryCount);
            var last = report.History(report.HistoryCount - 1);
            Assert.AreEqual(fight.TensionG, last.TensionG, 1e-12);
            Assert.AreEqual(fight.PeakTensionG, last.PeakG, 1e-12);
            Assert.AreEqual(fight.WeakestLinkG, last.LimitG, 1e-12);
            Assert.AreEqual(FloatDrag, last.DragG, 1e-12);
            Assert.That(report.RestsTotal, Is.GreaterThanOrEqualTo(1));
        }

        // ---------- Новые проверки ----------

        /// <summary>Каждая фаза, кроме броска от удара подсачеком, видна в NextPhase не позже, чем за lead − шаг.</summary>
        [Test]
        public void NextPhaseIsAnnouncedAtLeastLeadAhead()
        {
            var species = new Func<FishSpecies>[] { FightKit.Roach, FightKit.Perch, FightKit.Bream, FightKit.Pike };
            var weights = new[] { 300, 400, 2000, 4000 };
            int checkedStarts = 0;
            for (int s = 0; s < species.Length; s++)
            {
                for (uint seed = 1; seed <= 8; seed++)
                {
                    foreach (var makeBot in new Func<int, FightBot>[] { d => FightBots.Technique(d), d => FightBots.HoldReel(d), d => FightBots.LiftReleaseReel(d) })
                    {
                        var tackle = weights[s] > 1000 ? FightKit.SpinningTackle(4000) : FightKit.FloatTackle();
                        var fight = FightKit.Fight(species[s](), weights[s], tackle, seed, startCm: 1500);
                        var bot = makeBot(tackle.AutoDragG);
                        var announced = fight.NextPhase;
                        int since = fight.Tick;
                        while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 120)
                        {
                            if (fight.NextPhase != announced)
                            {
                                announced = fight.NextPhase;
                                since = fight.Tick;
                            }

                            fight.Step(bot(fight));
                            var events = fight.Events;
                            if ((events & FightEvents.PhaseStart) == 0 || (events & FightEvents.NetBump) != 0)
                                continue;
                            double shown = (fight.Tick - since) * Tick;
                            Assert.AreEqual(announced, fight.Phase, $"seed {seed}, тик {fight.Tick}");
                            Assert.That(shown, Is.GreaterThanOrEqualTo(LeadSeconds[(int)fight.Phase] - Tick - 1e-9),
                                $"{fight.Phase} объявлена за {shown} с (seed {seed}, тик {fight.Tick})");
                            checkedStarts++;
                        }
                    }
                }
            }

            Assert.That(checkedStarts, Is.GreaterThan(500));
        }

        /// <summary>Без кнопок, вне свечки и её телеграфа, слабина никогда не растёт.</summary>
        [Test]
        public void NoButtonsNeverAddSlack()
        {
            var species = new Func<FishSpecies>[] { FightKit.Roach, FightKit.Perch, FightKit.Bream };
            var weights = new[] { 300, 400, 2000 };
            int idleTicks = 0;
            for (int s = 0; s < species.Length; s++)
            {
                for (uint seed = 1; seed <= 6; seed++)
                {
                    var fight = FightKit.Fight(species[s](), weights[s], FightKit.FloatTackle(), seed, startCm: 1500, slackCm: 20 * seed);
                    var buttons = new SeededRandom(1000 + seed);
                    var input = new FightInput { DragG = FloatDrag };
                    while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 120)
                    {
                        if (buttons.NextDouble() < 0.1)
                        {
                            double roll = buttons.NextDouble();
                            input.Reel = roll >= 0.4 && roll < 0.8;
                            input.Lift = roll >= 0.65;
                        }

                        bool jumpAhead = fight.Phase == FishPhase.Jump
                                         || (fight.NextPhase == FishPhase.Jump && fight.TelegraphProgress > 0);
                        double before = fight.SlackCm;
                        fight.Step(input);
                        if (input.Reel || input.Lift || jumpAhead || fight.Phase == FishPhase.Jump)
                            continue;
                        idleTicks++;
                        Assert.That(fight.SlackCm, Is.LessThanOrEqualTo(before + 1e-9), $"seed {seed}, тик {fight.Tick}");
                    }
                }
            }

            Assert.That(idleTicks, Is.GreaterThan(1000));
        }

        /// <summary>Статический решатель: чем сильнее тянет рыба, тем медленнее подмотка, но не ноль, пока фрикцион держит.</summary>
        [Test]
        public void ReelInNeverRisesWithPull()
        {
            foreach (var tackle in new[] { FightKit.FloatTackle(), FightKit.SpinningTackle(4000) })
            {
                foreach (double bodyDrag in new[] { 1.0, 15.6, 28.9 })
                {
                    foreach (bool wasSlipping in new[] { false, true })
                    {
                        double drag = tackle.AutoDragG;
                        double previous = double.MaxValue;
                        int holding = 0;
                        for (int i = 0; i <= 400; i++)
                        {
                            double pull = 2 * drag * i / 400.0;
                            var sol = FightSolver.SolveTight(tackle, 0.8, pull, bodyDrag, true, false, 0.0, drag, wasSlipping, false);
                            double reelIn = Math.Max(0.0, -sol.LineCmPerSec);
                            Assert.That(reelIn, Is.LessThanOrEqualTo(previous + 1e-9), $"P = {pull}");
                            if (!sol.Slipping)
                            {
                                holding++;
                                Assert.That(reelIn, Is.GreaterThan(0), $"P = {pull}");
                                Assert.That(reelIn, Is.GreaterThanOrEqualTo(0.1 * tackle.ReelFightSpeedCmPerSec - 1e-9));
                            }

                            previous = reelIn;
                        }

                        Assert.That(holding, Is.GreaterThan(0));
                    }
                }
            }
        }

        /// <summary>Свечка на слабине стоит ровно 0,45 HookHold на шаге JumpApex (плюс обычный сход на слабине).</summary>
        [Test]
        public void SlackJumpCostsExactlyAtApex()
        {
            int found = 0;
            for (uint seed = 1; seed <= 40 && found < 3; seed++)
            {
                var fight = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), seed, startCm: 3000);
                var technique = FightBots.Technique(Drag020);
                bool letGo = false;
                while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 200)
                {
                    letGo |= fight.NextPhase == FishPhase.Jump && fight.TelegraphProgress > 0 && fight.HookHold > 0.9;
                    var input = letGo ? new FightInput { DragG = Drag020 } : technique(fight);
                    double hookBefore = fight.HookHold;
                    double slackBefore = fight.SlackCm;
                    fight.Step(input);
                    if ((fight.Events & FightEvents.JumpApex) == 0)
                        continue;
                    if (!letGo)
                        continue;

                    double slackShake = slackBefore > 30 ? 0.6 * Math.Min(1.0, (slackBefore - 30) / 60.0) * Tick : 0.0;
                    Assert.AreEqual(0.45 + slackShake, hookBefore - fight.HookHold, 1e-9, $"seed {seed}");
                    Assert.That((fight.Events & FightEvents.HookLoss) != 0);
                    Assert.That((fight.Events & FightEvents.Shock) == 0);
                    found++;
                    break;
                }
            }

            Assert.That(found, Is.GreaterThan(0), "не нашлось свечки");
        }

        [Test]
        public void NetLandsReadyFish()
        {
            var fight = FightKit.Fight(FightKit.Perch(), 600, FightKit.FloatTackle(), 3, startCm: 1200);
            Assert.That(fight.CanSwing, Is.False);
            while (!fight.CanNet && fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 300)
            {
                bool lift = fight.InNetReach && fight.RodAngle < 0.6;
                fight.Step(new FightInput { Reel = true, Lift = lift, DragG = FloatDrag });
            }

            Assert.IsTrue(fight.CanNet, "окунь 600 г должен дойти до подсачека");
            Assert.IsTrue(fight.HeadUp);
            fight.Step(new FightInput { Net = true, DragG = FloatDrag });

            Assert.AreEqual(FightOutcome.Landed, fight.Outcome);
            Assert.AreEqual(FightLanding.Netted, fight.Report.Landing);
            Assert.That((fight.Events & FightEvents.Landed) != 0);
            Assert.That((fight.Events & FightEvents.Swung) == 0);
        }

        [Test]
        public void NetBlockTellsWhatIsMissing()
        {
            // Окунь 600 г идёт к подсачеку: по дороге причины меняются, а «можно брать» — ровно когда причин нет.
            var fight = FightKit.Fight(FightKit.Perch(), 600, FightKit.FloatTackle(), 3, startCm: 1200);
            Assert.AreEqual(NetBlock.OutOfReach, fight.NetBlock, "рыба далеко");
            var seen = new System.Collections.Generic.HashSet<NetBlock>();
            while (!fight.CanNet && fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 300)
            {
                bool lift = fight.InNetReach && fight.RodAngle < 0.6;
                fight.Step(new FightInput { Reel = true, Lift = lift, DragG = FloatDrag });
                Assert.AreEqual(fight.CanNet, fight.NetBlock == NetBlock.None, "причина и CanNet не расходятся");
                seen.Add(fight.NetBlock);
            }

            Assert.IsTrue(fight.CanNet);
            Assert.AreEqual(NetBlock.None, fight.NetBlock);
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(2), "по дороге было больше одной причины");

            // Отпустил удилище рядом с рыбой — подсачек просит его поднять или натянуть леску.
            var near = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 1, startCm: 150);
            Assert.IsTrue(near.InNetReach);
            Assert.IsFalse(near.CanNet);
            Assert.AreNotEqual(NetBlock.None, near.NetBlock);
            Assert.AreNotEqual(NetBlock.OutOfReach, near.NetBlock, "рыба рядом — значит, дело в другом");
        }

        [Test]
        public void NetBumpsUnreadyFishIntoSurge()
        {
            var fight = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 1, startCm: 150);
            Assert.IsTrue(fight.InNetReach);
            Assert.IsFalse(fight.CanNet);
            double hookBefore = fight.HookHold;

            fight.Step(new FightInput { Net = true, DragG = Drag020 });

            Assert.AreEqual(FightOutcome.InProgress, fight.Outcome);
            Assert.AreEqual(FishPhase.Surge, fight.Phase);
            Assert.AreEqual(hookBefore - 0.1, fight.HookHold, 1e-12);
            var expected = FightEvents.NetBump | FightEvents.SurgeStart | FightEvents.PhaseStart;
            Assert.AreEqual(expected, fight.Events & expected);

            // Пока идёт пауза подсачека, повторное нажатие ничего не делает.
            fight.Step(new FightInput { Net = true, DragG = Drag020 });
            Assert.That((fight.Events & FightEvents.NetBump) == 0);
        }

        [Test]
        public void NetOutOfReachIsIgnored()
        {
            var fight = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 1, startCm: 3000);
            fight.Step(new FightInput { Net = true, DragG = Drag020 });

            Assert.AreEqual(FishPhase.Rest, fight.Phase);
            Assert.That((fight.Events & FightEvents.NetBump) == 0);
            Assert.AreEqual(1.0, fight.HookHold, 1e-12);
        }

        /// <summary>
        /// Свежую рыбу у берега «Подтянуть» не выуживает и не срывает: кнопка просто подтягивает, а «Выудить»
        /// (CanSwing) загорается, когда рыба устала. Раньше мах свежей рыбы стоил 0,4 крючка, и бот HoldReel
        /// терял всю мелочь, подсеченную ближе 3 м.
        /// </summary>
        [Test]
        public void FreshFishIsNotSwungLiftJustPumps()
        {
            var fight = FightKit.Fight(FightKit.Perch(), 300, FightKit.FloatTackle(), 1, startCm: 200);
            Assert.IsTrue(fight.InNetReach);
            Assert.IsFalse(fight.CanSwing, "свежую рыбу махом не предлагают");
            double angle = fight.RodAngle;

            fight.Step(new FightInput { Lift = true, DragG = FloatDrag });

            Assert.AreEqual(FightOutcome.InProgress, fight.Outcome);
            Assert.AreEqual(1.0, fight.HookHold, 1e-12);
            Assert.That(fight.RodAngle, Is.GreaterThan(angle), "«Подтянуть» подняло удилище");

            while (!fight.CanSwing && fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 60)
                fight.Step(new FightInput { Lift = fight.RodAngle < 0.9, Reel = fight.RodAngle >= 0.9, DragG = FloatDrag });
            Assert.IsTrue(fight.CanSwing, "уставшего окуня 300 г у берега можно выудить");
            Assert.That(fight.Stamina, Is.LessThan(0.6));

            fight.Step(new FightInput { Lift = true, DragG = FloatDrag });
            Assert.AreEqual(FightOutcome.Landed, fight.Outcome);
            Assert.AreEqual(FightLanding.Swung, fight.Report.Landing);
        }

        /// <summary>Мелочь, подсеченная у самого берега, не теряется у бота HoldReel (спецификация, §1).</summary>
        [Test]
        public void SmallFishHookedCloseIsLandedByJustReeling()
        {
            var species = new Func<FishSpecies>[] { FightKit.Roach, () => FightKit.Species("crucian", 3, 5, "steady"), FightKit.Perch };
            foreach (var fish in species)
            {
                foreach (int weight in new[] { 60, 120, 250, 450 })
                {
                    for (uint seed = 1; seed <= 4; seed++)
                    {
                        var fight = FightKit.Fight(fish(), weight, FightKit.FloatTackle(), seed, startCm: 250);
                        Assert.AreEqual(FightOutcome.Landed, FightBots.Play(fight, FightBots.HoldReel(FloatDrag), 60),
                            $"{fight.Report.Cause}, {weight} г, seed {seed}");
                        Assert.That(fight.ElapsedSeconds, Is.LessThan(15));
                    }
                }
            }
        }

        /// <summary>
        /// SwingLimitG — правило веса из CanSwing: min(450 г, 0,25 × прочность лески с износом). По нему HUD решает,
        /// показывать ли «Подсачек», поэтому рыба легче предела выуживается махом, а тяжелее — никогда.
        /// </summary>
        [Test]
        public void SwingLimitIsTheWeightRuleOfCanSwing()
        {
            Assert.AreEqual(450.0, FightKit.Fight(FightKit.Perch(), 250, FightKit.FloatTackle(), 1, startCm: 200).SwingLimitG, 1e-9);

            var thin = FightKit.FloatTackle();
            thin.LineBreakingLoadG = 1200;
            foreach (int weight in new[] { 250, 350 })
            {
                var fight = FightKit.Fight(FightKit.Perch(), weight, thin, 1, startCm: 200);
                Assert.AreEqual(300.0, fight.SwingLimitG, 1e-9);
                bool couldSwing = false;
                while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 60)
                {
                    couldSwing |= fight.CanSwing;
                    fight.Step(new FightInput { Lift = fight.RodAngle < 0.9, Reel = fight.RodAngle >= 0.9, DragG = thin.AutoDragG });
                }

                if (weight < fight.SwingLimitG)
                {
                    Assert.AreEqual(FightOutcome.Landed, fight.Outcome, $"{weight} г: {fight.Report.Cause}");
                    Assert.AreEqual(FightLanding.Swung, fight.Report.Landing);
                }
                else
                {
                    Assert.IsFalse(couldSwing, $"{weight} г тяжелее предела {fight.SwingLimitG} г — только подсачек");
                }
            }
        }

        [Test]
        public void VisualSeedChangesOnlyLateral()
        {
            var a = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 3, startCm: 3000, visualSeed: 1);
            var b = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), 3, startCm: 3000, visualSeed: 2);
            var botA = FightBots.Technique(Drag020);
            var botB = FightBots.Technique(Drag020);
            double maxLateralGap = 0;
            while (a.Outcome == FightOutcome.InProgress && a.ElapsedSeconds < 600)
            {
                a.Step(botA(a));
                b.Step(botB(b));
                Assert.AreEqual(a.FishDistanceCm, b.FishDistanceCm);
                Assert.AreEqual(a.TensionG, b.TensionG);
                Assert.That(Math.Abs(a.LateralCm), Is.LessThanOrEqualTo(0.3 * a.FishDistanceCm + 1e-9));
                maxLateralGap = Math.Max(maxLateralGap, Math.Abs(a.LateralCm - b.LateralCm));
            }

            Assert.AreEqual(a.Outcome, b.Outcome);
            Assert.AreEqual(a.Tick, b.Tick);
            AssertSameReport(a.Report, b.Report);
            Assert.That(maxLateralGap, Is.GreaterThan(10), "боковое смещение должно зависеть от visualSeed");
        }

        [Test]
        public void DoingNothingLetsRoachGoIdle()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle(), startCm: 1500);

            Assert.AreEqual(FightOutcome.Escaped, FightBots.Play(fight, FightBots.DoNothing(FloatDrag), 60));
            Assert.AreEqual(FightCause.Idle, fight.Report.Cause);
            Assert.That(fight.ElapsedSeconds, Is.LessThan(60));
        }

        /// <summary>Держать «Подтянуть», когда удилище уже в самом верху, — тоже простой: оно ничего не делает.</summary>
        [Test]
        public void HoldingLiftAtTheTopIsIdle()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle(), startCm: 1500);

            Assert.AreEqual(FightOutcome.Escaped, FightBots.Play(fight, m => new FightInput { Lift = true, DragG = FloatDrag }, 90));
            Assert.AreEqual(FightCause.Idle, fight.Report.Cause);
        }

        // ---------- Регрессии по ревью v2 ----------

        /// <summary>
        /// Вываживание всегда кончается. Раньше рыба отдыхала при T &lt; 0,25 F даже под полной нагрузкой снасти
        /// (лещ от 3,5 кг на поплавке, щука 7 кг на 0,30, судак 5–6 кг), а пробуксовка обнуляла счёт простоя,
        /// и бездействие или «Подтянуть» в упоре тянулись бесконечно.
        /// </summary>
        [Test]
        public void FightsAlwaysEnd()
        {
            Func<FishSpecies> bream = FightKit.Bream;
            Func<FishSpecies> pike = FightKit.Pike;
            Func<FishSpecies> zander = () => FightKit.Species("zander", 7, 6, "deep");
            Func<FightTackle> floatTackle = FightKit.FloatTackle;
            Func<FightTackle> line020 = () => FightKit.SpinningTackle(4000);
            Func<FightTackle> line030 = () => FightKit.SpinningTackle(8000);
            Func<int, FightBot> technique = d => FightBots.Technique(d);
            Func<int, FightBot> stopOnClicks = d => FightBots.StopOnClicks(d);
            Func<int, FightBot> holdReel = d => FightBots.HoldReel(d);
            Func<int, FightBot> doNothing = d => FightBots.DoNothing(d);
            Func<int, FightBot> liftOnly = d => m => new FightInput { Lift = true, DragG = d };
            var cases = new[]
            {
                (bream, 3500, floatTackle, 1500.0, technique),
                (bream, 3500, floatTackle, 1500.0, stopOnClicks),
                (bream, 5000, floatTackle, 1500.0, technique),
                (bream, 3000, floatTackle, 1500.0, doNothing),
                (bream, 3000, floatTackle, 1500.0, liftOnly),
                (pike, 7000, line030, 3000.0, stopOnClicks),
                (pike, 7000, line030, 3000.0, holdReel),
                (zander, 5000, line020, 3000.0, doNothing),
                (zander, 5000, line020, 3000.0, liftOnly),
                (zander, 6000, line020, 3000.0, technique),
            };
            foreach (var (fish, weight, makeTackle, start, makeBot) in cases)
            {
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var tackle = makeTackle();
                    var fight = FightKit.Fight(fish(), weight, tackle, seed, startCm: start);
                    var outcome = FightBots.Play(fight, makeBot(tackle.AutoDragG), FightBands.FightLimitSeconds);
                    Assert.AreNotEqual(FightOutcome.InProgress, outcome,
                        $"{weight} г, seed {seed}: через {FightBands.FightLimitSeconds} с силы {fight.Stamina:0.00}, до рыбы {fight.FishDistanceCm:0} см");
                }
            }
        }

        /// <summary>Рыба, намного сильнее фрикциона, устаёт, пока забирает леску (T = фрикцион), и не отдыхает под подъёмом.</summary>
        [Test]
        public void FishMuchStrongerThanDragStillTires()
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                var fight = FightKit.Fight(FightKit.Bream(), 3500, FightKit.FloatTackle(), seed, startCm: 1500);
                Assert.AreEqual(FightOutcome.Landed, FightBots.Play(fight, FightBots.Technique(FloatDrag)), $"seed {seed}");
            }
        }

        /// <summary>Сход на вершине свечки по слабине — причина JumpOnSlack, хотя тряска на слабине в том же шаге тоже снимает крючок.</summary>
        [Test]
        public void SlackJumpEscapeIsReportedAsJumpOnSlack()
        {
            int escapes = 0;
            for (uint seed = 1; seed <= 30; seed++)
            {
                var fight = FightKit.Fight(FightKit.Pike(), 4000, FightKit.SpinningTackle(4000), seed, startCm: 3000, hookHold: 0.3);
                var technique = FightBots.Technique(Drag020);
                FightBot letGoOnJumps = m => m.Phase == FishPhase.Jump || (m.NextPhase == FishPhase.Jump && m.TelegraphProgress > 0)
                    ? new FightInput { DragG = Drag020 }
                    : technique(m);
                FightBots.Play(fight, letGoOnJumps, 300);
                bool apexPenalty = (fight.Events & FightEvents.JumpApex) != 0 && (fight.Events & FightEvents.Shock) == 0;
                if (fight.Outcome != FightOutcome.Escaped || !apexPenalty)
                    continue;
                Assert.AreEqual(FightCause.JumpOnSlack, fight.Report.Cause, $"seed {seed}");
                escapes++;
            }

            Assert.That(escapes, Is.GreaterThan(5), "мало сходов на свечке — сценарий не проверяет причину");
        }

        /// <summary>Удар подсачеком, добивший крючок после 10 с простоя, — причина NetBump, а не Idle.</summary>
        [Test]
        public void NetBumpEscapeIsReportedAsNetBump()
        {
            var fight = FightKit.Fight(FightKit.Perch(), 600, FightKit.FloatTackle(), 1, startCm: 150, hookHold: 0.1);
            while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 10.5)
                fight.Step(new FightInput { DragG = FloatDrag });
            Assert.AreEqual(FightOutcome.InProgress, fight.Outcome);
            Assert.IsTrue(fight.InNetReach);
            Assert.IsFalse(fight.CanNet);
            Assert.That(fight.HookHold, Is.LessThan(0.1), "простой уже снимает крючок");

            fight.Step(new FightInput { Net = true, DragG = FloatDrag });

            Assert.AreEqual(FightOutcome.Escaped, fight.Outcome);
            Assert.AreEqual(FightCause.NetBump, fight.Report.Cause);
        }

        /// <summary>
        /// Трогание фрикциона в шаг удара рыбы не складывается с ударом: пик — большее из двух.
        /// Иначе на 0,30 с автофрикционом 2750 рывок при поднятом на телеграфе удилище ломал удилище.
        /// </summary>
        [Test]
        public void BreakawayShockDoesNotStackOnJerkShock()
        {
            var settings = new FightSettings();
            int checkedJerks = 0;
            for (uint seed = 1; seed <= 20 && checkedJerks < 5; seed++)
            {
                var tackle = FightKit.SpinningTackle(8000);
                int drag = tackle.AutoDragG;
                var fight = FightKit.Fight(FightKit.Pike(), 7000, tackle, seed, startCm: 3000);
                var bot = FightBots.Technique(drag);
                int quietTicks = 0;
                while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 300)
                {
                    double stamina = fight.Stamina;
                    fight.Step(bot(fight));
                    var events = fight.Events;
                    bool jerkBreakaway = (events & FightEvents.PhaseStart) != 0 && fight.Phase == FishPhase.Jerk
                                         && (events & FightEvents.Breakaway) != 0;
                    if (jerkBreakaway && quietTicks >= 3)
                    {
                        double shortLine = Math.Max(0.0, Math.Min(1.0, (800 - fight.LineOutCm) / 600));
                        double hit = settings.JerkShock * fight.FishForceG * (0.3 + 0.7 * stamina)
                                     * (1 - tackle.RodFlex * (0.5 + 0.5 * fight.RodAngle))
                                     * (fight.IsDragSlipping ? 0.5 : 0.8) * (1 + 0.6 * shortLine);
                        Assert.AreEqual(Math.Max(hit, 0.2 * drag), fight.PeakTensionG - fight.TensionG, 1e-6, $"seed {seed}, тик {fight.Tick}");
                        checkedJerks++;
                    }

                    quietTicks = (events & (FightEvents.Shock | FightEvents.Breakaway)) != 0 ? 0 : quietTicks + 1;
                }
            }

            Assert.That(checkedJerks, Is.GreaterThan(0), "не нашлось рывка с троганием фрикциона");
        }

        /// <summary>На 0,30 с автофрикционом техника из §3.7 (подъём на рывок) ловит щуку 4 кг, а не ломает удилище.</summary>
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(4u)]
        [TestCase(5u)]
        public void TechniqueOnStrongLineLandsFourKiloPike(uint seed)
        {
            var tackle = FightKit.SpinningTackle(8000);
            var fight = FightKit.Fight(FightKit.Pike(), 4000, tackle, seed, startCm: 3000);

            Assert.AreEqual(FightOutcome.Landed, FightBots.Play(fight, FightBots.Technique(tackle.AutoDragG)), fight.Report.Cause.ToString());
        }

        /// <summary>
        /// Пауза подсачека кончается так же, как её видит игрок: нажатие в шаг, когда она истекает, судится по тому же
        /// CanNet. Раньше в этот шаг нажатие уже принималось, а CanNet (из прошлого шага) ещё был false из-за паузы —
        /// и рыбу, во всём остальном готовую, снова задевали подсачеком.
        /// </summary>
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        public void NetCooldownEndDoesNotBumpReadyFish(uint seed)
        {
            var fight = FightKit.Fight(FightKit.Perch(), 600, FightKit.FloatTackle(), seed, startCm: 150);

            // Утомить подмоткой с опущенным удилищем: выдохшаяся рыба в паузе, но удилище ниже 0,4 — подсачек не готов.
            while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 120
                   && !(fight.Stamina < 0.15 && fight.Phase == FishPhase.Rest && fight.InNetReach
                        && fight.RodAngle < 0.4 && fight.SlackCm <= 30))
                fight.Step(new FightInput { Reel = true, DragG = FloatDrag });
            Assert.AreEqual(FightOutcome.InProgress, fight.Outcome, fight.Report.Cause.ToString());
            Assert.IsFalse(fight.CanNet);

            fight.Step(new FightInput { Net = true, DragG = FloatDrag });
            Assert.That((fight.Events & FightEvents.NetBump) != 0, "неготовую рыбу задели подсачеком");
            int bumpTick = fight.Tick;

            // Держать удилище от 0,6 и жать «Подсачек» каждый шаг: пока идёт пауза, нажатия пропускаются,
            // а первое принятое нажатие застаёт рыбу готовой (бросок 1,5 с короче паузы 2 с).
            bool sawReadyInCooldown = false;
            while (fight.Outcome == FightOutcome.InProgress && fight.Tick < bumpTick + 200)
            {
                bool readyButCooldown = !fight.CanNet && fight.Phase == FishPhase.Rest && fight.InNetReach
                                        && fight.Stamina <= 0.3 && fight.RodAngle >= 0.4 && fight.SlackCm <= 30;
                sawReadyInCooldown |= readyButCooldown;
                fight.Step(new FightInput { Net = true, Lift = fight.RodAngle < 0.6, DragG = FloatDrag });
                Assert.That((fight.Events & FightEvents.NetBump) == 0,
                    $"второй удар подсачеком на тике {fight.Tick} (через {fight.Tick - bumpTick} после первого), рыба была готова: {readyButCooldown}");
            }

            Assert.IsTrue(sawReadyInCooldown, "сценарий не дошёл до готовой рыбы во время паузы подсачека");
            Assert.AreEqual(FightOutcome.Landed, fight.Outcome);
            Assert.AreEqual(FightLanding.Netted, fight.Report.Landing);
            Assert.That(fight.Tick - bumpTick, Is.GreaterThan(40), "подсачек принят раньше конца паузы 2 с");
        }

        /// <summary>
        /// Удилище в упоре — ровно 0: остаток с плавающей точкой (~1e-19) после ограничения хода удилища запускал
        /// следующей «Подмоткой» режим (b), и шаг катушка ничего не выбирала. Окунь на ритме LiftReleaseReel
        /// при seed 1, 3, 5, 7 ловил такой остаток.
        /// </summary>
        [Test]
        public void RodAtBottomIsExactlyZero()
        {
            int reelsAtBottom = 0;
            for (uint seed = 1; seed <= 8; seed++)
            {
                var fight = FightKit.Fight(FightKit.Perch(), 300, FightKit.FloatTackle(), seed, startCm: 1500);
                var bot = FightBots.LiftReleaseReel(FloatDrag);
                while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < 120)
                {
                    double before = fight.RodAngle;
                    var input = bot(fight);
                    fight.Step(input);
                    Assert.That(fight.RodAngle == 0.0 || fight.RodAngle >= 1e-9, $"seed {seed}, тик {fight.Tick}: удилище {fight.RodAngle:R}");
                    Assert.That(fight.RodAngle == 1.0 || fight.RodAngle <= 1.0 - 1e-9, $"seed {seed}, тик {fight.Tick}: удилище {fight.RodAngle:R}");
                    if (!input.Reel || input.Lift || before >= 1e-9)
                        continue;
                    reelsAtBottom++;
                    Assert.AreNotEqual(FightMode.PumpReturn, fight.Mode, $"seed {seed}, тик {fight.Tick}: удилище внизу, а подмотка опускает его");
                }
            }

            Assert.That(reelsAtBottom, Is.GreaterThan(100));
        }

        [Test]
        public void LateStrikeStartsWithWeakerHook()
        {
            var fight = FightKit.Fight(FightKit.Roach(), 300, FightKit.FloatTackle(), hookHold: 0.75);
            Assert.AreEqual(0.75, fight.HookHoldStart, 1e-12);

            for (int i = 0; i < 40; i++)
                fight.Step(new FightInput { Reel = true, DragG = FloatDrag });
            Assert.That(fight.HookHold, Is.LessThanOrEqualTo(0.75 + 1e-12), "крючок не держит лучше, чем при подсечке");
        }

        private static void AssertSameReport(FightReport a, FightReport b)
        {
            Assert.AreEqual(a.Outcome, b.Outcome);
            Assert.AreEqual(a.Cause, b.Cause);
            Assert.AreEqual(a.Landing, b.Landing);
            Assert.AreEqual(a.ElapsedSeconds, b.ElapsedSeconds);
            Assert.AreEqual(a.PeakG, b.PeakG);
            Assert.AreEqual(a.LimitG, b.LimitG);
            Assert.AreEqual(a.MaxLoadShare, b.MaxLoadShare);
            Assert.AreEqual(a.Wear, b.Wear);
            Assert.AreEqual(a.SecondsCrankedOnSlip, b.SecondsCrankedOnSlip);
            Assert.AreEqual(a.Phase, b.Phase);
            Assert.AreEqual(a.RodAngle, b.RodAngle);
            Assert.AreEqual(a.RestsTotal, b.RestsTotal);
            Assert.AreEqual(a.RestsLifted, b.RestsLifted);
            Assert.AreEqual(a.HistoryCount, b.HistoryCount);
            for (int i = 0; i < a.HistoryCount; i++)
            {
                var x = a.History(i);
                var y = b.History(i);
                Assert.AreEqual(x.TensionG, y.TensionG);
                Assert.AreEqual(x.PeakG, y.PeakG);
                Assert.AreEqual(x.DragG, y.DragG);
                Assert.AreEqual(x.LimitG, y.LimitG);
            }
        }
    }
}
