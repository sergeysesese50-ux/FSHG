using System;
using System.IO;
using Fishing.Unity;

// Writes each palette's raw loop clips as 32-bit float WAV (no tiling, no gain) for seam / modulation checks.
public static class LoopDump
{
    public static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "loops";
        Directory.CreateDirectory(dir);
        Save(dir, "current_Whir", FightSynth.Whir(), FightSynth.SampleRate);
        Save(dir, "current_Growl", FightSynth.Growl(), FightSynth.SampleRate);
        Save(dir, "current_ClickLoop", FightSynth.ClickLoop(), FightSynth.SampleRate);
        Save(dir, "current_LineSing", FightSynth.LineSing(), FightSynth.SampleRate);
        Save(dir, "natural_LayBed", NaturalSynth.LayBed(), NaturalSynth.SampleRate);
        Save(dir, "natural_LineHum", NaturalSynth.LineHum(), NaturalSynth.SampleRate);
        Save(dir, "natural_Ambience", NaturalSynth.LakeMorning(64), NaturalSynth.SampleRate);
        Save(dir, "tactile_SpoolPurr", TactileSynth.SpoolPurr(), TactileSynth.SampleRate);
        Save(dir, "tactile_LineHum", TactileSynth.LineHum(), TactileSynth.SampleRate);
        Save(dir, "tactile_Ambience", TactileSynth.Ambience(64.0), TactileSynth.SampleRate);
        Save(dir, "cozy_ReelBed", CozySynth.ReelBed(), CozySynth.SampleRate);
        Save(dir, "cozy_StrainBed", CozySynth.StrainBed(), CozySynth.SampleRate);
        Save(dir, "cozy_LineTension", CozySynth.LineTension(), CozySynth.SampleRate);
        Save(dir, "cozy_Ambience", CozySynth.AmbienceBed(), CozySynth.SampleRate);
    }

    static void Save(string dir, string name, float[] x, int fs)
    {
        string path = Path.Combine(dir, "loop_" + name + ".wav");
        using var w = new BinaryWriter(File.Create(path));
        int n = x.Length;
        w.Write("RIFF".ToCharArray()); w.Write(36 + n * 4); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)3); w.Write((short)1);
        w.Write(fs); w.Write(fs * 4); w.Write((short)4); w.Write((short)32);
        w.Write("data".ToCharArray()); w.Write(n * 4);
        foreach (var s in x) w.Write(s);
        Console.WriteLine($"{name}: {n} samples, {n / (double)fs:F3} s");
    }
}
