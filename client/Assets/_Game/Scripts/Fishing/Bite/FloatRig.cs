using System;

namespace Fishing.Core
{
    /// <summary>
    /// Спуск поплавочной оснастки: на какой глубине под поплавком висит наживка. Его выставляет игрок, пока удочка
    /// на берегу: от 20 см до длины удилища без полуметра, шагом 10 см. Если спуск больше глубины воды в месте,
    /// куда упал поплавок, наживка лежит на дне. От глубины наживки зависит, какая рыба клюнет (BiteModel).
    /// </summary>
    public static class FloatRig
    {
        public const int MinDepthCm = 20;
        public const int StepCm = 10;
        public const int DefaultDepthCm = 100;

        /// <summary>Наибольший спуск для удилища: длина без 50 см, кратно шагу.</summary>
        public static int MaxDepthCm(int rodLengthCm)
        {
            return Math.Max(MinDepthCm, (rodLengthCm - 50) / StepCm * StepCm);
        }

        /// <summary>Спуск в допустимых пределах, округлённый до шага. Ноль и меньше — спуск по умолчанию.</summary>
        public static int Clamp(int depthCm, int rodLengthCm)
        {
            if (depthCm <= 0)
                depthCm = DefaultDepthCm;
            int snapped = (depthCm + StepCm / 2) / StepCm * StepCm;
            return Math.Max(MinDepthCm, Math.Min(MaxDepthCm(rodLengthCm), snapped));
        }

        /// <summary>Глубина наживки, см: спуск, но не глубже дна.</summary>
        public static float BaitDepthCm(int floatDepthCm, float waterDepthCm)
        {
            return Math.Max(1f, Math.Min(floatDepthCm, waterDepthCm));
        }

        /// <summary>Наживка лежит на дне: спуск не меньше глубины воды.</summary>
        public static bool OnBottom(int floatDepthCm, float waterDepthCm) => floatDepthCm >= waterDepthCm;
    }
}
