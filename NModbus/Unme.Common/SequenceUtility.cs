using System;
using System.Collections.Generic;
using System.Linq;

namespace NModbus.Unme.Common
{
    internal static class SequenceUtility
    {
        public static IEnumerable<T> Slice<T>(this IEnumerable<T> source, int startIndex, int size)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            // Fast path: if source is already an array, use Span.Slice for zero-enumerator allocation
            if (source is T[] array)
            {
                if (startIndex < 0 || array.Length < startIndex)
                {
                    throw new ArgumentOutOfRangeException(nameof(startIndex));
                }

                if (size < 0 || startIndex + size > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(size));
                }

                // Use Array.Copy instead of LINQ Skip/Take to avoid enumerator allocations
                T[] result = new T[size];
                Array.Copy(array, startIndex, result, 0, size);
                return result;
            }

            // Slow path: materialize then slice
            var materialized = source.ToArray();
            int num = materialized.Length;

            if (startIndex < 0 || num < startIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            if (size < 0 || startIndex + size > num)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            T[] sliced = new T[size];
            Array.Copy(materialized, startIndex, sliced, 0, size);
            return sliced;
        }
    }
}
