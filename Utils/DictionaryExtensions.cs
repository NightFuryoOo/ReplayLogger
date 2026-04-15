using System;
using System.Collections.Generic;

namespace ReplayLogger
{
    public static class DictionaryExtensions
    {
        public static void RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, Predicate<KeyValuePair<TKey, TValue>> match)
        {
            List<TKey> keysToRemove = new List<TKey>();
            foreach (var kvp in dictionary)
            {
                if (match(kvp))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                dictionary.Remove(key);
            }
        }

        public static void ReplaceWith<TKey, TValue>(this Dictionary<TKey, TValue> target, Dictionary<TKey, TValue> source)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            if (source == null || source.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<TKey, TValue> entry in source)
            {
                target[entry.Key] = entry.Value;
            }
        }
    }
}
