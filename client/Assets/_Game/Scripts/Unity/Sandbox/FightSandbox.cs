using System;
using System.Collections.Generic;
using System.Globalization;
using Fishing.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fishing.Unity
{
    /// <summary>
    /// Песочница вываживания без графики: шкалы и две кнопки, чтобы почувствовать
    /// FightModel руками и подкрутить числа. Не часть игры — инструмент прототипа.
    ///
    /// Управление. Телефон: левый палец — «Подтянуть», правый — «Подмотка» (можно вместе),
    /// у берега — кнопка «Подсачек». ПК: F — подтянуть, J — подмотка, пробел — подсачек, Q/E — фрикцион, R — заново.
    ///
    /// Инструмент разработчика: строки прямо в коде, правило про таблицы Localization
    /// к нему не относится. IMGUI только рисует; все нажатия читаются через Input System
    /// (IMGUI не видит касаний, когда включена только новая система ввода). Один и тот же
    /// метод Ui() раскладывает экран дважды за кадр: в Update — чтобы проверить нажатия,
    /// в OnGUI — чтобы нарисовать, поэтому прямоугольники кнопок всегда совпадают.
    /// </summary>
    public sealed class FightSandbox : MonoBehaviour
    {
        private const double StartDistanceCm = 2000;

        private struct TacklePreset
        {
            public string Name;
            public string Rod;
            public string Reel;
            public string Line;
        }

        private static readonly TacklePreset[] Presets =
        {
            new TacklePreset { Name = "Поплавок, леска 0,12", Rod = "float_rod_basic", Reel = "reel_1000", Line = "line_012" },
            new TacklePreset { Name = "Спиннинг, леска 0,20", Rod = "spinning_rod_basic", Reel = "reel_2500", Line = "line_020" },
            new TacklePreset { Name = "Спиннинг, леска 0,30", Rod = "spinning_rod_basic", Reel = "reel_2500", Line = "line_030" },
        };

        private static readonly Dictionary<string, string> FishNames = new Dictionary<string, string>
        {
            { "roach", "Плотва" }, { "crucian", "Карась" }, { "perch", "Окунь" },
            { "bream", "Лещ" }, { "zander", "Судак" }, { "pike", "Щука" },
        };

        /// <summary>Названия фаз по порядку FishPhase: Rest, Run, Jerk, Dive, Jump, Surge.</summary>
        private static readonly string[] PhaseNames = { "отдыхает", "уходит", "рывок!", "тянет вглубь", "свечка!", "бросок от берега!" };

        private static readonly Color Background = new Color(0.06f, 0.12f, 0.14f);
        private static readonly Color Panel = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color Pressed = new Color(1f, 0.72f, 0.28f, 0.9f);
        private static readonly Color Safe = new Color(0.49f, 0.83f, 0.45f);
        private static readonly Color Warn = new Color(1f, 0.78f, 0.34f);
        private static readonly Color Danger = new Color(1f, 0.36f, 0.31f);

        private FishDatabase _fish;
        private TackleDatabase _tackle;
        private string _error;

        private int _fishIndex;
        private int _presetIndex = 2;
        private bool _randomWeight = true;
        private int _weightG = 1000;
        private int _dragG;

        private FightModel _fight;
        private FightTackle _fightTackle;
        private FishSpecies _species;
        private FishPhase _lastPhase;
        private double _accumulator;
        private readonly double _tick = new FightSettings().TickSeconds;

        private readonly List<Vector2> _taps = new List<Vector2>();
        private bool _drawing;
        private bool _liftHeld;
        private bool _reelHeld;
        private bool _netQueued;
        private Rect _liftRect;
        private Rect _reelRect;

        private float _scale = 1f;
        private GUIStyle _text;
        private GUIStyle _title;
        private GUIStyle _center;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
            }

            try
            {
                _fish = SharedData.LoadFish();
                _tackle = SharedData.LoadTackle();
                _fishIndex = Math.Max(0, IndexOfFish("perch"));
                ResetWeight();
                ResetDrag();
            }
            catch (Exception e)
            {
                _error = e.Message;
            }
        }

        private void Update()
        {
            ReadInput();
            _drawing = false;
            Ui();
            _taps.Clear();

            if (_fight == null || _fight.Outcome != FightOutcome.InProgress)
                return;

            var input = new FightInput { Reel = _reelHeld, Lift = _liftHeld, DragG = _dragG };
            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= _tick && steps < 10 && _fight.Outcome == FightOutcome.InProgress)
            {
                // «Подсачек» — фронт: уходит в модель ровно одним шагом.
                input.Net = _netQueued;
                _netQueued = false;
                _fight.Step(input);
                _accumulator -= _tick;
                steps++;
            }

            // Вибрация — через Haptics (§6): короткий стук на рывке, длинный — в конце вываживания.
            if (_fight.Phase == FishPhase.Jerk && _lastPhase != FishPhase.Jerk)
                Haptics.Pulse(15, 180);
            if (_fight.Outcome != FightOutcome.InProgress)
                Haptics.Pulse(70, Haptics.MaxAmplitude);
            _lastPhase = _fight.Phase;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            EnsureStyles();
            _drawing = true;
            Ui();
        }

        // ---------- Ввод ----------

        private void ReadInput()
        {
            _liftHeld = false;
            _reelHeld = false;
            _taps.Clear();
            if (_fish == null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                _liftHeld |= keyboard.fKey.isPressed;
                _reelHeld |= keyboard.jKey.isPressed;
                if (keyboard.qKey.wasPressedThisFrame) ChangeDrag(-200);
                if (keyboard.eKey.wasPressedThisFrame) ChangeDrag(200);
                if (keyboard.rKey.wasPressedThisFrame) StartFight();
                if (keyboard.spaceKey.wasPressedThisFrame && _fight != null && _fight.Outcome == FightOutcome.InProgress)
                    _netQueued = true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var position = ToGui(mouse.position.ReadValue());
                if (mouse.leftButton.isPressed) Hold(position);
                if (mouse.leftButton.wasPressedThisFrame) _taps.Add(position);
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    var position = ToGui(touch.position.ReadValue());
                    if (touch.press.isPressed) Hold(position);
                    if (touch.press.wasPressedThisFrame) _taps.Add(position);
                }
            }
        }

        /// <summary>Экранные координаты Input System (снизу вверх) → координаты IMGUI (сверху вниз).</summary>
        private static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

        private void Hold(Vector2 point)
        {
            if (_fight == null || _fight.Outcome != FightOutcome.InProgress)
                return;
            if (_liftRect.Contains(point)) _liftHeld = true;
            if (_reelRect.Contains(point)) _reelHeld = true;
        }

        // ---------- Экран ----------

        private void Ui()
        {
            _scale = Mathf.Clamp(Mathf.Min(Screen.safeArea.width, Screen.safeArea.height) / 420f, 1f, 4f);
            float pad = 16 * _scale;
            var safe = Screen.safeArea;
            var area = new Rect(safe.x + pad, Screen.height - safe.yMax + pad, safe.width - 2 * pad, safe.height - 2 * pad);

            if (_error != null)
            {
                Label(area, "Ошибка загрузки данных:\n" + _error, _text);
                return;
            }

            if (_fight == null)
                SetupScreen(area);
            else
                FightScreen(area);
        }

        private void SetupScreen(Rect area)
        {
            float s = _scale;
            float width = Mathf.Min(area.width, 560 * s);
            float x = area.x + (area.width - width) / 2;
            float row = 56 * s;
            float gap = 10 * s;
            float y = area.y;

            Label(new Rect(x, y, width, 44 * s), "Песочница вываживания", _title);
            y += 44 * s + gap;

            var species = _fish.All[_fishIndex];
            int step = Stepper(new Rect(x, y, width, row), "Рыба", FishName(species.id));
            if (step != 0)
            {
                _fishIndex = (_fishIndex + step + _fish.All.Count) % _fish.All.Count;
                ResetWeight();
                return;
            }
            y += row + gap;

            step = Stepper(new Rect(x, y, width, row), "Вес", _randomWeight ? "случайный" : Kg(_weightG));
            if (step != 0)
            {
                _randomWeight = false;
                var w = species.weightG;
                _weightG = Mathf.Clamp(_weightG + step * Math.Max(50, _weightG / 10), w.min, w.max);
                return;
            }
            y += row + gap;

            if (Button(new Rect(x, y, width, row), _randomWeight ? "Вес: случайный, как при поклёвке" : "Сделать вес случайным"))
            {
                _randomWeight = !_randomWeight;
                return;
            }
            y += row + gap;

            var preset = Presets[_presetIndex];
            step = Stepper(new Rect(x, y, width, row), "Снасть", preset.Name);
            if (step != 0)
            {
                _presetIndex = (_presetIndex + step + Presets.Length) % Presets.Length;
                ResetDrag();
                return;
            }
            y += row + gap;

            var rod = _tackle.GetRod(preset.Rod);
            var reel = _tackle.GetReel(preset.Reel);
            var line = _tackle.GetLine(preset.Line);
            Label(new Rect(x, y, width, 52 * s), $"Удилище держит до {Kg(rod.maxLoadG)}, леска рвётся на {Kg(line.breakingLoadG)}, " +
                                                  $"фрикцион катушки до {Kg(reel.maxDragG)}.", _text);
            y += 52 * s + gap;

            step = Stepper(new Rect(x, y, width, row), "Фрикцион", Kg(_dragG));
            if (step != 0)
            {
                ChangeDrag(step * 200);
                return;
            }
            y += row + 2 * gap;

            if (Button(new Rect(x, y, width, row * 1.4f), "Начать"))
            {
                StartFight();
                return;
            }
            y += row * 1.4f + 2 * gap;

            Label(new Rect(x, y, width, area.yMax - y),
                "Телефон: левый палец — «Подтянуть», правый — «Подмотка», можно вместе.\n" +
                "ПК: F — подтянуть, J — подмотка, пробел — подсачек, Q/E — фрикцион, R — заново. " +
                "В окне Simulator сначала кликни в экран, чтобы работали клавиши.\n\n" +
                "Совет: на рывках и уходах не тяни, пусть работает фрикцион; когда рыба отдыхает — " +
                "«качай»: подтянул, опустил с подмоткой, снова подтянул.", _text);
        }

        private void FightScreen(Rect area)
        {
            var f = _fight;
            float s = _scale;
            float gap = 10 * s;
            float bar = 36 * s;
            float y = area.y;

            Label(new Rect(area.x, y, area.width, 30 * s), $"{FishName(_species.id)} · {Kg(f.WeightG)} · {Presets[_presetIndex].Name}", _text);
            y += 32 * s;
            Label(new Rect(area.x, y, area.width, 40 * s), "Рыба " + PhaseNames[(int)f.Phase], _title);
            y += 46 * s;

            Bar(new Rect(area.x, y, area.width, bar), f.Stamina, Safe, $"Силы рыбы {Percent(f.Stamina)}");
            y += bar + gap;

            double lineStrength = _fightTackle.LineBreakingLoadG * (1 - f.LineWear);
            double tension = f.PeakTensionG / lineStrength;
            string wear = f.LineWear > 0.01 ? $" · леска ослабла на {Percent(f.LineWear)}" : "";
            var tensionRect = new Rect(area.x, y, area.width, bar);
            Bar(tensionRect, tension, Zone(tension), $"Натяжение {Kg(f.PeakTensionG)} из {Kg(lineStrength)}{wear}");
            float dragX = tensionRect.x + tensionRect.width * Mathf.Clamp01((float)(_dragG / lineStrength));
            Box(new Rect(dragX - 2 * s, tensionRect.y - 4 * s, 4 * s, tensionRect.height + 8 * s), Color.white);
            y += bar + gap;

            double rodLoad = f.RodLoadG / _fightTackle.RodMaxLoadG;
            Bar(new Rect(area.x, y, area.width, bar), rodLoad, Zone(rodLoad),
                $"Удилище {Kg(f.RodLoadG)} из {Kg(_fightTackle.RodMaxLoadG)} · поднято на {Percent(f.RodAngle)}");
            y += bar + gap;

            Label(new Rect(area.x, y, area.width, 30 * s),
                $"Леска {Meters(f.LineOutCm)} из {Meters(_fightTackle.SpoolCapacityCm)} · до рыбы {Meters(f.FishDistanceCm)}", _text);
            y += 34 * s;

            if (f.SlackCm > 30)
                Label(new Rect(area.x, y, area.width, 36 * s), $"СЛАБИНА — подматывай! Крючок {Percent(f.HookHold)}", _title, Danger);
            else if (f.IsDragSlipping)
                Label(new Rect(area.x, y, area.width, 36 * s), "Фрикцион трещит — рыба забирает леску", _title, Warn);
            else if (f.CanSwing)
                Label(new Rect(area.x, y, area.width, 36 * s), "Подтянуть — выудить", _title, Safe);
            else if (f.CanNet)
                Label(new Rect(area.x, y, area.width, 36 * s), "Голова над водой — подсачек!", _title, Safe);
            y += 46 * s;

            // Временная кнопка «Подсачек»: видна, когда рыба в зоне подсачека.
            if (f.InNetReach && f.Outcome == FightOutcome.InProgress)
            {
                if (Button(new Rect(area.x, y, area.width, 56 * s), f.CanNet ? "Подсачек — сейчас!" : "Подсачек (рано)"))
                    _netQueued = true;
                y += 66 * s;
            }

            float small = 64 * s;
            if (Button(new Rect(area.x, y, small, small), "−")) ChangeDrag(-200);
            Label(new Rect(area.x + small, y, area.width - 2 * small, small), "Фрикцион " + Kg(_dragG), _center);
            if (Button(new Rect(area.xMax - small, y, small, small), "+")) ChangeDrag(200);

            float height = Mathf.Min(area.height * 0.28f, 240 * s);
            float width = (area.width - gap) / 2;
            _liftRect = new Rect(area.x, area.yMax - height, width, height);
            _reelRect = new Rect(area.x + width + gap, area.yMax - height, width, height);
            HoldButton(_liftRect, "Подтянуть", "F", _liftHeld);
            HoldButton(_reelRect, "Подмотка", "J", _reelHeld);

            if (f.Outcome != FightOutcome.InProgress)
                ResultOverlay(area);
        }

        private void ResultOverlay(Rect area)
        {
            var f = _fight;
            float s = _scale;
            Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.8f));

            float width = Mathf.Min(area.width, 520 * s);
            float x = area.x + (area.width - width) / 2;
            float y = area.y + area.height * 0.25f;

            string title;
            switch (f.Outcome)
            {
                case FightOutcome.Landed: title = "Вытащил!"; break;
                case FightOutcome.LineBroke: title = "Обрыв лески"; break;
                case FightOutcome.RodBroke: title = "Сломал удилище"; break;
                case FightOutcome.SpoolEmpty: title = "Рыба смотала всю леску"; break;
                default: title = "Сход"; break;
            }

            Label(new Rect(x, y, width, 48 * s), title, _title);
            y += 52 * s;
            Label(new Rect(x, y, width, 32 * s), $"{FishName(_species.id)} {Kg(f.WeightG)} · {Mathf.RoundToInt((float)f.ElapsedSeconds)} с", _text);
            y += 48 * s;

            if (Button(new Rect(x, y, width, 64 * s), "Ещё раз"))
            {
                StartFight();
                return;
            }
            y += 76 * s;
            if (Button(new Rect(x, y, width, 64 * s), "Настройки"))
                _fight = null;
        }

        // ---------- Виджеты: рисуют в OnGUI, ловят нажатия в Update ----------

        private bool Button(Rect rect, string label)
        {
            if (_drawing)
            {
                Fill(rect, ButtonColor);
                GUI.Label(rect, label, _center);
                return false;
            }

            foreach (var tap in _taps)
            {
                if (rect.Contains(tap))
                    return true;
            }
            return false;
        }

        private int Stepper(Rect rect, string label, string value)
        {
            float side = rect.height;
            float labelWidth = Mathf.Min(140 * _scale, rect.width * 0.3f);
            Label(new Rect(rect.x, rect.y, labelWidth, rect.height), label, _text);
            if (Button(new Rect(rect.x + labelWidth, rect.y, side, side), "<"))
                return -1;
            Label(new Rect(rect.x + labelWidth + side, rect.y, rect.width - labelWidth - 2 * side, rect.height), value, _center);
            if (Button(new Rect(rect.xMax - side, rect.y, side, side), ">"))
                return 1;
            return 0;
        }

        private void HoldButton(Rect rect, string label, string key, bool held)
        {
            if (!_drawing)
                return;
            Fill(rect, held ? Pressed : ButtonColor);
            var color = held ? new Color(0.1f, 0.06f, 0.02f) : Color.white;
            Label(rect, $"{label}\n<size={Mathf.RoundToInt(14 * _scale)}>удерживай · {key}</size>", _center, color);
        }

        private void Bar(Rect rect, double value, Color color, string label)
        {
            Box(rect, Panel);
            Box(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01((float)value), rect.height), color);
            Label(new Rect(rect.x + 8 * _scale, rect.y, rect.width - 16 * _scale, rect.height), label, _text);
        }

        private void Label(Rect rect, string text, GUIStyle style, Color? color = null)
        {
            if (!_drawing)
                return;
            var old = GUI.contentColor;
            GUI.contentColor = color ?? Color.white;
            GUI.Label(rect, text, style);
            GUI.contentColor = old;
        }

        private void Box(Rect rect, Color color)
        {
            if (_drawing)
                Fill(rect, color);
        }

        private static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ---------- Состояние ----------

        private void StartFight()
        {
            _species = _fish.All[_fishIndex];
            uint seed = (uint)Environment.TickCount;
            var random = new SeededRandom(seed);
            int weight = _randomWeight ? BiteModel.RollWeight(_species, random, out _) : _weightG;
            var preset = Presets[_presetIndex];
            _fightTackle = FightTackle.From(_tackle.GetRod(preset.Rod), _tackle.GetReel(preset.Reel), _tackle.GetLine(preset.Line));
            _dragG = Mathf.Clamp(_dragG, 0, _fightTackle.ReelMaxDragG);
            // Качество подсечки в песочнице не считается: крючок держит полностью (1,0).
            _fight = new FightModel(_species, weight, _fightTackle, StartDistanceCm, random, null, 0, 1.0, seed);
            _lastPhase = _fight.Phase;
            _accumulator = 0;
            _netQueued = false;
        }

        private void ChangeDrag(int deltaG)
        {
            int max = _tackle.GetReel(Presets[_presetIndex].Reel).maxDragG;
            _dragG = Mathf.Clamp(_dragG + deltaG, 0, max);
        }

        private void ResetWeight()
        {
            _weightG = _fish.All[_fishIndex].weightG.typical;
        }

        /// <summary>Фрикцион по умолчанию — тот, что модель ставит сама при подсечке (FightTackle.AutoDragG).</summary>
        private void ResetDrag()
        {
            var preset = Presets[_presetIndex];
            _dragG = FightTackle.From(_tackle.GetRod(preset.Rod), _tackle.GetReel(preset.Reel), _tackle.GetLine(preset.Line)).AutoDragG;
        }

        private int IndexOfFish(string id)
        {
            for (int i = 0; i < _fish.All.Count; i++)
                if (_fish.All[i].id == id)
                    return i;
            return -1;
        }

        private void EnsureStyles()
        {
            if (_text != null && _text.fontSize == Mathf.RoundToInt(15 * _scale))
                return;

            _text = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(15 * _scale), wordWrap = true, alignment = TextAnchor.MiddleLeft, richText = true,
            };
            _text.normal.textColor = Color.white;
            _title = new GUIStyle(_text) { fontSize = Mathf.RoundToInt(24 * _scale), fontStyle = FontStyle.Bold };
            _center = new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(20 * _scale), fontStyle = FontStyle.Bold };
        }

        private static Color Zone(double value)
        {
            if (value < 0.6) return Safe;
            return value < 0.85 ? Warn : Danger;
        }

        private static string FishName(string id) => FishNames.TryGetValue(id, out var name) ? name : id;

        private static string OneDecimal(double value) =>
            value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

        private static string Kg(double grams) => OneDecimal(grams / 1000.0) + " кг";

        private static string Meters(double cm) => OneDecimal(cm / 100.0) + " м";

        private static string Percent(double value) => Mathf.RoundToInt((float)(value * 100)) + "%";
    }
}
