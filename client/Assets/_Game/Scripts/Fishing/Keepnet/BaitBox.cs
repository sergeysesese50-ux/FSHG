using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Запись запаса наживки в сохранении профиля: id из tackle.json и сколько штук.</summary>
    [Serializable]
    public sealed class BaitStock
    {
        public string id;
        public int count;
    }

    /// <summary>Правила наживки: упаковки, запас новичка, копка червей, живец из мелкой рыбы.</summary>
    public static class BaitRules
    {
        /// <summary>Штук в упаковке: цена наживки в tackle.json — за упаковку.</summary>
        public const int PackSize = 10;

        /// <summary>Больше этого одной наживки с собой не унести.</summary>
        public const int MaxCount = 999;

        public const string WormId = "worm";
        public const string LiveBaitId = "livebait";

        /// <summary>Червей можно накопать бесплатно, пока их меньше DigWormsBelow: без наживки игрок не останется.</summary>
        public const int DigWormsCount = 10;
        public const int DigWormsBelow = 10;

        /// <summary>Рыба не тяжелее этого годится на живца (как в «Русской рыбалке 3»: живец — из мелочи).</summary>
        public const int LiveBaitMaxWeightG = 100;

        /// <summary>С чем начинает новый игрок (и игрок со старым сохранением, где наживки ещё не было).</summary>
        public static BaitStock[] StarterStock()
        {
            return new[]
            {
                new BaitStock { id = WormId, count = 30 },
                new BaitStock { id = "bread", count = 20 },
                new BaitStock { id = "maggot", count = 10 },
            };
        }

        public static bool CanBeLiveBait(int weightG) => weightG > 0 && weightG <= LiveBaitMaxWeightG;
    }

    /// <summary>
    /// Коробка с наживкой: сколько какой есть и какая сейчас на крючке. Одна поклёвка съедает одну штуку
    /// выбранной наживки. Порядок записей — в каком наживка появилась у игрока.
    /// </summary>
    public sealed class BaitBox
    {
        private readonly List<BaitStock> _items = new List<BaitStock>();

        public IReadOnlyList<BaitStock> Items => _items;

        /// <summary>Наживка на крючке; null — не выбрана. Может остаться выбранной и при нуле штук.</summary>
        public string SelectedId { get; private set; }

        public int SelectedCount => Count(SelectedId);

        public int Count(string id)
        {
            var item = Find(id);
            return item != null ? item.count : 0;
        }

        /// <summary>Выбирает наживку. false — такой нет в запасе.</summary>
        public bool Select(string id)
        {
            if (Count(id) <= 0)
                return false;
            SelectedId = id;
            return true;
        }

        /// <summary>Кладёт наживку в коробку (не больше BaitRules.MaxCount). Возвращает, сколько штук поместилось.</summary>
        public int Add(string id, int count)
        {
            if (string.IsNullOrEmpty(id) || count <= 0)
                return 0;
            var item = Find(id);
            if (item == null)
            {
                item = new BaitStock { id = id };
                _items.Add(item);
            }

            int added = Math.Min(count, BaitRules.MaxCount - item.count);
            if (added <= 0)
                return 0;
            item.count += added;
            if (SelectedCount <= 0)
                SelectedId = id;
            return added;
        }

        /// <summary>Поклёвка съела наживку с крючка. false — наживки не было.</summary>
        public bool UseSelected()
        {
            var item = Find(SelectedId);
            if (item == null || item.count <= 0)
                return false;
            item.count--;
            return true;
        }

        public BaitStock[] ToState()
        {
            var state = new BaitStock[_items.Count];
            for (int i = 0; i < state.Length; i++)
                state[i] = new BaitStock { id = _items[i].id, count = _items[i].count };
            return state;
        }

        /// <summary>
        /// Из сохранения: пустые и повторные записи пропускаются, число приводится к 0…MaxCount. Если выбранной
        /// наживки нет в запасе, выбирается первая, что есть.
        /// </summary>
        public static BaitBox FromState(BaitStock[] items, string selectedId)
        {
            var box = new BaitBox();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null || string.IsNullOrEmpty(item.id) || box.Find(item.id) != null)
                        continue;
                    box._items.Add(new BaitStock { id = item.id, count = Math.Max(0, Math.Min(item.count, BaitRules.MaxCount)) });
                }
            }

            if (!box.Select(selectedId))
            {
                foreach (var item in box._items)
                {
                    if (box.Select(item.id))
                        break;
                }
            }

            return box;
        }

        public static BaitBox Starter()
        {
            var starter = BaitRules.StarterStock();
            return FromState(starter, starter[0].id);
        }

        private BaitStock Find(string id)
        {
            if (id == null)
                return null;
            foreach (var item in _items)
            {
                if (item.id == id)
                    return item;
            }

            return null;
        }
    }
}
