// Copyright (c) You-Ri, 2026
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObjectの永続化を担当するユーティリティクラス。
    /// PlayerPrefs / ファイル / メモリストレージへの保存・読み込み・削除を提供する。
    /// </summary>
    public static class ExposedPropertyPersistence
    {
        // -------------------------------------------------------
        // PlayerPrefs
        // -------------------------------------------------------

        internal static void SavePlayerPrefs(IExposedObjectResolver resolver, string id, ExposedObject obj, bool isDirtyOnly = false)
        {
            if (string.IsNullOrEmpty(id)) throw new System.ArgumentNullException(nameof(id));
            if (obj == null) throw new System.ArgumentNullException(nameof(obj));

            var json = ExposedPropertySerializer.ToJson(obj, resolver, isDirtyOnly, forPersistence: true);
            PlayerPrefs.SetString($"{id}", json);
        }

        internal static void LoadPlayerPrefs(string id, ExposedObject obj)
        {
            if (string.IsNullOrEmpty(id)) throw new System.ArgumentNullException(nameof(id));
            if (obj == null) throw new System.ArgumentNullException(nameof(obj));

            var json = PlayerPrefs.GetString($"{id}", "{}");
            ExposedPropertySerializer.FromJson(json, obj);
        }

        internal static void DeletePlayerPrefs(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new System.ArgumentNullException(nameof(id));

            PlayerPrefs.DeleteKey($"{id}");
        }

        // -------------------------------------------------------
        // File
        // -------------------------------------------------------

        internal static void SaveToFile(IExposedObjectResolver resolver, string filePath, ExposedObject obj, bool isDirtyOnly = false)
        {
            if (string.IsNullOrEmpty(filePath)) throw new System.ArgumentNullException(nameof(filePath));
            if (obj == null) throw new System.ArgumentNullException(nameof(obj));

            var json = ExposedPropertySerializer.ToJson(obj, resolver, isDirtyOnly, forPersistence: true);
            System.IO.File.WriteAllText(filePath, json);
        }

        internal static bool LoadFromFile(string filePath, ExposedObject obj)
        {
            if (string.IsNullOrEmpty(filePath)) throw new System.ArgumentNullException(nameof(filePath));
            if (obj == null) throw new System.ArgumentNullException(nameof(obj));

            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogWarning($"[RemoteControl] File not found: '{filePath}'");
                return false;
            }

            var json = System.IO.File.ReadAllText(filePath);
            return ExposedPropertySerializer.FromJson(json, obj);
        }

        // -------------------------------------------------------
        // MemoryStorage
        // -------------------------------------------------------

        internal static void SaveToMemory(IExposedObjectResolver resolver, MemoryStorage storage, string key, ExposedObject obj, bool isDirtyOnly = false)
        {
            if (storage == null) throw new System.ArgumentNullException(nameof(storage));
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));
            if (obj == null) throw new System.ArgumentNullException(nameof(obj));

            var json = ExposedPropertySerializer.ToJson(obj, resolver, isDirtyOnly, forPersistence: true);
            storage.Set(key, json);
        }

        internal static bool LoadFromMemory(MemoryStorage storage, string key, ExposedObject obj)
        {
            if (storage == null) throw new System.ArgumentNullException(nameof(storage));
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));
            if (obj == null) throw new System.ArgumentNullException(nameof(obj));

            if (!storage.Has(key))
            {
                return false;
            }

            var json = storage.Get(key);
            return ExposedPropertySerializer.FromJson(json, obj);
        }

        internal static void DeleteFromMemory(MemoryStorage storage, string key)
        {
            if (storage == null) throw new System.ArgumentNullException(nameof(storage));
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));

            storage.Delete(key);
        }
    }
}
