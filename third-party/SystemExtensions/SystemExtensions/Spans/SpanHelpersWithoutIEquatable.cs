using System.Runtime.CompilerServices;

namespace SystemExtensions.Spans;
/// <summary>
/// Copied from System.SpanHelpers with minor changes and without <see cref="IEquatable{T}"/> limitation.
/// </summary>
/// <remarks>
/// Licensed to the .NET Foundation under the MIT license.
/// </remarks>
/// <seealso href="https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.T.cs"/>
internal static class SpanHelpersWithoutIEquatable {
	public static int IndexOf<T>(scoped ref T searchSpace, T value, int length) {
		if (!typeof(T).IsValueType) { // Faster for reference types
			if (typeof(T).IsAssignableTo(typeof(IEquatable<T>)) && value is IEquatable<T> ieqv /*null check*/)
				return SpanHelpersForObjects.IndexOf(ref searchSpace, ieqv, length);
			else
				return SpanHelpersForObjects.IndexOf(ref Unsafe.As<T, object?>(ref searchSpace), value, length);
		}

		nint index = 0; // Use nint for arithmetic to avoid unnecessary 64->32->64 truncations
		if (value is not null) {
			while (length >= 8) {
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index)))
					goto Found;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 1)))
					goto Found1;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 2)))
					goto Found2;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 3)))
					goto Found3;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 4)))
					goto Found4;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 5)))
					goto Found5;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 6)))
					goto Found6;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 7)))
					goto Found7;
				length -= 8;
				index += 8;
			}
			if (length >= 4) {
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index)))
					goto Found;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 1)))
					goto Found1;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 2)))
					goto Found2;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index + 3)))
					goto Found3;
				length -= 4;
				index += 4;
			}
			while (length > 0) {
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, index)))
					goto Found;
				--length;
				++index;
			}
		} else {
			for (nint len = length; index < len; ++index)
				if (Unsafe.Add(ref searchSpace, index) is null)
					goto Found;
		}
		return -1;
	Found: // Workaround for https://github.com/dotnet/runtime/issues/8795
		return (int)index;
	Found1:
		return (int)(index + 1);
	Found2:
		return (int)(index + 2);
	Found3:
		return (int)(index + 3);
	Found4:
		return (int)(index + 4);
	Found5:
		return (int)(index + 5);
	Found6:
		return (int)(index + 6);
	Found7:
		return (int)(index + 7);
	}
	public static int IndexOf<T>(scoped ref T searchSpace, int searchSpaceLength, scoped ref T value, int valueLength) {
		if (valueLength == 0)
			return 0;  // A zero-length sequence is always treated as "found" at the start of the search space.

		T valueHead = value;
		ref T valueTail = ref Unsafe.Add(ref value, 1);
		int valueTailLength = valueLength - 1;

		nint index = 0;
		while (true) {
			int remainingSearchSpaceLength = searchSpaceLength - (int)index - valueTailLength;
			if (remainingSearchSpaceLength <= 0)
				break;  // The unsearched portion is now shorter than the sequence we're looking for. So it can't be there.

			// Do a quick search for the first element of "value".
			int relativeIndex = IndexOf(ref Unsafe.Add(ref searchSpace, index), valueHead, remainingSearchSpaceLength);
			if (relativeIndex < 0)
				break;
			index += (nint)(uint)relativeIndex;

			// Found the first element of "value". See if the tail matches.
			if (SequenceEqual(ref Unsafe.Add(ref searchSpace, index + 1), ref valueTail, valueTailLength))
				return (int)index;  // The tail matched. Return a successful find.
			++index;
		}
		return -1;
	}
	public static int IndexOfAny<T>(scoped ref T searchSpace, int searchSpaceLength, scoped ref T value, int valueLength) {
		if (valueLength == 0)
			return -1;  // A zero-length set of values is always treated as "not found".

		if (!typeof(T).IsValueType) { // Faster for reference types
			if (typeof(T).IsAssignableTo(typeof(IEquatable<T>)))
				return SpanHelpersForObjects.IndexOfAny(ref Unsafe.As<T, IEquatable<T>?>(ref searchSpace), searchSpaceLength, ref value, valueLength);
			else
				return SpanHelpersForObjects.IndexOfAny(ref Unsafe.As<T, object?>(ref searchSpace), searchSpaceLength, ref Unsafe.As<T, object?>(ref value), valueLength);
		}

		// For the following paragraph, let:
		//   n := length of haystack
		//   i := index of first occurrence of any needle within haystack
		//   l := length of needle array
		//
		// We use a naive non-vectorized search because we want to bound the complexity of IndexOfAny
		// to O(i * l) rather than O(n * l), or just O(n * l) if no needle is found. The reason for
		// this is that it's common for callers to invoke IndexOfAny immediately before slicing,
		// and when this is called in a loop, we want the entire loop to be bounded by O(n * l)
		// rather than O(n^2 * l).

		for (nint i = 0, slen = searchSpaceLength, vlen = valueLength; i < slen; ++i) {
			if (Unsafe.Add(ref searchSpace, i) is T candidate /*not null*/) {
				for (nint j = 0; j < vlen; ++j)
					if (EqualityComparer<T>.Default.Equals(candidate, Unsafe.Add(ref value, j)))
						return (int)i;
			} else {
				for (nint j = 0; j < vlen; ++j)
					if (Unsafe.Add(ref value, j) is null)
						return (int)i;
			}
		}
		return -1; // not found
	}
	public static int LastIndexOf<T>(scoped ref T searchSpace, T value, int length) {
		if (!typeof(T).IsValueType) { // Faster for reference types
			if (typeof(T).IsAssignableTo(typeof(IEquatable<T>)) && value is IEquatable<T> ieqv /*null check*/)
				return SpanHelpersForObjects.LastIndexOf(ref searchSpace, ieqv, length);
			else
				return SpanHelpersForObjects.LastIndexOf(ref Unsafe.As<T, object?>(ref searchSpace), value, length);
		}

		nint len = (nint)(uint)length;
		if (value is not null) {
			while (len >= 8) {
				len -= 8;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 7)))
					goto Found7;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 6)))
					goto Found6;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 5)))
					goto Found5;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 4)))
					goto Found4;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 3)))
					goto Found3;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 2)))
					goto Found2;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 1)))
					goto Found1;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len)))
					goto Found;
			}

			if (len >= 4) {
				len -= 4;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 3)))
					goto Found3;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 2)))
					goto Found2;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len + 1)))
					goto Found1;
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len)))
					goto Found;
			}

			while (len-- > 0)
				if (EqualityComparer<T>.Default.Equals(value, Unsafe.Add(ref searchSpace, len)))
					goto Found;
		} else {
			for (--len; len >= 0; --len)
				if (Unsafe.Add(ref searchSpace, len) is null)
					goto Found;
		}

		return -1;
	Found: // Workaround for https://github.com/dotnet/runtime/issues/8795
		return (int)len;
	Found1:
		return (int)(len + 1);
	Found2:
		return (int)(len + 2);
	Found3:
		return (int)(len + 3);
	Found4:
		return (int)(len + 4);
	Found5:
		return (int)(len + 5);
	Found6:
		return (int)(len + 6);
	Found7:
		return (int)(len + 7);
	}
	public static int LastIndexOf<T>(scoped ref T searchSpace, int searchSpaceLength, scoped ref T value, int valueLength) {
		if (valueLength == 0)
			return searchSpaceLength;  // A zero-length sequence is always treated as "found" at the end of the search space.

		int valueTailLength = valueLength - 1;
		if (valueTailLength == 0)
			return LastIndexOf(ref searchSpace, value, searchSpaceLength);

		int index = 0;

		T valueHead = value;
		ref T valueTail = ref Unsafe.Add(ref value, 1);

		while (true) {
			int remainingSearchSpaceLength = searchSpaceLength - index - valueTailLength;
			if (remainingSearchSpaceLength <= 0)
				break;  // The unsearched portion is now shorter than the sequence we're looking for. So it can't be there.

			// Do a quick search for the first element of "value".
			int relativeIndex = LastIndexOf(ref searchSpace, valueHead, remainingSearchSpaceLength);
			if (relativeIndex < 0)
				break;

			// Found the first element of "value". See if the tail matches.
			if (SequenceEqual(ref Unsafe.Add(ref searchSpace, relativeIndex + 1), ref valueTail, valueTailLength))
				return relativeIndex;  // The tail matched. Return a successful find.

			index += remainingSearchSpaceLength - relativeIndex;
		}
		return -1;
	}
	public static void Replace<T>(scoped ref T src, scoped ref T dst, T oldValue, T newValue, nuint length) {
		if (!typeof(T).IsValueType) { // Faster for reference types
			if (typeof(T).IsAssignableTo(typeof(IEquatable<T>)) && oldValue is IEquatable<T> ieqOldValue /*null check*/)
				SpanHelpersForObjects.Replace(ref src, ref dst, ieqOldValue, newValue, length);
			else
				SpanHelpersForObjects.Replace(ref Unsafe.As<T, object?>(ref src), ref Unsafe.As<T, object?>(ref dst), oldValue, newValue, length);
		} else {
			if (oldValue is not null)
				for (nuint idx = 0; idx < length; ++idx) {
					T original = Unsafe.Add(ref src, idx);
					Unsafe.Add(ref dst, idx) = EqualityComparer<T>.Default.Equals(oldValue, original) ? newValue : original;
				}
			else
				for (nuint idx = 0; idx < length; ++idx) {
					T original = Unsafe.Add(ref src, idx);
					Unsafe.Add(ref dst, idx) = original is null ? newValue : original;
				}
		}
	}
	public static bool SequenceEqual<T>(scoped ref T first, scoped ref T second, int length) {
		if (Unsafe.AreSame(ref first, ref second))
			goto Equal;

		if (!typeof(T).IsValueType) { // Faster for reference types
			if (typeof(T).IsAssignableTo(typeof(IEquatable<T>)))
				return SpanHelpersForObjects.SequenceEqual(ref Unsafe.As<T, IEquatable<T>?>(ref first), ref second, length);
			else
				return SpanHelpersForObjects.SequenceEqual(ref Unsafe.As<T, object?>(ref first), ref Unsafe.As<T, object?>(ref second), length);
		}

		nint index = 0; // Use nint for arithmetic to avoid unnecessary 64->32->64 truncations
		T lookUp0;
		T lookUp1;
		while (length >= 8) {
			lookUp0 = Unsafe.Add(ref first, index);
			lookUp1 = Unsafe.Add(ref second, index);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 1);
			lookUp1 = Unsafe.Add(ref second, index + 1);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 2);
			lookUp1 = Unsafe.Add(ref second, index + 2);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 3);
			lookUp1 = Unsafe.Add(ref second, index + 3);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 4);
			lookUp1 = Unsafe.Add(ref second, index + 4);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 5);
			lookUp1 = Unsafe.Add(ref second, index + 5);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 6);
			lookUp1 = Unsafe.Add(ref second, index + 6);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 7);
			lookUp1 = Unsafe.Add(ref second, index + 7);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			length -= 8;
			index += 8;
		}

		if (length >= 4) {
			lookUp0 = Unsafe.Add(ref first, index);
			lookUp1 = Unsafe.Add(ref second, index);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 1);
			lookUp1 = Unsafe.Add(ref second, index + 1);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 2);
			lookUp1 = Unsafe.Add(ref second, index + 2);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			lookUp0 = Unsafe.Add(ref first, index + 3);
			lookUp1 = Unsafe.Add(ref second, index + 3);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			length -= 4;
			index += 4;
		}

		while (length > 0) {
			lookUp0 = Unsafe.Add(ref first, index);
			lookUp1 = Unsafe.Add(ref second, index);
			if (!EqualityComparer<T>.Default.Equals(lookUp0, lookUp1))
				goto NotEqual;
			++index;
			--length;
		}

	Equal:
		return true;
	NotEqual: // Workaround for https://github.com/dotnet/runtime/issues/8795
		return false;
	}
	public static int Count<T>(scoped ref T current, T value, int length) {
		if (!typeof(T).IsValueType) { // Faster for reference types
			if (typeof(T).IsAssignableTo(typeof(IEquatable<T>)) && value is IEquatable<T> ieqv /*null check*/)
				return SpanHelpersForObjects.Count(ref current, ieqv, length);
			else
				return SpanHelpersForObjects.Count(ref Unsafe.As<T, object?>(ref current), value, length);
		}

		int count = 0;
		ref T end = ref Unsafe.Add(ref current, (nint)(uint)length);
		if (value is not null)
			while (Unsafe.IsAddressLessThan(ref current, ref end)) {
				if (EqualityComparer<T>.Default.Equals(value, current))
					++count;
				current = ref Unsafe.Add(ref current, 1);
			}
		else
			while (Unsafe.IsAddressLessThan(ref current, ref end)) {
				if (current is null)
					++count;
				current = ref Unsafe.Add(ref current, 1);
			}
		return count;
	}
}