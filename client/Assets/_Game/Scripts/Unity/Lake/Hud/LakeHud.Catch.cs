using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Карточка улова в светлой теме «Летний день»: выезжает снизу, вес набегает, плашки («Трофей», «Зачётная»,
    // «Рекорд вида», «Личный рекорд», «Первая рыба вида», «Малёк») выскакивают по очереди, три плитки с итогами
    // вываживания, подсказка по главному событию, две кнопки с подписями — что даст каждое решение.
    // После решения — короткая анимация поверх озера: рыба улетает в садок («+вес») или по воде расходятся круги
    // и поднимаются пузыри; звуки — FightFeedback.PlayKeep / PlayRelease.
    // Правила разрядов и отметок — Fishing.Core (FishGrades, CatchMarks, KarmaRules); здесь только показ.
    public sealed partial class LakeHud
    {
        private const float CatchExitSeconds = 1.1f;
        private const float CatchFlySeconds = 0.55f;

        private static readonly Color Leaf = new Color(0.16f, 0.58f, 0.30f);
        private static readonly Color Gold = new Color(0.95f, 0.72f, 0.14f);
        private static readonly Color Plum = new Color(0.52f, 0.34f, 0.74f);
        private static readonly Color Azure = new Color(0.18f, 0.50f, 0.82f);
        private static readonly Color Slate = new Color(0.45f, 0.54f, 0.60f);

        private FightReport _catchTextsFor;
        private string _tileTime;
        private string _tilePeak;
        private string _tileRests;
        private string _catchHint;
        private string _catchXp;
        private string _releaseSub;
        private string _keepSub;
        private string _liveSub;
        private bool _liveSubBad;

        private int _exitKind;          // 0 — нет, 1 — в садок, 2 — отпустил, 3 — на живца
        private float _exitTime;
        private Vector2 _exitFrom;
        private string _exitText;
        private string _exitSpecies;

        private void CatchCard(Rect area)
        {
            var report = _game.Report;
            if (report == null)
                return;
            EnsureCardTexts(report);
            if (!ReferenceEquals(report, _catchTextsFor))
                BuildCatchTexts(report);

            float s = _scale;
            float t = _game.StateTime;
            float w = Mathf.Min(area.width, 380 * s);
            float h = 292 * s;
            // Карточка — у нижнего края экрана: рыба над ней видна целиком.
            float rest = Mathf.Max(area.y + 44 * s, area.yMax - h - 6 * s);
            // Выезжает снизу с лёгким перелётом.
            float y = rest + (1f - EaseOutBack(Mathf.Clamp01(t / 0.45f))) * 80 * s;
            var card = new Rect(area.center.x - w / 2, y, w, h);
            float x = card.x + 18 * s;
            float cw = w - 36 * s;

            // Мелочь до 100 г можно сразу пустить на живца: тогда кнопок три.
            bool small = _game.CatchCanBeLiveBait;
            float gap = (small ? 8 : 10) * s;
            float releaseWidth = cw * (small ? 0.30f : 0.40f);
            var release = new Rect(x, card.yMax - 70 * s, releaseWidth, 54 * s);
            var live = new Rect(release.xMax + gap, release.y, small ? cw * 0.31f : 0f, 54 * s);
            float keepX = small ? live.xMax + gap : release.xMax + gap;
            var keep = new Rect(keepX, release.y, x + cw - keepX, 54 * s);
            bool fits = _game.CatchFits;

            if (!_drawing)
            {
                if (CardAcceptsTaps && Tapped(release))
                {
                    ReleasePressed = true;
                    StartCatchExit(2, card);
                }
                else if (small && CardAcceptsTaps && Tapped(live))
                {
                    LiveBaitPressed = true;
                    StartCatchExit(3, card);
                }
                else if (CardAcceptsTaps && Tapped(keep))
                {
                    if (fits)
                    {
                        KeepPressed = true;
                        StartCatchExit(1, card);
                    }
                    else
                    {
                        // Не влезает: открыть садок, отпустить кого-то и вернуться к этой рыбе.
                        _keepnetOpen = true;
                        _keepnetPage = 0;
                    }
                }
                else if (ContinuePressed && fits)
                {
                    KeepPressed = true;
                    StartCatchExit(1, card);
                }
                return;
            }

            var marks = _game.CatchMarks;
            var grade = _game.CatchGrade;
            LightPill(card, 24 * s);

            // Название и вес.
            Label(new Rect(x, card.y + 14 * s, cw * 0.62f, 32 * s), _game.Species != null ? FishName(_game.Species.id) : "Рыба", _title, InkTitle);
            Label(new Rect(x, card.y + 46 * s, cw, 18 * s), _winSize, _smallLeft, InkSoft);
            float grow = Mathf.Clamp01(t / 1.2f);
            float shown = _game.Frame.WeightG * (1f - (1f - grow) * (1f - grow) * (1f - grow));
            Label(new Rect(x, card.y + 10 * s, cw, 40 * s), GramsText(shown), _weight, Coral);
            // Опыт за рыбу — под весом, появляется вместе с плашками.
            float xpIn = Mathf.Clamp01((t - 0.35f) / 0.3f);
            if (_catchXp != null && xpIn > 0f)
                Label(new Rect(x, card.y + 47 * s, cw, 18 * s), _catchXp, _depthCaption, WithAlpha(Azure, xpIn));

            // Плашки: разряд рыбы и личные достижения — выскакивают по очереди.
            float chipX = x;
            float chipY = card.y + 74 * s;
            int chip = 0;
            switch (grade)
            {
                case FishGrade.Trophy: Chip(ref chipX, chipY, "ТРОФЕЙ", Gold, Ink, t, chip++); break;
                case FishGrade.Qualifying: Chip(ref chipX, chipY, "ЗАЧЁТНАЯ", Leaf, Color.white, t, chip++); break;
                case FishGrade.Juvenile: Chip(ref chipX, chipY, "МАЛЁК", Slate, Color.white, t, chip++); break;
            }

            if (marks.SpeciesRecord)
                Chip(ref chipX, chipY, "РЕКОРД ВИДА", Coral, Color.white, t, chip++);
            if (marks.BiggestEver)
                Chip(ref chipX, chipY, "ЛИЧНЫЙ РЕКОРД", Plum, Color.white, t, chip++);
            if (marks.FirstOfSpecies)
                Chip(ref chipX, chipY, "ПЕРВАЯ РЫБА ВИДА", Azure, Color.white, t, chip++);

            // Итоги вываживания: три плитки.
            float tileGap = 8 * s;
            float tileWidth = (cw - 2 * tileGap) / 3f;
            float tileY = card.y + 108 * s;
            Tile(new Rect(x, tileY, tileWidth, 52 * s), _tileTime, "время");
            Tile(new Rect(x + tileWidth + tileGap, tileY, tileWidth, 52 * s), _tilePeak, "пик нагрузки");
            Tile(new Rect(x + 2 * (tileWidth + tileGap), tileY, tileWidth, 52 * s), _tileRests, "паузы взял");

            // Подсказка по главному событию — проявляется чуть позже.
            float hintIn = Mathf.Clamp01((t - 0.7f) / 0.3f);
            if (hintIn > 0f && !string.IsNullOrEmpty(_catchHint))
            {
                var hint = new Rect(x, card.y + 170 * s, cw, 32 * s);
                Round(hint, new Color(0.18f, 0.50f, 0.82f, 0.10f * hintIn), 10 * s);
                Disc(new Vector2(hint.x + 14 * s, hint.center.y), 3.5f * s, WithAlpha(Azure, hintIn));
                Label(new Rect(hint.x + 26 * s, hint.y, cw - 32 * s, hint.height), _catchHint, _smallLeft, WithAlpha(Ink, hintIn));
            }

            // Кнопки: что даст каждое решение, написано прямо на них.
            Round(release, new Color(0.08f, 0.20f, 0.25f, 0.08f), 18 * s);
            Ring(release, new Color(0.08f, 0.20f, 0.25f, 0.22f), 1.4f * s, 18 * s);
            Label(new Rect(release.x, release.y + 7 * s, release.width, 22 * s), "Отпустить", _center, Ink);
            Label(new Rect(release.x, release.y + 30 * s, release.width, 16 * s), _releaseSub, _tiny, Leaf);

            if (small)
            {
                Round(live, new Color(0.18f, 0.50f, 0.82f, 0.14f), 18 * s);
                Ring(live, new Color(0.18f, 0.50f, 0.82f, 0.55f), 1.4f * s, 18 * s);
                Label(new Rect(live.x, live.y + 7 * s, live.width, 22 * s), "На живца", _center, Ink);
                Label(new Rect(live.x, live.y + 30 * s, live.width, 16 * s), _liveSub, _tiny, _liveSubBad ? Coral : Azure);
            }

            if (fits)
            {
                Round(new Rect(keep.x, keep.y + 2 * s, keep.width, keep.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), 18 * s);
                Round(keep, Coral, 18 * s);
                Label(new Rect(keep.x, keep.y + 7 * s, keep.width, 22 * s), "В садок", _center, Color.white);
                float subWidth = TextWidth(_keepSub, _tiny);
                float subX = keep.center.x - subWidth / 2 + 8 * s;
                Coin(new Vector2(subX - 9 * s, keep.y + 38 * s), 5f * s);
                Label(new Rect(subX, keep.y + 30 * s, subWidth + 4 * s, 16 * s), _keepSub, _tiny, Color.white);
            }
            else
            {
                Round(keep, Gold, 18 * s);
                Label(new Rect(keep.x, keep.y + 7 * s, keep.width, 22 * s), "Садок полон", _center, Ink);
                Label(new Rect(keep.x, keep.y + 30 * s, keep.width, 16 * s), "открыть садок", _tiny, Ink);
            }

            if (grade == FishGrade.Trophy || marks.SpeciesRecord || marks.BiggestEver || _game.CatchXp.LeveledUp)
                Sparkles(new Vector2(card.xMax - 70 * s, card.y + 30 * s), t - 0.5f, s);
        }

        private void BuildCatchTexts(FightReport r)
        {
            _catchTextsFor = r;
            _tileTime = HudText.Seconds(r.ElapsedSeconds);
            _tilePeak = HudText.Percent(r.MaxLoadShare);
            _tileRests = r.RestsLifted + " из " + r.RestsTotal;

            var marks = _game.CatchMarks;
            var grade = _game.CatchGrade;
            var xp = _game.CatchXp;
            _catchXp = xp.Total > 0 ? "+" + MoneyNumber(xp.Total) + " опыта" : null;
            if (xp.LeveledUp)
                _catchHint = LevelUpText(xp);
            else if (marks.SpeciesRecord)
                _catchHint = "Новый рекорд вида! Прежний — " + Grams(marks.PreviousBestG) + " г";
            else if (marks.BiggestEver)
                _catchHint = "Самая крупная рыба за всё время";
            else if (marks.FirstOfSpecies)
                _catchHint = "Первая рыба этого вида — записана в профиль";
            else if (grade == FishGrade.Trophy)
                _catchHint = "Трофейный вес — такая попадается редко";
            else if (grade == FishGrade.Qualifying)
                _catchHint = "Зачётная: для этой рыбы — от " + Grams(_game.Species.weightG.qualifying) + " г";
            else if (grade == FishGrade.Juvenile)
                _catchHint = "Малёк — такую рыбу лучше отпустить";
            else if (_game.Species != null)
                _catchHint = "До зачётной не хватило " + Grams(Mathf.Max(1, _game.Species.weightG.qualifying - Mathf.RoundToInt(_game.Frame.WeightG))) + " г";
            else
                _catchHint = "";

            int releaseKarma = grade == FishGrade.Trophy ? 5 : grade == FishGrade.Juvenile ? 2 : 1;
            _releaseSub = "карма " + Signed(releaseKarma);
            _keepSub = "+" + MoneyNumber(_game.CatchPrice) + (grade == FishGrade.Juvenile ? " · карма −3" : "");
            _liveSubBad = grade == FishGrade.Juvenile;
            _liveSub = _liveSubBad ? "карма −3" : "+1 живец";
        }

        /// <summary>Плашка: выскакивает с небольшим перелётом через 0,45 с + 0,14 с на каждую следующую.</summary>
        private void Chip(ref float x, float y, string text, Color fill, Color ink, float t, int index)
        {
            float s = _scale;
            float w = TextWidth(text, _chip) + 20 * s;
            float h = 24 * s;
            var rect = new Rect(x, y, w, h);
            x += w + 6 * s;
            float local = Mathf.Clamp01((t - 0.45f - 0.14f * index) / 0.26f);
            if (local <= 0f)
                return;
            float scale = EaseOutBack(local);
            var matrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), rect.center);
            Round(rect, fill, h / 2);
            Label(rect, text, _chip, ink);
            GUI.matrix = matrix;
        }

        private void Tile(Rect rect, string value, string caption)
        {
            float s = _scale;
            Round(rect, new Color(0.08f, 0.20f, 0.25f, 0.06f), 12 * s);
            Label(new Rect(rect.x, rect.y + 6 * s, rect.width, 24 * s), value, _center, Ink);
            Label(new Rect(rect.x, rect.y + 30 * s, rect.width, 16 * s), caption, _tiny, InkSoft);
        }

        /// <summary>Искры от веса: десяток точек разлетается и гаснет за секунду.</summary>
        private void Sparkles(Vector2 origin, float t, float s)
        {
            if (t <= 0f || t >= 1.1f)
                return;
            float q = t / 1.1f;
            float reach = 1f - (1f - q) * (1f - q);
            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36f + 12f * (i % 3);
                var dir = Polar(angle);
                var at = origin + dir * ((26f + 9f * (i % 4)) * s * reach) + new Vector2(0f, 26f * s * q * q);
                var color = i % 3 == 0 ? Gold : i % 3 == 1 ? Coral : Azure;
                Disc(at, (3.2f - 2.2f * q) * s, WithAlpha(color, 1f - q));
            }
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            float k = x - 1f;
            return 1f + (c1 + 1f) * k * k * k + c1 * k * k;
        }

        // ---------- После решения: рыба в садок или круги на воде ----------

        private void StartCatchExit(int kind, Rect card)
        {
            _exitKind = kind;
            _exitTime = 0f;
            _exitFrom = new Vector2(card.center.x, card.y + 40 * _scale);
            _exitText = kind == 1 ? "+" + GramsText(_game.Frame.WeightG) : kind == 3 ? "+1 живец" : null;
            _exitSpecies = _game.Species != null ? _game.Species.id : null;
            if (_feedback == null)
                return;
            if (kind == 1)
                _feedback.PlayKeep();
            else if (kind == 3)
                _feedback.PlayTick(0.4f);
            else
                _feedback.PlayRelease();
        }

        private void TickCatchExit(float dt)
        {
            if (_exitKind == 0)
                return;
            _exitTime += dt;
            if (_exitTime >= CatchExitSeconds)
                _exitKind = 0;
        }

        private void CatchExit(Rect area)
        {
            if (!_drawing || _exitKind == 0)
                return;
            float s = _scale;
            if (_exitKind == 1 || _exitKind == 3)
            {
                // В садок — к кнопке садка; на живца — к кнопке наживки.
                var goal = _exitKind == 1 ? _keepnetButtonRect : _baitPillRect;
                var fallback = _exitKind == 1 ? new Vector2(area.center.x, area.yMax - 50 * s) : new Vector2(area.xMax - 43 * s, area.yMax - 357 * s);
                var target = goal.width > 0f ? goal.center : fallback;
                float p = Mathf.Clamp01(_exitTime / CatchFlySeconds);
                if (p < 1f)
                {
                    // Рыба летит дугой в садок и уменьшается.
                    float e = p * p * (3f - 2f * p);
                    var control = (_exitFrom + target) * 0.5f + new Vector2(0f, -130 * s);
                    var at = (1 - e) * (1 - e) * _exitFrom + 2 * (1 - e) * e * control + e * e * target;
                    float k = Mathf.Lerp(1.5f, 0.6f, e) * s;
                    var flying = FishPicture(_exitSpecies);
                    if (flying != null)
                    {
                        GUI.DrawTexture(new Rect(at.x - 44 * k, at.y - 22 * k, 88 * k, 44 * k), flying, ScaleMode.ScaleToFit, true);
                        return;
                    }

                    var body = new Rect(at.x - 16 * k, at.y - 6.5f * k, 28 * k, 13 * k);
                    Round(new Rect(body.x + 1.5f * k, body.y + 2.5f * k, body.width, body.height), new Color(0f, 0f, 0f, 0.25f), 6.5f * k);
                    Round(body, new Color(0.84f, 0.91f, 0.95f), 6.5f * k);
                    var tail = new Vector2(body.xMax, body.center.y);
                    Bar(tail, tail + new Vector2(8 * k, -6 * k), 3.2f * k, new Color(0.66f, 0.78f, 0.84f));
                    Bar(tail, tail + new Vector2(8 * k, 6 * k), 3.2f * k, new Color(0.66f, 0.78f, 0.84f));
                    Disc(new Vector2(body.x + 6 * k, body.center.y - 1.5f * k), 1.6f * k, new Color(0.1f, 0.15f, 0.2f));
                    return;
                }

                // Долетела: кнопка садка «пыхает» кольцом, вверх уходит «+вес».
                float q = Mathf.Clamp01((_exitTime - CatchFlySeconds) / (CatchExitSeconds - CatchFlySeconds));
                float radius = (goal.width > 0f ? goal.width / 2 : 40 * s) + 26 * s * q;
                Circle(target, radius, (3f - 2f * q) * s, WithAlpha(Amber, 1f - q));
                if (_exitText != null)
                {
                    // Подпись не уходит за правый край экрана (кнопка наживки стоит у самого края).
                    float tx = Mathf.Min(target.x - 80 * s, area.xMax - 160 * s);
                    Label(new Rect(tx, target.y - 64 * s - 34 * s * q, 160 * s, 24 * s), _exitText, _center, WithAlpha(Color.white, 1f - q * q));
                }
                return;
            }

            // Отпустил: круги расходятся по воде, пузыри поднимаются.
            var origin = new Vector2(Screen.width * 0.5f, Screen.height * 0.42f);
            for (int i = 0; i < 3; i++)
            {
                float q = Mathf.Clamp01(_exitTime / 0.95f - i * 0.16f);
                if (q <= 0f || q >= 1f)
                    continue;
                float w = (36f + 190f * q) * s;
                float h = w * 0.26f;
                Ring(new Rect(origin.x - w / 2, origin.y - h / 2, w, h), new Color(1f, 1f, 1f, 0.75f * (1f - q)), (2.4f - 1.4f * q) * s, h / 2);
            }

            for (int i = 0; i < 6; i++)
            {
                float q = Mathf.Clamp01(_exitTime / 0.8f - i * 0.07f);
                if (q <= 0f || q >= 1f)
                    continue;
                float x = origin.x + ((i * 37) % 60 - 30) * s + 5f * s * Mathf.Sin(q * 9f + i);
                float y = origin.y - 6 * s - 70f * s * q;
                Circle(new Vector2(x, y), (2.5f + (i % 3)) * s, 1.3f * s, new Color(1f, 1f, 1f, 0.8f * (1f - q)));
            }
        }
    }
}
