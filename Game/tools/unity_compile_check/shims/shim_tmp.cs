public static class DictCompatShim { public static bool TryAdd<K, V>(this System.Collections.Generic.Dictionary<K, V> d, K k, V v) { if (d.ContainsKey(k)) return false; d.Add(k, v); return true; } }
