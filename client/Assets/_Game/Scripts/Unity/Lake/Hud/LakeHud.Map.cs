using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Карта озера: выбор места ловли, как в «Русской рыбалке 3». Картинка — Resources/Art/LakeMap.png
    // (tools/art/lake_map.py), метки мест и карточка выбранного места рисуются здесь.
    // Открывается с причала турбазы и кнопкой «Места» на озере, пока удочка не заброшена.
    // Правила мест (глубина, рыба, с какого уровня открыто, нужна ли лодка) — в shared/data/locations.json;
    // здесь названия, описания и положение на карте. Закрытое место подписано тем, чего не хватает.
    // Лодку для мест на воде берут здесь же, если карта открыта с причала турбазы: аренда на время или покупка
    // (правила — PlayerProfile, цены — shared/data/transport.json).
    public sealed partial class LakeHud
    {
        private const string MapArtPath = "Art/LakeMap";
        private const int MaxSpots = 8;

        /// <summary>Дальность заброса для подсказки «глубина до…», м.</summary>
        private const float MapCastRangeM = 25f;

        private bool _mapOpen;
        private int _mapPick = -1;
        private Texture2D _mapArt;
        private bool _mapArtMissing;
        private Rect _mapButtonRect;
        private readonly Rect[] _mapMarkerRects = new Rect[MaxSpots];
        private int _rentTextKey = -1;
        private string _rentText;

        // ---------- Кнопка на озере ----------

        /// <summary>Круглая кнопка «Места» справа от садка: только пока удочка не заброшена.</summary>
        private void MapButton(Rect area)
        {
            float s = _scale;
            if (_keepnetButtonRect.width <= 0f || _game.Spots == null)
                return;
            float d = SideButtonSize(area);
            var rect = new Rect(_keepnetButtonRect.xMax + SideButtonGap * s, _keepnetButtonRect.yMax - d, d, d);
            _mapButtonRect = rect;
            if (Tapped(rect))
                OpenMap();

            if (!_drawing)
                return;
            RoundButton(rect, Glass, GlassBorder, 1.5f * s, new Color(0f, 0f, 0f, 0f));
            var pin = new Vector2(rect.center.x, rect.y + d * 0.30f);
            Circle(pin, d * 0.11f, 2f * s, Color.white);
            Bar(pin + new Vector2(0f, d * 0.11f), pin + new Vector2(0f, d * 0.26f), 2f * s, Color.white);
            Label(new Rect(rect.x, rect.y + d * 0.58f, d, 16 * s), "Места", _tiny, Color.white);
        }

        private void OpenMap()
        {
            _mapOpen = true;
            _swiping = false;
            _mapPick = -1;
            var spots = _game.Spots;
            if (spots == null || _game.Spot == null)
                return;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i].id == _game.Spot.id)
                    _mapPick = i;
            }
        }

        // ---------- Экран карты ----------

        private void MapScreen(Rect area)
        {
            var spots = _game.Spots;
            if (spots == null)
            {
                _mapOpen = false;
                return;
            }

            if (_mapArt == null && !_mapArtMissing)
            {
                _mapArt = Resources.Load<Texture2D>(MapArtPath);
                _mapArtMissing = _mapArt == null;
            }

            Backdrop(_mapArt, CampGrass, CampGrass);
            float s = _scale;
            float u = _campUnit;

            // Закрыть: назад туда, откуда пришли (турбаза или озеро).
            var close = new Rect(area.xMax - 44 * s, area.y, 44 * s, 44 * s);
            if (Tapped(close))
            {
                _mapOpen = false;
                return;
            }

            if (_drawing)
            {
                string title = "Озеро Тихое";
                float tw = TextWidth(title, _title) + 34 * s;
                var head = new Rect(area.x, area.y, tw, 44 * s);
                LightPill(head, 22 * s);
                Label(new Rect(head.x + 17 * s, head.y, tw - 20 * s, head.height), title, _title, InkTitle);
                // Монеты и лодка — под заголовком: видно, на что хватает и сколько осталось аренды.
                var profile = _game.Profile;
                if (profile != null)
                {
                    string boat = profile.OwnsBoat ? "лодка своя"
                        : profile.BoatRentSecondsLeft > 0.0 ? "лодка: аренда " + RentText(profile.BoatRentSecondsLeft)
                        : "лодки нет";
                    string money = MoneyText(profile.Money);
                    float mw = TextWidth(money, _word);
                    float bw = TextWidth(boat, _word);
                    var info = new Rect(area.x, head.yMax + 8 * s, 30 * s + mw + 14 * s + bw + 16 * s, 32 * s);
                    LightPill(info, 16 * s);
                    Coin(new Vector2(info.x + 16 * s, info.center.y), 6.5f * s);
                    Label(new Rect(info.x + 28 * s, info.y, mw + 4 * s, info.height), money, _word, Ink);
                    Label(new Rect(info.x + 28 * s + mw + 14 * s, info.y, bw + 6 * s, info.height), boat, _word, profile.HasBoat ? Leaf : InkSoft);
                }

                LightPill(close, 22 * s);
                var c = close.center;
                float k = 6.5f * s;
                Bar(c + new Vector2(-k, -k), c + new Vector2(k, k), 2.4f * s, Ink);
                Bar(c + new Vector2(-k, k), c + new Vector2(k, -k), 2.4f * s, Ink);
            }

            // Метки мест: точка на берегу и подпись над ней.
            int count = Mathf.Min(spots.Length, MaxSpots);
            for (int i = 0; i < count; i++)
            {
                if (!_drawing)
                {
                    if (Tapped(_mapMarkerRects[i]))
                        _mapPick = i;
                    continue;
                }

                var spot = spots[i];
                bool picked = i == _mapPick;
                bool here = _game.Spot != null && _game.Spot.id == spot.id;
                var place = SpotPlace(spot.id, i);
                var dot = new Vector2(_campOrigin.x + place.x * u, _campOrigin.y + place.y * u);
                string name = SpotName(spot.id);
                var access = _game.AccessTo(spot);
                // Закрытое место: чего не хватает. Место на воде без лодки подписано «лодка», даже если мал и уровень.
                if (spot.needsBoat && (_game.Profile == null || !_game.Profile.HasBoat))
                    name += " · лодка";
                else if (access == SpotAccess.NeedsLevel)
                    name += " · ур. " + spot.minLevel;
                float w = TextWidth(name, _word) + 28 * s;
                float h = 32 * s;
                float x = Mathf.Clamp(dot.x - w / 2, 8 * s, Mathf.Max(8 * s, Screen.width - w - 8 * s));
                var pill = new Rect(x, dot.y - h - 10 * s, w, h);
                // Нажимается и подпись, и точка под ней.
                _mapMarkerRects[i] = Rect.MinMaxRect(Mathf.Min(pill.x, dot.x - 22 * s), pill.y, Mathf.Max(pill.xMax, dot.x + 22 * s), dot.y + 22 * s);

                Disc(dot, 9 * s, Color.white);
                bool locked = access != SpotAccess.Open;
                Disc(dot, 6 * s, here ? new Color(0.16f, 0.62f, 0.32f) : locked ? Slate : Coral);

                if (picked)
                {
                    Round(new Rect(pill.x - 2 * s, pill.y - 2 * s, pill.width + 4 * s, pill.height + 4 * s), Color.white, h / 2 + 2 * s);
                    Round(pill, Coral, h / 2);
                    Label(new Rect(pill.x + 14 * s, pill.y, w - 14 * s, h), name, _word, Color.white);
                }
                else
                {
                    LightPill(pill, h / 2);
                    Label(new Rect(pill.x + 14 * s, pill.y, w - 14 * s, h), name, _word, locked ? InkSoft : Ink);
                }
            }

            SpotCard(area, spots);
        }

        /// <summary>Карточка выбранного места внизу: описание, глубина, рыба и кнопка «Ловить здесь».</summary>
        private void SpotCard(Rect area, LakeSpot[] spots)
        {
            float s = _scale;
            float w = Mathf.Min(area.width, 380 * s);
            float h = 196 * s;
            var card = new Rect(area.center.x - w / 2, area.yMax - h, w, h);
            float x = card.x + 18 * s;
            float cw = w - 36 * s;
            bool chosen = _mapPick >= 0 && _mapPick < spots.Length;
            var go = new Rect(x, card.yMax - 58 * s, cw, 44 * s);

            // Место на воде без лодки (уровня хватает): вместо «Ловить здесь» — «Аренда» и «Купить».
            // Лодку берут у причала турбазы; с озера карта только подсказывает, куда идти.
            var boat = _game.Boat;
            bool offer = chosen && boat != null && _game.AccessTo(spots[_mapPick]) == SpotAccess.NeedsBoat;
            float half = (cw - 10 * s) / 2f;
            var rent = new Rect(x, card.yMax - 64 * s, half, 52 * s);
            var buy = new Rect(rent.xMax + 10 * s, rent.y, half, 52 * s);
            if (offer && _campOpen)
            {
                if (Tapped(rent))
                {
                    if (_game.RentBoat() && _feedback != null)
                        _feedback.PlaySell(1);
                    return;
                }

                if (Tapped(buy))
                {
                    if (_game.BuyBoat() && _feedback != null)
                        _feedback.PlaySell(4);
                    return;
                }
            }

            // Лодка в аренде, место на воде, карта открыта с причала: рядом с «Ловить здесь» — «Продлить».
            var renter = _game.Profile;
            bool extend = chosen && _campOpen && boat != null && spots[_mapPick].needsBoat && _game.SpotOpen(spots[_mapPick])
                          && renter != null && !renter.OwnsBoat;
            var more = new Rect(x + cw * 0.60f + 10 * s, rent.y, cw * 0.40f - 10 * s, 52 * s);
            if (extend)
            {
                go = new Rect(x, rent.y, cw * 0.60f, 52 * s);
                if (Tapped(more))
                {
                    if (_game.RentBoat() && _feedback != null)
                        _feedback.PlaySell(1);
                    return;
                }
            }

            if (chosen && _game.SpotOpen(spots[_mapPick]) && Tapped(go))
            {
                _game.SetSpot(spots[_mapPick].id);
                _mapOpen = false;
                _campOpen = false;
                return;
            }

            if (!_drawing)
                return;
            LightPill(card, 22 * s);
            if (!chosen)
            {
                Label(new Rect(x, card.y, cw, h), "Нажми на место на карте", _coachStyle, InkSoft);
                return;
            }

            var spot = spots[_mapPick];
            bool here = _game.Spot != null && _game.Spot.id == spot.id;
            Label(new Rect(x, card.y + 12 * s, cw, 30 * s), SpotName(spot.id), _title, InkTitle);
            Label(new Rect(x, card.y + 46 * s, cw, 38 * s), SpotText(spot.id), _paragraph, Ink);

            float reach = Mathf.Min(spot.maxDepthCm, spot.shoreDepthCm + MapCastRangeM * spot.slopeCmPerM);
            string depth = "Глубина: от " + Kg(spot.shoreDepthCm * 10) + " до " + Kg(Mathf.RoundToInt(reach) * 10) + " м";
            Label(new Rect(x, card.y + 88 * s, cw, 18 * s), depth, _smallLeft, InkSoft);
            string fish = "";
            for (int i = 0; i < spot.fish.Length; i++)
                fish += (i > 0 ? " · " : "") + FishName(spot.fish[i]);
            Label(new Rect(x, card.y + 106 * s, cw, 18 * s), "Рыба: " + fish, _smallLeft, InkSoft);

            if (offer && _campOpen)
            {
                long money = _game.Profile.Money;
                BoatButton(rent, "Аренда " + boat.rentMinutes + " мин", boat.rentPrice, money >= boat.rentPrice, false);
                BoatButton(buy, "Купить лодку", boat.price, money >= boat.price, true);
                return;
            }

            if (offer)
            {
                Round(go, new Color(0.08f, 0.20f, 0.25f, 0.12f), 22 * s);
                Label(go, "Нужна лодка — «Лодки» на турбазе", _center, InkSoft);
                return;
            }

            if (!_game.SpotOpen(spot))
            {
                bool level = _game.Profile != null && _game.Profile.Level < spot.minLevel;
                bool noBoat = spot.needsBoat && (_game.Profile == null || !_game.Profile.HasBoat);
                string need = level && noBoat ? "Нужны лодка и " + spot.minLevel + " уровень"
                    : noBoat ? "Нужна лодка"
                    : "Откроется на " + spot.minLevel + " уровне";
                Round(go, new Color(0.08f, 0.20f, 0.25f, 0.12f), 22 * s);
                Label(go, need, _center, InkSoft);
                return;
            }

            if (extend)
                BoatButton(more, "+" + boat.rentMinutes + " мин", boat.rentPrice, renter.Money >= boat.rentPrice, false);
            Round(go, Coral, Mathf.Min(22 * s, go.height / 2));
            Label(go, here ? "Остаться здесь" : "Ловить здесь", _center, Color.white);
        }

        /// <summary>Кнопка лодки: что и за сколько; серая, если не хватает монет или она закрыта (locked).</summary>
        private void BoatButton(Rect rect, string title, int price, bool enough, bool main, string locked = null)
        {
            float s = _scale;
            // Закрыта (locked — почему): серая, вместо цены — причина.
            if (locked != null)
            {
                Round(rect, new Color(0.08f, 0.20f, 0.25f, 0.12f), 18 * s);
                Label(new Rect(rect.x, rect.y + 6 * s, rect.width, 22 * s), title, _center, InkSoft);
                Label(new Rect(rect.x, rect.y + 29 * s, rect.width, 16 * s), locked, _tiny, Coral);
                return;
            }

            if (!enough)
            {
                Round(rect, new Color(0.08f, 0.20f, 0.25f, 0.12f), 18 * s);
            }
            else if (main)
            {
                Round(new Rect(rect.x, rect.y + 2 * s, rect.width, rect.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), 18 * s);
                Round(rect, Coral, 18 * s);
            }
            else
            {
                Round(rect, new Color(0.18f, 0.50f, 0.82f, 0.14f), 18 * s);
                Ring(rect, new Color(0.18f, 0.50f, 0.82f, 0.55f), 1.4f * s, 18 * s);
            }

            var ink = !enough ? InkSoft : main ? Color.white : Ink;
            Label(new Rect(rect.x, rect.y + 6 * s, rect.width, 22 * s), title, _center, ink);
            string sub = enough ? MoneyNumber(price) : MoneyNumber(price) + " · не хватает";
            float subWidth = TextWidth(sub, _tiny);
            float subX = rect.center.x - subWidth / 2 + 8 * s;
            Coin(new Vector2(subX - 9 * s, rect.y + 37 * s), 5f * s);
            Label(new Rect(subX, rect.y + 29 * s, subWidth + 4 * s, 16 * s), sub, _tiny, ink);
        }

        /// <summary>Остаток аренды словами: «24 мин», в последнюю минуту — «меньше минуты».</summary>
        private string RentText(double secondsLeft)
        {
            int minutes = (int)(secondsLeft / 60.0);
            if (minutes != _rentTextKey || _rentText == null)
            {
                _rentTextKey = minutes;
                _rentText = minutes >= 1 ? minutes + " мин" : "меньше минуты";
            }
            return _rentText;
        }

        // ---------- Названия, описания, место на карте ----------

        private static string SpotName(string id)
        {
            switch (id)
            {
                case "maple": return "Под клёном";
                case "reeds": return "Камыши";
                case "old_pier": return "Старый причал";
                case "pit": return "Яма";
                case "stream": return "Устье ручья";
                case "island": return "Остров";
                case "center": return "Центр озера";
                case "backwater": return "Затон";
                default: return id ?? "Озеро";
            }
        }

        private static string SpotText(string id)
        {
            switch (id)
            {
                case "maple": return "Тихий мелкий заливчик у турбазы. Плотва и карась — хорошее место для начала.";
                case "reeds": return "Заросли вдоль берега. Карась и плотва, а в засаде стоит щука.";
                case "old_pier": return "Сваи и средняя глубина. Окунь и плотва, ночью подходит лещ.";
                case "pit": return "Самое глубокое место озера. Лещ и судак — здесь берёт крупная рыба.";
                case "stream": return "Свежая вода из ручья. Окунь и плотва, в сумерках — судак.";
                case "island": return "Отмель и свал у островка посреди воды. Окунь, лещ и щука. Только на лодке.";
                case "center": return "Открытая вода над самой глубиной. Крупный лещ, судак и щука. Только на лодке.";
                case "backwater": return "Заросший залив за топью — с берега не подойти. Карась и щука. Только на лодке.";
                default: return "";
            }
        }

        /// <summary>Точка места на карте, единицы сцены — как SPOTS в tools/art/lake_map.py.</summary>
        private static Vector2 SpotPlace(string id, int index)
        {
            switch (id)
            {
                case "maple": return new Vector2(88, 612);
                case "reeds": return new Vector2(30, 400);
                case "old_pier": return new Vector2(356, 470);
                case "pit": return new Vector2(205, 128);
                case "stream": return new Vector2(348, 180);
                case "island": return new Vector2(270, 336);
                case "center": return new Vector2(192, 404);
                case "backwater": return new Vector2(48, 222);
                default: return new Vector2(120 + 40 * index, 380);
            }
        }
    }
}
