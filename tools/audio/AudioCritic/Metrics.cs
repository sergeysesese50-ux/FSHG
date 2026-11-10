using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

/// <summary>All measurements. Every function is pure (thread-safe) and documented with the exact definition used.</summary>
public static class M
{
    public static double Db(double a) => 20 * Math.Log10(Math.Max(a, 1e-12));
    public static double DbP(double p) => 10 * Math.Log10(Math.Max(p, 1e-24));

    // =====================================================================================================
    // Levels
    // =====================================================================================================

    public static double Peak(double[] x) { double m = 0; foreach (var v in x) { double a = Math.Abs(v); if (a > m) m = a; } return m; }

    public static double Rms(double[] x, int a = 0, int b = -1)
    {
        if (b < 0) b = x.Length;
        double s = 0; for (int i = a; i < b; i++) s += x[i] * x[i];
        return b > a ? Math.Sqrt(s / (b - a)) : 0;
    }

    public static double Mean(double[] x) { double s = 0; foreach (var v in x) s += v; return x.Length > 0 ? s / x.Length : 0; }

    /// <summary>RMS over 100 ms blocks that are within 30 dB of the loudest block and above -60 dBFS.</summary>
    public static double ActiveRms(double[] x, int fs)
    {
        int blk = Math.Max(1, fs / 10), nb = x.Length / blk;
        if (nb < 2) return Rms(x);
        var ms = new double[nb];
        for (int j = 0; j < nb; j++) { double s = 0; for (int i = j * blk; i < (j + 1) * blk; i++) s += x[i] * x[i]; ms[j] = s / blk; }
        double thr = Math.Max(ms.Max() * 1e-3, 1e-6);
        double acc = 0; int n = 0;
        foreach (var v in ms) if (v >= thr) { acc += v; n++; }
        return n > 0 ? Math.Sqrt(acc / n) : Rms(x);
    }

    static double[] _tpH;
    const int TpL = 4, TpHalf = 16;

    /// <summary>True peak: 4x oversampling (Blackman-windowed sinc, 33 input taps) evaluated around samples above 0.6 x sample peak.</summary>
    public static double TruePeak(double[] x)
    {
        if (_tpH == null)
        {
            int n = 2 * TpHalf * TpL + 1;
            var h = new double[n];
            for (int m = 0; m < n; m++)
            {
                double t = (m - TpHalf * TpL) / (double)TpL;
                double s = t == 0 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
                double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * m / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * m / (n - 1));
                h[m] = s * w;
            }
            _tpH = h;
        }
        double pk = Peak(x), tp = pk, thr = 0.6 * pk;
        for (int i = 0; i < x.Length - 1; i++)
        {
            if (Math.Abs(x[i]) < thr && Math.Abs(x[i + 1]) < thr) continue;
            for (int p = 1; p < TpL; p++)
            {
                double acc = 0;
                for (int j = i - TpHalf + 1; j <= i + TpHalf; j++)
                {
                    if (j < 0 || j >= x.Length) continue;
                    int m = (i - j) * TpL + p + TpHalf * TpL;
                    if (m < 0 || m >= _tpH.Length) continue;
                    acc += x[j] * _tpH[m];
                }
                if (Math.Abs(acc) > tp) tp = Math.Abs(acc);
            }
        }
        return tp;
    }

    // =====================================================================================================
    // Loudness, ITU-R BS.1770-4 / EBU R128 (K-weighting coefficients for any rate, libebur128 formulas)
    // =====================================================================================================

    public static Biquad[] KWeight(double fs)
    {
        double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
        double K = Math.Tan(Math.PI * f0 / fs), Vh = Math.Pow(10.0, G / 20.0), Vb = Math.Pow(Vh, 0.4996667741545416);
        double a0 = 1.0 + K / Q + K * K;
        var shelf = new Biquad((Vh + Vb * K / Q + K * K) / a0, 2.0 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0,
                               1.0, 2.0 * (K * K - 1.0) / a0, (1.0 - K / Q + K * K) / a0);
        f0 = 38.13547087602444; Q = 0.5003270373238773;
        K = Math.Tan(Math.PI * f0 / fs);
        double d = 1.0 + K / Q + K * K;
        var hp = new Biquad(1.0, -2.0, 1.0, 1.0, 2.0 * (K * K - 1.0) / d, (1.0 - K / Q + K * K) / d);
        return new[] { shelf, hp };
    }

    public sealed class Loud { public double I = double.NaN, Mmax = double.NaN, Smax = double.NaN, Lra = double.NaN; public double[] Short100; }

    public static Loud Loudness(double[] x, int fs)
    {
        var r = new Loud();
        var k = D.Filt(x, KWeight(fs));
        int sb = Math.Max(1, fs / 10), nsb = k.Length / sb;
        var ms = new double[nsb];
        for (int j = 0; j < nsb; j++) { double s = 0; for (int i = j * sb; i < (j + 1) * sb; i++) s += k[i] * k[i]; ms[j] = s / sb; }
        Func<double, double> L = z => -0.691 + 10 * Math.Log10(Math.Max(z, 1e-20));
        if (nsb < 4)
        {
            double z = 0; foreach (var v in k) z += v * v; z /= Math.Max(1, k.Length);
            r.I = r.Mmax = L(z);
            r.Short100 = new double[0];
            return r;
        }
        var mom = new double[nsb - 3];
        for (int j = 0; j < mom.Length; j++) mom[j] = (ms[j] + ms[j + 1] + ms[j + 2] + ms[j + 3]) / 4;
        r.Mmax = L(mom.Max());
        var abs = mom.Where(z => L(z) > -70).ToArray();
        if (abs.Length > 0)
        {
            double gr = L(abs.Average()) - 10;
            var rel = abs.Where(z => L(z) > gr).ToArray();
            r.I = rel.Length > 0 ? L(rel.Average()) : double.NaN;
        }
        if (nsb >= 30)
        {
            var st = new double[nsb - 29];
            double acc = 0; for (int j = 0; j < 30; j++) acc += ms[j];
            for (int j = 0; j < st.Length; j++) { st[j] = acc / 30; if (j + 30 < nsb) acc += ms[j + 30] - ms[j]; }
            r.Smax = L(st.Max());
            var sa = st.Where(z => L(z) > -70).ToArray();
            if (sa.Length > 2)
            {
                double gr = L(sa.Average()) - 20;
                var sl = sa.Where(z => L(z) > gr).Select(L).OrderBy(v => v).ToArray();
                if (sl.Length > 2) r.Lra = D.Percentile(sl, 95) - D.Percentile(sl, 10);
            }
        }
        r.Short100 = mom.Select(L).ToArray(); // momentary (400 ms) every 100 ms
        return r;
    }

    // =====================================================================================================
    // Spectrum
    // =====================================================================================================

    public static int SegFor(int fs) => fs >= 40000 ? 4096 : 2048;

    /// <summary>Welch power spectrum, Hann window, 50% overlap; shorter inputs are zero-padded to one segment.</summary>
    public static double[] Welch(double[] x, int seg)
    {
        var acc = new double[seg / 2 + 1];
        var w = new double[seg];
        for (int i = 0; i < seg; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / seg);
        var re = new double[seg]; var im = new double[seg];
        int count = 0;
        if (x.Length < seg)
        {
            int segS = x.Length;
            for (int i = 0; i < seg; i++) { re[i] = i < segS ? x[i] : 0; im[i] = 0; }
            D.Fft(re, im);
            for (int k = 0; k <= seg / 2; k++) acc[k] = re[k] * re[k] + im[k] * im[k];
            return acc;
        }
        for (int start = 0; start + seg <= x.Length; start += seg / 2)
        {
            for (int i = 0; i < seg; i++) { re[i] = x[start + i] * w[i]; im[i] = 0; }
            D.Fft(re, im);
            for (int k = 0; k <= seg / 2; k++) acc[k] += re[k] * re[k] + im[k] * im[k];
            count++;
        }
        for (int k = 0; k < acc.Length; k++) acc[k] /= Math.Max(1, count);
        return acc;
    }

    public static double AWeight(double f)
    {
        if (f <= 0) return 0;
        double f2 = f * f;
        double ra = 12194.0 * 12194.0 * f2 * f2 /
                    ((f2 + 20.6 * 20.6) * Math.Sqrt((f2 + 107.7 * 107.7) * (f2 + 737.9 * 737.9)) * (f2 + 12194.0 * 12194.0));
        double g = ra * Math.Pow(10, 2.0 / 20);
        return g * g;
    }

    /// <summary>Rough phone loudspeaker: 2nd-order high-pass at 700 Hz (power factor) — same model as the earlier reports.</summary>
    public static double PhoneWeight(double f) { double r = Math.Pow(f / 700.0, 4); return r / (1 + r); }

    public sealed class Spec
    {
        public double[] Flat = new double[4], A = new double[4], Ph = new double[4];
        public double Cpow, Cmag, CpowA, Sub20;
        /// <summary>A-weighted level change through the phone-speaker model (dB, negative = lost on a phone).</summary>
        public double PhoneLossDb;
    }

    /// <summary>Band shares &lt;300, 300–2000, 2000–5000, &gt;5000 Hz (flat, A-weighted, phone x A). Centroids: power-weighted (Cpow),
    /// magnitude-weighted (Cmag, librosa-style), power-weighted after A-weighting (CpowA).</summary>
    public static Spec Bands(double[] p, double binHz)
    {
        var s = new Spec();
        double tf = 0, ta = 0, tp = 0, c1 = 0, c2 = 0, c3 = 0, c4 = 0, c5 = 0, sub = 0;
        for (int k = 1; k < p.Length; k++)
        {
            double f = k * binHz;
            if (f < 20) { sub += p[k]; tf += p[k]; continue; }
            int b = f < 300 ? 0 : f < 2000 ? 1 : f < 5000 ? 2 : 3;
            double a = AWeight(f), ph = PhoneWeight(f);
            s.Flat[b] += p[k]; tf += p[k];
            s.A[b] += p[k] * a; ta += p[k] * a;
            s.Ph[b] += p[k] * a * ph; tp += p[k] * a * ph;
            c1 += f * p[k]; c2 += p[k];
            double mag = Math.Sqrt(p[k]); c3 += f * mag; c4 += mag;
            c5 += f * p[k] * a;
        }
        for (int b = 0; b < 4; b++)
        {
            s.Flat[b] /= Math.Max(tf, 1e-300);
            s.A[b] /= Math.Max(ta, 1e-300);
            s.Ph[b] /= Math.Max(tp, 1e-300);
        }
        s.Cpow = c1 / Math.Max(c2, 1e-300);
        s.Cmag = c3 / Math.Max(c4, 1e-300);
        s.CpowA = c5 / Math.Max(ta, 1e-300);
        s.Sub20 = sub / Math.Max(tf, 1e-300);
        s.PhoneLossDb = 10 * Math.Log10(Math.Max(tp, 1e-300) / Math.Max(ta, 1e-300));
        return s;
    }

    // =====================================================================================================
    // Frame analysis: centroid over time + cepstral "buzz" index
    // =====================================================================================================

    public sealed class Frames
    {
        public int Active;
        public double CMean = double.NaN, CStd = double.NaN, CP10 = double.NaN, CP90 = double.NaN, CDeltaMed = double.NaN;
        public double BuzzMed = double.NaN, BuzzP90 = double.NaN, BuzzFrac = double.NaN;
    }

    public const double BuzzThreshold = 8.0;

    /// <summary>
    /// Harmonic contrast of one frame's power spectrum (dB): for every f0 in 80–400 Hz (1 Hz steps) with ≥ 6 harmonics inside
    /// 300–5000 Hz, the mean over those harmonics of L(h·f0) − ½[L((h−½)f0) + L((h+½)f0)], L = 10·log10 of the nearest-bin power with
    /// a floor 60 dB under the frame maximum; the frame value is the best f0. A periodic buzz (pulse/saw/regular stick-slip) puts
    /// every harmonic far above the gaps (+15…30 dB); noise, decaying modes and inharmonic ticks average out near 0–4 dB.
    /// </summary>
    static double HarmonicContrast(double[] P, double bin, int nBins, out double bestF0)
    {
        double pmax = 0;
        int k0 = (int)(150 / bin), k1 = Math.Min(nBins - 1, (int)(5200 / bin));
        for (int k = k0; k <= k1; k++) if (P[k] > pmax) pmax = P[k];
        double floor = pmax * 1e-6 + 1e-30;
        Func<double, double> L = f => { int k = (int)Math.Round(f / bin); if (k < 1 || k >= nBins) return 10 * Math.Log10(floor); return 10 * Math.Log10(P[k] + floor); };
        double best = double.MinValue; bestF0 = 0;
        for (double f0 = 80; f0 <= 400; f0 += 1)
        {
            int h0 = (int)Math.Ceiling(300 / f0), h1 = (int)Math.Floor(5000 / f0);
            if (h1 - h0 + 1 < 6) continue;
            double acc = 0;
            for (int h = h0; h <= h1; h++)
                acc += L(h * f0) - 0.5 * (L((h - 0.5) * f0) + L((h + 0.5) * f0));
            acc /= h1 - h0 + 1;
            if (acc > best) { best = acc; bestF0 = f0; }
        }
        return best;
    }

    /// <summary>
    /// Frames of SegFor(fs) samples (≈43–93 ms), hop half a frame, Hann. Active frames: energy within 40 dB of the loudest frame
    /// and above -70 dBFS. Centroid = power-weighted per frame; stats over active frames; CDeltaMed = median |ΔC| between
    /// consecutive active frames as % of the mean centroid (spectral flicker).
    /// Buzz (every 2nd frame) = HarmonicContrast in dB; BuzzFrac = share of active-frame energy with contrast ≥ BuzzThreshold.
    /// </summary>
    public static Frames FrameStats(double[] x, int fs, bool buzz)
    {
        int seg = SegFor(fs), hop = seg / 2;
        var r = new Frames();
        if (x.Length < seg) return r;
        var w = new double[seg];
        for (int i = 0; i < seg; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / seg);
        int nf = (x.Length - seg) / hop + 1;
        var en = new double[nf];
        for (int f = 0; f < nf; f++) { double s = 0; for (int i = 0; i < seg; i++) { double v = x[f * hop + i]; s += v * v; } en[f] = s / seg; }
        double thr = Math.Max(en.Max() * 1e-4, 1e-7);
        var re = new double[seg]; var im = new double[seg];
        double bin = (double)fs / seg;
        var cents = new List<double>(); var wts = new List<double>();
        var bz = new List<double>(); var bzW = new List<double>();
        var P = new double[seg / 2 + 1];
        for (int f = 0; f < nf; f++)
        {
            if (en[f] < thr) continue;
            for (int i = 0; i < seg; i++) { re[i] = x[f * hop + i] * w[i]; im[i] = 0; }
            D.Fft(re, im);
            double num = 0, den = 0, pmax = 0;
            for (int k = 1; k <= seg / 2; k++)
            {
                double p = re[k] * re[k] + im[k] * im[k];
                double fr = k * bin;
                if (fr < 20) continue;
                num += fr * p; den += p;
                if (p > pmax) pmax = p;
            }
            cents.Add(num / Math.Max(den, 1e-300)); wts.Add(en[f]);
            if (buzz && (f & 1) == 0)
            {
                for (int k = 0; k <= seg / 2; k++) P[k] = re[k] * re[k] + im[k] * im[k];
                bz.Add(HarmonicContrast(P, bin, seg / 2 + 1, out _)); bzW.Add(en[f]);
            }
        }
        r.Active = cents.Count;
        if (cents.Count > 0)
        {
            var c = cents.ToArray(); var wv = wts.ToArray();
            double sw = wv.Sum();
            r.CMean = c.Zip(wv, (a, b) => a * b).Sum() / sw;
            double m = c.Average();
            r.CStd = Math.Sqrt(c.Select(v => (v - m) * (v - m)).Average());
            r.CP10 = D.Pct(c, 10); r.CP90 = D.Pct(c, 90);
            if (c.Length > 2)
            {
                var dl = new double[c.Length - 1];
                for (int i = 1; i < c.Length; i++) dl[i - 1] = Math.Abs(c[i] - c[i - 1]);
                r.CDeltaMed = 100 * D.Median(dl) / Math.Max(m, 1);
            }
        }
        if (bz.Count > 0)
        {
            var b = bz.ToArray(); var bw = bzW.ToArray();
            r.BuzzMed = D.WPercentile(b, bw, 50); r.BuzzP90 = D.WPercentile(b, bw, 90);
            double tot = bw.Sum(), hi = 0;
            for (int i = 0; i < b.Length; i++) if (b[i] >= BuzzThreshold) hi += bw[i];
            r.BuzzFrac = hi / Math.Max(tot, 1e-300);
        }
        return r;
    }

    // =====================================================================================================
    // Amplitude modulation index
    // =====================================================================================================

    /// <summary>
    /// Amplitude-modulation index of a carrier band within a modulation band.
    /// Carrier: 4th-order Butterworth band (cLo..cHi) or, with cLo = 0, the whole signal high-passed at 90 Hz.
    /// Envelope: sqrt of the squared carrier low-passed (4th order) at 400 Hz (mod band ≥ 20 Hz) or 40 Hz, decimated to ≈2 kHz / 200 Hz.
    /// Index per segment: m = √2·RMS(envelope band-passed mLo..mHi, 4th order each side) / mean(envelope) — a sinusoidal AM of depth m reads m.
    /// Segments: 0.5 s (mod ≥ 20 Hz) or 2 s; active if carrier energy within 30 dB of the loudest segment. Loops are processed
    /// circularly as one segment. Returns the carrier-energy-weighted median and 90th percentile.
    /// </summary>
    public static (double med, double p90, int nseg) ModIndex(double[] x, int fs, double cLo, double cHi, double mLo, double mHi, bool circular)
    {
        Biquad[] car = cLo > 0
            ? (cHi < 0.45 * fs ? D.Join(D.Hp4(fs, cLo), D.Lp4(fs, cHi)) : D.Hp4(fs, cLo))
            : new[] { Biquad.Hp(fs, 90, 0.7071) };
        var y = circular ? D.FiltCircular(x, car) : D.Filt(x, car);
        for (int i = 0; i < y.Length; i++) y[i] *= y[i];
        bool fast = mHi >= 20;
        double envCut = fast ? 400 : 40;
        y = circular ? D.FiltCircular(y, D.Lp4(fs, envCut)) : D.Filt(y, D.Lp4(fs, envCut));
        int dec = Math.Max(1, (int)Math.Round(fs / (fast ? 2000.0 : 200.0)));
        double fe = (double)fs / dec;
        int ne = y.Length / dec;
        if (ne < 8) return (double.NaN, double.NaN, 0);
        var e = new double[ne];
        for (int i = 0; i < ne; i++) { double s = 0; for (int j = 0; j < dec; j++) s += y[i * dec + j]; e[i] = Math.Sqrt(Math.Max(0, s / dec)); }
        var bp = D.Join(D.Hp4(fe, mLo), D.Lp4(fe, mHi));
        var eb = circular ? D.FiltCircular(e, bp) : D.Filt(e, bp);
        if (circular)
        {
            double mean = e.Average(), rms = Math.Sqrt(eb.Select(v => v * v).Average());
            double mm = mean > 1e-12 ? Math.Sqrt(2) * rms / mean : double.NaN;
            return (mm, mm, 1);
        }
        int segN = (int)Math.Round((fast ? 0.5 : 2.0) * fe);
        int settle = (int)Math.Round((mLo < 5 ? 1.0 : 0.15) * fe);
        var ms = new List<double>(); var ws = new List<double>();
        for (int s0 = settle; s0 + segN <= ne; s0 += segN)
        {
            double mean = 0, rms = 0, en = 0;
            for (int i = s0; i < s0 + segN; i++) { mean += e[i]; rms += eb[i] * eb[i]; en += e[i] * e[i]; }
            mean /= segN; rms = Math.Sqrt(rms / segN); en /= segN;
            if (mean <= 1e-9) continue;
            ms.Add(Math.Sqrt(2) * rms / mean); ws.Add(en);
        }
        if (ms.Count == 0) return (double.NaN, double.NaN, 0);
        double emax = ws.Max();
        var keep = Enumerable.Range(0, ms.Count).Where(i => ws[i] >= emax * 1e-3 && ws[i] > 1e-10).ToArray();
        if (keep.Length == 0) return (double.NaN, double.NaN, 0);
        var mv = keep.Select(i => ms[i]).ToArray(); var wv = keep.Select(i => ws[i]).ToArray();
        return (D.WPercentile(mv, wv, 50), D.WPercentile(mv, wv, 90), keep.Length);
    }

    public sealed class Lines { public double D28, F28, D820, F820, D20, F20; }

    /// <summary>
    /// Periodic AM of a loop (rule 14): the whole file is treated as one period (exact for loops and integer tilings). Envelope as in
    /// ModIndex (HP 90 Hz, squared, LP 400 Hz, √, ≈2 kHz), rectangular window, 2× zero-padded FFT; depth(f) = 2|E(f)|/E(0), so a sinusoidal
    /// AM of depth m reads m (≤ 1 dB scalloping). Random envelope fluctuation of noise spreads over all bins and reads ≈ 0.01–0.03 per line.
    /// Returns the strongest line in 2–8, 8–20 and 20–150 Hz.
    /// </summary>
    public static Lines LineDepth(double[] x, int fs)
    {
        var y = D.FiltCircular(x, Biquad.Hp(fs, 90, 0.7071));
        for (int i = 0; i < y.Length; i++) y[i] *= y[i];
        y = D.FiltCircular(y, D.Lp4(fs, 400));
        int dec = Math.Max(1, (int)Math.Round(fs / 2000.0));
        double fe = (double)fs / dec;
        int ne = y.Length / dec;
        var e = new double[ne];
        for (int i = 0; i < ne; i++) { double s = 0; for (int j = 0; j < dec; j++) s += y[i * dec + j]; e[i] = Math.Sqrt(Math.Max(0, s / dec)); }
        int N = D.NextPow2(2 * ne);
        var re = new double[N]; var im = new double[N];
        double e0 = e.Sum(), em = e0 / Math.Max(1, ne);
        for (int i = 0; i < ne; i++) re[i] = e[i] - em;   // mean removed: no DC leakage into the zero-padded bins
        D.Fft(re, im);
        var r = new Lines();
        for (int k = 1; k < N / 2; k++)
        {
            double f = k * fe / N, d = 2 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / Math.Max(e0, 1e-30);
            if (f >= 2 && f < 8 && d > r.D28) { r.D28 = d; r.F28 = f; }
            else if (f >= 8 && f < 20 && d > r.D820) { r.D820 = d; r.F820 = f; }
            else if (f >= 20 && f <= 150 && d > r.D20) { r.D20 = d; r.F20 = f; }
        }
        return r;
    }

    // =====================================================================================================
    // Onsets, tick timing, grain similarity
    // =====================================================================================================

    public sealed class Ticks
    {
        public List<double> T = new List<double>();
        public double Rate = double.NaN, IoiMed = double.NaN, JitMed = double.NaN, JitP90 = double.NaN, Kept = double.NaN;
        public double R60 = double.NaN, R20 = double.NaN;
        public double SimMed = double.NaN, Ident = double.NaN;
        public double[] SimLag = new double[7];
        public int RotationK;
        // variant-set analysis (pairwise)
        public double PairMean = double.NaN, PairMax = double.NaN; public int Clusters;
    }

    /// <summary>
    /// Onset detector for grains/ticks (peak picking with prominence). Signal high-passed at 300 Hz (4th order); hop 0.25 ms.
    /// Events are local maxima of the centred 6 ms energy (smooths the Rayleigh fluctuation of noise beds, keeps ticks with τ ≥ 3 ms).
    /// A maximum becomes a new event when (a) it is ≥ 8 dB above the local floor (20th percentile of 6 ms energy over ±150 ms), or ≥ 5 dB
    /// above it while within 6 dB of the local maximum (dense trains); (b) it rises ≥ 6 dB above the lowest point since the previous event
    /// and the previous event had dropped ≥ 4 dB below its own peak; (c) within 25 ms of the previous event it reaches that event's peak
    /// − 6 dB; (d) it is ≥ 4 ms after the previous onset. Otherwise it belongs to the previous event. Onset time = interpolated crossing
    /// of (fast-envelope peak − 6 dB) walking back from the peak of the centred 2.5 ms energy — the same point on every grain of a family.
    /// </summary>
    public static Ticks DetectTicks(double[] x, int fs, bool pairwise, double t0 = 0, double t1 = double.MaxValue)
    {
        var r = new Ticks();
        var y = D.Filt(x, D.Hp4(fs, 300));
        int hop = Math.Max(1, (int)Math.Round(fs * 0.00025));
        int n = y.Length;
        if (n < fs * 0.03) return r;
        var cs = new double[n + 1];
        for (int i = 0; i < n; i++) cs[i + 1] = cs[i] + y[i] * y[i];
        int nf = n / hop;
        int h25 = (int)Math.Round(fs * 0.00125), h6 = (int)Math.Round(fs * 0.003);
        Func<int, int, double> En = (c, h) => { int a = Math.Max(0, c - h), b = Math.Min(n, c + h); return DbP((cs[b] - cs[a]) / Math.Max(1, b - a)); };
        var e = new double[nf]; var e6 = new double[nf];
        for (int f = 0; f < nf; f++) { e[f] = En(f * hop, h25); e6[f] = En(f * hop, h6); }
        double fps = (double)fs / hop;
        int fl = (int)Math.Round(0.150 * fps), stepF = Math.Max(1, (int)Math.Round(0.025 * fps));
        int nfl = nf / stepF + 2;
        var floorPts = new double[nfl]; var maxPts = new double[nfl];
        var tmp = new List<double>(fl / 2 + 4);
        for (int j = 0; j < nfl; j++)
        {
            int c = Math.Min(nf - 1, j * stepF);
            tmp.Clear();
            for (int k = Math.Max(0, c - fl); k <= Math.Min(nf - 1, c + fl); k += 4) tmp.Add(e6[k]);
            var arr = tmp.ToArray(); Array.Sort(arr);
            floorPts[j] = D.Percentile(arr, 20);
            maxPts[j] = arr[arr.Length - 1];
        }
        Func<double[], int, double> At = (pts, f) => { double p = (double)f / stepF; int a = (int)p; if (a >= nfl - 1) return pts[nfl - 1]; return pts[a] + (p - a) * (pts[a + 1] - pts[a]); };
        var es = (double[])e6.Clone(); Array.Sort(es);
        double gref = D.Percentile(es, 99.5);
        int f0 = Math.Max(1, (int)(t0 * fps)), f1 = Math.Min(nf - 2, t1 >= double.MaxValue / 2 ? nf - 2 : (int)(t1 * fps));
        int lastIdx = -1, lastOnsetF = -1; double lastVal = 0, valley = double.MaxValue, lastT = -1;
        int w4 = (int)Math.Round(0.004 * fps), w15 = (int)Math.Round(0.015 * fps);
        for (int f = f0; f <= f1; f++)
        {
            if (e6[f] < valley) valley = e6[f];
            if (!(e6[f] >= e6[f - 1] && e6[f] > e6[f + 1])) continue;
            double M = e6[f], floor = At(floorPts, f), lmax = At(maxPts, f);
            bool loud = (M - floor >= 8) || (M - floor >= 5 && M >= lmax - 6);
            if (!loud || M < gref - 45) continue;
            if (lastIdx >= 0)
            {
                double dt = (f - lastIdx) / fps;
                bool newEvent = M - valley >= 6 && lastVal - valley >= 4 && !(dt < 0.025 && M < lastVal - 6);
                if (!newEvent)
                {
                    if (M > lastVal) { lastVal = M; lastIdx = f; valley = M; }
                    continue;
                }
            }
            // onset: fast-envelope peak near the event, then walk back to (peak − 6 dB)
            int q = f; double ep = double.MinValue;
            for (int k = Math.Max(0, f - w4); k <= Math.Min(nf - 1, f + w4); k++) if (e[k] > ep) { ep = e[k]; q = k; }
            int lo = Math.Max(Math.Max(1, q - w15), lastOnsetF + 1);
            int m = q;
            while (m > lo && e[m - 1] >= ep - 6) m--;
            double frac = 0;
            if (m > 0 && e[m] != e[m - 1]) frac = Math.Max(0, Math.Min(1, (ep - 6 - e[m - 1]) / (e[m] - e[m - 1])));
            double t = (m - 1 + frac) / fps;
            if (lastT >= 0 && t - lastT < 0.004) { if (M > lastVal) { lastVal = M; lastIdx = f; } valley = M; continue; }
            r.T.Add(t); lastT = t; lastOnsetF = m;
            lastIdx = f; lastVal = M; valley = M;
        }
        var T = r.T;
        if (T.Count >= 3)
        {
            var ioi = new double[T.Count - 1];
            for (int i = 1; i < T.Count; i++) ioi[i - 1] = T[i] - T[i - 1];
            r.IoiMed = D.Median(ioi);
            var jit = new List<double>(); int pairs = 0;
            for (int i = 1; i < ioi.Length; i++)
            {
                if (ioi[i] > 0.6 || ioi[i - 1] > 0.6) continue;
                pairs++;
                double ratio = Math.Max(ioi[i], ioi[i - 1]) / Math.Min(ioi[i], ioi[i - 1]);
                if (ratio > 1.3) continue;
                jit.Add(Math.Abs(ioi[i] - ioi[i - 1]) / ((ioi[i] + ioi[i - 1]) / 2));
            }
            if (jit.Count > 0) { var ja = jit.ToArray(); r.JitMed = 100 * D.Median(ja); r.JitP90 = 100 * D.Pct(ja, 90); }
            r.Kept = pairs > 0 ? (double)jit.Count / pairs : double.NaN;
            // rate over "busy" stretches: onsets per second counted only across IOIs ≤ 0.6 s
            double busy = ioi.Where(v => v <= 0.6).Sum(); int nb = ioi.Count(v => v <= 0.6);
            r.Rate = busy > 0 ? nb / busy : double.NaN;
            // frame-grid locking: share of IOIs (≤ 0.6 s) within ±0.3 ms of an integer number of 60 fps frames (16.67 ms) or 50 ms model
            // ticks. Free timing gives ≈ 3.6% / 1.2%; grains fired at frame time give ≈ 100%.
            Func<double, double> Q = P =>
            {
                var v = ioi.Where(d => d <= 0.6).ToArray(); if (v.Length == 0) return double.NaN;
                return v.Count(d => { double k = Math.Round(d / P); return k >= 1 && Math.Abs(d - k * P) <= 0.0003; }) / (double)v.Length;
            };
            r.R60 = Q(1.0 / 60); r.R20 = Q(1.0 / 20);
        }
        // grain similarity
        if (T.Count >= 3)
        {
            int pre = (int)Math.Round(0.001 * fs), maxLen = (int)Math.Round((pairwise ? 0.060 : 0.024) * fs), minLen = (int)Math.Round(0.006 * fs);
            var segs = new List<double[]>();
            for (int i = 0; i < T.Count; i++)
            {
                int s = (int)Math.Round(T[i] * fs) - pre;
                int len = maxLen;
                if (i + 1 < T.Count) len = Math.Min(len, (int)Math.Round((T[i + 1] - T[i]) * fs) + pre - (int)Math.Round(0.0005 * fs));
                len = Math.Max(len, minLen);
                if (s < 0) s = 0;
                if (s + len > n) len = n - s;
                var g = new double[Math.Max(0, len)];
                Array.Copy(y, s, g, 0, g.Length);
                segs.Add(g);
            }
            int sh = (int)Math.Round(0.0005 * fs);
            Func<int, int, double> Sim = (a, b) => GrainSim(segs[a], segs[b], sh);
            var lagMed = new double[7];
            for (int k = 1; k <= 6; k++)
            {
                var v = new List<double>();
                for (int i = 0; i + k < segs.Count && v.Count < 400; i++) v.Add(Sim(i, i + k));
                lagMed[k] = v.Count > 0 ? D.Median(v.ToArray()) : double.NaN;
                if (k == 1 && v.Count > 0) { r.SimMed = lagMed[1]; r.Ident = v.Count(z => z >= 0.97) / (double)v.Count; }
            }
            r.SimLag = lagMed;
            for (int k = 2; k <= 6; k++)
                if (lagMed[k] >= 0.95 && lagMed[k] - lagMed[1] >= 0.15) { r.RotationK = k; break; }
            if (pairwise && segs.Count <= 80)
            {
                int ng = segs.Count; double sum = 0, mx = 0; int cnt = 0;
                var parent = Enumerable.Range(0, ng).ToArray();
                Func<int, int> find = null; find = a => parent[a] == a ? a : (parent[a] = find(parent[a]));
                for (int a = 0; a < ng; a++)
                    for (int b = a + 1; b < ng; b++)
                    {
                        double s = Sim(a, b); sum += s; cnt++; if (s > mx) mx = s;
                        if (s >= 0.97) parent[find(a)] = find(b);
                    }
                r.PairMean = cnt > 0 ? sum / cnt : double.NaN; r.PairMax = mx;
                r.Clusters = Enumerable.Range(0, ng).Select(find).Distinct().Count();
            }
        }
        return r;
    }

    /// <summary>Max normalised cross-correlation of two grains over ±sh samples on their common length.</summary>
    public static double GrainSim(double[] a, double[] b, int sh)
    {
        int L = Math.Min(a.Length, b.Length) - 2 * sh;
        if (L < 16) return double.NaN;
        double best = -1;
        double ea = 0; for (int i = sh; i < sh + L; i++) ea += a[i] * a[i];
        for (int d = -sh; d <= sh; d++)
        {
            double acc = 0, eb = 0;
            for (int i = sh; i < sh + L; i++) { double bv = b[i + d]; acc += a[i] * bv; eb += bv * bv; }
            double v = acc / Math.Sqrt(Math.Max(ea * eb, 1e-30));
            if (v > best) best = v;
        }
        return best;
    }

    // =====================================================================================================
    // Repetition
    // =====================================================================================================

    /// <summary>
    /// Waveform self-similarity: signal decimated to ≤ 12 kHz, normalised autocorrelation r(ℓ) = Σx[n]x[n+ℓ]/√(E₁E₂) over the overlap
    /// (circular for loops). Returns the maximum over ℓ ∈ [minLag, N/2] and its lag. 1.0 = exact repeat (a loop inside the file).
    /// </summary>
    public static (double r, double lag) WaveRepeat(double[] x, int fs, double minLag, bool circular)
    {
        int dec = fs >= 40000 ? 4 : fs >= 20000 ? 2 : 1;
        var z = D.Decimate(x, fs, dec, out int fz);
        int n = z.Length;
        if (n < fz * minLag * 3) return (double.NaN, double.NaN);
        int N = D.NextPow2(2 * n);
        double[] re, im;
        if (circular)
        {
            // circular autocorrelation: correlate the loop with two copies of itself (zero-padded FFT, no wrap-around error)
            re = new double[N]; im = new double[N];
            for (int i = 0; i < n; i++) { re[i] = z[i]; re[i + n] = z[i]; }
            D.Fft(re, im);
            var zr = new double[N]; var zi = new double[N];
            for (int i = 0; i < n; i++) zr[i] = z[i];
            D.Fft(zr, zi);
            for (int k = 0; k < N; k++) { double a = re[k] * zr[k] + im[k] * zi[k], b = im[k] * zr[k] - re[k] * zi[k]; re[k] = a; im[k] = b; }
            D.Fft(re, im, true);
            // re[l] = Σ_i z[i] * zz[i+l] where zz = z repeated → circular autocorrelation for l < n
            double e0 = z.Sum(v => v * v);
            int minL = (int)Math.Round(minLag * fz);
            double best = -1; int bl = 0;
            for (int l = minL; l <= n / 2; l++) { double v = re[l] / Math.Max(e0, 1e-30); if (v > best) { best = v; bl = l; } }
            return (best, (double)bl / fz);
        }
        re = new double[N]; im = new double[N];
        for (int i = 0; i < n; i++) re[i] = z[i];
        D.Fft(re, im);
        for (int k = 0; k < N; k++) { re[k] = re[k] * re[k] + im[k] * im[k]; im[k] = 0; }
        D.Fft(re, im, true);
        var cs = new double[n + 1];
        for (int i = 0; i < n; i++) cs[i + 1] = cs[i] + z[i] * z[i];
        int mL = (int)Math.Round(minLag * fz);
        double bst = -1; int bL = 0;
        for (int l = mL; l <= n / 2; l++)
        {
            double e1 = cs[n - l], e2 = cs[n] - cs[l];
            double v = re[l] / Math.Sqrt(Math.Max(e1 * e2, 1e-30));
            if (v > bst) { bst = v; bL = l; }
        }
        return (bst, (double)bL / fz);
    }

    /// <summary>
    /// Feature self-similarity: 50 ms frames, log energies in 7 octave bands (125 Hz–8 kHz, top band clipped at Nyquist), each band
    /// mean-removed; Pearson correlation between the feature matrix and itself shifted by ℓ (circular for loops), ℓ ∈ [minLag, N/2].
    /// Catches recurring events (birds, splashes) even when the waveform is not sample-identical.
    /// </summary>
    public static (double r, double lag) FeatureRepeat(double[] x, int fs, double minLag, bool circular)
    {
        int frame = (int)Math.Round(0.05 * fs), seg = D.NextPow2(frame);
        int nf = x.Length / frame;
        if (nf < 40) return (double.NaN, double.NaN);
        double[] edges = { 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        int nb = edges.Length - 1;
        var F = new double[nf, nb];
        var re = new double[seg]; var im = new double[seg];
        double bin = (double)fs / seg;
        for (int f = 0; f < nf; f++)
        {
            Array.Clear(re, 0, seg); Array.Clear(im, 0, seg);
            for (int i = 0; i < frame; i++) re[i] = x[f * frame + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / frame));
            D.Fft(re, im);
            var acc = new double[nb];
            for (int k = 1; k <= seg / 2; k++)
            {
                double fr = k * bin;
                for (int b = 0; b < nb; b++) if (fr >= edges[b] && fr < edges[b + 1]) { acc[b] += re[k] * re[k] + im[k] * im[k]; break; }
            }
            for (int b = 0; b < nb; b++) F[f, b] = 10 * Math.Log10(acc[b] + 1e-14);
        }
        int nbu = 0;
        for (int b = 0; b < nb; b++) if (edges[b] < fs / 2.0 * 0.95) nbu++;
        for (int b = 0; b < nbu; b++)
        {
            double m = 0; for (int f = 0; f < nf; f++) m += F[f, b]; m /= nf;
            for (int f = 0; f < nf; f++) F[f, b] -= m;
        }
        int minL = (int)Math.Round(minLag / 0.05);
        double best = -2; int bl = 0;
        for (int l = minL; l <= nf / 2; l++)
        {
            double sab = 0, saa = 0, sbb = 0;
            int cnt = circular ? nf : nf - l;
            for (int f = 0; f < cnt; f++)
            {
                int g = (f + l) % nf;
                for (int b = 0; b < nbu; b++) { double a = F[f, b], c = F[g, b]; sab += a * c; saa += a * a; sbb += c * c; }
            }
            double v = sab / Math.Sqrt(Math.Max(saa * sbb, 1e-30));
            if (v > best) { best = v; bl = l; }
        }
        return (best, bl * 0.05);
    }

    // =====================================================================================================
    // Loop seams
    // =====================================================================================================

    public sealed class Seam
    {
        public double Pos, Step, StepRank, CurvRank, ClickRank, LevelJumpDb, LevelRank, StepX, CurvX, ClickX;
        /// <summary>Discontinuity: the seam's step and curvature both exceed 1.5x the loop's own 99.9th percentile, or its HF-click energy 2x,
        /// or the 20 ms level jumps > 6 dB at rank > 99.5. Regular transients (a pulse edge or click that lands on the wrap) stay inside
        /// the loop's own distribution and are not flagged.</summary>
        public bool Bad => (StepX > 1.5 && CurvX > 1.5) || ClickX > 2 || (LevelRank > 99.5 && Math.Abs(LevelJumpDb) > 6);
        public bool Soft => !Bad && LevelRank > 99.5 && Math.Abs(LevelJumpDb) > 3;
        public override string ToString() =>
            $"@{Pos:F3}s step {Step:F4} (rank {StepRank:F1}, {StepX:F2}x p99.9), curvature rank {CurvRank:F1} ({CurvX:F2}x), HF-click rank {ClickRank:F1} ({ClickX:F2}x), level jump {(LevelJumpDb >= 0 ? "+" : "")}{LevelJumpDb:F1} dB (rank {LevelRank:F1})" + (Bad ? "  <-- SEAM" : Soft ? "  <-- soft level step" : "");
    }

    /// <summary>
    /// Seam between x[pos-1] and x[pos] (pos = 0 means the wrap x[N-1] → x[0]). Ranks are percentiles of the same statistic over every
    /// other position of the file: |Δx|, |Δ²x|, energy of the 2nd difference in a 1 ms window centred on the seam, and the level change
    /// between the 20 ms before and after. ≥ 99.5 on step and curvature together, or ≥ 99.8 on the HF click, means an audible discontinuity.
    /// </summary>
    public static Seam SeamAt(double[] x, int fs, int pos)
    {
        int n = x.Length;
        Func<int, double> X = i => x[((i % n) + n) % n];
        var s = new Seam { Pos = (double)pos / fs };
        s.Step = Math.Abs(X(pos) - X(pos - 1));
        double curv = Math.Max(Math.Abs(X(pos) - 2 * X(pos - 1) + X(pos - 2)), Math.Abs(X(pos + 1) - 2 * X(pos) + X(pos - 1)));
        var steps = new double[n]; var curvs = new double[n];
        for (int i = 0; i < n; i++) { steps[i] = Math.Abs(X(i) - X(i - 1)); curvs[i] = Math.Abs(X(i + 1) - 2 * X(i) + X(i - 1)); }
        Array.Sort(steps); Array.Sort(curvs);
        s.StepRank = D.Rank(steps, s.Step); s.CurvRank = D.Rank(curvs, curv);
        s.StepX = s.Step / Math.Max(D.Percentile(steps, 99.9), 1e-12); s.CurvX = curv / Math.Max(D.Percentile(curvs, 99.9), 1e-12);
        int hw = Math.Max(2, (int)Math.Round(0.0005 * fs));
        var d2 = new double[n]; for (int i = 0; i < n; i++) { double v = X(i + 1) - 2 * X(i) + X(i - 1); d2[i] = v * v; }
        var c2 = new double[n + 1]; for (int i = 0; i < n; i++) c2[i + 1] = c2[i] + d2[i];
        Func<int, double> clickE = c => { double a = 0; for (int i = c - hw; i <= c + hw; i++) a += d2[((i % n) + n) % n]; return a; };
        int stp = Math.Max(1, hw);
        var cl = new List<double>();
        for (int c = 0; c < n; c += stp) cl.Add(clickE(c));
        var cla = cl.ToArray(); Array.Sort(cla);
        s.ClickRank = D.Rank(cla, Math.Max(clickE(pos), clickE(pos - 1)));
        s.ClickX = Math.Max(clickE(pos), clickE(pos - 1)) / Math.Max(D.Percentile(cla, 99.9), 1e-30);
        int w = (int)Math.Round(0.02 * fs);
        Func<int, double> lvlJump = c =>
        {
            double a = 0, b = 0;
            for (int i = c - w; i < c; i++) { double v = X(i); a += v * v; }
            for (int i = c; i < c + w; i++) { double v = X(i); b += v * v; }
            return 10 * Math.Log10((b + 1e-20) / (a + 1e-20));
        };
        s.LevelJumpDb = lvlJump(pos);
        var lj = new List<double>();
        for (int c = 0; c < n; c += Math.Max(1, (int)Math.Round(0.005 * fs))) lj.Add(Math.Abs(lvlJump(c)));
        var lja = lj.ToArray(); Array.Sort(lja);
        s.LevelRank = D.Rank(lja, Math.Abs(s.LevelJumpDb));
        return s;
    }
}
