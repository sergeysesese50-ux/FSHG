using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Профиль игрока: плашка над кнопкой «Садок» (уровень, монеты и карма) открывает экран профиля — уровень
    // с рангом и полоской опыта (правила — LevelRules), карма со ступенью,
    // итоги ловли и таблица по видам. Изменение кармы всплывает подписью «Карма +1 · отпустил рыбу».
    // Правила кармы и статистики — в Fishing.Core (KarmaRules, PlayerStats); здесь только показ.
    public sealed partial class LakeHud
    {
        private const float KarmaToastSeconds = 3.5f;

        private bool _profileOpen;
        private Rect _profileChipRect;
        private int _karmaSeen = -1;
        private string _karmaToast;
        private Color _karmaToastColor;
        private float _karmaToastLeft;

        private int _karmaTextKey = int.MinValue;
        private string _karmaText;

        /// <summary>Открыт экран садка, профиля или турбаза: ввод рыбалки (заброс, кнопки) в это время не читается.</summary>
        private bool ModalOpen => _keepnetOpen || _profileOpen || _campOpen || _mapOpen || _baitOpen || _gearOpen || LevelUpPending;

        // ---------- Плашка и всплывающая подпись ----------

        /// <summary>Монеты и карма одной плашкой; нажатие открывает профиль. top — середина верхней кромки.</summary>
        private void ProfileChip(Vector2 top, PlayerProfile profile)
        {
            float s = _scale;
            float h = 28 * s;
            // Ширина считается только при рисовании (нужны стили); нажатие ловится по прямоугольнику прошлого кадра.
            if (Tapped(_profileChipRect))
            {
                _profileOpen = true;
                _swiping = false;
            }

            if (!_drawing)
                return;

            string money = MoneyText(profile.Money);
            string karma = KarmaText(profile.Karma);
            float moneyWidth = TextWidth(money, _word);
            float karmaWidth = TextWidth(karma, _word);
            // Слева — кружок с уровнем, дальше монеты и карма.
            float lead = 26 * s;
            float w = lead + 26 * s + moneyWidth + 30 * s + karmaWidth + 14 * s;
            var rect = new Rect(top.x - w / 2, top.y, w, h);
            _profileChipRect = rect;

            GlassPanel(rect, Glass, h / 2);
            Rim(rect, GlassBorder, 1.2f * s, h / 2);
            LevelBadge(new Vector2(rect.x + h / 2, rect.center.y), h / 2 - 3 * s, profile.Level);
            Coin(new Vector2(rect.x + lead + 14 * s, rect.center.y), 6.5f * s);
            Label(new Rect(rect.x + lead + 26 * s, rect.y, moneyWidth + 4 * s, h), money, _word, Color.white);
            float kx = rect.x + lead + 26 * s + moneyWidth + 12 * s;
            KarmaMark(new Vector2(kx + 6 * s, rect.center.y), 5.5f * s, profile.Karma);
            Label(new Rect(kx + 16 * s, rect.y, karmaWidth + 4 * s, h), karma, _word, KarmaColor(profile.Karma));
        }

        /// <summary>Кружок с номером уровня.</summary>
        private void LevelBadge(Vector2 center, float radius, int level)
        {
            if (!_drawing)
                return;
            Disc(center, radius, Amber);
            Label(new Rect(center.x - radius - 2 * _scale, center.y - radius, 2 * radius + 4 * _scale, 2 * radius), LevelText(level), _chip, Dark);
        }

        private int _levelTextKey = -1;
        private string _levelText;

        private string LevelText(int level)
        {
            if (level != _levelTextKey || _levelText == null)
            {
                _levelTextKey = level;
                _levelText = level.ToString();
            }
            return _levelText;
        }

        /// <summary>Подсказка карточки улова о новом уровне: уровень, новый ранг и открывшиеся места.</summary>
        private string LevelUpText(XpGain xp)
        {
            string text = "Новый уровень: " + xp.LevelAfter;
            if (LevelRules.RankFor(xp.LevelAfter) != LevelRules.RankFor(xp.LevelBefore))
                text += " · ранг «" + RankName(LevelRules.RankFor(xp.LevelAfter)) + "»";
            var spots = _game.Spots;
            if (spots != null)
            {
                foreach (var spot in spots)
                {
                    // Места на воде уровень сам не открывает: к ним ещё нужна лодка.
                    if (spot.minLevel > xp.LevelBefore && spot.minLevel <= xp.LevelAfter && _game.SpotOpen(spot))
                        text += " · открыто место «" + SpotName(spot.id) + "»";
                }
            }

            return text;
        }

        private static string RankName(PlayerRank rank)
        {
            switch (rank)
            {
                case PlayerRank.Amateur: return "Любитель";
                case PlayerRank.Angler: return "Рыболов";
                case PlayerRank.Experienced: return "Опытный рыболов";
                case PlayerRank.Master: return "Мастер";
                default: return "Новичок";
            }
        }

        /// <summary>Блок уровня на экране профиля: номер, ранг, опыт и полоска до следующего уровня.</summary>
        private void LevelBlock(Rect block, PlayerProfile profile)
        {
            float s = _scale;
            float cw = block.width;
            LevelRules.Progress(profile.Xp, out int level, out long into, out long span);
            Round(block, new Color(1f, 1f, 1f, 0.06f), 14 * s);
            Label(new Rect(block.x + 4 * s, block.y + 14 * s, 88 * s, 34 * s), level.ToString(), _gaugeBig, Amber);
            Label(new Rect(block.x + 4 * s, block.y + 46 * s, 88 * s, 14 * s), "уровень", _tiny, Soft);
            Label(new Rect(block.x + 96 * s, block.y + 8 * s, cw - 104 * s, 20 * s), RankName(profile.Rank), _word, Amber);
            string line = span > 0
                ? "Опыт: " + MoneyNumber(into) + " из " + MoneyNumber(span) + " · до уровня " + (level + 1) + " — " + MoneyNumber(span - into)
                : "Опыт: " + MoneyNumber(profile.Xp) + " · высший уровень";
            Label(new Rect(block.x + 96 * s, block.y + 28 * s, cw - 104 * s, 16 * s), line, _smallLeft, Color.white);
            // Пояснение занимает две строки — под него отдельное место, полоска ниже.
            // Что дадут за следующий уровень (на последнем — пояснение, откуда берётся опыт).
            string next = NextRewardText(level);
            Label(new Rect(block.x + 96 * s, block.y + 46 * s, cw - 104 * s, 32 * s),
                next.Length > 0 ? next : "Опыт дают за пойманную рыбу: зачётная — втрое, трофей — вшестеро.", _smallLeft, next.Length > 0 ? Amber : Soft);
            var track = new Rect(block.x + 12 * s, block.y + 90 * s, cw - 24 * s, 6 * s);
            Round(track, new Color(1f, 1f, 1f, 0.16f), 3 * s);
            float share = span > 0 ? Mathf.Clamp01(into / (float)span) : 1f;
            if (share > 0f)
                Round(new Rect(track.x, track.y, Mathf.Max(track.height, track.width * share), track.height), Amber, 3 * s);
        }

        /// <summary>Значок кармы: ромб цвета ступени.</summary>
        private void KarmaMark(Vector2 center, float radius, int karma)
        {
            if (!_drawing)
                return;
            var color = KarmaColor(karma);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center);
            GUI.DrawTexture(new Rect(center.x - radius * 0.75f, center.y - radius * 0.75f, radius * 1.5f, radius * 1.5f),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius * 0.25f);
            GUI.matrix = matrix;
        }

        /// <summary>Следит за изменениями кармы в профиле и готовит всплывающую подпись (вызывается раз в кадр).</summary>
        private void TrackKarma(float dt)
        {
            // Подпись ждёт, пока её негде показать (карточка улова или схода, открытый экран).
            var state = _game.State;
            bool visible = (state == LakeState.Idle || state == LakeState.Casting || state == LakeState.Waiting) && !ModalOpen;
            if (_karmaToastLeft > 0f && visible)
                _karmaToastLeft -= dt;
            var profile = _game.Profile;
            if (profile == null)
                return;
            if (_karmaSeen < 0)
            {
                _karmaSeen = profile.KarmaChangeCount;
                return;
            }

            if (profile.KarmaChangeCount == _karmaSeen)
                return;
            _karmaSeen = profile.KarmaChangeCount;
            var change = profile.LastKarmaChange;
            _karmaToast = "Карма " + Signed(change.Delta) + " · " + KarmaReasonText(change.Reason);
            _karmaToastColor = change.Delta > 0 ? Green : Red;
            _karmaToastLeft = KarmaToastSeconds;
        }

        /// <summary>Подпись об изменении кармы: плашка над плашкой профиля, всплывает и гаснет.</summary>
        private void KarmaToast(Vector2 top)
        {
            if (!_drawing || _karmaToastLeft <= 0f || _karmaToast == null)
                return;
            float s = _scale;
            float age = KarmaToastSeconds - _karmaToastLeft;
            float alpha = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01(_karmaToastLeft / 0.6f);
            float rise = (1f - Mathf.Clamp01(age / 0.35f)) * 10f * s;
            float h = 28 * s;
            float w = TextWidth(_karmaToast, _word) + 30 * s;
            var rect = new Rect(top.x - w / 2, top.y + rise, w, h);
            GlassPanel(rect, WithAlpha(Glass, alpha), h / 2, alpha);
            Ring(rect, WithAlpha(_karmaToastColor, 0.8f * alpha), 1.4f * s, h / 2);
            Label(new Rect(rect.x + 15 * s, rect.y, w - 15 * s, h), _karmaToast, _word, WithAlpha(_karmaToastColor, alpha));
        }

        // ---------- Экран профиля ----------

        private void ProfilePanel(Rect area)
        {
            var profile = _game.Profile;
            if (profile == null)
            {
                _profileOpen = false;
                return;
            }

            float s = _scale;
            float w = Mathf.Min(area.width, 380 * s);
            float top = area.y + 48 * s;
            float h = Mathf.Min(area.yMax - top - 8 * s, 712 * s);
            var card = new Rect(area.center.x - w / 2, top, w, h);
            float x = card.x + 18 * s;
            float cw = w - 36 * s;

            var close = new Rect(card.xMax - 50 * s, card.y + 12 * s, 38 * s, 38 * s);
            if (Tapped(close))
            {
                _profileOpen = false;
                return;
            }

            if (!_drawing)
                return;

            GlassPanel(card, new Color(0.07f, 0.13f, 0.15f, 0.95f), 22 * s);
            Rim(card, GlassBorder, 1.2f * s, 22 * s);
            Label(new Rect(x, card.y + 12 * s, cw - 120 * s, 36 * s), "Профиль", _title, Color.white);
            MoneyPillRight(new Vector2(close.x - 8 * s, card.y + 18 * s), profile.Money);
            GlassPanel(close, Glass, close.width / 2);
            Rim(close, GlassBorder, 1.2f * s, close.width / 2);
            var c = close.center;
            float k = 6.5f * s;
            Bar(c + new Vector2(-k, -k), c + new Vector2(k, k), 2.4f * s, Color.white);
            Bar(c + new Vector2(-k, k), c + new Vector2(k, -k), 2.4f * s, Color.white);

            // Уровень и опыт.
            float y = card.y + 60 * s;
            LevelBlock(new Rect(x, y, cw, 108 * s), profile);
            y += 118 * s;

            // Карма: число, ступень, шкала от −100 до +100.
            var block = new Rect(x, y, cw, 96 * s);
            Round(block, new Color(1f, 1f, 1f, 0.06f), 14 * s);
            var karmaColor = KarmaColor(profile.Karma);
            Label(new Rect(block.x + 4 * s, block.y + 8 * s, 88 * s, 34 * s), Signed(profile.Karma), _gaugeBig, karmaColor);
            Label(new Rect(block.x + 4 * s, block.y + 40 * s, 88 * s, 14 * s), "карма", _tiny, Soft);
            Label(new Rect(block.x + 96 * s, block.y + 8 * s, cw - 104 * s, 20 * s), KarmaLevelName(KarmaRules.Level(profile.Karma)), _word, karmaColor);
            Label(new Rect(block.x + 96 * s, block.y + 28 * s, cw - 104 * s, 34 * s),
                "Растёт, когда отпускаешь рыбу. Падает за мальков в садке и обрывы лески.", _smallLeft, Soft);
            var track = new Rect(block.x + 12 * s, block.y + 76 * s, cw - 24 * s, 6 * s);
            Round(track, new Color(1f, 1f, 1f, 0.16f), 3 * s);
            Round(new Rect(track.center.x - s, track.y - 3 * s, 2 * s, 12 * s), new Color(1f, 1f, 1f, 0.4f), s);
            float share = Mathf.InverseLerp(KarmaRules.Min, KarmaRules.Max, profile.Karma);
            var marker = new Vector2(Mathf.Lerp(track.x, track.xMax, share), track.center.y);
            Disc(marker, 7 * s, new Color(0.07f, 0.13f, 0.15f));
            Disc(marker, 5 * s, karmaColor);
            y += 108 * s;

            // Итоги.
            var stats = profile.Stats;
            int caught = stats.FishCaught;
            float line = 21 * s;
            Stat(x, y, cw, "Поймано", caught + " " + Plural(caught, "рыба", "рыбы", "рыб") + " · " + Kg((int)Mathf.Min(stats.TotalWeightG, int.MaxValue)) + " кг");
            y += line;
            var biggest = stats.Biggest;
            Stat(x, y, cw, "Самая крупная", biggest != null ? FishName(biggest.speciesId) + " · " + Grams(biggest.bestWeightG) + " г" : "—");
            y += line;
            Stat(x, y, cw, "Трофеев", stats.Trophies + " · отпущено " + stats.Released + " · продано " + stats.Sold);
            y += line;
            Stat(x, y, cw, "Сходов", stats.Losses.Total + " · из них обрывов лески " + stats.Losses.lineBroke);
            y += line;
            Stat(x, y, cw, "Заработано", MoneyNumber(stats.MoneyEarned) + " " + Plural((int)(stats.MoneyEarned % 100), "монета", "монеты", "монет"));
            y += line + 10 * s;

            // По видам: вид, штук, всего, средний вес, рекорд.
            float c1 = x + 8 * s;
            float c2 = x + cw * 0.36f;
            float c3 = x + cw * 0.50f;
            float c4 = x + cw * 0.74f;
            Label(new Rect(c1, y, 120 * s, 16 * s), "Вид", _smallLeft, Soft);
            Label(new Rect(c2, y, 50 * s, 16 * s), "шт.", _smallLeft, Soft);
            Label(new Rect(c3, y, 90 * s, 16 * s), "всего", _smallLeft, Soft);
            Label(new Rect(c4, y, 90 * s, 16 * s), "рекорд", _smallLeft, Soft);
            y += 20 * s;
            if (stats.Species.Count == 0)
            {
                Label(new Rect(x, y + 8 * s, cw, 40 * s), "Пока ничего не поймано.", _coachStyle, Soft);
                return;
            }

            float rowHeight = 28 * s;
            foreach (var species in stats.Species)
            {
                if (y + rowHeight > card.yMax - 12 * s)
                    break;
                Round(new Rect(x, y, cw, rowHeight - 4 * s), new Color(1f, 1f, 1f, 0.05f), 9 * s);
                float ry = y;
                float rh = rowHeight - 4 * s;
                Label(new Rect(c1, ry, c2 - c1, rh), FishName(species.speciesId), _word, Color.white);
                Label(new Rect(c2, ry, 50 * s, rh), species.caught.ToString(), _smallLeft, Color.white);
                Label(new Rect(c3, ry, 90 * s, rh), Kg((int)Mathf.Min(species.totalWeightG, int.MaxValue)) + " кг", _smallLeft, Color.white);
                Label(new Rect(c4, ry, 90 * s, rh), Grams(species.bestWeightG) + " г", _smallLeft, species.trophies > 0 ? Amber : Color.white);
                y += rowHeight;
            }
        }

        private void Stat(float x, float y, float width, string name, string value)
        {
            float s = _scale;
            Label(new Rect(x + 8 * s, y, 112 * s, 20 * s), name, _smallLeft, Soft);
            Label(new Rect(x + 120 * s, y, width - 120 * s, 20 * s), value, _smallLeft, Color.white);
        }

        // ---------- Строки и цвета ----------

        private string KarmaText(int karma)
        {
            if (karma != _karmaTextKey || _karmaText == null)
            {
                _karmaTextKey = karma;
                _karmaText = Signed(karma);
            }
            return _karmaText;
        }

        /// <summary>Число со знаком и типографским минусом: +5, 0, −3.</summary>
        private static string Signed(int value) => value > 0 ? "+" + value : value < 0 ? "−" + (-value) : "0";

        private static Color KarmaColor(int karma) => karma >= 10 ? Green : karma <= -10 ? Red : Color.white;

        private static string KarmaLevelName(KarmaLevel level)
        {
            switch (level)
            {
                case KarmaLevel.Poacher: return "Браконьер";
                case KarmaLevel.Grabber: return "Хапуга";
                case KarmaLevel.Honest: return "Честный рыбак";
                case KarmaLevel.Keeper: return "Хранитель озера";
                default: return "Рыбак";
            }
        }

        private static string KarmaReasonText(KarmaReason reason)
        {
            switch (reason)
            {
                case KarmaReason.ReleasedFish: return "отпустил рыбу";
                case KarmaReason.ReleasedJuvenile: return "отпустил малька";
                case KarmaReason.ReleasedTrophy: return "отпустил трофей";
                case KarmaReason.ReleasedFromKeepnet: return "отпустил рыбу из садка";
                case KarmaReason.KeptJuvenile: return "забрал малька";
                case KarmaReason.LineBroke: return "оборвал леску";
                default: return "";
            }
        }
    }
}
