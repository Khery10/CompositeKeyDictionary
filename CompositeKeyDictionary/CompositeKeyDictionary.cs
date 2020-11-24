using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CompositeKeyDictionary
{
    public class CompositeKeyDictionary<TKeyA, TKeyB, TValue> : IDictionary<CompositeKey<TKeyA, TKeyB>, TValue>, ICollection<KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>>
    {
        public CompositeKeyDictionary() : this(0, EqualityComparer<CompositeKey<TKeyA, TKeyB>>.Default) { }

        public CompositeKeyDictionary(int capacity) : this(capacity, EqualityComparer<CompositeKey<TKeyA, TKeyB>>.Default) { }

        public CompositeKeyDictionary(int capacity, IEqualityComparer<CompositeKey<TKeyA, TKeyB>> comparer)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));

            Initialize(capacity);
        }

        // Кэш индексов значений по каждому элементу составного ключа.
        // Кэшируем индексы, чтобы избежать излишнее потребление памяти.
        private readonly Dictionary<TKeyA, List<int>> _firstKeyCache = new Dictionary<TKeyA, List<int>>();
        private readonly Dictionary<TKeyB, List<int>> _secondKeyCache = new Dictionary<TKeyB, List<int>>();

        private readonly IEqualityComparer<CompositeKey<TKeyA, TKeyB>> _comparer;

        private int[] _buckets;
        private Entry[] _entries;
        private int _count;
        private int _freeCount;
        private int _freeList;

        public TValue this[CompositeKey<TKeyA, TKeyB> key]
        {
            get
            {
                if (!TryGetValue(key, out TValue value))
                    throw new KeyNotFoundException();

                return value;
            }
            set => Insert(key, value, false);
        }

        public ICollection<CompositeKey<TKeyA, TKeyB>> Keys
        {
            get
            {
                List<CompositeKey<TKeyA, TKeyB>> keys = new List<CompositeKey<TKeyA, TKeyB>>(Count);

                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].hashCode >= 0)
                    {
                        keys.Add(_entries[i].key);
                    }
                }
                
                return keys;
            }
        }

        public ICollection<TValue> Values
        {
            get
            {
                List<TValue> values = new List<TValue>(Count);

                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].hashCode >= 0)
                    {
                        values.Add(_entries[i].value);
                    }
                }

                return values;
            }
        }

        public int Count => _count - _freeCount;

        public bool IsReadOnly => false;

        public void Add(CompositeKey<TKeyA, TKeyB> key, TValue value)
        {
            Insert(key, value, true);
        }

        public void Add(TKeyA keyA, TKeyB keyB, TValue value)
        {
            Add(new CompositeKey<TKeyA, TKeyB>(keyA, keyB), value);
        }

        public void Clear()
        {
            if (_count == 0)
                return;

            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = -1;
            }

            Array.Clear(_entries, 0, _count);
            _count = 0;
            _freeCount = 0;
        }

        public bool ContainsKey(CompositeKey<TKeyA, TKeyB> key)
        {
            if (key.KeyA == null || key.KeyB == null)
                throw new ArgumentNullException("Any subkey is null");

            return FindEntry(key) >= 0;
        }


        public bool Remove(CompositeKey<TKeyA, TKeyB> key)
        {
            if (key.KeyA == null || key.KeyB == null)
                throw new ArgumentNullException("Any subkey is null");

            int keyHash = _comparer.GetHashCode(key) & int.MaxValue;
            int bucket = keyHash % _buckets.Length;

            int previousIndex = -1;
            for (int i = _buckets[bucket]; i >= 0; i = _entries[i].next)
            {
                if (_entries[i].hashCode == keyHash && _comparer.Equals(_entries[i].key, key))
                {
                    if (previousIndex < 0)
                    {
                        _buckets[bucket] = _entries[i].next;
                    }
                    else
                    {
                        _entries[previousIndex].next = _entries[i].next;
                    }
                    _entries[i].hashCode = -1;
                    _entries[i].next = _freeList;
                    _entries[i].key = default;
                    _entries[i].value = default;
                    _freeList = i;
                    _freeCount++;

                    return true;
                }
                previousIndex = i;
            }

            return false;
        }

        public bool TryGetValue(CompositeKey<TKeyA, TKeyB> key, out TValue value)
        {
            int entryIndex = FindEntry(key);
            if (entryIndex >= 0)
            {
                value = _entries[entryIndex].value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Быстрый поиск всех значений по первому элементу составного ключа.
        /// </summary>
        public bool TryGetFirstKeyValues(TKeyA subKey, out IEnumerable<TValue> values)
        {
            if (!_firstKeyCache.TryGetValue(subKey, out List<int> indexes))
            {
                values = null;
                return false;
            }

            var subKeyComparer = EqualityComparer<TKeyA>.Default;
            values = new SubKeyValueCollection(this, indexes, (key) => subKeyComparer.Equals(subKey, key.KeyA));
            return true;
        }

       
        /// <summary>
        /// Быстрый поиск всех значений по второму элементу составного ключа.
        /// </summary>
        public bool TryGetSecondKeyValues(TKeyB subKey, out IEnumerable<TValue> values)
        {
            if (!_secondKeyCache.TryGetValue(subKey, out List<int> indexes))
            {
                values = null;
                return false;
            }

            var subKeyComparer = EqualityComparer<TKeyB>.Default;
            values = new SubKeyValueCollection(this, indexes, key => subKeyComparer.Equals(subKey, key.KeyB));
            return true;
        }

       
        #region IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].hashCode >= 0)
                {
                    var kvp = new KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>(_entries[i].key, _entries[i].value);
                    yield return kvp;
                }
            }
        }
        #endregion

        #region ICollection
        bool ICollection<KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>>.Remove(KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>>.CopyTo(KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>>.Contains(KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue>>.Add(KeyValuePair<CompositeKey<TKeyA, TKeyB>, TValue> item)
        {
            Add(item.Key, item.Value);
        }
        #endregion

        private void Insert(CompositeKey<TKeyA, TKeyB> key, TValue value, bool add)
        {
            if (key.KeyA == null || key.KeyB == null)
                throw new ArgumentNullException("Any key is null");

            int index;

            int hash = _comparer.GetHashCode(key) & int.MaxValue;
            int bucket = hash % _buckets.Length;

            //проверяем, есть ли в словаре значение по такому же ключу
            for (int i = _buckets[bucket]; i >= 0; i = _entries[i].next)
            {
                if (_entries[i].hashCode == hash && _comparer.Equals(_entries[i].key, key))
                {
                    if (add)
                        throw new ArgumentException("Key has already been added.");

                    _entries[i].value = value;
                    return;
                }
            }

            if (_freeCount > 0)
            {
                index = _freeList;
                _freeList = _entries[index].next;
                _freeCount--;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Resize();
                    bucket = hash % _buckets.Length;
                }

                index = _count++;
            }

            _entries[index].hashCode = hash;
            _entries[index].next = _buckets[bucket];
            _entries[index].key = key;
            _entries[index].value = value;
            _buckets[bucket] = index;

            //добавляем индекс в кэш для быстрого поиска по составляющим ключа
            AddCacheValue(_firstKeyCache, key.KeyA, index);
            AddCacheValue(_secondKeyCache, key.KeyB, index);
        }

        private void AddCacheValue<T>(Dictionary<T, List<int>> cache, T subKey, int entryIndex)
        {
            if (!cache.TryGetValue(subKey, out List<int> indexes))
            {
                indexes = new List<int>();
                cache.Add(subKey, indexes);
            }

            indexes.Add(entryIndex);
        }

        private void Initialize(int capacity)
        {
            int primeSize = HashHelpers.GetPrime(capacity);
            _buckets = new int[primeSize];
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = -1;
            }

            _entries = new Entry[primeSize];
        }

        private void Resize()
        {
            int newSize = HashHelpers.ExpandPrime(_count);
            int[] newBuckets = new int[newSize];

            for (int i = 0; i < newBuckets.Length; i++)
                newBuckets[i] = -1;

            Entry[] newEntries = new Entry[newSize];
            Array.Copy(_entries, 0, newEntries, 0, _count);

            for (int i = 0; i < _count; i++)
            {
                if (newEntries[i].hashCode >= 0)
                {
                    int bucketIndex = newEntries[i].hashCode % newSize;
                    newEntries[i].next = newBuckets[bucketIndex];
                    newBuckets[bucketIndex] = i;
                }
            }

            _buckets = newBuckets;
            _entries = newEntries;
        }

        private int FindEntry(CompositeKey<TKeyA, TKeyB> key)
        {
            int keyHash = _comparer.GetHashCode(key) & int.MaxValue;

            for (int i = _buckets[keyHash % _buckets.Length]; i >= 0; i = _entries[i].next)
            {
                if (_entries[i].hashCode == keyHash && _comparer.Equals(key, _entries[i].key))
                {
                    return i;
                }
            }

            return -1;
        }

        private struct Entry
        {
            public CompositeKey<TKeyA, TKeyB> key;
            public TValue value;
            public int next;
            public int hashCode;
        }

      

        internal class SubKeyValueCollection : IEnumerable<TValue>
        {
            private readonly CompositeKeyDictionary<TKeyA, TKeyB, TValue> dictionary;
            private IList<int> indexes;
            private Func<CompositeKey<TKeyA, TKeyB>, bool> keyPredicate;

            internal SubKeyValueCollection(CompositeKeyDictionary<TKeyA, TKeyB, TValue> dict, IList<int> indexes, Func<CompositeKey<TKeyA, TKeyB>, bool> keyPredicate)
            {
                dictionary = dict;
                this.indexes = indexes;
                this.keyPredicate = keyPredicate;
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                //проходимся по всем индексам и берем по ним значения
                for (int i = 0; i < indexes.Count; i++)
                {
                    for (int entryIndex = indexes[i]; entryIndex >= 0; entryIndex = dictionary._entries[entryIndex].next)
                    {
                        if (dictionary._entries[entryIndex].hashCode > 0 && keyPredicate(dictionary._entries[entryIndex].key))
                        {
                            yield return dictionary._entries[entryIndex].value;
                        }
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

    }

    public struct CompositeKey<TKeyA, TKeyB>
    {
        public CompositeKey(TKeyA keyA, TKeyB keyB)
        {
            KeyA = keyA;
            KeyB = keyB;
        }

        public TKeyA KeyA { get; set; }

        public TKeyB KeyB { get; set; }

        public override bool Equals(object obj)
        {
            return obj is CompositeKey<TKeyA, TKeyB> key &&
                   EqualityComparer<TKeyA>.Default.Equals(KeyA, key.KeyA) &&
                   EqualityComparer<TKeyB>.Default.Equals(KeyB, key.KeyB);
        }

        public override int GetHashCode()
        {
            int hashCode = 185775513;
            hashCode = hashCode * -1521134295 + EqualityComparer<TKeyA>.Default.GetHashCode(KeyA);
            hashCode = hashCode * -1521134295 + EqualityComparer<TKeyB>.Default.GetHashCode(KeyB);
            return hashCode;
        }
    }
}
