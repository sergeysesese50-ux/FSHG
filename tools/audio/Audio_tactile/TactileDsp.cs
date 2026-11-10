using System;

namespace Fishing.Unity
{
    /// <summary>Deterministic hash helpers (no global state): variant choice, jitter, event timing.</summary>
    internal static class THash
    {
        public static uint Mix(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }

        public static uint Of(uint a, uint b) => Mix(a * 0x9E3779B1u ^ Mix(b + 0x632BE5ABu));
        public static uint Of(uint a, long b) => Of(a, (uint)b ^ Mix((uint)(b >> 32)));

        /// <summary>0..1 from a hash.</summary>
        public static double U(uint h) => (h >> 8) * (1.0 / 16777216.0);

        /// <summary>-1..1 from a hash.</summary>
        public static double S(uint h) => 2.0 * U(h) - 1.0;
    }

    /// <summary>xorshift32 seeded through a hash; a local struct, never static.</summary>
    internal struct TRng
    {
        private uint _s;

        public TRng(uint seed)
        {
            _s = THash.Mix(seed ^ 0xA5A5A5A5u);
            if (_s == 0) _s = 0x9E3779B9u;
        }

        public uint NextU() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public double U() => (NextU() >> 8) * (1.0 / 16777216.0);
        public double S() => 2.0 * U() - 1.0;
        public float N() => (float)S();
        /// <summary>1 ± rel.</summary>
        public double J(double rel) => 1.0 + rel * S();
        public double R(double a, double b) => a + (b - a) * U();
    }

    /// <summary>Topology-preserving state-variable filter (Zavalishin): stable under cutoff sweeps.</summary>
    internal struct Svf
    {
        private double _ic1, _ic2, _g, _k, _a1;
        private readonly int _sr;

        public Svf(int sr, double fc, double q)
        {
            _sr = sr; _ic1 = _ic2 = _g = _k = _a1 = 0;
            Set(fc, q);
        }

        public void Set(double fc, double q)
        {
            fc = Math.Max(5.0, Math.Min(fc, 0.45 * _sr));
            _g = Math.Tan(Math.PI * fc / _sr);
            _k = 1.0 / q;
            _a1 = 1.0 / (1.0 + _g * (_g + _k));
        }

        public void Tick(double x, out double lp, out double bp, out double hp)
        {
            double v3 = x - _ic2;
            double v1 = _a1 * _ic1 + _g * _a1 * v3;
            double v2 = _ic2 + _g * v1;
            _ic1 = 2 * v1 - _ic1;
            _ic2 = 2 * v2 - _ic2;
            lp = v2; bp = v1; hp = x - _k * v1 - v2;
        }

        public double Lp(double x) { Tick(x, out var l, out _, out _); return l; }
        public double Hp(double x) { Tick(x, out _, out _, out var h); return h; }
        /// <summary>Band-pass with unity gain at the centre.</summary>
        public double Bp(double x) { Tick(x, out _, out var b, out _); return _k * b; }
    }

    internal enum FType { Lp, Hp, Bp }

    /// <summary>Building blocks shared by the recipes. All pure functions of their arguments.</summary>
    internal static class TDsp
    {
        public const double TwoPi = 2.0 * Math.PI;

        public static int N(double seconds, int sr) => (int)Math.Round(seconds * sr);
        public static float[] Buf(double seconds, int sr) => new float[Math.Max(1, N(seconds, sr))];

        /// <summary>2-pole filter in place. circular: state warmed by one pass over the same loop first (seamless).</summary>
        public static void Filter(float[] s, int sr, FType type, double fc, double q = 0.7071, bool circular = false)
        {
            var f = new Svf(sr, fc, q);
            if (circular)
                for (int i = 0; i < s.Length; i++) Run(ref f, type, s[i]);
            for (int i = 0; i < s.Length; i++) s[i] = (float)Run(ref f, type, s[i]);
        }

        private static double Run(ref Svf f, FType t, double x) => t == FType.Lp ? f.Lp(x) : t == FType.Hp ? f.Hp(x) : f.Bp(x);

        /// <summary>Struck mode: sin(2πft)·e^(−t/τ) with a raised-cosine onset (no single-sample edge).</summary>
        public static void Mode(float[] s, int sr, double t0, double f, double tau, double amp, double attack)
        {
            if (f >= 0.45 * sr || amp == 0 || tau <= 0) return;
            int i0 = N(t0, sr);
            int len = (int)Math.Ceiling((tau * 9.2 + attack) * sr);
            int na = Math.Max(2, N(attack, sr));
            double w = TwoPi * f / sr, dec = Math.Exp(-1.0 / (tau * sr)), env = amp;
            for (int k = 0; k < len && i0 + k < s.Length; k++)
            {
                double a = k < na ? 0.5 - 0.5 * Math.Cos(Math.PI * k / na) : 1.0;
                if (i0 + k >= 0) s[i0 + k] += (float)(env * a * Math.Sin(w * k));
                env *= dec;
            }
        }

        /// <summary>Mode with an exponential pitch glide f0 → f0·ratio over glideSec (string losing tension etc.).</summary>
        public static void GlideMode(float[] s, int sr, double t0, double f0, double ratio, double glideSec, double tau, double amp, double attack)
        {
            int i0 = N(t0, sr);
            int len = (int)Math.Ceiling((tau * 9.2 + attack) * sr);
            int na = Math.Max(2, N(attack, sr));
            double ph = 0, dec = Math.Exp(-1.0 / (tau * sr)), env = amp;
            for (int k = 0; k < len && i0 + k < s.Length; k++)
            {
                double t = (double)k / sr;
                double f = f0 * Math.Pow(ratio, Math.Min(1.0, t / glideSec));
                if (f >= 0.45 * sr) break;
                ph += TwoPi * f / sr;
                double a = k < na ? 0.5 - 0.5 * Math.Cos(Math.PI * k / na) : 1.0;
                if (i0 + k >= 0) s[i0 + k] += (float)(env * a * Math.Sin(ph));
                env *= dec;
            }
        }

        /// <summary>Hann-windowed noise burst (exciter), at least 0.3 ms.</summary>
        public static void Burst(float[] s, int sr, double t0, double dur, double amp, ref TRng r)
        {
            int i0 = N(t0, sr), n = Math.Max(3, N(Math.Max(dur, 0.0003), sr));
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                double w = Math.Sin(Math.PI * k / (n - 1));
                s[i0 + k] += (float)(amp * w * w * r.S());
            }
        }

        /// <summary>Unipolar raised-cosine bump (stick-slip pulse).</summary>
        public static void Bump(float[] s, int sr, double t0, double dur, double amp)
        {
            int i0 = N(t0, sr), n = Math.Max(3, N(dur, sr));
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                double w = Math.Sin(Math.PI * k / (n - 1));
                if (i0 + k >= 0) s[i0 + k] += (float)(amp * w * w);
            }
        }

        /// <summary>
        /// Minnaert bubble (Sounding Liquids): f0 = 3000 / r[mm] Hz, p = A·sin(2π∫f)·e^(−t/τ), f(t) = f0(1 + ξt/τ),
        /// τ = (6 + 4·r) ms. Negative ξ = falling pitch (a gulp).
        /// </summary>
        public static void Bubble(float[] s, int sr, double t0, double rMm, double amp, double xi = 0.1, double tauScale = 1.0, bool wrap = false)
        {
            double f0 = 3000.0 / rMm;
            double tau = (0.006 + 0.004 * rMm) * tauScale;
            int i0 = N(t0, sr), len = (int)(tau * 8 * sr), na = Math.Max(3, N(0.0006, sr));
            double ph = 0, dec = Math.Exp(-1.0 / (tau * sr)), env = amp;
            for (int k = 0; k < len; k++)
            {
                double t = (double)k / sr;
                double f = f0 * Math.Max(0.3, 1.0 + xi * t / tau);
                if (f >= 0.42 * sr) break;
                ph += TwoPi * f / sr;
                double a = k < na ? 0.5 - 0.5 * Math.Cos(Math.PI * k / na) : 1.0;
                int j = i0 + k;
                if (wrap) j %= s.Length;
                else if (j >= s.Length) break;
                s[j] += (float)(env * a * Math.Sin(ph));
                env *= dec;
            }
        }

        /// <summary>Noise through a band-pass whose centre follows fc(x), x = 0..1 over the span; envelope env(x).</summary>
        public static void SweptNoise(float[] s, int sr, double t0, double dur, Func<double, double> fc, double q,
            Func<double, double> env, double amp, ref TRng r)
        {
            int i0 = N(t0, sr), n = N(dur, sr);
            var f = new Svf(sr, fc(0), q);
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                double x = (double)k / n;
                if ((k & 7) == 0) f.Set(fc(x), q);
                s[i0 + k] += (float)(amp * env(x) * f.Bp(r.S()));
            }
        }

        /// <summary>Plucked string: harmonics k·f with 1/k^tilt, τ_k = τ/k^0.6, optional pitch drop.</summary>
        public static void Pluck(float[] s, int sr, double t0, double f, int harmonics, double tau, double amp,
            double tilt = 1.1, double pluckPos = 0.18, double drop = 1.0, double dropSec = 0.06)
        {
            for (int k = 1; k <= harmonics; k++)
            {
                double a = amp * Math.Abs(Math.Sin(Math.PI * k * pluckPos)) / Math.Pow(k, tilt);
                GlideMode(s, sr, t0, f * k, drop, dropSec, tau / Math.Pow(k, 0.6), a, 0.0005);
            }
        }

        /// <summary>
        /// Small Schroeder/Freeverb room (4 combs 558/594/638/678 @22.05k, fb 0.84, damping 0.2; allpasses 278/220, g 0.5).
        /// The wet part is scaled to wetDb of the dry energy. circular: the loop is processed twice so tails wrap.
        /// Returns a new buffer (dry + tail when not circular).
        /// </summary>
        public static float[] Reverb(float[] dry, int sr, double wetDb, double tailSec = 0.7, double feedback = 0.84,
            double damp = 0.2, bool circular = false)
        {
            double sc = sr / 22050.0;
            int[] cl = { (int)(558 * sc), (int)(594 * sc), (int)(638 * sc), (int)(678 * sc) };
            int[] al = { (int)(278 * sc), (int)(220 * sc) };
            int n = circular ? dry.Length : dry.Length + N(tailSec, sr);
            var cb = new float[4][]; var ci = new int[4]; var cf = new float[4];
            for (int c = 0; c < 4; c++) cb[c] = new float[cl[c]];
            var ab = new float[2][]; var ai = new int[2];
            for (int a = 0; a < 2; a++) ab[a] = new float[al[a]];
            var wet = new float[n];
            int passes = circular ? 2 : 1;
            for (int p = 0; p < passes; p++)
                for (int i = 0; i < n; i++)
                {
                    float x = i < dry.Length ? dry[i] : 0f;
                    float acc = 0f;
                    for (int c = 0; c < 4; c++)
                    {
                        float y = cb[c][ci[c]];
                        cf[c] = (float)(y * (1 - damp) + cf[c] * damp);
                        cb[c][ci[c]] = (float)(x + cf[c] * feedback);
                        ci[c] = (ci[c] + 1) % cl[c];
                        acc += y;
                    }
                    float v = acc * 0.25f;
                    for (int a = 0; a < 2; a++)
                    {
                        float bo = ab[a][ai[a]];
                        float o = -v + bo;
                        ab[a][ai[a]] = v + bo * 0.5f;
                        ai[a] = (ai[a] + 1) % al[a];
                        v = o;
                    }
                    if (p == passes - 1) wet[i] = v;
                }
            double ed = 0, ew = 0;
            for (int i = 0; i < dry.Length; i++) ed += (double)dry[i] * dry[i];
            for (int i = 0; i < n; i++) ew += (double)wet[i] * wet[i];
            double g = ew > 0 ? Math.Sqrt(Math.Pow(10, wetDb / 10) * ed / ew) : 0;
            var o2 = new float[n];
            for (int i = 0; i < n; i++) o2[i] = (i < dry.Length ? dry[i] : 0f) + (float)(g * wet[i]);
            return o2;
        }

        public static void RemoveDc(float[] s)
        {
            double m = 0;
            for (int i = 0; i < s.Length; i++) m += s[i];
            m /= s.Length;
            for (int i = 0; i < s.Length; i++) s[i] -= (float)m;
        }

        public static void Normalize(float[] s, double peak)
        {
            double m = 0;
            for (int i = 0; i < s.Length; i++) m = Math.Max(m, Math.Abs(s[i]));
            if (m <= 0) return;
            float k = (float)(peak / m);
            for (int i = 0; i < s.Length; i++) s[i] *= k;
        }

        public static void FadeIn(float[] s, int sr, double sec)
        {
            int n = Math.Min(s.Length, Math.Max(2, N(sec, sr)));
            for (int i = 0; i < n; i++) s[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / n));
        }

        public static void FadeOut(float[] s, int sr, double sec)
        {
            int n = Math.Min(s.Length, Math.Max(2, N(sec, sr)));
            for (int i = 0; i < n; i++) s[s.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / n));
        }

        /// <summary>Common finish for one-shots: DC out, high-pass, raised-cosine edges, peak.</summary>
        public static float[] Finish(float[] s, int sr, double peak, double hp = 90, double fadeOutSec = 0.01)
        {
            if (hp > 0) Filter(s, sr, FType.Hp, hp);
            RemoveDc(s);
            FadeIn(s, sr, 0.0004);
            FadeOut(s, sr, fadeOutSec);
            Normalize(s, peak);
            return s;
        }

        public static float[] Concat(float[] a, int padSamples)
        {
            var o = new float[a.Length + padSamples];
            Array.Copy(a, o, a.Length);
            return o;
        }
    }
}
