using System;

namespace Fishing.Core
{
    /// <summary>За что изменилась карма.</summary>
    public enum KarmaReason
    {
        None,
        /// <summary>Отпустил только что пойманную рыбу.</summary>
        ReleasedFish,
        /// <summary>Отпустил малька.</summary>
        ReleasedJuvenile,
        /// <summary>Отпустил трофей.</summary>
        ReleasedTrophy,
        /// <summary>Отпустил рыбу из садка.</summary>
        ReleasedFromKeepnet,
        /// <summary>Забрал малька.</summary>
        KeptJuvenile,
        /// <summary>Оборвал леску: рыба ушла с крючком.</summary>
        LineBroke,
    }

    /// <summary>Ступени кармы — от браконьера до хранителя озера.</summary>
    public enum KarmaLevel
    {
        Poacher,
        Grabber,
        Angler,
        Honest,
        Keeper,
    }

    /// <summary>Одно изменение кармы: на сколько, за что и какой она стала.</summary>
    public readonly struct KarmaChange
    {
        public readonly int Delta;
        public readonly KarmaReason Reason;
        public readonly int Karma;

        public KarmaChange(int delta, KarmaReason reason, int karma)
        {
            Delta = delta;
            Reason = reason;
            Karma = karma;
        }
    }

    /// <summary>
    /// Правила кармы, как в «Русской рыбалке»: бережное отношение к рыбе её поднимает, жадность и небрежность — роняют.
    /// Карма — целое от −100 до +100, у нового игрока 0.
    ///
    /// | Действие                              | Карма |
    /// | отпустил пойманную рыбу               | +1    |
    /// | отпустил малька                       | +2    |
    /// | отпустил трофей                       | +5    |
    /// | отпустил рыбу из садка                | +1    |
    /// | забрал малька в садок                 | −3    |
    /// | оборвал леску (рыба ушла с крючком)   | −1    |
    ///
    /// Малёк — рыба легче половины обычного веса своего вида (weightG.typical). Продажа карму не меняет:
    /// выбор всегда один — монеты за рыбу или карма за то, что её отпустил.
    /// </summary>
    public static class KarmaRules
    {
        public const int Min = -100;
        public const int Max = 100;

        /// <summary>Малёк — легче этой доли обычного веса вида, %.</summary>
        public const int JuvenilePercent = 50;

        public static bool IsJuvenile(FishSpecies species, int weightG)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            return weightG > 0 && (long)weightG * 100 < (long)species.weightG.typical * JuvenilePercent;
        }

        /// <summary>Отпустил только что пойманную рыбу.</summary>
        public static (int delta, KarmaReason reason) ForReleasingCatch(FishSpecies species, int weightG, bool trophy)
        {
            if (trophy)
                return (5, KarmaReason.ReleasedTrophy);
            return IsJuvenile(species, weightG) ? (2, KarmaReason.ReleasedJuvenile) : (1, KarmaReason.ReleasedFish);
        }

        /// <summary>Положил рыбу в садок.</summary>
        public static (int delta, KarmaReason reason) ForKeeping(FishSpecies species, int weightG)
        {
            return IsJuvenile(species, weightG) ? (-3, KarmaReason.KeptJuvenile) : (0, KarmaReason.None);
        }

        public static (int delta, KarmaReason reason) ForReleasingFromKeepnet() => (1, KarmaReason.ReleasedFromKeepnet);

        public static (int delta, KarmaReason reason) ForLoss(FightOutcome outcome)
        {
            return outcome == FightOutcome.LineBroke ? (-1, KarmaReason.LineBroke) : (0, KarmaReason.None);
        }

        public static int Clamp(int karma) => Math.Max(Min, Math.Min(Max, karma));

        public static KarmaLevel Level(int karma)
        {
            if (karma <= -50) return KarmaLevel.Poacher;
            if (karma <= -10) return KarmaLevel.Grabber;
            if (karma < 10) return KarmaLevel.Angler;
            return karma < 50 ? KarmaLevel.Honest : KarmaLevel.Keeper;
        }
    }
}
