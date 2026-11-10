using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Условия, в которых стоит снасть.</summary>
    public struct BiteConditions
    {
        /// <summary>Игровое время, часы от 0 до 24.</summary>
        public float Hour;

        /// <summary>Глубина, на которой находится наживка, в сантиметрах.</summary>
        public float DepthCm;

        /// <summary>Id наживки или приманки из tackle.json.</summary>
        public string BaitId;

        /// <summary>Диаметр лески в миллиметрах.</summary>
        public float LineDiameterMm;
    }

    /// <summary>Результат: какая рыба клюнет, какого веса и через сколько секунд.</summary>
    public readonly struct BiteResult
    {
        public readonly string SpeciesId;
        public readonly int WeightG;
        public readonly bool IsTrophy;
        public readonly double SecondsToBite;

        public BiteResult(string speciesId, int weightG, bool isTrophy, double secondsToBite)
        {
            SpeciesId = speciesId;
            WeightG = weightG;
            IsTrophy = isTrophy;
            SecondsToBite = secondsToBite;
        }
    }

    /// <summary>Настройки модели клёва, общие для всех видов.</summary>
    public sealed class BiteSettings
    {
        /// <summary>Во сколько раз слабее клёв вне activeHours.</summary>
        public double OffHoursFactor = 0.1;

        /// <summary>На каком расстоянии от нужной глубины клёв падает вдвое, см.</summary>
        public double DepthFalloffCm = 50.0;

        /// <summary>На сколько мм толще maxLineMm леска, чтобы клёв упал вдвое.</summary>
        public double LineFalloffMm = 0.05;
    }

    /// <summary>
    /// Модель клёва. Каждый вид клюёт как случайный поток событий со своей интенсивностью
    /// (поклёвок в минуту). Интенсивность = biteRatePerMin × вкус наживки (1…100 %) × множители за час,
    /// глубину и леску; наживка, которой нет в списке вида, даёт ноль. Время до поклёвки — экспоненциальное
    /// от суммы интенсивностей, вид выбирается пропорционально интенсивности.
    ///
    /// Без зависимостей от Unity: эту же логику потом повторит сервер (этап 2).
    /// Порядок вызовов генератора важен для совпадения с серверной реализацией:
    /// время, вид, затем вес (два числа).
    /// </summary>
    public sealed class BiteModel
    {
        private readonly FishDatabase _database;
        private readonly BiteSettings _settings;

        public BiteModel(FishDatabase database, BiteSettings settings = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _settings = settings ?? new BiteSettings();
        }

        /// <summary>Поклёвок в минуту для вида в этих условиях.</summary>
        public double RatePerMinute(FishSpecies species, in BiteConditions conditions)
        {
            int taste = species.TasteFor(conditions.BaitId);
            if (taste <= 0)
                return 0.0;

            return species.biteRatePerMin
                   * (taste / 100.0)
                   * HourFactor(species, conditions.Hour)
                   * DepthFactor(species, conditions.DepthCm)
                   * LineFactor(species, conditions.LineDiameterMm);
        }

        /// <summary>
        /// Разыгрывает следующую поклёвку среди видов водоёма.
        /// Возвращает false, если в этих условиях не клюнет никто (например, не та наживка).
        /// </summary>
        public bool TryRollBite(IReadOnlyList<string> speciesIds, in BiteConditions conditions, IRandom random,
            out BiteResult result)
        {
            if (speciesIds == null)
                throw new ArgumentNullException(nameof(speciesIds));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            var rates = new double[speciesIds.Count];
            double total = 0.0;
            for (int i = 0; i < speciesIds.Count; i++)
            {
                rates[i] = RatePerMinute(_database.Get(speciesIds[i]), conditions);
                total += rates[i];
            }

            if (total <= 0.0)
            {
                result = default;
                return false;
            }

            double secondsToBite = -Math.Log(1.0 - random.NextDouble()) / total * 60.0;

            double pick = random.NextDouble() * total;
            int chosen = rates.Length - 1;
            for (int i = 0; i < rates.Length; i++)
            {
                if (pick < rates[i])
                {
                    chosen = i;
                    break;
                }
                pick -= rates[i];
            }

            var species = _database.Get(speciesIds[chosen]);
            int weight = RollWeight(species, random, out bool isTrophy);
            result = new BiteResult(species.id, weight, isTrophy, secondsToBite);
            return true;
        }

        /// <summary>
        /// Вес рыбы. С вероятностью trophyChance — трофей от trophy до max, ближе к нижней
        /// границе; иначе — треугольное распределение от min до trophy с пиком на typical.
        /// Всегда берёт из генератора ровно два числа.
        /// </summary>
        public static int RollWeight(FishSpecies species, IRandom random, out bool isTrophy)
        {
            var w = species.weightG;
            double roll = random.NextDouble();
            double u = random.NextDouble();

            if (roll < species.trophyChance)
            {
                isTrophy = true;
                return (int)Math.Round(w.trophy + (w.max - w.trophy) * u * u);
            }

            isTrophy = false;
            double weight = Triangular(w.min, w.typical, w.trophy, u);
            return Math.Min((int)Math.Round(weight), w.trophy - 1);
        }

        private double HourFactor(FishSpecies species, float hour)
        {
            int h = ((int)Math.Floor(hour) % 24 + 24) % 24;
            return Array.IndexOf(species.activeHours, h) >= 0 ? 1.0 : _settings.OffHoursFactor;
        }

        private double DepthFactor(FishSpecies species, float depthCm)
        {
            double distance = 0.0;
            if (depthCm < species.depthCm.min)
                distance = species.depthCm.min - depthCm;
            else if (depthCm > species.depthCm.max)
                distance = depthCm - species.depthCm.max;
            return Falloff(distance, _settings.DepthFalloffCm);
        }

        private double LineFactor(FishSpecies species, float lineDiameterMm)
        {
            double excess = Math.Max(0.0, lineDiameterMm - species.maxLineMm);
            return Falloff(excess, _settings.LineFalloffMm);
        }

        /// <summary>1 при нулевом отклонении, 0.5 при отклонении, равном halfAt, дальше плавно к нулю.</summary>
        private static double Falloff(double deviation, double halfAt)
        {
            double x = deviation / halfAt;
            return 1.0 / (1.0 + x * x);
        }

        private static double Triangular(double min, double mode, double max, double u)
        {
            double split = (mode - min) / (max - min);
            return u < split
                ? min + Math.Sqrt(u * (max - min) * (mode - min))
                : max - Math.Sqrt((1.0 - u) * (max - min) * (max - mode));
        }
    }
}
