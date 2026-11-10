using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Variant-set diversity: the files that play every grain variant in a row, separated by silence.</summary>
public static class Variants
{
    public sealed class Result { public int N; public double PairMean = double.NaN, PairMax = double.NaN, NearestMed = double.NaN; public int Clusters; }

    /// <summary>
    /// Segments the file at silences (5 ms frames more than 45 dB under the loudest frame, gaps ≥ 40 ms), high-passes at 300 Hz, and compares
    /// every pair of whole variants by maximum normalised cross-correlation (±0.5 ms) over their common length. Clusters = groups whose
    /// members are ≥ 0.97 alike (effectively the same sound); NearestMed = median similarity of each variant to its closest sibling.
    /// </summary>
    public static Result Analyze(double[] x, int fs)
    {
        var r = new Result();
        var y = D.Filt(x, D.Hp4(fs, 300));
        int fr = (int)Math.Round(0.005 * fs), nf = y.Length / fr;
        if (nf < 4) return r;
        var e = new double[nf];
        for (int f = 0; f < nf; f++) { double s = 0; for (int i = f * fr; i < (f + 1) * fr; i++) s += y[i] * y[i]; e[f] = M.DbP(s / fr); }
        double thr = e.Max() - 45;
        int gapF = 8;
        var regions = new List<(int a, int b)>();
        int start = -1, lastOn = -100;
        for (int f = 0; f < nf; f++)
        {
            if (e[f] < thr) continue;
            if (start < 0) start = f;
            else if (f - lastOn > gapF) { regions.Add((start, lastOn)); start = f; }
            lastOn = f;
        }
        if (start >= 0) regions.Add((start, lastOn));
        var segs = regions.Where(g => g.b - g.a >= 1).Select(g =>
        {
            int a = Math.Max(0, g.a * fr - fr), b = Math.Min(y.Length, (g.b + 2) * fr);
            var s = new double[b - a]; Array.Copy(y, a, s, 0, s.Length); return s;
        }).ToList();
        r.N = segs.Count;
        if (segs.Count < 3) return r;
        int sh = (int)Math.Round(0.0005 * fs);
        var sim = new double[segs.Count, segs.Count];
        double sum = 0, mx = 0; int cnt = 0;
        var parent = Enumerable.Range(0, segs.Count).ToArray();
        Func<int, int> find = null; find = a => parent[a] == a ? a : (parent[a] = find(parent[a]));
        for (int a = 0; a < segs.Count; a++)
            for (int b = a + 1; b < segs.Count; b++)
            {
                double s = AlignSim(segs[a], segs[b], sh);
                sim[a, b] = sim[b, a] = s; sum += s; cnt++; if (s > mx) mx = s;
                if (s >= 0.97) parent[find(a)] = find(b);
            }
        r.PairMean = sum / cnt; r.PairMax = mx;
        r.Clusters = Enumerable.Range(0, segs.Count).Select(find).Distinct().Count();
        var nearest = Enumerable.Range(0, segs.Count).Select(a => Enumerable.Range(0, segs.Count).Where(b => b != a).Max(b => sim[a, b])).ToArray();
        r.NearestMed = D.Median(nearest);
        return r;
    }

    /// <summary>Onsets aligned on the first sample above −20 dB of each segment's peak, then max normalised xcorr over ±sh.</summary>
    static double AlignSim(double[] a, double[] b, int sh)
    {
        int oa = Onset(a), ob = Onset(b);
        int L = Math.Min(a.Length - oa, b.Length - ob) - 2 * sh;
        if (L < 16) return double.NaN;
        double best = -1, ea = 0;
        for (int i = 0; i < L; i++) { double v = a[oa + sh + i]; ea += v * v; }
        for (int d = -sh; d <= sh; d++)
        {
            double acc = 0, eb = 0;
            for (int i = 0; i < L; i++) { double av = a[oa + sh + i], bv = b[ob + sh + i + d]; acc += av * bv; eb += bv * bv; }
            double v = acc / Math.Sqrt(Math.Max(ea * eb, 1e-30));
            if (v > best) best = v;
        }
        return best;
    }

    static int Onset(double[] s)
    {
        double pk = 0; foreach (var v in s) pk = Math.Max(pk, Math.Abs(v));
        for (int i = 0; i < s.Length; i++) if (Math.Abs(s[i]) >= 0.1 * pk) return Math.Max(0, i - 8);
        return 0;
    }
}
