using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace ReplayLogger
{
    internal static class TempObjectPools
    {
        private static readonly SimpleObjectPool<StringBuilder> StringBuilderPool =
            new(() => new StringBuilder(1024), static builder => builder.Clear(), maxRetained: 16);

        private static readonly SimpleObjectPool<List<string>> StringListPool =
            new(() => new List<string>(512), static list => list.Clear(), maxRetained: 32);

        private static readonly SimpleObjectPool<Dictionary<string, List<string>>> LogGroupPool =
            new(() => new Dictionary<string, List<string>>(StringComparer.Ordinal), static map => map.Clear(), maxRetained: 8);

        private static readonly SimpleObjectPool<List<string>> GroupListPool =
            new(() => new List<string>(8), static list => list.Clear(), maxRetained: 64);

        internal static StringBuilder RentStringBuilder(int minCapacity = 0)
        {
            StringBuilder builder = StringBuilderPool.Rent();
            if (minCapacity > 0 && builder.Capacity < minCapacity)
            {
                builder.EnsureCapacity(minCapacity);
            }

            return builder;
        }

        internal static void ReturnStringBuilder(StringBuilder builder)
        {
            StringBuilderPool.Return(builder);
        }

        internal static List<string> RentStringList(int minCapacity = 0)
        {
            List<string> list = StringListPool.Rent();
            if (minCapacity > 0 && list.Capacity < minCapacity)
            {
                list.Capacity = minCapacity;
            }

            return list;
        }

        internal static void ReturnStringList(List<string> list)
        {
            StringListPool.Return(list);
        }

        internal static Dictionary<string, List<string>> RentLogGroupMap()
        {
            return LogGroupPool.Rent();
        }

        internal static void ReturnLogGroupMap(Dictionary<string, List<string>> map, bool returnNestedLists)
        {
            if (map == null)
            {
                return;
            }

            if (returnNestedLists)
            {
                foreach (KeyValuePair<string, List<string>> pair in map)
                {
                    GroupListPool.Return(pair.Value);
                }
            }

            LogGroupPool.Return(map);
        }

        internal static List<string> RentGroupList()
        {
            return GroupListPool.Rent();
        }

        private sealed class SimpleObjectPool<T> where T : class
        {
            private readonly ConcurrentBag<T> bag = new();
            private readonly Func<T> factory;
            private readonly Action<T> reset;
            private readonly int maxRetained;
            private int retainedCount;

            internal SimpleObjectPool(Func<T> factory, Action<T> reset, int maxRetained)
            {
                this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
                this.reset = reset;
                this.maxRetained = Math.Max(1, maxRetained);
            }

            internal T Rent()
            {
                if (bag.TryTake(out T value))
                {
                    System.Threading.Interlocked.Decrement(ref retainedCount);
                    return value;
                }

                return factory();
            }

            internal void Return(T value)
            {
                if (value == null)
                {
                    return;
                }

                reset?.Invoke(value);

                int newCount = System.Threading.Interlocked.Increment(ref retainedCount);
                if (newCount > maxRetained)
                {
                    System.Threading.Interlocked.Decrement(ref retainedCount);
                    return;
                }

                bag.Add(value);
            }
        }
    }
}
