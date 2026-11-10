using System;
using System.IO;

/// <summary>
/// Пути харнессов относительно репозитория: корень ищется вверх от папки сборки (bin/Release/net8.0) по client/Assets,
/// поэтому WAV пишутся в &lt;репозиторий&gt;/.previews/audio/… на любой машине.
/// </summary>
internal static class RepoPaths
{
    public static string Root
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "client", "Assets")))
                    return d.FullName;
            return Directory.GetCurrentDirectory();
        }
    }

    public static string Previews(string sub = "") => Path.Combine(Root, ".previews", "audio", sub);
}
