using System;

namespace Fishing.Core
{
    /// <summary>Режим натянутой лески (или слабина) в этот шаг.</summary>
    public enum FightMode
    {
        /// <summary>Слабина больше допуска: натяжения нет, рыба плывёт свободно.</summary>
        Slack,
        /// <summary>(a) Удилище внизу и «Подмотка», или «Подтянуть»: моторы тянут рыбу.</summary>
        Pull,
        /// <summary>(b) «Подмотка» без «Подтянуть», удилище поднято: удилище опускается, рыба стоит.</summary>
        PumpReturn,
        /// <summary>(c) Кнопки отпущены: леска стоит, удилище уступает под нагрузкой.</summary>
        Hold,
    }

    /// <summary>Решение баланса сил за шаг: скорости, а не перемещения. Перемещения считает FightModel.</summary>
    public struct FightSolution
    {
        public FightMode Mode;

        /// <summary>Натяжение лески без ударов, г. Не меньше 0.</summary>
        public double TensionG;

        /// <summary>Фрикцион пробуксовывает.</summary>
        public bool Slipping;

        /// <summary>Пробуксовка началась в этот шаг (трогание фрикциона).</summary>
        public bool Breakaway;

        /// <summary>Тяга рыбы отрицательная (свечка): натяжение 0, рыба идёт к рыбаку сама.</summary>
        public bool Negative;

        /// <summary>Скорость рыбы, см/с: плюс — к рыбаку.</summary>
        public double FishCmPerSec;

        /// <summary>Как меняется размотанная леска, см/с: плюс — леска уходит.</summary>
        public double LineCmPerSec;

        /// <summary>Скорость кончика удилища, см/с: плюс — вверх.</summary>
        public double RodCmPerSec;

        /// <summary>Скорость мотора катушки (ручки) при этом натяжении, см/с; 0 — «Подмотка» не нажата.</summary>
        public double ReelMotorCmPerSec;

        /// <summary>Скорость пробуксовки фрикциона, см/с.</summary>
        public double SlipCmPerSec;
    }

    /// <summary>
    /// Баланс сил на натянутой леске (спецификация fight-v2, §3.3). Чистые функции без состояния,
    /// только + − × ÷ min max: тот же расчёт потом повторит сервер.
    ///
    /// Моторы: катушка c(T) = C0 × max(0,1; 1 − T/Tr), подъём l(T) = L0 × max(0; 1 − T/Ta),
    /// L0 = вылет / LiftSeconds, Ta = min(0,9 × фрикцион; сила удилища). Тело рыбы тормозит
    /// с силой b × скорость, поэтому рыба, которую тянут со скоростью s, даёт натяжение P + b × s.
    /// </summary>
    public static class FightSolver
    {
        /// <summary>Доля скорости катушки, ниже которой она не падает, пока фрикцион держит.</summary>
        public const double ReelMinShare = 0.1;

        /// <summary>Порог трогания фрикциона: стоящий фрикцион срывается при 1,2 × фрикцион.</summary>
        public const double BreakawayFactor = 1.2;

        /// <summary>Мотор «Подтянуть» встаёт при 0,9 × фрикцион: подъём сам фрикцион не срывает.</summary>
        public const double LiftDragShare = 0.9;

        /// <summary>Как быстро уступает удилище без кнопок, доля подъёма в секунду.</summary>
        public const double RodGivePerSec = 0.35;

        public static double ReelSpeed(double tensionG, double c0, double tr)
        {
            if (tr <= 0)
                return c0 * ReelMinShare;
            return c0 * Math.Max(ReelMinShare, 1.0 - tensionG / tr);
        }

        public static double LiftSpeed(double tensionG, double l0, double ta)
        {
            if (ta <= 0)
                return 0.0;
            return l0 * Math.Max(0.0, 1.0 - tensionG / ta);
        }

        /// <summary>Сила, при которой встаёт подъём удилища (Ta), г.</summary>
        public static double LiftStallG(FightTackle tackle, double dragG)
        {
            return Math.Min(LiftDragShare * dragG, tackle.RodPowerG);
        }

        /// <summary>
        /// Решение для натянутой лески. pullG — тяга рыбы P (может быть меньше 0 на свечке),
        /// bodyDrag — b, г на см/с. resting — фаза Rest (удилище уступает втрое медленнее).
        /// </summary>
        public static FightSolution SolveTight(FightTackle tackle, double liftSeconds, double pullG, double bodyDrag,
            bool reel, bool lift, double rodAngle, double dragG, bool wasSlipping, bool resting)
        {
            double c0 = tackle.ReelFightSpeedCmPerSec;
            double tr = tackle.ReelPowerG;
            double reach = tackle.RodReachCm;
            double l0 = liftSeconds > 0 ? reach / liftSeconds : 0.0;
            double ta = LiftStallG(tackle, dragG);
            bool liftActive = lift && rodAngle < 1.0 && ta > 0 && l0 > 0;
            double b = bodyDrag;
            double freeSpeed = b > 0 ? -pullG / b : 0.0;

            var s = new FightSolution();
            double c = 0.0;
            double t;

            if (lift || (reel && rodAngle <= 0.0))
            {
                // (a) Моторы тянут рыбу: s = c + l, T = P + b × s.
                s.Mode = FightMode.Pull;
                if (!reel && !liftActive)
                {
                    // «Подтянуть» при удилище вверху: леска просто держит.
                    t = Math.Max(pullG, 0.0);
                    s.Negative = pullG < 0;
                    s.FishCmPerSec = s.Negative ? freeSpeed : 0.0;
                }
                else
                {
                    t = Balance(pullG, b, reel, c0, tr, liftActive, l0, ta);
                    double l;
                    if (t < 0)
                    {
                        // Рыба идёт к рыбаку быстрее моторов: натяжения нет, слабина растёт сама.
                        t = 0.0;
                        s.Negative = true;
                        c = reel ? c0 : 0.0;
                        l = liftActive ? l0 : 0.0;
                        s.FishCmPerSec = freeSpeed;
                    }
                    else
                    {
                        c = reel ? ReelSpeed(t, c0, tr) : 0.0;
                        l = liftActive ? LiftSpeed(t, l0, ta) : 0.0;
                        s.FishCmPerSec = c + l;
                    }

                    s.LineCmPerSec = -c;
                    s.RodCmPerSec = l;
                }
            }
            else if (reel)
            {
                // (b) Возврат удилища с подмоткой: рыба стоит, леска выбирается ровно на опускание.
                s.Mode = FightMode.PumpReturn;
                t = Math.Max(pullG, 0.0);
                s.Negative = pullG < 0;
                c = ReelSpeed(t, c0, tr);
                s.LineCmPerSec = -c;
                s.RodCmPerSec = -c;
                s.FishCmPerSec = s.Negative ? freeSpeed : 0.0;
            }
            else
            {
                // (c) Без кнопок: леска стоит, удилище уступает под нагрузкой, рыба отходит на столько же.
                s.Mode = FightMode.Hold;
                t = Math.Max(pullG, 0.0);
                s.Negative = pullG < 0;
                double load = dragG > 0 ? Math.Min(1.0, t / (0.25 * dragG)) : (t > 0 ? 1.0 : 0.0);
                double give = RodGivePerSec * load * (resting ? 0.3 : 1.0) * reach;
                s.RodCmPerSec = -give;
                s.FishCmPerSec = s.Negative ? freeSpeed : -give;
            }

            // Фрикцион с трогания: стоящий срывается выше 1,2 × фрикцион, идущий держит ровно фрикцион.
            double breakaway = wasSlipping ? dragG : BreakawayFactor * dragG;
            if (t > breakaway)
            {
                s.Slipping = true;
                s.Breakaway = !wasSlipping;
                s.Negative = false;
                t = Math.Max(0.0, dragG);
                double u = b > 0 ? (t - pullG) / b : 0.0;
                s.FishCmPerSec = u;
                s.LineCmPerSec = -u;
                s.RodCmPerSec = 0.0;
                c = reel ? ReelSpeed(t, c0, tr) : 0.0;
                s.SlipCmPerSec = Math.Max(0.0, c - u);
            }

            s.TensionG = t;
            s.ReelMotorCmPerSec = reel ? (s.Negative ? c0 : ReelSpeed(t, c0, tr)) : 0.0;
            return s;
        }

        /// <summary>
        /// Натяжение при работающих моторах: T = (P + b·ΣA) / (1 + b·ΣA/Tпредел). Мотор, упёршийся
        /// в ограничение (катушка — 10% скорости, подъём — 0), фиксируется и уравнение решается снова.
        /// Фиксация только увеличивает T, поэтому хватает двух повторов.
        /// </summary>
        private static double Balance(double p, double b, bool reel, double c0, double tr, bool lift, double l0, double ta)
        {
            bool reelFixed = tr <= 0;
            bool liftFixed = false;
            double t = 0.0;
            for (int pass = 0; pass < 3; pass++)
            {
                double num = p;
                double den = 1.0;
                if (reel)
                {
                    if (reelFixed)
                    {
                        num += b * ReelMinShare * c0;
                    }
                    else
                    {
                        num += b * c0;
                        den += b * c0 / tr;
                    }
                }

                if (lift && !liftFixed)
                {
                    num += b * l0;
                    den += b * l0 / ta;
                }

                t = num / den;
                bool changed = false;
                if (reel && !reelFixed && t > (1.0 - ReelMinShare) * tr)
                {
                    reelFixed = true;
                    changed = true;
                }

                if (lift && !liftFixed && t > ta)
                {
                    liftFixed = true;
                    changed = true;
                }

                if (!changed)
                    break;
            }

            return t;
        }
    }
}
