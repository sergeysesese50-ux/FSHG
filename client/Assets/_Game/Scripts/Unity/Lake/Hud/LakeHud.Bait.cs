using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Наживка:
    // - на озере, пока удочка на берегу, справа над столбиком спуска — круглая кнопка: картинка наживки на крючке
    //   и число штук в углу; нажатие открывает коробку с наживкой (светлая карточка), там выбирают другую.
    //   В коробке только наживка и приманки; удилище, катушка и леска — в «Инвентаре» (LakeHud.Tackle);
    // - на турбазе дом «Снасти» — магазин: вкладка наживки здесь, вкладки снастей — LakeHud.Tackle. Наживка: упаковки по BaitRules.PackSize штук, «Накопать червей» бесплатно,
    //   когда червей почти нет. Покупает только кнопка с ценой справа (не вся строка); кнопка проседает и
    //   выпрыгивает, значок подпрыгивает, всплывает «+10», при нехватке монет кнопка трясётся;
    // - на странице мелкой рыбы в садке — «На живца».
    // Правила (запас, цена, кто на что клюёт) — Fishing.Core: BaitBox, BaitRules, PlayerProfile, BiteModel.
    // Картинки наживок — Resources/Art/Bait/<id>.png, их рисует tools/art/bait_icons.py.
    // На что берёт какая рыба, игра не подсказывает: это узнают на воде (на странице рыбы в садке видно, на что она поймана).
    public sealed partial class LakeHud
    {
        private const float BaitRowHeight = 52f;

        private bool _baitOpen;
        private bool _shopOpen;
        private Rect _baitPillRect;
        private string _shopNote;
        private float _shopNoteLeft;

        // ---------- Плашка на озере ----------

        /// <summary>
        /// Круглая кнопка наживки справа над столбиком спуска, пока удочка на берегу: только картинка того, что на
        /// крючке, и число штук в правом нижнем углу. Нажатие открывает коробку с наживкой.
        /// </summary>
        private void BaitPill(Rect area)
        {
            var profile = _game.Profile;
            if (profile == null)
            {
                _baitPillRect = default;
                return;
            }

            float s = _scale;
            float d = 62 * s;
            // Столбик спуска начинается на 308 от низа (RigWidget): кнопка — над ним, по тому же правому краю.
            var rect = new Rect(area.xMax - d - 12 * s, area.yMax - 308 * s - 18 * s - d, d, d);
            _baitPillRect = rect;
            if (Tapped(rect))
            {
                _baitOpen = true;
                _swiping = false;
                _boxShelf = BoxShelfOf(profile.Baits.SelectedId);
            }

            if (!_drawing)
                return;

            string id = profile.Baits.SelectedId;
            // Наживка, которая не подходит удилищу в руках, считается как «нет».
            int count = _game.BaitCount;
            RoundButton(rect, Glass, count > 0 ? GlassBorder : Red, 1.5f * s, new Color(0f, 0f, 0f, 0f));
            var picture = BaitPicture(id);
            if (picture != null)
            {
                float half = d * 0.36f;
                var tint = count > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                GUI.DrawTexture(new Rect(rect.center.x - half, rect.center.y - half, 2 * half, 2 * half), picture,
                    ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
            }
            else if (id != null)
            {
                Disc(rect.center, d * 0.24f, BaitColor(id));
            }

            // Число штук — плашкой в правом нижнем углу кнопки.
            string amount = count.ToString();
            float bh = 20 * s;
            float bw = Mathf.Max(bh, TextWidth(amount, _chip) + 12 * s);
            var badge = new Rect(rect.xMax - bw + 3 * s, rect.yMax - bh + 2 * s, bw, bh);
            Round(new Rect(badge.x - 1.5f * s, badge.y - 1.5f * s, bw + 3 * s, bh + 3 * s), new Color(1f, 1f, 1f, 0.55f), bh / 2 + 1.5f * s);
            Round(badge, count > 0 ? new Color(0.13f, 0.09f, 0.07f, 1f) : Red, bh / 2);
            Label(badge, amount, _chip, Color.white);
        }

        // ---------- Коробка со снастями и наживкой (озеро) ----------

        private void BaitPanel(Rect area)
        {
            var profile = _game.Profile;
            if (profile == null)
            {
                _baitOpen = false;
                return;
            }

            float s = _scale;
            var items = profile.Baits.Items;
            string selected = profile.Baits.SelectedId;
            // Показывается только то, что подходит удилищу в руках (спиннингу — приманки, остальным — наживка),
            // и только с выбранной полки: животная, растительная, для хищника — или семейства приманок.
            bool spinning = _game.RodType == RodKinds.Spinning;
            int boxTab = spinning ? ShopTabLures : ShopTabBaits;
            _boxShelf = Mathf.Clamp(_boxShelf, 0, ShelfCount(boxTab) - 1);
            string shelfKind = ShelfBaitKind(boxTab, _boxShelf);
            int rows = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (BoxShows(items[i], selected, shelfKind))
                    rows++;
            }

            float w = Mathf.Min(area.width, 380 * s);
            float top = area.y + 48 * s;
            float tackleH = ShelfHeight * s + 8 * s;
            float maxHeight = Mathf.Min(area.yMax - top - 8 * s, 700 * s);
            float fixedH = 160 * s + tackleH;
            float rowH = Mathf.Min(BaitRowHeight * s, (maxHeight - fixedH) / Mathf.Max(1, rows));
            float h = Mathf.Min(maxHeight, fixedH + rowH * Mathf.Max(1, rows));
            var card = new Rect(area.center.x - w / 2, top, w, h);
            float x = card.x + 16 * s;
            float cw = w - 32 * s;

            if (CardHeader(card, spinning ? "Приманка" : "Наживка", "что на крючке · " + RodName(_game.Rod != null ? _game.Rod.id : null), false))
            {
                _baitOpen = false;
                return;
            }

            float y = card.y + 84 * s;
            if (Shelves(card, y, boxTab, ref _boxShelf))
                return;
            y += ShelfHeight * s + 4 * s;

            if (rows == 0 && _drawing)
            {
                Label(new Rect(x, y, cw, 60 * s), spinning
                    ? "На этой полке пусто.\nПриманки продают в «Снастях» на турбазе."
                    : "На этой полке пусто.\nНаживку продают на турбазе, в доме «Снасти».", _coachStyle, InkSoft);
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                bool current = item.id == selected;
                if (!BoxShows(item, selected, shelfKind))
                    continue;
                var row = new Rect(x, y, cw, rowH - 6 * s);
                y += rowH;
                if (item.count > 0 && Tapped(row))
                {
                    _game.SelectBait(item.id);
                    _baitOpen = false;
                    if (_feedback != null)
                        _feedback.PlayTick(0.3f);
                    return;
                }

                if (!_drawing)
                    continue;
                Round(row, current ? new Color(0.90f, 0.33f, 0.18f, 0.10f) : new Color(0.18f, 0.50f, 0.82f, 0.08f), 14 * s);
                if (current)
                    Ring(row, Coral, 1.6f * s, 14 * s);
                BaitDot(new Vector2(row.x + 26 * s, row.center.y), Mathf.Min(19 * s, row.height / 2 - 2 * s), item.id, true);
                Label(new Rect(row.x + 54 * s, row.y + row.height * 0.5f - 19 * s, cw - 150 * s, 20 * s), Capital(BaitName(item.id)), _word, Ink);
                Label(new Rect(row.x + 54 * s, row.y + row.height * 0.5f + 1 * s, cw - 150 * s, 16 * s),
                    current ? "на крючке" : BaitKind(item.id), _smallLeft, current ? Coral : InkSoft);
                string amount = spinning ? "" : item.count > 0 ? item.count + " шт" : "кончилась";
                float aw = TextWidth(amount, _word);
                Label(new Rect(row.xMax - aw - 16 * s, row.y, aw + 8 * s, row.height), amount, _word, item.count > 0 ? Ink : Coral);
            }

            if (_drawing)
            {
                Label(new Rect(x, card.yMax - 58 * s, cw, 46 * s), spinning
                    ? "Приманка не расходуется.\nУдилище, катушка и леска — в «Инвентаре»."
                    : "Одна поклёвка — одна наживка.\nУдилище, катушка и леска — в «Инвентаре».", _coachStyle, InkSoft);
            }
        }

        /// <summary>Запись коробки видна: подходит удилищу, лежит на выбранной полке и не пуста (или сейчас на крючке).</summary>
        private bool BoxShows(BaitStock item, string selected, string shelfKind)
        {
            if (!_game.BaitFitsRod(item.id) || (item.count <= 0 && item.id != selected))
                return false;
            var data = _game.TackleData;
            return data != null && data.TryGetBait(item.id, out var bait) && bait.kind == shelfKind;
        }

        /// <summary>Полка, на которой лежит наживка (чтобы коробка открывалась на той, что на крючке).</summary>
        private int BoxShelfOf(string baitId)
        {
            var data = _game.TackleData;
            if (data == null || !data.TryGetBait(baitId, out var bait))
                return 0;
            int tab = bait.rodType == RodKinds.Spinning ? ShopTabLures : ShopTabBaits;
            for (int i = 0; i < ShelfCount(tab); i++)
            {
                if (ShelfBaitKind(tab, i) == bait.kind)
                    return i;
            }

            return 0;
        }

        // ---------- Магазин наживки (турбаза) ----------

        private const int ShopDigRow = -1;

        private float _shopOpenTime;            // секунд с открытия: строки выезжают по очереди
        private int _shopFxRow = int.MinValue;  // строка последнего нажатия: номер наживки или ShopDigRow
        private float _shopFxTime = 10f;
        private bool _shopFxOk;
        private string _shopFxText;
        private float _shopFxAtY = float.NaN;   // где всплывает подпись покупки; NaN — считать по номеру строки

        private void OpenShop()
        {
            _shopOpen = true;
            _shopOpenTime = 0f;
            _shopNoteLeft = 0f;
            _shopFxRow = int.MinValue;
            _shopFxTime = 10f;
        }

        private void ShopFx(int row, bool ok, string text)
        {
            _shopFxRow = row;
            _shopFxOk = ok;
            _shopFxText = ok ? text : null;
            _shopFxTime = 0f;
            _shopFxAtY = float.NaN;
        }

        private void ShopPanel(Rect area)
        {
            var profile = _game.Profile;
            var baits = _game.ShopBaits;
            if (profile == null || baits == null)
            {
                _shopOpen = false;
                return;
            }

            float s = _scale;
            bool baitTab = _shopTab == ShopTabBaits;
            // Червей копают на полке животной наживки, когда они почти кончились.
            bool dig = baitTab && _shopShelf == 0 && profile.CanDigWorms;
            string shelfKind = ShelfBaitKind(ShopTabBaits, _shopShelf);
            int rows = dig ? 1 : 0;
            if (baitTab)
            {
                foreach (var b in baits)
                {
                    if (b.kind == shelfKind)
                        rows++;
                }
            }
            else
            {
                rows = ShopTackleRows(_shopTab);
            }

            float w = Mathf.Min(area.width, 380 * s);
            float top = area.y + 48 * s;
            float fixedH = (150 + ShopTabsHeight + ShelfHeight) * s;
            float maxHeight = Mathf.Min(area.yMax - top - 8 * s, 720 * s);
            // Карточка одной высоты на всех вкладках (под восемь строк): окно не прыгает при переключении.
            float h = Mathf.Min(maxHeight, fixedH + 8 * BaitRowHeight * s);
            float shelfH = ShelfCount(_shopTab) > 0 ? ShelfHeight * s : 0f;
            float rowH = Mathf.Min(BaitRowHeight * s, (h - fixedH + ShelfHeight * s - shelfH) / Mathf.Max(1, rows));
            var card = new Rect(area.center.x - w / 2, top, w, h);
            float x = card.x + 16 * s;
            float cw = w - 32 * s;

            if (CardHeader(card, "Снасти", ShopTabHint(_shopTab), true))
            {
                _shopOpen = false;
                return;
            }

            if (ShopTabs(card))
                return;
            if (Shelves(card, card.y + (86 + ShopTabsHeight) * s, _shopTab, ref _shopShelf))
                return;
            float listTop = card.y + (88 + ShopTabsHeight) * s + shelfH;
            if (!baitTab)
            {
                ShopTackle(card, listTop, rowH);
                if (_drawing)
                    ShopFooter(card, x, cw, listTop, rowH, "Купленное надевается здесь же или в коробке на озере.");
                return;
            }

            float y = listTop;
            float buttonWidth = 86 * s;
            int line = 0;

            // Червей можно накопать даром, когда их почти не осталось: без наживки игрок не останется.
            if (dig)
            {
                var row = new Rect(x, y, cw, rowH - 6 * s);
                var button = new Rect(row.xMax - buttonWidth - 8 * s, row.center.y - 17 * s, buttonWidth, 34 * s);
                y += rowH;
                // Покупка — только по кнопке справа: случайное касание строки ничего не делает.
                if (Tapped(button))
                {
                    bool ok = _game.DigWorms();
                    ShopFx(ShopDigRow, ok, "+" + BaitRules.DigWormsCount);
                    if (ok)
                    {
                        ShopSay("Накопал червей: +" + BaitRules.DigWormsCount);
                        if (_feedback != null)
                            _feedback.PlayTick(0.8f);
                    }

                    return;
                }

                if (_drawing)
                {
                    ShopRow(row, button, line, ShopDigRow, BaitRules.WormId, "Накопать червей",
                        "за сараем, +" + BaitRules.DigWormsCount + " штук", InkSoft, new Color(0.16f, 0.58f, 0.30f, 0.12f));
                    var pressed = FxRect(button, _shopFxRow == ShopDigRow, !_shopFxOk, _shopFxTime);
                    Round(pressed, Leaf, pressed.height / 2);
                    Label(pressed, "даром", _center, Color.white);
                    GUI.color = Color.white;
                }

                line++;
            }

            for (int i = 0; i < baits.Count; i++)
            {
                var bait = baits[i];
                if (bait.kind != shelfKind)
                    continue;
                var row = new Rect(x, y, cw, rowH - 6 * s);
                var button = new Rect(row.xMax - buttonWidth - 8 * s, row.center.y - 17 * s, buttonWidth, 34 * s);
                y += rowH;
                int needLevel = TackleRules.LevelFor(bait.minLevel);
                bool locked = profile.Level < needLevel;
                bool enough = profile.Money >= bait.price;
                if (Tapped(button))
                {
                    bool ok = !locked && _game.BuyBait(bait.id);
                    ShopFx(line, ok, "+" + BaitRules.PackSize);
                    if (ok)
                    {
                        ShopSay(Capital(BaitName(bait.id)) + ": +" + BaitRules.PackSize + " штук");
                        if (_feedback != null)
                            _feedback.PlaySell(1);
                    }
                    else
                    {
                        ShopSay(locked ? "Продаётся с " + needLevel + " уровня"
                            : enough ? "Больше в коробку не влезет" : "Не хватает монет — сдай улов на рынке");
                        if (_feedback != null)
                            _feedback.PlayTick(0.9f);
                    }

                    return;
                }

                if (!_drawing)
                {
                    line++;
                    continue;
                }

                int have = profile.Baits.Count(bait.id);
                bool fresh = _shopFxRow == line && _shopFxOk && _shopFxTime < 0.9f;
                ShopRow(row, button, line, line, bait.id, Capital(BaitName(bait.id)),
                    BaitKind(bait.id) + (have > 0 ? " · есть " + have : ""), fresh ? Leaf : InkSoft, new Color(0.18f, 0.50f, 0.82f, 0.08f));
                var pressed = FxRect(button, _shopFxRow == line, !_shopFxOk, _shopFxTime);
                if (locked)
                {
                    Round(pressed, new Color(0.08f, 0.20f, 0.25f, 0.12f), pressed.height / 2);
                    Label(pressed, "с " + needLevel + " ур.", _word2Center(), InkSoft);
                    GUI.color = Color.white;
                    line++;
                    continue;
                }

                if (enough)
                {
                    Round(new Rect(pressed.x, pressed.y + 2 * s, pressed.width, pressed.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), pressed.height / 2);
                    Round(pressed, Coral, pressed.height / 2);
                    // Блик пробегает по кнопке после покупки.
                    if (fresh && _shopFxTime < 0.45f)
                    {
                        float sweep = _shopFxTime / 0.45f;
                        float gx = Mathf.Lerp(pressed.x, pressed.xMax - 18 * s, sweep);
                        Round(new Rect(gx, pressed.y + 3 * s, 18 * s, pressed.height - 6 * s), new Color(1f, 1f, 1f, 0.35f * (1f - sweep)), 9 * s);
                    }
                }
                else
                {
                    Round(pressed, new Color(0.08f, 0.20f, 0.25f, 0.14f), pressed.height / 2);
                }

                string price = MoneyNumber(bait.price);
                float pw = TextWidth(price, _word);
                float px = pressed.center.x - (pw + 18 * s) / 2;
                Coin(new Vector2(px + 6 * s, pressed.center.y), 6 * s);
                Label(new Rect(px + 18 * s, pressed.y, pw + 6 * s, pressed.height), price, _word, enough ? Color.white : InkSoft);
                GUI.color = Color.white;
                line++;
            }

            if (!_drawing)
                return;
            // «Накопать червей» — строка с номером ShopDigRow (−1): её подпись всплывает от первой строки.
            int fxShift = _shopFxRow == ShopDigRow ? 1 : 0;
            ShopFooter(card, x, cw, listTop + rowH * fxShift, rowH, "Удилища, катушки, леска и приманки — на вкладках сверху.");
        }

        /// <summary>Низ магазина: всплывающая подпись покупки («+10», «куплено») и строка-подсказка.</summary>
        private void ShopFooter(Rect card, float x, float cw, float listTop, float rowH, string hint)
        {
            float s = _scale;
            if (_shopFxText != null && _shopFxTime < 1.0f && _shopFxRow != int.MinValue)
            {
                float p = _shopFxTime / 1.0f;
                float fy = float.IsNaN(_shopFxAtY) ? listTop + rowH * _shopFxRow + (rowH - 6 * s) / 2 : _shopFxAtY;
                float rise = (1f - (1f - p) * (1f - p)) * 30 * s;
                Label(new Rect(x + 30 * s, fy - 12 * s - rise, 110 * s, 24 * s), _shopFxText, _center, WithAlpha(Leaf, 1f - p * p));
            }

            bool note = _shopNoteLeft > 0f && _shopNote != null;
            Label(new Rect(x, card.yMax - 52 * s, cw, 40 * s), note ? _shopNote : hint, _coachStyle, note ? Ink : InkSoft);
        }

        /// <summary>
        /// Строка магазина: выезжает при открытии (по очереди), вспыхивает после покупки, значок подпрыгивает.
        /// Оставляет GUI.color с прозрачностью строки — вызывающий дорисовывает кнопку и возвращает белый.
        /// </summary>
        private void ShopRow(Rect row, Rect button, int line, int id, string baitId, string title, string sub, Color subInk, Color fill)
        {
            float s = _scale;
            float show = Mathf.Clamp01((_shopOpenTime - 0.04f * line) / 0.26f);
            show = 1f - (1f - show) * (1f - show) * (1f - show);
            GUI.color = new Color(1f, 1f, 1f, show);
            float slide = (1f - show) * 14 * s;
            var at = new Rect(row.x, row.y + slide, row.width, row.height);
            Round(at, fill, 14 * s);
            bool bought = _shopFxRow == id && _shopFxOk;
            if (bought && _shopFxTime < 0.6f)
                Round(at, new Color(0.16f, 0.58f, 0.30f, 0.22f * (1f - _shopFxTime / 0.6f)), 14 * s);
            float bounce = bought && _shopFxTime < 0.5f ? 1f + 0.30f * Mathf.Sin(Mathf.PI * _shopFxTime / 0.5f) * (1f - _shopFxTime / 0.5f) : 1f;
            BaitDot(new Vector2(at.x + 26 * s, at.center.y), Mathf.Min(19 * s, at.height / 2 - s) * bounce, baitId, true);
            Label(new Rect(at.x + 54 * s, at.center.y - 19 * s, row.width - 156 * s, 20 * s), title, _word, Ink);
            Label(new Rect(at.x + 54 * s, at.center.y + 1 * s, row.width - 156 * s, 16 * s), sub, SmallLine(), subInk);
        }

        private void ShopSay(string text)
        {
            _shopNote = text;
            _shopNoteLeft = 2.5f;
        }

        // ---------- Общее ----------

        /// <summary>
        /// Шапка светлой карточки: фон, заголовок с подписью, крестик и (если money) монеты. true — крестик нажат.
        /// </summary>
        private bool CardHeader(Rect card, string title, string sub, bool money)
        {
            float s = _scale;
            var close = new Rect(card.xMax - 52 * s, card.y + 12 * s, 40 * s, 40 * s);
            if (Tapped(close))
                return true;
            if (!_drawing)
                return false;

            LightPill(card, 24 * s);
            Round(close, new Color(0.08f, 0.20f, 0.25f, 0.08f), 20 * s);
            var c = close.center;
            float k = 6.5f * s;
            Bar(c + new Vector2(-k, -k), c + new Vector2(k, k), 2.4f * s, Ink);
            Bar(c + new Vector2(-k, k), c + new Vector2(k, -k), 2.4f * s, Ink);
            if (money && _game.Profile != null)
                LightMoney(new Vector2(close.x - 8 * s, card.y + 18 * s), _game.Profile.Money);
            float x = card.x + 16 * s;
            float cw = card.width - 32 * s;
            Label(new Rect(x, card.y + 12 * s, cw - 150 * s, 36 * s), title, _title, InkTitle);
            Label(new Rect(x, card.y + 52 * s, cw, 18 * s), sub, _smallLeft, InkSoft);
            return false;
        }

        private readonly System.Collections.Generic.Dictionary<string, Texture2D> _baitPictures =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>Картинка наживки из Resources/Art/Bait (tools/art/bait_icons.py); null — картинки нет.</summary>
        private Texture2D BaitPicture(string baitId)
        {
            if (string.IsNullOrEmpty(baitId))
                return null;
            if (!_baitPictures.TryGetValue(baitId, out var picture))
            {
                picture = Resources.Load<Texture2D>("Art/Bait/" + baitId);
                _baitPictures[baitId] = picture;
            }
            return picture;
        }

        /// <summary>
        /// Значок наживки в круге радиуса radius: картинка на мягкой подложке (light — на светлой карточке, иначе
        /// на тёмном стекле). Нет картинки — кружок цвета наживки.
        /// </summary>
        private void BaitDot(Vector2 center, float radius, string id, bool light)
        {
            if (!_drawing)
                return;
            var picture = BaitPicture(id);
            if (picture != null)
            {
                Disc(center, radius, light ? new Color(0.18f, 0.50f, 0.82f, 0.12f) : new Color(1f, 1f, 1f, 0.14f));
                float half = radius * 0.86f;
                GUI.DrawTexture(new Rect(center.x - half, center.y - half, 2 * half, 2 * half), picture, ScaleMode.ScaleToFit, true);
                return;
            }

            Disc(center, radius, light ? new Color(0.08f, 0.20f, 0.25f, 0.22f) : new Color(1f, 1f, 1f, 0.85f));
            Disc(center, radius - 1.6f * _scale, BaitColor(id));
            Disc(center + new Vector2(-radius * 0.28f, -radius * 0.30f), radius * 0.22f, new Color(1f, 1f, 1f, 0.45f));
        }

        private static Color BaitColor(string id)
        {
            switch (id)
            {
                case "worm": return new Color(0.80f, 0.40f, 0.36f);
                case "maggot": return new Color(0.96f, 0.91f, 0.78f);
                case "bloodworm": return new Color(0.76f, 0.12f, 0.16f);
                case "bread": return new Color(0.90f, 0.74f, 0.46f);
                case "dough": return new Color(0.95f, 0.87f, 0.68f);
                case "corn": return new Color(0.98f, 0.80f, 0.16f);
                case "pearl_barley": return new Color(0.84f, 0.79f, 0.66f);
                case "peas": return new Color(0.47f, 0.72f, 0.27f);
                case "livebait": return new Color(0.56f, 0.70f, 0.82f);
                case "nightcrawler": return new Color(0.66f, 0.34f, 0.34f);
                case "caddis": return new Color(0.78f, 0.72f, 0.50f);
                case "bark_grub": return new Color(0.94f, 0.86f, 0.66f);
                case "grasshopper": return new Color(0.50f, 0.74f, 0.30f);
                case "semolina": return new Color(0.97f, 0.93f, 0.80f);
                case "oats": return new Color(0.88f, 0.80f, 0.60f);
                case "pasta": return new Color(0.96f, 0.84f, 0.50f);
                case "oilcake": return new Color(0.58f, 0.44f, 0.28f);
                case "leech": return new Color(0.24f, 0.26f, 0.22f);
                case "frog": return new Color(0.36f, 0.62f, 0.30f);
                case "spinner_1":
                case "spinner":
                case "spinner_3":
                    return new Color(0.80f, 0.84f, 0.88f);
                case "spoon_1":
                case "spoon_2":
                    return new Color(0.90f, 0.76f, 0.36f);
                case "twister_1":
                case "twister_2":
                case "jig":
                    return new Color(0.62f, 0.85f, 0.25f);
                case "wobbler_1":
                case "wobbler":
                case "wobbler_3":
                    return new Color(0.95f, 0.55f, 0.20f);
                case "popper": return new Color(0.92f, 0.30f, 0.30f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        /// <summary>
        /// Род наживки или приманки — общая подсказка, без списка рыбы (кто на что берёт, узнают на воде).
        /// У приманки — семейство и ход: «вертушка · в полводы».
        /// </summary>
        private string BaitKind(string id)
        {
            var data = _game.TackleData;
            if (data == null || !data.TryGetBait(id, out var bait))
                return "";
            switch (bait.kind)
            {
                case BaitKinds.Animal: return "животная";
                case BaitKinds.Plant: return "растительная";
                case BaitKinds.Live: return "для хищника";
                case BaitKinds.Spinner: return "вертушка · " + RunName(bait.run);
                case BaitKinds.Spoon: return "колебалка · " + RunName(bait.run);
                case BaitKinds.Soft: return "силикон · " + RunName(bait.run);
                default: return "воблер · " + RunName(bait.run);
            }
        }

        private static string RunName(string run)
        {
            switch (run)
            {
                case LureRuns.Top: return "идёт поверху";
                case LureRuns.Surface: return "у поверхности";
                case LureRuns.Bottom: return "у дна";
                default: return "в полводы";
            }
        }

        private static string Capital(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
