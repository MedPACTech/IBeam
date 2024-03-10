using System;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.API.Utilities
{
    public class NamespacedMemoryCache
    {
        private class EntryKey
        {
            private readonly string _prefix;
            private readonly object _key;

            public EntryKey(string prefix, object key)
            {
                _prefix = prefix;
                _key = key;
            }

            public override bool Equals(object obj)
            {
                if (obj == null) return false;
                if (!(obj is EntryKey)) return false;
                var other = (EntryKey) obj;
                return other._prefix == _prefix && 
                       other._key.Equals(_key);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_prefix, _key);
            }
        }
        
        private readonly IMemoryCache _backingCache;
        private readonly string _prefix;

        public NamespacedMemoryCache(IMemoryCache backingCache, string prefix)
        {
            _backingCache = backingCache;
            _prefix = prefix;
        }

        public void Set(object key, object value, TimeSpan expiry)
        {
            _backingCache.Set(new EntryKey(_prefix, key), value, expiry);
        }

        public object Get(object key)
        {
            return _backingCache.Get(new EntryKey(_prefix, key));
        }

        public bool TryGetValue(object key, out object value)
        {
            return _backingCache.TryGetValue(new EntryKey(_prefix, key), out value);
        }
        
        public bool TryGetValue<T>(object key, out T value)
        {
            if (TryGetValue(key, out var result))
            {
                value = result switch
                {
                    T item => item,
                    _ => default
                };
                return true;
            }

            value = default;
            return false;
        }

        public void Remove(object key)
        {
            _backingCache.Remove(new EntryKey(_prefix, key));
        }
    }
}