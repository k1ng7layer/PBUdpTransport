using System.Collections.Generic;

namespace PBUdpTransport.Utils
{
    internal class ConcurrentHashSet<T>
    {
        private readonly HashSet<T> _hashSet = new();
        private readonly object _locker = new();

        public int Count
        {
            get
            {
                lock (_locker)
                {
                    return _hashSet.Count;
                }
            }
        }

        public void Add(T item)
        {
            lock (_locker)
            {
                _hashSet.Add(item);
            }
        }

        public void Remove(T item)
        {
            lock (_locker)
            {
                _hashSet.Remove(item);
            }
        }

        public bool Contains(T item)
        {
            lock (_locker)
            {
                return _hashSet.Contains(item);
            }
        }

        public void Clear()
        {
            lock (_locker)
            {
                _hashSet.Clear();
            }
        }
    }
}