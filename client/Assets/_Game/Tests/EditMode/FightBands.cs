using System;
using System.Collections.Generic;
using System.Globalization;
using Fishing.Core;

namespace Fishing.Tests
{
    /// <summary>Полоса баланса: рыба, снасть, бот и в каких пределах должен быть результат на 200 seed.</summary>
    internal sealed class FightBand
    {
        public string Name;
        public Func<FishSpecies> Species;
        public int WeightG;
        public Func<FightTackle> Tackle;
        public int DragG;
        public double StartCm;
        public string BotName;
        public Func<int, FightBot> Bot;

        /// <summary>Цель из спецификации (§3.7): доля пойманных.</summary>
        public double MinLanded;
        public double MaxLanded = 1.0;

        /// <summary>Медиана времени пойманных рыб, с; NaN — не проверяется.</summary>
        public double MinMedianSeconds = double.NaN;
        public double MaxMedianSeconds = double.NaN;

        /// <summary>
        /// Задокументированное отклонение: цель спецификации недостижима ручками из §3.7 (почему — здесь).
        /// Тогда проверяется то, что модель делает сейчас (DeviationMinLanded…DeviationMaxLanded), а попадание
        /// в цель спецификации тоже роняет тест (FightBandResult.DeviationStale): отклонение пора убрать.
        /// </summary>
        public string Deviation;
        public double DeviationMinLanded;
        public double DeviationMaxLanded = 1.0;

        /// <summary>Какие пределы доли пойманных проверяются: отклонения, если оно есть, иначе спецификации.</summary>
        public double CheckedMinLanded => Deviation != null ? DeviationMinLanded : MinLanded;
        public double CheckedMaxLanded => Deviation != null ? DeviationMaxLanded : MaxLanded;

        /// <summary>Цель спецификации текстом.</summary>
        public string Target => Describe(MinLanded, MaxLanded)
                                + (double.IsNaN(MinMedianSeconds) ? "" : $", {MinMedianSeconds:0}–{MaxMedianSeconds:0} с");

        /// <summary>Что проверяется для полосы с отклонением, текстом.</summary>
        public string DeviationTarget => Describe(DeviationMinLanded, DeviationMaxLanded);

        private static string Describe(double min, double max) =>
            min >= 1.0 ? "100%"
            : max <= 0.0 ? "0%"
            : min > 0 && max < 1.0 ? $"{min * 100:0}–{max * 100:0}%"
            : min > 0 ? $"≥{min * 100:0}%"
            : $"≤{max * 100:0}%";
    }

    internal sealed class FightBandResult
    {
        public FightBand Band;
        public int Runs;
        public int Landed;
        public double MedianSeconds = double.NaN;
        public readonly int[] Outcomes = new int[Enum.GetValues(typeof(FightOutcome)).Length];
        public readonly int[] Causes = new int[Enum.GetValues(typeof(FightCause)).Length];

        public double LandedShare => Runs > 0 ? (double)Landed / Runs : 0.0;

        /// <summary>Сколько вываживаний не кончилось за FightBands.FightLimitSeconds: тупик, полоса не проходит.</summary>
        public int StillRunning => Outcomes[(int)FightOutcome.InProgress];

        /// <summary>Полоса проходит: без тупиков и в проверяемых пределах (у полосы с отклонением — в пределах отклонения).</summary>
        public bool Passed => StillRunning == 0 && Within(Band.CheckedMinLanded, Band.CheckedMaxLanded);

        /// <summary>Результат попал в цель спецификации.</summary>
        public bool MeetsSpec => StillRunning == 0 && Within(Band.MinLanded, Band.MaxLanded);

        /// <summary>У полосы записано отклонение, а результат уже в цели спецификации: верни цель в FightBands.</summary>
        public bool DeviationStale => Band.Deviation != null && MeetsSpec;

        private bool Within(double minLanded, double maxLanded)
        {
            const double eps = 1e-9;
            if (LandedShare < minLanded - eps || LandedShare > maxLanded + eps)
                return false;
            if (double.IsNaN(Band.MinMedianSeconds))
                return true;
            return !double.IsNaN(MedianSeconds) && MedianSeconds >= Band.MinMedianSeconds
                                                && MedianSeconds <= Band.MaxMedianSeconds;
        }

        public string Result
        {
            get
            {
                string median = double.IsNaN(MedianSeconds) ? "—" : MedianSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " с";
                return $"{LandedShare * 100:0.0}% пойманы, медиана {median}";
            }
        }

        /// <summary>Исходы и причины, например «LineBroke 120 (WornByCranking 100, Overload 20)».</summary>
        public string Breakdown
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < Outcomes.Length; i++)
                {
                    if (Outcomes[i] > 0)
                        parts.Add($"{(FightOutcome)i} {Outcomes[i]}");
                }

                var causes = new List<string>();
                for (int i = 1; i < Causes.Length; i++)
                {
                    if (Causes[i] > 0)
                        causes.Add($"{(FightCause)i} {Causes[i]}");
                }

                return string.Join(", ", parts) + (causes.Count > 0 ? " | " + string.Join(", ", causes) : "");
            }
        }

        public override string ToString() =>
            $"{Band.Name} [{Band.BotName}]: {Result}; цель {Band.Target}"
            + (Band.Deviation != null ? $" (отклонение: проверяется {Band.DeviationTarget})" : "") + $"; {Breakdown}";
    }

    /// <summary>Полосы баланса из спецификации fight-v2, §3.7. Тесты и симулятор берут их отсюда.</summary>
    internal static class FightBands
    {
        public const int Seeds = 200;
        public const double FightLimitSeconds = 600;

        /// <summary>
        /// Почему щука 7 кг на 0,20 не «почти невозможна» (§3.7: Technique 20–40%, TechniqueLate ≤35%), хотя на 0,30
        /// она честная (≥90%) — обе цели этими ручками не сходятся. Бот Technique держит удилище на 0,9 в паузах и
        /// поднимает его на телеграфе рывка, а при подъёме от 0,83 самое слабое звено спиннинга — удилище
        /// (6000 / (1 + 0,6 × угол) ≤ 4000), и на 0,20, и на 0,30. Решает удар свежего рывка при поднятом удилище:
        /// пик = фрикцион + удар, предел ≈ 3750–3900 г у обеих лесок, а автофрикцион у 0,30 выше (2750 против 2400).
        /// Значит, на этом ударе 0,30 всегда ближе к поломке, чем 0,20 (доля нагрузки — медиана 0,98 против 0,90 при
        /// k = 0,4), и любая ручка, что даёт 0,20 20–40%, ломает удилище на 0,30 ещё чаще. Перекрута у техники нет,
        /// а к берегу рыба подходит выдохшейся, поэтому короткая леска тоже не губит. Выбрана честная 0,30:
        /// k рывка 0,35, и техника ловит щуку 7 кг на 0,20 почти всегда. Развести полосы может только решение
        /// по спецификации: автофрикцион с рычагом поднятого удилища (удилище / 1,6), меньший рычаг у спиннинга
        /// или техника, которая на рывке не поднимает удилище, когда слабее удилище.
        /// </summary>
        public const string SevenKiloDeviation =
            "щука 7 кг на 0,20: при поднятом на рывок удилище слабее удилище, а не леска, и у 0,30 автофрикцион выше, " +
            "поэтому 0,20 не может быть опаснее 0,30 для техники; см. FightBands.SevenKiloDeviation";

        public static List<FightBand> All()
        {
            const double near = 1500;
            const double far = 3000;
            int floatDrag = FightKit.FloatTackle().AutoDragG;
            int drag020 = FightKit.SpinningTackle(4000).AutoDragG;
            int drag030 = FightKit.SpinningTackle(8000).AutoDragG;

            FightBand Make(string name, Func<FishSpecies> fish, int weight, Func<FightTackle> tackle, int drag, double start,
                string bot, Func<int, FightBot> make) => new FightBand
            {
                Name = name, Species = fish, WeightG = weight, Tackle = tackle, DragG = drag, StartCm = start,
                BotName = bot, Bot = make,
            };

            Func<FightTackle> float012 = FightKit.FloatTackle;
            Func<FightTackle> spin020 = () => FightKit.SpinningTackle(4000);
            Func<FightTackle> spin030 = () => FightKit.SpinningTackle(8000);

            var bands = new List<FightBand>();

            var b = Make("Плотва 150 г, поплавок, 15 м", FightKit.Roach, 150, float012, floatDrag, near, "HoldReel", d => FightBots.HoldReel(d));
            b.MinLanded = 1.0;
            b.MinMedianSeconds = 12;
            b.MaxMedianSeconds = 17;
            bands.Add(b);

            b = Make("Окунь 300 г, поплавок, 15 м", FightKit.Perch, 300, float012, floatDrag, near, "HoldReel", d => FightBots.HoldReel(d));
            b.MinLanded = 1.0;
            b.MinMedianSeconds = 18;
            b.MaxMedianSeconds = 26;
            bands.Add(b);

            b = Make("Окунь 300 г, поплавок, 15 м", FightKit.Perch, 300, float012, floatDrag, near, "LiftReleaseReel", d => FightBots.LiftReleaseReel(d));
            b.MinLanded = 0.95;
            bands.Add(b);

            b = Make("Лещ 2 кг, поплавок, 15 м", FightKit.Bream, 2000, float012, floatDrag, near, "HoldReel", d => FightBots.HoldReel(d));
            b.MaxLanded = 0.40;
            bands.Add(b);

            b = Make("Лещ 2 кг, поплавок, 15 м", FightKit.Bream, 2000, float012, floatDrag, near, "StopOnClicks", d => FightBots.StopOnClicks(d));
            b.MinLanded = 0.80;
            bands.Add(b);

            b = Make("Лещ 2 кг, поплавок, 15 м", FightKit.Bream, 2000, float012, floatDrag, near, "Technique", d => FightBots.Technique(d));
            b.MinLanded = 0.95;
            b.MinMedianSeconds = 55;
            b.MaxMedianSeconds = 100;
            bands.Add(b);

            b = Make("Щука 4 кг, 0,20, 30 м", FightKit.Pike, 4000, spin020, drag020, far, "HoldReel", d => FightBots.HoldReel(d));
            b.MaxLanded = 0.10;
            bands.Add(b);

            b = Make("Щука 4 кг, 0,20, 30 м", FightKit.Pike, 4000, spin020, drag020, far, "StopOnClicks", d => FightBots.StopOnClicks(d));
            b.MinLanded = 0.85;
            bands.Add(b);

            b = Make("Щука 4 кг, 0,20, 30 м", FightKit.Pike, 4000, spin020, drag020, far, "Technique", d => FightBots.Technique(d));
            b.MinLanded = 0.95;
            b.MinMedianSeconds = 60;
            b.MaxMedianSeconds = 120;
            bands.Add(b);

            b = Make("Щука 7 кг, 0,20, 30 м", FightKit.Pike, 7000, spin020, drag020, far, "Technique", d => FightBots.Technique(d));
            b.MinLanded = 0.20;
            b.MaxLanded = 0.40;
            b.Deviation = SevenKiloDeviation;
            b.DeviationMinLanded = 0.90;
            bands.Add(b);

            b = Make("Щука 7 кг, 0,20, 30 м", FightKit.Pike, 7000, spin020, drag020, far, "TechniqueLate", d => FightBots.TechniqueLate(d));
            b.MaxLanded = 0.35;
            b.Deviation = SevenKiloDeviation;
            b.DeviationMinLanded = 0.90;
            bands.Add(b);

            b = Make("Щука 7 кг, 0,20, 30 м", FightKit.Pike, 7000, spin020, drag020, far, "HoldReel", d => FightBots.HoldReel(d));
            b.MaxLanded = 0.0;
            bands.Add(b);

            b = Make("Щука 7 кг, 0,30, 30 м", FightKit.Pike, 7000, spin030, drag030, far, "Technique", d => FightBots.Technique(d));
            b.MinLanded = 0.90;
            bands.Add(b);

            b = Make("Щука 12 кг, поплавок, 15 м", FightKit.Pike, 12000, float012, floatDrag, near, "Technique", d => FightBots.Technique(d));
            b.MaxLanded = 0.0;
            bands.Add(b);

            b = Make("Щука 12 кг, поплавок, 15 м", FightKit.Pike, 12000, float012, floatDrag, near, "HoldReel", d => FightBots.HoldReel(d));
            b.MaxLanded = 0.0;
            bands.Add(b);

            return bands;
        }

        public static FightBandResult Run(FightBand band, int seeds = Seeds, FightSettings settings = null)
        {
            var result = new FightBandResult { Band = band };
            var times = new List<double>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var fight = new FightModel(band.Species(), band.WeightG, band.Tackle(), band.StartCm,
                    new SeededRandom((uint)seed), settings, 0, 1.0, (uint)seed);
                var outcome = FightBots.Play(fight, band.Bot(band.DragG), FightLimitSeconds);
                result.Runs++;
                result.Outcomes[(int)outcome]++;
                result.Causes[(int)fight.Report.Cause]++;
                if (outcome == FightOutcome.Landed)
                {
                    result.Landed++;
                    times.Add(fight.ElapsedSeconds);
                }
            }

            if (times.Count > 0)
            {
                times.Sort();
                int n = times.Count;
                result.MedianSeconds = n % 2 == 1 ? times[n / 2] : 0.5 * (times[n / 2 - 1] + times[n / 2]);
            }

            return result;
        }
    }
}
