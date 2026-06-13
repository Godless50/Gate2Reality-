using System;
using System.IO;
using UnityEngine;

namespace Gate2Reality.Persistence
{
    /// <summary>
    /// Снимок прогресса игрока. [Serializable] под JsonUtility — компактно,
    /// без сторонних зависимостей. Версионируется на случай миграций формата.
    /// </summary>
    [Serializable]
    public sealed class ProgressData
    {
        public int version = ProgressStore.CurrentVersion;
        public int chapter = 1;
        public int nodeIndex = 0;       // активный узел графа на момент сейва
        public int seenObjectsMask = 0; // битовая маска NarrativeLabel (опционально)
        public bool crossedOver = false; // игрок пересёк портал (вход в изнанку)
        public long savedAtUnixSeconds = 0;
    }

    /// <summary>
    /// Хранилище прогресса: один JSON-файл в Application.persistentDataPath.
    /// Чистая I/O-утилита без Unity-lifecycle — её дёргает ProgressTracker.
    /// Сейвы редки (на границах узлов), так что синхронная запись допустима;
    /// файл крошечный (&lt;200 Б).
    /// </summary>
    public static class ProgressStore
    {
        public const int CurrentVersion = 1;
        private const string FileName = "progress.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool HasSave => File.Exists(FilePath);

        public static void Save(ProgressData data)
        {
            if (data == null) return;
            data.version = CurrentVersion;
            data.savedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: false);
                // Атомарность: пишем во временный файл, затем заменяем.
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gate2Reality] Сейв прогресса не записан: {e.Message}");
            }
        }

        public static bool TryLoad(out ProgressData data)
        {
            data = null;
            try
            {
                if (!File.Exists(FilePath)) return false;
                string json = File.ReadAllText(FilePath);
                data = JsonUtility.FromJson<ProgressData>(json);
                if (data == null) return false;

                // Точка миграции форматов при росте version.
                if (data.version != CurrentVersion)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Gate2Reality] Сейв версии {data.version}, текущая {CurrentVersion} — миграция/сброс при необходимости.");
#endif
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gate2Reality] Сейв прогресса повреждён, игнорируем: {e.Message}");
                data = null;
                return false;
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gate2Reality] Не удалось удалить сейв: {e.Message}");
            }
        }
    }
}
