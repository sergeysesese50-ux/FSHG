using System;
using Fishing.Core;

namespace Fishing.Unity
{
    /// <summary>
    /// Тексты карточки схода (docs/specs/fight-v2.md, §5): заголовок по итогу, причина с числами из FightReport
    /// и один совет «Как надо» на каждую причину FightCause. Только чистые функции; строки собираются один раз
    /// в конце вываживания. Прототип: строки прямо в коде; в игре они уйдут в таблицы Localization.
    /// </summary>
    internal static class FightLossText
    {
        /// <summary>Фрикцион выше этой доли прочности рвёт леску на трогании (как DragTooTight в модели).</summary>
        private const double SafeDragShare = 0.7;

        /// <summary>Удилище держит фрикцион до предела / 1,3 (как AutoDragG в FightTackle).</summary>
        private const double RodDragDivider = 1.3;

        public static string Title(FightOutcome outcome)
        {
            switch (outcome)
            {
                case FightOutcome.LineBroke: return "Обрыв лески";
                case FightOutcome.RodBroke: return "Сломал удилище";
                case FightOutcome.SpoolEmpty: return "Смотала всю леску";
                case FightOutcome.Escaped: return "Сход";
                default: return "Рыба ушла";
            }
        }

        /// <summary>
        /// Причина с числами, например «Обрыв: 1,26 кг при прочности 1,22 — 21 с крутил вхолостую (−32%)».
        /// stamina — силы рыбы в момент конца (FightFrame.Stamina).
        /// </summary>
        public static string Cause(FightReport r, FightTackle tackle, float stamina)
        {
            double drag = LastDrag(r);
            switch (r.Cause)
            {
                case FightCause.WornByCranking:
                    return Break(r) + " — " + HudText.Seconds(r.SecondsCrankedOnSlip) + " крутил вхолостую ("
                           + HudText.Minus + HudText.Percent(r.Wear) + ")";
                case FightCause.DragTooTight:
                    return Break(r) + " — фрикцион " + HudText.Kg(drag) + " (" + HudText.Percent(drag / tackle.LineBreakingLoadG)
                           + " лески) сорвался рывком";
                case FightCause.ShockRodLow:
                    return r.RodAngle < 0.05
                        ? Break(r) + " — удар рыбы пришёлся на опущенное до упора удилище"
                        : Break(r) + " — удар рыбы пришёлся на опущенное удилище (поднято на " + HudText.Percent(r.RodAngle) + ")";
                case FightCause.RodHighTightDrag:
                    return RodBreak(r) + " — поднято на " + HudText.Percent(r.RodAngle) + " при фрикционе " + HudText.Kg(drag);
                case FightCause.Overload:
                    return r.Outcome == FightOutcome.RodBroke
                        ? RodBreak(r) + " — рыба сильнее удилища"
                        : Break(r) + " — рыба сильнее лески";
                case FightCause.SpoolEmptyDragLoose:
                    return "Рыба смотала все " + HudText.Fixed(HudText.Scaled(tackle.SpoolCapacityCm / 100.0, 0), 0)
                           + " м лески — фрикцион был " + HudText.Kg(drag);
                case FightCause.SlackShaking:
                {
                    double slack = SlackSeconds(r);
                    return slack >= 0.1
                        ? "Сход: леска провисала " + HudText.Fixed(HudText.Scaled(slack, 1), 1) + " с из последних 10 — рыба стряхнула крючок"
                        : "Сход: леска провисла — рыба стряхнула крючок";
                }
                case FightCause.JumpOnSlack:
                    return "Сход на свечке: в верхней точке прыжка леска была без натяжения";
                case FightCause.NetBump:
                    return "Сход: подсачек задел рыбу, у которой ещё " + HudText.Percent(stamina) + " сил";
                case FightCause.SwingFresh:
                    return "Сход: пытался выудить свежую рыбу";
                case FightCause.Idle:
                    // Удержание «Подтянуть» с удилищем в самом верху модель считает простоем: игрок мог держать
                    // кнопку всё это время, и «без подъёма» он бы не понял.
                    return RodWasAtTop(r)
                        ? "Сход: больше 10 с без подмотки — удилище стояло вверху, «Подтянуть» его уже не поднимало, крючок вышел"
                        : "Сход: больше 10 с без подмотки и подъёма — крючок вышел";
                default:
                    switch (r.Outcome)
                    {
                        case FightOutcome.LineBroke: return Break(r);
                        case FightOutcome.RodBroke: return RodBreak(r);
                        case FightOutcome.SpoolEmpty: return "Рыба смотала всю леску — фрикцион был " + HudText.Kg(drag);
                        default: return "Рыба сошла с крючка";
                    }
            }
        }

        /// <summary>Один совет «Как надо» на причину.</summary>
        public static string Tip(FightReport r, FightTackle tackle, float fishForceG)
        {
            switch (r.Cause)
            {
                case FightCause.WornByCranking:
                    return "Трещит фрикцион — отпусти «Подмотку»: рыба забирает леску, а ты её только перекручиваешь. " +
                           "Подматывай, когда рыба встала.";
                case FightCause.DragTooTight:
                    return "Не зажимай фрикцион: держи около " + HudText.Kg(tackle.AutoDragG) +
                           " — 60% прочности лески. Убавить — «−» над «Подмоткой» (Q).";
                case FightCause.ShockRodLow:
                    return "Видишь «рывок» — подними удилище («Подтянуть»): его изгиб гасит удар.";
                case FightCause.RodHighTightDrag:
                    return "Когда рыба тянет, не держи удилище задранным: «Подмотка» опустит его, а фрикцион лучше ослабить.";
                case FightCause.Overload:
                    return r.Outcome == FightOutcome.RodBroke
                        ? "Удилище не держит такую рыбу: опускай его на рывках и ослабь фрикцион — или возьми удилище мощнее."
                        : "Леска не держит такую рыбу: отпускай «Подмотку», пока рыба тянет, и утомляй её в паузах — или возьми леску толще.";
                case FightCause.SpoolEmptyDragLoose:
                    switch (JudgeSpool(r, tackle, fishForceG, out int target))
                    {
                        case SpoolVerdict.DragAtLimit:
                            return "Фрикцион был почти на пределе — эта рыба сильнее катушки и лески. На такую нужна снасть мощнее.";
                        case SpoolVerdict.FishTooStrong:
                            return "Эта рыба намного сильнее снасти: её не удержать и затянутым фрикционом — леска лопнет раньше. " +
                                   "На такую нужна снасть мощнее.";
                        default:
                            return "Затяни фрикцион до " + HudText.Kg(target) + " — «+» над «Подмоткой» (E): рыба будет уставать, а не уходить.";
                    }
                case FightCause.SlackShaking:
                    return "Не давай леске провисать: на слабине сразу подматывай или поднимай удилище.";
                case FightCause.JumpOnSlack:
                    return "На свечке подматывай и поднимай удилище — леска должна остаться натянутой.";
                case FightCause.NetBump:
                    return "Подсачек — только когда рыба устала и подняла голову: кнопка загорится зелёным. Держи удилище выше.";
                case FightCause.SwingFresh:
                    return "Сначала утоми рыбу — кнопка «Выудить» появится сама.";
                case FightCause.Idle:
                    return RodWasAtTop(r)
                        ? "Удилище дошло до верха — опусти его «Подмоткой» и подтягивай снова."
                        : "Не стой без дела: в паузах подтягивай, потом подматывай.";
                default:
                    return "Держи леску натянутой и утомляй рыбу в паузах.";
            }
        }

        /// <summary>Что сказать, когда рыба смотала всю леску.</summary>
        public enum SpoolVerdict
        {
            /// <summary>Фрикцион был слабым — затянуть до target.</summary>
            Tighten,
            /// <summary>Фрикцион уже у безопасного предела min(катушка, 0,7 × слабое звено) — затягивать некуда.</summary>
            DragAtLimit,
            /// <summary>Фрикцион был слабым, но рыба в разы сильнее безопасного предела — затяжка не спасла бы.</summary>
            FishTooStrong,
        }

        /// <summary>
        /// Разбор «смотала всю леску». target — до какого фрикциона стоило затянуть (кратно 50 г): автофрикцион,
        /// если игрок ослабил ниже него, иначе безопасный предел.
        /// </summary>
        public static SpoolVerdict JudgeSpool(FightReport r, FightTackle tackle, float fishForceG, out int target)
        {
            double weakest = Math.Min(tackle.LineBreakingLoadG * (1.0 - r.Wear), tackle.RodMaxLoadG / RodDragDivider);
            double safe = Math.Min(tackle.ReelMaxDragG, SafeDragShare * weakest);
            int limit = (int)(Math.Floor(safe / 50.0) * 50.0);
            double drag = LastDrag(r);
            target = drag < tackle.AutoDragG - 50 ? Math.Min(tackle.AutoDragG, limit) : limit;
            if (drag >= 0.9 * safe || limit <= drag + 50)
                return SpoolVerdict.DragAtLimit;
            return fishForceG >= 2.5 * safe ? SpoolVerdict.FishTooStrong : SpoolVerdict.Tighten;
        }

        /// <summary>Вываживание кончилось с удилищем в самом верху (порог — как у FightFrame.RodAtTop).</summary>
        private static bool RodWasAtTop(FightReport r) => r.RodAngle >= FightFrame.RodTopAngle;

        private static string Break(FightReport r) =>
            "Обрыв: " + HudText.Kg(r.PeakG) + " при прочности " + HudText.KgNumber(r.LimitG);

        private static string RodBreak(FightReport r) =>
            "Удилище: " + HudText.Kg(r.PeakG) + " при пределе " + HudText.KgNumber(r.LimitG);

        /// <summary>Фрикцион последнего шага истории (с учётом предела катушки), г.</summary>
        private static double LastDrag(FightReport r) =>
            r.HistoryCount > 0 ? r.History(r.HistoryCount - 1).DragG : 0.0;

        /// <summary>Сколько секунд из истории (последние 10 с) леска была без натяжения.</summary>
        private static double SlackSeconds(FightReport r)
        {
            int slack = 0;
            for (int i = 0; i < r.HistoryCount; i++)
            {
                if (r.History(i).TensionG <= 0.5)
                    slack++;
            }
            return slack * new FightSettings().TickSeconds;
        }
    }
}
