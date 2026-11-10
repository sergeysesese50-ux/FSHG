using System;
using System.Collections.Concurrent;

/// <summary>RBJ biquad, transposed direct form II, double precision.</summary>
public sealed class Biquad
{
    public double B0, B1, B2, A1, A2;
    double z1, z2;
    public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    { B0 = b0 / a0; B1 = b1 / a0; B2 = b2 / a0; A1 = a1 / a0; A2 = a2 / a0; }
    public static Biquad Lp(double fs, double f, double q)
    {
        double w = 2 * Math.PI * f / fs, c = Math.Cos(w), al = Math.Sin(w) / (2 * q);
        return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
    }
    public static Biquad Hp(double fs, double f, double q)
    {
        double w = 2 * Math.PI * f / fs, c = Math.Cos(w), al = Math.Sin(w) / (2 * q);
        return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
    }
    public double Step(double x) { double y = B0 * x + z1; z1 = B1 * x - A1 * y + z2; z2 = B2 * x - A2 * y; return y; }
    public void Reset() { z1 = z2 = 0; }
}

public static class D
{
    public const double Q1 = 0.5411961, Q2 = 1.3065630; // 4th-order Butterworth sections

    public static Biquad[] Lp4(double fs, double f) => new[] { Biquad.Lp(fs, f, Q1), Biquad.Lp(fs, f, Q2) };
    public static Biquad[] Hp4(double fs, double f) => new[] { Biquad.Hp(fs, f, Q1), Biquad.Hp(fs, f, Q2) };

    public static Biquad[] Join(params Biquad[][] parts)
    {
        int n = 0; foreach (var p in parts) n += p.Length;
        var r = new Biquad[n]; int k = 0;
        foreach (var p in parts) foreach (var b in p) r[k++] = b;
        return r;
    }

    public static double[] Filt(double[] x, params Biquad[] chain)
    {
        var y = (double[])x.Clone();
        foreach (var b in chain)
        {
            b.Reset();
            for (int i = 0; i < y.Length; i++) y[i] = b.Step(y[i]);
        }
        return y;
    }

    /// <summary>Steady-state circular filtering of a loop: each stage runs priming passes, the last pass is kept.</summary>
    public static double[] FiltCircular(double[] x, params Biquad[] chain)
    {
        var y = (double[])x.Clone();
        foreach (var b in chain)
        {
            b.Reset();
            for (int r = 0; r < 3; r++)
                for (int i = 0; i < y.Length; i++)
                {
                    double v = b.Step(y[i]);
                    if (r == 2) y[i] = v;
                }
        }
        return y;
    }

    // ---------------- FFT ----------------
    static readonly ConcurrentDictionary<int, (double[] c, double[] s)> Tw = new ConcurrentDictionary<int, (double[], double[])>();

    public static int NextPow2(int n) { int m = 1; while (m < n) m <<= 1; return m; }

    public static void Fft(double[] re, double[] im, bool inverse = false)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        var t = Tw.GetOrAdd(n, nn =>
        {
            var c = new double[Math.Max(1, nn / 2)]; var s = new double[Math.Max(1, nn / 2)];
            for (int k = 0; k < nn / 2; k++) { c[k] = Math.Cos(2 * Math.PI * k / nn); s[k] = Math.Sin(2 * Math.PI * k / nn); }
            return (c, s);
        });
        double sg = inverse ? 1 : -1;
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len / 2, step = n / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < half; k++)
                {
                    double wr = t.c[k * step], wi = sg * t.s[k * step];
                    int a = i + k, b = a + half;
                    double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                }
        }
        if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    /// <summary>Integer-factor decimation after an 8th-order Butterworth anti-alias low-pass at 0.4 of the new rate.</summary>
    public static double[] Decimate(double[] x, int fs, int factor, out int newFs)
    {
        newFs = fs / factor;
        if (factor <= 1) return x;
        var y = Filt(x, Join(Lp4(fs, 0.4 * newFs), Lp4(fs, 0.4 * newFs)));
        var r = new double[y.Length / factor];
        for (int i = 0; i < r.Length; i++) r[i] = y[i * factor];
        return r;
    }

    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return double.NaN;
        double pos = p / 100.0 * (sorted.Length - 1);
        int i = (int)Math.Floor(pos);
        if (i >= sorted.Length - 1) return sorted[sorted.Length - 1];
        return sorted[i] + (pos - i) * (sorted[i + 1] - sorted[i]);
    }

    public static double Median(double[] v)
    {
        if (v.Length == 0) return double.NaN;
        var s = (double[])v.Clone(); Array.Sort(s); return Percentile(s, 50);
    }

    public static double Pct(double[] v, double p)
    {
        if (v.Length == 0) return double.NaN;
        var s = (double[])v.Clone(); Array.Sort(s); return Percentile(s, p);
    }

    /// <summary>Weighted percentile (values with non-negative weights).</summary>
    public static double WPercentile(double[] v, double[] w, double p)
    {
        if (v.Length == 0) return double.NaN;
        var idx = new int[v.Length];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        Array.Sort(idx, (a, b) => v[a].CompareTo(v[b]));
        double tot = 0; foreach (var x in w) tot += x;
        if (tot <= 0) return v[idx[idx.Length / 2]];
        double acc = 0, target = p / 100.0 * tot;
        foreach (var i in idx) { acc += w[i]; if (acc >= target) return v[i]; }
        return v[idx[idx.Length - 1]];
    }

    /// <summary>Percentile rank (0..100) of value among samples (fraction strictly below).</summary>
    public static double Rank(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (sorted[mid] < value) lo = mid + 1; else hi = mid; }
        return sorted.Length == 0 ? double.NaN : 100.0 * lo / sorted.Length;
    }
}
