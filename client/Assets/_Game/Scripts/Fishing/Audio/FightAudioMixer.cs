using System;
using System.Threading;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Весь звук озера в одном месте — для звукового потока (OnAudioFilterRead источника с тихой петлёй в FightFeedback;
    /// офлайн — в харнессе tools/audio/AudioIngame, тот же код). Без движка, без блокировок, без выделений памяти в Render.
    ///
    /// Потоки:
    /// - главный: Publish(снимок параметров) — тройной буфер; Post(событие) — очередь одного писателя;
    /// - звуковой: Render(data, channels) — берёт последний снимок, разбирает события и считает буфер.
    ///
    /// Внутри Render, блоками до 512 сэмплов на частоте вывода (клипы 22050 Гц пересчитываются линейной интерполяцией
    /// под частоту вывода × высоту):
    /// 1. FightGrainScheduler ставит зёрна в их сэмпл — голоса зёрен (24, при нехватке отнимается самый доигранный);
    /// 2. разовые звуки — свои 8 голосов (старше 0,3 с с момента отправки не играются);
    /// 3. петли: укладка лески (с «волной раскладчика» ±10% на 520 см), гул лески, постель озера; громкость и высота
    ///    подтягиваются к снимку посэмплово (без «ступенек»), петля стартует с хешированного места, высота не «ныряет»;
    ///    укладка и гул (петли по 4 с) «блуждают»: каждые 35–65% длины клипа чтение перескакивает в хешированное место
    ///    (не ближе четверти клипа) с равномощной перекрёстной сменой 30 мс — у них нет периода, который можно выучить;
    /// 4. «шина катушки» (зёрна, укладка, гул) приглушается на 9 дБ, пока звучит тело разового звука (90% его энергии, не
    ///    короче 0,4 с — FightSoundBank.BodyLength; атака 5 мс, отпускание 250 мс): всплески, телеграфы, подсечка, «дзинь» выходят
    ///    над катушкой не громкостью, а местом в миксе, а тихие хвосты (капли, эхо) катушку уже не держат;
    /// 5. фон: днём — птицы по своему расписанию (иволга, «цип», серия кукушки раз в 7–19 с), ночью — два сверчка;
    ///    ночью постель мягче (срез 1,5 кГц) и тише на 30% — свои 8 голосов;
    /// 6. сумма × Makeup × общая громкость → SoftLimiter (−1 дБFS, мягкое колено) → во все каналы буфера.
    /// Почему всё в одном микшере, а не AudioSource на петли и разовые: лимитер видит весь бой (иначе щелчки трёх
    /// источников сложатся выше 0 дБFS), петли меняют громкость посэмплово, а харнесс слышит ровно то же, что игра.
    ///
    /// Защита звукового потока: каждый снимок проходит FightAudioParams.Sanitize (NaN, бесконечности, пределы),
    /// состояние петель и сглаживаний проверяется раз в блок, а сэмпл вне ±1 (NaN) на выходе заменяется тишиной —
    /// одно испорченное число не подвешивает поток и не глушит звук до конца сессии.
    /// </summary>
    public sealed class FightAudioMixer
    {
        public const int GrainVoices = 24;
        public const int FxVoices = 8;
        public const int AmbientVoices = 8;
        public const int MaxBlock = 512;

        /// <summary>
        /// Усиление всего микса перед лимитером: при общей громкости 0,8 (по умолчанию) бой звучит около −16 LUFS
        /// (правило 18; подобрано по боям окуня и леща в харнессе AudioIngame).
        /// </summary>
        public const float Makeup = 3.15f;

        /// <summary>Общая громкость по умолчанию (FightFeedback): под неё выставлены Makeup и предел разовых звуков.</summary>
        public const float DefaultMaster = 0.8f;

        /// <summary>Во сколько раз тише шина катушки, пока звучит разовый звук (−9 дБ).</summary>
        public const float CueDuck = 0.35f;

        private const double StrokeCm = 520.0;
        private const double LoopAttackSeconds = 0.04;
        private const double LoopPitchSeconds = 0.08;
        private const double AmbienceSeconds = 0.6;
        private const double NightSeconds = 2.0;
        private const double WindSeconds = 2.0;
        private const double WindCurve = 1.2;
        private const double MasterSeconds = 0.03;
        private const double DuckAttackSeconds = 0.005;
        private const double DuckReleaseSeconds = 0.25;
        private const double WanderFadeSeconds = 0.03;
        private const double WanderMin = 0.35;
        private const double WanderMax = 0.65;
        private const double WanderMinDistance = 0.25;
        private const float Silence = 0.002f;
        private const double StaleSeconds = 0.3;
        private const float NightCutoffHz = 1500f;
        private const float NightDim = 0.3f;

        private struct Voice
        {
            public float[] Clip;
            public double Pos;
            public double Step;
            public float Gain;
            public int Delay;

            /// <summary>Разовый звук приглушает катушку, пока не доиграл до этого сэмпла клипа (90% энергии, без хвоста).</summary>
            public int DuckEnd;
        }

        private struct Loop
        {
            public float[] Clip;
            public double Pos;
            public bool Playing;
            public float Volume;
            public float Pitch;
            public uint Seed;

            /// <summary>Петля «блуждает» (укладка, гул): чтение перескакивает с перекрёстной сменой.</summary>
            public bool Wander;

            /// <summary>Сколько сэмплов клипа прочитано с последнего перескока и когда следующий (в сэмплах клипа).</summary>
            public double Since, JumpAt;

            /// <summary>Старое место чтения и сколько сэмплов вывода осталось до конца смены (0 — смены нет).</summary>
            public double OldPos;

            public int Fade;
        }

        private readonly FightSoundBank _bank;
        private readonly uint _seed;
        private readonly SnapshotBuffer<FightAudioParams> _params = new SnapshotBuffer<FightAudioParams>();
        private readonly SpscQueue<FightAudioEvent> _events = new SpscQueue<FightAudioEvent>(256);
        private readonly FightGrainScheduler _scheduler;
        private readonly SoftLimiter _limiter = new SoftLimiter();
        private readonly GrainStart[] _starts = new GrainStart[256];
        private readonly Voice[] _grains = new Voice[GrainVoices];
        private readonly Voice[] _fx = new Voice[FxVoices];
        private readonly Voice[] _ambient = new Voice[AmbientVoices];
        private readonly float[] _mix = new float[MaxBlock];
        private readonly float[] _reel = new float[MaxBlock];
        private readonly float[] _bed = new float[MaxBlock];
        private Loop _lay, _hum, _lake, _windLoop;
        private double _wind;
        private FightAudioParams _p;
        private long _rendered;
        private double _master;
        private double _duck = 1;
        private double _ambience;
        private double _night;
        private double _lp1, _lp2;
        private bool _started;
        private double _birdWait = 4;
        private long _birdIndex;
        private readonly double[] _cricketWait = { 0.3, 0.75 };
        private readonly long[] _cricketIndex = new long[2];

        // ---------- Статистика (пишет звуковой поток; читать там же или для отчёта) ----------

        public int PeakGrainVoices { get; private set; }
        public long StolenVoices { get; private set; }
        public long StaleOneShots { get; private set; }
        public long OneShotsPlayed { get; private set; }
        public long BirdCalls { get; private set; }
        public long CricketChirps { get; private set; }

        /// <summary>Сколько сэмплов вывода пришлось заменить тишиной (NaN или вне ±1) — в исправном микшере 0.</summary>
        public long BadSamples { get; private set; }

        /// <summary>Сколько раз состояние (петли, сглаживания) пришлось сбросить из-за NaN — в исправном микшере 0.</summary>
        public long StateResets { get; private set; }

        /// <summary>Сколько сэмплов вывода шина катушки была приглушена больше чем на 1 дБ (для отчёта харнесса).</summary>
        public long DuckedFrames { get; private set; }

        /// <summary>Зёрна по видам (индекс — FightGrain) с создания микшера.</summary>
        public long[] GrainCounts => _scheduler.Counts;

        public SoftLimiter Limiter => _limiter;

        /// <summary>Необязательный журнал зёрен (харнесс): в массив пишутся начала зёрен, пока есть место.</summary>
        public GrainLogEntry[] GrainLog;

        public int GrainLogCount;

        public FightAudioMixer(FightSoundBank bank, uint seed = 0x5EEDu)
        {
            _bank = bank ?? throw new ArgumentNullException(nameof(bank));
            _seed = seed;
            _scheduler = new FightGrainScheduler(seed);
            _lay = new Loop { Clip = bank.LayBed, Seed = 0x1A70u ^ seed, Pitch = 1f, Wander = true };
            _hum = new Loop { Clip = bank.LineHum, Seed = 0x4C40u ^ seed, Pitch = 1f, Wander = true };
            _lake = new Loop { Clip = bank.LakeBed, Seed = 0xA3B0u ^ seed, Pitch = 1f, Playing = true };
            _lake.Pos = SynthKit.Hash(_lake.Seed) % (uint)bank.LakeBed.Length;
            _windLoop = new Loop { Clip = bank.WindBed, Seed = 0x71D0u ^ seed, Pitch = 1f };
        }

        // =================================================================================================
        // Главный поток
        // =================================================================================================

        /// <summary>Главный поток: новый снимок параметров (раз в кадр).</summary>
        public void Publish(in FightAudioParams p) => _params.Publish(p);

        /// <summary>Главный поток: событие (разовый звук, подсечка, обрыв, трогание фрикциона). false — очередь полна.</summary>
        public bool Post(FightAudioEvent e)
        {
            e.PostedAtFrame = RenderedFrames;
            return _events.TryWrite(e);
        }

        /// <summary>Сколько сэмплов на канал уже выдано (часы звукового потока).</summary>
        public long RenderedFrames => Interlocked.Read(ref _rendered);

        // =================================================================================================
        // Звуковой поток
        // =================================================================================================

        /// <summary>Звуковой поток: заполнить data (каналы чередуются, channels штук) целиком — содержимое перезаписывается.</summary>
        public void Render(float[] data, int channels)
        {
            if (data == null || channels <= 0)
                return;
            _params.Read(out _p);
            _p.Sanitize();
            int rate = _p.OutputRate;
            int frames = data.Length / channels;
            if (rate <= 0)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            _limiter.SetRate(rate);
            if (!_started)
            {
                // Первый буфер: ночь — сразу как есть (иначе ночью в первые секунды запели бы птицы), фон — плавно с нуля.
                _started = true;
                _night = _p.Night;
            }
            RecoverState();
            DrainEvents(rate);

            int done = 0;
            while (done < frames)
            {
                int n = Math.Min(MaxBlock, frames - done);
                RenderBlock(n, rate);
                int o = done * channels;
                for (int f = 0; f < n; f++)
                {
                    float v = _mix[f];
                    // После лимитера |v| ≤ 0,99; иное — NaN из испорченного состояния: тишина вместо «отравы» дальше по цепочке.
                    if (!(v >= -1f && v <= 1f))
                    {
                        v = 0f;
                        BadSamples++;
                    }
                    for (int c = 0; c < channels; c++)
                        data[o++] = v;
                }
                done += n;
                Interlocked.Add(ref _rendered, n);
            }
        }

        /// <summary>Сглаженные величины и петли — без NaN и бесконечностей (раз в буфер; в исправной работе ничего не делает).</summary>
        private void RecoverState()
        {
            bool bad = false;
            if (!SafeMath.IsFinite(_master)) { _master = 0; bad = true; }
            if (!SafeMath.IsFinite(_ambience)) { _ambience = 0; bad = true; }
            if (!SafeMath.IsFinite(_night)) { _night = _p.Night; bad = true; }
            if (!SafeMath.IsFinite(_duck)) { _duck = 1; bad = true; }
            if (!SafeMath.IsFinite(_lp1) || !SafeMath.IsFinite(_lp2)) { _lp1 = _lp2 = 0; bad = true; }
            if (!SafeMath.IsFinite(_wind)) { _wind = 0; bad = true; }
            bad |= RecoverLoop(ref _lay) | RecoverLoop(ref _hum) | RecoverLoop(ref _lake) | RecoverLoop(ref _windLoop);
            if (bad)
                StateResets++;
        }

        private static bool RecoverLoop(ref Loop loop)
        {
            if (loop.Clip == null)
                return false;
            int len = loop.Clip.Length;
            bool bad = false;
            if (!SafeMath.IsFinite(loop.Volume)) { loop.Volume = 0f; bad = true; }
            if (!(loop.Pitch > 0f && loop.Pitch < 100f)) { loop.Pitch = 1f; bad = true; }
            if (!(loop.Pos >= 0 && loop.Pos < len)) { loop.Pos = 0; bad = true; }
            if (!(loop.OldPos >= 0 && loop.OldPos < len)) { loop.OldPos = 0; loop.Fade = 0; bad = true; }
            if (!SafeMath.IsFinite(loop.Since) || !SafeMath.IsFinite(loop.JumpAt)) { loop.Since = 0; loop.JumpAt = 0.5 * len; bad = true; }
            return bad;
        }

        private void DrainEvents(int rate)
        {
            long now = _rendered;
            int count = 0;
            while (_events.TryRead(out var e))
            {
                switch (e.Kind)
                {
                    case FightAudioEventKind.FightStart:
                        _scheduler.Reset();
                        break;
                    case FightAudioEventKind.Cut:
                        for (int v = 0; v < _grains.Length; v++)
                            _grains[v].Clip = null;
                        _lay.Playing = false;
                        _lay.Volume = 0f;
                        _hum.Playing = false;
                        _hum.Volume = 0f;
                        break;
                    case FightAudioEventKind.DragBreakaway:
                        _scheduler.Breakaway(_starts, ref count);
                        break;
                    case FightAudioEventKind.OneShot:
                        if (now - e.PostedAtFrame > (long)(StaleSeconds * rate))
                        {
                            StaleOneShots++;
                            break;
                        }
                        var clip = _bank.OneShot(e.Sound, e.Variant, e.Far);
                        float volume = SafeMath.Clamp(e.Volume, 0f, 1f);
                        if (clip != null && volume > 0f)
                        {
                            int slot = Start(_fx, clip, 1f, volume, 0, rate);
                            if (slot >= 0)
                                _fx[slot].DuckEnd = _bank.BodyLength(clip);
                            OneShotsPlayed++;
                        }
                        break;
                }
            }
            StartGrains(_starts, count, rate);
        }

        private void RenderBlock(int n, int rate)
        {
            Array.Clear(_mix, 0, n);
            Array.Clear(_reel, 0, n);
            Array.Clear(_bed, 0, n);
            double dt = (double)n / rate;

            // 1. Зёрна этого блока.
            int count = 0;
            double strokeFrom = Stroke();
            _scheduler.Advance(_p, n, rate, _starts, ref count);
            double strokeTo = Stroke();
            StartGrains(_starts, count, rate);

            // 2. Зёрна — в шину катушки, разовые — сразу в микс (и пока хоть один звучит — катушку приглушить).
            bool cue = false;
            for (int v = 0; v < _fx.Length; v++)
                cue |= _fx[v].Clip != null && _fx[v].Pos < _fx[v].DuckEnd;
            int active = RenderVoices(_grains, _reel, n, 1f);
            if (active > PeakGrainVoices)
                PeakGrainVoices = active;
            RenderVoices(_fx, _mix, n, 1f);

            // 3. Петли катушки — в её шину.
            float release = _p.LoopReleaseSeconds > 0 ? _p.LoopReleaseSeconds : 0.4f;
            RenderLoop(ref _lay, _p.LayVolume, _p.LayPitch, release, strokeFrom, strokeTo, _reel, n, rate);
            RenderLoop(ref _hum, _p.HumVolume, _p.HumPitch, release, 1, 1, _reel, n, rate);

            // 4. Шина катушки в микс — с приглушением под разовые звуки.
            double duckTarget = cue ? CueDuck : 1.0;
            double aDuck = 1 - Math.Exp(-1.0 / ((duckTarget < _duck ? DuckAttackSeconds : DuckReleaseSeconds) * rate));
            double d = _duck;
            int ducked = 0;
            for (int i = 0; i < n; i++)
            {
                d += (duckTarget - d) * aDuck;
                _mix[i] += (float)(_reel[i] * d);
                if (d < 0.891)
                    ducked++;
            }
            _duck = d;
            DuckedFrames += ducked;

            // 5. Фон: постель, ночной срез, птицы и сверчки.
            Ambient(n, rate, dt);

            // 6. Общая громкость и лимитер.
            double target = _p.Master * Makeup;
            double am = 1 - Math.Exp(-1.0 / (MasterSeconds * rate));
            double m = _master;
            for (int i = 0; i < n; i++)
            {
                m += (target - m) * am;
                _mix[i] = (float)(_mix[i] * m);
            }
            _master = m;
            _limiter.Process(_mix, n);
        }

        private double Stroke() => 1 + 0.10 * Math.Sin(2 * Math.PI * _scheduler.RotorDistanceCm / StrokeCm);

        private void StartGrains(GrainStart[] starts, int count, int rate)
        {
            for (int k = 0; k < count; k++)
            {
                var g = starts[k];
                var clip = _bank.Grain(g.Kind, g.Variant);
                Start(_grains, clip, g.Pitch, g.Gain, Math.Max(0, g.Offset), rate);
                if (GrainLog != null && GrainLogCount < GrainLog.Length)
                    GrainLog[GrainLogCount++] = new GrainLogEntry
                    {
                        Frame = _rendered + Math.Max(0, g.Offset), Kind = g.Kind, Variant = g.Variant, Gain = g.Gain, Pitch = g.Pitch,
                    };
            }
        }

        /// <summary>Начать голос; вернуть его номер в пуле (−1 — клип пустой).</summary>
        private int Start(Voice[] pool, float[] clip, float pitch, float gain, int delay, int rate)
        {
            if (clip == null || clip.Length < 2)
                return -1;
            int slot = -1;
            double most = -1;
            for (int v = 0; v < pool.Length; v++)
            {
                if (pool[v].Clip == null)
                {
                    slot = v;
                    break;
                }
                // Отнимается самый доигранный (ещё не начавшиеся — в последнюю очередь).
                double done = pool[v].Delay > 0 ? -1 + 1e-9 * (pool.Length - v) : pool[v].Pos / pool[v].Clip.Length;
                if (done > most)
                {
                    most = done;
                    slot = v;
                }
            }
            if (pool[slot].Clip != null)
                StolenVoices++;
            pool[slot] = new Voice
            {
                Clip = clip,
                Pos = 0,
                Step = (double)FightSoundBank.SampleRate / rate * pitch,
                Gain = gain,
                Delay = delay,
            };
            return slot;
        }

        /// <summary>Подмешать голоса пула в dst; вернуть число звучащих.</summary>
        private static int RenderVoices(Voice[] pool, float[] dst, int n, float busGain)
        {
            int active = 0;
            for (int v = 0; v < pool.Length; v++)
            {
                if (pool[v].Clip == null)
                    continue;
                ref var voice = ref pool[v];
                if (voice.Delay >= n)
                {
                    voice.Delay -= n;
                    continue;
                }
                active++;
                var clip = voice.Clip;
                int last = clip.Length - 1;
                float gain = voice.Gain * busGain;
                double pos = voice.Pos, step = voice.Step;
                for (int f = voice.Delay; f < n; f++)
                {
                    int k = (int)pos;
                    if (k >= last)
                    {
                        voice.Clip = null;
                        break;
                    }
                    float frac = (float)(pos - k);
                    dst[f] += (clip[k] + (clip[k + 1] - clip[k]) * frac) * gain;
                    pos += step;
                }
                voice.Pos = pos;
                voice.Delay = 0;
            }
            return active;
        }

        /// <summary>
        /// Петля: громкость (атака 40 мс, отпускание release) и высота (80 мс) — посэмплово; gainFrom→gainTo — множитель
        /// по блоку. «Блуждающая» петля (Wander) каждые WanderMin–WanderMax длины клипа перескакивает в хешированное место
        /// (не ближе WanderMinDistance клипа к текущему) с равномощной сменой WanderFadeSeconds: старое и новое места
        /// звучат вместе с весами cos/sin, громкость шумовой постели не проседает.
        /// </summary>
        private static void RenderLoop(ref Loop loop, float targetVolume, float targetPitch, float release, double gainFrom, double gainTo,
            float[] dst, int n, int rate)
        {
            if (loop.Clip == null)
                return;
            var clip = loop.Clip;
            int len = clip.Length;
            if (!loop.Playing)
            {
                if (targetVolume <= 0f)
                    return;
                loop.Playing = true;
                loop.Seed = SynthKit.Hash(loop.Seed + 0x9E37u);
                loop.Pos = loop.Seed % (uint)len;
                loop.Pitch = targetPitch > 0 ? targetPitch : 1f;
                loop.Volume = 0f;
                loop.Fade = 0;
                loop.Since = 0;
                loop.JumpAt = len * (WanderMin + (WanderMax - WanderMin) * Unit(loop.Seed >> 8));
            }
            if (targetPitch <= 0f)
                targetPitch = loop.Pitch;
            double aUp = 1 - Math.Exp(-1.0 / (LoopAttackSeconds * rate));
            double aDown = 1 - Math.Exp(-1.0 / (Math.Max(0.005, release) * rate));
            double aPitch = 1 - Math.Exp(-1.0 / (LoopPitchSeconds * rate));
            double vol = loop.Volume, pitch = loop.Pitch, pos = loop.Pos, old = loop.OldPos, since = loop.Since;
            int fade = loop.Fade, fadeLen = Math.Max(1, (int)(WanderFadeSeconds * rate));
            double baseStep = (double)FightSoundBank.SampleRate / rate;
            for (int i = 0; i < n; i++)
            {
                vol += (targetVolume - vol) * (targetVolume > vol ? aUp : aDown);
                pitch += (targetPitch - pitch) * aPitch;
                double g = vol * (gainFrom + (gainTo - gainFrom) * i / n);
                double step = baseStep * pitch;
                double x = Read(clip, pos);
                if (fade > 0)
                {
                    double w = 0.5 * Math.PI * (1 - (double)fade / fadeLen);
                    x = x * Math.Sin(w) + Read(clip, old) * Math.Cos(w);
                    old = Wrap(old + step, len);
                    fade--;
                }
                dst[i] += (float)(x * g);
                pos = Wrap(pos + step, len);
                if (loop.Wander)
                {
                    since += step;
                    if (fade == 0 && since >= loop.JumpAt)
                    {
                        loop.Seed = SynthKit.Hash(loop.Seed + 0x9E37u);
                        double next = loop.Seed % (uint)len;
                        double dist = Math.Abs(next - pos);
                        if (Math.Min(dist, len - dist) < WanderMinDistance * len)
                            next = Wrap(next + 0.5 * len, len);
                        old = pos;
                        pos = next;
                        fade = fadeLen;
                        since = 0;
                        loop.JumpAt = len * (WanderMin + (WanderMax - WanderMin) * Unit(loop.Seed >> 8));
                    }
                }
            }
            loop.Volume = (float)vol;
            loop.Pitch = (float)pitch;
            loop.Pos = pos;
            loop.OldPos = old;
            loop.Since = since;
            loop.Fade = fade;
            if (targetVolume <= 0f && vol < Silence)
            {
                loop.Playing = false;
                loop.Volume = 0f;
            }
        }

        /// <summary>Сэмпл петли в дробном месте pos (линейная интерполяция, за концом — начало).</summary>
        private static double Read(float[] clip, double pos)
        {
            int k = (int)pos;
            int k1 = k + 1 >= clip.Length ? 0 : k + 1;
            float frac = (float)(pos - k);
            return clip[k] + (clip[k1] - clip[k]) * frac;
        }

        /// <summary>pos в [0, len) для петли (шаг всегда меньше длины клипа; остаток — на всякий случай).</summary>
        private static double Wrap(double pos, int len)
        {
            if (pos >= len)
            {
                pos -= len;
                if (pos >= len)
                    pos %= len;
            }
            return pos;
        }

        // ---------- Фон ----------

        private void Ambient(int n, int rate, double dt)
        {
            // Ночь и громкость фона — медленно (ночь — 2 с, приглушение на вываживание — 0,6 с), по блоку.
            _night += (_p.Night - _night) * (1 - Math.Exp(-dt / NightSeconds));
            double ambFrom = _ambience;
            _ambience += (_p.AmbienceVolume - _ambience) * (1 - Math.Exp(-dt / AmbienceSeconds));
            double night = _night;
            double dimFrom = ambFrom * (1 - NightDim * night), dimTo = _ambience * (1 - NightDim * night);

            ScheduleBirds(rate, dt, night);
            ScheduleCrickets(rate, dt, night);

            // Постель — петля без конца; ночью смешивается со своей копией через два однополюсных ФНЧ 1,5 кГц.
            RenderLoop(ref _lake, 1f, 1f, 1f, 1, 1, _bed, n, rate);
            // Ветер — отдельной петлёй поверх спокойного озера: сила меняется плавно (около 2 с), в штиль слой молчит.
            // Громкость растёт чуть быстрее силы ветра: 2 м/с — лёгкий шелест, 8 м/с — как прежний полный фон.
            _wind += (_p.Wind - _wind) * (1 - Math.Exp(-dt / WindSeconds));
            float windVolume = _wind < 0.02 ? 0f : (float)Math.Pow(_wind, WindCurve);
            RenderLoop(ref _windLoop, windVolume, 1f, 1.5f, 1, 1, _bed, n, rate);
            double a = 1 - Math.Exp(-2 * Math.PI * NightCutoffHz / rate);
            double lp1 = _lp1, lp2 = _lp2;
            for (int i = 0; i < n; i++)
            {
                double x = _bed[i];
                lp1 += (x - lp1) * a;
                lp2 += (lp1 - lp2) * a;
                _bed[i] = (float)(x + (lp2 - x) * night);
            }
            _lp1 = lp1;
            _lp2 = lp2;

            // Птицы и сверчки — мимо ночного среза (сверчок на 4,3 кГц), но под той же громкостью фона.
            RenderVoices(_ambient, _bed, n, 1f);
            for (int i = 0; i < n; i++)
                _mix[i] += (float)(_bed[i] * (dimFrom + (dimTo - dimFrom) * i / n));
        }

        /// <summary>Днём птицы: раз в 7–19 с иволга (3 из 6), «цип» (2 из 6) или серия из пяти «ку-ку» (1 из 6); громкость × (1 − ночь).</summary>
        private void ScheduleBirds(int rate, double dt, double night)
        {
            _birdWait -= dt;
            if (_birdWait > 0)
                return;
            _birdIndex++;
            uint h = SynthKit.Hash((uint)_birdIndex * 0x2C1B3C6Du + 0xB12Du ^ _seed);
            _birdWait = 7 + 12 * Unit(h >> 8);
            double day = 1 - night;
            if (day < 0.05)
                return;
            int kind = (int)(h % 6);
            int variant = (int)((h >> 20) & 0xFF);
            if (kind < 3)
            {
                Start(_ambient, _bank.Orioles[variant % _bank.Orioles.Length], 1f, (float)day, 0, rate);
            }
            else if (kind < 5)
            {
                Start(_ambient, _bank.Chirps[variant % _bank.Chirps.Length], 1f, (float)day, 0, rate);
            }
            else
            {
                double at = 0;
                for (int k = 0; k < 5; k++)
                {
                    uint hk = SynthKit.Hash(h + (uint)k * 0x9E3779B9u);
                    Start(_ambient, _bank.Cuckoos[k % _bank.Cuckoos.Length], 1f, (float)(day * (1 - 0.04 * k)), (int)(at * rate), rate);
                    at += 0.82 + 0.10 * Unit(hk);
                }
            }
            BirdCalls++;
        }

        /// <summary>Ночью два сверчка: трель раз в 0,42 и 0,55 с (±4%), изредка пауза 2–6 с; громкость × ночь.</summary>
        private void ScheduleCrickets(int rate, double dt, double night)
        {
            for (int c = 0; c < 2; c++)
            {
                _cricketWait[c] -= dt;
                if (_cricketWait[c] > 0)
                    continue;
                _cricketIndex[c]++;
                uint h = SynthKit.Hash((uint)_cricketIndex[c] * 0x7FEB352Du + (uint)c * 0x68E31DA4u ^ _seed);
                double period = c == 0 ? 0.42 : 0.55;
                bool pause = Unit(h >> 4) < 0.05;
                _cricketWait[c] = pause ? 2 + 4 * Unit(h >> 12) : period * (0.96 + 0.08 * Unit(h >> 12));
                if (night < 0.05 || pause)
                    continue;
                int offset = (int)(Unit(h >> 20) * dt * rate);
                float gain = (float)(night * (c == 0 ? 1.0 : 0.7));
                Start(_ambient, _bank.Crickets[(int)(h % (uint)_bank.Crickets.Length)], (float)(1 + 0.02 * (2 * Unit(h >> 24) - 1)), gain, offset, rate);
                CricketChirps++;
            }
        }

        private static double Unit(uint h) => (h & 0xFFFF) / 65535.0;

    }

    /// <summary>Запись журнала зёрен: абсолютный сэмпл начала (часы микшера), вид, вариант, громкость, высота.</summary>
    public struct GrainLogEntry
    {
        public long Frame;
        public FightGrain Kind;
        public int Variant;
        public float Gain;
        public float Pitch;
    }
}
