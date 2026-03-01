using System;
using System.Collections.Generic;

namespace Extensions
{
    public static class EnumerableExtensions
    {
        public static string AsFormatted<T>(this IEnumerable<T> list)
        {
            return $"[ {string.Join(", ", list)} ]";
        }
    }
}