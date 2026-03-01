using System.Runtime.CompilerServices;
using UnityEngine;

namespace Extensions
{
    public static class Color32Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SameAs(this in Color32 a, in Color32 b)
        {
            return a.r == b.r &&
                   a.g == b.g &&
                   a.b == b.b &&
                   a.a == b.a;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Pack(this in Color32 c) => (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
    }
}