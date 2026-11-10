using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Цена рыбы. Только целые числа: тот же расчёт потом повторит сервер и получит те же монеты.</summary>
    public static class FishPricing
    {
        /// <summary>Трофей стоит вдвое дороже обычной рыбы того же веса.</summary>
        public const int TrophyFactorPercent = 200;

        /// <summary>Монеты за рыбу: вес × цена вида за килограмм (× 2 за трофей), вниз до целого, но не меньше 1.</summary>
        public static long Price(FishSpecies species, int weightG, bool trophy)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            if (weightG <= 0)
                return 0;
            long price = (long)weightG * species.pricePerKg * (trophy ? TrophyFactorPercent : 100) / 100000L;
            return Math.Max(1L, price);
        }

        public static long Price(FishDatabase fish, CaughtFish caught)
        {
            return Price(fish.Get(caught.speciesId), caught.weightG, caught.trophy);
        }
    }

    /// <summary>Сохранение профиля. Имена полей — ключи JSON.</summary>
    [Serializable]
    public sealed class ProfileState
    {
        public int version;
        public long money;
        public int nextFishId;
        public CaughtFish[] keepnet;
        // С версии 2: карма и статистика.
        public int karma;
        public SpeciesStats[] species;
        public LossStats losses;
        public long moneyEarned;
        /// <summary>Место ловли, где игрок был в последний раз (id из locations.json); пусто — первое место.</summary>
        public string spotId;
        /// <summary>Спуск поплавка, см (FloatRig); 0 — не выставлял, по умолчанию.</summary>
        public int floatDepthCm;
        // С версии 3: запас наживки и та, что на крючке.
        public BaitStock[] baits;
        public string baitId;
        // С версии 4: опыт (уровень и ранг считаются из него — LevelRules).
        public long xp;
        /// <summary>Лодка куплена насовсем (места на воде). В старых сохранениях поля нет — лодки нет.</summary>
        public bool boat;
        /// <summary>Сколько секунд игры осталось от аренды лодки; 0 — аренды нет.</summary>
        public int boatRentSeconds;
        // С версии 5: купленные снасти и то, что сейчас собрано. Стартовый набор есть всегда.
        public string[] tackleOwned;
        public string rodId;
        public string reelId;
        public string lineId;
        // С версии 6: за какой уровень награды уже выданы (levels.json).
        public int rewardedLevel;
        /// <summary>Скорость проводки спиннингом, % (LureRetrieve); 0 — не выставляли, обычная.</summary>
        public int retrievePercent;
    }

    /// <summary>Итог продажи: сколько рыб, общий вес и сколько монет получено.</summary>
    public readonly struct SaleReceipt
    {
        public readonly int Count;
        public readonly int WeightG;
        public readonly long Money;

        public SaleReceipt(int count, int weightG, long money)
        {
            Count = count;
            WeightG = weightG;
            Money = money;
        }
    }

    /// <summary>
    /// Профиль игрока: деньги, садок, карма и статистика ловли. Все правила — здесь, без движка: что входит в садок,
    /// сколько стоит рыба, что происходит при продаже, за что меняется карма (KarmaRules), что попадает в статистику. Сейчас профиль хранится на устройстве (ProfileStore); когда появится сервер,
    /// эти же правила будет считать он, а клиент — только показывать.
    /// </summary>
    public sealed class PlayerProfile
    {
        public const int StateVersion = 6;

        /// <summary>Вместимость садка, г. Позже — свойство купленного садка.</summary>
        public const int KeepnetCapacityG = 10000;

        private int _nextFishId = 1;

        public PlayerProfile()
        {
            Keepnet = new Keepnet(KeepnetCapacityG);
        }

        public long Money { get; private set; }
        public Keepnet Keepnet { get; }
        public PlayerStats Stats { get; private set; } = new PlayerStats();

        /// <summary>Место ловли, где игрок был в последний раз (id из locations.json); null — ещё не выбирал.</summary>
        public string SpotId { get; set; }

        /// <summary>Аренду нельзя накопить больше суток игры.</summary>
        public const int MaxBoatRentSeconds = 24 * 60 * 60;

        /// <summary>Лодка куплена насовсем.</summary>
        public bool OwnsBoat { get; private set; }

        /// <summary>Сколько секунд игры осталось от аренды лодки. Убывает, пока игра идёт (TickBoatRent).</summary>
        public double BoatRentSecondsLeft { get; private set; }

        /// <summary>Лодка есть — своя или арендованная: открыты места на воде (LakeSpot.needsBoat).</summary>
        public bool HasBoat => OwnsBoat || BoatRentSecondsLeft > 0.0;

        /// <summary>Покупает лодку насовсем. false — уже своя или не хватает монет. Остаток аренды сгорает.</summary>
        public bool BuyBoat(BoatData boat)
        {
            if (boat == null)
                throw new ArgumentNullException(nameof(boat));
            if (OwnsBoat || Money < boat.price)
                return false;
            Money -= boat.price;
            OwnsBoat = true;
            BoatRentSecondsLeft = 0.0;
            return true;
        }

        /// <summary>
        /// Берёт лодку в аренду на boat.rentMinutes минут игры; повторная аренда продлевает срок.
        /// false — лодка своя, не хватает монет или срок уже упёрся в предел.
        /// </summary>
        public bool RentBoat(BoatData boat)
        {
            if (boat == null)
                throw new ArgumentNullException(nameof(boat));
            double added = boat.rentMinutes * 60.0;
            if (OwnsBoat || Money < boat.rentPrice || BoatRentSecondsLeft + added > MaxBoatRentSeconds)
                return false;
            Money -= boat.rentPrice;
            BoatRentSecondsLeft += added;
            return true;
        }

        /// <summary>Время игры идёт: аренда убывает. true — аренда закончилась именно сейчас.</summary>
        public bool TickBoatRent(double seconds)
        {
            if (BoatRentSecondsLeft <= 0.0 || seconds <= 0.0)
                return false;
            BoatRentSecondsLeft = Math.Max(0.0, BoatRentSecondsLeft - seconds);
            return BoatRentSecondsLeft <= 0.0;
        }

        /// <summary>Скорость проводки спиннингом, % (LureRetrieve); 0 — не выставлял.</summary>
        public int RetrievePercent { get; set; }

        /// <summary>Спуск поплавка, см (FloatRig); 0 — не выставлял.</summary>
        public int FloatDepthCm { get; set; }

        /// <summary>За какой уровень награды уже выданы; 1 — ещё ни за один.</summary>
        public int RewardedLevel { get; private set; } = 1;

        /// <summary>
        /// Выдаёт награды за все уровни после RewardedLevel до нынешнего (обычно за один; за несколько — если уровень
        /// перескочил или награды появились позже, чем игрок вырос). null — выдавать нечего.
        /// Вещь, которая у игрока уже есть, заменяется монетами: половина её цены. Купленной лодке время аренды не нужно —
        /// за него тоже монеты, по одной за минуту.
        /// </summary>
        public RewardReceipt ClaimLevelRewards(LevelRewardDatabase rewards, TackleDatabase tackle)
        {
            if (rewards == null)
                throw new ArgumentNullException(nameof(rewards));
            if (tackle == null)
                throw new ArgumentNullException(nameof(tackle));
            int level = Level;
            if (level <= RewardedLevel)
                return null;

            var receipt = new RewardReceipt { FromLevel = RewardedLevel, ToLevel = level };
            for (int next = RewardedLevel + 1; next <= level; next++)
            {
                var reward = rewards.For(next);
                if (reward == null)
                    continue;
                receipt.Coins += reward.coins;
                foreach (var bait in reward.baits ?? Array.Empty<BaitStock>())
                {
                    int added = Baits.Add(bait.id, bait.count);
                    if (added > 0)
                        receipt.AddBait(bait.id, added);
                }

                foreach (string id in reward.lures ?? Array.Empty<string>())
                {
                    if (Baits.Count(id) > 0)
                        receipt.CompensationCoins += tackle.GetBait(id).price / 2;
                    else if (Baits.Add(id, 1) > 0)
                        receipt.Lures.Add(id);
                }

                foreach (string id in reward.tackle ?? Array.Empty<string>())
                {
                    if (Tackle.Add(id))
                        receipt.Tackle.Add(id);
                    else
                        receipt.CompensationCoins += TacklePrice(tackle, id) / 2;
                }

                if (reward.boatMinutes > 0)
                {
                    if (OwnsBoat)
                    {
                        receipt.CompensationCoins += reward.boatMinutes;
                    }
                    else
                    {
                        double before = BoatRentSecondsLeft;
                        BoatRentSecondsLeft = Math.Min(MaxBoatRentSeconds, BoatRentSecondsLeft + reward.boatMinutes * 60.0);
                        receipt.BoatMinutes += (int)Math.Round((BoatRentSecondsLeft - before) / 60.0);
                    }
                }
            }

            Money += receipt.Coins + receipt.CompensationCoins;
            RewardedLevel = level;
            return receipt;
        }

        private static int TacklePrice(TackleDatabase tackle, string id)
        {
            if (tackle.TryGetRod(id, out var rod))
                return rod.price;
            if (tackle.TryGetReel(id, out var reel))
                return reel.price;
            return tackle.TryGetLine(id, out var line) ? line.price : 0;
        }

        /// <summary>Снасти: что куплено и что сейчас собрано (удилище, катушка, леска).</summary>
        public Loadout Tackle { get; private set; } = new Loadout();

        /// <summary>
        /// Покупает удилище, катушку или леску. false — уже есть, не хватает монет или уровня; профиль не меняется.
        /// Купленное само не надевается.
        /// </summary>
        public bool BuyTackle(string id, int price, int minLevel)
        {
            if (string.IsNullOrEmpty(id) || price < 0 || Tackle.Owns(id) || Money < price || Level < TackleRules.LevelFor(minLevel))
                return false;
            Money -= price;
            Tackle.Add(id);
            return true;
        }

        /// <summary>
        /// Покупает искусственную приманку (блесна, воблер, джиг): она не расходуется, поэтому одна штука навсегда.
        /// false — это расходная наживка, приманка уже есть, не хватает монет или уровня.
        /// </summary>
        public bool BuyLure(BaitData lure)
        {
            if (lure == null)
                throw new ArgumentNullException(nameof(lure));
            if (lure.consumable || Baits.Count(lure.id) > 0 || Money < lure.price || Level < TackleRules.LevelFor(lure.minLevel))
                return false;
            Money -= lure.price;
            Baits.Add(lure.id, 1);
            return true;
        }

        /// <summary>Наживка: запас и та, что на крючке. Новый игрок начинает с BaitRules.StarterStock.</summary>
        public BaitBox Baits { get; private set; } = BaitBox.Starter();

        /// <summary>Опыт за всё время. Растёт за каждую пойманную рыбу (LevelRules).</summary>
        public long Xp { get; private set; }

        /// <summary>Уровень, 1…LevelRules.MaxLevel, и ранг — оба считаются из опыта.</summary>
        public int Level => LevelRules.LevelFor(Xp);
        public PlayerRank Rank => LevelRules.RankFor(Level);

        /// <summary>Опыт за последнюю пойманную рыбу и счётчик таких начислений — интерфейсу.</summary>
        public XpGain LastXpGain { get; private set; }
        public int XpGainCount { get; private set; }

        /// <summary>Карма, −100…+100. Правила — KarmaRules.</summary>
        public int Karma { get; private set; }

        /// <summary>Последнее изменение кармы и их счётчик — интерфейсу, чтобы показать «Карма +1».</summary>
        public KarmaChange LastKarmaChange { get; private set; }
        public int KarmaChangeCount { get; private set; }

        /// <summary>
        /// Рыба вытащена на берег: в статистику и в опыт (до решения, оставить её или отпустить).
        /// Возвращает отметки улова; начисленный опыт — в LastXpGain.
        /// </summary>
        public CatchMarks RecordCatch(FishSpecies species, int weightG, int lengthCm, bool trophy)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            var marks = Stats.RecordCatch(species.id, weightG, lengthCm, trophy);
            LastXpGain = LevelRules.ForCatch(species, weightG, trophy, marks, Xp);
            Xp += LastXpGain.Total;
            XpGainCount++;
            return marks;
        }

        /// <summary>Рыба сошла: в статистику сходов; обрыв лески роняет карму.</summary>
        public void RecordLoss(FightOutcome outcome)
        {
            Stats.RecordLoss(outcome);
            ApplyKarma(KarmaRules.ForLoss(outcome));
        }

        /// <summary>Только что пойманную рыбу отпустили (с карточки улова): карма растёт.</summary>
        public void ReleaseCatch(FishSpecies species, int weightG, bool trophy)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            Stats.RecordRelease(species.id);
            ApplyKarma(KarmaRules.ForReleasingCatch(species, weightG, trophy));
        }

        private void ApplyKarma((int delta, KarmaReason reason) change)
        {
            if (change.delta == 0)
                return;
            Karma = KarmaRules.Clamp(Karma + change.delta);
            LastKarmaChange = new KarmaChange(change.delta, change.reason, Karma);
            KarmaChangeCount++;
        }

        /// <summary>
        /// Кладёт пойманную рыбу в садок. null — не влезла (профиль не изменился): её остаётся отпустить
        /// или сначала освободить место.
        /// </summary>
        public CaughtFish Keep(FishSpecies species, int weightG, int lengthCm, bool trophy, int caughtAtMinute, CatchContext context = default)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            if (!Keepnet.Fits(weightG))
                return null;
            var fish = new CaughtFish
            {
                id = _nextFishId,
                speciesId = species.id,
                weightG = weightG,
                lengthCm = Math.Max(0, lengthCm),
                trophy = trophy,
                caughtAtMinute = caughtAtMinute,
                spotId = context.SpotId,
                baitId = context.BaitId,
                baitDepthCm = Math.Max(0, context.BaitDepthCm),
                rodId = context.RodId,
                lineId = context.LineId,
            };
            Keepnet.TryAdd(fish);
            _nextFishId++;
            ApplyKarma(KarmaRules.ForKeeping(species, weightG));
            return fish;
        }

        /// <summary>Отпускает рыбу из садка: карма растёт. false — такой рыбы нет.</summary>
        public bool Release(int fishId)
        {
            var fish = Keepnet.Remove(fishId);
            if (fish == null)
                return false;
            Stats.RecordRelease(fish.speciesId);
            ApplyKarma(KarmaRules.ForReleasingFromKeepnet());
            return true;
        }

        /// <summary>Продаёт одну рыбу из садка. Пустой чек — такой рыбы нет.</summary>
        public SaleReceipt Sell(int fishId, FishDatabase fish)
        {
            if (fish == null)
                throw new ArgumentNullException(nameof(fish));
            var caught = Keepnet.Find(fishId);
            if (caught == null)
                return default;
            long price = FishPricing.Price(fish, caught);
            Keepnet.Remove(fishId);
            Money += price;
            Stats.RecordSale(caught.speciesId, price);
            return new SaleReceipt(1, caught.weightG, price);
        }

        /// <summary>Продаёт весь садок.</summary>
        public SaleReceipt SellAll(FishDatabase fish)
        {
            if (fish == null)
                throw new ArgumentNullException(nameof(fish));
            long money = 0;
            int weight = 0;
            // Сначала цены, потом садок: если вид неизвестен, Get бросит исключение, а рыба останется на месте.
            foreach (var caught in Keepnet.Fish)
            {
                money += FishPricing.Price(fish, caught);
                weight += caught.weightG;
            }

            int count = Keepnet.Count;
            foreach (var caught in Keepnet.RemoveAll())
                Stats.RecordSale(caught.speciesId, FishPricing.Price(fish, caught));
            Money += money;
            return new SaleReceipt(count, weight, money);
        }

        /// <summary>
        /// Покупает упаковку наживки (BaitRules.PackSize штук за bait.price). false — не наживка-расходник,
        /// не хватает монет или уровня или в коробку больше не лезет; профиль тогда не меняется.
        /// </summary>
        public bool BuyBait(BaitData bait)
        {
            if (bait == null)
                throw new ArgumentNullException(nameof(bait));
            if (!bait.consumable || bait.price <= 0 || Money < bait.price || Level < TackleRules.LevelFor(bait.minLevel))
                return false;
            if (Baits.Count(bait.id) + BaitRules.PackSize > BaitRules.MaxCount)
                return false;
            Money -= bait.price;
            Baits.Add(bait.id, BaitRules.PackSize);
            return true;
        }

        /// <summary>Червей можно накопать, пока их меньше BaitRules.DigWormsBelow.</summary>
        public bool CanDigWorms => Baits.Count(BaitRules.WormId) < BaitRules.DigWormsBelow;

        /// <summary>Накопать червей бесплатно: +BaitRules.DigWormsCount. false — червей и так хватает.</summary>
        public bool DigWorms()
        {
            if (!CanDigWorms)
                return false;
            Baits.Add(BaitRules.WormId, BaitRules.DigWormsCount);
            return true;
        }

        /// <summary>
        /// Пускает мелкую рыбу из садка на живца: рыба пропадает, в коробке +1 живец. false — такой рыбы нет
        /// или она тяжелее BaitRules.LiveBaitMaxWeightG.
        /// </summary>
        public bool MakeLiveBait(int fishId)
        {
            var caught = Keepnet.Find(fishId);
            if (caught == null || !BaitRules.CanBeLiveBait(caught.weightG))
                return false;
            if (Baits.Count(BaitRules.LiveBaitId) >= BaitRules.MaxCount)
                return false;
            Keepnet.Remove(fishId);
            Baits.Add(BaitRules.LiveBaitId, 1);
            return true;
        }

        /// <summary>В коробке есть место ещё для одного живца.</summary>
        public bool HasRoomForLiveBait => Baits.Count(BaitRules.LiveBaitId) < BaitRules.MaxCount;

        /// <summary>
        /// Только что пойманную мелкую рыбу — сразу на живца, мимо садка: +1 живец. Карма — как если бы рыбу
        /// забрали (малёк — минус). false — рыба тяжелее BaitRules.LiveBaitMaxWeightG или коробка полна.
        /// </summary>
        public bool MakeLiveBaitFromCatch(FishSpecies species, int weightG)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            if (!BaitRules.CanBeLiveBait(weightG) || !HasRoomForLiveBait)
                return false;
            Baits.Add(BaitRules.LiveBaitId, 1);
            ApplyKarma(KarmaRules.ForKeeping(species, weightG));
            return true;
        }

        /// <summary>Сколько монет даст продажа всего садка сейчас.</summary>
        public long KeepnetValue(FishDatabase fish)
        {
            long money = 0;
            foreach (var caught in Keepnet.Fish)
                money += FishPricing.Price(fish, caught);
            return money;
        }

        public ProfileState ToState()
        {
            var keepnet = new CaughtFish[Keepnet.Count];
            for (int i = 0; i < keepnet.Length; i++)
                keepnet[i] = Keepnet.Fish[i];
            return new ProfileState
            {
                version = StateVersion,
                money = Money,
                nextFishId = _nextFishId,
                keepnet = keepnet,
                karma = Karma,
                species = Stats.SpeciesState(),
                losses = Stats.Losses,
                moneyEarned = Stats.MoneyEarned,
                spotId = SpotId,
                floatDepthCm = FloatDepthCm,
                baits = Baits.ToState(),
                baitId = Baits.SelectedId,
                xp = Xp,
                boat = OwnsBoat,
                boatRentSeconds = OwnsBoat ? 0 : (int)Math.Ceiling(BoatRentSecondsLeft),
                tackleOwned = Tackle.OwnedState(),
                rodId = Tackle.RodId,
                reelId = Tackle.ReelId,
                lineId = Tackle.LineId,
                rewardedLevel = RewardedLevel,
                retrievePercent = RetrievePercent,
            };
        }

        /// <summary>
        /// Восстанавливает профиль из сохранения. Испорченные записи не роняют игру: отрицательные деньги становятся
        /// нулём, рыба неизвестного вида, без веса или не влезающая в садок пропускается. Чужая версия — FormatException.
        /// </summary>
        public static PlayerProfile FromState(ProfileState state, FishDatabase fish)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (fish == null)
                throw new ArgumentNullException(nameof(fish));
            // Версия 1 — без кармы и статистики: читается как есть, они начинаются с нуля.
            // Версии 1 и 2 — без наживки: игрок получает запас новичка.
            if (state.version < 1 || state.version > StateVersion)
                throw new FormatException("Сохранение профиля: версия " + state.version + " не поддерживается, нужна " + StateVersion);

            var profile = new PlayerProfile
            {
                Money = Math.Max(0L, state.money), SpotId = state.spotId, FloatDepthCm = Math.Max(0, state.floatDepthCm),
                RetrievePercent = Math.Max(0, Math.Min(state.retrievePercent, LureRetrieve.MaxPercent)),
                OwnsBoat = state.boat,
                BoatRentSecondsLeft = state.boat ? 0.0 : Math.Max(0, Math.Min(state.boatRentSeconds, MaxBoatRentSeconds)),
            };
            if (state.version >= 2)
            {
                profile.Karma = KarmaRules.Clamp(state.karma);
                profile.Stats = PlayerStats.FromState(state.species, state.losses, state.moneyEarned);
            }
            if (state.version >= 3)
                profile.Baits = BaitBox.FromState(state.baits, state.baitId);
            // До версии 4 опыта не было: старый игрок начинает с первого уровня.
            if (state.version >= 4)
                profile.Xp = Math.Max(0L, state.xp);
            // До версии 5 снасти не покупались: у игрока стартовый набор.
            if (state.version >= 5)
                profile.Tackle = Loadout.FromState(state.tackleOwned, state.rodId, state.reelId, state.lineId);
            // До версии 6 наград за уровни не было: они придут за все набранные уровни при первом входе.
            if (state.version >= 6)
                profile.RewardedLevel = Math.Max(1, Math.Min(state.rewardedLevel, profile.Level));
            var seen = new HashSet<int>();
            int maxId = 0;
            if (state.keepnet != null)
            {
                foreach (var caught in state.keepnet)
                {
                    if (caught == null || caught.id <= 0 || !seen.Add(caught.id))
                        continue;
                    if (string.IsNullOrEmpty(caught.speciesId) || !fish.TryGet(caught.speciesId, out _))
                        continue;
                    if (!profile.Keepnet.TryAdd(caught))
                        continue;
                    maxId = Math.Max(maxId, caught.id);
                }
            }

            profile._nextFishId = Math.Max(Math.Max(1, state.nextFishId), maxId + 1);
            return profile;
        }
    }
}
