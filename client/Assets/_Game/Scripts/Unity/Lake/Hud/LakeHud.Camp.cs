using UnityEngine;

namespace Fishing.Unity
{
    // Турбаза «Тихая заводь»: нарисованная сцена во весь экран (Resources/Art/CampDay.png, её рисует
    // tools/art/camp_day.py; дома — в стиле советской турбазы семидесятых), здания — кнопки: нажимается и подпись, и сам дом. Тема «Летний день»: белые плашки, тёмный текст, коралловый акцент.
    // Пока работают причал (обратно на озеро), рынок (сдать улов), «Снасти» (наживка — LakeHud.Bait)
    // и «Лодки» (прокат и продажа — LakeHud.Boats);
    // остальные места отвечают «скоро».
    // С озера на турбазу — круглая кнопка «Турбаза» рядом с садком, когда удочка не заброшена.
    public sealed partial class LakeHud
    {
        // Геометрия картинки — как в tools/art/camp_day.py: сцена 390×844 и запас вокруг неё.
        private const float CampSceneWidth = 390f;
        private const float CampSceneHeight = 844f;
        private const float CampPadX = 125f;
        private const float CampPadTop = 160f;
        private const float CampPadBottom = 96f;
        private const string CampArtPath = "Art/CampDay";
        private const int CampPlaces = 7;

        private static readonly Color Paper = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color Ink = new Color(0.08f, 0.20f, 0.25f);
        private static readonly Color InkSoft = new Color(0.28f, 0.39f, 0.44f);
        private static readonly Color InkTitle = new Color(0.06f, 0.21f, 0.31f);
        private static readonly Color Coral = new Color(0.90f, 0.33f, 0.18f);
        private static readonly Color CampSky = new Color(0.24f, 0.62f, 0.90f);
        private static readonly Color CampGrass = new Color(0.14f, 0.35f, 0.17f);

        private bool _campOpen;
        private Texture2D _campArt;
        private bool _campArtMissing;
        private Rect _campButtonRect;
        private Rect _campChipRect;
        private readonly Rect[] _campPlaceRects = new Rect[CampPlaces];
        private string _campToast;
        private float _campToastLeft;

        // Положение сцены на экране в этом кадре: левый верхний угол сцены и пикселей на единицу сцены.
        private Vector2 _campOrigin;
        private float _campUnit = 1f;

        // ---------- Кнопка на озере ----------

        /// <summary>Круглая кнопка «Турбаза» слева от садка: только пока удочка не заброшена.</summary>
        private void CampButton(Rect area)
        {
            float s = _scale;
            if (_keepnetButtonRect.width <= 0f)
                return;
            float d = SideButtonSize(area);
            var rect = new Rect(_keepnetButtonRect.x - SideButtonGap * s - d, _keepnetButtonRect.yMax - d, d, d);
            _campButtonRect = rect;
            if (Tapped(rect))
            {
                _campOpen = true;
                _swiping = false;
            }

            if (!_drawing)
                return;
            RoundButton(rect, Glass, GlassBorder, 1.5f * s, new Color(0f, 0f, 0f, 0f));
            HouseIcon(new Vector2(rect.center.x, rect.y + d * 0.36f), d * 0.17f, Color.white, 2f * s);
            Label(new Rect(rect.x, rect.y + d * 0.58f, d, 16 * s), "Турбаза", _tiny, Color.white);
        }

        // ---------- Экран турбазы ----------

        private void CampBackdrop() => Backdrop(CampArt(), CampSky, CampGrass);

        /// <summary>
        /// Фон-картинка во весь экран (турбаза, карта озера): сцена 390×844 под ширину экрана; на высоких экранах
        /// сверху и снизу виден запас картинки. top и bottom — цвета под картинкой, если её нет или не хватило.
        /// Заодно считает положение сцены на экране (_campOrigin, _campUnit) — и в проходе нажатий тоже.
        /// </summary>
        private void Backdrop(Texture2D art, Color top, Color bottom)
        {
            float sw = Screen.width;
            float sh = Screen.height;
            float unit = sw / CampSceneWidth;
            float total = CampSceneHeight + CampPadTop + CampPadBottom;
            if (sh / unit > total)
                unit = sh / total;              // очень высокий экран: по высоте, бока сцены подрезаются
            else if (sh / unit < CampSceneHeight)
                unit = sh / CampSceneHeight;    // широкий экран: по высоте, по бокам — запас картинки
            float extra = Mathf.Max(0f, sh / unit - CampSceneHeight);
            _campUnit = unit;
            _campOrigin = new Vector2((sw - CampSceneWidth * unit) / 2f,
                extra * unit * CampPadTop / (CampPadTop + CampPadBottom));

            if (!_drawing)
                return;
            GUI.DrawTexture(new Rect(0, 0, sw, sh * 0.5f), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, top, 0f, 0f);
            GUI.DrawTexture(new Rect(0, sh * 0.5f, sw, sh * 0.5f), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, bottom, 0f, 0f);
            if (art == null)
                return;
            var rect = new Rect(_campOrigin.x - CampPadX * unit, _campOrigin.y - CampPadTop * unit,
                (CampSceneWidth + 2 * CampPadX) * unit, total * unit);
            GUI.DrawTexture(rect, art, ScaleMode.StretchToFill, false);
        }

        private Texture2D CampArt()
        {
            if (_campArt == null && !_campArtMissing)
            {
                _campArt = Resources.Load<Texture2D>(CampArtPath);
                _campArtMissing = _campArt == null;
            }
            return _campArt;
        }

        private void Camp(Rect area)
        {
            var profile = _game.Profile;
            float s = _scale;

            // Верхняя строка: время и погода слева, монеты и карма справа (нажатие — профиль).
            float h = 36 * s;
            if (profile != null && Tapped(_campChipRect))
                _profileOpen = true;
            if (_drawing)
            {
                string clock = TopLeftText();
                float cw = Mathf.Min(area.width * 0.6f, TextWidth(clock, _word) + 28 * s);
                var left = new Rect(area.x, area.y, cw, h);
                LightPill(left, h / 2);
                Label(new Rect(left.x + 14 * s, left.y, cw - 20 * s, h), clock, _word, Ink);

                if (profile != null)
                {
                    string money = MoneyText(profile.Money);
                    string karma = KarmaText(profile.Karma);
                    float mw = TextWidth(money, _word);
                    karma = "карма " + karma;
                    float kw = TextWidth(karma, _word);
                    float w = 28 * s + mw + 12 * s + kw + 16 * s;
                    var chip = new Rect(area.xMax - w, area.y, w, h);
                    _campChipRect = chip;
                    LightPill(chip, h / 2);
                    Coin(new Vector2(chip.x + 16 * s, chip.center.y), 6.5f * s);
                    Label(new Rect(chip.x + 28 * s, chip.y, mw + 4 * s, h), money, _word, Ink);
                    float kx = chip.x + 28 * s + mw + 12 * s;
                    var karmaInk = profile.Karma <= -10 ? Coral : new Color(0.12f, 0.48f, 0.24f);
                    Label(new Rect(kx, chip.y, kw + 8 * s, h), karma, _word, karmaInk);
                }

                Label(new Rect(area.x + 2 * s, area.y + h + 8 * s, area.width, 34 * s), "Турбаза «Тихая заводь»", _title, InkTitle);
            }

            // Места: координаты левого верхнего угла плашки — в единицах сцены, как на макете.
            string catchText = profile != null && profile.Keepnet.Count > 0
                ? "сдать улов · " + NetWeightText(profile.Keepnet.TotalWeightG)
                : "сдать улов";
            // Причал: если мест несколько — на карту озера, выбирать место; иначе сразу на озеро.
            if (CampPlace(0, 16, 338, "Причал", "на озеро", new Rect(136, 318, 86, 98)))
            {
                if (_game.Spots != null && _game.Spots.Length > 1)
                    OpenMap();
                else
                    _campOpen = false;
                return;
            }

            // Лодочная станция — на берегу между причалом и магазином: будка проката и лодки у мостков.
            // Подпись короткая: плашка стоит между парусником и будкой и не должна залезать на причал.
            string boats = profile == null ? "прокат"
                : profile.OwnsBoat ? "лодка своя"
                : profile.BoatRentSecondsLeft > 0.0 ? "ещё " + RentText(profile.BoatRentSecondsLeft)
                : "прокат";
            if (CampPlace(6, 232, 322, "Лодки", boats, new Rect(226, 366, 134, 62)))
            {
                OpenBoats();
                return;
            }

            if (CampPlace(1, 206, 440, "Снасти", "удочки, наживка", new Rect(190, 444, 174, 96)))
                OpenShop();

            if (CampPlace(2, 20, 478, "Рынок", catchText, new Rect(20, 526, 146, 96)))
            {
                _keepnetOpen = true;
                _keepnetPage = 0;
            }

            if (CampPlace(3, 240, 588, "Доска", "задания", new Rect(178, 574, 60, 54)))
                CampSoon("Заданий пока нет");
            if (CampPlace(4, 236, 792, "Домик", "отдых и склад", new Rect(228, 676, 136, 74)))
                CampSoon("Домик скоро откроется");
            if (CampPlace(5, 20, 800, "Мастерская", "ремонт снастей", new Rect(36, 698, 120, 78)))
                CampSoon("Мастерская скоро откроется");

            if (_drawing && _campToastLeft > 0f && _campToast != null)
            {
                float alpha = Mathf.Clamp01(_campToastLeft / 0.4f);
                float tw = TextWidth(_campToast, _word) + 32 * s;
                var toast = new Rect(area.center.x - tw / 2, area.yMax - 54 * s, tw, 38 * s);
                Round(toast, WithAlpha(Ink, 0.9f * alpha), 19 * s);
                Label(new Rect(toast.x + 16 * s, toast.y, tw - 16 * s, toast.height), _campToast, _word, WithAlpha(Color.white, alpha));
            }
        }

        private void CampSoon(string text)
        {
            _campToast = text;
            _campToastLeft = 2.2f;
        }

        /// <summary>
        /// Место на сцене: плашка (коралловый кружок со значком, название и подпись) и само здание на картинке —
        /// building, прямоугольник в единицах сцены. true — нажата плашка или здание в этом кадре.
        /// Ширина плашки зависит от текста и известна только при рисовании, поэтому её нажатие ловится
        /// по прямоугольнику прошлого кадра.
        /// </summary>
        private bool CampPlace(int index, float sceneX, float sceneY, string title, string sub, Rect building)
        {
            if (!_drawing)
            {
                var house = new Rect(_campOrigin.x + building.x * _campUnit, _campOrigin.y + building.y * _campUnit,
                    building.width * _campUnit, building.height * _campUnit);
                return Tapped(_campPlaceRects[index]) || Tapped(house);
            }

            float u = _campUnit;
            float h = 44 * u;
            float textWidth = Mathf.Max(TextWidth(title, _word), TextWidth(sub, _smallLeft));
            float w = 46 * u + textWidth + 16 * u;
            float x = _campOrigin.x + sceneX * u;
            // Плашка не выходит за края экрана.
            x = Mathf.Clamp(x, 8 * u, Mathf.Max(8 * u, Screen.width - w - 8 * u));
            var rect = new Rect(x, _campOrigin.y + sceneY * u, w, h);
            _campPlaceRects[index] = rect;

            LightPill(rect, h / 2);
            var center = new Vector2(rect.x + 22 * u, rect.center.y);
            Disc(center, 16 * u, Coral);
            CampIcon(index, center, 16 * u);
            Label(new Rect(rect.x + 46 * u, rect.y + 5 * u, textWidth + 8 * u, 19 * u), title, _word, Ink);
            Label(new Rect(rect.x + 46 * u, rect.y + 22 * u, textWidth + 8 * u, 16 * u), sub, _smallLeft, InkSoft);
            return false;
        }

        /// <summary>Белая плашка светлой темы с мягкой синеватой тенью.</summary>
        private void LightPill(Rect rect, float radius)
        {
            if (!_drawing)
                return;
            float s = _scale;
            for (int i = 1; i <= 4; i++)
            {
                float grow = 0.8f * i * s;
                var shadow = new Rect(rect.x - grow, rect.y - grow + 2f * s, rect.width + 2 * grow, rect.height + 2 * grow);
                Round(shadow, new Color(0.06f, 0.2f, 0.32f, 0.07f), radius + grow);
            }

            Round(rect, Paper, radius);
        }

        /// <summary>Значок места белыми штрихами в кружке радиуса r: 0 причал, 1 снасти, 2 рынок, 3 доска, 4 домик, 5 мастерская, 6 лодки.</summary>
        private void CampIcon(int kind, Vector2 c, float r)
        {
            float w = Mathf.Max(1.5f, r * 0.14f);
            var white = Color.white;
            switch (kind)
            {
                case 0: // лодка с парусом
                    Bar(c + new Vector2(-r * 0.55f, r * 0.30f), c + new Vector2(r * 0.55f, r * 0.30f), w * 1.4f, white);
                    Bar(c + new Vector2(0f, r * 0.30f), c + new Vector2(0f, -r * 0.55f), w, white);
                    Bar(c + new Vector2(0f, -r * 0.55f), c + new Vector2(r * 0.42f, r * 0.08f), w, white);
                    Bar(c + new Vector2(r * 0.42f, r * 0.08f), c + new Vector2(0f, r * 0.08f), w, white);
                    break;
                case 1: // удилище с катушкой
                    Bar(c + new Vector2(-r * 0.5f, r * 0.5f), c + new Vector2(r * 0.5f, -r * 0.5f), w, white);
                    Circle(c + new Vector2(-r * 0.18f, r * 0.34f), r * 0.2f, w, white);
                    Bar(c + new Vector2(r * 0.5f, -r * 0.5f), c + new Vector2(r * 0.5f, r * 0.1f), w * 0.7f, white);
                    break;
                case 2: // рыба
                    Circle(c + new Vector2(-r * 0.12f, 0f), r * 0.34f, w, white);
                    Bar(c + new Vector2(r * 0.22f, 0f), c + new Vector2(r * 0.58f, -r * 0.28f), w, white);
                    Bar(c + new Vector2(r * 0.22f, 0f), c + new Vector2(r * 0.58f, r * 0.28f), w, white);
                    Disc(c + new Vector2(-r * 0.24f, -r * 0.06f), w * 0.6f, white);
                    break;
                case 3: // доска с листками
                    Ring(new Rect(c.x - r * 0.42f, c.y - r * 0.5f, r * 0.84f, r), white, w, r * 0.1f);
                    Bar(c + new Vector2(-r * 0.2f, -r * 0.18f), c + new Vector2(r * 0.2f, -r * 0.18f), w, white);
                    Bar(c + new Vector2(-r * 0.2f, r * 0.12f), c + new Vector2(r * 0.2f, r * 0.12f), w, white);
                    break;
                case 4:
                    HouseIcon(c, r * 0.5f, white, w);
                    break;
                case 6: // гребная лодка с веслом
                    Bar(c + new Vector2(-r * 0.58f, -r * 0.05f), c + new Vector2(r * 0.58f, -r * 0.05f), w, white);
                    Bar(c + new Vector2(-r * 0.58f, -r * 0.05f), c + new Vector2(-r * 0.32f, r * 0.34f), w, white);
                    Bar(c + new Vector2(r * 0.58f, -r * 0.05f), c + new Vector2(r * 0.32f, r * 0.34f), w, white);
                    Bar(c + new Vector2(-r * 0.32f, r * 0.34f), c + new Vector2(r * 0.32f, r * 0.34f), w, white);
                    Bar(c + new Vector2(-r * 0.10f, -r * 0.50f), c + new Vector2(r * 0.40f, r * 0.10f), w * 0.8f, white);
                    break;
                default: // ключ
                    Bar(c + new Vector2(-r * 0.42f, r * 0.42f), c + new Vector2(r * 0.18f, -r * 0.18f), w * 1.3f, white);
                    Circle(c + new Vector2(r * 0.3f, -r * 0.3f), r * 0.22f, w, white);
                    break;
            }
        }

        /// <summary>Домик: крыша уголком и стены.</summary>
        private void HouseIcon(Vector2 c, float half, Color color, float width)
        {
            Bar(c + new Vector2(-half * 1.15f, 0f), c + new Vector2(0f, -half), width, color);
            Bar(c + new Vector2(half * 1.15f, 0f), c + new Vector2(0f, -half), width, color);
            Ring(new Rect(c.x - half * 0.8f, c.y - half * 0.05f, half * 1.6f, half * 1.05f), color, width, half * 0.1f);
        }
    }
}
