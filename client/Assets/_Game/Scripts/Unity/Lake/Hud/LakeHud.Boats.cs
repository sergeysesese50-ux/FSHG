using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Лодочная станция турбазы (будка с лодками на берегу, между причалом и магазином снастей): аренда лодки
    // на время и покупка насовсем. Экран сразу показывает, что у игрока с лодкой сейчас и чего не хватает
    // для каждого места на воде — уровня, лодки или того и другого.
    // Правила — PlayerProfile (BuyBoat, RentBoat), цены — shared/data/transport.json, места — locations.json.
    // Картинка лодки — Resources/Art/Rowboat.png (tools/art/camp_day.py).
    public sealed partial class LakeHud
    {
        private const string RowboatPath = "Art/Rowboat";

        private bool _boatsOpen;
        private float _boatsOpenTime;
        private int _boatFx;            // 0 — нет, 1 — взял в аренду, 2 — купил, 3 — не хватило на аренду, 4 — не хватило на покупку
        private float _boatFxTime = 10f;
        private Texture2D _rowboat;
        private bool _rowboatMissing;

        private void OpenBoats()
        {
            _boatsOpen = true;
            _boatsOpenTime = 0f;
            _boatFx = 0;
            _boatFxTime = 10f;
        }

        private void BoatFx(int kind)
        {
            _boatFx = kind;
            _boatFxTime = 0f;
            if (_feedback == null)
                return;
            if (kind == 1)
                _feedback.PlaySell(1);
            else if (kind == 2)
                _feedback.PlaySell(4);
            else
                _feedback.PlayTick(0.9f);
        }

        private void BoatsPanel(Rect area)
        {
            var profile = _game.Profile;
            var boat = _game.Boat;
            var spots = _game.Spots;
            if (profile == null || boat == null || spots == null)
            {
                _boatsOpen = false;
                return;
            }

            float s = _scale;
            int water = 0;
            foreach (var spot in spots)
            {
                if (spot.needsBoat)
                    water++;
            }

            float w = Mathf.Min(area.width, 380 * s);
            float top = area.y + 48 * s;
            float rowH = 46 * s;
            float h = Mathf.Min(area.yMax - top - 8 * s, 84 * s + 118 * s + 72 * s + 30 * s + rowH * water + 56 * s);
            var card = new Rect(area.center.x - w / 2, top, w, h);
            float x = card.x + 16 * s;
            float cw = w - 32 * s;

            if (CardHeader(card, "Лодки", "прокат и продажа — для мест на воде", true))
            {
                _boatsOpen = false;
                return;
            }

            // Кнопки: аренда и покупка. У своей лодки кнопок нет.
            var stage = new Rect(x, card.y + 84 * s, cw, 108 * s);
            float half = (cw - 10 * s) / 2f;
            var rent = new Rect(x, stage.yMax + 10 * s, half, 52 * s);
            var buy = new Rect(rent.xMax + 10 * s, rent.y, half, 52 * s);
            bool rented = !profile.OwnsBoat && profile.BoatRentSecondsLeft > 0.0;
            if (!profile.OwnsBoat)
            {
                if (Tapped(rent))
                {
                    BoatFx(_game.RentBoat() ? 1 : 3);
                    return;
                }

                if (Tapped(buy))
                {
                    BoatFx(_game.BuyBoat() ? 2 : 4);
                    return;
                }
            }

            if (!_drawing)
                return;

            // Сцена: вода и лодка на ней покачивается; сверху — что с лодкой сейчас.
            float t = _boatsOpenTime;
            Round(stage, new Color(0.18f, 0.50f, 0.82f, 0.12f), 20 * s);
            var sea = new Rect(stage.x, stage.yMax - 34 * s, stage.width, 34 * s);
            GUI.DrawTexture(sea, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.17f, 0.56f, 0.75f, 0.55f),
                Vector4.zero, new Vector4(0f, 0f, 20 * s, 20 * s));
            for (int i = 0; i < 3; i++)
            {
                float wx = stage.x + stage.width * (0.18f + 0.3f * i) + 10 * s * Mathf.Sin(t * 0.7f + i * 2.1f);
                Round(new Rect(wx, sea.y + (8 + 7 * i) * s, (38 - 6 * i) * s, 2.2f * s), new Color(1f, 1f, 1f, 0.45f), 1.1f * s);
            }

            if (_rowboat == null && !_rowboatMissing)
            {
                _rowboat = Resources.Load<Texture2D>(RowboatPath);
                _rowboatMissing = _rowboat == null;
            }

            if (_rowboat != null)
            {
                // Купил — лодка подпрыгивает; иначе просто качается на волне.
                float hop = _boatFx == 2 && _boatFxTime < 0.6f ? Mathf.Sin(Mathf.PI * _boatFxTime / 0.6f) * 12f * s : 0f;
                float bob = Mathf.Sin(t * 1.7f) * 2.5f * s;
                var center = new Vector2(stage.center.x, sea.y + 2 * s + bob - hop);
                float bwidth = 150 * s;
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(2.2f * Mathf.Sin(t * 1.1f), center);
                var tint = profile.HasBoat ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(center.x - bwidth / 2, center.y - bwidth * 0.36f, bwidth, bwidth * 0.5f), _rowboat,
                    ScaleMode.StretchToFill, true, 0f, tint, 0f, 0f);
                GUI.matrix = matrix;
            }

            float chipX = stage.x + 10 * s;
            if (profile.OwnsBoat)
                StaticChip(ref chipX, stage.y + 10 * s, "СВОЯ ЛОДКА", Leaf, Color.white);
            else if (rented)
                StaticChip(ref chipX, stage.y + 10 * s, "В АРЕНДЕ · " + RentText(profile.BoatRentSecondsLeft).ToUpperInvariant(), Azure, Color.white);
            else
                StaticChip(ref chipX, stage.y + 10 * s, "ЛОДКИ НЕТ", Slate, Color.white);
            if (_boatFx == 2 && _boatFxTime < 1.4f)
                Sparkles(new Vector2(stage.center.x, stage.y + 46 * s), _boatFxTime, s);
            if (_boatFx == 1 && _boatFxTime < 1.1f)
            {
                float p = _boatFxTime / 1.1f;
                Label(new Rect(stage.x, stage.y + 34 * s - 26 * s * p, stage.width, 24 * s), "+" + boat.rentMinutes + " мин", _center,
                    WithAlpha(Azure, 1f - p * p));
            }

            if (profile.OwnsBoat)
            {
                var own = new Rect(x, rent.y, cw, 52 * s);
                Round(own, new Color(0.16f, 0.58f, 0.30f, 0.12f), 18 * s);
                Label(new Rect(own.x, own.y + 6 * s, own.width, 22 * s), "Лодка твоя", _center, Ink);
                Label(new Rect(own.x, own.y + 29 * s, own.width, 16 * s), "места на воде открываются по уровню", _tiny, Leaf);
            }
            else
            {
                long money = profile.Money;
                // Пока уровня не хватает ни на одно место на воде, аренду не дают: время просто сгорело бы.
                string rentLocked = _game.BoatRentMakesSense ? null : "с " + _game.BoatUsefulFromLevel + " уровня";
                BoatButton(FxRect(rent, _boatFx == 1 || _boatFx == 3, _boatFx == 3, _boatFxTime),
                    rented ? "+" + boat.rentMinutes + " мин" : "Аренда " + boat.rentMinutes + " мин", boat.rentPrice, money >= boat.rentPrice, false,
                    rentLocked);
                BoatButton(FxRect(buy, _boatFx == 2 || _boatFx == 4, _boatFx == 4, _boatFxTime),
                    "Купить насовсем", boat.price, money >= boat.price, true);
            }

            // Места на воде: что открыто и чего не хватает.
            float y = rent.yMax + 12 * s;
            Label(new Rect(x, y, cw * 0.6f, 20 * s), "Куда можно на лодке", _word, Ink);
            string mine = "твой уровень: " + LevelText(profile.Level);
            float mineWidth = TextWidth(mine, _smallLeft);
            Label(new Rect(x + cw - mineWidth - 2 * s, y, mineWidth + 4 * s, 20 * s), mine, _smallLeft, InkSoft);
            y += 26 * s;

            LakeSpot nearest = null;
            bool onlyBoatMissing = false;
            int line = 0;
            foreach (var spot in spots)
            {
                if (!spot.needsBoat)
                    continue;
                if (nearest == null || spot.minLevel < nearest.minLevel)
                    nearest = spot;
                var access = _game.AccessTo(spot);
                bool levelOk = profile.Level >= spot.minLevel;
                onlyBoatMissing |= access == SpotAccess.NeedsBoat;
                if (y + rowH > card.yMax - 44 * s)
                    break;

                float show = Mathf.Clamp01((_boatsOpenTime - 0.10f - 0.06f * line) / 0.28f);
                show = 1f - (1f - show) * (1f - show) * (1f - show);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, show);
                var row = new Rect(x, y + (1f - show) * 12 * s, cw, rowH - 6 * s);
                Round(row, new Color(0.18f, 0.50f, 0.82f, 0.08f), 14 * s);
                Label(new Rect(row.x + 14 * s, row.center.y - 19 * s, cw * 0.55f, 20 * s), SpotName(spot.id), _word, Ink);
                Label(new Rect(row.x + 14 * s, row.center.y + 1 * s, cw * 0.55f, 16 * s), "с " + spot.minLevel + " уровня", _smallLeft,
                    levelOk ? Leaf : InkSoft);

                string status;
                Color fill;
                if (access == SpotAccess.Open)
                {
                    status = "ОТКРЫТО";
                    fill = Leaf;
                }
                else if (access == SpotAccess.NeedsBoat)
                {
                    status = "НУЖНА ЛОДКА";
                    fill = Azure;
                }
                else if (profile.HasBoat)
                {
                    status = "НУЖЕН УР. " + spot.minLevel;
                    fill = Coral;
                }
                else
                {
                    status = "УР. " + spot.minLevel + " И ЛОДКА";
                    fill = Slate;
                }

                float sw = TextWidth(status, _chip) + 20 * s;
                var chip = new Rect(row.xMax - sw - 10 * s, row.center.y - 12 * s, sw, 24 * s);
                Round(chip, fill, 12 * s);
                Label(chip, status, _chip, Color.white);
                GUI.color = old;
                y += rowH;
                line++;
            }

            // Главное одной строкой: чего не хватает прямо сейчас.
            string hint;
            if (nearest == null)
                hint = "На этом озере мест на воде нет.";
            else if (profile.HasBoat && profile.Level >= nearest.minLevel)
                hint = "Лодка есть — выбирай место на карте: «Причал».";
            else if (profile.HasBoat)
                hint = "Лодка есть, но ближайшее место — «" + SpotName(nearest.id) + "» — с " + nearest.minLevel + " уровня.";
            else if (onlyBoatMissing)
                hint = "Уровня хватает — не хватает только лодки.";
            else
                hint = "Ближайшее место — «" + SpotName(nearest.id) + "», с " + nearest.minLevel + " уровня. До него аренду не дают: плыть некуда.";
            Label(new Rect(x, card.yMax - 46 * s, cw, 36 * s), hint, _coachStyle, InkSoft);
        }

        /// <summary>
        /// Кнопка в момент нажатия: active — нажали именно её; time — сколько секунд назад. Удачное нажатие —
        /// кнопка проседает и выпрыгивает обратно; неудачное (fail) — трясётся из стороны в сторону.
        /// </summary>
        private Rect FxRect(Rect rect, bool active, bool fail, float time)
        {
            if (!active || time > 0.45f)
                return rect;
            if (fail)
            {
                float shake = Mathf.Sin(time * 46f) * 5f * _scale * (1f - time / 0.45f);
                return new Rect(rect.x + shake, rect.y, rect.width, rect.height);
            }

            float k = time < 0.09f ? Mathf.Lerp(1f, 0.90f, time / 0.09f) : 0.90f + 0.10f * EaseOutBack(Mathf.Clamp01((time - 0.09f) / 0.28f));
            float dw = rect.width * (1f - k) / 2f;
            float dh = rect.height * (1f - k) / 2f;
            return new Rect(rect.x + dw, rect.y + dh, rect.width - 2 * dw, rect.height - 2 * dh);
        }
    }
}
