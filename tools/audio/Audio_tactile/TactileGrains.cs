using System;

namespace Fishing.Unity
{
    internal enum GrainKind : byte { Detent, Drag, Strain, Creak, Rotor, DragAccent }

    /// <summary>Pre-rendered grains at the output sample rate (AudioSettings.outputSampleRate in Unity). ~0.6 MB at 48 kHz.</summary>
    internal sealed class TactileGrainBank
    {
        public readonly int SampleRate;
        public readonly float[][] Detent, DragBright, DragDark, Strain, CreakSoft, CreakHard, Rotor;

        public TactileGrainBank(int sampleRate)
        {
            SampleRate = sampleRate;
            Detent = Make(TactileSynth.DetentVariants, v => TactileSynth.Detent(v, sampleRate));
            DragBright = Make(TactileSynth.DragVariants, v => TactileSynth.DragTick(v, false, sampleRate));
            DragDark = Make(TactileSynth.DragVariants, v => TactileSynth.DragTick(v, true, sampleRate));
            Strain = Make(TactileSynth.StrainVariants, v => TactileSynth.Strain(v, sampleRate));
            CreakSoft = Make(TactileSynth.CreakVariants, v => TactileSynth.RodCreak(v, false, sampleRate));
            CreakHard = Make(TactileSynth.CreakVariants, v => TactileSynth.RodCreak(v, true, sampleRate));
            Rotor = Make(TactileSynth.RotorVariants, v => TactileSynth.RotorSwish(v, sampleRate));
        }

        private static float[][] Make(int n, Func<int, float[]> f)
        {
            var a = new float[n][];
            for (int i = 0; i < n; i++) a[i] = f(i);
            return a;
        }
    }

    /// <summary>
    /// Grain voice for the audio thread (Unity: OnAudioFilterRead on one AudioSource, 16 voices).
    /// The main thread writes rate/gain targets once per frame (TactileCues → here); this class integrates
    /// four "mechanical oscillators" sample-accurately and fires pre-rendered grains:
    /// - detent: phase += rate/fs; a detent each time the phase passes 1 ± 3% (hash of the detent index);
    ///   every 8th detent (one handle turn) is accented +1 dB and 3.5% lower; pitch 1 − 0.1·heavy, ±0.25 st, gain ±0.8 dB;
    /// - drag: the same with ±3% (a ratchet is regular); first 3 ticks of a run +1.5/+1/+0.5 dB; above 14 ticks/s
    ///   gain × √(14/rate) (±0.8 dB) and dark variants with probability (rate − 14)/40 (max 0.85);
    /// - rotor swish: rides the detent rate × 5.2/8 (rotor turns per handle turn / detents per turn), ±4%,
    ///   so swishes and detents never lock in step; gain from the main thread (line tension);
    /// - strain and rod creak: irregular events (strain intervals ~exponential, clamped 0.35–2.2× the mean;
    ///   creak 0.65–1.35×), pitch ±0.6 st, gain ±2 dB.
    /// Variants are random-exclusive (never the same twice in a row) from a hash of the event index, so the
    /// same FightFrame stream gives the same grains for a spectator. No allocation, no locks: targets are
    /// plain float writes, one-off triggers go through a single-producer/single-consumer ring.
    /// </summary>
    internal sealed class TactileGrains
    {
        public const int MaxVoices = 16;
        private const int DetentsPerTurn = 8;
        private const double RotorPerDetent = 5.2 / DetentsPerTurn;

        // ---- Targets: main thread writes, audio thread reads ----
        public volatile float DetentRate, DetentHeavy, DetentGain, RotorGain;
        public volatile float DragRate, DragGain;
        public volatile float StrainRate, StrainGain;
        public volatile float CreakRate, CreakGain, CreakHard;

        private readonly TactileGrainBank _bank;
        private readonly double _fs;

        // SPSC ring for one-off grains (Breakaway accent).
        private readonly int[] _ring = new int[32];
        private volatile int _head, _tail;

        // Voices.
        private readonly float[][] _vClip = new float[MaxVoices][];
        private readonly double[] _vPos = new double[MaxVoices];
        private readonly float[] _vStep = new float[MaxVoices];
        private readonly float[] _vGain = new float[MaxVoices];
        private readonly byte[] _vKind = new byte[MaxVoices];

        // Oscillators.
        private double _detentRate, _detentPhase, _detentThresh = 1;
        private double _dragRate, _dragPhase, _dragThresh = 1;
        private double _strainPhase, _strainThresh = 1;
        private double _creakPhase, _creakThresh = 1;
        private double _rotorPhase = 0.45, _rotorThresh = 1;
        private long _rotorIdx;
        private int _lastRotorV = -1;
        private long _detentIdx, _dragIdx, _strainIdx, _creakIdx;
        private int _dragRun, _dragIdleSamples;
        private float _lastDetentTarget;
        private int _lastDetentV = -1, _lastDragV = -1, _lastStrainV = -1, _lastCreakV = -1;
        private float[] _scratch = new float[0];

        // ---- Analysis hooks (null in the game) ----
        public Action<GrainKind, int, float, float, long> OnFire;   // kind, variant, gain, pitch, sample
        public float[][] KindStems;                                   // [5][] mono stems per kind (detent, drag, strain, creak, rotor)
        public long SampleClock;
        public int PeakVoices;

        public TactileGrains(TactileGrainBank bank)
        {
            _bank = bank;
            _fs = bank.SampleRate;
        }

        /// <summary>Main thread: queue a one-off grain (dropped if the ring is full).</summary>
        public void Trigger(GrainKind kind)
        {
            int h = _head, next = (h + 1) & 31;
            if (next == _tail) return;
            _ring[h] = (int)kind;
            _head = next;
        }

        /// <summary>Unity: call from OnAudioFilterRead(data, channels) — adds the grains to every channel.</summary>
        public void Render(float[] data, int channels)
        {
            int frames = data.Length / channels;
            if (_scratch.Length < frames) _scratch = new float[frames];
            Array.Clear(_scratch, 0, frames);
            RenderMono(_scratch, 0, frames, 0);
            for (int i = 0, j = 0; i < frames; i++)
                for (int c = 0; c < channels; c++, j++)
                    data[j] += _scratch[i];
        }

        /// <summary>Adds `count` mono samples into buf[offset..]. stemOffset: where KindStems are written (analysis).</summary>
        public void RenderMono(float[] buf, int offset, int count, int stemOffset)
        {
            // One-off triggers.
            while (_tail != _head)
            {
                var k = (GrainKind)_ring[_tail];
                _tail = (_tail + 1) & 31;
                if (k == GrainKind.DragAccent)
                {
                    _dragRun = 0;
                    _dragPhase = 0;
                    FireDrag(Math.Max(1.0, _dragRate), offset);
                }
            }

            float detentTarget = DetentRate, dragTarget = DragRate;
            if (detentTarget > 0 && _lastDetentTarget <= 0)
                _detentPhase = Math.Max(_detentPhase, 0.8 * _detentThresh); // handle engages: first detent comes soon
            _lastDetentTarget = detentTarget;
            double kDetent = 1 - Math.Exp(-1.0 / ((detentTarget > _detentRate ? 0.05 : 0.03) * _fs));
            double kDrag = 1 - Math.Exp(-1.0 / (0.025 * _fs));
            double strainRate = StrainRate, creakRate = CreakRate;

            for (int i = 0; i < count; i++)
            {
                long now = SampleClock + i;
                _detentRate += (detentTarget - _detentRate) * kDetent;
                if (_detentRate > 0.02)
                {
                    _detentPhase += _detentRate / _fs;
                    if (_detentPhase >= _detentThresh)
                    {
                        _detentPhase -= _detentThresh;
                        FireDetent(i + offset, now);
                    }
                    _rotorPhase += _detentRate * RotorPerDetent / _fs;
                    if (_rotorPhase >= _rotorThresh)
                    {
                        _rotorPhase -= _rotorThresh;
                        if (RotorGain > 0.0005f) FireRotor(i + offset, now);
                    }
                }

                _dragRate += (dragTarget - _dragRate) * kDrag;
                if (dragTarget <= 0)
                {
                    if (++_dragIdleSamples > 0.15 * _fs) _dragRun = 0;
                }
                else _dragIdleSamples = 0;
                if (_dragRate > 0.05)
                {
                    _dragPhase += _dragRate / _fs;
                    if (_dragPhase >= _dragThresh)
                    {
                        _dragPhase -= _dragThresh;
                        FireDrag(_dragRate, i + offset, now);
                    }
                }

                if (strainRate > 0)
                {
                    _strainPhase += strainRate / _fs;
                    if (_strainPhase >= _strainThresh)
                    {
                        _strainPhase -= _strainThresh;
                        FireStrain(i + offset, now);
                    }
                }

                if (creakRate > 0)
                {
                    _creakPhase += creakRate / _fs;
                    if (_creakPhase >= _creakThresh)
                    {
                        _creakPhase -= _creakThresh;
                        FireCreak(i + offset, now);
                    }
                }
            }

            // Voices.
            int active = 0;
            for (int v = 0; v < MaxVoices; v++)
            {
                var clip = _vClip[v];
                if (clip == null) continue;
                active++;
                double pos = _vPos[v];
                float step = _vStep[v], g = _vGain[v];
                int last = clip.Length - 1;
                float[] stem = KindStems != null ? KindStems[_vKind[v]] : null;
                int start = _vStartAt[v] > offset ? _vStartAt[v] - offset : 0;
                _vStartAt[v] = 0;
                for (int i = start; i < count; i++)
                {
                    int i0 = (int)pos;
                    if (i0 >= last) { clip = null; break; }
                    float frac = (float)(pos - i0);
                    float y = (clip[i0] + (clip[i0 + 1] - clip[i0]) * frac) * g;
                    buf[offset + i] += y;
                    if (stem != null && stemOffset + i < stem.Length) stem[stemOffset + i] += y;
                    pos += step;
                }
                _vPos[v] = pos;
                if (clip == null) _vClip[v] = null;
            }
            if (active > PeakVoices) PeakVoices = active;
            SampleClock += count;
        }

        private readonly int[] _vStartAt = new int[MaxVoices];

        private void Start(float[] clip, float pitch, float gain, GrainKind kind, int at)
        {
            int slot = -1;
            double oldest = -1;
            for (int v = 0; v < MaxVoices; v++)
            {
                if (_vClip[v] == null) { slot = v; break; }
                double age = _vPos[v] / _vClip[v].Length;
                if (age > oldest) { oldest = age; slot = v; }
            }
            _vClip[slot] = clip;
            _vPos[slot] = 0;
            _vStep[slot] = pitch;
            _vGain[slot] = gain;
            _vKind[slot] = (byte)(kind == GrainKind.DragAccent ? GrainKind.Drag : kind);
            _vStartAt[slot] = at; // first sample inside this block
        }

        private static int Pick(uint h, int n, ref int last)
        {
            int v = (int)(h % (uint)n);
            if (v == last) v = (v + 1 + (int)((h >> 16) % (uint)(n - 1))) % n;
            last = v;
            return v;
        }

        private static float Db(double db) => (float)Math.Pow(10, db / 20);
        private static float Semis(double st) => (float)Math.Pow(2, st / 12);

        private void FireDetent(int at, long now)
        {
            long idx = _detentIdx++;
            uint h = THash.Of(0xD0u, idx);
            int v = Pick(h, _bank.Detent.Length, ref _lastDetentV);
            bool accent = idx % DetentsPerTurn == 0;
            float pitch = (float)(1 - 0.10 * DetentHeavy) * Semis(0.25 * THash.S(THash.Mix(h + 1))) * (accent ? 0.965f : 1f);
            float gain = DetentGain * Db(0.8 * THash.S(THash.Mix(h + 2))) * (accent ? Db(1.0) : 1f);
            _detentThresh = 1 + 0.03 * THash.S(THash.Mix(h + 3));
            Start(_bank.Detent[v], pitch, gain, GrainKind.Detent, at);
            OnFire?.Invoke(GrainKind.Detent, v, gain, pitch, now);
        }

        private void FireRotor(int at, long now)
        {
            long idx = _rotorIdx++;
            uint h = THash.Of(0xD4u, idx);
            int v = Pick(h, _bank.Rotor.Length, ref _lastRotorV);
            float pitch = (float)(1 - 0.15 * DetentHeavy) * Semis(0.3 * THash.S(THash.Mix(h + 1)));
            float gain = RotorGain * Db(1.5 * THash.S(THash.Mix(h + 2)));
            _rotorThresh = 1 + 0.04 * THash.S(THash.Mix(h + 3));
            Start(_bank.Rotor[v], pitch, gain, GrainKind.Rotor, at);
            OnFire?.Invoke(GrainKind.Rotor, v, gain, pitch, now);
        }

        private void FireDrag(double rate, int at, long now = -1)
        {
            long idx = _dragIdx++;
            uint h = THash.Of(0xD1u, idx);
            double dark = Math.Max(0, Math.Min(0.85, (rate - 14) / 40));
            bool useDark = THash.U(THash.Mix(h + 4)) < dark;
            var set = useDark ? _bank.DragDark : _bank.DragBright;
            int v = Pick(h, set.Length, ref _lastDragV);
            double accent = _dragRun == 0 ? 1.5 : _dragRun == 1 ? 1.0 : _dragRun == 2 ? 0.5 : 0.0;
            _dragRun++;
            double level = rate <= 14 ? 1 : Math.Sqrt(14 / rate);
            float pitch = Semis(0.2 * THash.S(THash.Mix(h + 1)));
            float gain = (float)(DragGain * level) * Db(0.8 * THash.S(THash.Mix(h + 2)) + accent);
            _dragThresh = 1 + 0.03 * THash.S(THash.Mix(h + 3));
            Start(set[v], pitch, gain, GrainKind.Drag, at);
            OnFire?.Invoke(GrainKind.Drag, v + (useDark ? 100 : 0), gain, pitch, now < 0 ? SampleClock : now);
        }

        private void FireStrain(int at, long now)
        {
            long idx = _strainIdx++;
            uint h = THash.Of(0xD2u, idx);
            int v = Pick(h, _bank.Strain.Length, ref _lastStrainV);
            float pitch = Semis(0.6 * THash.S(THash.Mix(h + 1)));
            float gain = StrainGain * Db(2.0 * THash.S(THash.Mix(h + 2)));
            double u = THash.U(THash.Mix(h + 3));
            _strainThresh = Math.Max(0.35, Math.Min(2.2, -Math.Log(1 - 0.999 * u)));
            Start(_bank.Strain[v], pitch, gain, GrainKind.Strain, at);
            OnFire?.Invoke(GrainKind.Strain, v, gain, pitch, now);
        }

        private void FireCreak(int at, long now)
        {
            long idx = _creakIdx++;
            uint h = THash.Of(0xD3u, idx);
            bool hard = THash.U(THash.Mix(h + 4)) < CreakHard;
            var set = hard ? _bank.CreakHard : _bank.CreakSoft;
            int v = Pick(h, set.Length, ref _lastCreakV);
            float pitch = Semis(0.6 * THash.S(THash.Mix(h + 1)));
            float gain = CreakGain * Db(2.0 * THash.S(THash.Mix(h + 2)));
            _creakThresh = 0.65 + 0.7 * THash.U(THash.Mix(h + 3));
            Start(set[v], pitch, gain, GrainKind.Creak, at);
            OnFire?.Invoke(GrainKind.Creak, v + (hard ? 100 : 0), gain, pitch, now);
        }
    }
}
