using System.Collections.Generic;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// インメモリでキー・バリュー形式のデータを保持するストレージ
    /// </summary>
    internal class MemoryStorage
    {
        private readonly Dictionary<string, string> _storage = new();

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));
            _storage[key] = value;
        }

        public string Get(string key, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));
            return _storage.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));
            return _storage.ContainsKey(key);
        }

        public void Delete(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentNullException(nameof(key));
            _storage.Remove(key);
        }

        public void Clear()
        {
            _storage.Clear();
        }
    }
}
