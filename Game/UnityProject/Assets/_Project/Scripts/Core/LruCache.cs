using System;
using System.Collections.Generic;

namespace MorpheHouse.Core
{
    /// <summary>
    /// 개수 상한과 (추정) 바이트 상한을 함께 가진 LRU 캐시(순수, Unity 비의존). 상한을 넘으면 가장 오래 쓰지 않은 항목부터 내보내고
    /// <c>onEvict</c> 콜백을 부른다(호출자가 거기서 리소스를 해제한다).
    /// 규칙: ① <see cref="TryGet"/>/<see cref="Put"/> 은 항목을 "방금 씀"으로 올린다(touch on use).
    /// ② <see cref="Pin"/> 으로 고정한 항목은 내보내지 않는다(고정이 풀릴 때까지 상한을 넘을 수 있다).
    /// ③ 방금 넣은 항목은 같은 Put 안에서 내보내지 않는다(그 하나가 바이트 상한보다 커도 하나는 보관).
    /// 스레드 안전하지 않다(메인 스레드 전용).
    /// </summary>
    public sealed class LruCache<K, V>
    {
        private sealed class Entry
        {
            public K Key;
            public V Value;
            public long Bytes;
            public int Pins;
            public LinkedListNode<Entry> Node;
        }

        private readonly Dictionary<K, Entry> _map;
        private readonly LinkedList<Entry> _order = new LinkedList<Entry>(); // First = 가장 최근, Last = 가장 오래됨
        private readonly int _maxCount;
        private readonly long _maxBytes;
        private readonly Action<K, V> _onEvict;
        private long _bytes;

        /// <param name="maxCount">최대 항목 수(1 이상).</param>
        /// <param name="maxBytes">추정 바이트 상한. 0 이하면 바이트 상한 없음.</param>
        /// <param name="onEvict">내보낼 때(Evict 로 밀려남·Remove·Clear) 호출. null 허용.</param>
        public LruCache(int maxCount, long maxBytes, Action<K, V> onEvict)
        {
            if (maxCount < 1) throw new ArgumentOutOfRangeException("maxCount");
            _maxCount = maxCount;
            _maxBytes = maxBytes;
            _onEvict = onEvict;
            _map = new Dictionary<K, Entry>();
        }

        public int Count { get { return _map.Count; } }
        public long TotalBytes { get { return _bytes; } }
        public int MaxCount { get { return _maxCount; } }
        public long MaxBytes { get { return _maxBytes; } }

        public bool Contains(K key)
        {
            return _map.ContainsKey(key);
        }

        /// <summary>찾으면 가장 최근으로 올리고 true.</summary>
        public bool TryGet(K key, out V value)
        {
            Entry e;
            if (_map.TryGetValue(key, out e))
            {
                Touch(e);
                value = e.Value;
                return true;
            }
            value = default(V);
            return false;
        }

        /// <summary>넣거나 교체한다. 같은 키의 옛 값이 다른 객체면 옛 값에 대해 onEvict 를 부른다. 그 뒤 상한을 맞춘다.</summary>
        public void Put(K key, V value, long bytes)
        {
            if (bytes < 0) bytes = 0;
            Entry e;
            if (_map.TryGetValue(key, out e))
            {
                V old = e.Value;
                _bytes += bytes - e.Bytes;
                e.Value = value;
                e.Bytes = bytes;
                Touch(e);
                if (!object.ReferenceEquals(old, value) && !EqualityComparer<V>.Default.Equals(old, value) && _onEvict != null) _onEvict(key, old);
            }
            else
            {
                e = new Entry();
                e.Key = key;
                e.Value = value;
                e.Bytes = bytes;
                e.Node = _order.AddFirst(e);
                _map[key] = e;
                _bytes += bytes;
            }
            Trim(e);
        }

        /// <summary>고정(참조 +1). 고정된 항목은 내보내지 않는다. 키가 없으면 false.</summary>
        public bool Pin(K key)
        {
            Entry e;
            if (!_map.TryGetValue(key, out e)) return false;
            e.Pins++;
            return true;
        }

        /// <summary>고정 해제(참조 -1, 0 아래로 내려가지 않음). 풀린 뒤 상한을 다시 맞춘다.</summary>
        public bool Unpin(K key)
        {
            Entry e;
            if (!_map.TryGetValue(key, out e)) return false;
            if (e.Pins > 0) e.Pins--;
            Trim(null);
            return true;
        }

        public int PinCount(K key)
        {
            Entry e;
            return _map.TryGetValue(key, out e) ? e.Pins : 0;
        }

        /// <summary>항목을 지운다(고정 여부와 무관). onEvict 를 부른다.</summary>
        public bool Remove(K key)
        {
            Entry e;
            if (!_map.TryGetValue(key, out e)) return false;
            Drop(e);
            return true;
        }

        /// <summary>전부 지운다(고정 포함). 각 항목에 onEvict 를 부른다.</summary>
        public void Clear()
        {
            List<Entry> all = new List<Entry>(_order);
            for (int i = 0; i < all.Count; i++) Drop(all[i]);
        }

        /// <summary>오래된 순(가장 오래된 것이 먼저)의 키 목록. 검사·디버그용.</summary>
        public List<K> KeysOldestFirst()
        {
            List<K> keys = new List<K>();
            for (LinkedListNode<Entry> n = _order.Last; n != null; n = n.Previous) keys.Add(n.Value.Key);
            return keys;
        }

        private void Touch(Entry e)
        {
            if (e.Node.List != null && _order.First != e.Node)
            {
                _order.Remove(e.Node);
                _order.AddFirst(e.Node);
            }
        }

        private bool Over()
        {
            return _map.Count > _maxCount || (_maxBytes > 0 && _bytes > _maxBytes);
        }

        // keep: 방금 넣은 항목(내보내지 않는다). 가장 오래된 쪽에서 고정되지 않은 항목을 찾아 내보낸다.
        private void Trim(Entry keep)
        {
            LinkedListNode<Entry> n = _order.Last;
            while (Over() && n != null)
            {
                LinkedListNode<Entry> prev = n.Previous;
                Entry e = n.Value;
                if (e.Pins == 0 && e != keep) Drop(e);
                n = prev;
            }
        }

        private void Drop(Entry e)
        {
            _map.Remove(e.Key);
            _order.Remove(e.Node);
            _bytes -= e.Bytes;
            if (_onEvict != null) _onEvict(e.Key, e.Value);
        }
    }
}

namespace MorpheHouse.Core
{
    /// <summary>"마지막 실행 이후 최소 간격이 지났는가" 판정(순수). 비싼 호출(Resources.UnloadUnusedAssets 등)을 화면 전환 때만, 일정 간격으로만 부르기 위한 것.</summary>
    public static class Throttle
    {
        public static bool Elapsed(float now, float last, float minInterval)
        {
            return now - last >= minInterval;
        }
    }
}
