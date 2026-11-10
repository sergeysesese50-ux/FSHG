using System.Globalization;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Садок: круглая кнопка «Садок» внизу по центру (вес улова), над ней плашка профиля; экран садка — светлая
    // карточка с плитками рыбы (картинка, вес, цена); нажатие на рыбу открывает её страницу — где, когда и на что
    // поймана, «Отпустить». На рынке турбазы тот же экран с «Продать» и «Продать всё». Правила (вместимость, цены, продажа) — в Fishing.Core
    // (PlayerProfile); здесь только показ и нажатия. Пока экран открыт, заброс и кнопки вываживания не работают.
    public sealed partial class LakeHud
    {

        private bool _keepnetOpen;
        private int _keepnetPage;
        private Rect _keepnetButtonRect;
        private string _saleText;
        private float _saleTextLeft;

        // Кэш подписей кнопки и кошелька: строки собираются только при изменении чисел.
        private int _netWeightKey = -1;
        private string _netWeightText;
        private long _moneyKey = -1;
        private string _moneyText;

        /// <summary>«В садок» на карточке улова нажата в этом кадре (или Enter).</summary>
        public bool KeepPressed { get; private set; }

        /// <summary>«Отпустить» на карточке улова нажата в этом кадре.</summary>
        public bool ReleasePressed { get; private set; }

        /// <summary>«На живца» на карточке улова нажата в этом кадре (кнопка есть только у рыбы до 100 г).</summary>
        public bool LiveBaitPressed { get; private set; }

        /// <summary>Экран садка открыт: ввод рыбалки (заброс, кнопки) в это время не читается.</summary>
        public bool KeepnetOpen => _keepnetOpen;

        // ---------- Кнопка и кошелёк ----------

        /// <summary>Кнопка «Садок» и монеты: вне вываживания, внизу по центру (между «Подтянуть» и «Подмоткой»).</summary>
        private void KeepnetButton(Rect area)
        {
            var profile = _game.Profile;
            if (profile == null)
                return;
            float s = _scale;
            float gap = area.width - 2 * Mathf.Min(area.width * 0.36f, 150 * s) - 8 * s;
            float d = Mathf.Clamp(gap - 16 * s, 60 * s, 84 * s);
            var rect = new Rect(area.center.x - d / 2, area.yMax - d - 8 * s, d, d);
            _keepnetButtonRect = rect;
            if (Tapped(rect))
            {
                _keepnetOpen = true;
                _keepnetPage = 0;
                _swiping = false;
            }

            if (!_drawing)
            {
                ProfileChip(new Vector2(area.center.x, rect.y - 36 * s), profile);
                return;
            }

            RoundButton(rect, Glass, GlassBorder, 1.5f * s, new Color(0f, 0f, 0f, 0f));
            NetIcon(new Vector2(rect.center.x, rect.y + d * 0.20f), d * 0.24f, d * 0.30f, Color.white);
            Label(new Rect(rect.x, rect.y + d * 0.56f, d, 16 * s), "Садок", _small, Color.white);
            Label(new Rect(rect.x, rect.y + d * 0.56f + 13 * s, d, 14 * s), NetWeightText(profile.Keepnet.TotalWeightG), _tiny, Soft);

            var chip = new Vector2(area.center.x, rect.y - 36 * s);
            ProfileChip(chip, profile);
            KarmaToast(new Vector2(chip.x, chip.y - 36 * s));
        }

        /// <summary>Плашка с монетами: янтарный кружок и число. center — середина верхней кромки.</summary>
        private void MoneyPill(Vector2 center, long money)
        {
            if (!_drawing)
                return;
            float s = _scale;
            string text = MoneyText(money);
            float h = 26 * s;
            float w = TextWidth(text, _word) + 40 * s;
            var rect = new Rect(center.x - w / 2, center.y, w, h);
            GlassPanel(rect, Glass, h / 2);
            Rim(rect, GlassBorder, 1.2f * s, h / 2);
            Coin(new Vector2(rect.x + 14 * s, rect.center.y), 6.5f * s);
            Label(new Rect(rect.x + 26 * s, rect.y, w - 30 * s, h), text, _word, Color.white);
        }

        private void Coin(Vector2 center, float radius)
        {
            Disc(center, radius, Amber);
            Circle(center, radius * 0.62f, Mathf.Max(1f, radius * 0.16f), new Color(0.55f, 0.33f, 0.05f, 0.8f));
        }

        /// <summary>Значок садка: три обруча, сужающиеся книзу, и боковые стойки.</summary>
        private void NetIcon(Vector2 top, float halfWidth, float height, Color color)
        {
            float w = Mathf.Max(1.5f, 2f * _scale);
            for (int i = 0; i < 3; i++)
            {
                float t = i / 2f;
                float half = halfWidth * Mathf.Lerp(1f, 0.55f, t);
                float y = top.y + height * t;
                Bar(new Vector2(top.x - half, y), new Vector2(top.x + half, y), w, color);
            }

            Bar(new Vector2(top.x - halfWidth, top.y), new Vector2(top.x - halfWidth * 0.55f, top.y + height), w * 0.8f, WithAlpha(color, 0.7f));
            Bar(new Vector2(top.x + halfWidth, top.y), new Vector2(top.x + halfWidth * 0.55f, top.y + height), w * 0.8f, WithAlpha(color, 0.7f));
        }

        // ---------- Экран садка (на турбазе он же — рынок) ----------

        private const float FishCellHeight = 128f;
        private int _keepnetFish;      // номер открытой рыбы (её страница «где и на что поймана»); 0 — список
        private readonly System.Collections.Generic.Dictionary<string, Texture2D> _fishPictures =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>Картинка вида из Resources/Art/Fish (tools/art/fish_icons.py); null — картинки нет.</summary>
        private Texture2D FishPicture(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId))
                return null;
            if (!_fishPictures.TryGetValue(speciesId, out var picture))
            {
                picture = Resources.Load<Texture2D>("Art/Fish/" + speciesId);
                _fishPictures[speciesId] = picture;
            }
            return picture;
        }

        private void KeepnetPanel(Rect area)
        {
            var profile = _game.Profile;
            var fish = _game.Fish;
            if (profile == null || fish == null)
            {
                _keepnetOpen = false;
                return;
            }

            float s = _scale;
            var net = profile.Keepnet;
            bool market = _campOpen;
            float w = Mathf.Min(area.width, 380 * s);
            float top = area.y + 48 * s;
            float h = Mathf.Min(area.yMax - top - 8 * s, 640 * s);
            var card = new Rect(area.center.x - w / 2, top, w, h);
            float x = card.x + 16 * s;
            float cw = w - 32 * s;

            var close = new Rect(card.xMax - 52 * s, card.y + 12 * s, 40 * s, 40 * s);
            if (Tapped(close))
            {
                _keepnetOpen = false;
                _keepnetFish = 0;
                return;
            }

            if (_drawing)
            {
                LightPill(card, 24 * s);
                Round(close, new Color(0.08f, 0.20f, 0.25f, 0.08f), 20 * s);
                var c = close.center;
                float k = 6.5f * s;
                Bar(c + new Vector2(-k, -k), c + new Vector2(k, k), 2.4f * s, Ink);
                Bar(c + new Vector2(-k, k), c + new Vector2(k, -k), 2.4f * s, Ink);
                LightMoney(new Vector2(close.x - 8 * s, card.y + 18 * s), profile.Money);
            }

            var opened = _keepnetFish != 0 ? net.Find(_keepnetFish) : null;
            if (opened != null)
            {
                FishPage(card, opened, fish, market);
                return;
            }

            _keepnetFish = 0;
            if (_drawing)
            {
                Label(new Rect(x, card.y + 12 * s, cw - 150 * s, 36 * s), market ? "Рынок" : "Садок", _title, InkTitle);
                string summary = Kg(net.TotalWeightG) + " из " + Kg(net.CapacityG) + " кг · " + net.Count + " "
                                 + Plural(net.Count, "рыба", "рыбы", "рыб");
                Label(new Rect(x, card.y + 52 * s, cw, 18 * s), summary, _smallLeft, InkSoft);
                var bar = new Rect(x, card.y + 76 * s, cw, 8 * s);
                Round(bar, new Color(0.08f, 0.20f, 0.25f, 0.10f), 4 * s);
                float share = Mathf.Clamp01(net.TotalWeightG / (float)net.CapacityG);
                if (share > 0f)
                    Round(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * share), bar.height), share >= 0.999f ? Red : Coral, 4 * s);
                if (net.Count == 0)
                    Label(new Rect(x, card.y + 150 * s, cw, 60 * s), "Садок пуст.\nПойманная рыба появится здесь.", _coachStyle, InkSoft);
            }

            // Рыба — плитками по две в ряд: картинка, название, вес и цена. Нажатие открывает страницу рыбы.
            float listTop = card.y + 98 * s;
            float listBottom = card.yMax - 72 * s;
            float gap = 8 * s;
            float cellW = (cw - gap) / 2f;
            float cellH = FishCellHeight * s;
            int rows = Mathf.Max(1, Mathf.FloorToInt((listBottom - listTop - 36 * s + gap) / (cellH + gap)));
            int perPage = rows * 2;
            int pages = Mathf.Max(1, (net.Count + perPage - 1) / perPage);
            _keepnetPage = Mathf.Clamp(_keepnetPage, 0, pages - 1);
            int first = _keepnetPage * perPage;
            int last = Mathf.Min(net.Count, first + perPage);
            for (int i = first; i < last; i++)
            {
                var caught = net.Fish[i];
                int slot = i - first;
                var cell = new Rect(x + (slot % 2) * (cellW + gap), listTop + (slot / 2) * (cellH + gap), cellW, cellH);
                if (Tapped(cell))
                {
                    _keepnetFish = caught.id;
                    return;
                }

                if (_drawing)
                    FishCell(cell, caught, fish);
            }

            if (pages > 1)
            {
                float py = listBottom - 34 * s;
                var prev = new Rect(card.center.x - 84 * s, py, 44 * s, 32 * s);
                var next = new Rect(card.center.x + 40 * s, py, 44 * s, 32 * s);
                if (Tapped(prev) && _keepnetPage > 0)
                    _keepnetPage--;
                if (Tapped(next) && _keepnetPage < pages - 1)
                    _keepnetPage++;
                if (_drawing)
                {
                    Round(prev, new Color(0.08f, 0.20f, 0.25f, 0.08f), 16 * s);
                    Round(next, new Color(0.08f, 0.20f, 0.25f, 0.08f), 16 * s);
                    Label(prev, "<", _center, Ink);
                    Label(next, ">", _center, Ink);
                    Label(new Rect(prev.xMax, py, next.x - prev.xMax, 32 * s), (_keepnetPage + 1) + " / " + pages, _small, InkSoft);
                }
            }

            // Продажа — только на рынке турбазы; на озере садок можно смотреть и отпускать из него рыбу.
            var sell = new Rect(x, card.yMax - 62 * s, cw, 48 * s);
            if (!market)
            {
                if (_drawing && net.Count > 0)
                    Label(sell, "Сдать улов можно на рынке турбазы", _coachStyle, InkSoft);
                return;
            }

            if (net.Count > 0)
            {
                long value = profile.KeepnetValue(fish);
                if (Tapped(sell))
                {
                    var receipt = _game.SellKeepnet();
                    _saleText = "Продано: " + receipt.Count + " " + Plural(receipt.Count, "рыба", "рыбы", "рыб") + ", "
                                + Kg(receipt.WeightG) + " кг · +" + MoneyNumber(receipt.Money);
                    _saleTextLeft = 4f;
                    _keepnetPage = 0;
                    if (_feedback != null)
                        _feedback.PlaySell(receipt.Count);
                    return;
                }

                if (_drawing)
                {
                    Round(new Rect(sell.x, sell.y + 2 * s, sell.width, sell.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), 18 * s);
                    Round(sell, Coral, 18 * s);
                    Label(sell, "Продать всё · +" + MoneyNumber(value), _center, Color.white);
                }
            }
            else if (_drawing && _saleTextLeft > 0f && _saleText != null)
            {
                Label(sell, _saleText, _coachStyle, WithAlpha(Leaf, Mathf.Clamp01(_saleTextLeft / 0.5f)));
            }
        }

        /// <summary>
        /// Плитка рыбы в списке: картинка, название, вес с длиной, цена; у трофея — золотая метка,
        /// у зачётной — зелёный кружок с галочкой в том же углу; у мелочи до 100 г справа вверху — значок живца.
        /// </summary>
        private void FishCell(Rect cell, CaughtFish caught, FishDatabase fish)
        {
            float s = _scale;
            Round(cell, new Color(0.18f, 0.50f, 0.82f, 0.08f), 16 * s);
            var picture = FishPicture(caught.speciesId);
            var frame = new Rect(cell.x + 8 * s, cell.y + 6 * s, cell.width - 16 * s, 62 * s);
            if (picture != null)
                GUI.DrawTexture(frame, picture, ScaleMode.ScaleToFit, true);
            fish.TryGet(caught.speciesId, out var species);
            if (caught.trophy)
            {
                var badge = new Rect(cell.x + 8 * s, cell.y + 8 * s, 64 * s, 18 * s);
                Round(badge, Gold, 9 * s);
                Label(badge, "ТРОФЕЙ", _chip, Ink);
            }
            else if (species != null && FishGrades.Of(species, caught.weightG, false) == FishGrade.Qualifying)
            {
                QualifyingMark(new Vector2(cell.x + 18 * s, cell.y + 17 * s), 9 * s);
            }

            if (BaitRules.CanBeLiveBait(caught.weightG))
                BaitDot(new Vector2(cell.xMax - 19 * s, cell.y + 19 * s), 12 * s, BaitRules.LiveBaitId, true);

            Label(new Rect(cell.x + 12 * s, cell.y + 70 * s, cell.width - 20 * s, 20 * s), FishName(caught.speciesId), _word, Ink);
            Label(new Rect(cell.x + 12 * s, cell.y + 89 * s, cell.width - 20 * s, 16 * s),
                Grams(caught.weightG) + " г · " + caught.lengthCm + " см", _smallLeft, InkSoft);
            long price = species != null ? FishPricing.Price(species, caught.weightG, caught.trophy) : 0;
            Coin(new Vector2(cell.x + 18 * s, cell.y + 114 * s), 5.5f * s);
            Label(new Rect(cell.x + 28 * s, cell.y + 105 * s, 80 * s, 18 * s), MoneyNumber(price), _word, Ink);
        }

        /// <summary>Значок зачётной рыбы: зелёный кружок в белой кайме с белой галочкой.</summary>
        private void QualifyingMark(Vector2 center, float radius)
        {
            Disc(center, radius + 1.5f * _scale, Color.white);
            Disc(center, radius, Leaf);
            float w = radius * 0.26f;
            var knee = center + new Vector2(-radius * 0.12f, radius * 0.36f);
            Bar(center + new Vector2(-radius * 0.46f, radius * 0.02f), knee, w, Color.white);
            Bar(knee, center + new Vector2(radius * 0.48f, -radius * 0.34f), w, Color.white);
        }

        /// <summary>
        /// Страница одной рыбы: крупная картинка, вес и разряд, где и на что поймана, цена; «Отпустить»,
        /// а на рынке ещё и «Продать».
        /// </summary>
        private void FishPage(Rect card, CaughtFish caught, FishDatabase fish, bool market)
        {
            float s = _scale;
            float x = card.x + 16 * s;
            float cw = card.width - 32 * s;
            fish.TryGet(caught.speciesId, out var species);
            long price = species != null ? FishPricing.Price(species, caught.weightG, caught.trophy) : 0;

            var back = new Rect(x, card.y + 12 * s, 92 * s, 40 * s);
            float gap = 10 * s;
            var release = new Rect(x, card.yMax - 66 * s, market ? cw * 0.42f : cw, 52 * s);
            var sell = new Rect(release.xMax + gap, release.y, cw - release.width - gap, 52 * s);
            // Мелочь годится на живца — наживку для хищника.
            bool small = BaitRules.CanBeLiveBait(caught.weightG);
            var live = new Rect(x, release.y - 46 * s, cw, 38 * s);
            if (Tapped(back))
            {
                _keepnetFish = 0;
                return;
            }

            if (Tapped(release))
            {
                _game.ReleaseFish(caught.id);
                _keepnetFish = 0;
                if (_feedback != null)
                    _feedback.PlayRelease();
                return;
            }

            if (small && Tapped(live))
            {
                if (_game.MakeLiveBait(caught.id))
                {
                    _keepnetFish = 0;
                    if (_feedback != null)
                        _feedback.PlayTick(0.4f);
                }

                return;
            }

            if (market && Tapped(sell))
            {
                _game.SellFish(caught.id);
                _keepnetFish = 0;
                if (_feedback != null)
                    _feedback.PlaySell(1);
                return;
            }

            if (!_drawing)
                return;

            Round(back, new Color(0.08f, 0.20f, 0.25f, 0.08f), 20 * s);
            var arrow = new Vector2(back.x + 18 * s, back.center.y);
            Bar(arrow + new Vector2(5 * s, -6 * s), arrow + new Vector2(-2 * s, 0), 2.2f * s, Ink);
            Bar(arrow + new Vector2(5 * s, 6 * s), arrow + new Vector2(-2 * s, 0), 2.2f * s, Ink);
            Label(new Rect(back.x + 30 * s, back.y, back.width - 30 * s, back.height), "Назад", _word, Ink);

            // Картинка: рыба чуть покачивается на голубом фоне.
            var stage = new Rect(x, card.y + 62 * s, cw, 150 * s);
            Round(stage, new Color(0.18f, 0.50f, 0.82f, 0.10f), 20 * s);
            var picture = FishPicture(caught.speciesId);
            if (picture != null)
            {
                float bob = Mathf.Sin(Time.time * 1.6f) * 3f * s;
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Sin(Time.time * 1.1f) * 1.5f, stage.center);
                GUI.DrawTexture(new Rect(stage.x + 14 * s, stage.y + 8 * s + bob, stage.width - 28 * s, stage.height - 16 * s),
                    picture, ScaleMode.ScaleToFit, true);
                GUI.matrix = matrix;
            }

            float y = stage.yMax + 10 * s;
            Label(new Rect(x, y, cw * 0.6f, 32 * s), FishName(caught.speciesId), _title, InkTitle);
            Label(new Rect(x, y - 3 * s, cw, 38 * s), GramsText(caught.weightG), _weight, Coral);
            y += 36 * s;

            // Разряд — плашкой, рядом длина.
            float chipX = x;
            if (species != null)
            {
                switch (FishGrades.Of(species, caught.weightG, caught.trophy))
                {
                    case FishGrade.Trophy: StaticChip(ref chipX, y, "ТРОФЕЙ", Gold, Ink); break;
                    case FishGrade.Qualifying: StaticChip(ref chipX, y, "ЗАЧЁТНАЯ", Leaf, Color.white); break;
                    case FishGrade.Juvenile: StaticChip(ref chipX, y, "МАЛЁК", Slate, Color.white); break;
                }
            }

            Label(new Rect(chipX + 4 * s, y, 120 * s, 24 * s), "≈ " + caught.lengthCm + " см", _smallLeft, InkSoft);
            y += 34 * s;

            // Где и на что поймана.
            Fact(x, ref y, cw, "Где", string.IsNullOrEmpty(caught.spotId) ? "—" : SpotName(caught.spotId));
            Fact(x, ref y, cw, "Когда", (caught.caughtAtMinute / 60 % 24).ToString("00") + ":" + (caught.caughtAtMinute % 60).ToString("00"));
            Fact(x, ref y, cw, "Наживка", string.IsNullOrEmpty(caught.baitId) ? "—" : BaitName(caught.baitId));
            Fact(x, ref y, cw, "Глубина наживки", caught.baitDepthCm > 0 ? Kg(caught.baitDepthCm * 10) + " м" : "—");
            Fact(x, ref y, cw, "Снасть", string.IsNullOrEmpty(caught.rodId) ? "—" : RodName(caught.rodId) + " · " + LineName(caught.lineId));
            Fact(x, ref y, cw, "Цена", MoneyNumber(price) + " " + Plural((int)(price % 100), "монета", "монеты", "монет"));

            if (small)
            {
                Round(live, new Color(0.18f, 0.50f, 0.82f, 0.12f), 19 * s);
                Ring(live, new Color(0.18f, 0.50f, 0.82f, 0.45f), 1.4f * s, 19 * s);
                BaitDot(new Vector2(live.x + 24 * s, live.center.y), 15 * s, BaitRules.LiveBaitId, true);
                Label(new Rect(live.x + 46 * s, live.y, live.width - 54 * s, live.height), "На живца · +1 в коробку с наживкой", _word, Ink);
            }

            Round(release, new Color(0.08f, 0.20f, 0.25f, 0.08f), 18 * s);
            Ring(release, new Color(0.08f, 0.20f, 0.25f, 0.22f), 1.4f * s, 18 * s);
            Label(new Rect(release.x, release.y + 7 * s, release.width, 22 * s), "Отпустить", _center, Ink);
            Label(new Rect(release.x, release.y + 30 * s, release.width, 16 * s), "карма +1", _tiny, Leaf);
            if (market)
            {
                Round(new Rect(sell.x, sell.y + 2 * s, sell.width, sell.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), 18 * s);
                Round(sell, Coral, 18 * s);
                Label(new Rect(sell.x, sell.y + 7 * s, sell.width, 22 * s), "Продать", _center, Color.white);
                Label(new Rect(sell.x, sell.y + 30 * s, sell.width, 16 * s), "+" + MoneyNumber(price), _tiny, Color.white);
            }
        }

        private void StaticChip(ref float x, float y, string text, Color fill, Color ink)
        {
            float s = _scale;
            float w = TextWidth(text, _chip) + 20 * s;
            var rect = new Rect(x, y, w, 24 * s);
            x += w + 6 * s;
            Round(rect, fill, 12 * s);
            Label(rect, text, _chip, ink);
        }

        /// <summary>Строка «подпись — значение» на странице рыбы.</summary>
        private void Fact(float x, ref float y, float width, string name, string value)
        {
            float s = _scale;
            Round(new Rect(x, y, width, 26 * s), new Color(0.08f, 0.20f, 0.25f, 0.05f), 9 * s);
            Label(new Rect(x + 12 * s, y, 130 * s, 26 * s), name, _smallLeft, InkSoft);
            Label(new Rect(x + 140 * s, y, width - 150 * s, 26 * s), value, _word, Ink);
            y += 30 * s;
        }

        /// <summary>Светлая плашка с монетами; right — её правый верхний угол.</summary>
        private void LightMoney(Vector2 right, long money)
        {
            float s = _scale;
            string text = MoneyText(money);
            float h = 28 * s;
            float w = TextWidth(text, _word) + 42 * s;
            var rect = new Rect(right.x - w, right.y, w, h);
            Round(rect, new Color(0.08f, 0.20f, 0.25f, 0.08f), h / 2);
            Coin(new Vector2(rect.x + 15 * s, rect.center.y), 6.5f * s);
            Label(new Rect(rect.x + 28 * s, rect.y, w - 32 * s, h), text, _word, Ink);
        }

        /// <summary>Плашка с монетами, прижатая правым краем к точке right (её верхний правый угол). Тёмная — для профиля.</summary>
        private void MoneyPillRight(Vector2 right, long money)
        {
            float w = TextWidth(MoneyText(money), _word) + 40 * _scale;
            MoneyPill(new Vector2(right.x - w / 2, right.y), money);
        }

        private static string BaitName(string id)
        {
            switch (id)
            {
                case "worm": return "червь";
                case "maggot": return "опарыш";
                case "bread": return "хлеб";
                case "corn": return "кукуруза";
                case "bloodworm": return "мотыль";
                case "dough": return "тесто";
                case "pearl_barley": return "перловка";
                case "peas": return "горох";
                case "livebait": return "живец";
                case "nightcrawler": return "выползок";
                case "caddis": return "ручейник";
                case "bark_grub": return "личинка короеда";
                case "grasshopper": return "кузнечик";
                case "semolina": return "манка";
                case "oats": return "геркулес";
                case "pasta": return "макароны";
                case "oilcake": return "макуха";
                case "leech": return "пиявка";
                case "frog": return "лягушка";
                case "spinner_1": return "вертушка №1";
                case "spinner_3": return "вертушка №3";
                case "spoon_1": return "колебалка №1";
                case "spoon_2": return "колебалка №2";
                case "twister_1": return "твистер №1";
                case "twister_2": return "твистер №2";
                case "wobbler_1": return "воблер №1";
                case "wobbler_3": return "воблер глубинный";
                case "popper": return "поппер";
                case "spinner": return "вертушка №2";
                case "jig": return "джиг";
                case "wobbler": return "воблер №2";
                default: return id;
            }
        }

        private static string RodName(string id)
        {
            switch (id)
            {
                case "float_rod_basic": return "поплавочная 4 м";
                case "float_rod_tele5": return "телескоп 5 м";
                case "float_rod_long": return "маховая 6 м";
                case "float_rod_bolo": return "болонская 5 м";
                case "float_rod_match": return "матчевая 4,2 м";
                case "float_rod_carbon7": return "карбоновая 7 м";
                case "bottom_rod_picker": return "пикер 2,7 м";
                case "feeder_rod_heavy": return "тяжёлый фидер 3,9 м";
                case "carp_rod": return "карповое 3,6 м";
                case "spinning_rod_ultralight": return "ультралайт 1,8 м";
                case "spinning_rod_medium": return "спиннинг 2,7 м";
                case "spinning_rod_jig": return "джиговый 2,4 м";
                case "spinning_rod_trophy": return "трофейный 3 м";
                case "bottom_rod_basic": return "донка 3 м";
                case "feeder_rod": return "фидер 3,6 м";
                case "spinning_rod_basic": return "спиннинг 2,4 м";
                case "spinning_rod_strong": return "силовой спиннинг 2,7 м";
                default: return id;
            }
        }

        /// <summary>«леска 0,16» из id line_016, «плетёнка 0,14» из braid_014.</summary>
        private static string LineName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            int cut = id.IndexOf('_');
            if (cut < 0 || cut + 2 >= id.Length)
                return id;
            string size = id.Substring(cut + 1, 1) + "," + id.Substring(cut + 2);
            return (id.StartsWith("braid") ? "плетёнка " : "леска ") + size;
        }

        // ---------- Строки ----------

        private string NetWeightText(int weightG)
        {
            if (weightG != _netWeightKey || _netWeightText == null)
            {
                _netWeightKey = weightG;
                _netWeightText = weightG == 0 ? "пусто" : Kg(weightG) + " кг";
            }
            return _netWeightText;
        }

        private string MoneyText(long money)
        {
            if (money != _moneyKey || _moneyText == null)
            {
                _moneyKey = money;
                _moneyText = MoneyNumber(money);
            }
            return _moneyText;
        }

        /// <summary>Число с пробелами между тысячами: 12 500.</summary>
        private static string MoneyNumber(long value) => value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

        private static string Grams(int grams) => MoneyNumber(grams);

        /// <summary>Килограммы с одним знаком после запятой: 3,2.</summary>
        private static string Kg(int grams) => (grams / 1000f).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

        private static string Plural(int count, string one, string few, string many)
        {
            int n = Mathf.Abs(count) % 100;
            if (n >= 11 && n <= 14)
                return many;
            switch (n % 10)
            {
                case 1: return one;
                case 2:
                case 3:
                case 4: return few;
                default: return many;
            }
        }
    }
}
