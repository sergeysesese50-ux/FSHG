using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    // Классы для чтения shared/data/levels.json. Имена полей совпадают с ключами JSON.
    // Описание полей — shared/data/README.md, TypeScript-двойник — server/modules/src/data.ts.

    [Serializable]
    public sealed class LevelRewardFile
    {
        public int version;
        public LevelReward[] levels;
    }

    /// <summary>
    /// Награда за уровень: монеты всегда; на значимых уровнях — наживка, приманка, снасть или время на лодке.
    /// Приманки, снасти и лодка подобраны под то, что на этом уровне открывается.
    /// </summary>
    [Serializable]
    public sealed class LevelReward
    {
        public int level;
        public int coins;
        /// <summary>Расходная наживка: id и сколько штук.</summary>
        public BaitStock[] baits;
        /// <summary>Приманки спиннинга (по одной штуке, не расходуются).</summary>
        public string[] lures;
        /// <summary>Удилища, катушки, лески.</summary>
        public string[] tackle;
        /// <summary>Бесплатная аренда лодки, минут игры.</summary>
        public int boatMinutes;
    }

    /// <summary>Проверенные награды из levels.json: у каждого уровня со 2-го по последний своя.</summary>
    public sealed class LevelRewardDatabase
    {
        public const int SupportedVersion = 1;

        private readonly LevelReward[] _byLevel = new LevelReward[LevelRules.MaxLevel + 1];

        /// <param name="tackle">Снасти: награда может называть только существующие вещи.</param>
        public LevelRewardDatabase(LevelRewardFile file, TackleDatabase tackle)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (tackle == null)
                throw new ArgumentNullException(nameof(tackle));
            if (file.version != SupportedVersion)
                throw new FormatException("levels.json: ожидалась версия " + SupportedVersion + ", а в файле " + file.version);
            if (file.levels == null)
                throw new FormatException("levels.json: нет списка levels");

            foreach (var reward in file.levels)
            {
                if (reward == null || reward.level < 2 || reward.level > LevelRules.MaxLevel)
                    throw new FormatException("levels.json: уровень награды должен быть от 2 до " + LevelRules.MaxLevel);
                string where = "levels.json, уровень " + reward.level + ": ";
                if (_byLevel[reward.level] != null)
                    throw new FormatException(where + "описан дважды");
                if (reward.coins < 0 || reward.boatMinutes < 0)
                    throw new FormatException(where + "coins и boatMinutes не могут быть меньше 0");
                foreach (var bait in reward.baits ?? Array.Empty<BaitStock>())
                {
                    if (bait == null || bait.count <= 0 || !tackle.TryGetBait(bait.id, out var data) || !data.consumable)
                        throw new FormatException(where + "в baits — только расходная наживка из tackle.json и больше нуля штук");
                }

                foreach (string id in reward.lures ?? Array.Empty<string>())
                {
                    if (!tackle.TryGetBait(id, out var data) || data.consumable)
                        throw new FormatException(where + "в lures — только приманки спиннинга из tackle.json: «" + id + "»");
                }

                foreach (string id in reward.tackle ?? Array.Empty<string>())
                {
                    if (!tackle.TryGetRod(id, out _) && !tackle.TryGetReel(id, out _) && !tackle.TryGetLine(id, out _))
                        throw new FormatException(where + "в tackle — удилище, катушка или леска из tackle.json: «" + id + "»");
                }

                _byLevel[reward.level] = reward;
            }

            for (int level = 2; level <= LevelRules.MaxLevel; level++)
            {
                if (_byLevel[level] == null)
                    throw new FormatException("levels.json: нет награды за уровень " + level);
            }
        }

        /// <summary>Награда за уровень; null — за первый уровень и за пределами таблицы.</summary>
        public LevelReward For(int level)
        {
            return level >= 2 && level <= LevelRules.MaxLevel ? _byLevel[level] : null;
        }
    }

    /// <summary>Что игрок получил за новые уровни (может быть за несколько сразу) — для оповещения.</summary>
    public sealed class RewardReceipt
    {
        public int FromLevel;
        public int ToLevel;
        public long Coins;
        public readonly List<BaitStock> Baits = new List<BaitStock>();
        /// <summary>Приманки, полученные в награду (id).</summary>
        public readonly List<string> Lures = new List<string>();
        /// <summary>Удилища, катушки и лески, полученные в награду (id).</summary>
        public readonly List<string> Tackle = new List<string>();
        public int BoatMinutes;
        /// <summary>Монеты взамен вещей, которые у игрока уже были (половина их цены).</summary>
        public long CompensationCoins;

        public void Add(RewardReceipt other)
        {
            ToLevel = Math.Max(ToLevel, other.ToLevel);
            FromLevel = Math.Min(FromLevel, other.FromLevel);
            Coins += other.Coins;
            foreach (var bait in other.Baits)
                AddBait(bait.id, bait.count);
            Lures.AddRange(other.Lures);
            Tackle.AddRange(other.Tackle);
            BoatMinutes += other.BoatMinutes;
            CompensationCoins += other.CompensationCoins;
        }

        public void AddBait(string id, int count)
        {
            foreach (var bait in Baits)
            {
                if (bait.id == id)
                {
                    bait.count += count;
                    return;
                }
            }

            Baits.Add(new BaitStock { id = id, count = count });
        }
    }
}
