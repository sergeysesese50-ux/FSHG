using System;
using System.IO;
using System.Text;

/// <summary>Decoded mono audio (multi-channel files are averaged) plus format facts.</summary>
public sealed class AudioBuf
{
    public double[] X;
    public int Fs, Channels, Bits;
    public bool IsFloat;
    public int FullScale;   // samples at the integer format's extreme (or |x| >= 1 for float)
    public int NearClip;    // samples with |x| >= -0.1 dBFS
}

public static class Wav
{
    public static AudioBuf Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
            throw new InvalidDataException("not a RIFF/WAVE file: " + path);
        int pos = 12, fmt = 0, ch = 0, fs = 0, bits = 0, dOff = -1, dLen = 0;
        while (pos + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, pos, 4);
            int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ")
            {
                fmt = BitConverter.ToUInt16(b, pos + 8);
                ch = BitConverter.ToUInt16(b, pos + 10);
                fs = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToUInt16(b, pos + 22);
                if (fmt == 0xFFFE && len >= 26) fmt = BitConverter.ToUInt16(b, pos + 32);
            }
            else if (id == "data")
            {
                dOff = pos + 8;
                dLen = (len < 0 || dOff + len > b.Length) ? b.Length - dOff : len;
                break;
            }
            pos += 8 + len + (len & 1);
        }
        if (dOff < 0 || ch <= 0) throw new InvalidDataException("no data/fmt chunk: " + path);
        int bps = bits / 8;
        int frames = dLen / (bps * ch);
        var x = new double[frames];
        int full = 0, near = 0;
        double nearThr = Math.Pow(10, -0.1 / 20);
        for (int i = 0; i < frames; i++)
        {
            double s = 0;
            for (int c = 0; c < ch; c++)
            {
                int o = dOff + (i * ch + c) * bps;
                double v;
                if (fmt == 3 && bits == 32) { v = BitConverter.ToSingle(b, o); if (Math.Abs(v) >= 1.0) full++; }
                else if (fmt == 3 && bits == 64) { v = BitConverter.ToDouble(b, o); if (Math.Abs(v) >= 1.0) full++; }
                else if (bits == 16) { short q = BitConverter.ToInt16(b, o); v = q / 32768.0; if (q == 32767 || q == -32768) full++; }
                else if (bits == 24)
                {
                    int q = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
                    if ((q & 0x800000) != 0) q |= unchecked((int)0xFF000000);
                    v = q / 8388608.0; if (q >= 8388607 || q <= -8388608) full++;
                }
                else if (bits == 32) { int q = BitConverter.ToInt32(b, o); v = q / 2147483648.0; if (q == int.MaxValue || q == int.MinValue) full++; }
                else if (bits == 8) { v = (b[o] - 128) / 128.0; if (b[o] == 0 || b[o] == 255) full++; }
                else throw new InvalidDataException($"unsupported format {fmt}/{bits} bit: {path}");
                if (Math.Abs(v) >= nearThr) near++;
                s += v;
            }
            x[i] = s / ch;
        }
        return new AudioBuf { X = x, Fs = fs, Channels = ch, Bits = bits, IsFloat = fmt == 3, FullScale = full, NearClip = near };
    }

    /// <summary>16-bit PCM mono with TPDF dither.</summary>
    public static void Write16(string path, double[] x, int fs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var w = new BinaryWriter(File.Create(path));
        int n = x.Length;
        w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(fs); w.Write(fs * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(n * 2);
        var rng = new Random(12345);
        foreach (var s in x)
        {
            double d = (rng.NextDouble() - rng.NextDouble()) / 32768.0;
            double v = Math.Max(-1.0, Math.Min(32767.0 / 32768.0, s + d));
            w.Write((short)Math.Round(v * 32768.0));
        }
    }
}
