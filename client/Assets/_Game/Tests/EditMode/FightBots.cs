using System;
using System.Collections.Generic;
using Fishing.Core;

namespace Fishing.Tests
{
    // Общие заготовки для тестов вываживания и симулятора баланса. Без NUnit и UnityEngine:
    // этот файл и FightBands.cs компилирует ещё и консольный симулятор (CoreSim).

    /// <summary>Снасти и рыбы для тестов: числа как в tackle.json и fish.json, но заданы явно.</summary>
    internal static class FightKit
    {
        public static FightTackle FloatTackle() => new FightTackle
        {
            RodMaxLoadG = 2500, RodFlex = 0.6, RodReachCm = 240, RodPowerG = 1500, LineBreakingLoadG = 1800,
            ReelMaxDragG = 3000, ReelFightSpeedCmPerSec = 110, ReelPowerG = 600, SpoolCapacityCm = 10000,
        };

        /// <summary>Спиннинг с катушкой 2500 и леской заданной прочности (0,20 — 4000 г, 0,30 — 8000 г).</summary>
        public static FightTackle SpinningTackle(int lineG = 4000) => new FightTackle
        {
            RodMaxLoadG = 6000, RodFlex = 0.35, RodReachCm = 144, RodPowerG = 3600, LineBreakingLoadG = lineG,
            ReelMaxDragG = 6000, ReelFightSpeedCmPerSec = 250, ReelPowerG = 1600, SpoolCapacityCm = 15000,
        };

        public static FishSpecies Species(string id, int strength, int stamina, string fightStyle)
        {
            return new FishSpecies
            {
                id = id,
                weightG = new WeightRange { min = 100, typical = 300, qualifying = 600, trophy = 1000, max = 20000 },
                trophyChance = 0.01f,
                depthCm = new IntRange { min = 100, max = 300 },
                activeHours = new[] { 6, 7, 8 },
                baits = new[] { new BaitTaste { id = "worm", taste = 100 } },
                biteRatePerMin = 1f,
                maxLineMm = 0.15f,
                strength = strength,
                stamina = stamina,
                fightStyle = fightStyle,
            };
        }

        public static FishSpecies Roach() => Species("roach", 2, 3, "steady");
        public static FishSpecies Perch() => Species("perch", 4, 4, "jerky");
        public static FishSpecies Bream() => Species("bream", 5, 5, "heavy");
        public static FishSpecies Pike() => Species("pike", 8, 7, "jumps");

        public static FightModel Fight(FishSpecies fish, int weightG, FightTackle tackle, uint seed = 1,
            double startCm = 2000, double slackCm = 0, double hookHold = 1, uint visualSeed = 0,
            FightSettings settings = null)
        {
            return new FightModel(fish, weightG, tackle, startCm, new SeededRandom(seed), settings, slackCm, hookHold, visualSeed);
        }
    }

    /// <summary>Игрок-бот: по состоянию модели решает, что нажать в этот шаг.</summary>
    internal delegate FightInput FightBot(FightModel fight);

    /// <summary>То, что бот видит на экране; TechniqueLate получает такие снимки с опозданием.</summary>
    internal struct FightView
    {
        public FishPhase Phase;
        public FishPhase NextPhase;
        public double TelegraphProgress;
        public bool Slipping;
        public double RodAngle;
        public double Stamina;
        public bool CanSwing;
        public bool CanNet;
        public bool InNetReach;

        public static FightView Of(FightModel m) => new FightView
        {
            Phase = m.Phase,
            NextPhase = m.NextPhase,
            TelegraphProgress = m.TelegraphProgress,
            Slipping = m.IsDragSlipping,
            RodAngle = m.RodAngle,
            Stamina = m.Stamina,
            CanSwing = m.CanSwing,
            CanNet = m.CanNet,
            InNetReach = m.InNetReach,
        };
    }

    /// <summary>Боты из спецификации (fight-v2, §3.7). Каждый вызов фабрики — новый бот со своим состоянием.</summary>
    internal static class FightBots
    {
        public const int LateTicks = 5;

        /// <summary>Всегда крутит. Выуживает, когда можно; у берега держит удилище от 0,6 и бьёт подсачеком, когда можно.</summary>
        public static FightBot HoldReel(int drag) => m => Land(m, new FightInput { Reel = true, DragG = drag });

        /// <summary>Крутит, пока фрикцион не трещит.</summary>
        public static FightBot StopOnClicks(int drag) => m => Land(m, new FightInput { Reel = !m.IsDragSlipping, DragG = drag });

        /// <summary>Ничего не нажимает.</summary>
        public static FightBot DoNothing(int drag) => m => new FightInput { DragG = drag };

        /// <summary>Обе кнопки всё время (зажатый фрикцион + подъём — так ломают удилище).</summary>
        public static FightBot ReelAndLift(int drag) => m => new FightInput { Reel = true, Lift = true, DragG = drag };

        /// <summary>Ритм новичка: 1 с «Подтянуть», 0,3 с ничего, 3 с «Подмотка», по кругу.</summary>
        public static FightBot LiftReleaseReel(int drag) => m =>
        {
            int t = m.Tick % 86;
            return Land(m, new FightInput { Lift = t < 20, Reel = t >= 26, DragG = drag });
        };

        /// <summary>Правильная игра по телеграфу (см. TechniqueOn).</summary>
        public static FightBot Technique(int drag) => m => TechniqueOn(FightView.Of(m), drag);

        /// <summary>Та же техника, но по состоянию LateTicks шагов назад.</summary>
        public static FightBot TechniqueLate(int drag, int lateTicks = LateTicks)
        {
            var seen = new Queue<FightView>();
            return m =>
            {
                seen.Enqueue(FightView.Of(m));
                var view = seen.Count > lateTicks ? seen.Dequeue() : seen.Peek();
                return TechniqueOn(view, drag);
            };
        }

        /// <summary>
        /// Телеграф ухода, ухода в глубину или броска, а также пробуксовка — отпустить подмотку;
        /// пауза — подтягивать до 0,9, потом подматывать; телеграф свечки и свечка — обе кнопки;
        /// телеграф рывка — «Подтянуть»; выуживать и брать подсачеком, когда можно.
        /// </summary>
        public static FightInput TechniqueOn(in FightView v, int drag)
        {
            var input = new FightInput { DragG = drag };
            bool telegraph = v.TelegraphProgress > 0;
            var next = v.NextPhase;
            if (v.Phase == FishPhase.Jump || (telegraph && next == FishPhase.Jump))
            {
                input.Reel = true;
                input.Lift = true;
            }
            else if (telegraph && next == FishPhase.Jerk)
            {
                input.Lift = true;
            }
            else if ((telegraph && (next == FishPhase.Run || next == FishPhase.Dive || next == FishPhase.Surge)) || v.Slipping)
            {
                // Отпустить подмотку: пусть работает фрикцион и удилище.
            }
            else if (v.Phase == FishPhase.Rest)
            {
                if (v.RodAngle < 0.9)
                    input.Lift = true;
                else
                    input.Reel = true;
            }
            else
            {
                input.Reel = true;
            }

            // CanSwing горит только у уставшей рыбы: мах всегда удаётся.
            if (v.CanSwing)
                input.Lift = true;

            if (v.CanNet)
                input.Net = true;
            return input;
        }

        /// <summary>Как кончают простые боты: выудить, если можно; у берега поднять удилище до 0,6 и взять подсачеком.</summary>
        private static FightInput Land(FightModel m, FightInput input)
        {
            if (m.CanSwing)
            {
                input.Lift = true;
                return input;
            }

            if (m.InNetReach)
            {
                if (m.CanNet)
                    input.Net = true;
                else if (m.RodAngle < 0.6)
                    input.Lift = true;
            }

            return input;
        }

        public static FightOutcome Play(FightModel fight, FightBot bot, double maxSeconds = 600)
        {
            while (fight.Outcome == FightOutcome.InProgress && fight.ElapsedSeconds < maxSeconds)
                fight.Step(bot(fight));
            return fight.Outcome;
        }
    }
}
