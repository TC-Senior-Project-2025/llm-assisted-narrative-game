using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Implements an equality comparer for the <see cref="Color32"/> struct.
    /// </summary>
    /// <remarks>
    /// The <see cref="Color32Equality"/> class enables comparisons of <see cref="Color32"/> objects
    /// by evaluating their individual color channel values (red, green, blue, and alpha).
    /// This class is typically used in scenarios where <see cref="Color32"/> values are used as keys
    /// in collections such as dictionaries or hash sets.
    /// </remarks>
    public sealed class Color32Equality : IEqualityComparer<Color32>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Color32 x, Color32 y) =>
            x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(Color32 c) =>
            c.r | (c.g << 8) | (c.b << 16) | (c.a << 24);
    }
}