using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Проверенный справочник снастей из tackle.json.</summary>
    public sealed class TackleDatabase
    {
        public const int SupportedVersion = 5;

        private static readonly HashSet<string> RodTypes = new HashSet<string> { RodKinds.Float, RodKinds.Bottom, RodKinds.Spinning };

        private readonly Dictionary<string, RodData> _rods = new Dictionary<string, RodData>();
        private readonly Dictionary<string, ReelData> _reels = new Dictionary<string, ReelData>();
        private readonly Dictionary<string, LineData> _lines = new Dictionary<string, LineData>();
        private readonly Dictionary<string, BaitData> _baits = new Dictionary<string, BaitData>();

        public TackleDatabase(TackleFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (file.version != SupportedVersion)
                throw new FormatException($"tackle.json: ожидалась версия {SupportedVersion}, а в файле {file.version}");

            AddAll(file.rods, "rods", _rods, r => r.id, ValidateRod);
            AddAll(file.reels, "reels", _reels, r => r.id, ValidateReel);
            AddAll(file.lines, "lines", _lines, l => l.id, ValidateLine);
            AddAll(file.baits, "baits", _baits, b => b.id, ValidateBait);
            Baits = file.baits;
            Rods = file.rods;
            Reels = file.reels;
            Lines = file.lines;
        }

        /// <summary>Удилища, катушки и лески в порядке файла — для магазина.</summary>
        public IReadOnlyList<RodData> Rods { get; }
        public IReadOnlyList<ReelData> Reels { get; }
        public IReadOnlyList<LineData> Lines { get; }

        public bool TryGetRod(string id, out RodData rod)
        {
            rod = null;
            return id != null && _rods.TryGetValue(id, out rod);
        }

        public bool TryGetReel(string id, out ReelData reel)
        {
            reel = null;
            return id != null && _reels.TryGetValue(id, out reel);
        }

        public bool TryGetLine(string id, out LineData line)
        {
            line = null;
            return id != null && _lines.TryGetValue(id, out line);
        }

        /// <summary>Все наживки и приманки в порядке файла — для магазина.</summary>
        public IReadOnlyList<BaitData> Baits { get; }

        public bool TryGetBait(string id, out BaitData bait)
        {
            bait = null;
            return id != null && _baits.TryGetValue(id, out bait);
        }

        public RodData GetRod(string id) => Get(_rods, id, "удилища");
        public ReelData GetReel(string id) => Get(_reels, id, "катушки");
        public LineData GetLine(string id) => Get(_lines, id, "лески");
        public BaitData GetBait(string id) => Get(_baits, id, "наживки");

        private static T Get<T>(Dictionary<string, T> items, string id, string what)
        {
            if (id != null && items.TryGetValue(id, out var item))
                return item;
            throw new KeyNotFoundException($"Нет {what} «{id}» в tackle.json");
        }

        private static void AddAll<T>(T[] items, string section, Dictionary<string, T> target,
            Func<T, string> idOf, Action<T, string> validate)
        {
            if (items == null || items.Length == 0)
                throw new FormatException($"tackle.json: раздел {section} пуст");
            foreach (var item in items)
            {
                if (item == null)
                    throw new FormatException($"tackle.json: пустая запись в {section}");
                string id = idOf(item);
                if (string.IsNullOrEmpty(id))
                    throw new FormatException($"tackle.json: запись без id в {section}");
                string prefix = $"tackle.json, {section}, «{id}»: ";
                validate(item, prefix);
                if (target.ContainsKey(id))
                    throw new FormatException(prefix + "описан дважды");
                target.Add(id, item);
            }
        }

        private static void CheckLevel(int minLevel, string prefix)
        {
            if (minLevel < 0 || minLevel > LevelRules.MaxLevel)
                throw new FormatException(prefix + "minLevel должен быть от 0 до " + LevelRules.MaxLevel);
        }

        private static void ValidateRod(RodData r, string prefix)
        {
            if (!RodTypes.Contains(r.type ?? ""))
                throw new FormatException(prefix + $"неизвестный type «{r.type}»");
            if (r.lengthCm <= 0 || r.maxLoadG <= 0 || r.castRangeCm <= 0)
                throw new FormatException(prefix + "lengthCm, maxLoadG и castRangeCm должны быть больше 0");
            if (r.flex < 0f || r.flex > 1f)
                throw new FormatException(prefix + "flex должен быть от 0 до 1");
            CheckLevel(r.minLevel, prefix);
        }

        private static void ValidateReel(ReelData r, string prefix)
        {
            if (r.maxDragG <= 0 || r.retrieveCmPerSec <= 0f || r.spoolCapacityM <= 0)
                throw new FormatException(prefix + "maxDragG, retrieveCmPerSec и spoolCapacityM должны быть больше 0");
            if (r.fightSpeedCmPerSec <= 0f || r.powerG <= 0)
                throw new FormatException(prefix + "fightSpeedCmPerSec и powerG должны быть больше 0");
            CheckLevel(r.minLevel, prefix);
        }

        private static void ValidateLine(LineData l, string prefix)
        {
            if (l.diameterMm <= 0f || l.breakingLoadG <= 0)
                throw new FormatException(prefix + "diameterMm и breakingLoadG должны быть больше 0");
            CheckLevel(l.minLevel, prefix);
        }

        private static void ValidateBait(BaitData b, string prefix)
        {
            if (!RodTypes.Contains(b.rodType ?? ""))
                throw new FormatException(prefix + $"неизвестный rodType «{b.rodType}»");
            if (b.price <= 0)
                throw new FormatException(prefix + "price должен быть больше 0");
            if (b.rodType == RodKinds.Bottom)
                throw new FormatException(prefix + "rodType наживки — float или spinning (донка берёт наживку float)");
            bool lure = b.rodType == RodKinds.Spinning;
            if (!(lure ? BaitKinds.IsLure(b.kind) : BaitKinds.IsNatural(b.kind)))
                throw new FormatException(prefix + $"kind «{b.kind}» не подходит: у наживки — animal, plant или live, у приманки — spinner, spoon, soft или wobbler");
            if (lure && !LureRuns.IsKnown(b.run))
                throw new FormatException(prefix + $"неизвестный ход приманки run «{b.run}»: top, surface, mid или bottom");
            CheckLevel(b.minLevel, prefix);
        }
    }
}
