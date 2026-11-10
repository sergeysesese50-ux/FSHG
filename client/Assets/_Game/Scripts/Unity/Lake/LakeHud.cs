using System.Collections.Generic;
using Fishing.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fishing.Unity
{
    /// <summary>
    /// Интерфейс тестового озера в стиле «Золотой час»: тёмное стекло поверх кадра, янтарный акцент.
    /// IMGUI только рисует; ввод — Input System (мультитач для двух кнопок и −/+, свайп для заброса, клавиши).
    /// Метод Ui() раскладывает экран дважды за кадр: в Update ловит нажатия, в OnGUI рисует.
    /// Плавные значения HUD (стрелка, скорость лески, вращение катушки, тренер) считаются в LateUpdate —
    /// после того как FishingController.Update шагнул модель и собрал Frame этого кадра.
    ///
    /// Вываживание читается только из FishingController.Frame (контракт — FightFrame.cs) и Report; модель — нет.
    /// Весь ввод озера читается здесь: контроллер, FightFeedback (звук: M и «Звук: вкл/выкл») и камера клавиш не читают.
    /// Части класса: этот файл — ввод, раскладка, верхняя панель, настройки;
    /// Hud/LakeHud.Fight.cs — экран вываживания (§5): дорожка, тренер, дуга натяжения, счётчик лески, кнопки,
    /// фрикцион, подсачек; Hud/LakeHud.Cards.cs — карточка схода с графиком; Hud/LakeHud.Catch.cs — карточка улова; Hud/LakeHud.Rig.cs — «Вытащить» и спуск поплавка; Hud/LakeHud.Keepnet.cs —
    /// кнопка и экран садка, кошелёк; Hud/LakeHud.Profile.cs — плашка и экран профиля, карма; Hud/LakeHud.Camp.cs — турбаза; Hud/LakeHud.Map.cs — карта озера и выбор места; Hud/LakeHud.Paint.cs —
    /// примитивы и стили; Hud/FightCoach.cs — тренер; Hud/FightLossText.cs — причины и советы; Hud/HudText.cs — числа.
    ///
    /// Прототип: строки прямо в коде; в игре они уйдут в таблицы Localization.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed partial class LakeHud : MonoBehaviour
    {
        private const string PrefBeginner = "fishing.hud.beginner";
        private const string PrefFirstFish = "fishing.hud.firstFishCaught";

        /// <summary>Шаг фрикциона −/+, г; долгое нажатие повторяет 8 шагов/с после короткой паузы.</summary>
        private const int DragStepG = 50;
        private const float DragRepeatDelay = 0.35f;
        private const float DragRepeatInterval = 1f / 8f;

        private FishingController _game;
        private LakeClock _clock;
        private FightFeedback _feedback;
        private FightCoach _coach;

        private readonly List<Vector2> _taps = new List<Vector2>();
        private bool _drawing;
        private Rect _liftRect;
        private Rect _reelRect;
        private Rect _minusRect;
        private Rect _plusRect;
        private bool _previousLift;

        private bool _minusHeld;
        private bool _plusHeld;
        private int _dragDirection;
        private float _dragRepeat;

        private bool _swiping;
        private Vector2 _swipeStart;
        private bool _castReady;
        private float _castPower;
        private float _castAim;

        private bool _beginner = true;
        private bool _firstFishCaught;
        private string _toast;
        private float _toastLeft;

        // Кэш строк верхней панели и подсказки по клавишам (собираются только при изменении).
        private int _topKey = int.MinValue;
        private string _topLeft;

        /// <summary>Палец тянет вверх для заброса (ещё не отпущен): контроллер показывает прицел — дугу и кольцо на воде.</summary>
        public bool CastAiming { get; private set; }

        /// <summary>Сила будущего заброса 0,1…1 и увод вбок −1…1, пока CastAiming.</summary>
        public float CastAimPower { get; private set; }
        public float CastAimSide { get; private set; }

        public bool LiftHeld { get; private set; }
        public bool ReelHeld { get; private set; }
        public bool LiftPressed { get; private set; }
        public bool ContinuePressed { get; private set; }

        /// <summary>
        /// «Подтянуть» держат с самой подсечки: контроллер не пускает такое нажатие в модель, пока кнопку не отпустят.
        /// Кнопка в это время не выглядит нажатой и просит «отпусти». Правило одно — в контроллере
        /// (FishingController.LiftHeldBack); читать после его Update (OnGUI, LateUpdate).
        /// </summary>
        public bool LiftHeldBack => _game != null && _game.LiftHeldBack;

        /// <summary>«Подсачек» нажат в этом кадре (пробел во время вываживания или кнопка «Подсачек»).</summary>
        public bool NetPressed { get; private set; }
        public bool DebugBite { get; private set; }

        /// <summary>Изменение фрикциона в этом кадре, г: шаги по 50 от −/+ и Q/E (с повтором при удержании).</summary>
        public int DragDelta { get; private set; }

        /// <param name="feedback">Звук и вибрация: переключатель «Звук» и клавиша M. Может быть null — тогда без них.</param>
        public void Init(FishingController game, LakeClock clock, FightFeedback feedback)
        {
            _game = game;
            _clock = clock;
            _feedback = feedback;
            _coach = new FightCoach();
            _beginner = PlayerPrefs.GetInt(PrefBeginner, 1) != 0;
            _firstFishCaught = PlayerPrefs.GetInt(PrefFirstFish, 0) != 0;
        }

        /// <summary>
        /// Забирает заброс, если игрок его сделал (свайп вверх или пробел). power 0,1…1 — дальность; aim −1…1 —
        /// куда вбок: −1 и 1 — свайп на полэкрана влево или вправо. В угол его переводит контроллер — по тому,
        /// что видит камера.
        /// </summary>
        public bool TakeCast(out float power, out float aim)
        {
            power = _castPower;
            aim = _castAim;
            if (!_castReady)
                return false;
            _castReady = false;
            return true;
        }

        private void Update()
        {
            if (_game == null)
                return;
            float dt = Time.deltaTime;
            ReadInput();
            _drawing = false;
            Ui();
            _taps.Clear();

            LiftPressed = LiftHeld && !_previousLift;
            _previousLift = LiftHeld;

            UpdateDragRepeat(dt);
        }

        private void LateUpdate()
        {
            if (_game == null || _coach == null)
                return;
            float dt = Time.deltaTime;
            TrackFightState();
            TickFight(dt);
            TickLineCounter(dt);
            if (_toastLeft > 0f)
                _toastLeft -= dt;
            if (_saleTextLeft > 0f)
                _saleTextLeft -= dt;
            TrackKarma(dt);
            TickCatchExit(dt);
            _rigTouched += dt;
            if (_campToastLeft > 0f)
                _campToastLeft -= dt;
            if (_shopNoteLeft > 0f)
                _shopNoteLeft -= dt;
            _shopOpenTime += dt;
            _shopFxTime += dt;
            _boatsOpenTime += dt;
            _gearOpenTime += dt;
            _levelTime += dt;
            _boatFxTime += dt;
            if (_feedback != null)
                _feedback.SetCamp(_campOpen);
            TickCamp(dt);
        }

        private void OnGUI()
        {
            if (_game == null || Event.current.type != EventType.Repaint)
                return;
            EnsureStyles();
            _drawing = true;
            Ui();
        }

        // ---------- Ввод ----------

        private void ReadInput()
        {
            LiftHeld = false;
            ReelHeld = false;
            ContinuePressed = false;
            KeepPressed = false;
            ReleasePressed = false;
            LiveBaitPressed = false;
            RetrievePressed = false;
            _rigDrag = false;
            _speedDrag = false;
            NetPressed = false;
            DebugBite = false;
            _minusHeld = false;
            _plusHeld = false;
            _taps.Clear();

            var state = _game.State;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                LiftHeld |= keyboard.fKey.isPressed;
                ReelHeld |= keyboard.jKey.isPressed;
                _minusHeld |= keyboard.qKey.isPressed;
                _plusHeld |= keyboard.eKey.isPressed;
                if (keyboard.tKey.wasPressedThisFrame) _clock.ToggleFast();
                if (keyboard.nKey.wasPressedThisFrame) _clock.SkipHours(3f);
                if (keyboard.wKey.wasPressedThisFrame) _clock.CycleWind();
                if (keyboard.bKey.wasPressedThisFrame) DebugBite = true;
                if (keyboard.kKey.wasPressedThisFrame) _game.ToggleTestBites();
                if (keyboard.hKey.wasPressedThisFrame) _coach.SetEnabled(!_coach.Enabled);
                if (keyboard.gKey.wasPressedThisFrame) SetBeginner(!_beginner);
                if (keyboard.mKey.wasPressedThisFrame && _feedback != null) _feedback.ToggleMute();
                if (keyboard.rKey.wasPressedThisFrame && (state == LakeState.Idle || state == LakeState.Waiting))
                    ResetHints();
                if (keyboard.enterKey.wasPressedThisFrame) ContinuePressed = true;
                if (keyboard.xKey.wasPressedThisFrame && state == LakeState.Waiting)
                    RetrievePressed = true;
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    // Сначала закрывается открытое окно или карта, потом — турбаза.
                    if (_keepnetOpen || _profileOpen || _mapOpen || _baitOpen || _shopOpen || _boatsOpen || _gearOpen)
                    {
                        _gearOpen = false;
                        _boatsOpen = false;
                        _keepnetOpen = false;
                        _profileOpen = false;
                        _mapOpen = false;
                        _baitOpen = false;
                        _shopOpen = false;
                    }
                    else
                    {
                        _campOpen = false;
                    }
                }
                if (keyboard.spaceKey.wasPressedThisFrame && state == LakeState.Idle && !ModalOpen)
                    QueueCast(0.6f, 0f);
                if (keyboard.spaceKey.wasPressedThisFrame && state == LakeState.Fight)
                    NetPressed = true;
            }

            bool pointerDown = false, pointerUp = false;
            Vector2 pointer = Vector2.zero;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                pointer = ToGui(mouse.position.ReadValue());
                pointerDown = mouse.leftButton.wasPressedThisFrame;
                pointerUp = mouse.leftButton.wasReleasedThisFrame;
                if (mouse.leftButton.isPressed) Hold(pointer);
                if (pointerDown) _taps.Add(pointer);
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

                var primary = touchscreen.primaryTouch;
                if (primary.press.wasPressedThisFrame || primary.press.isPressed || primary.press.wasReleasedThisFrame)
                {
                    pointer = ToGui(primary.position.ReadValue());
                    pointerDown |= primary.press.wasPressedThisFrame;
                    pointerUp |= primary.press.wasReleasedThisFrame;
                }
            }

            Swipe(pointer, pointerDown, pointerUp);

            // Открыт экран садка или профиля: рыбалка ввода не получает.
            if (ModalOpen)
            {
                LiftHeld = false;
                ReelHeld = false;
                _minusHeld = false;
                _plusHeld = false;
            }
        }

        private void Swipe(Vector2 pointer, bool down, bool up)
        {
            CastAiming = false;
            // Палец, начавший на кнопке «Садок» или плашке профиля, забросом не становится.
            if (_game.State != LakeState.Idle || ModalOpen
                || (down && (_keepnetButtonRect.Contains(pointer) || _profileChipRect.Contains(pointer)
                             || _campButtonRect.Contains(pointer) || _mapButtonRect.Contains(pointer)
                             || _rigRect.Contains(pointer) || _baitPillRect.Contains(pointer)
                             || _gearButtonRect.Contains(pointer) || _speedRect.Contains(pointer))))
            {
                _swiping = false;
                return;
            }

            if (down)
            {
                _swiping = true;
                _swipeStart = pointer;
            }

            // Пока палец тянет вверх — прицел: та же сила и тот же увод вбок, что получит заброс при отпускании.
            if (_swiping && !up)
            {
                var drag = pointer - _swipeStart;
                if (-drag.y > 24f * _scale)
                {
                    CastAiming = true;
                    CastAimPower = Mathf.Max(0.1f, Mathf.Clamp01(-drag.y / (Screen.height * 0.45f)));
                    CastAimSide = Mathf.Clamp(drag.x / (Screen.width * 0.5f), -1f, 1f);
                }
            }

            if (up && _swiping)
            {
                _swiping = false;
                var delta = pointer - _swipeStart;
                if (-delta.y > 50f * _scale)
                {
                    float power = Mathf.Clamp01(-delta.y / (Screen.height * 0.45f));
                    float aim = Mathf.Clamp(delta.x / (Screen.width * 0.5f), -1f, 1f);
                    QueueCast(Mathf.Max(0.1f, power), aim);
                }
            }
        }

        private void QueueCast(float power, float aim)
        {
            _castReady = true;
            _castPower = power;
            _castAim = aim;
        }

        private static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

        /// <summary>Палец или мышь держат точку: кнопки удержания (мультитач — каждая отдельно).</summary>
        private void Hold(Vector2 point)
        {
            // Удочка на берегу: палец на столбике спуска тянет крючок.
            if (_game.State == LakeState.Idle && !ModalOpen && _rigRect.Contains(point))
            {
                _rigDrag = true;
                _rigDragY = point.y;
            }

            // Спиннинг: палец на столбике скорости проводки — и на берегу, и пока приманка в воде.
            if (!ModalOpen && _speedRect.Contains(point))
            {
                _speedDrag = true;
                _speedDragY = point.y;
            }

            if (!_game.FloatInWater)
                return;
            if (_liftRect.Contains(point)) LiftHeld = true;
            if (_reelRect.Contains(point)) ReelHeld = true;
            if (_minusRect.Contains(point)) _minusHeld = true;
            if (_plusRect.Contains(point)) _plusHeld = true;
        }

        /// <summary>Фрикцион: шаг сразу при нажатии, при удержании — после паузы 8 шагов/с.</summary>
        private void UpdateDragRepeat(float dt)
        {
            DragDelta = 0;
            int direction = (_plusHeld ? 1 : 0) - (_minusHeld ? 1 : 0);
            if (_game.State != LakeState.Fight)
                direction = 0;

            if (direction == 0)
            {
                _dragDirection = 0;
                return;
            }

            if (direction != _dragDirection)
            {
                _dragDirection = direction;
                _dragRepeat = -DragRepeatDelay;
                DragDelta = direction * DragStepG;
                return;
            }

            _dragRepeat += dt;
            while (_dragRepeat >= DragRepeatInterval)
            {
                _dragRepeat -= DragRepeatInterval;
                DragDelta += direction * DragStepG;
            }
        }

        // ---------- Настройки ----------

        private void SetBeginner(bool on)
        {
            _beginner = on;
            PlayerPrefs.SetInt(PrefBeginner, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Тренер снова учит с нуля, правило первой рыбы снова показывается.</summary>
        private void ResetHints()
        {
            _coach.ResetProgress();
            _firstFishCaught = false;
            PlayerPrefs.SetInt(PrefFirstFish, 0);
            PlayerPrefs.Save();
            _toast = "Подсказки сброшены";
            _toastLeft = 2f;
        }

        // ---------- Экран ----------

        private void Ui()
        {
            var safe = Screen.safeArea;
            _scale = Mathf.Clamp(Mathf.Min(safe.width, safe.height) / 420f, 1f, 4f);
            float s = _scale;
            float pad = 14 * s;
            var area = new Rect(safe.x + pad, Screen.height - safe.yMax + pad, safe.width - 2 * pad, safe.height - 2 * pad);

            // Цели касаний вываживания существуют, только пока их раскладывает экран вываживания.
            _minusRect = default;
            _plusRect = default;
            _netRect = default;

            if (_game.Error != null)
            {
                Pill(new Rect(area.x, area.y, area.width, 120 * s), "Ошибка данных: " + _game.Error, _text);
                return;
            }

            // Турбаза: своя сцена во весь экран со своей верхней строкой; рынок и профиль открываются поверх неё.
            var now = _game.State;
            if (now != LakeState.Idle)
            {
                _campOpen = false;
                _mapOpen = false;
                _baitOpen = false;
                _gearOpen = false;
            }

            if (!_campOpen)
            {
                _shopOpen = false;
                _boatsOpen = false;
            }

            // Карта озера: выбор места. Открывается с причала турбазы или кнопкой «Места» на озере.
            if (_mapOpen)
            {
                MapScreen(area);
                return;
            }

            if (_campOpen)
            {
                CampBackdrop();
                CampLife();
                if (_keepnetOpen)
                    KeepnetPanel(area);
                else if (_profileOpen)
                    ProfilePanel(area);
                else if (_shopOpen)
                    ShopPanel(area);
                else if (_boatsOpen)
                    BoatsPanel(area);
                else
                    Camp(area);
                return;
            }

            TopBar(area);

            // Экран садка: вне вываживания; поклёвка его закрывает сама.
            if (now == LakeState.Bite || now == LakeState.Fight || now == LakeState.Lost)
            {
                _keepnetOpen = false;
                _profileOpen = false;
            }

            // Новый уровень: оповещение поверх озера, как только удочка на берегу.
            if (LevelUpPending)
            {
                _keepnetOpen = false;
                _profileOpen = false;
                _baitOpen = false;
                _gearOpen = false;
                LevelUpPanel(area);
                return;
            }

            if (_keepnetOpen)
            {
                KeepnetPanel(area);
                return;
            }

            if (_profileOpen)
            {
                ProfilePanel(area);
                return;
            }

            if (_baitOpen)
            {
                BaitPanel(area);
                return;
            }

            if (_gearOpen)
            {
                InventoryPanel(area);
                return;
            }

            switch (_game.State)
            {
                case LakeState.Idle:
                    HelpAndSettings(area);
                    if (_game.BaitCount > 0)
                        CenterNote(area, CastAiming ? AimText() : _game.Message ?? "Проведи пальцем вверх — заброс", false);
                    else
                        CenterNote(area, _game.RodType == RodKinds.Spinning
                            ? "Нет приманки для спиннинга — купи в «Снастях»"
                            : "Наживки нет — выбери другую или загляни на турбазу", true);
                    break;
                case LakeState.Casting:
                    HelpAndSettings(area);
                    break;
                case LakeState.Waiting:
                    HelpAndSettings(area);
                    string waiting = _game.RodType == RodKinds.Spinning ? "Веди приманку — держи «Подмотку»"
                        : _game.RodType == RodKinds.Bottom ? "Ждём поклёвку… наживка на дне"
                        : _clock.WindSpeed > 0.5f ? "Ждём поклёвку… поплавок сносит ветром" : "Ждём поклёвку…";
                    CenterNote(area, _game.Message ?? waiting, false);
                    break;
                case LakeState.Bite:
                    CenterNote(area, "Клюёт! Подсекай — «Подтянуть»", true);
                    break;
                case LakeState.Fight:
                    FightTop(area);
                    break;
                case LakeState.Caught:
                    CatchCard(area);
                    return;
                case LakeState.Lost:
                    LostCard(area);
                    return;
            }

            if (_game.FloatInWater)
                Controls(area);
            // Скорость проводки — только у спиннинга: на берегу и пока приманку ведут.
            if ((now == LakeState.Idle || now == LakeState.Waiting) && _game.RodType == RodKinds.Spinning)
                RetrieveSlider(area);
            else
                _speedRect = default;
            if (now == LakeState.Idle || now == LakeState.Casting || now == LakeState.Waiting)
                KeepnetButton(area);
            if (now == LakeState.Idle)
            {
                CampButton(area);
                MapButton(area);
                InventoryButton(area);
                if (_game.RodType == RodKinds.Float)
                {
                    RigWidget(area);
                }
                else
                {
                    _rigRect = default;
                    RigNote(area);
                }

                BaitPill(area);
            }
            else
            {
                _campButtonRect = default;
                _mapButtonRect = default;
                _rigRect = default;
                _baitPillRect = default;
                _gearButtonRect = default;
            }

            // После «В садок» или «Отпустить»: рыба улетает в садок или по воде расходятся круги.
            CatchExit(area);
        }

        private void TopBar(Rect area)
        {
            // Нажатий здесь нет, а ширина плашек зависит от текста — считаем только при рисовании.
            if (!_drawing)
                return;
            float s = _scale;
            float h = 36 * s;
            float gap = 8 * s;
            string right = SpotName(_game.Spot != null ? _game.Spot.id : null);
            // На месте, куда приплыл на арендованной лодке, — сколько минут аренды осталось.
            var profile = _game.Profile;
            bool rented = _game.Spot != null && _game.Spot.needsBoat && profile != null && !profile.OwnsBoat && profile.BoatRentSecondsLeft > 0.0;
            string left = TopLeftText();
            // Левой плашке нужно место под иконку и весь её текст; остаток аренды дописывается справа, только если влезает.
            float leftNeeded = TextWidth(left, _word) + 50 * s;
            if (rented)
            {
                string withRent = right + " · " + RentText(profile.BoatRentSecondsLeft);
                if (TextWidth(withRent, _word) + 32 * s + gap + leftNeeded <= area.width)
                    right = withRent;
            }

            // Правая плашка — по своему тексту (но не шире 45% строки); левая занимает остальное и на неё не наезжает.
            float rw = Mathf.Clamp(TextWidth(right, _word) + 32 * s, 96 * s, area.width * 0.45f);
            float lw = Mathf.Min(300 * s, area.width - rw - gap);
            PillIcon(new Rect(area.x, area.y, lw, h), left, _word, _clock.Night > 0.5f);
            Pill(new Rect(area.xMax - rw, area.y, rw, h), right, _word);
        }

        /// <summary>
        /// Переключатели «Тренер» / «Новичок» / «Звук» — вне вываживания. Клавиши редактора (подсказки на экране нет):
        /// пробел — заброс и подсачек, F/J — кнопки, X — вытащить, Q/E — фрикцион, T — время ×10, N — +3 ч, W — ветер,
        /// B — клёв сейчас, K — тестовый клёв, H — тренер, G — новичок, M — звук, R — сбросить подсказки.
        /// </summary>
        private void HelpAndSettings(Rect area)
        {
            float s = _scale;
            float y = area.y + 46 * s;
            float gap = 8 * s;
            float w = Mathf.Min(118 * s, (area.width - 2 * gap) / 3f);
            float h = 30 * s;
            float x = area.center.x - (3 * w + 2 * gap) / 2;
            var coach = new Rect(x, y, w, h);
            var beginner = new Rect(x + w + gap, y, w, h);
            var sound = new Rect(x + 2 * (w + gap), y, w, h);
            if (Toggle(coach, _coach.Enabled ? "Тренер: вкл" : "Тренер: выкл", _coach.Enabled))
                _coach.SetEnabled(!_coach.Enabled);
            if (Toggle(beginner, _beginner ? "Новичок: вкл" : "Новичок: выкл", _beginner))
                SetBeginner(!_beginner);
            if (_feedback != null && Toggle(sound, _feedback.Muted ? "Звук: выкл" : "Звук: вкл", !_feedback.Muted))
                _feedback.ToggleMute();

            if (_toastLeft > 0f && _toast != null)
                Label(new Rect(area.x, y + h + 4 * s, area.width, 20 * s), _toast, _small, WithAlpha(Amber, Mathf.Clamp01(_toastLeft / 0.4f)));
        }

        /// <summary>
        /// Строка состояния («Ждём поклёвку…», «Клюёт!»): небольшая плашка по ширине текста, по центру, в небе под
        /// верхней строкой и переключателями — поплавок и воду она не закрывает. Поклёвка — крупнее, янтарная, пульсирует.
        /// </summary>
        private void CenterNote(Rect area, string text, bool alert)
        {
            if (!_drawing || string.IsNullOrEmpty(text))
                return;
            float s = _scale;
            var style = alert ? _center : _word;
            float h = (alert ? 44 : 32) * s;
            float w = Mathf.Min(area.width, TextWidth(text, style) + (alert ? 44 : 30) * s);
            var rect = new Rect(area.center.x - w / 2, area.y + 108 * s, w, h);
            if (alert)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
                float grow = (3f + 5f * pulse) * s;
                Round(new Rect(rect.x - grow, rect.y - grow, rect.width + 2 * grow, rect.height + 2 * grow),
                    WithAlpha(Amber, 0.16f + 0.16f * pulse), h / 2 + grow);
                GlassPanel(rect, WithAlpha(Amber, 0.88f + 0.12f * pulse), h / 2);
                Label(rect, text, _center, Dark);
            }
            else
            {
                GlassPanel(rect, Glass, h / 2);
                Rim(rect, GlassBorder, 1.2f * s, h / 2);
                Label(new Rect(rect.x + 15 * s, rect.y, rect.width - 15 * s, rect.height), text, style, Color.white);
            }
        }

        private int _aimTextKey = -1;
        private string _aimText;

        /// <summary>Подпись прицела: дальность будущего заброса.</summary>
        private string AimText()
        {
            int meters = Mathf.RoundToInt(_game.CastDistanceM(CastAimPower));
            if (meters != _aimTextKey || _aimText == null)
            {
                _aimTextKey = meters;
                _aimText = "Заброс на " + meters + " м — отпусти палец";
            }
            return _aimText;
        }

        private string TopLeftText()
        {
            int minutes = Mathf.FloorToInt(_clock.Hour * 60f) % (24 * 60);
            int key = (minutes * 64 + Mathf.RoundToInt(_clock.WindSpeed)) * 2 + (_clock.Fast ? 1 : 0);
            if (key != _topKey || _topLeft == null)
            {
                _topKey = key;
                _topLeft = _clock.ClockText + " · " + _clock.WeatherText + (_clock.Fast ? " · ×10" : "");
            }
            return _topLeft;
        }

        private static string FishName(string id)
        {
            switch (id)
            {
                case "roach": return "Плотва";
                case "crucian": return "Карась";
                case "perch": return "Окунь";
                case "bream": return "Лещ";
                case "zander": return "Судак";
                case "pike": return "Щука";
                default: return id;
            }
        }
    }
}
