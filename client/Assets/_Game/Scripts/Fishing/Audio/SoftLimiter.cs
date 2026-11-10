using System;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Мастер-лимитер звука озера (правило 18 исследования: около −16 LUFS на устройстве, пик не выше −1 дБ):
    /// пиковый лимитер с заглядыванием вперёд и мягкий ограничитель после него.
    ///
    /// 1. Нужное усиление на сэмпл: min(1, порог / |x|), порог Ceiling (−1 дБFS).
    /// 2. Минимум за окно W = 1,5 мс (заглядывание) — и сразу «атака» без задержки; отпускание экспонентой Release (40 мс):
    ///    короткое — лимитер срезает только вершины щелчков и не «качает» постель между ними (замер: при 30 мс он работает
    ///    на 16% сэмплов боя окуня, при 80 мс — на 37%, при 200 мс — на 61%, а пик-фактор и модуляция 2–8 Гц те же).
    /// 3. Скользящее среднее за то же окно: усиление опускается плавной рампой ровно к пику, а не ступенькой.
    /// 4. Сигнал задержан на W − 1 сэмпл, поэтому рампа приходит заранее и пик проходит не выше порога.
    /// 5. Мягкий ограничитель: до 0,9 — без изменений, выше — tanh-колено к 0,99. Выше 0 дБFS не выходит никогда.
    /// Тихий материал (ниже порога) проходит без изменений, только с задержкой 1,5 мс. Без выделений памяти на звуковом потоке.
    /// </summary>
    public sealed class SoftLimiter
    {
        public const float Ceiling = 0.891f;       // −1 дБFS
        private const float KneeStart = 0.9f;
        private const float KneeTop = 0.99f;
        private const double LookaheadSeconds = 0.0015;
        private const double ReleaseSeconds = 0.04;
        private const int MaxWindow = 512;          // хватает до 192 кГц

        private readonly float[] _delay = new float[MaxWindow];
        private readonly float[] _gainWindow = new float[MaxWindow];
        private readonly float[] _dequeValue = new float[MaxWindow];
        private readonly int[] _dequeIndex = new int[MaxWindow];
        private int _window = 1;
        private int _rate;
        private int _pos;
        private long _counter;
        private int _dqHead, _dqCount;
        private double _release = 1;
        private double _releaseCoef;
        private double _boxSum;

        /// <summary>Наименьшее усиление (наибольшее ослабление) с последнего ResetStats: 1 — лимитер не работал.</summary>
        public float MinGain { get; private set; } = 1f;

        /// <summary>Сколько сэмплов прошло с ослаблением глубже 0,1 / 1 / 3 дБ (с последнего ResetStats).</summary>
        public long LimitedSamples { get; private set; }

        public long LimitedOver1Db { get; private set; }

        public long LimitedOver3Db { get; private set; }

        public void ResetStats()
        {
            MinGain = 1f;
            LimitedSamples = 0;
            LimitedOver1Db = 0;
            LimitedOver3Db = 0;
        }

        /// <summary>Частота сэмплов; при смене — сброс состояния (вызывать на том же потоке, что Process).</summary>
        public void SetRate(int rate)
        {
            if (rate == _rate || rate <= 0)
                return;
            _rate = rate;
            _window = Math.Max(1, Math.Min(MaxWindow, (int)Math.Round(LookaheadSeconds * rate)));
            _releaseCoef = 1 - Math.Exp(-1.0 / (ReleaseSeconds * rate));
            Array.Clear(_delay, 0, _delay.Length);
            for (int i = 0; i < MaxWindow; i++)
                _gainWindow[i] = 1f;
            _boxSum = _window;
            _release = 1;
            _pos = 0;
            _dqHead = 0;
            _dqCount = 0;
        }

        /// <summary>Обработать count сэмплов на месте.</summary>
        public void Process(float[] buffer, int count)
        {
            if (_rate <= 0)
                SetRate(48000);
            int w = _window;
            for (int i = 0; i < count; i++)
            {
                float x = buffer[i];
                float ax = x < 0 ? -x : x;
                float need = ax > Ceiling ? Ceiling / ax : 1f;

                // Минимум за окно: монотонная очередь (значения растут от головы к хвосту).
                long now = _counter++;
                while (_dqCount > 0 && _dequeValue[(_dqHead + _dqCount - 1) % MaxWindow] >= need)
                    _dqCount--;
                int tail = (_dqHead + _dqCount) % MaxWindow;
                _dequeValue[tail] = need;
                _dequeIndex[tail] = (int)(now % int.MaxValue);
                _dqCount++;
                while (Age(_dequeIndex[_dqHead], now) >= w)
                {
                    _dqHead = (_dqHead + 1) % MaxWindow;
                    _dqCount--;
                }
                double held = _dequeValue[_dqHead];

                // Атака мгновенная (рампу даёт среднее ниже), отпускание — экспонента.
                _release = held < _release ? held : _release + (held - _release) * _releaseCoef;

                // Скользящее среднее за окно.
                int slot = _pos;
                _boxSum += _release - _gainWindow[slot];
                _gainWindow[slot] = (float)_release;
                double gain = _boxSum / w;
                if (gain > 1)
                    gain = 1;

                // Задержка на w − 1 сэмпл: кольцо длиной w, читаем самый старый.
                float delayed = w > 1 ? _delay[(slot + 1) % w] : x;
                _delay[slot] = x;
                _pos = (slot + 1) % w;

                float y = (float)(delayed * gain);
                if (gain < 0.9886)
                {
                    LimitedSamples++;
                    if (gain < 0.8913)
                        LimitedOver1Db++;
                    if (gain < 0.7079)
                        LimitedOver3Db++;
                }
                if (gain < MinGain)
                    MinGain = (float)gain;
                buffer[i] = Soft(y);
            }
            // Сумма скользящего среднего копит ошибку округления — пересчитать раз в буфер.
            double sum = 0;
            for (int k = 0; k < w; k++)
                sum += _gainWindow[k];
            _boxSum = sum;
        }

        private static long Age(int index, long now)
        {
            long i = now % int.MaxValue;
            long age = i - index;
            return age < 0 ? age + int.MaxValue : age;
        }

        /// <summary>Мягкое колено: ниже KneeStart — без изменений, выше — tanh к KneeTop (никогда не 1).</summary>
        public static float Soft(float y)
        {
            float a = y < 0 ? -y : y;
            if (a <= KneeStart)
                return y;
            double over = (a - KneeStart) / (KneeTop - KneeStart);
            float s = (float)(KneeStart + (KneeTop - KneeStart) * Math.Tanh(over));
            return y < 0 ? -s : s;
        }
    }
}
