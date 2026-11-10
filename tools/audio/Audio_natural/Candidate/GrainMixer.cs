using System;
using System.Threading;

namespace Fishing.Unity
{
    /// <summary>
    /// Микшер зёрен с точностью до сэмпла: главный поток кладёт зёрна (клип, время dsp, громкость, высота)
    /// в кольцо без блокировок, звуковой поток (OnAudioFilterRead) начинает каждое ровно в его сэмпле и
    /// подмешивает с линейной интерполяцией (клипы 22050 Гц → частота вывода × высота).
    /// Зачем: при 60 к/с кадр — 16,7 мс, а период ротора на 18 зёрнах/с — 55 мс; зёрна, запущенные в момент
    /// кадра (PlayOneShot), «хромали» бы. Без движка: этот же класс рендерит превью в харнессе.
    /// На звуковом потоке нет выделений памяти. Не для WebGL (там нет OnAudioFilterRead).
    /// </summary>
    internal sealed class GrainMixer
    {
        public const int MaxVoices = 16;
        private const int RingSize = 512;

        private struct Pending
        {
            public float[] Clip;
            public double Time;
            public float Gain;
            public float Pitch;
        }

        private struct Voice
        {
            public float[] Clip;
            public double Position;
            public double Step;
            public float Gain;
            public int Delay;
        }

        private readonly Pending[] _ring = new Pending[RingSize];
        private int _write;
        private int _read;
        private readonly Pending[] _waiting = new Pending[256];
        private int _waitingCount;
        private readonly Voice[] _voices = new Voice[MaxVoices];
        private readonly int _clipRate;
        private int _clearRequests;
        private int _clearsDone;

        /// <summary>Сколько зёрен не влезло в очередь (главный поток писал быстрее, чем звук забирал).</summary>
        public int Dropped;

        /// <summary>Сколько раз голос отняли у самого старого зерна (все 16 заняты).</summary>
        public int Stolen;

        /// <summary>Наибольшее число одновременно звучащих зёрен.</summary>
        public int PeakVoices;

        public GrainMixer(int clipRate)
        {
            _clipRate = clipRate;
        }

        /// <summary>Главный поток: поставить зерно на время time (те же часы, что bufferStart в Render).</summary>
        public bool Schedule(float[] clip, double time, float gain, float pitch)
        {
            if (clip == null || gain <= 0f)
                return false;
            int w = _write;
            int next = (w + 1) & (RingSize - 1);
            if (next == Volatile.Read(ref _read))
            {
                Dropped++;
                return false;
            }
            _ring[w] = new Pending { Clip = clip, Time = time, Gain = gain, Pitch = pitch };
            Volatile.Write(ref _write, next);
            return true;
        }

        /// <summary>Главный поток: оборвать всё (обрыв лески) — звуковой поток очистит голоса в следующем буфере.</summary>
        public void Clear() => Interlocked.Increment(ref _clearRequests);

        /// <summary>
        /// Звуковой поток: добавить зёрна в data (чередование каналов, channels штук) — буфер начинается
        /// во время bufferStart (с) и идёт с частотой outputRate.
        /// </summary>
        public void Render(float[] data, int channels, int outputRate, double bufferStart, float master)
        {
            int frames = data.Length / channels;
            double bufferEnd = bufferStart + (double)frames / outputRate;

            int clears = Volatile.Read(ref _clearRequests);
            bool clear = clears != _clearsDone;
            _clearsDone = clears;

            int write = Volatile.Read(ref _write);
            while (_read != write)
            {
                if (!clear && _waitingCount < _waiting.Length)
                    _waiting[_waitingCount++] = _ring[_read];
                else if (!clear)
                    Dropped++;
                _ring[_read].Clip = null;
                Volatile.Write(ref _read, (_read + 1) & (RingSize - 1));
            }
            if (clear)
            {
                _waitingCount = 0;
                for (int v = 0; v < MaxVoices; v++)
                    _voices[v].Clip = null;
            }

            for (int i = 0; i < _waitingCount;)
            {
                if (_waiting[i].Time < bufferEnd)
                {
                    int offset = (int)Math.Round((_waiting[i].Time - bufferStart) * outputRate);
                    Start(_waiting[i], Math.Max(0, Math.Min(frames - 1, offset)), outputRate);
                    _waiting[i] = _waiting[--_waitingCount];
                    _waiting[_waitingCount].Clip = null;
                }
                else
                {
                    i++;
                }
            }

            int active = 0;
            for (int v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Clip == null)
                    continue;
                active++;
                ref var voice = ref _voices[v];
                var clip = voice.Clip;
                int last = clip.Length - 1;
                float gain = voice.Gain * master;
                double pos = voice.Position, step = voice.Step;
                for (int f = voice.Delay; f < frames; f++)
                {
                    int k = (int)pos;
                    if (k >= last)
                    {
                        voice.Clip = null;
                        break;
                    }
                    float frac = (float)(pos - k);
                    float sample = (clip[k] + (clip[k + 1] - clip[k]) * frac) * gain;
                    int o = f * channels;
                    for (int c = 0; c < channels; c++)
                        data[o + c] += sample;
                    pos += step;
                }
                voice.Position = pos;
                voice.Delay = 0;
            }
            if (active > PeakVoices)
                PeakVoices = active;
        }

        private void Start(in Pending p, int offset, int outputRate)
        {
            int slot = -1;
            double oldest = -1;
            for (int v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Clip == null)
                {
                    slot = v;
                    break;
                }
                double done = _voices[v].Position / _voices[v].Clip.Length;
                if (done > oldest)
                {
                    oldest = done;
                    slot = v;
                }
            }
            if (_voices[slot].Clip != null)
                Stolen++;
            _voices[slot] = new Voice
            {
                Clip = p.Clip,
                Position = 0,
                Step = (double)_clipRate / outputRate * p.Pitch,
                Gain = p.Gain,
                Delay = offset,
            };
        }
    }
}
