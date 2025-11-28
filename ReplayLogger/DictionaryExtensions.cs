using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ReplayLogger
{
    public static class DictionaryExtensions
    {
        
        
        
        
        
        
        
        public static Dictionary<GameObject, HealthManager> GetKeysWithUniqueGameObject(this Dictionary<HealthManager, (int maxHP, int lastHP)> dictionary)
        {
            Dictionary<GameObject, HealthManager> lastHealthManagers = new Dictionary<GameObject, HealthManager>();

            foreach (var kvp in dictionary)
            {
                HealthManager hm = kvp.Key;
                if (hm == null) continue;
                GameObject go = hm.gameObject;

                lastHealthManagers[go] = hm;
            }

            return lastHealthManagers;
        }


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
    }
}
