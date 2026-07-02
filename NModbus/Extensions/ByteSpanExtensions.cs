using System;
using System.Collections.Generic;

namespace NModbus.Extensions
{
    internal static class CollectionExtensions
    {
        /// <summary>Creates a new array containing <paramref name="size"/> elements copied from <paramref name="source"/> starting at <paramref name="startIndex"/>.</summary>
        internal static T[] Slice<T>(this ICollection<T> source, int startIndex, int size)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var result = new T[size];
            if (source is T[] arr)
            {
                // Fast path: direct Array.Copy
                Array.Copy(arr, startIndex, result, 0, size);
            }
            else
            {
                // Slow path: copy via enumerator
                int i = 0;
                foreach (var item in source)
                {
                    if (i >= startIndex + size) break;
                    if (i >= startIndex)
                        result[i - startIndex] = item;
                    i++;
                }
            }
            return result;
        }
    }
}
