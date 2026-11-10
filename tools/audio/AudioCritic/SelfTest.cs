using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>Calibration of every metric on synthetic signals with a known answer. Printed at the top of out.txt.</summary>
public static class SelfTest
{
    const int Fs = 48000;

    static double[] Noise(int n, int seed) { var r = new Random(seed); var x = new double[n]; for (int i = 0; i < n; i++) x[i] = r.NextDouble() * 2 - 1; return x; }

    static double[] Tick(Random rng, double gain)
    {
        int n = (int)(0.04 * Fs);
        var g = new double[n];
        double[] f = { 380, 1250, 2050 }, tau = { 0.007, 0.005, 0.003 }, a = { 0.25, 1.0, 0.6 };
        for (int k = 0; k < f.Length; k++)
        {
            double ff = f[k] * (1 + (rng.NextDouble() - 0.5) * 0.08), ph = rng.NextDouble() * 6.28;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Fs;
                double att = t < 0.0004 ? 0.5 - 0.5 * Math.Cos(Math.PI * t / 0.0004) : 1;
                g[i] += gain * a[k] * att * Math.Exp(-t / tau[k]) * Math.Sin(2 * Math.PI * ff * t + ph);
            }
        }
        return g;
    }

    static double[] TickTrain(double rate, double jitter, double seconds, bool quant60, double bedDb, int variants, bool rotate, int seed)
    {
        var rng = new Random(seed);
        int n = (int)(seconds * Fs);
        var x = new double[n];
        var bank = new List<double[]>();
        for (int v = 0; v < Math.Max(1, variants); v++) bank.Add(Tick(new Random(seed * 31 + v), 0.3));
        double P = 1.0 / rate;
        int idx = 0;
        for (double t = 0.2; t < seconds - 0.1; t += P, idx++)
        {
            double tt = t + (rng.NextDouble() * 2 - 1) * jitter * P;
            if (quant60) tt = Math.Ceiling(tt * 60) / 60.0;
            var g = rotate ? bank[idx % bank.Count] : (variants <= 1 ? bank[0] : Tick(rng, 0.3));
            int s = (int)(tt * Fs);
            for (int i = 0; i < g.Length && s + i < n; i++) x[s + i] += g[i];
        }
        if (bedDb > -200)
        {
            var bed = D.Filt(Noise(n, seed + 7), Biquad.Hp(Fs, 450, 0.7), Biquad.Lp(Fs, 900, 0.7));
            double k = 0.3 * Math.Pow(10, bedDb / 20) / Math.Max(M.Rms(bed) * 1.414, 1e-9);
            for (int i = 0; i < n; i++) x[i] += bed[i] * k;
        }
        return x;
    }

    public static void Debug70()
    {
        var x = TickTrain(70, 0.02, 2, false, -12, 16, false, 11);
        var t = M.DetectTicks(x, Fs, false);
        Console.WriteLine(string.Join(" ", t.T.Take(60).Select(v => (v * 1000).ToString("F1"))));
        // energy trace around 0.3-0.36 s
        var y = D.Filt(x, D.Hp4(Fs, 300));
        int hop = 12, win = 120;
        for (int f = (int)(0.30 * Fs / hop); f < (int)(0.36 * Fs / hop); f += 4)
        {
            double s = 0; for (int i = 0; i < win; i++) s += y[f * hop + i] * y[f * hop + i];
            Console.Write($"{f * hop * 1000.0 / Fs:F1}:{M.DbP(s / win):F0} ");
        }
        Console.WriteLine();
    }

    public static void Run(TextWriter o)
    {
        o.WriteLine("=== SELF-TEST (synthetic signals with known answers) ===");
        // 1. loudness / levels
        int n = 5 * Fs;
        var sine = new double[n];
        for (int i = 0; i < n; i++) sine[i] = 0.1 * Math.Sin(2 * Math.PI * 997 * i / Fs);
        var L = M.Loudness(sine, Fs);
        o.WriteLine($"  997 Hz sine, peak -20 dBFS: LUFS-I {L.I:F2} (expect -23.0), RMS {M.Db(M.Rms(sine)):F2} (expect -23.01), TP {M.Db(M.TruePeak(sine)):F2} (expect -20.0)");
        var s22 = new double[5 * 22050];
        for (int i = 0; i < s22.Length; i++) s22[i] = 0.1 * Math.Sin(2 * Math.PI * 997 * i / 22050);
        o.WriteLine($"  same at 22050 Hz: LUFS-I {M.Loudness(s22, 22050).I:F2} (expect -23.0)");
        // 2. modulation index
        var car = D.Filt(Noise(n, 1), D.Join(D.Hp4(Fs, 2200), D.Lp4(Fs, 4500)));
        var am50 = new double[n]; var am4 = new double[n]; var am0 = new double[n];
        var bb = D.Filt(Noise(n, 2), D.Join(D.Hp4(Fs, 300), D.Lp4(Fs, 1500)));
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Fs;
            am50[i] = car[i] * (1 + 0.3 * Math.Sin(2 * Math.PI * 50 * t));
            am4[i] = bb[i] * (1 + 0.3 * Math.Sin(2 * Math.PI * 4 * t));
            am0[i] = bb[i];
        }
        o.WriteLine($"  2-5k noise AM 30% @50 Hz: m(2-5k,20-150) {M.ModIndex(am50, Fs, 2000, 5000, 20, 150, false).med:F3} (expect ~0.30 + noise floor)");
        o.WriteLine($"  band noise AM 30% @4 Hz: m(2-8) {M.ModIndex(am4, Fs, 0, 0, 2, 8, false).med:F3} (expect ~0.30);  unmodulated noise: m(2-8) {M.ModIndex(am0, Fs, 0, 0, 2, 8, false).med:F3}, m(20-150) {M.ModIndex(am0, Fs, 0, 0, 20, 150, false).med:F3} (noise floor of the estimator)");
        // 3. buzz index
        var pulse = new double[3 * Fs];
        for (int i = 0; i < pulse.Length; i++) { double ph = (i * 220.0 / Fs) % 1.0; pulse[i] = ph < 0.2 ? 0.3 : 0; }
        var pulse300 = new double[3 * Fs];
        for (int i = 0; i < pulse300.Length; i++) { double ph = (i * 302.0 / Fs) % 1.0; pulse300[i] = ph < 0.2 ? 0.3 : 0; }
        var bpn = D.Filt(Noise(3 * Fs, 3), Biquad.Hp(Fs, 450, 1.1), Biquad.Lp(Fs, 850, 1.1));
        var hum = new double[3 * Fs];
        for (int i = 0; i < hum.Length; i++) { double t = (double)i / Fs; hum[i] = 0.2 * Math.Sin(2 * Math.PI * 320 * t) + 0.09 * Math.Sin(2 * Math.PI * 643 * t) + 0.025 * Math.Sin(2 * Math.PI * 1290 * t); }
        var tk = TickTrain(12, 0.04, 3, false, -12, 16, false, 5);
        var tkSilent = TickTrain(6, 0.04, 3, false, -200, 16, false, 6);
        var dbl = TickTrain(8, 0.04, 3, false, -200, 1, false, 7);
        { var cp = (double[])dbl.Clone(); int dd = (int)(0.003 * Fs); for (int i = dd; i < dbl.Length; i++) dbl[i] += 0.25 * cp[i - dd]; }
        var slip = new double[3 * Fs]; { var rr = new Random(8); double t = 0.01; while (t < 2.9) { int s0 = (int)(t * Fs); for (int i = 0; i < 40 && s0 + i < slip.Length; i++) slip[s0 + i] += 0.3 * Math.Exp(-i / 8.0) * (i == 0 ? 0 : 1); t += 1.0 / 100 * (1 + (rr.NextDouble() - 0.5) * 0.1); } slip = D.Filt(slip, Biquad.Lp(Fs, 2500, 0.7)); }
        var slipIrr = new double[3 * Fs]; { var rr = new Random(9); double t = 0.01; while (t < 2.9) { int s0 = (int)(t * Fs); for (int i = 0; i < 40 && s0 + i < slipIrr.Length; i++) slipIrr[s0 + i] += 0.3 * Math.Exp(-i / 8.0) * (i == 0 ? 0 : 1); t += 0.0065 + rr.NextDouble() * 0.0065; } slipIrr = D.Filt(slipIrr, Biquad.Lp(Fs, 2500, 0.7)); }
        Func<double[], string> B = v => { var f = M.FrameStats(v, Fs, true); return $"{f.BuzzMed:F1} (≥{M.BuzzThreshold} dB in {100 * f.BuzzFrac:F0}%)"; };
        o.WriteLine($"  buzz (harmonic contrast dB): pulse 220 Hz 20% duty {B(pulse)}; pulse 302 Hz {B(pulse300)}; 3-partial hum 320 Hz {B(hum)}; band noise 620 Hz {B(bpn)}; modal ticks over bed {B(tk)}");
        o.WriteLine($"      ticks in silence {B(tkSilent)}; double-click ticks (3 ms, -12 dB) {B(dbl)}; regular 100/s stick-slip {B(slip)}; irregular 6.5-13 ms stick-slip {B(slipIrr)}");
        // 4. tick timing
        foreach (var (rate, jit, q, label) in new[] { (12.0, 0.04, false, "12/s ±4% sample-accurate"), (18.0, 0.04, false, "18/s ±4%"), (18.0, 0.04, true, "18/s ±4% snapped to 60 fps"), (40.0, 0.03, false, "40/s ±3%"), (70.0, 0.02, false, "70/s ±2%") })
        {
            var x = TickTrain(rate, jit, 6, q, -12, 16, false, 11);
            var t = M.DetectTicks(x, Fs, false);
            int expect = (int)Math.Floor((6 - 0.3) * rate) + 1;
            o.WriteLine($"  ticks {label}: detected {t.T.Count} (expect ~{expect}), rate {t.Rate:F1}/s, local IOI jitter p50 {t.JitMed:F1}% (theory for uniform ±{jit * 100:F0}%: ~{jit * 100 * 0.95:F1}%), kept {100 * t.Kept:F0}%, Q60 {100 * t.R60:F0}%");
            var tn = M.DetectTicks(TickTrain(rate, jit, 6, q, -200, 16, false, 11), Fs, false);
            o.WriteLine($"      same without bed: detected {tn.T.Count}, jitter p50 {tn.JitMed:F1}%");
        }
        // 5. machine-gun detection
        var one = TickTrain(12, 0.04, 4, false, -200, 1, false, 21);
        var rot = TickTrain(12, 0.04, 4, false, -200, 3, true, 22);
        var many = TickTrain(12, 0.04, 4, false, -200, 16, false, 23);
        Func<double[], string> G = v => { var t = M.DetectTicks(v, Fs, false); return $"sim lag1 {t.SimMed:F2}, identical {100 * t.Ident:F0}%, lag-k [{string.Join(" ", t.SimLag.Skip(1).Select(z => z.ToString("F2")))}], rotation k={t.RotationK}"; };
        o.WriteLine($"  one variant: {G(one)}");
        o.WriteLine($"  3 variants rotating: {G(rot)}");
        o.WriteLine($"  fresh random variant per tick: {G(many)}");
        // 6. periodic AM lines on a 4 s loop
        int l4 = 4 * 22050;
        var nz = D.FiltCircular(Noise(l4, 41), D.Join(D.Hp4(22050, 400), D.Lp4(22050, 1200)));
        var am = new double[l4];
        for (int i = 0; i < l4; i++) am[i] = nz[i] * (1 + 0.15 * Math.Sin(2 * Math.PI * 4 * i / 22050.0) + 0.12 * Math.Sin(2 * Math.PI * 40 * i / 22050.0));
        var la = M.LineDepth(am, 22050); var ln0 = M.LineDepth(nz, 22050);
        o.WriteLine($"  periodic AM lines, noise loop with 15% @4 Hz + 12% @40 Hz: 2-8 {100 * la.D28:F1}% @{la.F28:F2}, 20-150 {100 * la.D20:F1}% @{la.F20:F1};  plain noise loop: 2-8 {100 * ln0.D28:F1}%, 8-20 {100 * ln0.D820:F1}%, 20-150 {100 * ln0.D20:F1}%");
        // 7. seam
        int ln = 2 * 22050;
        var raw = Noise(ln, 9);
        var circ = D.FiltCircular(raw, D.Lp4(22050, 1500));
        var lin = D.Filt(raw, D.Lp4(22050, 1500));
        o.WriteLine($"  seam, circularly filtered noise loop: {M.SeamAt(circ, 22050, 0)}");
        o.WriteLine($"  seam, linearly filtered noise cut to a loop: {M.SeamAt(lin, 22050, 0)}");
        o.WriteLine();
    }
}
