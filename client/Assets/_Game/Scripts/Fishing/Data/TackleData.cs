using System;

namespace Fishing.Core
{
    // Классы для чтения shared/data/tackle.json. Имена полей совпадают с ключами JSON.
    // Описание полей — shared/data/README.md, TypeScript-двойник — server/modules/src/data.ts.

    [Serializable]
    public sealed class TackleFile
    {
        public int version;
        public RodData[] rods;
        public ReelData[] reels;
        public LineData[] lines;
        public BaitData[] baits;
    }

    [Serializable]
    public sealed class RodData
    {
        public string id;
        public string type;
        public int lengthCm;
        public int maxLoadG;
        public float flex;
        public int castRangeCm;
        public int price;
        /// <summary>С какого уровня игрока вещь продаётся (0 или нет поля — с первого).</summary>
        public int minLevel;
    }

    [Serializable]
    public sealed class ReelData
    {
        public string id;
        public int maxDragG;
        public float retrieveCmPerSec;
        public float fightSpeedCmPerSec;
        public int powerG;
        public int spoolCapacityM;
        public int price;
        public int minLevel;
    }

    [Serializable]
    public sealed class LineData
    {
        public string id;
        public float diameterMm;
        public int breakingLoadG;
        public int price;
        public int minLevel;
        /// <summary>Плетёный шнур: тонкий и прочный (в магазине — отдельная полка).</summary>
        public bool braid;
    }

    [Serializable]
    public sealed class BaitData
    {
        public string id;
        public string rodType;
        /// <summary>
        /// Род: у наживки — animal (животная), plant (растительная), live (для хищника); у приманки — spinner
        /// (вертушка), spoon (колебалка), soft (силикон), wobbler (воблер). По нему раскладываются полки магазина.
        /// </summary>
        public string kind;
        /// <summary>Ход приманки спиннинга: top (по самой поверхности), surface (до 1,2 м), mid (в полводы), bottom (у дна).</summary>
        public string run;
        public bool consumable;
        public int price;
        public int minLevel;
    }
}
