using System.Collections.Generic;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Оповещение о новом уровне: карточка по центру экрана, когда удочка на берегу (после карточки улова).
    // На ней — уровень и ранг, что получено в награду (монеты, наживка, приманки, снасти, время на лодке)
    // и что открылось (места, вещи в магазине). Награды к этому моменту уже выданы — карточка только сообщает.
    // Правила выдачи — PlayerProfile.ClaimLevelRewards, таблица наград — shared/data/levels.json.
    public sealed partial class LakeHud
    {
        private const int RowCoins = 0, RowBait = 1, RowLure = 2, RowTackle = 3, RowBoat = 4;

        private struct LevelRow
        {
            public int Kind;
            public string Id;
            public string Text;
        }

        private RewardReceipt _levelShown;
        private float _levelTime;
        private readonly List<LevelRow> _levelRows = new List<LevelRow>();
        private readonly List<string> _levelOpened = new List<string>();
        private string _levelTitle;
        private string _levelRank;
        private bool _levelNewRank;

        private int _nextRewardKey = -1;
        private string _nextRewardText;

        /// <summary>Есть непоказанное оповещение о новом уровне, и его можно показать: удочка на берегу, улов уже убран.</summary>
        private bool LevelUpPending => _game.LevelUp != null && _game.State == LakeState.Idle && _exitKind == 0;

        private void LevelUpPanel(Rect area)
        {
            var notice = _game.LevelUp;
            if (notice == null)
                return;
            if (!ReferenceEquals(notice, _levelShown) || _levelTitle == null)
            {
                BuildLevelUp(notice);
                _levelShown = notice;
                _levelTime = 0f;
                if (_feedback != null)
                    _feedback.PlaySell(4);
            }

            float s = _scale;
            float w = Mathf.Min(area.width, 360 * s);
            float rowH = 38 * s;
            float openedH = _levelOpened.Count > 0 ? 30 * s + _levelOpened.Count * 20 * s : 0f;
            float h = 150 * s + _levelRows.Count * rowH + openedH + 78 * s;
            h = Mathf.Min(h, area.height - 60 * s);
            float pop = 1f - EaseOutBack(Mathf.Clamp01(_levelTime / 0.45f));
            var card = new Rect(area.center.x - w / 2, area.center.y - h / 2 + 20 * s + pop * 50 * s, w, h);
            float x = card.x + 20 * s;
            float cw = w - 40 * s;
            var button = new Rect(x, card.yMax - 64 * s, cw, 48 * s);

            // Закрывается кнопкой — и не раньше, чем карточка успела появиться (чтобы не пролистать случайным касанием).
            if (_levelTime > 0.5f && Tapped(button))
            {
                _game.DismissLevelUp();
                _levelShown = null;
                _levelTitle = null;
                return;
            }

            if (!_drawing)
                return;

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.03f, 0.08f, 0.12f, 0.45f * Mathf.Clamp01(_levelTime / 0.3f)), 0f, 0f);
            LightPill(card, 26 * s);

            // Кружок с уровнем — наполовину над карточкой, слегка пульсирует.
            var badge = new Vector2(card.center.x, card.y + 6 * s);
            float pulse = 1f + 0.05f * Mathf.Sin(_levelTime * 4f);
            Disc(badge, 46 * s * pulse, new Color(1f, 0.71f, 0.28f, 0.30f));
            Disc(badge, 38 * s, Color.white);
            Disc(badge, 34 * s, Amber);
            Label(new Rect(badge.x - 40 * s, badge.y - 22 * s, 80 * s, 44 * s), LevelText(notice.ToLevel), _big, Dark);
            Sparkles(new Vector2(card.center.x, card.y + 10 * s), _levelTime - 0.15f, s);

            Label(new Rect(x, card.y + 52 * s, cw, 30 * s), _levelTitle, _gaugeBig, InkTitle);
            Label(new Rect(x, card.y + 82 * s, cw, 20 * s), _levelRank, _coachStyle, _levelNewRank ? Coral : InkSoft);

            float y = card.y + 116 * s;
            Label(new Rect(x, y, cw, 18 * s), "Награда", _smallLeft, InkSoft);
            y += 24 * s;
            for (int i = 0; i < _levelRows.Count; i++)
            {
                if (y + rowH > button.y - openedH - 4 * s)
                    break;
                var item = _levelRows[i];
                float show = Mathf.Clamp01((_levelTime - 0.35f - 0.10f * i) / 0.3f);
                show = 1f - (1f - show) * (1f - show) * (1f - show);
                GUI.color = new Color(1f, 1f, 1f, show);
                var row = new Rect(x + (1f - show) * 16 * s, y, cw, rowH - 6 * s);
                Round(row, new Color(0.18f, 0.50f, 0.82f, 0.08f), 12 * s);
                var icon = new Vector2(row.x + 22 * s, row.center.y);
                switch (item.Kind)
                {
                    case RowCoins:
                        Coin(icon, 9 * s);
                        break;
                    case RowBait:
                    case RowLure:
                        BaitDot(icon, 14 * s, item.Id, true);
                        break;
                    case RowTackle:
                        TackleIcon(icon, 14 * s, TackleKindOf(item.Id), false);
                        break;
                    default:
                        TackleIcon(icon, 14 * s, 0, false);
                        break;
                }

                Label(new Rect(row.x + 44 * s, row.y, cw - 52 * s, row.height), item.Text, _word, Ink);
                GUI.color = Color.white;
                y += rowH;
            }

            if (_levelOpened.Count > 0)
            {
                y = button.y - openedH + 2 * s;
                Label(new Rect(x, y, cw, 18 * s), "Открылось", _smallLeft, InkSoft);
                y += 22 * s;
                foreach (string line in _levelOpened)
                {
                    Disc(new Vector2(x + 6 * s, y + 9 * s), 3 * s, Leaf);
                    Label(new Rect(x + 16 * s, y, cw - 16 * s, 18 * s), line, SmallLine(), Ink);
                    y += 20 * s;
                }
            }

            Round(new Rect(button.x, button.y + 2 * s, button.width, button.height), new Color(0.55f, 0.16f, 0.06f, 0.35f), 20 * s);
            Round(button, Coral, 20 * s);
            Label(button, "Отлично", _center, Color.white);
        }

        /// <summary>Собирает строки оповещения: заголовок, ранг, награды и что открылось на новых уровнях.</summary>
        private void BuildLevelUp(RewardReceipt notice)
        {
            _levelRows.Clear();
            _levelOpened.Clear();
            int gained = notice.ToLevel - notice.FromLevel;
            _levelTitle = gained > 1 ? "Сразу +" + gained + " " + Plural(gained, "уровень", "уровня", "уровней") + "!" : "Новый уровень!";
            var rank = LevelRules.RankFor(notice.ToLevel);
            _levelNewRank = rank != LevelRules.RankFor(notice.FromLevel);
            _levelRank = _levelNewRank ? "Новый ранг — " + RankName(rank) : "Ранг: " + RankName(rank);

            long coins = notice.Coins + notice.CompensationCoins;
            if (coins > 0)
            {
                string text = "+" + MoneyNumber(coins) + " " + Plural((int)(coins % 100), "монета", "монеты", "монет");
                if (notice.CompensationCoins > 0)
                    text += " (часть — взамен вещей, что уже есть)";
                _levelRows.Add(new LevelRow { Kind = RowCoins, Text = text });
            }

            foreach (var bait in notice.Baits)
                _levelRows.Add(new LevelRow { Kind = RowBait, Id = bait.id, Text = Capital(BaitName(bait.id)) + " ×" + bait.count });
            foreach (string id in notice.Lures)
                _levelRows.Add(new LevelRow { Kind = RowLure, Id = id, Text = "Приманка: " + BaitName(id) });
            foreach (string id in notice.Tackle)
                _levelRows.Add(new LevelRow { Kind = RowTackle, Id = id, Text = GearName(TackleKindOf(id), id) });
            if (notice.BoatMinutes > 0)
                _levelRows.Add(new LevelRow { Kind = RowBoat, Text = "Лодка напрокат: " + notice.BoatMinutes + " мин" });

            // Что открылось на новых уровнях: места и вещи в магазине.
            var spots = _game.Spots;
            if (spots != null)
            {
                foreach (var spot in spots)
                {
                    if (spot.minLevel > notice.FromLevel && spot.minLevel <= notice.ToLevel)
                        _levelOpened.Add("Место «" + SpotName(spot.id) + "»" + (spot.needsBoat ? " — туда на лодке" : ""));
                }
            }

            var data = _game.TackleData;
            if (data == null)
                return;
            string shop = "";
            int more = 0;
            void Offer(string name, int minLevel)
            {
                int level = TackleRules.LevelFor(minLevel);
                if (level <= notice.FromLevel || level > notice.ToLevel)
                    return;
                if (shop.Length < 34)
                    shop += (shop.Length > 0 ? ", " : "") + name;
                else
                    more++;
            }

            foreach (var rod in data.Rods)
                Offer(RodName(rod.id), rod.minLevel);
            foreach (var reel in data.Reels)
                Offer(ReelName(reel.id), reel.minLevel);
            foreach (var line in data.Lines)
                Offer(LineName(line.id), line.minLevel);
            foreach (var bait in data.Baits)
                Offer(BaitName(bait.id), bait.minLevel);
            if (shop.Length > 0)
                _levelOpened.Add("В магазине: " + shop + (more > 0 ? " и ещё " + more : ""));
        }

        /// <summary>Что за вещь по её id: 0 — удилище, 1 — катушка, 2 — леска.</summary>
        private int TackleKindOf(string id)
        {
            var data = _game.TackleData;
            if (data != null && data.TryGetReel(id, out _))
                return 1;
            return data != null && data.TryGetLine(id, out _) ? 2 : 0;
        }

        /// <summary>Награда за следующий уровень одной строкой — для профиля: «40 монет · пиявка ×10».</summary>
        private string NextRewardText(int level)
        {
            if (level == _nextRewardKey && _nextRewardText != null)
                return _nextRewardText;
            _nextRewardKey = level;
            var reward = _game.RewardFor(level + 1);
            if (reward == null)
            {
                _nextRewardText = "";
                return _nextRewardText;
            }

            string text = MoneyNumber(reward.coins) + " " + Plural(reward.coins % 100, "монета", "монеты", "монет");
            if (reward.baits != null)
            {
                foreach (var bait in reward.baits)
                    text += " · " + BaitName(bait.id) + " ×" + bait.count;
            }

            if (reward.lures != null)
            {
                foreach (string id in reward.lures)
                    text += " · " + BaitName(id);
            }

            if (reward.tackle != null)
            {
                foreach (string id in reward.tackle)
                    text += " · " + GearName(TackleKindOf(id), id).ToLowerInvariant();
            }

            if (reward.boatMinutes > 0)
                text += " · лодка на " + reward.boatMinutes + " мин";
            _nextRewardText = "Награда за уровень " + (level + 1) + ": " + text;
            return _nextRewardText;
        }
    }
}
