using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Оснастка на берегу и на воде:
    // - «Вытащить» — круглая кнопка над «Подтянуть», пока ждём поклёвку: сразу достаёт удочку (клавиша X);
    // - спуск поплавка — пока удочка на берегу, справа внизу стоит столбик воды с поплавком и крючком: тянешь
    //   крючок вверх-вниз пальцем — меняется глубина наживки. Рядом — число и рыба этого места, которая держится
    //   на такой глубине (значки у столбика подсвечиваются).
    // Правила спуска — Fishing.Core.FloatRig; здесь только показ и жест.
    public sealed partial class LakeHud
    {
        private Rect _retrieveRect;
        private Rect _rigRect;
        private bool _rigDrag;
        private float _rigDragY;
        private float _rigTouched;      // сколько секунд назад трогали столбик (подсказка «тяни» гаснет)

        private int _rigTextKey = int.MinValue;
        private string _rigNumber;
        private string _rigFish;
        private bool _rigAnyFish;

        /// <summary>«Вытащить» нажата в этом кадре (кнопка или клавиша X), пока поплавок ждёт поклёвку.</summary>
        public bool RetrievePressed { get; private set; }

        // ---------- «Вытащить» ----------

        private void RetrieveButton()
        {
            float s = _scale;
            float d = 62 * s;
            var rect = new Rect(_liftRect.x + 6 * s, _liftRect.y - 12 * s - d, d, d);
            _retrieveRect = rect;
            if (Tapped(rect))
                RetrievePressed = true;
            if (!_drawing)
                return;

            RoundButton(rect, Glass, GlassBorder, 1.5f * s, new Color(0f, 0f, 0f, 0f));
            // Удилище и стрелка вверх.
            var c = new Vector2(rect.center.x, rect.y + d * 0.34f);
            Bar(c + new Vector2(-d * 0.16f, d * 0.12f), c + new Vector2(d * 0.10f, -d * 0.14f), 2.2f * s, Color.white);
            Bar(c + new Vector2(d * 0.17f, d * 0.10f), c + new Vector2(d * 0.17f, -d * 0.12f), 1.8f * s, Color.white);
            Bar(c + new Vector2(d * 0.17f, -d * 0.12f), c + new Vector2(d * 0.10f, -d * 0.04f), 1.8f * s, Color.white);
            Bar(c + new Vector2(d * 0.17f, -d * 0.12f), c + new Vector2(d * 0.24f, -d * 0.04f), 1.8f * s, Color.white);
            Label(new Rect(rect.x - 6 * s, rect.y + d * 0.60f, d + 12 * s, 16 * s), "Вытащить", _tiny, Color.white);
        }

        // ---------- Спуск поплавка ----------

        /// <summary>Столбик настройки спуска: справа, над нижним рядом кнопок, пока удочка на берегу.</summary>
        private void RigWidget(Rect area)
        {
            float s = _scale;
            int max = _game.FloatDepthMaxCm;
            int min = FloatRig.MinDepthCm;
            if (max <= min)
                return;

            float width = 20 * s;
            float height = 156 * s;
            // Выше нижнего ряда кнопок и плашки с монетами — чтобы подписи ни на что не наезжали.
            var column = new Rect(area.xMax - width - 12 * s, area.yMax - height - 152 * s, width, height);
            float top = column.y + 14 * s;          // поверхность воды в столбике
            float bottom = column.yMax - 8 * s;     // наибольший спуск
            _rigRect = new Rect(column.x - 30 * s, column.y - 12 * s, width + 46 * s, height + 24 * s);

            if (!_drawing)
            {
                if (_rigDrag)
                {
                    float share = Mathf.InverseLerp(top, bottom, _rigDragY);
                    int before = _game.FloatDepthCm;
                    _game.SetFloatDepth(Mathf.RoundToInt(Mathf.Lerp(0f, max, share)));
                    _rigTouched = 0f;
                    // Каждое деление отзывается тихим щелчком и лёгкой вибрацией.
                    if (_game.FloatDepthCm != before && _feedback != null)
                        _feedback.PlayTick(_game.FloatDepthCm / (float)max);
                }
                return;
            }

            int depth = _game.FloatDepthCm;
            float radius = width / 2;
            float hookY = Mathf.Lerp(top, bottom, depth / (float)max);

            // Вода.
            Round(new Rect(column.x - 2 * s, column.y, column.width + 4 * s, column.height + 4 * s), new Color(0f, 0f, 0f, 0.28f), radius + 2 * s);
            Round(column, new Color(0.05f, 0.27f, 0.42f, 0.95f), radius);
            GUI.DrawTexture(column, Sheen(), ScaleMode.StretchToFill, true, 0f, new Color(0.55f, 0.88f, 1f, 0.75f), 0f, radius);
            Ring(column, new Color(1f, 1f, 1f, 0.55f), 1.3f * s, radius);

            // Деления: каждые полметра, метровые длиннее.
            for (int cm = 50; cm < max; cm += 50)
            {
                float y = Mathf.Lerp(top, bottom, cm / (float)max);
                float len = cm % 100 == 0 ? 7 * s : 4 * s;
                Round(new Rect(column.xMax + 3 * s, y - 0.8f * s, len, 1.6f * s), new Color(1f, 1f, 1f, 0.55f), 0.8f * s);
            }

            // Рыба этого места: значок на середине её глубин; подсвечен, если крючок сейчас в её слое.
            var spot = _game.Spot;
            var fish = _game.Fish;
            if (spot != null && fish != null)
            {
                for (int i = 0; i < spot.fish.Length; i++)
                {
                    if (!fish.TryGet(spot.fish[i], out var species))
                        continue;
                    int lo = Mathf.Clamp(species.depthCm.min, 0, max);
                    int hi = Mathf.Clamp(species.depthCm.max, 0, max);
                    if (hi <= lo)
                        continue;
                    bool here = depth >= species.depthCm.min && depth <= species.depthCm.max;
                    float y = Mathf.Lerp(top, bottom, (lo + hi) * 0.5f / max) + (i - 1) * 9 * s;
                    FishMark(new Vector2(column.x - 15 * s, y), s, here ? Amber : new Color(1f, 1f, 1f, 0.45f));
                }
            }

            // Поплавок, леска и крючок с наживкой — за него и тянут.
            float bob = Mathf.Sin(Time.time * 2.2f) * 1.2f * s;
            var floatAt = new Vector2(column.center.x, top + bob);
            var hook = new Vector2(column.center.x, hookY);
            Bar(floatAt, hook, 1f * s, new Color(1f, 1f, 1f, 0.75f));
            Disc(floatAt, 4.4f * s, Color.white);
            Disc(floatAt + new Vector2(0f, -1.9f * s), 3.1f * s, Red);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            float hint = Mathf.Clamp01(1f - _rigTouched / 6f);
            Circle(hook, (9f + 2.5f * pulse * hint) * s, 1.6f * s, new Color(1f, 1f, 1f, 0.35f + 0.4f * hint));
            Disc(hook, 4.2f * s, Orange);
            Bar(hook + new Vector2(0f, 3 * s), hook + new Vector2(3.4f * s, 6 * s), 1.4f * s, Color.white);
            Bar(hook + new Vector2(3.4f * s, 6 * s), hook + new Vector2(5.6f * s, 2.6f * s), 1.4f * s, Color.white);

            // Число и подсказки — слева от столбика, прижаты к нему.
            if (depth * 31 + (spot != null ? spot.id.GetHashCode() : 0) != _rigTextKey || _rigNumber == null)
                BuildRigTexts(depth, spot, fish);
            float right = column.x - 40 * s;
            float textWidth = Mathf.Max(190 * s, right - area.x);   // длинный список рыбы не обрезается слева
            var number = new Rect(right - textWidth, column.y + 22 * s, textWidth, 30 * s);
            ShadowLabel(new Rect(number.x, number.y - 16 * s, textWidth, 16 * s), "спуск поплавка", _depthCaption, Soft);
            ShadowLabel(number, _rigNumber, _depthStyle, Color.white);
            ShadowLabel(new Rect(number.x, number.yMax + 2 * s, textWidth, 16 * s), _rigFish, _depthCaption, _rigAnyFish ? Amber : Soft);
            if (hint > 0.01f)
                ShadowLabel(new Rect(number.x, number.yMax + 19 * s, textWidth, 16 * s), "тяни крючок вверх-вниз", _depthCaption, WithAlpha(Soft, hint));
        }

        private void BuildRigTexts(int depth, LakeSpot spot, FishDatabase fish)
        {
            _rigTextKey = depth * 31 + (spot != null ? spot.id.GetHashCode() : 0);
            _rigNumber = HudText.Fixed(HudText.Scaled(depth / 100.0, 1), 1) + " м";
            string names = "";
            if (spot != null && fish != null)
            {
                foreach (string id in spot.fish)
                {
                    if (fish.TryGet(id, out var species) && depth >= species.depthCm.min && depth <= species.depthCm.max)
                        names += (names.Length > 0 ? ", " : "") + FishName(id).ToLowerInvariant();
                }
            }

            _rigAnyFish = names.Length > 0;
            _rigFish = _rigAnyFish ? "здесь держится: " + names : "на этой глубине рыбы мало";
        }

        /// <summary>Маленькая рыбка-значок, головой к столбику.</summary>
        private void FishMark(Vector2 center, float s, Color color)
        {
            Round(new Rect(center.x - 6 * s, center.y - 2.6f * s, 11 * s, 5.2f * s), color, 2.6f * s);
            var tail = new Vector2(center.x - 6 * s, center.y);
            Bar(tail, tail + new Vector2(-3.4f * s, -2.6f * s), 1.5f * s, color);
            Bar(tail, tail + new Vector2(-3.4f * s, 2.6f * s), 1.5f * s, color);
        }
    }
}
