using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>
    /// Проверенный справочник видов рыбы. Битые данные падают сразу при загрузке
    /// с понятным сообщением, а не странным клёвом посреди игры.
    /// </summary>
    public sealed class FishDatabase
    {
        public const int SupportedVersion = 6;

        private static readonly HashSet<string> FightStyles =
            new HashSet<string> { "steady", "jerky", "heavy", "deep", "jumps" };

        private readonly Dictionary<string, FishSpecies> _byId = new Dictionary<string, FishSpecies>();
        private readonly List<FishSpecies> _all = new List<FishSpecies>();

        public FishDatabase(FishFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (file.version != SupportedVersion)
                throw new FormatException($"fish.json: ожидалась версия {SupportedVersion}, а в файле {file.version}");
            if (file.species == null || file.species.Length == 0)
                throw new FormatException("fish.json: список species пуст");

            foreach (var species in file.species)
            {
                Validate(species);
                if (_byId.ContainsKey(species.id))
                    throw new FormatException($"fish.json: вид «{species.id}» описан дважды");
                _byId.Add(species.id, species);
                _all.Add(species);
            }
        }

        public IReadOnlyList<FishSpecies> All => _all;

        public FishSpecies Get(string id)
        {
            if (id != null && _byId.TryGetValue(id, out var species))
                return species;
            throw new KeyNotFoundException($"Нет вида рыбы «{id}» в fish.json");
        }

        public bool TryGet(string id, out FishSpecies species)
        {
            species = null;
            return id != null && _byId.TryGetValue(id, out species);
        }

        private static void Validate(FishSpecies s)
        {
            if (s == null)
                throw new FormatException("fish.json: пустая запись в species");
            if (string.IsNullOrEmpty(s.id))
                throw new FormatException("fish.json: у вида нет id");

            string Prefix() => $"fish.json, «{s.id}»: ";

            var w = s.weightG;
            if (w == null)
                throw new FormatException(Prefix() + "нет weightG");
            if (!(w.min > 0 && w.min <= w.typical && w.typical < w.trophy && w.trophy <= w.max))
                throw new FormatException(Prefix() + "веса должны идти так: 0 < min ≤ typical < trophy ≤ max");
            if (s.weightG.qualifying < s.weightG.typical || s.weightG.qualifying > s.weightG.trophy)
                throw new FormatException(Prefix() + "зачётный вес должен быть между typical и trophy");
            if (s.trophyChance < 0f || s.trophyChance > 1f)
                throw new FormatException(Prefix() + "trophyChance должен быть от 0 до 1");

            var d = s.depthCm;
            if (d == null || d.min < 0 || d.min > d.max)
                throw new FormatException(Prefix() + "depthCm должен быть 0 ≤ min ≤ max");

            if (s.activeHours == null || s.activeHours.Length == 0)
                throw new FormatException(Prefix() + "нет activeHours");
            foreach (var hour in s.activeHours)
                if (hour < 0 || hour > 23)
                    throw new FormatException(Prefix() + $"час {hour} вне диапазона 0–23");

            if (s.baits == null || s.baits.Length == 0)
                throw new FormatException(Prefix() + "нет baits");
            var baitIds = new HashSet<string>();
            foreach (var bait in s.baits)
            {
                if (bait == null || string.IsNullOrEmpty(bait.id))
                    throw new FormatException(Prefix() + "в baits запись без id");
                if (bait.taste < 1 || bait.taste > 100)
                    throw new FormatException(Prefix() + $"вкус наживки «{bait.id}» должен быть от 1 до 100");
                if (!baitIds.Add(bait.id))
                    throw new FormatException(Prefix() + $"наживка «{bait.id}» указана дважды");
            }
            if (s.biteRatePerMin <= 0f)
                throw new FormatException(Prefix() + "biteRatePerMin должен быть больше 0");
            if (s.maxLineMm <= 0f)
                throw new FormatException(Prefix() + "maxLineMm должен быть больше 0");
            if (s.strength < 1 || s.strength > 10)
                throw new FormatException(Prefix() + "strength должен быть от 1 до 10");
            if (s.stamina < 1 || s.stamina > 10)
                throw new FormatException(Prefix() + "stamina должен быть от 1 до 10");
            if (s.fightStyle == null || !FightStyles.Contains(s.fightStyle))
                throw new FormatException(Prefix() + $"неизвестный fightStyle «{s.fightStyle}»");
            if (s.pricePerKg <= 0)
                throw new FormatException(Prefix() + "pricePerKg должен быть больше 0");
        }
    }
}
