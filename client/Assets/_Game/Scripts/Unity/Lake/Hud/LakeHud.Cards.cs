using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Карточки конца вываживания (docs/specs/fight-v2.md, §5), по FishingController.Report:
    // - улов: карточка в Hud/LakeHud.Catch.cs (здесь — только её строки «≈ длина · способ»);
    // - сход: итог, причина с числами, один совет «Как надо», график натяжения / фрикциона / предела за 10 с
    //   (история отчёта — 200 шагов по 0,05 с) линиями GL.
    // Строки собираются один раз в конце вываживания (EndFightHud), в кадре только рисуются.
    public sealed partial class LakeHud
    {
        // Подпись графика: цвета совпадают с линиями (белая, Sky, Red).
        private const string SparkLegend =
            "<color=#ffffff>— натяжение</color>   <color=#8cccff>— фрикцион</color>   <color=#e8432e>— предел</color>   · последние 10 с";

        /// <summary>
        /// Кнопка карточки не принимает касания первые полсекунды: на телефоне она перекрывает «Подтянуть», и тап,
        /// которым выудили рыбу или добили схватку, иначе мог бы сразу закрыть карточку.
        /// </summary>
        private const float CardTapDelaySeconds = 0.5f;

        private FightReport _cardReport;
        private string _cardTitle;
        private string _cardSubtitle;
        private string _cardCause;
        private string _cardTip;
        private string _winSize;
        private string _winTime;
        private string _winRests;
        private int _gramsKey = -1;
        private string _gramsText;

        private bool CardAcceptsTaps => _game.StateTime >= CardTapDelaySeconds;

        private void EnsureCardTexts(FightReport report)
        {
            if (!ReferenceEquals(report, _cardReport))
                BuildCardTexts(report);
        }

        private void BuildCardTexts(FightReport r)
        {
            _cardReport = r;
            var f = _game.Frame;
            string name = _game.Species != null ? FishName(_game.Species.id) : "Рыба";

            if (r.Outcome == FightOutcome.Landed)
            {
                _winSize = "≈ " + Mathf.RoundToInt(_game.FishLengthM * 100f) + " см · "
                           + (r.Landing == FightLanding.Netted ? "подсачеком" : "махом удилища");
                _winTime = "За " + HudText.Seconds(r.ElapsedSeconds) + " · пик нагрузки " + HudText.Percent(r.MaxLoadShare);
                _winRests = "подтянул " + r.RestsLifted + " из " + r.RestsTotal + " "
                            + HudText.Plural(r.RestsTotal, "паузы", "пауз", "пауз") + " · "
                            + HudText.Seconds(r.SecondsCrankedOnSlip) + " вхолостую";
                return;
            }

            _cardTitle = FightLossText.Title(r.Outcome);
            _cardSubtitle = name + " · ≈ " + HudText.Fixed(HudText.Scaled(f.WeightG / 1000.0, 1), 1) + " кг · "
                            + HudText.Seconds(r.ElapsedSeconds);
            _cardCause = FightLossText.Cause(r, _game.Tackle, f.Stamina);
            _cardTip = FightLossText.Tip(r, _game.Tackle, f.FishForceG);
        }

        private void LostCard(Rect area)
        {
            var report = _game.Report;
            if (report == null)
                return;
            EnsureCardTexts(report);

            float s = _scale;
            float w = Mathf.Min(area.width, 380 * s);
            float spark = Mathf.Clamp(area.height - 44 * s - 300 * s, 44 * s, 84 * s);
            float h = 300 * s + spark;
            float y = Mathf.Max(area.y + 44 * s, Mathf.Min(area.y + area.height * 0.34f, area.yMax - h));
            var card = new Rect(area.center.x - w / 2, y, w, h);
            GlassPanel(card, new Color(0.12f, 0.06f, 0.04f, 0.9f), 22 * s);
            Rim(card, GlassBorder, 1.2f * s, 22 * s);

            float x = card.x + 20 * s;
            float cw = w - 40 * s;
            float cy = card.y + 16 * s;
            Label(new Rect(x, cy, cw, 32 * s), _cardTitle, _title, Color.white);
            cy += 32 * s;
            Label(new Rect(x, cy, cw, 18 * s), _cardSubtitle, _smallLeft, Soft);
            cy += 22 * s;
            Label(new Rect(x, cy, cw, 56 * s), _cardCause, _paragraph, Color.white);
            cy += 58 * s;
            Label(new Rect(x, cy, cw, 18 * s), "Как надо", _tipHeader, Amber);
            cy += 20 * s;
            Label(new Rect(x, cy, cw, 58 * s), _cardTip, _paragraph, new Color(1f, 1f, 1f, 0.88f));
            cy += 60 * s;

            Sparkline(new Rect(x, cy, cw, spark), report);
            cy += spark + 2 * s;
            Label(new Rect(x, cy, cw, 14 * s), SparkLegend, _tiny, Color.white);

            var button = new Rect(x, card.yMax - 58 * s, cw, 44 * s);
            if (Button(button, "Дальше", Color.white, Dark) && CardAcceptsTaps)
                ContinuePressed = true;
        }

        /// <summary>
        /// График последних 10 с: натяжение с ударами (PeakG), фрикцион и предел самого слабого звена.
        /// Линии GL в пикселях экрана (верх — 0, как у IMGUI); толщина — несколько смещённых проходов.
        /// История короче 10 с прижата к правому краю (ось времени постоянная).
        /// </summary>
        private void Sparkline(Rect rect, FightReport r)
        {
            float s = _scale;
            Round(rect, new Color(0f, 0f, 0f, 0.28f), 10 * s);
            int n = r.HistoryCount;
            if (!_drawing || n < 2)
                return;
            var material = GlMaterial();
            if (material == null)
                return;

            double top = 1.0;
            for (int i = 0; i < n; i++)
            {
                var sample = r.History(i);
                if (sample.PeakG > top) top = sample.PeakG;
                if (sample.LimitG > top) top = sample.LimitG;
                if (sample.DragG > top) top = sample.DragG;
            }
            top *= 1.08;

            float left = rect.x + 6 * s;
            float right = rect.xMax - 6 * s;
            float bottom = rect.yMax - 5 * s;
            float height = rect.height - 10 * s;
            float step = (right - left) / (FightReport.HistoryCapacity - 1);
            float start = right - step * (n - 1);
            int passes = Mathf.Clamp(Mathf.RoundToInt(1.6f * s), 1, 4);

            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.LINES);
            SparkSeries(r, 2, WithAlpha(Red, 0.9f), start, step, bottom, height, top, passes);
            SparkSeries(r, 1, Sky, start, step, bottom, height, top, passes);
            SparkSeries(r, 0, Color.white, start, step, bottom, height, top, passes);
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>Одна линия графика: 0 — натяжение (PeakG), 1 — фрикцион, 2 — предел.</summary>
        private static void SparkSeries(FightReport r, int series, Color color, float start, float step, float bottom,
            float height, double top, int passes)
        {
            GL.Color(color);
            int n = r.HistoryCount;
            for (int pass = 0; pass < passes; pass++)
            {
                float offset = pass - (passes - 1) * 0.5f;
                float px = 0f, py = 0f;
                for (int i = 0; i < n; i++)
                {
                    var sample = r.History(i);
                    double value = series == 0 ? sample.PeakG : series == 1 ? sample.DragG : sample.LimitG;
                    float x = start + step * i;
                    float y = bottom - (float)(value / top) * height + offset;
                    if (i > 0)
                    {
                        GL.Vertex3(px, py, 0f);
                        GL.Vertex3(x, y, 0f);
                    }
                    px = x;
                    py = y;
                }
            }
        }

        /// <summary>Вес на карточке улова: «1 234 г»; строка пересобирается, только когда меняется число.</summary>
        private string GramsText(float grams)
        {
            int g = Mathf.RoundToInt(grams);
            if (g != _gramsKey || _gramsText == null)
            {
                _gramsKey = g;
                _gramsText = g >= 1000 ? (g / 1000) + " " + (g % 1000).ToString("000") + " г" : g + " г";
            }
            return _gramsText;
        }
    }
}
