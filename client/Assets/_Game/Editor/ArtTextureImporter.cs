using UnityEditor;
using UnityEngine;

namespace Fishing.Editor
{
    /// <summary>
    /// Настройки импорта картинок интерфейса из Assets/_Game/Resources/Art (фон турбазы и подобные):
    /// без подгонки к степени двойки и без мип-уровней, до 4096 пикселей, качественное сжатие.
    /// Задаются кодом, чтобы не править настройки руками в инспекторе у каждой новой картинки.
    /// </summary>
    public sealed class ArtTextureImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Game/Resources/Art/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
        }
    }
}
