using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    // Классы для чтения shared/data/transport.json. Имена полей совпадают с ключами JSON.
    // Описание полей — shared/data/README.md, TypeScript-двойник — server/modules/src/data.ts.

    [Serializable]
    public sealed class TransportFile
    {
        public int version;
        public BoatData[] boats;
    }

    /// <summary>Лодка: открывает места на воде (LakeSpot.needsBoat). Её покупают насовсем или берут в аренду на время.</summary>
    [Serializable]
    public sealed class BoatData
    {
        public string id;
        /// <summary>Цена покупки, монет.</summary>
        public int price;
        /// <summary>Цена одной аренды, монет.</summary>
        public int rentPrice;
        /// <summary>На сколько минут игры одна аренда.</summary>
        public int rentMinutes;
    }

    /// <summary>Проверенный транспорт из transport.json.</summary>
    public sealed class TransportDatabase
    {
        public const int SupportedVersion = 1;

        public TransportDatabase(TransportFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (file.version != SupportedVersion)
                throw new FormatException("transport.json: ожидалась версия " + SupportedVersion + ", а в файле " + file.version);
            if (file.boats == null || file.boats.Length == 0)
                throw new FormatException("transport.json: список boats пуст");

            var seen = new HashSet<string>();
            foreach (var boat in file.boats)
            {
                if (boat == null || string.IsNullOrEmpty(boat.id))
                    throw new FormatException("transport.json: у лодки нет id");
                string where = "transport.json: лодка «" + boat.id + "»: ";
                if (!seen.Add(boat.id))
                    throw new FormatException(where + "описана дважды");
                if (boat.price <= 0)
                    throw new FormatException(where + "price должен быть больше 0");
                if (boat.rentPrice <= 0 || boat.rentPrice >= boat.price)
                    throw new FormatException(where + "rentPrice должен быть больше 0 и меньше price");
                if (boat.rentMinutes <= 0)
                    throw new FormatException(where + "rentMinutes должен быть больше 0");
            }

            Boats = file.boats;
        }

        /// <summary>Лодки в порядке файла; первая — та, что стоит у причала турбазы.</summary>
        public IReadOnlyList<BoatData> Boats { get; }
    }
}
