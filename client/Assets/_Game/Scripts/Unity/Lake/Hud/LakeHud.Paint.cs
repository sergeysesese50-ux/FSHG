using UnityEngine;
using UnityEngine.Rendering;

namespace Fishing.Unity
{
    // Примитивы HUD: цвета «Золотого часа», стили, скруглённые прямоугольники через GUI.DrawTexture с радиусом,
    // «стекло» (тень, заливка, блик сверху, светлая кромка), отрезки и дуги, линии GL для графика. Всё рисует только второй проход Ui() (OnGUI, Repaint):
    // в первом проходе (Update) те же вызовы ничего не рисуют, а кнопки проверяют нажатия.
    public sealed partial class LakeHud
    {
        // Цвета — неизменяемые значения (static readonly struct), не состояние.
        private static readonly Color Glass = new Color(0.12f, 0.06f, 0.04f, 0.62f);
        private static readonly Color GlassBorder = new Color(1f, 1f, 1f, 0.30f);
        private static readonly Color Amber = new Color(1f, 0.71f, 0.28f);
        private static readonly Color Orange = new Color(0.94f, 0.48f, 0.23f);
        private static readonly Color Red = new Color(0.91f, 0.26f, 0.18f);
        private static readonly Color Green = new Color(0.45f, 0.82f, 0.42f);
        private static readonly Color Sky = new Color(0.55f, 0.80f, 1f);
        private static readonly Color Dark = new Color(0.16f, 0.12f, 0.10f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color Soft = new Color(1f, 1f, 1f, 0.78f);

        /// <summary>Зоны нагрузки (§5): зелёная до 55%, янтарная до 85%, выше — красная.</summary>
        private const float SafeZone = 0.55f;
        private const float DangerZone = 0.85f;

        private float _scale = 1f;
        private GUIStyle _text;
        private GUIStyle _paragraph;
        private GUIStyle _small;
        private GUIStyle _smallLeft;
        private GUIStyle _tiny;
        private GUIStyle _center;
        private GUIStyle _title;
        private GUIStyle _big;
        private GUIStyle _gaugeBig;
        private GUIStyle _counter;
        private GUIStyle _rate;
        private GUIStyle _word;
        private GUIStyle _coachStyle;
        private GUIStyle _strike;
        private GUIStyle _tipHeader;
        private GUIStyle _weight;
        private GUIStyle _chip;
        private GUIStyle _depthStyle;
        private GUIStyle _depthCaption;

        private readonly GUIContent _measure = new GUIContent();
        private Material _glMaterial;
        private bool _glMissing;
        private Texture2D _sheen;
        private Texture2D _halo;
        private Font _fontText;
        private Font _fontBold;

        // ---------- Кнопки и подписи ----------

        /// <summary>Нажатие по прямоугольнику в этом кадре (первый проход); во втором всегда false.</summary>
        private bool Tapped(Rect rect)
        {
            if (_drawing)
                return false;
            foreach (var tap in _taps)
            {
                if (rect.Contains(tap))
                    return true;
            }
            return false;
        }

        private bool Button(Rect rect, string label, Color? fill = null, Color? textColor = null)
        {
            if (_drawing)
            {
                float radius = Mathf.Min(rect.height, rect.width) * 0.3f;
                GlassPanel(rect, fill ?? Glass, radius);
                if (fill == null)
                    Rim(rect, GlassBorder, 1.5f * _scale, radius);
                Label(rect, label, _center, textColor ?? Color.white);
                return false;
            }
            return Tapped(rect);
        }

        /// <summary>Переключатель-пилюля: включённый — янтарный, выключенный — стекло.</summary>
        private bool Toggle(Rect rect, string label, bool on)
        {
            if (_drawing)
            {
                GlassPanel(rect, on ? WithAlpha(Amber, 0.85f) : Glass, rect.height / 2);
                Rim(rect, on ? Amber : GlassBorder, 1.2f * _scale, rect.height / 2);
                Label(rect, label, _small, on ? Dark : Color.white);
                return false;
            }
            return Tapped(rect);
        }

        private void Pill(Rect rect, string text, GUIStyle style)
        {
            if (!_drawing)
                return;
            GlassPanel(rect, Glass, rect.height / 2);
            Rim(rect, GlassBorder, 1.2f * _scale, rect.height / 2);
            // Короткий текст стоит по центру плашки; длинный (с переносами) — от левого края.
            float pad = 12 * _scale;
            float width = TextWidth(text, style);
            if (width < rect.width - 2 * pad)
                Label(new Rect(rect.center.x - width / 2, rect.y, width + 4 * _scale, rect.height), text, style, Color.white);
            else
                Label(new Rect(rect.x + pad, rect.y, rect.width - 2 * pad, rect.height), text, style, Color.white);
        }

        private void Label(Rect rect, string text, GUIStyle style, Color? color = null)
        {
            if (!_drawing || text == null)
                return;
            var old = GUI.contentColor;
            GUI.contentColor = color ?? Color.white;
            GUI.Label(rect, text, style);
            GUI.contentColor = old;
        }

        private float TextWidth(string text, GUIStyle style)
        {
            _measure.text = text;
            return style.CalcSize(_measure).x;
        }

        // ---------- Фигуры ----------

        private void Round(Rect rect, Color color, float radius)
        {
            if (_drawing)
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        private void Ring(Rect rect, Color color, float width, float radius)
        {
            if (_drawing)
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, width, radius);
        }

        /// <summary>
        /// «Стекло»: мягкая тень под панелью, заливка и блик — светлее сверху, как у выпуклой кнопки.
        /// strength — доля тени и блика (для панелей, которые проявляются и гаснут).
        /// </summary>
        private void GlassPanel(Rect rect, Color fill, float radius, float strength = 1f)
        {
            if (!_drawing)
                return;
            float s = _scale;
            // Тень: четыре тонких слоя вплотную к панели — мягкий край без заметных ступеней.
            for (int i = 1; i <= 4; i++)
            {
                float grow = 0.7f * i * s;
                var shadow = new Rect(rect.x - grow, rect.y - grow + 1.5f * s, rect.width + 2 * grow, rect.height + 2 * grow);
                Round(shadow, new Color(0.05f, 0.02f, 0.01f, 0.055f * strength), radius + grow);
            }

            Round(rect, fill, radius);
            GUI.DrawTexture(rect, Sheen(), ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.07f * strength), 0f, radius);
        }

        /// <summary>
        /// Круглая кнопка макета: тень, тёмное стекло, цветная кромка и мягкое свечение наружу
        /// (у главных кнопок — янтарное). glow с нулевой альфой — без свечения.
        /// </summary>
        private void RoundButton(Rect rect, Color fill, Color rim, float rimWidth, Color glow)
        {
            if (!_drawing)
                return;
            float s = _scale;
            float radius = rect.width / 2;
            Halo(rect, new Color(0.04f, 0.02f, 0.01f, 0.42f), 3f * s);
            if (glow.a > 0.004f)
                Halo(rect, glow, 0f);
            Round(rect, fill, radius);
            GUI.DrawTexture(rect, Sheen(), ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.10f), 0f, radius);
            Ring(rect, rim, rimWidth, radius);
            GUI.DrawTexture(rect, Sheen(), ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.35f), rimWidth, radius);
        }

        /// <summary>Мягкий ореол вокруг круга rect: ярче всего у кромки, гаснет наружу.</summary>
        private void Halo(Rect rect, Color color, float offsetY)
        {
            if (!_drawing)
                return;
            float half = rect.width / 2 / HaloEdge;
            var c = rect.center;
            GUI.DrawTexture(new Rect(c.x - half, c.y - half + offsetY, 2 * half, 2 * half), HaloTexture(), ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
        }

        /// <summary>Доля радиуса текстуры ореола, на которой лежит кромка кнопки.</summary>
        private const float HaloEdge = 0.6f;

        private Texture2D HaloTexture()
        {
            if (_halo != null)
                return _halo;
            const int size = 64;
            _halo = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha;
                    if (r < HaloEdge)
                    {
                        float inside = r / HaloEdge;
                        alpha = Mathf.Lerp(0.12f, 1f, inside * inside * inside);
                    }
                    else
                    {
                        float outside = 1f - Mathf.Clamp01((r - HaloEdge) / (1f - HaloEdge));
                        alpha = outside * outside;
                    }

                    _halo.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            _halo.Apply();
            return _halo;
        }

        /// <summary>Плашка с иконкой (солнце днём, месяц ночью) и текстом: вместе они стоят по центру плашки.</summary>
        private void PillIcon(Rect rect, string text, GUIStyle style, bool night)
        {
            if (!_drawing)
                return;
            float s = _scale;
            GlassPanel(rect, Glass, rect.height / 2);
            Rim(rect, GlassBorder, 1.2f * s, rect.height / 2);
            // Иконка (с лучами — 17,2 единицы в ширину), зазор и текст — одной группой по центру.
            float icon = 17.2f * s;
            float gap = 7 * s;
            float textWidth = Mathf.Min(TextWidth(text, style), rect.width - icon - gap - 20 * s);
            float left = rect.center.x - (icon + gap + textWidth) / 2;
            var c = new Vector2(left + icon / 2, rect.center.y);
            if (night)
            {
                Disc(c, 6.5f * s, new Color(0.86f, 0.91f, 1f));
                Disc(c + new Vector2(3.4f * s, -2.4f * s), 5.6f * s, new Color(0.14f, 0.08f, 0.06f));
            }
            else
            {
                Disc(c, 4.2f * s, Amber);
                for (int i = 0; i < 8; i++)
                    Spoke(c, i * 45f, 6.2f * s, 8.6f * s, 1.6f * s, Amber);
            }

            Label(new Rect(left + icon + gap, rect.y, textWidth + 4 * s, rect.height), text, style, Color.white);
        }

        /// <summary>Две галочки вверх: «тянуть на себя».</summary>
        private void Chevrons(Vector2 center, float size, Color color)
        {
            float w = Mathf.Max(2f, size * 0.22f);
            for (int i = 0; i < 2; i++)
            {
                var top = center + new Vector2(0f, (i - 0.5f) * size * 0.7f - size * 0.25f);
                Bar(top + new Vector2(-size * 0.6f, size * 0.5f), top, w, color);
                Bar(top + new Vector2(size * 0.6f, size * 0.5f), top, w, color);
            }
        }

        /// <summary>Кромка стекла: тонкое кольцо, сверху ярче — будто ловит свет.</summary>
        private void Rim(Rect rect, Color color, float width, float radius)
        {
            if (!_drawing)
                return;
            Ring(rect, WithAlpha(color, 0.55f), width, radius);
            GUI.DrawTexture(rect, Sheen(), ScaleMode.StretchToFill, true, 0f, color, width, radius);
        }

        /// <summary>Вертикальный градиент прозрачности: непрозрачный сверху, прозрачный снизу.</summary>
        private Texture2D Sheen()
        {
            if (_sheen != null)
                return _sheen;
            const int height = 32;
            _sheen = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < height; y++)
            {
                float t = y / (height - 1f);
                _sheen.SetPixel(0, y, new Color(1f, 1f, 1f, t * t));
            }

            _sheen.Apply();
            return _sheen;
        }

        /// <summary>Кнопка под пальцем чуть сжимается: press 0…1.</summary>
        private static Rect Pressed(Rect rect, float press)
        {
            float inset = rect.width * 0.035f * press;
            return new Rect(rect.x + inset, rect.y + inset, rect.width - 2 * inset, rect.height - 2 * inset);
        }

        private void Disc(Vector2 center, float radius, Color color)
        {
            Round(new Rect(center.x - radius, center.y - radius, 2 * radius, 2 * radius), color, radius);
        }

        private void Circle(Vector2 center, float radius, float width, Color color)
        {
            Ring(new Rect(center.x - radius, center.y - radius, 2 * radius, 2 * radius), color, width, radius);
        }

        /// <summary>Отрезок толщиной width со скруглёнными концами.</summary>
        private void Bar(Vector2 from, Vector2 to, float width, Color color)
        {
            if (!_drawing)
                return;
            var delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
                return;
            var middle = (from + to) * 0.5f;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, middle);
            float w = Mathf.Min(width, length);
            GUI.DrawTexture(new Rect(middle.x - length / 2, middle.y - width / 2, length, width), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0f, color, 0f, w / 2);
            GUI.matrix = matrix;
        }

        /// <summary>Радиальная чёрточка дуги: от радиуса r0 до r1 под углом angle (градусы, 0 — вправо, 90 — вверх).</summary>
        private void Spoke(Vector2 center, float angle, float r0, float r1, float width, Color color)
        {
            var direction = Polar(angle);
            Bar(center + direction * r0, center + direction * r1, width, color);
        }

        /// <summary>Верхняя половина кольца (подложка дуги натяжения): круг-рамка, обрезанный группой.</summary>
        private void HalfRing(Vector2 center, float radius, float width, Color color)
        {
            if (!_drawing)
                return;
            float outer = radius + width / 2;
            GUI.BeginGroup(new Rect(center.x - outer, center.y - outer, 2 * outer, outer + width * 0.25f));
            GUI.DrawTexture(new Rect(0, 0, 2 * outer, 2 * outer), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                color, width, outer);
            GUI.EndGroup();
        }

        /// <summary>Единичный вектор экрана под углом в градусах: 0 — вправо, 90 — вверх (ось y экрана вниз).</summary>
        private static Vector2 Polar(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private static Color Zone(float share)
        {
            if (share < SafeZone) return Green;
            return share < DangerZone ? Amber : Red;
        }

        // ---------- Линии GL (график на карточке схода) ----------

        /// <summary>Материал для GL: встроенный Hidden/Internal-Colored, без глубины, с прозрачностью.</summary>
        private Material GlMaterial()
        {
            if (_glMaterial != null || _glMissing)
                return _glMaterial;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                _glMissing = true;
                return null;
            }

            _glMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // Свойства шейдера объявлены как Int в ShaderLab — это числа с плавающей точкой.
            _glMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _glMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            _glMaterial.SetFloat("_Cull", (float)CullMode.Off);
            _glMaterial.SetFloat("_ZWrite", 0f);
            _glMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            return _glMaterial;
        }

        private void OnDestroy()
        {
            if (_glMaterial != null)
                Destroy(_glMaterial);
            if (_sheen != null)
                Destroy(_sheen);
            if (_halo != null)
                Destroy(_halo);
        }

        // ---------- Стили ----------

        private void EnsureStyles()
        {
            int size = Mathf.RoundToInt(14 * _scale);
            if (_text != null && _text.fontSize == size)
                return;
            // Шрифт макета — Manrope из Resources/Fonts (два начертания: обычный текст и жирный).
            // Системный шрифт (Font.CreateDynamicFontFromOSFont) не годится: в IMGUI этой версии Unity
            // он рисуется мелко и без выравнивания.
            if (_fontText == null)
            {
                _fontText = Resources.Load<Font>("Fonts/Manrope-Medium");
                _fontBold = Resources.Load<Font>("Fonts/Manrope-Bold");
            }

            _text = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true, alignment = TextAnchor.MiddleLeft, richText = true };
            _text.normal.textColor = Color.white;
            _paragraph = new GUIStyle(_text) { alignment = TextAnchor.UpperLeft, fontSize = Px(13) };
            _small = new GUIStyle(_text) { fontSize = Px(11), alignment = TextAnchor.MiddleCenter };
            _smallLeft = new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft };
            _tiny = new GUIStyle(_text) { fontSize = Px(10), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _center = new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter, fontSize = Px(16), fontStyle = FontStyle.Bold };
            _title = new GUIStyle(_text) { fontSize = Px(24), fontStyle = FontStyle.Bold };
            _big = new GUIStyle(_text) { fontSize = Px(32), alignment = TextAnchor.MiddleCenter };
            _gaugeBig = new GUIStyle(_big) { fontSize = Px(24), fontStyle = FontStyle.Bold, wordWrap = false };
            _counter = new GUIStyle(_center) { fontSize = Px(22), wordWrap = false };
            _rate = new GUIStyle(_center) { fontSize = Px(14), wordWrap = false };
            _word = new GUIStyle(_center) { fontSize = Px(13), alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _coachStyle = new GUIStyle(_center) { fontSize = Px(13) };
            _strike = new GUIStyle(_center) { fontSize = Px(30), wordWrap = false };
            _tipHeader = new GUIStyle(_text) { fontSize = Px(13), fontStyle = FontStyle.Bold };
            _weight = new GUIStyle(_text) { fontSize = Px(30), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            _chip = new GUIStyle(_small) { fontStyle = FontStyle.Bold, wordWrap = false };
            _depthStyle = new GUIStyle(_text) { fontSize = Px(26), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            _depthCaption = new GUIStyle(_text) { fontSize = Px(11), alignment = TextAnchor.MiddleRight, wordWrap = false };

            var styles = new[]
            {
                _text, _paragraph, _small, _smallLeft, _tiny, _center, _title, _big, _gaugeBig, _counter, _rate, _word,
                _coachStyle, _strike, _tipHeader, _weight, _chip, _depthStyle, _depthCaption,
            };
            foreach (var style in styles)
                UseFont(style);
        }

        /// <summary>Жирным стилям — жирный файл шрифта (а не «утолщение» обычного), остальным — обычный.</summary>
        private void UseFont(GUIStyle style)
        {
            if (_fontText == null || _fontBold == null)
                return;
            bool bold = style.fontStyle == FontStyle.Bold || style.fontStyle == FontStyle.BoldAndItalic;
            style.font = bold ? _fontBold : _fontText;
            style.fontStyle = FontStyle.Normal;
        }

        private int Px(float size) => Mathf.RoundToInt(size * _scale);
    }
}
