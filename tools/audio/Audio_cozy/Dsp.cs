using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>Analysis helpers: exact-length DFT (Bluestein), Welch PSD, weightings, level and impulse metrics, WAV IO.</summary>
public static class Dsp
{
    // ---------------- FFT ----------------

    public static void Fft(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        double sign = inverse ? 1 : -1;
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len / 2;
            var cr = new double[half];
            var ci = new double[half];
            for (int k = 0; k < half; k++)
            {
                double a = sign * 2 * Math.PI * k / len;
                cr[k] = Math.Cos(a);
                ci[k] = Math.Sin(a);
            }
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    double xr = re[b] * cr[k] - im[b] * ci[k];
                    double xi = re[b] * ci[k] + im[b] * cr[k];
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                }
        }
        if (inverse)
            for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    static bool Pow2(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>Exact DFT of any length (Bluestein). Returns complex spectrum of length n.</summary>
    public static (double[] re, double[] im) Dft(double[] x)
    {
        int n = x.Length;
        if (Pow2(n))
        {
            var r = (double[])x.Clone();
            var i0 = new double[n];
            Fft(r, i0, false);
            return (r, i0);
        }
        int m = 1;
        while (m < 2 * n - 1) m <<= 1;
        var wr = new double[n];
        var wi = new double[n];
        for (int k = 0; k < n; k++)
        {
            long kk = (long)k * k % (2L * n);
            double ang = Math.PI * kk / n;
            wr[k] = Math.Cos(ang);
            wi[k] = Math.Sin(ang);
        }
        var ar = new double[m]; var ai = new double[m];
        var br = new double[m]; var bi = new double[m];
        for (int k = 0; k < n; k++)
        {
            ar[k] = x[k] * wr[k];
            ai[k] = -x[k] * wi[k];
        }
        br[0] = wr[0]; bi[0] = wi[0];
        for (int k = 1; k < n; k++)
        {
            br[k] = br[m - k] = wr[k];
            bi[k] = bi[m - k] = wi[k];
        }
        Fft(ar, ai, false);
        Fft(br, bi, false);
        for (int k = 0; k < m; k++)
        {
            double r = ar[k] * br[k] - ai[k] * bi[k];
            double i = ar[k] * bi[k] + ai[k] * br[k];
            ar[k] = r; ai[k] = i;
        }
        Fft(ar, ai, true);
        var xr = new double[n]; var xi = new double[n];
        for (int k = 0; k < n; k++)
        {
            xr[k] = ar[k] * wr[k] + ai[k] * wi[k];
            xi[k] = ai[k] * wr[k] - ar[k] * wi[k];
        }
        return (xr, xi);
    }

    /// <summary>One-sided power spectrum |X_k|^2, k = 0..n/2, exact length (no window).</summary>
    public static double[] Power(float[] x, int padTo = 0)
    {
        int n = Math.Max(x.Length, padTo);
        var d = new double[n];
        for (int i = 0; i < x.Length; i++) d[i] = x[i];
        var (re, im) = Dft(d);
        var p = new double[n / 2 + 1];
        for (int k = 0; k <= n / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k];
        return p;
    }

    /// <summary>Welch PSD with Hann window (power per bin, arbitrary scale). Bin width = fs / seg.</summary>
    public static double[] Welch(float[] x, int seg = 8192)
    {
        var acc = new double[seg / 2 + 1];
        var w = new double[seg];
        for (int i = 0; i < seg; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / seg);
        int count = 0;
        var re = new double[seg]; var im = new double[seg];
        for (int start = 0; start + seg <= x.Length; start += seg / 2)
        {
            for (int i = 0; i < seg; i++) { re[i] = x[start + i] * w[i]; im[i] = 0; }
            Fft(re, im, false);
            for (int k = 0; k <= seg / 2; k++) acc[k] += re[k] * re[k] + im[k] * im[k];
            count++;
        }
        if (count > 0) for (int k = 0; k < acc.Length; k++) acc[k] /= count;
        return acc;
    }

    // ---------------- Weightings ----------------

    /// <summary>A-weighting as a power factor (0 dB at 1 kHz).</summary>
    public static double AWeight(double f)
    {
        if (f <= 0) return 0;
        double f2 = f * f;
        double ra = 12194.0 * 12194.0 * f2 * f2 /
                    ((f2 + 20.6 * 20.6) * Math.Sqrt((f2 + 107.7 * 107.7) * (f2 + 737.9 * 737.9)) * (f2 + 12194.0 * 12194.0));
        double g = ra * Math.Pow(10, 2.0 / 20);
        return g * g;
    }

    /// <summary>Very rough phone loudspeaker: 2nd-order high-pass at 700 Hz (power factor).</summary>
    public static double PhoneWeight(double f)
    {
        double r = Math.Pow(f / 700.0, 4);
        return r / (1 + r);
    }

    public static readonly double[] BandEdges = { 0, 300, 2000, 5000, 1e9 };
    public static readonly string[] BandNames = { "<300", "300-2k", "2k-5k", ">5k" };

    public sealed class Spectrum
    {
        public double[] Flat = new double[4], A = new double[4], PhoneA = new double[4];
        public double CentroidHz, CentroidAHz, Above11kShare;
        public double LevelA; // A-weighted power relative to flat power (dB)
    }

    public static Spectrum Bands(double[] p, double binHz)
    {
        var s = new Spectrum();
        double tf = 0, ta = 0, tp = 0, cNum = 0, cDen = 0, caNum = 0, caDen = 0, hi = 0;
        for (int k = 1; k < p.Length; k++)
        {
            double f = k * binHz;
            int b = f < 300 ? 0 : f < 2000 ? 1 : f < 5000 ? 2 : 3;
            double a = AWeight(f);
            double ph = PhoneWeight(f);
            s.Flat[b] += p[k]; tf += p[k];
            s.A[b] += p[k] * a; ta += p[k] * a;
            s.PhoneA[b] += p[k] * a * ph; tp += p[k] * a * ph;
            double mag = Math.Sqrt(p[k]);
            cNum += f * mag; cDen += mag;
            double magA = Math.Sqrt(p[k] * a);
            caNum += f * magA; caDen += magA;
            if (f > 11025) hi += p[k];
        }
        for (int b = 0; b < 4; b++)
        {
            s.Flat[b] /= Math.Max(tf, 1e-300);
            s.A[b] /= Math.Max(ta, 1e-300);
            s.PhoneA[b] /= Math.Max(tp, 1e-300);
        }
        s.CentroidHz = cNum / Math.Max(cDen, 1e-300);
        s.CentroidAHz = caNum / Math.Max(caDen, 1e-300);
        s.Above11kShare = hi / Math.Max(tf, 1e-300);
        s.LevelA = 10 * Math.Log10(Math.Max(ta, 1e-300) / Math.Max(tf, 1e-300));
        return s;
    }

    // ---------------- Levels ----------------

    public static double Db(double amp) => 20 * Math.Log10(Math.Max(amp, 1e-12));
    public static double Peak(float[] x) => x.Length == 0 ? 0 : x.Max(v => Math.Abs((double)v));
    public static double Rms(float[] x) => x.Length == 0 ? 0 : Math.Sqrt(x.Average(v => (double)v * v));
    public static double Mean(float[] x) => x.Length == 0 ? 0 : x.Average(v => (double)v);

    public static double MaxShortRms(float[] x, int win)
    {
        if (x.Length <= win) return Rms(x);
        double sum = 0, best = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sum += (double)x[i] * x[i];
            if (i >= win) sum -= (double)x[i - win] * x[i - win];
            if (i >= win - 1) best = Math.Max(best, sum / win);
        }
        return Math.Sqrt(best);
    }

    public static double ExcessKurtosis(float[] x)
    {
        double m = Mean(x), m2 = 0, m4 = 0;
        foreach (var v in x) { double d = v - m; m2 += d * d; m4 += d * d * d * d; }
        m2 /= x.Length; m4 /= x.Length;
        return m2 > 0 ? m4 / (m2 * m2) - 3 : 0;
    }

    /// <summary>Impulses: second differences beyond 6 robust sigmas (MAD-based); also the largest single-sample step / peak.</summary>
    public static (int impulses, double maxStepOfPeak, double p999StepOfRms) Edges(float[] x, bool circular)
    {
        int n = x.Length;
        var d2 = new List<double>(n);
        var d1 = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            int ip = i + 1, im = i - 1;
            if (circular) { ip %= n; im = (im + n) % n; }
            if (ip < n) d1.Add(Math.Abs(x[ip] - x[i]));
            if (im >= 0 && ip < n) d2.Add(x[ip] - 2.0 * x[i] + x[im]);
        }
        var abs2 = d2.Select(Math.Abs).OrderBy(v => v).ToList();
        double mad = abs2.Count > 0 ? abs2[abs2.Count / 2] : 0;
        double sigma = 1.4826 * mad;
        int imp = sigma > 0 ? abs2.Count(v => v > 6 * sigma) : 0;
        double peak = Peak(x), rms = Rms(x);
        var s1 = d1.OrderBy(v => v).ToList();
        double p999 = s1.Count > 0 ? s1[Math.Min(s1.Count - 1, (int)(s1.Count * 0.999))] : 0;
        return (imp, s1.Count > 0 ? s1[s1.Count - 1] / Math.Max(peak, 1e-12) : 0, p999 / Math.Max(rms, 1e-12));
    }

    /// <summary>Loop seam: step and curvature at the wrap point, as percentile ranks within the loop's own distribution.</summary>
    public static (double step, double stepRank, double curvRank) Seam(float[] x)
    {
        int n = x.Length;
        double seamStep = Math.Abs(x[0] - x[n - 1]);
        double seamCurv = Math.Abs(x[0] - 2.0 * x[n - 1] + x[n - 2]);
        int lessStep = 0, lessCurv = 0, total = 0;
        for (int i = 1; i < n - 1; i++)
        {
            if (Math.Abs(x[i] - x[i - 1]) < seamStep) lessStep++;
            if (Math.Abs(x[i + 1] - 2.0 * x[i] + x[i - 1]) < seamCurv) lessCurv++;
            total++;
        }
        return (seamStep, 100.0 * lessStep / total, 100.0 * lessCurv / total);
    }

    /// <summary>Amplitude-modulation spectrum of a periodic loop: envelope (|x| smoothed twice at 30 Hz, circular), exact DFT.</summary>
    public static List<(double hz, double depth)> Modulation(float[] x, int fs, double maxHz = 40)
    {
        int n = x.Length;
        var e = new double[n];
        for (int i = 0; i < n; i++) e[i] = Math.Abs(x[i]);
        for (int pass = 0; pass < 2; pass++)
        {
            double a = 1 - Math.Exp(-2 * Math.PI * 30 / fs);
            double y = 0;
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < n; i++)
                {
                    y += a * (e[i] - y);
                    if (r == 1) e[i] = y;
                }
        }
        var (re, im) = Dft(e);
        double dc = re[0] / n;
        var list = new List<(double, double)>();
        for (int k = 1; k <= n / 2; k++)
        {
            double f = (double)k * fs / n;
            if (f > maxHz) break;
            double amp = 2 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / n;
            list.Add((f, amp / Math.Max(dc, 1e-12)));
        }
        return list.OrderByDescending(t => t.Item2).ToList();
    }

    // ---------------- WAV ----------------

    public static void WriteWav(string path, float[] samples, int fs, int repeat = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var w = new BinaryWriter(File.Create(path));
        int n = samples.Length * repeat;
        w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(fs); w.Write(fs * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(n * 2);
        for (int r = 0; r < repeat; r++)
            foreach (var s in samples)
            {
                double v = Math.Max(-1.0, Math.Min(1.0, s));
                w.Write((short)Math.Round(v * 32767));
            }
    }
}
