using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Рыба в садке. Имена полей — ключи JSON сохранения, поэтому в camelCase.</summary>
    [Serializable]
    public sealed class CaughtFish
    {
        /// <summary>Номер рыбы в профиле игрока: растёт с каждой пойманной, не повторяется.</summary>
        public int id;
        public string speciesId;
        public int weightG;
        public int lengthCm;
        public bool trophy;
        /// <summary>Игровое время поимки, минуты от полуночи.</summary>
        public int caughtAtMinute;

        // Где и на что поймана (у рыбы из старых сохранений этих полей нет — пусто и 0).
        /// <summary>Место ловли (id из locations.json).</summary>
        public string spotId;
        /// <summary>Наживка (id).</summary>
        public string baitId;
        /// <summary>Глубина наживки в момент поклёвки, см.</summary>
        public int baitDepthCm;
        /// <summary>Удилище и леска (id из tackle.json).</summary>
        public string rodId;
        public string lineId;
    }

    /// <summary>Обстоятельства поимки: место, наживка, глубина, снасть — записываются в рыбу, когда её кладут в садок.</summary>
    public readonly struct CatchContext
    {
        public readonly string SpotId;
        public readonly string BaitId;
        public readonly int BaitDepthCm;
        public readonly string RodId;
        public readonly string LineId;

        public CatchContext(string spotId, string baitId, int baitDepthCm, string rodId, string lineId)
        {
            SpotId = spotId;
            BaitId = baitId;
            BaitDepthCm = baitDepthCm;
            RodId = rodId;
            LineId = lineId;
        }
    }

    /// <summary>
    /// Садок: рыба лежит в порядке поимки, предел — по общему весу. Рыба входит, если её вес не больше
    /// свободного места; в пустой садок входит любая — иначе трофей тяжелее всего садка нельзя было бы ни оставить,
    /// ни продать.
    /// </summary>
    public sealed class Keepnet
    {
        private readonly List<CaughtFish> _fish = new List<CaughtFish>();

        public Keepnet(int capacityG)
        {
            if (capacityG <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacityG), "Вместимость садка должна быть больше нуля");
            CapacityG = capacityG;
        }

        public int CapacityG { get; }
        public IReadOnlyList<CaughtFish> Fish => _fish;
        public int Count => _fish.Count;
        public int TotalWeightG { get; private set; }

        /// <summary>Свободное место, г. Ноль, если единственная рыба тяжелее всего садка.</summary>
        public int FreeG => Math.Max(0, CapacityG - TotalWeightG);

        public bool Fits(int weightG) => weightG > 0 && (_fish.Count == 0 || weightG <= FreeG);

        /// <summary>Кладёт рыбу в садок. false — не влезла, садок не изменился.</summary>
        public bool TryAdd(CaughtFish fish)
        {
            if (fish == null)
                throw new ArgumentNullException(nameof(fish));
            if (!Fits(fish.weightG))
                return false;
            _fish.Add(fish);
            TotalWeightG += fish.weightG;
            return true;
        }

        public CaughtFish Find(int id)
        {
            foreach (var fish in _fish)
            {
                if (fish.id == id)
                    return fish;
            }
            return null;
        }

        /// <summary>Достаёт рыбу из садка по номеру; null — такой нет.</summary>
        public CaughtFish Remove(int id)
        {
            for (int i = 0; i < _fish.Count; i++)
            {
                if (_fish[i].id != id)
                    continue;
                var fish = _fish[i];
                _fish.RemoveAt(i);
                TotalWeightG -= fish.weightG;
                return fish;
            }
            return null;
        }

        /// <summary>Достаёт всю рыбу; садок остаётся пустым.</summary>
        public List<CaughtFish> RemoveAll()
        {
            var all = new List<CaughtFish>(_fish);
            _fish.Clear();
            TotalWeightG = 0;
            return all;
        }
    }
}
