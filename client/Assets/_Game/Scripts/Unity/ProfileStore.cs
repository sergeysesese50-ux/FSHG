using System;
using System.IO;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Профиль игрока в файле на устройстве (деньги, садок): persistentDataPath/profile.json.
    /// Временное хранилище до сервера: потом деньги и инвентарь будет вести он.
    /// Нечитаемый файл не роняет игру: он переименовывается в profile.broken.json, игрок начинает с чистого профиля.
    /// </summary>
    public static class ProfileStore
    {
        private const string FileName = "profile.json";
        private const string BrokenName = "profile.broken.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static PlayerProfile Load(FishDatabase fish)
        {
            string path = FilePath;
            if (!File.Exists(path))
                return new PlayerProfile();
            try
            {
                var state = JsonUtility.FromJson<ProfileState>(File.ReadAllText(path));
                if (state == null)
                    throw new FormatException("файл профиля пуст");
                return PlayerProfile.FromState(state, fish);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Profile] Профиль не прочитан, начинаем с чистого: " + e.Message);
                try
                {
                    string broken = Path.Combine(Application.persistentDataPath, BrokenName);
                    File.Copy(path, broken, true);
                }
                catch (Exception copyError)
                {
                    Debug.LogWarning("[Profile] Не удалось сохранить копию нечитаемого профиля: " + copyError.Message);
                }
                return new PlayerProfile();
            }
        }

        /// <summary>Пишет профиль: сначала во временный файл, потом подменяет основной — обрыв записи не портит сохранение.</summary>
        public static void Save(PlayerProfile profile)
        {
            if (profile == null)
                return;
            try
            {
                string path = FilePath;
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(profile.ToState()));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Profile] Профиль не сохранён: " + e.Message);
            }
        }
    }
}
