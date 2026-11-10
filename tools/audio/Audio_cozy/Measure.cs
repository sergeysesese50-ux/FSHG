using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Loudness (BS.1770-style K-weighting, gated), envelope modulation, spectral shares for any sample rate.</summary>
public static class Measure
{
    // ---------------- K-weighting (pyloudnorm formulas: shelf +4 dB @1500 Hz Q 0.707, HP 38 Hz Q 0.5) ----------------

    struct Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;
        public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            this.b0 = b0 / a0; this.b1 = b1 / a0; this.b2 = b2 / a0; this.a1 = a1 / a0; this.a2 = a2 / a0; z1 = z2 = 0;
        }
        public double Tick(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    public static double[] KWeight(float[] x, int fs)
    {
        double G = 4.0, Q = 1 / Math.Sqrt(2), fc = 1500;
        double A = Math.Pow(10, G / 40), w0 = 2 * Math.PI * fc / fs, alpha = Math.Sin(w0) / (2 * Q), c = Math.Cos(w0);
        var shelf = new Biquad(
            A * ((A + 1) + (A - 1) * c + 2 * Math.Sqrt(A) * alpha),
            -2 * A * ((A - 1) + (A + 1) * c),
            A * ((A + 1) + (A - 1) * c - 2 * Math.Sqrt(A) * alpha),
            (A + 1) - (A - 1) * c + 2 * Math.Sqrt(A) * alpha,
            2 * ((A - 1) - (A + 1) * c),
            (A + 1) - (A - 1) * c - 2 * Math.Sqrt(A) * alpha);
        double fh = 38, Qh = 0.5, wh = 2 * Math.PI * fh / fs, al = Math.Sin(wh) / (2 * Qh), ch = Math.Cos(wh);
        var hp = new Biquad((1 + ch) / 2, -(1 + ch), (1 + ch) / 2, 1 + al, -2 * ch, 1 - al);
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++) y[i] = hp.Tick(shelf.Tick(x[i]));
        return y;
    }

    /// <summary>Integrated loudness (gated), max short-term (3 s), loudness range (10th..95th pct of short-term, rel-gated).</summary>
    public static (double integrated, double maxShort, double lra) Loudness(float[] x, int fs)
    {
        var k = KWeight(x, fs);
        int block = (int)(0.4 * fs), hop = (int)(0.1 * fs);
        var z = new List<double>();
        var pre = new double[k.Length + 1];
        for (int i = 0; i < k.Length; i++) pre[i + 1] = pre[i] + k[i] * k[i];
        for (int s = 0; s + block <= k.Length; s += hop) z.Add((pre[s + block] - pre[s]) / block);
        if (z.Count == 0) z.Add(pre[k.Length] / Math.Max(1, k.Length));
        double L(double ms) => -0.691 + 10 * Math.Log10(Math.Max(ms, 1e-20));
        var abs = z.Where(v => L(v) > -70).ToList();
        double integrated = double.NegativeInfinity;
        if (abs.Count > 0)
        {
            double rel = L(abs.Average()) - 10;
            var g = abs.Where(v => L(v) > rel).ToList();
            integrated = L(g.Count > 0 ? g.Average() : abs.Average());
        }
        int sb = 3 * fs;
        var st = new List<double>();
        for (int s = 0; s + sb <= k.Length; s += hop) st.Add(L((pre[s + sb] - pre[s]) / sb));
        double maxShort = st.Count > 0 ? st.Max() : integrated;
        double lra = 0;
        if (st.Count > 2)
        {
            var g = st.Where(v => v > -70).ToList();
            if (g.Count > 0)
            {
                double relS = 10 * Math.Log10(g.Select(v => Math.Pow(10, v / 10)).Average()) - 20;
                var gg = g.Where(v => v > relS).OrderBy(v => v).ToList();
                if (gg.Count > 2) lra = gg[(int)(0.95 * (gg.Count - 1))] - gg[(int)(0.10 * (gg.Count - 1))];
            }
        }
        return (integrated, maxShort, lra);
    }

    // ---------------- Envelope modulation ----------------

    /// <summary>
    /// Envelope = |x| through a 2-pole 300 Hz low-pass (circular for loops), decimated to ~2.2 kHz.
    /// Returns exact-DFT modulation depths (2|E_k|/E_0) for loops (periodic) or Welch-averaged for long non-periodic signals.
    /// </summary>
    public static List<(double hz, double depth)> Modulation(float[] x, int fs, bool circular)
    {
        int n = x.Length;
        var e = new double[n];
        for (int i = 0; i < n; i++) e[i] = Math.Abs(x[i]);
        double a = 1 - Math.Exp(-2 * Math.PI * 300.0 / fs);
        for (int pass = 0; pass < 2; pass++)
        {
            double y = 0;
            int reps = circular ? 2 : 1;
            for (int r = 0; r < reps; r++)
                for (int i = 0; i < n; i++)
                {
                    y += a * (e[i] - y);
                    if (r == reps - 1) e[i] = y;
                }
        }
        int dec = Math.Max(1, fs / 2205);
        int m = n / dec;
        var d = new double[m];
        for (int i = 0; i < m; i++) d[i] = e[i * dec];
        double efs = (double)fs / dec;
        var list = new List<(double, double)>();
        if (circular)
        {
            var (re, im) = Dsp.Dft(d);
            double dc = re[0] / m;
            for (int kk = 1; kk <= m / 2; kk++)
            {
                double f = kk * efs / m;
                if (f > 160) break;
                list.Add((f, 2 * Math.Sqrt(re[kk] * re[kk] + im[kk] * im[kk]) / m / Math.Max(dc, 1e-12)));
            }
        }
        else
        {
            int seg = 1;
            while (seg * 2 <= Math.Min(m, 4096)) seg *= 2;
            var w = new double[seg];
            double wsum = 0;
            for (int i = 0; i < seg; i++) { w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / seg); wsum += w[i]; }
            var acc = new double[seg / 2 + 1];
            int cnt = 0;
            double mean = d.Average();
            for (int s = 0; s + seg <= m; s += seg / 2)
            {
                var re = new double[seg]; var im = new double[seg];
                for (int i = 0; i < seg; i++) re[i] = (d[s + i] - mean) * w[i];
                Dsp.Fft(re, im, false);
                for (int kk = 0; kk <= seg / 2; kk++) acc[kk] += re[kk] * re[kk] + im[kk] * im[kk];
                cnt++;
            }
            for (int kk = 1; kk <= seg / 2; kk++)
            {
                double f = kk * efs / seg;
                if (f > 160) break;
                double amp = 2 * Math.Sqrt(acc[kk] / Math.Max(1, cnt)) / wsum;
                list.Add((f, amp / Math.Max(mean, 1e-12)));
            }
        }
        return list;
    }

    public static double MaxDepth(List<(double hz, double depth)> mod, double lo, double hi)
        => mod.Where(t => t.hz >= lo && t.hz < hi).Select(t => t.depth).DefaultIfEmpty(0).Max();

    public static (double hz, double depth) Peak(List<(double hz, double depth)> mod, double lo, double hi)
        => mod.Where(t => t.hz >= lo && t.hz < hi).OrderByDescending(t => t.depth).DefaultIfEmpty((0, 0)).First();

    // ---------------- Spectrum ----------------

    public static Dsp.Spectrum Spectrum(float[] x, int fs)
    {
        if (x.Length >= 8192 * 2) return Dsp.Bands(Dsp.Welch(x), (double)fs / 8192);
        int pad = Math.Max(16384, x.Length);
        return Dsp.Bands(Dsp.Power(x, pad), (double)fs / pad);
    }

    /// <summary>Share of flat energy above 5 kHz in the first `ms` of a clip vs. after it (transient-only brightness check).</summary>
    public static double ShareAbove(float[] x, int fs, double hz)
    {
        var p = x.Length >= 16384 ? Dsp.Welch(x) : Dsp.Power(x, Math.Max(16384, x.Length));
        double bin = x.Length >= 16384 ? (double)fs / 8192 : (double)fs / Math.Max(16384, x.Length);
        double hi = 0, tot = 0;
        for (int k = 1; k < p.Length; k++) { tot += p[k]; if (k * bin > hz) hi += p[k]; }
        return hi / Math.Max(tot, 1e-30);
    }

    public static int Clipped(float[] x) => x.Count(v => Math.Abs(v) >= 0.9999f);

    /// <summary>Max normalized cross-correlation between two clips (lags within ±maxLag samples).</summary>
    public static double XCorr(float[] a, float[] b, int maxLag)
    {
        double ea = a.Sum(v => (double)v * v), eb = b.Sum(v => (double)v * v);
        double best = 0;
        for (int lag = -maxLag; lag <= maxLag; lag++)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int j = i + lag;
                if (j >= 0 && j < b.Length) s += a[i] * b[j];
            }
            best = Math.Max(best, Math.Abs(s) / Math.Sqrt(ea * eb));
        }
        return best;
    }
}
