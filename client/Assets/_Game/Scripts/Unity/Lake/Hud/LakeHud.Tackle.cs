using System.Collections.Generic;
using System.Globalization;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Снасти: удилища трёх видов (поплавочное, донное, спиннинг), катушки, лески и приманки.
    // - магазин «Снасти» на турбазе: вкладки над списком (ShopTabs) и строки вещей (ShopTackle): купить, надеть,
    //   «с N уровня»; внутри вкладок — полки (Shelves): удилища по виду, леска по толщине, приманки по семействам,
    //   наживка по роду — как разделы снастей в РР3;
    // - на озере: круглая кнопка «Инвентарь» справа от «Мест» (InventoryButton) открывает экран купленных снастей
    //   (InventoryPanel): что собрано и списки удилищ, катушек и лесок с кнопкой «Надеть»;
    // - подпись на месте столбика спуска, когда в руках не поплавочная удочка (RigNote);
    // - ползунок скорости проводки для спиннинга (RetrieveSlider).
    // Правила — Fishing.Core (Loadout, TackleRules, PlayerProfile), вещи и цены — shared/data/tackle.json.
    public sealed partial class LakeHud
    {
        private const int ShopTabBaits = 0, ShopTabRods = 1, ShopTabReels = 2, ShopTabLines = 3, ShopTabLures = 4;
        private const float ShopTabsHeight = 38f;

        private int _shopTab;

        // ---------- Вкладки магазина ----------

        /// <summary>Полоса вкладок под шапкой магазина. true — вкладку сменили в этом кадре.</summary>
        private bool ShopTabs(Rect card)
        {
            float s = _scale;
            float x = card.x + 16 * s;
            float cw = card.width - 32 * s;
            float gap = 4 * s;
            float w = (cw - 4 * gap) / 5f;
            for (int i = 0; i < 5; i++)
            {
                var tab = new Rect(x + i * (w + gap), card.y + 80 * s, w, 30 * s);
                if (i != _shopTab && Tapped(tab))
                {
                    _shopTab = i;
                    _shopShelf = 0;
                    _shopOpenTime = 0f;
                    _shopFxRow = int.MinValue;
                    _shopNoteLeft = 0f;
                    return true;
                }

                if (!_drawing)
                    continue;
                bool on = i == _shopTab;
                Round(tab, on ? Coral : new Color(0.08f, 0.20f, 0.25f, 0.08f), 15 * s);
                Label(tab, ShopTabName(i), _small, on ? Color.white : Ink);
            }

            return false;
        }

        private static string ShopTabName(int tab)
        {
            switch (tab)
            {
                case ShopTabRods: return "Удилища";
                case ShopTabReels: return "Катушки";
                case ShopTabLines: return "Леска";
                case ShopTabLures: return "Приманки";
                default: return "Наживка";
            }
        }

        private static string ShopTabHint(int tab)
        {
            switch (tab)
            {
                case ShopTabRods: return "у каждого вида — своя ловля";
                case ShopTabReels: return "сильнее катушка — тяжелее рыба";
                case ShopTabLines: return "толще леска — крепче, но рыба её видит";
                case ShopTabLures: return "для спиннинга; не расходуются";
                default: return "упаковками по " + BaitRules.PackSize + " штук; кто на что клюёт — узнаешь на воде";
            }
        }

        // ---------- Строки снастей в магазине ----------

        // ---------- Полки: вторая строка кнопок внутри вкладки ----------

        private const float ShelfHeight = 34f;

        private int _shopShelf;
        private int _boxShelf;

        /// <summary>
        /// Полки вкладки магазина — как разделы снастей в «Русской рыбалке 3»: наживка по роду, удилища по виду
        /// (поплавочные, донные, спиннинговые), леска по толщине, приманки по семействам. У катушек полок нет.
        /// </summary>
        private static int ShelfCount(int tab)
        {
            switch (tab)
            {
                case ShopTabReels: return 0;
                case ShopTabLures: return 4;
                default: return 3;
            }
        }

        private static string ShelfName(int tab, int shelf)
        {
            switch (tab)
            {
                case ShopTabRods: return shelf == 0 ? "Поплавочные" : shelf == 1 ? "Донные" : "Спиннинговые";
                case ShopTabLines: return shelf == 0 ? "Тонкая" : shelf == 1 ? "Толстая" : "Плетёнка";
                case ShopTabLures: return shelf == 0 ? "Вертушки" : shelf == 1 ? "Колебалки" : shelf == 2 ? "Силикон" : "Воблеры";
                default: return shelf == 0 ? "Животная" : shelf == 1 ? "Растительная" : "Для хищника";
            }
        }

        /// <summary>Род наживки или приманки, который лежит на полке (вкладки «Наживка» и «Приманки»).</summary>
        private static string ShelfBaitKind(int tab, int shelf)
        {
            if (tab == ShopTabLures)
                return shelf == 0 ? BaitKinds.Spinner : shelf == 1 ? BaitKinds.Spoon : shelf == 2 ? BaitKinds.Soft : BaitKinds.Wobbler;
            return shelf == 0 ? BaitKinds.Animal : shelf == 1 ? BaitKinds.Plant : BaitKinds.Live;
        }

        private static string ShelfRodKind(int shelf) => shelf == 0 ? RodKinds.Float : shelf == 1 ? RodKinds.Bottom : RodKinds.Spinning;

        /// <summary>Леска на полке: тонкая — до 0,20 мм, толстая — толще, плетёнка — отдельно.</summary>
        private static bool OnLineShelf(LineData line, int shelf)
        {
            if (line.braid)
                return shelf == 2;
            return shelf == (line.diameterMm <= 0.201f ? 0 : 1);
        }

        /// <summary>Строка полок на высоте y. true — полку сменили в этом кадре.</summary>
        private bool Shelves(Rect card, float y, int tab, ref int shelf)
        {
            int count = ShelfCount(tab);
            if (count == 0)
                return false;
            float s = _scale;
            float x = card.x + 16 * s;
            float cw = card.width - 32 * s;
            float gap = 6 * s;
            float w = (cw - (count - 1) * gap) / count;
            shelf = Mathf.Clamp(shelf, 0, count - 1);
            for (int i = 0; i < count; i++)
            {
                var chip = new Rect(x + i * (w + gap), y, w, 26 * s);
                if (i != shelf && Tapped(chip))
                {
                    shelf = i;
                    _shopOpenTime = 0f;
                    _shopFxRow = int.MinValue;
                    return true;
                }

                if (!_drawing)
                    continue;
                bool on = i == shelf;
                Round(chip, on ? new Color(0.18f, 0.50f, 0.82f, 0.20f) : new Color(0.08f, 0.20f, 0.25f, 0.05f), 13 * s);
                if (on)
                    Ring(chip, Azure, 1.4f * s, 13 * s);
                Label(chip, ShelfName(tab, i), _small, on ? InkTitle : InkSoft);
            }

            return false;
        }

        /// <summary>Сколько вещей на выбранной полке вкладки снастей (для высоты строк).</summary>
        private int ShopTackleRows(int tab)
        {
            var data = _game.TackleData;
            if (data == null)
                return 0;
            int count = 0;
            if (tab == ShopTabRods)
            {
                foreach (var rod in data.Rods)
                {
                    if (rod.type == ShelfRodKind(_shopShelf))
                        count++;
                }
            }
            else if (tab == ShopTabReels)
            {
                count = data.Reels.Count;
            }
            else if (tab == ShopTabLines)
            {
                foreach (var line in data.Lines)
                {
                    if (OnLineShelf(line, _shopShelf))
                        count++;
                }
            }
            else
            {
                foreach (var bait in data.Baits)
                {
                    if (!bait.consumable && bait.kind == ShelfBaitKind(tab, _shopShelf))
                        count++;
                }
            }

            return count;
        }

        /// <summary>Список вещей выбранной полки. listTop — верх первой строки.</summary>
        private void ShopTackle(Rect card, float listTop, float rowH)
        {
            var data = _game.TackleData;
            var profile = _game.Profile;
            float s = _scale;
            float x = card.x + 16 * s;
            float cw = card.width - 32 * s;
            float y = listTop;
            int line = 0;

            if (_shopTab == ShopTabRods)
            {
                foreach (var rod in data.Rods)
                {
                    if (rod.type != ShelfRodKind(_shopShelf))
                        continue;
                    string sub = "до " + KgText(rod.maxLoadG) + " · заброс " + (int)TackleRules.MaxCastM(rod) + " м";
                    if (ShopItem(new Rect(x, y, cw, rowH - 6 * s), line, rod.id, Capital(RodName(rod.id)), sub, rod.price, rod.minLevel,
                            profile.Tackle.Owns(rod.id), profile.Tackle.RodId == rod.id))
                        return;
                    y += rowH;
                    line++;
                }

                return;
            }

            if (_shopTab == ShopTabReels)
            {
                foreach (var reel in data.Reels)
                {
                    string sub = "фрикцион до " + KgText(reel.maxDragG) + " · " + reel.spoolCapacityM + " м";
                    if (ShopItem(new Rect(x, y, cw, rowH - 6 * s), line, reel.id, Capital(ReelName(reel.id)), sub, reel.price, reel.minLevel,
                            profile.Tackle.Owns(reel.id), profile.Tackle.ReelId == reel.id))
                        return;
                    y += rowH;
                    line++;
                }

                return;
            }

            if (_shopTab == ShopTabLines)
            {
                foreach (var fishingLine in data.Lines)
                {
                    if (!OnLineShelf(fishingLine, _shopShelf))
                        continue;
                    if (ShopItem(new Rect(x, y, cw, rowH - 6 * s), line, fishingLine.id, Capital(LineName(fishingLine.id)) + " мм",
                            "держит " + KgText(fishingLine.breakingLoadG), fishingLine.price, fishingLine.minLevel,
                            profile.Tackle.Owns(fishingLine.id), profile.Tackle.LineId == fishingLine.id))
                        return;
                    y += rowH;
                    line++;
                }

                return;
            }

            foreach (var bait in data.Baits)
            {
                if (bait.consumable || bait.kind != ShelfBaitKind(_shopTab, _shopShelf))
                    continue;
                bool have = profile.Baits.Count(bait.id) > 0;
                if (ShopItem(new Rect(x, y, cw, rowH - 6 * s), line, bait.id, Capital(BaitName(bait.id)), BaitKind(bait.id), bait.price,
                        bait.minLevel, have, have))
                    return;
                y += rowH;
                line++;
            }
        }

        /// <summary>
        /// Строка вещи в магазине: значок, название, описание и кнопка — цена, «Надеть», «надето» или «с N ур.».
        /// true — кнопку нажали в этом кадре (вызывающий выходит из прохода нажатий).
        /// </summary>
        private bool ShopItem(Rect row, int line, string id, string title, string sub, int price, int minLevel, bool owned, bool worn)
        {
            var profile = _game.Profile;
            float s = _scale;
            float buttonWidth = 92 * s;
            var button = new Rect(row.xMax - buttonWidth - 8 * s, row.center.y - 17 * s, buttonWidth, 34 * s);
            bool lure = _shopTab == ShopTabLures;
            bool locked = !owned && profile.Level < TackleRules.LevelFor(minLevel);
            bool enough = profile.Money >= price;
            if (!worn && Tapped(button))
            {
                if (owned)
                {
                    bool ok = _game.EquipTackle(_shopTab - 1, id);
                    ShopFx(line, ok, null);
                    if (ok)
                    {
                        ShopSay(title + " — надето");
                        if (_feedback != null)
                            _feedback.PlayTick(0.4f);
                    }
                }
                else
                {
                    bool ok = !locked && (lure ? _game.BuyLure(id) : _game.BuyTackle(_shopTab - 1, id));
                    ShopFx(line, ok, "куплено");
                    if (ok)
                    {
                        ShopSay(lure ? title + " — в коробке с наживкой" : title + " — куплено. Нажми «Надеть»");
                        if (_feedback != null)
                            _feedback.PlaySell(2);
                    }
                    else
                    {
                        ShopSay(locked ? "Продаётся с " + TackleRules.LevelFor(minLevel) + " уровня" : "Не хватает монет — сдай улов на рынке");
                        if (_feedback != null)
                            _feedback.PlayTick(0.9f);
                    }
                }

                _shopFxAtY = row.center.y;
                return true;
            }

            if (!_drawing)
                return false;

            bool fresh = _shopFxRow == line && _shopFxOk && _shopFxTime < 0.9f;
            float show = Mathf.Clamp01((_shopOpenTime - 0.04f * line) / 0.26f);
            show = 1f - (1f - show) * (1f - show) * (1f - show);
            GUI.color = new Color(1f, 1f, 1f, show);
            var at = new Rect(row.x, row.y + (1f - show) * 14 * s, row.width, row.height);
            Round(at, worn && !lure ? new Color(0.16f, 0.58f, 0.30f, 0.12f) : new Color(0.18f, 0.50f, 0.82f, 0.08f), 14 * s);
            if (fresh && _shopFxTime < 0.6f)
                Round(at, new Color(0.16f, 0.58f, 0.30f, 0.22f * (1f - _shopFxTime / 0.6f)), 14 * s);
            float bounce = fresh && _shopFxTime < 0.5f ? 1f + 0.30f * Mathf.Sin(Mathf.PI * _shopFxTime / 0.5f) * (1f - _shopFxTime / 0.5f) : 1f;
            float radius = Mathf.Min(19 * s, at.height / 2 - 2 * s) * bounce;
            var iconAt = new Vector2(at.x + 26 * s, at.center.y);
            if (lure)
                BaitDot(iconAt, radius, id, true);
            else
                TackleIcon(iconAt, radius, _shopTab - 1, locked);
            Label(new Rect(at.x + 54 * s, at.center.y - 19 * s, row.width - 162 * s, 20 * s), title, _word, locked ? InkSoft : Ink);
            Label(new Rect(at.x + 54 * s, at.center.y + 1 * s, row.width - 162 * s, 16 * s), sub, SmallLine(), InkSoft);

            var pressed = FxRect(button, _shopFxRow == line, !_shopFxOk, _shopFxTime);
            if (worn)
            {
                Round(pressed, new Color(0.16f, 0.58f, 0.30f, 0.16f), pressed.height / 2);
                Label(pressed, lure ? "есть" : "надето", _word2Center(), Leaf);
            }
            else if (owned)
            {
                Round(pressed, Azure, pressed.height / 2);
                Label(pressed, "Надеть", _word2Center(), Color.white);
            }
            else if (locked)
            {
                Round(pressed, new Color(0.08f, 0.20f, 0.25f, 0.12f), pressed.height / 2);
                Label(pressed, "с " + TackleRules.LevelFor(minLevel) + " ур.", _word2Center(), InkSoft);
            }
            else
            {
                if (enough)
                {
                    Round(new Rect(pressed.x, pressed.y + 2 * s, pressed.width, pressed.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), pressed.height / 2);
                    Round(pressed, Coral, pressed.height / 2);
                }
                else
                {
                    Round(pressed, new Color(0.08f, 0.20f, 0.25f, 0.14f), pressed.height / 2);
                }

                string cost = MoneyNumber(price);
                float pw = TextWidth(cost, _word);
                float px = pressed.center.x - (pw + 18 * s) / 2;
                Coin(new Vector2(px + 6 * s, pressed.center.y), 6 * s);
                Label(new Rect(px + 18 * s, pressed.y, pw + 6 * s, pressed.height), cost, _word, enough ? Color.white : InkSoft);
            }

            GUI.color = Color.white;
            return false;
        }

        private GUIStyle _wordCenter;
        private GUIStyle _smallLine;

        /// <summary>Мелкая подпись в одну строку: как _smallLeft, но без переноса (перенос ломал строки магазина).</summary>
        private GUIStyle SmallLine()
        {
            if (_smallLine == null || _smallLine.fontSize != _smallLeft.fontSize)
                _smallLine = new GUIStyle(_smallLeft) { wordWrap = false, clipping = TextClipping.Clip };
            return _smallLine;
        }

        /// <summary>Стиль подписи на кнопке: как _word, но по центру.</summary>
        private GUIStyle _word2Center()
        {
            if (_wordCenter == null || _wordCenter.fontSize != _word.fontSize)
                _wordCenter = new GUIStyle(_word) { alignment = TextAnchor.MiddleCenter };
            return _wordCenter;
        }

        /// <summary>Значок вещи в кружке: 0 — удилище с катушкой, 1 — катушка с ручкой, 2 — шпуля лески.</summary>
        private void TackleIcon(Vector2 c, float r, int kind, bool dim)
        {
            if (!_drawing)
                return;
            Disc(c, r, new Color(0.18f, 0.50f, 0.82f, dim ? 0.08f : 0.14f));
            var ink = dim ? InkSoft : InkTitle;
            float w = Mathf.Max(1.5f, r * 0.13f);
            switch (kind)
            {
                case 0:
                    Bar(c + new Vector2(-r * 0.55f, r * 0.50f), c + new Vector2(r * 0.55f, -r * 0.55f), w, ink);
                    Circle(c + new Vector2(-r * 0.24f, r * 0.34f), r * 0.20f, w, ink);
                    Bar(c + new Vector2(r * 0.55f, -r * 0.55f), c + new Vector2(r * 0.55f, r * 0.10f), w * 0.6f, ink);
                    break;
                case 1:
                    Circle(c, r * 0.42f, w, ink);
                    Disc(c, r * 0.12f, ink);
                    Bar(c + new Vector2(r * 0.30f, -r * 0.30f), c + new Vector2(r * 0.62f, -r * 0.58f), w, ink);
                    Disc(c + new Vector2(r * 0.64f, -r * 0.60f), r * 0.12f, ink);
                    break;
                default:
                    Circle(c, r * 0.50f, w, ink);
                    Circle(c, r * 0.28f, w, ink);
                    Bar(c + new Vector2(r * 0.50f, 0f), c + new Vector2(r * 0.72f, r * 0.40f), w * 0.7f, ink);
                    break;
            }
        }

        // ---------- Инвентарь: кнопка в нижнем ряду и экран купленных снастей ----------

        private bool _gearOpen;
        private int _gearKind;          // 0 — удилища, 1 — катушки, 2 — леска
        private float _gearOpenTime;
        private Rect _gearButtonRect;

        /// <summary>
        /// Размер боковых кнопок нижнего ряда («Турбаза» слева от садка, «Места» и «Инвентарь» справа): справа их
        /// две, поэтому размер считается так, чтобы обе поместились до края экрана.
        /// </summary>
        private float SideButtonSize(Rect area)
        {
            float s = _scale;
            float room = area.xMax - _keepnetButtonRect.xMax;
            return Mathf.Clamp((room - 2 * SideButtonGap * s - 2 * s) / 2f, 44 * s, 72 * s);
        }

        private const float SideButtonGap = 10f;

        /// <summary>Круглая кнопка «Инвентарь» справа от «Мест»: только пока удочка не заброшена.</summary>
        private void InventoryButton(Rect area)
        {
            float s = _scale;
            if (_keepnetButtonRect.width <= 0f || _game.Profile == null || _game.TackleData == null)
            {
                _gearButtonRect = default;
                return;
            }

            float d = SideButtonSize(area);
            // Правее «Мест»; если мест на озере нет и той кнопки нет — на её месте.
            float left = _mapButtonRect.width > 0f ? _mapButtonRect.xMax : _keepnetButtonRect.xMax;
            var rect = new Rect(left + SideButtonGap * s, _keepnetButtonRect.yMax - d, d, d);
            _gearButtonRect = rect;
            if (Tapped(rect))
            {
                _gearOpen = true;
                _gearOpenTime = 0f;
                _swiping = false;
            }

            if (!_drawing)
                return;
            RoundButton(rect, Glass, GlassBorder, 1.5f * s, new Color(0f, 0f, 0f, 0f));
            // Ящик со снастями: корпус, крышка и ручка.
            var c = new Vector2(rect.center.x, rect.y + d * 0.36f);
            float w = d * 0.36f, h = d * 0.20f;
            Ring(new Rect(c.x - w / 2, c.y - h * 0.35f, w, h), Color.white, 2f * s, 3f * s);
            Bar(c + new Vector2(-w / 2, -h * 0.02f), c + new Vector2(w / 2, -h * 0.02f), 1.8f * s, Color.white);
            Bar(c + new Vector2(-w * 0.18f, -h * 0.35f), c + new Vector2(-w * 0.12f, -h * 0.72f), 1.8f * s, Color.white);
            Bar(c + new Vector2(w * 0.18f, -h * 0.35f), c + new Vector2(w * 0.12f, -h * 0.72f), 1.8f * s, Color.white);
            Bar(c + new Vector2(-w * 0.12f, -h * 0.72f), c + new Vector2(w * 0.12f, -h * 0.72f), 1.8f * s, Color.white);
            Label(new Rect(rect.x - 4 * s, rect.y + d * 0.58f, d + 8 * s, 16 * s), "Инвентарь", _tiny, Color.white);
        }

        /// <summary>
        /// Экран «Инвентарь»: что сейчас собрано и список купленных удилищ, катушек или лесок (кнопки сверху).
        /// Нажатие «Надеть» ставит вещь на удочку. Покупают снасти на турбазе.
        /// </summary>
        private void InventoryPanel(Rect area)
        {
            var profile = _game.Profile;
            var data = _game.TackleData;
            if (profile == null || data == null)
            {
                _gearOpen = false;
                return;
            }

            float s = _scale;
            int rows = 0;
            int total = _gearKind == 0 ? data.Rods.Count : _gearKind == 1 ? data.Reels.Count : data.Lines.Count;
            for (int i = 0; i < total; i++)
            {
                if (profile.Tackle.Owns(GearId(data, _gearKind, i)))
                    rows++;
            }

            float w = Mathf.Min(area.width, 380 * s);
            float top = area.y + 48 * s;
            float maxHeight = Mathf.Min(area.yMax - top - 8 * s, 700 * s);
            float fixedH = 262 * s;
            float rowH = Mathf.Min(BaitRowHeight * s, (maxHeight - fixedH) / Mathf.Max(1, rows));
            float h = Mathf.Min(maxHeight, fixedH + rowH * Mathf.Max(3, rows));
            var card = new Rect(area.center.x - w / 2, top, w, h);
            float x = card.x + 16 * s;
            float cw = w - 32 * s;

            if (CardHeader(card, "Инвентарь", "снасти, которые у тебя есть", false))
            {
                _gearOpen = false;
                return;
            }

            // Сейчас собрано: три плитки — удилище, катушка, леска; нажатие открывает их список.
            float tileGap = 8 * s;
            float tileW = (cw - 2 * tileGap) / 3f;
            float tileY = card.y + 80 * s;
            for (int kind = 0; kind < 3; kind++)
            {
                var tile = new Rect(x + kind * (tileW + tileGap), tileY, tileW, 86 * s);
                if (kind != _gearKind && Tapped(tile))
                {
                    _gearKind = kind;
                    _gearOpenTime = 0f;
                    return;
                }

                if (!_drawing)
                    continue;
                bool on = kind == _gearKind;
                Round(tile, on ? new Color(0.18f, 0.50f, 0.82f, 0.16f) : new Color(0.08f, 0.20f, 0.25f, 0.05f), 14 * s);
                if (on)
                    Ring(tile, Azure, 1.5f * s, 14 * s);
                TackleIcon(new Vector2(tile.center.x, tile.y + 24 * s), 15 * s, kind, false);
                string current = kind == 0 ? profile.Tackle.RodId : kind == 1 ? profile.Tackle.ReelId : profile.Tackle.LineId;
                Label(new Rect(tile.x, tile.y + 42 * s, tile.width, 16 * s), kind == 0 ? "Удилище" : kind == 1 ? "Катушка" : "Леска", _tiny, InkSoft);
                Label(new Rect(tile.x + 4 * s, tile.y + 58 * s, tile.width - 8 * s, 22 * s), GearName(kind, current), _small, Ink);
            }

            float y = tileY + 96 * s;
            if (_drawing)
            {
                string caption = _gearKind == 0 ? "Твои удилища" : _gearKind == 1 ? "Твои катушки" : "Твоя леска";
                Label(new Rect(x, y, cw, 20 * s), caption, _word, Ink);
            }

            y += 28 * s;
            int line = 0;
            for (int i = 0; i < total; i++)
            {
                string id = GearId(data, _gearKind, i);
                if (!profile.Tackle.Owns(id))
                    continue;
                bool worn = (_gearKind == 0 ? profile.Tackle.RodId : _gearKind == 1 ? profile.Tackle.ReelId : profile.Tackle.LineId) == id;
                var row = new Rect(x, y, cw, rowH - 6 * s);
                var button = new Rect(row.xMax - 100 * s, row.center.y - 17 * s, 92 * s, 34 * s);
                y += rowH;
                if (!worn && Tapped(button))
                {
                    if (_game.EquipTackle(_gearKind, id) && _feedback != null)
                        _feedback.PlayTick(0.4f);
                    return;
                }

                if (_drawing)
                {
                    float show = Mathf.Clamp01((_gearOpenTime - 0.04f * line) / 0.26f);
                    show = 1f - (1f - show) * (1f - show) * (1f - show);
                    GUI.color = new Color(1f, 1f, 1f, show);
                    var at = new Rect(row.x, row.y + (1f - show) * 14 * s, row.width, row.height);
                    Round(at, worn ? new Color(0.16f, 0.58f, 0.30f, 0.12f) : new Color(0.18f, 0.50f, 0.82f, 0.08f), 14 * s);
                    TackleIcon(new Vector2(at.x + 26 * s, at.center.y), Mathf.Min(19 * s, at.height / 2 - 2 * s), _gearKind, false);
                    Label(new Rect(at.x + 54 * s, at.center.y - 19 * s, row.width - 162 * s, 20 * s), GearName(_gearKind, id), _word, Ink);
                    Label(new Rect(at.x + 54 * s, at.center.y + 1 * s, row.width - 162 * s, 16 * s), GearSpec(data, _gearKind, i), SmallLine(), InkSoft);
                    if (worn)
                    {
                        Round(button, new Color(0.16f, 0.58f, 0.30f, 0.16f), button.height / 2);
                        Label(button, "надето", _word2Center(), Leaf);
                    }
                    else
                    {
                        Round(button, Azure, button.height / 2);
                        Label(button, "Надеть", _word2Center(), Color.white);
                    }

                    GUI.color = Color.white;
                }

                line++;
            }

            if (_drawing)
            {
                string hint = rows <= 1
                    ? "Другие снасти продают на турбазе, в доме «Снасти»."
                    : "Наживку меняют кнопкой над спуском поплавка.";
                Label(new Rect(x, card.yMax - 44 * s, cw, 34 * s), hint, _coachStyle, InkSoft);
            }
        }

        private static string GearId(TackleDatabase data, int kind, int index)
        {
            return kind == 0 ? data.Rods[index].id : kind == 1 ? data.Reels[index].id : data.Lines[index].id;
        }

        private static string GearName(int kind, string id)
        {
            return kind == 0 ? Capital(RodName(id)) : kind == 1 ? Capital(ReelName(id)) : Capital(LineName(id)) + " мм";
        }

        private static string GearSpec(TackleDatabase data, int kind, int index)
        {
            if (kind == 0)
            {
                var rod = data.Rods[index];
                return "до " + KgText(rod.maxLoadG) + " · заброс " + (int)TackleRules.MaxCastM(rod) + " м";
            }

            if (kind == 1)
            {
                var reel = data.Reels[index];
                return "фрикцион до " + KgText(reel.maxDragG) + " · " + reel.spoolCapacityM + " м";
            }

            return "держит " + KgText(data.Lines[index].breakingLoadG);
        }

        // ---------- Ползунок скорости проводки (спиннинг) ----------

        private Rect _speedRect;
        private bool _speedDrag;
        private float _speedDragY;
        private int _speedTextKey = -1;
        private string _speedName;

        /// <summary>
        /// Скорость проводки приманки: столбик слева над «Вытащить» — на берегу и пока приманка в воде. Тянешь бегунок
        /// вверх — быстрее (приманка идёт мельче), вниз — медленнее (глубже). Рядом — название скорости и глубина хода.
        /// Правила — LureRetrieve и TackleRules.BaitDepthCm.
        /// </summary>
        private void RetrieveSlider(Rect area)
        {
            float s = _scale;
            float width = 20 * s;
            float height = 132 * s;
            var column = new Rect(area.x + 14 * s, area.yMax - 246 * s - height, width, height);
            float top = column.y + 12 * s;
            float bottom = column.yMax - 12 * s;
            _speedRect = new Rect(column.x - 12 * s, column.y - 12 * s, width + 40 * s, height + 24 * s);

            if (!_drawing)
            {
                if (_speedDrag)
                {
                    float share = Mathf.InverseLerp(bottom, top, _speedDragY);
                    int before = _game.RetrievePercent;
                    _game.SetRetrievePercent(Mathf.RoundToInt(Mathf.Lerp(LureRetrieve.MinPercent, LureRetrieve.MaxPercent, share)));
                    if (_game.RetrievePercent != before && _feedback != null)
                        _feedback.PlayTick(1f - share);
                }

                return;
            }

            int percent = _game.RetrievePercent;
            float at = Mathf.InverseLerp(LureRetrieve.MinPercent, LureRetrieve.MaxPercent, percent);
            float knobY = Mathf.Lerp(bottom, top, at);
            float radius = width / 2;

            // Столбик: тёмное стекло, заполнение от низа до бегунка.
            Round(new Rect(column.x - 2 * s, column.y, column.width + 4 * s, column.height + 4 * s), new Color(0f, 0f, 0f, 0.28f), radius + 2 * s);
            Round(column, new Color(0.12f, 0.10f, 0.09f, 0.85f), radius);
            Round(new Rect(column.x + 5 * s, knobY, column.width - 10 * s, column.yMax - 6 * s - knobY), WithAlpha(Amber, 0.85f), 5 * s);
            Ring(column, new Color(1f, 1f, 1f, 0.55f), 1.3f * s, radius);
            // Деления: каждые 10%, обычная скорость — длиннее.
            for (int p = LureRetrieve.MinPercent; p <= LureRetrieve.MaxPercent; p += LureRetrieve.StepPercent)
            {
                float y = Mathf.Lerp(bottom, top, Mathf.InverseLerp(LureRetrieve.MinPercent, LureRetrieve.MaxPercent, p));
                float len = p == LureRetrieve.DefaultPercent ? 9 * s : 4 * s;
                Round(new Rect(column.xMax + 3 * s, y - 0.8f * s, len, 1.6f * s), new Color(1f, 1f, 1f, 0.55f), 0.8f * s);
            }

            var knob = new Vector2(column.center.x, knobY);
            Disc(knob, 11 * s, new Color(0f, 0f, 0f, 0.30f));
            Disc(knob, 9.5f * s, Color.white);
            Disc(knob, 5.5f * s, Orange);

            if (percent != _speedTextKey || _speedName == null)
            {
                _speedTextKey = percent;
                _speedName = percent <= 30 ? "медленная" : percent >= 80 ? "быстрая" : percent == LureRetrieve.DefaultPercent ? "обычная" : percent < 50 ? "неспешная" : "бодрая";
            }

            float tx = column.xMax + 18 * s;
            float tw = 190 * s;
            // Подписи — строго в одну строку каждая: длинная фраза с переносом налезала сама на себя.
            ShadowLabel(new Rect(tx, column.y + 6 * s, tw, 16 * s), "проводка", SmallLine(), Soft);
            ShadowLabel(new Rect(tx, column.y + 22 * s, tw, 26 * s), _speedName, _center2Left(), Color.white);
            if (_game.State == LakeState.Waiting)
            {
                ShadowLabel(new Rect(tx, column.y + 50 * s, tw, 16 * s),
                    "приманка идёт на " + HudText.Fixed(HudText.Scaled(_game.BaitDepthM, 1), 1) + " м", SmallLine(), Amber);
            }
            else
            {
                ShadowLabel(new Rect(tx, column.y + 50 * s, tw, 16 * s), "вверх — быстрее и мельче", SmallLine(), Amber);
                ShadowLabel(new Rect(tx, column.y + 66 * s, tw, 16 * s), "вниз — медленнее и глубже", SmallLine(), Amber);
            }
        }

        private GUIStyle _centerLeft;

        /// <summary>Крупная подпись слева: как _center, но прижата к левому краю.</summary>
        private GUIStyle _center2Left()
        {
            if (_centerLeft == null || _centerLeft.fontSize != _center.fontSize)
                _centerLeft = new GUIStyle(_center) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            return _centerLeft;
        }

        // ---------- Подпись вместо столбика спуска ----------

        /// <summary>Донка и спиннинг спуск не выставляют: на месте столбика — что за снасть и как ею ловить.</summary>
        private void RigNote(Rect area)
        {
            if (!_drawing)
                return;
            float s = _scale;
            bool spinning = _game.RodType == RodKinds.Spinning;
            float w = 230 * s;
            var at = new Rect(area.xMax - w - 14 * s, area.yMax - 290 * s, w, 16 * s);
            ShadowLabel(at, spinning ? "спиннинг" : "донная удочка", _depthCaption, Soft);
            ShadowLabel(new Rect(at.x, at.y + 16 * s, w, 30 * s), "заброс до " + (int)_game.MaxCastM + " м", _depthStyle, Color.white);
            ShadowLabel(new Rect(at.x, at.y + 48 * s, w, 16 * s), spinning ? "клюёт, пока ведёшь приманку" : "наживка лежит на дне", _depthCaption, Amber);
            ShadowLabel(new Rect(at.x, at.y + 65 * s, w, 16 * s), spinning ? "после заброса держи «Подмотку»" : "поплавка нет — следи за подсказкой", _depthCaption, Soft);
        }

        // ---------- Названия ----------

        /// <summary>«катушка 2500» из id reel_2500.</summary>
        private static string ReelName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            return id.StartsWith("reel_") ? "катушка " + id.Substring(5) : id;
        }

        /// <summary>Граммы как килограммы: «2,5 кг», «12 кг».</summary>
        private static string KgText(int grams)
        {
            return (grams / 1000f).ToString(grams % 1000 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " кг";
        }
    }
}
