using System;
using System.Collections.Generic;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    public enum LakeState
    {
        Idle,
        Casting,
        Waiting,
        Bite,
        Fight,
        Caught,
        Lost,
    }

    /// <summary>
    /// Рыбалка на тестовом озере: заброс → ожидание → поклёвка → подсечка → вываживание → улов.
    /// Логика — в Fishing.Core (BiteModel, FightModel), здесь только связь с вводом и картинкой.
    /// Снасть пока одна: поплавочная удочка, леска 0,12, червь.
    ///
    /// Вываживание: FightModel шагается фиксированными шагами TickSeconds (0,05 с) внутри Update, и после шагов
    /// кадра собирается снимок Frame (FightFrame: плавные значения, значения последнего шага, события кадра,
    /// позиция рыбы в мире). HUD, виды и FightFeedback читают Frame, а не модель.
    ///
    /// Подсечка: «Подтянуть» в первые 0,8 с поклёвки — «Чётко!» (крючок держит на 1,0), до 1,3 с — «Поздно»
    /// (0,75), позже — поклёвка пропущена. Нажатие подсечки в вываживание не идёт: «Подтянуть» доходит до модели
    /// только после того, как кнопку отпустили и нажали снова.
    ///
    /// Вибрации здесь нет: её и звук по State, Frame и Report делает FightFeedback через Haptics.
    /// </summary>
    public sealed class FishingController : MonoBehaviour
    {
        private const float StrikeWindowSeconds = 1.3f;
        private const float CleanStrikeSeconds = 0.8f;
        private const double CleanStrikeHookHold = 1.0;
        private const double LateStrikeHookHold = 0.75;
        private const float StrikeTextSeconds = 1.5f;
        private const int MaxStepsPerFrame = 10;
        private const float CastSeconds = 0.9f;
        private const float WindDriftFactor = 0.012f;

        /// <summary>Заброс вбок не дальше этого угла (на широком экране).</summary>
        private const float MaxCastAngleDeg = 30f;

        /// <summary>Запас от края кадра для бокового заброса: поплавку есть куда сноситься ветром, рыбе — уходить вбок.</summary>
        private const float CastEdgeMarginDeg = 3f;

        /// <summary>Тестовый клёв: ждать в столько раз меньше, но не дольше TestBiteMaxSeconds.</summary>
        private const float TestBiteSpeedup = 6f;
        private const float TestBiteMaxSeconds = 12f;

        private TestLake _lake;
        private LakeClock _clock;
        private LakeHud _hud;
        private Camera _camera;
        private float _baseFov;

        private BiteModel _bites;
        private readonly List<string> _speciesIds = new List<string>();
        private FishDatabase _fish;
        private LocationDatabase _locations;
        private int _rodLengthCm = 400;
        private string _biteBaitId;      // наживка, на которую разыграна поклёвка: она же пишется в улов
        private readonly List<BaitData> _shopBaits = new List<BaitData>();
        private TackleDatabase _tackle;
        private RodData _rodData;
        private ReelData _reelData;
        private LineData _lineData;
        private float _rentSaveIn;       // до следующего сохранения остатка аренды, с
        private int _biteDepthCm;        // глубина наживки там, куда упал поплавок: по ней клюёт рыба, она же пишется в улов
        private FightTackle _fightTackle;
        private float _lineDiameterMm;
        private float _retrieveSpeedMs;
        private SeededRandom _random;

        private CastAimView _aim;
        private RodView _rod;
        private FishingLineView _line;
        private FloatView _float;
        private RippleField _ripples;
        private FishView _fishView;
        private LandingNetView _net;
        private bool _netSwing;     // кнопка подсачека нажата, итог от модели ещё не пришёл

        private Vector3 _castFrom;
        private Vector3 _castTo;
        private bool _hasBite;
        private BiteResult _bite;
        private float _biteIn;
        private double _accumulator;
        private float _messageUntil;
        private string _message;
        private float _strikeTextUntil;
        private string _strikeText;
        private float _lift;
        private bool _netQueued;
        private bool _strikeLiftHeld;
        private readonly double _tick = new FightSettings().TickSeconds;
        private readonly FightFrame _frame = new FightFrame();

        /// <summary>Модель текущего (или последнего) вываживания. Наружу не отдаётся: представление читает Frame.</summary>
        private FightModel _fight;

        public LakeState State { get; private set; }
        public float StateTime { get; private set; }

        /// <summary>
        /// Снимок вываживания на этот кадр (контракт — в FightFrame.cs). Всегда один и тот же объект.
        /// Валиден при State == Fight, в кадре конца вываживания и замороженным при Caught/Lost;
        /// в остальных состояниях не читать.
        /// </summary>
        public FightFrame Frame => _frame;

        /// <summary>Итог последнего вываживания: причина, числа, история натяжения. Null до первой подсечки.</summary>
        public FightReport Report => _fight?.Report;

        /// <summary>
        /// «Подтянуть» держат с самой подсечки: такое нажатие не идёт в модель, пока кнопку не отпустят
        /// (иначе подсечка сразу становилась бы подъёмом удилища). HUD показывает кнопку ненажатой и «отпусти».
        /// Значение этого кадра — после Update контроллера (OnGUI, LateUpdate).
        /// </summary>
        public bool LiftHeldBack => State == LakeState.Fight && _strikeLiftHeld;

        /// <summary>
        /// Качество последней подсечки — с какого HookHold начато вываживание: 1,0 («Чётко!», до 0,8 с от поклёвки)
        /// или 0,75 («Поздно», 0,8–1,3 с). 0 до первой подсечки.
        /// </summary>
        public double StrikeQuality { get; private set; }

        /// <summary>«Чётко!» или «Поздно» в течение 1,5 с после подсечки, иначе null.</summary>
        public string StrikeText => Time.time < _strikeTextUntil ? _strikeText : null;

        public FishSpecies Species { get; private set; }
        public bool Trophy { get; private set; }
        public float FishLengthM => _fishView != null ? _fishView.LengthM : 0f;

        /// <summary>Фрикцион, выставленный игроком, г; уходит в модель следующим шагом.</summary>
        public int DragG { get; private set; }
        public string Error { get; private set; }

        /// <summary>Деньги и садок игрока. Хранятся на устройстве (ProfileStore), правила — в Fishing.Core.</summary>
        public PlayerProfile Profile { get; private set; }

        /// <summary>Водоём игры (пока один): его места ловли — в shared/data/locations.json.</summary>
        public const string LocationId = "lake_start";

        /// <summary>Место ловли сейчас: своя глубина и свой набор рыбы. null — данные не загрузились.</summary>
        public LakeSpot Spot { get; private set; }

        /// <summary>Все места озера — для карты.</summary>
        public LakeSpot[] Spots => _locations != null ? _locations.Get(LocationId).spots : null;

        /// <summary>
        /// Переход на другое место озера. Только пока удочка не заброшена (State == Idle); false — нельзя или места нет.
        /// Озеро перестраивается под место (TestLake.ApplySpot), выбор запоминается в профиле.
        /// </summary>
        public bool SetSpot(string spotId)
        {
            if (State != LakeState.Idle || _locations == null)
                return false;
            var spot = _locations.FindSpot(LocationId, spotId);
            if (spot == null || !SpotOpen(spot))
                return false;
            UseSpot(spot);
            if (Profile != null)
            {
                Profile.SpotId = spot.id;
                ProfileStore.Save(Profile);
            }
            return true;
        }

        /// <summary>Что мешает попасть на место: уровень, лодка или ничего.</summary>
        public SpotAccess AccessTo(LakeSpot spot)
        {
            return Profile == null ? SpotAccess.Open : spot.AccessFor(Profile.Level, Profile.HasBoat);
        }

        /// <summary>Место открыто игроку: уровня хватает, а если место на воде — есть лодка.</summary>
        public bool SpotOpen(LakeSpot spot)
        {
            return spot != null && AccessTo(spot) == SpotAccess.Open;
        }

        private void UseSpot(LakeSpot spot)
        {
            Spot = spot;
            _speciesIds.Clear();
            _speciesIds.AddRange(spot.fish);
            if (_lake != null)
                _lake.ApplySpot(spot.id);
        }

        /// <summary>Глубина в точке озера на текущем месте, см.</summary>
        private float SpotDepthCm(Vector3 point)
        {
            float shore = Mathf.Max(0f, LakeBuilder.ShoreDistance(point.x, point.z));
            return Spot != null ? Spot.DepthCm(shore) : 40f + shore * 18f;
        }

        /// <summary>Спуск поплавка, см: глубина наживки под поплавком. Правила — FloatRig.</summary>
        public int FloatDepthCm { get; private set; } = FloatRig.DefaultDepthCm;

        /// <summary>Наибольший спуск для удилища, см.</summary>
        public int FloatDepthMaxCm => FloatRig.MaxDepthCm(_rodLengthCm);

        // ---------- Снасти (правила — Loadout, TackleRules, PlayerProfile; вещи — shared/data/tackle.json) ----------

        /// <summary>Собранная снасть. null — данные не загрузились.</summary>
        public RodData Rod => _rodData;
        public ReelData Reel => _reelData;
        public LineData Line => _lineData;

        /// <summary>Вид удилища в руках: RodKinds.Float, Bottom или Spinning.</summary>
        public string RodType => _rodData != null ? _rodData.type : RodKinds.Float;

        /// <summary>Все снасти игры — магазину и коробке со снастями.</summary>
        public TackleDatabase TackleData => _tackle;

        /// <summary>
        /// Собирает снасть по тому, что надето в профиле: длина удилища, предел нагрузок, леска, скорость подмотки.
        /// Незнакомый id (вещь убрали из данных) заменяется стартовым. Если наживка не подходит удилищу,
        /// на крючок ставится первая подходящая из коробки.
        /// </summary>
        private void ApplyTackle()
        {
            var set = Profile.Tackle;
            if (!_tackle.TryGetRod(set.RodId, out _rodData))
                _rodData = _tackle.GetRod(TackleRules.StarterRod);
            if (!_tackle.TryGetReel(set.ReelId, out _reelData))
                _reelData = _tackle.GetReel(TackleRules.StarterReel);
            if (!_tackle.TryGetLine(set.LineId, out _lineData))
                _lineData = _tackle.GetLine(TackleRules.StarterLine);
            _rodLengthCm = _rodData.lengthCm;
            _fightTackle = FightTackle.From(_rodData, _reelData, _lineData);
            _lineDiameterMm = _lineData.diameterMm;
            _retrieveSpeedMs = _reelData.retrieveCmPerSec / 100f;
            DragG = _fightTackle.AutoDragG;
            FloatDepthCm = FloatRig.Clamp(FloatDepthCm, _rodLengthCm);
            if (!BaitFitsRod(Profile.Baits.SelectedId))
            {
                foreach (var stock in Profile.Baits.Items)
                {
                    if (stock.count > 0 && BaitFitsRod(stock.id) && Profile.Baits.Select(stock.id))
                        break;
                }
            }
        }

        /// <summary>Наживка подходит удилищу в руках: спиннингу — приманки, поплавочному и донному — натуральная.</summary>
        public bool BaitFitsRod(string baitId)
        {
            return _tackle != null && _tackle.TryGetBait(baitId, out var bait) && TackleRules.BaitFits(bait, _rodData);
        }

        /// <summary>
        /// Надеть купленную вещь: kind 0 — удилище, 1 — катушка, 2 — леска. Только пока удочка на берегу.
        /// false — нельзя, вещь не куплена или это не та вещь.
        /// </summary>
        public bool EquipTackle(int kind, string id)
        {
            if (State != LakeState.Idle || Profile == null || _tackle == null)
                return false;
            bool done = kind == 0 ? _tackle.TryGetRod(id, out _) && Profile.Tackle.EquipRod(id)
                : kind == 1 ? _tackle.TryGetReel(id, out _) && Profile.Tackle.EquipReel(id)
                : _tackle.TryGetLine(id, out _) && Profile.Tackle.EquipLine(id);
            if (!done)
                return false;
            ApplyTackle();
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>Купить удилище, катушку или леску (kind — как в EquipTackle). false — не хватает монет, уровня или уже есть.</summary>
        public bool BuyTackle(int kind, string id)
        {
            if (Profile == null || _tackle == null)
                return false;
            int price, minLevel;
            if (kind == 0 && _tackle.TryGetRod(id, out var rod))
            {
                price = rod.price;
                minLevel = rod.minLevel;
            }
            else if (kind == 1 && _tackle.TryGetReel(id, out var reel))
            {
                price = reel.price;
                minLevel = reel.minLevel;
            }
            else if (kind == 2 && _tackle.TryGetLine(id, out var line))
            {
                price = line.price;
                minLevel = line.minLevel;
            }
            else
            {
                return false;
            }

            if (!Profile.BuyTackle(id, price, minLevel))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>Купить приманку для спиннинга (одна штука навсегда).</summary>
        public bool BuyLure(string baitId)
        {
            if (Profile == null || _tackle == null || !_tackle.TryGetBait(baitId, out var lure) || !Profile.BuyLure(lure))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>Меняет спуск. Только пока удочка на берегу (State == Idle); значение приводится к шагу и пределам.</summary>
        public void SetFloatDepth(int depthCm)
        {
            if (State == LakeState.Idle)
                FloatDepthCm = FloatRig.Clamp(Mathf.Max(FloatRig.MinDepthCm, depthCm), _rodLengthCm);
        }

        /// <summary>Глубина наживки там, где сейчас снасть, м: зависит от вида удилища (TackleRules.BaitDepthCm).</summary>
        public float BaitDepthM => TackleRules.BaitDepthCm(RodType, RunOf(BaitId), FloatDepthCm, SpotDepthCm(_float.Position), RetrievePercent) / 100f;

        /// <summary>Скорость проводки спиннингом, % (LureRetrieve): 10 — медленно и глубоко, 100 — быстро и мелко.</summary>
        public int RetrievePercent { get; private set; } = LureRetrieve.DefaultPercent;

        /// <summary>
        /// Меняет скорость проводки. Можно на берегу и пока приманка в воде. Если от этого заметно изменилась глубина
        /// хода приманки, поклёвка разыгрывается заново — на новой глубине другая рыба.
        /// </summary>
        public void SetRetrievePercent(int percent)
        {
            if (State != LakeState.Idle && State != LakeState.Waiting)
                return;
            int value = LureRetrieve.Clamp(percent);
            if (value == RetrievePercent)
                return;
            RetrievePercent = value;
            if (State != LakeState.Waiting || RodType != RodKinds.Spinning)
                return;
            float depth = TackleRules.BaitDepthCm(RodType, RunOf(BaitId), FloatDepthCm, SpotDepthCm(_float.Position), RetrievePercent);
            if (Mathf.Abs(depth - _biteDepthCm) >= 25f)
                RollBite();
        }

        /// <summary>Ход приманки (LureRuns) по её id; у наживки — null.</summary>
        private string RunOf(string baitId)
        {
            return _tackle != null && _tackle.TryGetBait(baitId, out var bait) ? bait.run : null;
        }

        /// <summary>Наживка лежит на дне: спуск больше глубины под поплавком.</summary>
        public bool BaitOnBottom => TackleRules.OnBottom(RodType, RunOf(BaitId), FloatDepthCm, SpotDepthCm(_float.Position));

        /// <summary>Виды рыб: интерфейсу — для цен в садке.</summary>
        public FishDatabase Fish => _fish;

        /// <summary>Пойманная рыба (State == Caught) влезает в садок.</summary>
        public bool CatchFits => Profile != null && Profile.Keepnet.Fits(_bite.WeightG);

        /// <summary>Чем примечателен улов (State == Caught): рекорд вида, первая рыба вида, самая крупная за всё время.</summary>
        public CatchMarks CatchMarks { get; private set; }

        /// <summary>Опыт за пойманную рыбу (State == Caught): сколько, из чего сложился, поднял ли уровень.</summary>
        public XpGain CatchXp { get; private set; }

        /// <summary>Разряд пойманной рыбы (State == Caught): малёк, обычная, зачётная, трофей.</summary>
        public FishGrade CatchGrade => Species != null ? FishGrades.Of(Species, _bite.WeightG, Trophy) : FishGrade.Regular;

        /// <summary>Пойманная рыба (State == Caught) — малёк: забрать его — минус к карме.</summary>
        public bool CatchIsJuvenile => Species != null && KarmaRules.IsJuvenile(Species, _bite.WeightG);

        /// <summary>Пойманную рыбу (State == Caught) можно сразу пустить на живца: она не тяжелее 100 г.</summary>
        public bool CatchCanBeLiveBait => Profile != null && BaitRules.CanBeLiveBait(_bite.WeightG) && Profile.HasRoomForLiveBait;

        /// <summary>Сколько монет даст пойманная рыба (State == Caught) при продаже.</summary>
        public long CatchPrice => Species != null ? FishPricing.Price(Species, _bite.WeightG, Trophy) : 0;

        public SaleReceipt SellKeepnet()
        {
            if (Profile == null)
                return default;
            var receipt = Profile.SellAll(_fish);
            ProfileStore.Save(Profile);
            return receipt;
        }

        public SaleReceipt SellFish(int fishId)
        {
            if (Profile == null)
                return default;
            var receipt = Profile.Sell(fishId, _fish);
            ProfileStore.Save(Profile);
            return receipt;
        }

        public void ReleaseFish(int fishId)
        {
            if (Profile != null && Profile.Release(fishId))
                ProfileStore.Save(Profile);
        }

        // ---------- Награды за уровни (правила — PlayerProfile.ClaimLevelRewards; таблица — shared/data/levels.json) ----------

        private LevelRewardDatabase _rewards;

        /// <summary>
        /// Оповещение о новом уровне, которое игрок ещё не закрыл: с какого уровня на какой и что получено.
        /// null — показывать нечего. Интерфейс показывает его, когда удочка на берегу, и закрывает через DismissLevelUp.
        /// </summary>
        public RewardReceipt LevelUp { get; private set; }

        /// <summary>Награда за уровень (для подсказки «следующая награда» в профиле); null — такой нет.</summary>
        public LevelReward RewardFor(int level) => _rewards != null ? _rewards.For(level) : null;

        public void DismissLevelUp() => LevelUp = null;

        /// <summary>Выдать награды за набранные уровни и запомнить оповещение. Вызывается после улова и при входе в игру.</summary>
        private void ClaimLevelRewards()
        {
            if (Profile == null || _rewards == null || _tackle == null)
                return;
            var receipt = Profile.ClaimLevelRewards(_rewards, _tackle);
            if (receipt == null)
                return;
            if (LevelUp != null)
                LevelUp.Add(receipt);
            else
                LevelUp = receipt;
        }

        // ---------- Лодка (правила — PlayerProfile; цены — shared/data/transport.json) ----------

        /// <summary>Лодка у причала турбазы: цена покупки, цена и срок аренды. null — данные не загрузились.</summary>
        public BoatData Boat { get; private set; }

        /// <summary>Купить лодку насовсем. false — уже своя или не хватает монет.</summary>
        public bool BuyBoat()
        {
            if (Profile == null || Boat == null || !Profile.BuyBoat(Boat))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>С какого уровня открывается первое место на воде; 0 — на озере таких мест нет.</summary>
        public int BoatUsefulFromLevel => _locations != null ? _locations.Get(LocationId).BoatUsefulFromLevel() : 0;

        /// <summary>
        /// Аренда имеет смысл: уровня игрока хватает хотя бы на одно место на воде. Иначе время аренды
        /// просто сгорит, поэтому её не дают (купить лодку насовсем можно и заранее).
        /// </summary>
        public bool BoatRentMakesSense
        {
            get
            {
                int from = BoatUsefulFromLevel;
                return Profile != null && from > 0 && Profile.Level >= from;
            }
        }

        /// <summary>
        /// Взять лодку в аренду (или продлить её). false — лодка своя, не хватает монет или уровня не хватает
        /// ни на одно место на воде.
        /// </summary>
        public bool RentBoat()
        {
            if (Profile == null || Boat == null || !BoatRentMakesSense || !Profile.RentBoat(Boat))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>
        /// Аренда убывает, пока игра идёт (и на озере, и на турбазе). Остаток сохраняется раз в 20 секунд
        /// и в момент, когда аренда кончилась.
        /// </summary>
        private void TickBoatRent(float dt)
        {
            if (Profile == null || Profile.BoatRentSecondsLeft <= 0.0)
                return;
            bool ended = Profile.TickBoatRent(dt);
            _rentSaveIn -= dt;
            if (ended || _rentSaveIn <= 0f)
            {
                _rentSaveIn = 20f;
                ProfileStore.Save(Profile);
            }
        }

        /// <summary>
        /// Место закрылось под игроком (кончилась аренда лодки): как только удочка на берегу, он возвращается
        /// на первое открытое место.
        /// </summary>
        private void LeaveClosedSpot()
        {
            if (Spot == null || _locations == null || SpotOpen(Spot))
                return;
            foreach (var spot in _locations.Get(LocationId).spots)
            {
                if (!SpotOpen(spot))
                    continue;
                UseSpot(spot);
                Profile.SpotId = spot.id;
                ProfileStore.Save(Profile);
                ShowMessage("Аренда лодки закончилась — ты снова на берегу");
                return;
            }
        }

        // ---------- Наживка (правила — BaitBox, BaitRules, PlayerProfile) ----------

        /// <summary>Наживка на крючке (id из tackle.json); null — не выбрана.</summary>
        public string BaitId => Profile != null ? Profile.Baits.SelectedId : null;

        /// <summary>
        /// Сколько штук выбранной наживки осталось (0 — и если она не подходит удилищу). Одна поклёвка съедает одну
        /// штуку расходной наживки; искусственная приманка не расходуется.
        /// </summary>
        public int BaitCount => Profile != null && BaitFitsRod(Profile.Baits.SelectedId) ? Profile.Baits.SelectedCount : 0;

        /// <summary>Что продаёт магазин турбазы: расходная наживка для поплавочной удочки, в порядке tackle.json.</summary>
        public IReadOnlyList<BaitData> ShopBaits => _shopBaits;

        /// <summary>Сменить наживку. Только пока удочка на берегу (State == Idle); false — нельзя или такой нет.</summary>
        public bool SelectBait(string baitId)
        {
            if (State != LakeState.Idle || Profile == null || !BaitFitsRod(baitId) || !Profile.Baits.Select(baitId))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>Купить упаковку наживки на турбазе. false — не хватает монет или коробка полна.</summary>
        public bool BuyBait(string baitId)
        {
            if (Profile == null || _tackle == null || !_tackle.TryGetBait(baitId, out var bait) || !Profile.BuyBait(bait))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        public bool DigWorms()
        {
            if (Profile == null || !Profile.DigWorms())
                return false;
            ProfileStore.Save(Profile);
            return true;
        }

        /// <summary>Пустить мелкую рыбу из садка на живца.</summary>
        public bool MakeLiveBait(int fishId)
        {
            if (Profile == null || !Profile.MakeLiveBait(fishId))
                return false;
            ProfileStore.Save(Profile);
            return true;
        }
        public string Message => Time.time < _messageUntil ? _message : null;
        public float FloatDistanceM => Flat(_float.Position).magnitude;
        public float DepthAtFloatM => SpotDepthCm(_float.Position) / 100f;
        public bool FloatInWater => State == LakeState.Waiting || State == LakeState.Bite || State == LakeState.Fight;
        public FightTackle Tackle => _fightTackle;

        /// <summary>Тестовый режим частого клёва (клавиша K переключает на настоящий темп).</summary>
        public bool TestBites { get; private set; } = true;

        public void ToggleTestBites()
        {
            TestBites = !TestBites;
            if (State == LakeState.Waiting)
                RollBite();
            ShowMessage(TestBites ? "Тестовый клёв: часто" : "Клёв в настоящем темпе");
        }

        public void Init(TestLake lake, LakeClock clock, LakeHud hud)
        {
            _lake = lake;
            _clock = clock;
            _hud = hud;
            _camera = Camera.main;
            // Исходный угол обзора (TestLake настроил камеру до Init). Во время вываживания его меняет CameraRig,
            // а к забросу камера ещё может возвращаться, поэтому заброс считается по исходному.
            _baseFov = _camera != null ? _camera.fieldOfView : 55f;
            _random = new SeededRandom((uint)Environment.TickCount);

            try
            {
                _fish = SharedData.LoadFish();
                var tackle = SharedData.LoadTackle();
                _tackle = tackle;
                _shopBaits.Clear();
                foreach (var bait in tackle.Baits)
                {
                    if (bait.consumable && bait.rodType == "float")
                        _shopBaits.Add(bait);
                }
                _locations = SharedData.LoadLocations(_fish);
                Boat = SharedData.LoadTransport().Boats[0];
                _bites = new BiteModel(_fish);
                Profile = ProfileStore.Load(_fish);
                FloatDepthCm = Profile.FloatDepthCm;
                RetrievePercent = LureRetrieve.Clamp(Profile.RetrievePercent);
                ApplyTackle();
                // Награды за уровни, набранные до того, как награды появились, — сразу при входе.
                _rewards = SharedData.LoadLevelRewards(tackle);
                ClaimLevelRewards();
                if (LevelUp != null)
                    ProfileStore.Save(Profile);
                // Место — то, где игрок был в прошлый раз; иначе первое в списке.
                var spots = _locations.Get(LocationId).spots;
                var start = _locations.FindSpot(LocationId, Profile.SpotId);
                if (!SpotOpen(start))
                {
                    // Места нет или оно ещё закрыто по уровню: первое открытое (такое есть всегда — проверяет LocationDatabase).
                    start = spots[0];
                    foreach (var spot in spots)
                    {
                        if (SpotOpen(spot))
                        {
                            start = spot;
                            break;
                        }
                    }
                }

                UseSpot(start);
            }
            catch (Exception e)
            {
                Error = e.Message;
                return;
            }

            _rod = new RodView(transform, lake.LineMaterial);
            _aim = new CastAimView(transform, lake.LineMaterial);
            _line = new FishingLineView(transform, lake.LineMaterial);
            _float = new FloatView(transform, lake);
            _ripples = new RippleField(transform, lake.LineMaterial);
            _fishView = new FishView(transform, lake);
            _net = new LandingNetView(transform, lake.LineMaterial);
            _line.SetVisible(false);
        }

        private void Update()
        {
            if (Error != null || _camera == null)
                return;

            float dt = Time.deltaTime;
            StateTime += dt;
            TickBoatRent(dt);
            DragG = Mathf.Clamp(DragG + _hud.DragDelta, 0, _fightTackle.ReelMaxDragG);
            _frame.BeginFrame();

            switch (State)
            {
                case LakeState.Idle: UpdateIdle(); break;
                case LakeState.Casting: UpdateCasting(); break;
                case LakeState.Waiting: UpdateWaiting(dt); break;
                case LakeState.Bite: UpdateBite(); break;
                case LakeState.Fight: UpdateFight(dt); break;
                case LakeState.Caught:
                    // «В садок» — только если рыба влезает; иначе её остаётся отпустить или освободить место в садке.
                    if (_hud.KeepPressed && CatchFits)
                    {
                        Profile.Keep(Species, _bite.WeightG, Mathf.RoundToInt(FishLengthM * 100f), Trophy,
                            Mathf.FloorToInt(_clock.Hour * 60f) % (24 * 60),
                            new CatchContext(Spot != null ? Spot.id : null, _biteBaitId, _biteDepthCm, _rodData.id, _lineData.id));
                        ProfileStore.Save(Profile);
                        SetState(LakeState.Idle);
                    }
                    else if (_hud.LiveBaitPressed && CatchCanBeLiveBait)
                    {
                        Profile.MakeLiveBaitFromCatch(Species, _bite.WeightG);
                        ProfileStore.Save(Profile);
                        SetState(LakeState.Idle);
                    }
                    else if (_hud.ReleasePressed)
                    {
                        if (Profile != null)
                        {
                            Profile.ReleaseCatch(Species, _bite.WeightG, Trophy);
                            ProfileStore.Save(Profile);
                        }
                        SetState(LakeState.Idle);
                    }
                    break;
                case LakeState.Lost:
                    if (_hud.ContinuePressed)
                        SetState(LakeState.Idle);
                    break;
            }

            UpdateViews(dt);
        }

        private void UpdateIdle()
        {
            LeaveClosedSpot();
            if (!_hud.TakeCast(out float power, out float aim))
                return;
            // Без наживки не забросить: её выбирают в коробке или покупают на турбазе.
            if (BaitCount <= 0)
                return;
            // Спуск мог поменяться, пока удочка была на берегу: запоминаем его перед забросом.
            if (Profile != null && (Profile.FloatDepthCm != FloatDepthCm || Profile.RetrievePercent != RetrievePercent))
            {
                Profile.FloatDepthCm = FloatDepthCm;
                Profile.RetrievePercent = RetrievePercent;
                ProfileStore.Save(Profile);
            }

            _castFrom = _rod.Tip;
            _castTo = CastTarget(power, aim);
            SetState(LakeState.Casting);
        }

        /// <summary>Самый короткий и самый дальний заброс, м: короткий свайп кладёт поплавок под самый берег.</summary>
        public const float MinCastM = 2f;

        /// <summary>Самый дальний заброс удилищем в руках, м.</summary>
        public float MaxCastM => TackleRules.MaxCastM(_rodData);

        /// <summary>Дальность заброса для силы свайпа 0,1…1, м: зависит от удилища.</summary>
        public float CastDistanceM(float power) => Mathf.Lerp(MinCastM, MaxCastM, Mathf.InverseLerp(0.1f, 1f, power));

        /// <summary>Куда упадёт поплавок: сила — дальность, aim −1…1 — вбок в пределах кадра. Одна формула для прицела и заброса.</summary>
        private Vector3 CastTarget(float power, float aim)
        {
            float angle = aim * MaxCastSideAngle();
            var target = Quaternion.Euler(0, angle, 0) * Vector3.forward * CastDistanceM(power);
            target.z = Mathf.Max(target.z, LakeBuilder.NearShoreZ(target.x) + 2f);
            return new Vector3(target.x, 0, target.z);
        }

        /// <summary>
        /// Наибольший угол заброса вбок, град.: половина горизонтального обзора исходной камеры минус запас 3°,
        /// но не больше 30°. Камера стоит на месте (повернуть её за поплавком может только вываживание, и то на
        /// ±8°), а на телефоне стоя по горизонтали видно лишь ±13°: заброс на ±30° унёс бы поплавок за край
        /// кадра ещё до поклёвки. Весь свайп (на полэкрана вбок) теперь — от края до края видимой воды.
        /// </summary>
        private float MaxCastSideAngle()
        {
            float halfVertical = _baseFov * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * Mathf.Max(0.1f, _camera.aspect)) * Mathf.Rad2Deg;
            return Mathf.Clamp(halfHorizontal - CastEdgeMarginDeg, 0f, MaxCastAngleDeg);
        }

        private void UpdateCasting()
        {
            float t = Mathf.Clamp01(StateTime / CastSeconds);
            float height = 2f + Vector3.Distance(_castFrom, _castTo) * 0.22f;
            var apex = Vector3.Lerp(_castFrom, _castTo, 0.5f) + Vector3.up * height;
            _float.Position = Vector3.Lerp(Vector3.Lerp(_castFrom, apex, t), Vector3.Lerp(apex, _castTo, t), t);
            if (t < 1f)
                return;

            _float.Position = _castTo;
            _ripples.Spawn(_castTo, 0.9f, 1.4f);
            _ripples.Spawn(_castTo, 0.5f, 0.9f);
            RollBite();
            SetState(LakeState.Waiting);
        }

        private void UpdateWaiting(float dt)
        {
            // «Вытащить»: удочка сразу на берегу — можно поменять спуск или место.
            if (_hud.RetrievePressed)
            {
                SetState(LakeState.Idle);
                return;
            }

            var position = _float.Position + _clock.Wind3 * (WindDriftFactor * dt);
            // Спиннинг: приманка идёт со скоростью проводки, выставленной ползунком.
            bool spinning = RodType == RodKinds.Spinning;
            float pace = spinning ? LureRetrieve.SpeedFactor(RetrievePercent) : 1f;
            if (_hud.ReelHeld)
                position = Vector3.MoveTowards(position, new Vector3(0, 0, 0.5f), _retrieveSpeedMs * pace * dt);
            if (LakeBuilder.ShoreDistance(position.x, position.z) > 1f)
                _float.Position = new Vector3(position.x, 0, position.z);

            if (_hud.ReelHeld && FloatDistanceM < 2.5f)
            {
                SetState(LakeState.Idle);
                return;
            }

            if (_hud.LiftPressed)
            {
                ShowMessage("Рано! Рыба насторожилась");
                _biteIn += 5f;
            }

            if (_hud.DebugBite)
                _biteIn = 0f;

            // Спиннинг: рыба берёт приманку только на ходу — время до поклёвки идёт, пока крутят катушку,
            // и тем быстрее, чем быстрее проводка (приманка проходит больше воды).
            if (!spinning)
                _biteIn -= dt;
            else if (_hud.ReelHeld || _hud.DebugBite)
                _biteIn -= dt * pace;
            if (_hasBite && _biteIn <= 0f)
            {
                _ripples.Spawn(_float.Position, 0.5f, 0.8f);
                // Рыба взяла наживку: подсёк или прозевал — одной штуки в коробке больше нет.
                // Искусственная приманка не расходуется.
                bool consumable = !_tackle.TryGetBait(_biteBaitId, out var used) || used.consumable;
                if (consumable && Profile != null && Profile.Baits.UseSelected())
                    ProfileStore.Save(Profile);
                SetState(LakeState.Bite);
            }
        }

        /// <summary>Подсечка: время от поклёвки до нажатия «Подтянуть» задаёт, как держит крючок.</summary>
        private void UpdateBite()
        {
            if (StateTime > StrikeWindowSeconds)
            {
                // Наживку рыба съела. Осталась ещё — насаживаем и ждём дальше; нет — удочка на берег.
                if (BaitCount <= 0)
                {
                    SetState(LakeState.Idle);
                    return;
                }

                ShowMessage("Поклёвку пропустил — жди следующую");
                RollBite();
                SetState(LakeState.Waiting);
                return;
            }

            if (_hud.LiftPressed)
                StartFight(StateTime);
        }

        private void StartFight(float reactionSeconds)
        {
            bool clean = reactionSeconds <= CleanStrikeSeconds;
            StrikeQuality = clean ? CleanStrikeHookHold : LateStrikeHookHold;
            _strikeText = clean ? "Чётко!" : "Поздно";
            _strikeTextUntil = Time.time + StrikeTextSeconds;

            Species = _fish.Get(_bite.SpeciesId);
            Trophy = _bite.IsTrophy;
            // Зерно косметики (боковой уход рыбы): в физику не входит, поэтому берётся из общего генератора озера.
            uint visualSeed = _random.NextUInt();
            _fight = new FightModel(Species, _bite.WeightG, _fightTackle, FloatDistanceM * 100.0, _random, null, 0,
                StrikeQuality, visualSeed);
            DragG = _fightTackle.AutoDragG;
            _fishView.Setup(Species.id, _bite.WeightG);
            _accumulator = 0;
            _netQueued = false;
            _strikeLiftHeld = true;
            _frame.Start(_fight, _fightTackle, Flat(_float.Position), _tick);
            SetState(LakeState.Fight);
        }

        /// <summary>
        /// Шаги модели за кадр и сборка снимка. Ловит рыбу сама модель: «Подтянуть» при CanSwing — выудить,
        /// «Подсачек» при CanNet. Нажатие подсачека — фронт: уходит в модель ровно одним шагом, даже если
        /// в этом кадре шагов не было. «Подтянуть» подсечки не доходит до модели, пока его не отпустят.
        /// </summary>
        private void UpdateFight(float dt)
        {
            _netQueued |= _hud.NetPressed;
            // Подсачек в кадре машет по кнопке — но удачно или мимо, решает модель (см. ниже, после её шага).
            _netSwing |= _hud.NetPressed && _frame.NetShown;
            bool liftHeld = _hud.LiftHeld;
            if (!liftHeld)
                _strikeLiftHeld = false;

            var input = new FightInput { Reel = _hud.ReelHeld, Lift = liftHeld && !_strikeLiftHeld, DragG = DragG };
            _accumulator += dt;
            int steps = 0;
            while (_accumulator >= _tick && steps < MaxStepsPerFrame && _fight.Outcome == FightOutcome.InProgress)
            {
                input.Net = _netQueued;
                _netQueued = false;
                _fight.Step(input);
                _frame.AddTick(_fight);
                _accumulator -= _tick;
                steps++;
            }

            // После зависания не догоняем: лишние шаги отбрасываются, остаётся доля шага для интерполяции.
            if (_accumulator >= _tick)
                _accumulator -= Math.Floor(_accumulator / _tick) * _tick;

            // Нажатие подсачека дошло до модели: взяла рыбу — подсачек подныривает и поднимает её,
            // нет — шлёпает по воде (рыбу это пугает, см. FightModel).
            if (_netSwing && !_netQueued)
            {
                _netSwing = false;
                bool taken = _fight.Outcome == FightOutcome.Landed;
                _net.Scoop(taken);
                if (!taken)
                    _ripples.Spawn(_float.Position, 0.7f, 0.6f, 0.6f, 0.03f);
            }

            if (_fight.Outcome == FightOutcome.InProgress)
            {
                _frame.Compose((float)(_accumulator / _tick));
                return;
            }

            _frame.End();
            // Итог — в профиль: пойманная рыба идёт в статистику сразу (оставят её или отпустят — решится на карточке),
            // сход — в статистику сходов; обрыв лески роняет карму.
            bool landed = _fight.Outcome == FightOutcome.Landed;
            CatchMarks = default;
            if (Profile != null)
            {
                if (landed)
                {
                    CatchMarks = Profile.RecordCatch(Species, _bite.WeightG, Mathf.RoundToInt(FishLengthM * 100f), Trophy);
                    CatchXp = Profile.LastXpGain;
                    if (CatchXp.LeveledUp)
                        ClaimLevelRewards();
                }
                else
                    Profile.RecordLoss(_fight.Outcome);
                ProfileStore.Save(Profile);
            }

            SetState(landed ? LakeState.Caught : LakeState.Lost);
        }

        private void RollBite()
        {
            _biteBaitId = BaitId;
            _biteDepthCm = Mathf.RoundToInt(TackleRules.BaitDepthCm(RodType, RunOf(_biteBaitId), FloatDepthCm, SpotDepthCm(_float.Position), RetrievePercent));
            var conditions = new BiteConditions
            {
                Hour = _clock.Hour,
                DepthCm = _biteDepthCm,
                BaitId = _biteBaitId,
                LineDiameterMm = _lineDiameterMm,
            };
            _hasBite = _bites.TryRollBite(_speciesIds, conditions, _random, out _bite);
            if (!_hasBite)
                _biteIn = float.MaxValue;
            else if (TestBites)
                _biteIn = Mathf.Min((float)_bite.SecondsToBite / TestBiteSpeedup, TestBiteMaxSeconds);
            else
                _biteIn = (float)_bite.SecondsToBite;
        }

        // ---------------------------------------------------------------------------------------------
        // Виды (docs/specs/fight-v2.md, §4). На вываживании читают только Frame, модель — никогда.
        // Разовые эффекты (всплески, «стук» рывка, выскакивающий поплавок) — по покадровым событиям
        // Frame.Events, дуга подсечки — по самой подсечке (смене State на Fight), непрерывные — по плавным
        // и дискретным полям.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Изгиб удилища: 0,05 + 0,75·sqrt(нагрузка / предел), сглаживание 0,08 с.</summary>
        private const float RodRestBend = 0.05f;
        private const float RodLoadBend = 0.75f;
        private const float RodBendSmoothSeconds = 0.08f;

        /// <summary>Дуга на подсечке (с кадра подсечки): изгиб держится 0,12 с и спадает за 0,45 с.</summary>
        private const float HoopBend = 0.85f;
        private const float HoopHoldSeconds = 0.12f;
        private const float HoopFadeSeconds = 0.45f;

        /// <summary>Телеграф: два кивка по 4° (уход, вглубь, бросок), стук 6° на 0,6 (рывок), подъём перед свечкой.</summary>
        private const float TelegraphDipDeg = 4f;
        private const float KnockDeg = 6f;
        private const float KnockAtProgress = 0.6f;
        private const float KnockAttackSeconds = 0.03f;
        private const float KnockFadeSeconds = 0.15f;
        private const float JumpRiseDeg = 5f;
        private const float JumpBendDrop = 0.5f;
        private const float RodPitchSmoothSeconds = 0.04f;

        /// <summary>Усы: кольца каждые max(0,1; 0,5 − |v|/250) с, радиус 0,2 + |v|/150 м, ±0,3 м позади поплавка.</summary>
        private const float WakeMinSpeedCmPerSec = 5f;
        private const float WakeSideM = 0.3f;
        private const float WakeBehindM = 0.2f;
        private const float WakeSeconds = 0.9f;
        private const float TakingLineCmPerSec = -40f;
        private const float BurstEverySeconds = 0.25f;
        private const float FishVelocitySmoothSeconds = 0.15f;

        private bool _viewsInFight;
        private float _rodBend = RodRestBend;
        private float _rodPitch;
        private float _hoopAge = 1e6f;
        private int _tellTick = -1;
        private float _tellPrevious;
        private float _tellCurrent;
        private bool _knockArmed;
        private bool _knockDone;
        private float _knockAge = 1e6f;
        private float _jumpRise;
        private Vector3 _lastFishWorld;
        private Vector3 _fishVelocity;
        private float _wakeIn;
        private float _burstIn;
        private bool _takingLine;

        // Волна от рыбы, когда её подтягивают удилищем: рыбу тащит к берегу, и вода перед ней вздувается.
        private const float LiftWaveMinRate = 0.25f;     // подъём удилища, долей хода в секунду, с которого идёт волна
        private const float LiftWaveFullRate = 1.4f;     // подъём, при котором волна самая сильная
        private const float LiftWaveEverySeconds = 0.16f;
        private const float LiftWaveMinTensionG = 60f;   // на провисшей леске рыбу не тащит — волны нет
        private float _liftAngle;
        private float _liftWaveIn;
        private bool _lifting;

        private void UpdateViews(float dt)
        {
            bool fight = State == LakeState.Fight;
            var frame = _frame;
            if (fight && !_viewsInFight)
                ResetFightViews(frame);
            _viewsInFight = fight;

            if (fight)
            {
                _float.Position = frame.FishWorldPosition;
                TrackFish(frame, dt);
            }

            // Дуга подсечки стартует в кадре подсечки (ResetFightViews) — вместе со звуком и вибрацией FightFeedback,
            // а не по HookSet первого шага, который приходит до 50 мс позже.
            _hoopAge = Mathf.Min(_hoopAge + dt, 1e6f);
            UpdateTelegraph(frame, fight, dt);

            // Прицел заброса: палец тянет вверх — удилище отводится назад и смотрит туда, куда полетит поплавок.
            bool aiming = State == LakeState.Idle && _hud.CastAiming && _aim != null;
            var aimTarget = aiming ? CastTarget(_hud.CastAimPower, _hud.CastAimSide) : Vector3.zero;

            float targetLift = fight ? frame.RodAngle : aiming ? 0.3f + 0.6f * _hud.CastAimPower : _hud.LiftHeld ? 0.5f : 0f;
            _lift = Mathf.MoveTowards(_lift, targetLift, dt * 3f);

            // Удилище: изгиб по нагрузке (поклёвка больше не гнёт — только вываживание), дуга подсечки.
            float bend = RodRestBend;
            if (fight)
                bend += RodLoadBend * Mathf.Sqrt(Mathf.Clamp01(frame.RodLoadShare));
            bend *= 1f - JumpBendDrop * _jumpRise;
            bend = Mathf.Max(bend, HoopBend * HoopEnvelope(_hoopAge));
            _rodBend += (bend - _rodBend) * (1f - Mathf.Exp(-dt / RodBendSmoothSeconds));
            var rodTarget = aiming ? aimTarget
                : State == LakeState.Idle || State == LakeState.Caught || State == LakeState.Lost
                    ? new Vector3(0.6f, 0, 12f)
                    : _float.Position;
            _rod.Update(_camera, rodTarget, _lift, _rodBend, _rodPitch - KnockDeg * KnockEnvelope(_knockAge));
            frame.SetRodTip(_rod.Tip);
            if (_aim != null)
            {
                if (aiming)
                    _aim.Show(_rod.Tip, aimTarget, _camera.transform.position, Time.time);
                else
                    _aim.Hide();
            }

            // Подсачек: вынут, пока рыба в его зоне (кнопка «Подсачек» на экране); взмах доигрывается и после поимки.
            _net.Update(_camera, _float.Position, fight && frame.NetShown, fight && frame.CanNet, dt, Time.time);

            // Поплавок: на вываживании на поверхности над рыбой.
            // Поплавок есть только у поплавочной удочки: у донки и спиннинга в воду уходит одна леска.
            _float.SetVisible((State == LakeState.Casting || FloatInWater) && RodType == RodKinds.Float);
            if (fight)
            {
                float tensionShare = frame.FishForceG > 0f ? frame.TensionG / frame.FishForceG : 0f;
                bool underwater = frame.Phase == FishPhase.Dive || frame.Phase == FishPhase.Jerk;
                bool pop = (frame.Events & FightEvents.PhaseStart) != 0 && frame.Phase == FishPhase.Rest;
                _float.UpdateTowed(_camera, dt, Time.time, _clock.WindSpeed, _rod.Tip, tensionShare, underwater, pop);
            }
            else
            {
                _float.UpdateFree(_camera, dt, Time.time, _clock.WindSpeed, State == LakeState.Bite ? 1f : 0f);
            }

            // Леска: провис от слабины (60 см — лежит на воде), яркость от пикового натяжения к слабому звену.
            bool lineVisible = State == LakeState.Casting || FloatInWater;
            _line.SetVisible(lineVisible);
            if (lineVisible)
            {
                float sag = fight
                    ? Mathf.Clamp01(frame.SlackCm / 60f)
                    : State == LakeState.Casting ? 0.2f : 0.55f;
                float load = fight ? Mathf.Clamp01(frame.LoadShare) : 0.45f;
                _line.Update(_camera, _rod.Tip, _float.LineAttach, sag, load);
            }

            if (fight)
            {
                _fishView.Splashes(frame, _ripples);
                _fishView.UpdateFight(frame, _fishVelocity, _camera, _ripples, dt, Time.time);
                UpdateWake(frame, dt);
                UpdateLiftWave(frame, dt);
            }
            else
            {
                if (frame.EndedThisFrame)
                    _fishView.Splashes(frame, _ripples);
                if (State == LakeState.Caught)
                    _fishView.UpdateShowcase(_camera, Time.time);
                else
                    _fishView.Hide();
            }

            _ripples.Update(dt, _camera);
        }

        /// <summary>Первый кадр вываживания (кадр подсечки): сброс того, что виды копят между кадрами, и дуга подсечки.</summary>
        private void ResetFightViews(FightFrame frame)
        {
            _hoopAge = 0f;
            _tellTick = -1;
            _tellPrevious = 0f;
            _tellCurrent = 0f;
            _knockArmed = false;
            _knockDone = false;
            _knockAge = 1e6f;
            _jumpRise = 0f;
            _lastFishWorld = frame.FishWorldPosition;
            _fishVelocity = Vector3.zero;
            _wakeIn = 0f;
            _burstIn = 0f;
            _takingLine = false;
            _liftAngle = frame.RodAngle;
            _liftWaveIn = 0f;
            _lifting = false;
            _netSwing = false;
            _net.Hide();
        }

        /// <summary>Скорость точки рыбы по воде, м/с: по изменению плавной позиции, сглажена.</summary>
        private void TrackFish(FightFrame frame, float dt)
        {
            var position = frame.FishWorldPosition;
            if (dt > 1e-5f)
            {
                var velocity = (position - _lastFishWorld) / dt;
                _fishVelocity += (velocity - _fishVelocity) * (1f - Mathf.Exp(-dt / FishVelocitySmoothSeconds));
            }
            _lastFishWorld = position;
        }

        /// <summary>
        /// Телеграф на удилище. TelegraphProgress меняется ступенькой раз в шаг — здесь он интерполируется
        /// между двумя последними шагами на Frame.Alpha (как плавные поля снимка): кивки идут без ступенек.
        /// </summary>
        private void UpdateTelegraph(FightFrame frame, bool fight, float dt)
        {
            _knockAge = Mathf.Min(_knockAge + dt, 1e6f);
            float dip = 0f;
            float riseTarget = 0f;
            if (fight)
            {
                if (frame.Tick != _tellTick)
                {
                    float current = frame.TelegraphProgress;
                    int ticks = Mathf.Max(1, frame.TicksThisFrame);
                    // Новый телеграф или его конец (прогресс упал) — без интерполяции назад.
                    _tellPrevious = current < _tellCurrent ? current : current - (current - _tellCurrent) / ticks;
                    _tellCurrent = current;
                    _tellTick = frame.Tick;
                }
                float progress = Mathf.Lerp(_tellPrevious, _tellCurrent, frame.Alpha);

                // Стук — ровно один на рывок: на 0,6 его телеграфа, а если телеграф пролетел за один кадр
                // (после зависания) — в начале самого рывка. Начало фазы проверяется до взвода: у короткого
                // рывка телеграф следующей паузы стартует тем же шагом, что и рывок.
                var events = frame.Events;
                if ((events & FightEvents.PhaseStart) != 0)
                {
                    if (frame.Phase == FishPhase.Jerk && !_knockDone)
                        Knock();
                    _knockDone = false;
                    _knockArmed = false;
                }
                if ((events & FightEvents.TelegraphStart) != 0)
                    _knockArmed = frame.NextPhase == FishPhase.Jerk;
                if (progress > 0f)
                {
                    switch (frame.NextPhase)
                    {
                        case FishPhase.Run:
                        case FishPhase.Dive:
                        case FishPhase.Surge:
                            float wave = Mathf.Sin(2f * Mathf.PI * progress);
                            dip = -TelegraphDipDeg * wave * wave;
                            break;
                        case FishPhase.Jerk:
                            if (_knockArmed && progress >= KnockAtProgress)
                                Knock();
                            break;
                        case FishPhase.Jump:
                            riseTarget = progress;
                            break;
                    }
                }
                if (frame.Phase == FishPhase.Jump)
                    riseTarget = 1f;
            }

            _jumpRise = Mathf.MoveTowards(_jumpRise, riseTarget, dt * 4f);
            float pitch = dip + JumpRiseDeg * _jumpRise;
            _rodPitch += (pitch - _rodPitch) * (1f - Mathf.Exp(-dt / RodPitchSmoothSeconds));
        }

        private void Knock()
        {
            _knockArmed = false;
            _knockDone = true;
            _knockAge = 0f;
        }

        /// <summary>
        /// Усы за поплавком: два кольца по бокам позади него (вместе — «V»), чаще и шире с ростом скорости.
        /// Когда рыба забирает леску быстрее 40 см/с — ещё и всплеск колец у поплавка.
        /// </summary>
        private void UpdateWake(FightFrame frame, float dt)
        {
            float speed = frame.FishSpeedCmPerSec;
            float absSpeed = Mathf.Abs(speed);
            var at = _float.Position;

            var move = _fishVelocity;
            move.y = 0f;
            if (move.sqrMagnitude < 0.0025f)
                move = speed >= 0f ? -frame.Direction : frame.Direction;
            move.Normalize();
            var side = Vector3.Cross(Vector3.up, move);

            _wakeIn -= dt;
            if (absSpeed >= WakeMinSpeedCmPerSec && _wakeIn <= 0f)
            {
                float radius = 0.2f + absSpeed / 150f;
                var behind = at - move * WakeBehindM;
                _ripples.Spawn(behind + side * WakeSideM, radius, WakeSeconds, 0.4f, 0.02f);
                _ripples.Spawn(behind - side * WakeSideM, radius, WakeSeconds, 0.4f, 0.02f);
                _wakeIn = Mathf.Max(0.1f, 0.5f - absSpeed / 250f);
            }

            bool taking = speed < TakingLineCmPerSec;
            if (taking && !_takingLine)
            {
                float grow = 1f + absSpeed / 200f;
                _ripples.Spawn(at, 0.5f * grow, 0.7f, 0.6f, 0.025f);
                _ripples.Spawn(at, 0.8f * grow, 0.9f, 0.5f, 0.025f);
                _ripples.Spawn(at, 1.1f * grow, 1.1f, 0.4f, 0.025f);
                _burstIn = BurstEverySeconds;
            }
            else if (taking)
            {
                _burstIn -= dt;
                if (_burstIn <= 0f)
                {
                    _ripples.Spawn(at, 0.4f + absSpeed / 120f, 0.7f, 0.5f, 0.025f);
                    _burstIn = BurstEverySeconds;
                }
            }
            _takingLine = taking;
        }

        /// <summary>
        /// Подтягивание: пока удилище идёт вверх на натянутой леске, от места, где рыба, расходится волна — как
        /// в жизни, когда рыбу волокут к берегу. В начале рывка — всплеск из двух колец, дальше кольца идут одно
        /// за другим, пока тянут; каждое рождается чуть ближе к рыболову, поэтому волна «бежит» на берег.
        /// Сила зависит от скорости подъёма и от веса рыбы: мелочь даёт рябь, крупная — заметный вал.
        /// </summary>
        private void UpdateLiftWave(FightFrame frame, float dt)
        {
            float rate = dt > 1e-5f ? (frame.RodAngle - _liftAngle) / dt : 0f;
            _liftAngle = frame.RodAngle;
            bool lifting = rate >= LiftWaveMinRate && frame.TensionG >= LiftWaveMinTensionG;
            _liftWaveIn -= dt;
            if (!lifting)
            {
                _lifting = false;
                return;
            }

            float pull = Mathf.InverseLerp(LiftWaveMinRate, LiftWaveFullRate, rate);
            float size = Mathf.Lerp(0.7f, 1.5f, Mathf.Clamp01(_bite.WeightG / 2000f));
            float strength = (0.55f + 0.45f * pull) * size;
            var at = _float.Position;
            // К рыболову: рыбу тянут туда, туда же бежит волна.
            var toShore = -frame.Direction;
            toShore.y = 0f;
            if (toShore.sqrMagnitude < 1e-4f)
                toShore = Vector3.back;
            toShore.Normalize();

            if (!_lifting)
            {
                // Начало рывка: вода вздувается над рыбой.
                _ripples.Spawn(at, 0.9f * strength, 0.8f, 0.75f, 0.045f);
                _ripples.Spawn(at + toShore * 0.25f, 1.6f * strength, 1.2f, 0.55f, 0.035f);
                _liftWaveIn = LiftWaveEverySeconds;
                _lifting = true;
                return;
            }

            if (_liftWaveIn <= 0f)
            {
                _ripples.Spawn(at + toShore * (0.35f * strength), (1.0f + 0.9f * pull) * strength, 1.0f, 0.35f + 0.3f * pull, 0.035f);
                _liftWaveIn = LiftWaveEverySeconds;
            }
        }

        private static float HoopEnvelope(float age)
        {
            if (age <= HoopHoldSeconds)
                return 1f;
            return Mathf.Max(0f, 1f - (age - HoopHoldSeconds) / HoopFadeSeconds);
        }

        private static float KnockEnvelope(float age)
        {
            if (age < KnockAttackSeconds)
                return age / KnockAttackSeconds;
            return Mathf.Max(0f, 1f - (age - KnockAttackSeconds) / KnockFadeSeconds);
        }

        private void SetState(LakeState state)
        {
            State = state;
            StateTime = 0f;
        }

        private void ShowMessage(string message)
        {
            _message = message;
            _messageUntil = Time.time + 2.5f;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
