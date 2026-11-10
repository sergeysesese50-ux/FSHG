using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Виды удилищ (поле type в tackle.json).</summary>
    public static class RodKinds
    {
        /// <summary>Поплавочная: наживка висит под поплавком на выставленной глубине.</summary>
        public const string Float = "float";
        /// <summary>Донная (донка, фидер): наживка лежит на дне, заброс дальше.</summary>
        public const string Bottom = "bottom";
        /// <summary>Спиннинг: искусственная приманка, клюёт только пока её ведут подмоткой.</summary>
        public const string Spinning = "spinning";
    }

    /// <summary>Род наживки и приманки (поле kind в tackle.json): по нему раскладываются полки магазина и коробки.</summary>
    public static class BaitKinds
    {
        public const string Animal = "animal";
        public const string Plant = "plant";
        public const string Live = "live";
        public const string Spinner = "spinner";
        public const string Spoon = "spoon";
        public const string Soft = "soft";
        public const string Wobbler = "wobbler";

        public static bool IsNatural(string kind) => kind == Animal || kind == Plant || kind == Live;

        public static bool IsLure(string kind) => kind == Spinner || kind == Spoon || kind == Soft || kind == Wobbler;
    }

    /// <summary>Ход приманки спиннинга (поле run в tackle.json): на какой глубине она идёт.</summary>
    public static class LureRuns
    {
        /// <summary>По самой поверхности (поппер): 30 см.</summary>
        public const string Top = "top";
        /// <summary>У поверхности: до 1,2 м.</summary>
        public const string Surface = "surface";
        /// <summary>В полводы, но не мельче 60 см.</summary>
        public const string Mid = "mid";
        /// <summary>У дна: 20 см над ним.</summary>
        public const string Bottom = "bottom";

        public static bool IsKnown(string run) => run == Top || run == Surface || run == Mid || run == Bottom;
    }

    /// <summary>
    /// Скорость проводки приманки спиннингом, в процентах от 10 до 100 (шаг 10; 50 — обычная).
    /// Быстрее — приманка идёт мельче и быстрее проходит воду (чаще поклёвки, раньше берег);
    /// медленнее — идёт глубже и дольше остаётся в воде.
    /// </summary>
    public static class LureRetrieve
    {
        public const int MinPercent = 10;
        public const int MaxPercent = 100;
        public const int StepPercent = 10;
        public const int DefaultPercent = 50;

        /// <summary>Приводит значение к шагу и пределам; 0 (не выставляли) — обычная скорость.</summary>
        public static int Clamp(int percent)
        {
            if (percent <= 0)
                return DefaultPercent;
            int stepped = (int)Math.Round(percent / (double)StepPercent) * StepPercent;
            return Math.Max(MinPercent, Math.Min(MaxPercent, stepped));
        }

        /// <summary>Отклонение от обычной скорости: −0,8 — самая медленная, 0 — обычная, +1 — самая быстрая.</summary>
        public static float Offset(int percent) => (Clamp(percent) - DefaultPercent) / 50f;

        /// <summary>Во сколько раз приманка идёт быстрее обычной подмотки катушки: 0,52 … 1,6.</summary>
        public static float SpeedFactor(int percent) => 1f + 0.6f * Offset(percent);
    }

    /// <summary>Правила снастей: что с чем сочетается и на какой глубине идёт наживка у каждого вида удилища.</summary>
    public static class TackleRules
    {
        public const string StarterRod = "float_rod_basic";
        public const string StarterReel = "reel_1000";
        public const string StarterLine = "line_012";

        /// <summary>Самый дальний заброс, который вмещает озеро и кадр, м (даже если удилище может дальше).</summary>
        public const float LongestCastM = 45f;

        /// <summary>Какую наживку (rodType в tackle.json) берёт удилище: спиннинг — приманки, остальные — натуральную.</summary>
        public static string BaitTypeFor(string rodType)
        {
            return rodType == RodKinds.Spinning ? RodKinds.Spinning : RodKinds.Float;
        }

        public static bool BaitFits(BaitData bait, RodData rod)
        {
            return bait != null && rod != null && bait.rodType == BaitTypeFor(rod.type);
        }

        /// <summary>
        /// Глубина наживки, см. Поплавочная — спуск поплавка, но не глубже дна; донная — дно; спиннинг — по ходу
        /// приманки lureRun (LureRuns): у дна, в полводы (не мельче 60 см), у поверхности (до 1,2 м) или поверху (30 см).
        /// </summary>
        public static float BaitDepthCm(string rodType, string lureRun, int floatDepthCm, float waterDepthCm)
        {
            float water = Math.Max(1f, waterDepthCm);
            if (rodType == RodKinds.Bottom)
                return water;
            if (rodType != RodKinds.Spinning)
                return FloatRig.BaitDepthCm(floatDepthCm, water);
            switch (lureRun)
            {
                case LureRuns.Bottom: return Math.Max(20f, water - 20f);
                case LureRuns.Surface: return Math.Min(water, 120f);
                case LureRuns.Top: return Math.Min(water, 30f);
                default: return Math.Min(water, Math.Max(60f, water * 0.5f));
            }
        }

        /// <summary>
        /// То же с учётом скорости проводки спиннингом (LureRetrieve): быстрая поднимает приманку, медленная опускает.
        /// У дна: быстрая отрывает от дна до четверти глубины. В полводы: от 66% глубины (медленно) до 30% (быстро).
        /// У поверхности: от 1,5 м до 0,8 м. Поверху — всегда 30 см. На обычной скорости — как без неё.
        /// </summary>
        public static float BaitDepthCm(string rodType, string lureRun, int floatDepthCm, float waterDepthCm, int retrievePercent)
        {
            if (rodType != RodKinds.Spinning)
                return BaitDepthCm(rodType, lureRun, floatDepthCm, waterDepthCm);
            float water = Math.Max(1f, waterDepthCm);
            float offset = LureRetrieve.Offset(retrievePercent);
            switch (lureRun)
            {
                case LureRuns.Bottom: return Math.Max(20f, water - 20f - Math.Max(0f, offset) * 0.25f * water);
                case LureRuns.Surface: return Math.Min(water, 120f * (1f - 0.35f * offset));
                case LureRuns.Top: return Math.Min(water, 30f);
                default: return Math.Min(water, Math.Max(60f, water * (0.5f - 0.2f * offset)));
            }
        }

        /// <summary>Наживка у самого дна: донка всегда, приманка с ходом «у дна», поплавочная — если спуск больше глубины.</summary>
        public static bool OnBottom(string rodType, string lureRun, int floatDepthCm, float waterDepthCm)
        {
            if (rodType == RodKinds.Bottom)
                return true;
            if (rodType == RodKinds.Spinning)
                return lureRun == LureRuns.Bottom;
            return FloatRig.OnBottom(floatDepthCm, waterDepthCm);
        }

        /// <summary>Дальность заброса удилища, м: castRangeCm, но не дальше LongestCastM.</summary>
        public static float MaxCastM(RodData rod)
        {
            return rod == null ? 24f : Math.Min(LongestCastM, rod.castRangeCm / 100f);
        }

        /// <summary>Уровень, с которого вещь продаётся: поле minLevel, пусто — с первого.</summary>
        public static int LevelFor(int minLevel) => Math.Max(1, minLevel);
    }

    /// <summary>
    /// Снасти игрока: что куплено и что сейчас собрано (удилище, катушка, леска). Стартовый набор есть всегда.
    /// Какой id чем является (удилище или леска), знает TackleDatabase; здесь только списки.
    /// </summary>
    public sealed class Loadout
    {
        private readonly List<string> _owned = new List<string>();

        public Loadout()
        {
            _owned.Add(TackleRules.StarterRod);
            _owned.Add(TackleRules.StarterReel);
            _owned.Add(TackleRules.StarterLine);
        }

        public IReadOnlyList<string> Owned => _owned;
        public string RodId { get; private set; } = TackleRules.StarterRod;
        public string ReelId { get; private set; } = TackleRules.StarterReel;
        public string LineId { get; private set; } = TackleRules.StarterLine;

        public bool Owns(string id) => id != null && _owned.Contains(id);

        /// <summary>Добавляет купленную вещь. false — уже есть или id пустой.</summary>
        public bool Add(string id)
        {
            if (string.IsNullOrEmpty(id) || _owned.Contains(id))
                return false;
            _owned.Add(id);
            return true;
        }

        /// <summary>Ставит удилище. false — оно не куплено.</summary>
        public bool EquipRod(string id)
        {
            if (!Owns(id))
                return false;
            RodId = id;
            return true;
        }

        public bool EquipReel(string id)
        {
            if (!Owns(id))
                return false;
            ReelId = id;
            return true;
        }

        public bool EquipLine(string id)
        {
            if (!Owns(id))
                return false;
            LineId = id;
            return true;
        }

        public string[] OwnedState() => _owned.ToArray();

        /// <summary>
        /// Из сохранения: пустые и повторные id пропускаются, стартовый набор есть всегда; собранное, которого нет
        /// среди купленного, заменяется стартовым.
        /// </summary>
        public static Loadout FromState(string[] owned, string rodId, string reelId, string lineId)
        {
            var loadout = new Loadout();
            if (owned != null)
            {
                foreach (string id in owned)
                    loadout.Add(id);
            }

            loadout.EquipRod(rodId);
            loadout.EquipReel(reelId);
            loadout.EquipLine(lineId);
            return loadout;
        }
    }
}
