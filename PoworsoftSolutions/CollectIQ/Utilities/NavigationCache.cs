/*
* FILE: NavigationCache.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-29
* DESCRIPTION:
*     Provides a temporary storage mechanism for data passed backward
*     between pages when Shell navigation parameters are not supported.
*/

using System.Collections.Generic;

namespace CollectIQ.Utilities
{
    public static class NavigationCache
    {
        private static readonly Dictionary<string, object> cache = new();

        public static void Set(string key, object value)
        {
            cache[key] = value;
        }

        public static T? Get<T>(string key)
        {
            if (cache.TryGetValue(key, out var value) && value is T typed)
                return typed;
            return default;
        }

        public static void Clear(string key)
        {
            if (cache.ContainsKey(key))
                cache.Remove(key);
        }
    }
}
