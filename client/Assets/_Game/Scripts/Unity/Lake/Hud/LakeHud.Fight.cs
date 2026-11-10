using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Экран вываживания (docs/specs/fight-v2.md, §5; поведение кнопок — §1–§2).
    //
    // Раскладка (единицы × _scale; телефон стоя):
    // - сверху: часы и глубина; под ними дорожка — подсачек у берега слева, метка рыбы по FishDistance / старт,
    //   цвет метки — силы рыбы; кольцо крючка; у новичка над меткой заполняется слово телеграфа;
    // - под дорожкой — одно постоянное место строки тренера; ниже, в центре, «Чётко!» / «Поздно» после подсечки;
    // - внизу: «Подтянуть» слева, «Подмотка» справа; над «Подмоткой» — фрикцион −/+, над ним счётчик лески;
    //   над ними по центру — дуга натяжения, под её центром у первой рыбы правило «Тянет — не крути…»;
    //   между кнопками — «Подсачек», когда рыба в зоне подсачека и тяжелее предела маха.
    // На широком экране дуга встаёт между кнопками, а «Подсачек» — над «Подтянуть».
    //
    // Данные — только FishingController.Frame (плавные поля для непрерывного, дискретные для состояний, события
    // кадра для вспышек), фрикцион игрока — FishingController.DragG, снасть — FishingController.Tackle.
    // Сглаживание и таймеры — в LateUpdate (TickFight), после того как контроллер собрал Frame этого кадра.
    public sealed partial class LakeHud
    {
        private const float TightSlackCm = 30f;
        private const float NeedleSmoothSeconds = 0.12f;
        private const float GhostSeconds = 0.6f;
        private const float HookFlashSeconds = 0.45f;
        private const float TelegraphHoldSeconds = 0.6f;
        private const float RateSmoothSeconds = 0.3f;
        private const float HoldStillCmPerSec = 3f;
        private const float LabourReelLoad = 0.65f;
        private const float LiftCueMaxAngle = 0.9f;
        private const float HandleDegreesAtFullSpeed = 720f;
        private const float IdleCrankDegreesPerSec = 480f;
        private const float TrackOverflow = 1.15f;
        private const float FreshStamina = 0.6f;
        private const float SpentStamina = 0.3f;
        private const int ArcSegments = 24;
        private const int LiftArcSegments = 12;

        private const string FirstFishRuleText = "Тянет — не крути. Встала — подтягивай.";

        private FightReport _fightReport;
        private LakeState _hudState;

        private float _startDistanceCm = 1f;
        private float _needle;
        private float _needleVelocity;
        private float _ghost;
        private float _ghostLeft;
        private float _hookFlash;
        private float _handleAngle;
        private float _spoolAngle;
        private float _liftPress;
        private float _reelPress;
        private float _telegraphFill;
        private float _telegraphLeft;
        private FishPhase _telegraphPhase;

        private bool _counterValid;
        private bool _counterFight;
        private float _lastLineCm;
        private float _lineRate;

        // Раскладка вываживания: считается в обоих проходах Ui().
        private Rect _trackRect;
        private Rect _coachRect;
        private Rect _counterRect;
        private Rect _dragRowRect;
        private Rect _netRect;
        private Rect _ruleRect;
        private Vector2 _gaugeCenter;
        private float _gaugeRadius;

        // Числа: ключ — значение в сотых (кг) или десятых (м); строка собирается один раз.
        private readonly TextCache _peakText = new TextCache(3000, 2, "", " кг");
        private readonly TextCache _limitText = new TextCache(3000, 2, "из ", " кг");
        private readonly TextCache _dragValueText = new TextCache(1000, 2, "", " кг");
        private readonly TextCache _dragNotchText = new TextCache(1000, 2, "фр ", "");
        private readonly TextCache _lineText = new TextCache(4000, 1, "", " м");
        private readonly TextCache _fishText = new TextCache(4000, 1, "до рыбы ", " м");
        private readonly TextCache _rateDownText = new TextCache(300, 1, "↓ ", " м/с");
        private readonly TextCache _rateUpText = new TextCache(300, 1, "↑ ", " м/с");
        private readonly TextCache _twistText = new TextCache(101, 0, "леска · перекрут " + HudText.Minus, "%");

        // ---------- Состояние (LateUpdate) ----------

        /// <summary>Начало и конец вываживания по смене State и Report.</summary>
        private void TrackFightState()
        {
            var state = _game.State;
            var report = _game.Report;
            if (state == LakeState.Fight && report != null && !ReferenceEquals(report, _fightReport))
            {
                _fightReport = report;
                BeginFightHud();
            }

            if (state == _hudState)
                return;
            if ((state == LakeState.Caught || state == LakeState.Lost) && report != null)
                EndFightHud(report);
            _hudState = state;
        }

        private void BeginFightHud()
        {
            var f = _game.Frame;
            _startDistanceCm = Mathf.Max(100f, f.FishDistanceCm);
            _needle = f.LoadShare;
            _needleVelocity = 0f;
            _ghost = 0f;
            _ghostLeft = 0f;
            _hookFlash = 0f;
            _telegraphFill = 0f;
            _telegraphLeft = 0f;
            _cardReport = null;
            _coach.BeginFight();
        }

        private void EndFightHud(FightReport report)
        {
            _coach.EndFight(report.Outcome, report.Cause);
            if (report.Outcome == FightOutcome.Landed && !_firstFishCaught)
            {
                _firstFishCaught = true;
                PlayerPrefs.SetInt(PrefFirstFish, 1);
            }
            PlayerPrefs.Save();
            BuildCardTexts(report);
        }

        /// <summary>Сглаживание и разовые эффекты вываживания. События — только из покадровых полей Frame.</summary>
        private void TickFight(float dt)
        {
            _hookFlash = Mathf.Max(0f, _hookFlash - dt);
            _ghostLeft = Mathf.Max(0f, _ghostLeft - dt);
            if (_game.State == LakeState.Waiting && ReelHeld)
            {
                // Подмотка поплавка до поклёвки: ручка и шпуля просто крутятся.
                _handleAngle = Mathf.Repeat(_handleAngle - IdleCrankDegreesPerSec * dt, 360f);
                _spoolAngle = _handleAngle;
            }
            if (_game.State != LakeState.Fight || dt <= 0f)
                return;

            var f = _game.Frame;

            // Стрелка: доля от самого слабого звена, критически задемпфирована (~0,12 с).
            _needle = Mathf.SmoothDamp(_needle, f.LoadShare, ref _needleVelocity, NeedleSmoothSeconds, Mathf.Infinity, dt);

            // Удар оставляет «призрак» на дуге: пик берётся из шагов кадра, интерполяция его срезала бы.
            if ((f.Events & (FightEvents.Shock | FightEvents.Breakaway)) != 0 && f.WeakestLinkG > 0f)
            {
                _ghost = f.PeakTensionMaxThisFrameG / f.WeakestLinkG;
                _ghostLeft = GhostSeconds;
            }

            if ((f.Events & FightEvents.HookLoss) != 0)
                _hookFlash = HookFlashSeconds;

            // Катушка: ручка крутится со скоростью подмотки (вхолостую — крутится, а леска не идёт),
            // шпуля — вместе с ней, а при пробуксовке фрикциона — назад со скоростью схода лески.
            float c0 = Mathf.Max(1f, (float)_game.Tackle.ReelFightSpeedCmPerSec);
            float handle = 0f;
            if (ReelHeld)
                handle = f.IsCrankingIntoSlip ? IdleCrankDegreesPerSec : HandleDegreesAtFullSpeed * f.ReelInCmPerSec / c0;
            _handleAngle = Mathf.Repeat(_handleAngle - handle * dt, 360f);
            float spool = f.IsDragSlipping ? -HandleDegreesAtFullSpeed * f.SlipCmPerSec / c0 : handle;
            _spoolAngle = Mathf.Repeat(_spoolAngle - spool * dt, 360f);

            // Телеграф: слово заполняется по TelegraphProgress (ступенька на шаг — сглаживаем), после начала фазы
            // держится заполненным и гаснет.
            if (f.TelegraphProgress > 0f)
            {
                if (_telegraphLeft <= 0f || _telegraphPhase != f.NextPhase)
                    _telegraphFill = 0f;
                _telegraphPhase = f.NextPhase;
                _telegraphLeft = TelegraphHoldSeconds;
                _telegraphFill = Mathf.MoveTowards(_telegraphFill, f.TelegraphProgress, dt * 5f);
            }
            else if (_telegraphLeft > 0f)
            {
                _telegraphLeft -= dt;
                _telegraphFill = Mathf.MoveTowards(_telegraphFill, 1f, dt * 5f);
            }

            _coach.Update(f, ReelHeld, LiftHeld && !LiftHeldBack, NetPressed, DragDelta, dt);
        }

        /// <summary>Скорость счётчика лески: изменение LineOut, сглаженное за 0,3 с. До вываживания — поплавок.</summary>
        private void TickLineCounter(float dt)
        {
            var state = _game.State;
            bool fight = state == LakeState.Fight;
            if (!fight && state != LakeState.Waiting && state != LakeState.Bite)
            {
                _counterValid = false;
                return;
            }

            float line = fight ? _game.Frame.LineOutCm : _game.FloatDistanceM * 100f;
            if (!_counterValid || fight != _counterFight || dt <= 0f)
            {
                _counterValid = true;
                _counterFight = fight;
                _lastLineCm = line;
                _lineRate = 0f;
                return;
            }

            float raw = (line - _lastLineCm) / dt;
            _lastLineCm = line;
            _lineRate += (raw - _lineRate) * (1f - Mathf.Exp(-dt / RateSmoothSeconds));
        }

        // ---------- Верх: дорожка, тренер, подсечка ----------

        private void FightTop(Rect area)
        {
            float s = _scale;
            float w = Mathf.Min(area.width - 24 * s, 330 * s);
            _trackRect = new Rect(area.center.x - w / 2, area.y + 62 * s, w, 30 * s);
            float cw = Mathf.Min(area.width, 380 * s);
            _coachRect = new Rect(area.center.x - cw / 2, _trackRect.yMax + 18 * s, cw, 40 * s);

            FishTrack();
            CoachLine();
            StrikeBanner(area);
        }

        private void FishTrack()
        {
            if (!_drawing)
                return;
            float s = _scale;
            var f = _game.Frame;
            var bar = _trackRect;
            GlassPanel(bar, Glass, bar.height / 2);
            Rim(bar, GlassBorder, 1.2f * s, bar.height / 2);

            // Подсачек у берега: обруч и ручка.
            var net = new Vector2(bar.x + 17 * s, bar.center.y - 2 * s);
            Circle(net, 7 * s, 2 * s, Soft);
            Bar(net + Polar(-60f) * 7 * s, net + Polar(-60f) * 12 * s, 2 * s, Soft);

            float x0 = bar.x + 34 * s;
            float x1 = bar.xMax - 16 * s;
            float cy = bar.center.y;
            Round(new Rect(x0, cy - s, x1 - x0, 2 * s), new Color(1f, 1f, 1f, 0.3f), s);
            float startX = x0 + (x1 - x0) / TrackOverflow;
            Round(new Rect(startX - s, cy - 6 * s, 2 * s, 12 * s), new Color(1f, 1f, 1f, 0.45f), s);

            float t = f.FishDistanceCm / _startDistanceCm;
            float mx = x0 + (x1 - x0) * Mathf.Clamp(t, 0f, TrackOverflow) / TrackOverflow;
            var marker = new Vector2(mx, cy);
            Color tint = StaminaColor(f.Stamina);

            if (f.HookHold < f.HookHoldStart - 0.001f || f.SlackCm > TightSlackCm || _hookFlash > 0f)
                HookRing(marker, f);

            // Рыба смотрит к берегу (влево): тело, хвост, глаз.
            Round(new Rect(mx - 10 * s, cy - 5 * s, 18 * s, 10 * s), tint, 5 * s);
            Bar(new Vector2(mx + 6 * s, cy), new Vector2(mx + 12 * s, cy - 4.5f * s), 3 * s, tint);
            Bar(new Vector2(mx + 6 * s, cy), new Vector2(mx + 12 * s, cy + 4.5f * s), 3 * s, tint);
            Disc(new Vector2(mx - 5.5f * s, cy - 1.2f * s), 1.3f * s, Dark);

            Label(new Rect(mx - 40 * s, bar.yMax + 1 * s, 80 * s, 14 * s), StaminaWord(f.Stamina), _tiny, tint);

            if (_beginner)
                TelegraphWord(bar, mx);
        }

        /// <summary>Кольцо крючка: 12 точек, горят по доле HookHold от начального; мигает на HookLoss.</summary>
        private void HookRing(Vector2 center, FightFrame f)
        {
            float s = _scale;
            float share = f.HookHoldStart > 0f ? Mathf.Clamp01(f.HookHold / f.HookHoldStart) : 0f;
            bool flash = _hookFlash > 0f && Mathf.Repeat(Time.time * 8f, 1f) < 0.5f;
            float radius = (flash ? 16f : 14f) * s;
            Color lit = flash || share <= 0.5f ? Red : Amber;
            const int dots = 12;
            for (int i = 0; i < dots; i++)
            {
                bool on = (i + 0.5f) / dots <= share;
                Disc(center + Polar(90f - i * 360f / dots) * radius, (flash ? 2.2f : 1.8f) * s, on ? lit : Dim);
            }
        }

        /// <summary>Слово телеграфа над меткой (только HUD новичка): серое, заполняется цветом слева направо.</summary>
        private void TelegraphWord(Rect bar, float mx)
        {
            float alpha = Mathf.Clamp01(_telegraphLeft / 0.3f);
            if (alpha <= 0f)
                return;
            float s = _scale;
            string word = TelegraphWordOf(_telegraphPhase);
            float tw = TextWidth(word, _word) + 2f;
            float h = 18 * s;
            float x = Mathf.Clamp(mx - tw / 2, bar.x + 8 * s, bar.xMax - tw - 8 * s);
            float y = bar.y - h - 2 * s;

            Round(new Rect(x - 6 * s, y, tw + 12 * s, h), WithAlpha(Glass, alpha), h / 2);
            Label(new Rect(x, y, tw, h), word, _word, new Color(1f, 1f, 1f, 0.35f * alpha));
            float fill = Mathf.Clamp01(_telegraphFill);
            if (fill <= 0f)
                return;
            GUI.BeginGroup(new Rect(x, y, tw * fill, h));
            Label(new Rect(0f, 0f, tw, h), word, _word, WithAlpha(TelegraphColor(_telegraphPhase), alpha));
            GUI.EndGroup();
        }

        private void CoachLine()
        {
            if (!_drawing)
                return;
            string text = _coach.Text;
            float alpha = _coach.Alpha;
            if (text == null || alpha <= 0f)
                return;
            var rect = _coachRect;
            Color fill, ink;
            switch (_coach.CurrentKind)
            {
                case FightCoach.Kind.Danger:
                    fill = Red;
                    ink = Color.white;
                    break;
                case FightCoach.Kind.Mistake:
                    fill = Amber;
                    ink = Dark;
                    break;
                default:
                    fill = Green;
                    ink = Dark;
                    break;
            }
            GlassPanel(rect, WithAlpha(fill, 0.92f * alpha), rect.height / 2, alpha);
            Label(new Rect(rect.x + 14 * _scale, rect.y, rect.width - 28 * _scale, rect.height), text, _coachStyle, WithAlpha(ink, alpha));
        }

        /// <summary>«Чётко!» / «Поздно» после подсечки: всплывает и гаснет к концу 1,5 с.</summary>
        private void StrikeBanner(Rect area)
        {
            string text = _game.StrikeText;
            if (!_drawing || text == null)
                return;
            float s = _scale;
            float age = _game.StateTime;
            float alpha = Mathf.Clamp01((1.5f - age) / 0.4f);
            float pop = 1f + 0.35f * Mathf.Clamp01(1f - age / 0.18f);
            float w = 210 * s;
            float h = 58 * s;
            float y = Mathf.Max(area.y + area.height * 0.3f, _coachRect.yMax + 14 * s);
            var rect = new Rect(area.center.x - w / 2, y, w, h);
            bool clean = _game.StrikeQuality >= 0.999;

            var matrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), rect.center);
            GlassPanel(rect, WithAlpha(clean ? Green : Amber, 0.92f * alpha), h / 2, alpha);
            Label(rect, text, _strike, WithAlpha(Dark, alpha));
            GUI.matrix = matrix;
        }

        // ---------- Низ: кнопки, дуга, счётчик, фрикцион, подсачек ----------

        private void Controls(Rect area)
        {
            float s = _scale;
            float size = Mathf.Min(area.width * 0.36f, 150 * s);
            _liftRect = new Rect(area.x + 4 * s, area.yMax - size, size, size);
            _reelRect = new Rect(area.xMax - size - 4 * s, area.yMax - size, size, size);

            // Правая колонка над «Подмоткой»: фрикцион −/+, над ним счётчик лески.
            float pill = 40 * s;
            _dragRowRect = new Rect(_reelRect.x, _reelRect.y - 8 * s - pill, size, pill);
            _counterRect = new Rect(_reelRect.x - 4 * s, _dragRowRect.y - 8 * s - 66 * s, size + 8 * s, 66 * s);

            if (_game.State == LakeState.Fight)
            {
                FightLayout(area, size);
                TensionArc();
                FirstFishRule();
                DragPills();
                NetButton();
            }

            LineCounter();
            LiftButton(_liftRect);
            ReelButton(_reelRect);
            if (_game.State == LakeState.Waiting)
                RetrieveButton();
        }

        private void FightLayout(Rect area, float size)
        {
            float s = _scale;
            float gap = _reelRect.x - _liftRect.xMax;
            float radius = Mathf.Min(area.width * 0.25f, 96 * s);
            _gaugeRadius = radius;

            // Подпись насечки фрикциона выходит за дугу примерно на 60 единиц — столько же нужно по бокам.
            if (gap >= 2 * (radius + 60 * s))
            {
                // Широкий экран: дуга между кнопками, «Подсачек» над «Подтянуть», правило под центром дуги.
                _gaugeCenter = new Vector2(area.center.x, area.yMax - 34 * s);
                float d = Mathf.Min(size * 0.7f, 96 * s);
                _netRect = new Rect(_liftRect.center.x - d / 2, _liftRect.y - 10 * s - d, d, d);
                _ruleRect = new Rect(_gaugeCenter.x - radius - 70 * s, _gaugeCenter.y + 6 * s, 2 * radius + 140 * s, 28 * s);
                return;
            }

            // Телефон стоя: дуга над правой колонкой, «Подсачек» между кнопками (если влезает), правило слева под дугой.
            _gaugeCenter = new Vector2(area.center.x, _counterRect.y - 16 * s);
            float between = Mathf.Min(gap - 10 * s, 100 * s);
            if (between >= 64 * s)
            {
                _netRect = new Rect(area.center.x - between / 2, _liftRect.center.y - between / 2, between, between);
            }
            else
            {
                float d = Mathf.Min(size * 0.7f, 96 * s);
                _netRect = new Rect(_liftRect.center.x - d / 2, _liftRect.y - 10 * s - d, d, d);
            }
            _ruleRect = new Rect(area.x, _gaugeCenter.y + 10 * s, _counterRect.x - 8 * s - area.x, 40 * s);
        }

        /// <summary>
        /// Дуга натяжения: стрелка = PeakT / самое слабое звено; зоны 55% / 85%; насечка фрикциона со значением;
        /// «призрак» удара гаснет за 0,6 с; подпись «леска» / «удилище». Перекрут — заштрихованный конец дуги:
        /// шкала сжимается на долю износа, и стрелка упирается в штриховку раньше.
        /// </summary>
        private void TensionArc()
        {
            if (!_drawing)
                return;
            var f = _game.Frame;
            float s = _scale;
            var c = _gaugeCenter;
            float r = _gaugeRadius;
            float hatch = f.WeakestIsRod ? 0f : Mathf.Clamp(f.LineWear, 0f, 0.4f);
            float usable = 1f - hatch;

            HalfRing(c, r, 26 * s, Glass);

            float segment = Mathf.Min(7f * s, Mathf.PI * r / ArcSegments * 0.6f);
            float needle = Mathf.Clamp01(_needle) * usable;
            for (int i = 0; i < ArcSegments; i++)
            {
                float t = (i + 0.5f) / ArcSegments;
                var u = Polar(180f - t * 180f);
                var p0 = c + u * (r - 9 * s);
                var p1 = c + u * (r + 9 * s);
                if (t > usable)
                {
                    var v = new Vector2(-u.y, u.x) * (segment * 0.5f);
                    Bar(p0, p1, segment, WithAlpha(Red, 0.3f));
                    Bar(p0 + u * (2 * s) - v, p1 - u * (2 * s) + v, 1.6f * s, WithAlpha(Red, 0.9f));
                    continue;
                }
                Color zone = Zone(t / usable);
                Bar(p0, p1, segment, t <= needle ? zone : WithAlpha(zone, 0.28f));
            }

            if (f.WeakestLinkG > 0f)
            {
                float drag = Mathf.Clamp01(_game.DragG / f.WeakestLinkG) * usable;
                float angle = 180f - drag * 180f;
                Spoke(c, angle, r + 11 * s, r + 21 * s, 3 * s, Sky);
                var at = c + Polar(angle) * (r + 34 * s);
                Label(new Rect(at.x - 30 * s, at.y - 8 * s, 60 * s, 16 * s), _dragNotchText.Get(_game.DragG / 10), _tiny, Sky);
            }

            if (_ghostLeft > 0f)
            {
                float ghost = Mathf.Clamp01(_ghost) * usable;
                Spoke(c, 180f - ghost * 180f, r - 12 * s, r + 12 * s, 3 * s, WithAlpha(Red, _ghostLeft / GhostSeconds));
            }

            float needleAngle = 180f - needle * 180f;
            Spoke(c, needleAngle, r - 15 * s, r + 14 * s, 6.5f * s, WithAlpha(Dark, 0.85f));
            Spoke(c, needleAngle, r - 14 * s, r + 13 * s, 3.5f * s, Color.white);

            float top = c.y - r * 0.55f;
            Label(new Rect(c.x - r, top - 18 * s, 2 * r, 32 * s), _peakText.Get(Mathf.RoundToInt(f.PeakTensionG / 10f)), _gaugeBig, Color.white);
            Label(new Rect(c.x - r, top + 13 * s, 2 * r, 16 * s), _limitText.Get(Mathf.RoundToInt(f.WeakestLinkG / 10f)), _small, Soft);
            string weakest = f.WeakestIsRod ? "удилище"
                : hatch >= 0.01f ? _twistText.Get(Mathf.RoundToInt(hatch * 100f)) : "леска";
            Label(new Rect(c.x - r, top + 29 * s, 2 * r, 16 * s), weakest, _small, f.LoadShare > DangerZone ? Red : Amber);
        }

        /// <summary>Правило первой рыбы под дугой (HUD новичка, пока не поймана первая рыба).</summary>
        private void FirstFishRule()
        {
            if (!_drawing || !_beginner || _firstFishCaught)
                return;
            float s = _scale;
            var rect = _ruleRect;
            GlassPanel(rect, Glass, 12 * s);
            Label(new Rect(rect.x + 8 * s, rect.y, rect.width - 16 * s, rect.height), FirstFishRuleText, _coachStyle, Amber);
        }

        /// <summary>
        /// В вываживании — счётчик лески: размотано, скорость (↓ выбираю / ↑ отдаю / = держишь), до рыбы.
        /// Вне вываживания на том же месте — глубиномер (DepthGauge).
        /// </summary>
        private void LineCounter()
        {
            if (!_drawing || !_counterValid)
                return;
            bool fight = _game.State == LakeState.Fight;
            var f = _game.Frame;
            float s = _scale;
            var rect = _counterRect;
            if (!fight)
            {
                // Пока ждём поклёвку, важна глубина под поплавком, а не длина лески.
                DepthGauge(rect);
                return;
            }

            // Без рамки: цифры прямо поверх воды, с тенью.
            float lineCm = f.LineOutCm;
            ShadowLabel(new Rect(rect.x, rect.y + 4 * s, rect.width, 26 * s), _lineText.Get(Mathf.RoundToInt(lineCm / 10f)), _counter, Color.white);

            // Катушка упирается (§1: «трудится») — держишь, хотя подмотка и выбирает свои 10% скорости.
            bool labour = fight && ReelHeld && !f.IsDragSlipping && f.ReelLoad > LabourReelLoad;
            string rate = null;
            Color color = Color.white;
            if (_lineRate > HoldStillCmPerSec)
            {
                rate = _rateUpText.Get(Mathf.Max(1, Mathf.RoundToInt(_lineRate / 10f)));
                color = Amber;
            }
            else if (labour)
            {
                rate = "= держишь";
            }
            else if (_lineRate < -HoldStillCmPerSec)
            {
                rate = _rateDownText.Get(Mathf.Max(1, Mathf.RoundToInt(-_lineRate / 10f)));
                color = Green;
            }
            else if (fight && f.SlackCm <= TightSlackCm)
            {
                rate = "= держишь";
            }
            else if (fight)
            {
                rate = "слабина";
                color = Red;
            }
            ShadowLabel(new Rect(rect.x, rect.y + 29 * s, rect.width, 18 * s), rate, _rate, color);

            if (fight)
                ShadowLabel(new Rect(rect.x, rect.y + 46 * s, rect.width, 16 * s), _fishText.Get(Mathf.RoundToInt(f.FishDistanceCm / 10f)), _small, Soft);
        }

        /// <summary>
        /// Глубиномер, пока поплавок на воде: столбик воды от поверхности до предела глубины этого места — с делениями
        /// через метр, дном под поплавком, поплавком наверху и наживкой на выставленном спуске (FloatRig); рядом крупно глубина и мелко —
        /// сколько до поплавка. Без рамки, поверх воды.
        /// </summary>
        private void DepthGauge(Rect rect)
        {
            float s = _scale;
            float depthM = _game.DepthAtFloatM;
            var spot = _game.Spot;
            float scaleM = Mathf.Max(1f, Mathf.Ceil((spot != null ? spot.maxDepthCm : 650) / 50f) * 0.5f);

            // Столбик: справа, выше прежней рамки.
            float columnWidth = 18 * s;
            float columnHeight = 112 * s;
            var column = new Rect(rect.xMax - columnWidth - 6 * s, rect.yMax - columnHeight, columnWidth, columnHeight);
            float radius = columnWidth / 2;
            Round(new Rect(column.x - 2 * s, column.y - 2 * s + 2 * s, column.width + 4 * s, column.height + 4 * s), new Color(0f, 0f, 0f, 0.28f), radius + 2 * s);
            Round(column, new Color(0.05f, 0.27f, 0.42f, 0.95f), radius);
            GUI.DrawTexture(column, Sheen(), ScaleMode.StretchToFill, true, 0f, new Color(0.55f, 0.88f, 1f, 0.75f), 0f, radius);

            // Дно под поплавком: всё, что ниже, — грунт.
            float bottom = Mathf.Lerp(column.y + 8 * s, column.yMax, Mathf.Clamp01(depthM / scaleM));
            if (bottom < column.yMax - 1f)
            {
                GUI.BeginGroup(new Rect(column.x, bottom, column.width, column.yMax - bottom));
                // Тот же скруглённый столбик, сдвинутый вверх: группа оставляет от него только низ.
                Round(new Rect(0, column.y - bottom, column.width, column.height), new Color(0.50f, 0.38f, 0.22f), radius);
                GUI.EndGroup();
            }

            Round(new Rect(column.x, bottom - 1.2f * s, column.width, 2.4f * s), new Color(0.93f, 0.82f, 0.55f), 1.2f * s);
            Ring(column, new Color(1f, 1f, 1f, 0.55f), 1.3f * s, radius);

            // Деления через метр — слева от столбика.
            for (int m = 1; m < scaleM; m++)
            {
                float y = Mathf.Lerp(column.y + 8 * s, column.yMax, m / scaleM);
                Round(new Rect(column.x - 7 * s, y - 0.8f * s, 5 * s, 1.6f * s), new Color(1f, 1f, 1f, 0.55f), 0.8f * s);
            }

            // Поплавок на поверхности (покачивается) и наживка в 20 см от дна.
            float bob = Mathf.Sin(Time.time * 2.2f) * 1.2f * s;
            var top = new Vector2(column.center.x, column.y + 8 * s + bob);
            float baitDepthM = _game.BaitDepthM;
            var bait = new Vector2(column.center.x, Mathf.Lerp(column.y + 8 * s, column.yMax, Mathf.Clamp01(baitDepthM / scaleM)));
            Bar(top, bait, 1f * s, new Color(1f, 1f, 1f, 0.7f));
            Disc(bait, 2.6f * s, Orange);
            Disc(top, 4.2f * s, Color.white);
            Disc(top + new Vector2(0f, -1.8f * s), 3f * s, Red);

            // Числа слева от столбика: глубина крупно, расстояние до поплавка мелко.
            int key = Mathf.RoundToInt(depthM * 10f);
            if (key != _depthKey || _depthNumber == null)
            {
                _depthKey = key;
                _depthNumber = HudText.Fixed(HudText.Scaled(depthM, 1), 1) + " м";
            }

            float textRight = column.x - 14 * s;
            float textWidth = textRight - rect.x + 30 * s;
            var number = new Rect(textRight - textWidth, column.y + 26 * s, textWidth, 30 * s);
            ShadowLabel(number, _depthNumber, _depthStyle, Color.white);
            ShadowLabel(new Rect(number.x, number.y - 16 * s, textWidth, 16 * s), "глубина", _depthCaption, Soft);
            int far = Mathf.RoundToInt(_game.FloatDistanceM * 10f);
            if (far != _floatFarKey || _floatFarText == null)
            {
                _floatFarKey = far;
                _floatFarText = _lineText.Get(far) + " до поплавка";
            }

            ShadowLabel(new Rect(number.x, number.yMax + 2 * s, textWidth, 16 * s), _floatFarText, _depthCaption, Soft);
            if (_game.BaitOnBottom)
                ShadowLabel(new Rect(number.x, number.yMax + 18 * s, textWidth, 16 * s), "наживка на дне", _depthCaption, Amber);
        }

        private int _depthKey = int.MinValue;
        private string _depthNumber;
        private int _floatFarKey = int.MinValue;
        private string _floatFarText;

        /// <summary>Подпись поверх воды без рамки: тёмная копия со сдвигом, потом сам текст.</summary>
        private void ShadowLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            if (!_drawing || text == null)
                return;
            float o = 1.4f * _scale;
            Label(new Rect(rect.x + o, rect.y + o, rect.width, rect.height), text, style, new Color(0f, 0f, 0f, 0.55f * color.a));
            Label(rect, text, style, color);
        }

        /// <summary>Фрикцион −/+ над «Подмоткой»: значение — настройка игрока (FishingController.DragG).</summary>
        private void DragPills()
        {
            float s = _scale;
            var row = _dragRowRect;
            float p = row.height;
            _minusRect = new Rect(row.x, row.y, p, p);
            _plusRect = new Rect(row.xMax - p, row.y, p, p);
            if (!_drawing)
                return;

            GlassPanel(row, Glass, p / 2);
            DragPill(_minusRect, "−", _minusHeld);
            DragPill(_plusRect, "+", _plusHeld);
            var middle = new Rect(row.x + p, row.y, row.width - 2 * p, p);
            Label(new Rect(middle.x, middle.y + 3 * s, middle.width, 12 * s), "фрикцион", _tiny, Soft);
            Label(new Rect(middle.x, middle.y + 14 * s, middle.width, 22 * s), _dragValueText.Get(_game.DragG / 10), _rate, Color.white);
        }

        private void DragPill(Rect rect, string label, bool held)
        {
            Round(rect, held ? WithAlpha(Amber, 0.9f) : Glass, rect.height / 2);
            Ring(rect, held ? Amber : GlassBorder, 1.5f * _scale, rect.height / 2);
            Label(rect, label, _counter, held ? Dark : Color.white);
        }

        /// <summary>
        /// «Подсачек»: виден, когда рыбу махом не выудить, а она в зоне подсачека (FightFrame.NetShown — то же
        /// правило читает тренер); горит ровно при CanNet, иначе под названием написано, чего не хватает (NetBlock).
        /// </summary>
        /// <summary>Что сделать, чтобы подсачек взял рыбу, — коротко, под кнопку.</summary>
        private static string NetBlockText(NetBlock block)
        {
            switch (block)
            {
                case NetBlock.Slack: return "натяни леску";
                case NetBlock.RodLow: return "подними удилище";
                case NetBlock.FishStrong: return "рыба не устала";
                case NetBlock.FishFighting: return "жди паузы";
                case NetBlock.Cooldown: return "промах — подожди";
                default: return "рано";
            }
        }

        private void NetButton()
        {
            var f = _game.Frame;
            if (!f.NetShown)
                return;
            if (Tapped(_netRect))
                NetPressed = true;
            if (!_drawing)
                return;

            float s = _scale;
            var rect = _netRect;
            float radius = rect.width / 2;
            var title = new Rect(rect.x, rect.center.y - 15 * s, rect.width, 20 * s);
            var sub = new Rect(rect.x, rect.center.y + 4 * s, rect.width, 14 * s);
            if (f.CanNet)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
                float grow = (4f + 6f * pulse) * s;
                var glow = new Rect(rect.x - grow, rect.y - grow, rect.width + 2 * grow, rect.height + 2 * grow);
                Round(glow, WithAlpha(Green, 0.22f + 0.2f * pulse), glow.width / 2);
                RoundButton(rect, WithAlpha(Green, 0.95f), Color.white, 2 * s, WithAlpha(Green, 0.45f));
                Label(title, "Подсачек", _center, Dark);
                Label(sub, "жми! · пробел", _tiny, Dark);
            }
            else
            {
                GlassPanel(rect, Glass, radius);
                Rim(rect, GlassBorder, 2 * s, radius);
                // Не «рано», а что именно сделать: нажатие не вовремя пугает рыбу, и она бросается прочь.
                Label(title, "Подсачек", _center, Soft);
                Label(sub, NetBlockText(f.NetBlock), _tiny, Amber);
            }
        }

        /// <summary>
        /// «Подтянуть»: дуга — подъём удилища, цвет — нагрузка на удилище; в паузе на натянутой леске (удилище ниже
        /// 0,9) пульсирует зелёным; при CanSwing — «Выудить»; на поклёвке — подсечка; удерживаемая с удилищем
        /// в самом верху — «вверху · мотай» (это уже простой).
        /// </summary>
        private void LiftButton(Rect rect)
        {
            if (!_drawing)
                return;
            float s = _scale;
            var state = _game.State;
            bool fight = state == LakeState.Fight;
            var f = _game.Frame;
            bool held = fight ? LiftHeld && !LiftHeldBack : LiftHeld;
            bool swing = fight && f.CanSwing;
            bool cue = fight && !swing && f.Phase == FishPhase.Rest && f.SlackCm <= TightSlackCm && f.RodAngle < LiftCueMaxAngle;
            bool strike = state == LakeState.Bite;
            bool calling = swing || cue || strike;
            Color accent = strike ? Amber : Green;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            _liftPress = Mathf.MoveTowards(_liftPress, held ? 1f : 0f, Time.unscaledDeltaTime * 12f);
            rect = Pressed(rect, _liftPress);
            float radius = rect.width / 2;

            Color fill;
            if (held)
                fill = calling ? WithAlpha(accent, 0.92f) : WithAlpha(Amber, 0.9f);
            else if (calling)
                fill = Color.Lerp(Glass, WithAlpha(accent, 0.9f), (cue ? 0.2f : 0.55f) + 0.4f * pulse);
            else
                fill = Glass;
            // Как в макете: янтарная кромка и свечение; зовущая кнопка светится своим цветом и пульсирует.
            Color rim = calling ? WithAlpha(accent, 0.6f + 0.4f * pulse) : Amber;
            Color glow = calling ? WithAlpha(accent, 0.35f + 0.35f * pulse) : WithAlpha(Amber, held ? 0.55f : 0.30f);
            RoundButton(rect, fill, rim, (calling ? 3.5f : 3f) * s, glow);

            bool bright = held || swing || strike;
            Color ink = bright ? Dark : Color.white;

            if (fight)
            {
                // Подъём удилища: дуга от «опущено» (слева внизу) к «поднято» (вверху).
                float arc = radius - 9 * s;
                Color lit = Zone(f.RodLoadShare);
                for (int i = 0; i < LiftArcSegments; i++)
                {
                    float t = (i + 0.5f) / LiftArcSegments;
                    float angle = Mathf.Lerp(205f, 95f, t);
                    if (t <= f.RodAngle)
                    {
                        Spoke(rect.center, angle, arc - 6 * s, arc + 4 * s, 5f * s, WithAlpha(Dark, 0.6f));
                        Spoke(rect.center, angle, arc - 5 * s, arc + 3 * s, 3.2f * s, lit);
                    }
                    else
                    {
                        Spoke(rect.center, angle, arc - 5 * s, arc + 3 * s, 3.2f * s, bright ? WithAlpha(Dark, 0.25f) : Dim);
                    }
                }
            }

            if (!fight)
                Chevrons(new Vector2(rect.center.x, rect.y + rect.height * 0.27f), rect.width * 0.13f, ink);

            string label = swing ? "Выудить" : "Подтянуть";
            // Удилище в самом верху: держать дальше — простой (крючок слабеет), надо опустить его «Подмоткой».
            // Как у строки тренера «Удилище вверху»: не на пробуксовке и не в зоне подсачека (там держат высоко).
            bool top = fight && held && !swing && f.RodAtTop && !f.IsDragSlipping && !f.NetShown;
            string sub = fight && LiftHeldBack ? "отпусти" : strike ? "подсекай!" : swing ? "жми · F" : top ? "вверху · мотай" : "держи · F";
            float y = rect.y + rect.height * 0.42f;
            Label(new Rect(rect.x, y, rect.width, 22 * s), label, _center, ink);
            Label(new Rect(rect.x, y + 20 * s, rect.width, 16 * s), sub, _small, WithAlpha(ink, 0.85f));
        }

        /// <summary>
        /// «Подмотка»: ручка крутится со скоростью подмотки; при ReelLoad > 0,65 — медленно, с дрожью, «= держишь»;
        /// при пробуксовке шпуля крутится назад красным; подмотка в пробуксовку — янтарное кольцо и «вхолостую».
        /// </summary>
        private void ReelButton(Rect rect)
        {
            if (!_drawing)
                return;
            float s = _scale;
            bool fight = _game.State == LakeState.Fight;
            var f = _game.Frame;
            bool held = ReelHeld;
            bool idle = fight && held && f.IsCrankingIntoSlip;
            bool labour = fight && held && !idle && f.ReelLoad > LabourReelLoad;
            bool slipping = fight && f.IsDragSlipping;
            _reelPress = Mathf.MoveTowards(_reelPress, held ? 1f : 0f, Time.unscaledDeltaTime * 12f);
            rect = Pressed(rect, _reelPress);
            float radius = rect.width / 2;

            if (idle)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 12f);
                RoundButton(rect, new Color(0.22f, 0.10f, 0.05f, 0.82f), WithAlpha(Amber, pulse), 5 * s, WithAlpha(Amber, 0.35f * pulse));
            }
            else
            {
                RoundButton(rect, held ? WithAlpha(Amber, 0.92f) : Glass, Amber, 3 * s, WithAlpha(Amber, held ? 0.55f : 0.30f));
            }

            Color ink = held && !idle ? Dark : Color.white;

            // Катушка: шпуля — чёрточки по кругу, поверх — ручка с рукояткой.
            var hub = new Vector2(rect.center.x, rect.y + rect.height * 0.33f);
            float g = rect.width * 0.15f;
            Color spool = slipping ? Red : WithAlpha(ink, 0.55f);
            for (int i = 0; i < 8; i++)
                Spoke(hub, _spoolAngle + i * 45f, g * 0.75f, g * 1.15f, 2.6f * s, spool);
            float jitter = labour ? (Mathf.PerlinNoise(Time.time * 25f, 0.37f) - 0.5f) * 24f : 0f;
            var knob = hub + Polar(_handleAngle + jitter) * (g * 1.45f);
            Bar(hub, knob, 3.5f * s, ink);
            Disc(knob, 4.8f * s, ink);
            Disc(hub, 3.5f * s, ink);

            float y = rect.y + rect.height * 0.58f;
            Label(new Rect(rect.x, y, rect.width, 22 * s), "Подмотка", _center, idle ? Amber : ink);
            string sub = idle ? "вхолостую" : labour ? "= держишь" : "держи · J";
            Label(new Rect(rect.x, y + 20 * s, rect.width, 16 * s), sub, _small, idle ? Amber : WithAlpha(ink, 0.85f));
        }

        // ---------- Слова и цвета ----------

        /// <summary>Силы рыбы в три ступени: пороги совпадают с моделью (выудить — меньше 0,6, подсачек — до 0,3).</summary>
        private static string StaminaWord(float stamina)
        {
            if (stamina >= FreshStamina) return "свежая";
            return stamina > SpentStamina ? "устаёт" : "выдохлась";
        }

        private static Color StaminaColor(float stamina)
        {
            if (stamina >= FreshStamina) return Orange;
            return stamina > SpentStamina ? Amber : Green;
        }

        private static string TelegraphWordOf(FishPhase phase)
        {
            switch (phase)
            {
                case FishPhase.Run: return "уйдёт";
                case FishPhase.Jerk: return "рывок";
                case FishPhase.Dive: return "вглубь";
                case FishPhase.Jump: return "свечка";
                // Бросок — рыба у берега разворачивается и уходит от рыбака, забирая леску (ответ — отпустить
                // «Подмотку»), поэтому «от берега»: «к берегу» звало бы подматывать.
                case FishPhase.Surge: return "бросок от берега";
                default: return "встала";
            }
        }

        private static Color TelegraphColor(FishPhase phase)
        {
            switch (phase)
            {
                case FishPhase.Rest: return Green;
                case FishPhase.Jump: return Amber;
                default: return Orange;
            }
        }
    }
}
