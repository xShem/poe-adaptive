using System.Runtime.CompilerServices;

namespace SystemExtensions.Spans;
/// <summary>
/// Faster implementation of <see cref="SpanHelpersWithoutIEquatable"/> for reference types.
/// </summary>
/// <remarks>
/// All methods here assume the input type T is reference type.
/// Otherwise, they may be slower than <see cref="SpanHelpersWithoutIEquatable"/>.
/// </remarks>
internal static class SpanHelpersForObjects {
	public static int IndexOf<T>(scoped ref T searchSpace, IEquatable<T> value, int length) {
		for (nint index = 0, len = length; index < len; ++index)
			if (value.Equals(Unsafe.Add(ref searchSpace, index)))
				return (int)index;
		return -1;
	}
	public static int IndexOf(scoped ref object? searchSpace, object? value, int length) {
		if (value is not null) {
			for (nint index = 0, len = length; index < len; ++index)
				if (value.Equals(Unsafe.Add(ref searchSpace, index)))
					return (int)index;
		} else {
			for (nint index = 0, len = length; index < len; ++index)
				if (Unsafe.Add(ref searchSpace, index) is null)
					return (int)index;
		}
		return -1;
	}

	public static int LastIndexOf<T>(scoped ref T searchSpace, IEquatable<T> value, int length) {
		for (nint index = length - 1; index >= 0; --index)
			if (value.Equals(Unsafe.Add(ref searchSpace, index)))
				return (int)index;
		return -1;
	}
	public static int LastIndexOf(scoped ref object? searchSpace, object? value, int length) {
		if (value is not null) {
			for (nint index = length - 1; index >= 0; --index)
				if (value.Equals(Unsafe.Add(ref searchSpace, index)))
					return (int)index;
		} else {
			for (nint index = length - 1; index >= 0; --index)
				if (Unsafe.Add(ref searchSpace, index) is null)
					return (int)index;
		}
		return -1;
	}

	public static int IndexOfAny<T>(scoped ref IEquatable<T>? searchSpace, int searchSpaceLength, ref T value, int valueLength) {
		for (nint i = 0, slen = searchSpaceLength, vlen = valueLength; i < slen; ++i) {
			var candidate = Unsafe.Add(ref searchSpace, i);
			if (candidate is not null) {
				for (nint j = 0; j < vlen; j++)
					if (candidate.Equals(Unsafe.Add(ref value, j)))
						return (int)i;
			} else {
				for (nint j = 0; j < vlen; j++)
					if (Unsafe.Add(ref value, j) is null)
						return (int)i;
			}
		}
		return -1; // not found
	}
	public static int IndexOfAny(scoped ref object? searchSpace, int searchSpaceLength, ref object? value, int valueLength) {
		for (nint i = 0, slen = searchSpaceLength, vlen = valueLength; i < slen; ++i) {
			var candidate = Unsafe.Add(ref searchSpace, i);
			if (candidate is not null) {
				for (nint j = 0; j < vlen; ++j)
					if (candidate.Equals(Unsafe.Add(ref value, j)))
						return (int)i;
			} else {
				for (nint j = 0; j < vlen; ++j)
					if (Unsafe.Add(ref value, j) is null)
						return (int)i;
			}
		}
		return -1; // not found
	}

	public static bool SequenceEqual<T>(scoped ref IEquatable<T>? first, ref T second, int length) {
		for (nint index = 0, len = length; index < len; ++index) {
			if (Unsafe.Add(ref first, index) is IEquatable<T> notnull && !notnull.Equals(Unsafe.Add(ref second, index)))
				return false;
			if (Unsafe.Add(ref second, index) is not null) // null != not null
				return false;
		}
		return true;
	}
	public static bool SequenceEqual(scoped ref object? first, ref object? second, int length) {
		for (nint index = 0, len = length; index < len; ++index)
			if (!Equals(Unsafe.Add(ref first, index), Unsafe.Add(ref second, index)))
				return false;
		return true;
	}

	public static void Replace<T>(scoped ref T src, ref T dst, IEquatable<T> oldValue, T newValue, nuint length) {
		for (nuint idx = 0; idx < length; ++idx) {
			T original = Unsafe.Add(ref src, idx);
			Unsafe.Add(ref dst, idx) = oldValue.Equals(original) ? newValue : original;
		}
	}
	public static void Replace(scoped ref object? src, ref object? dst, object? oldValue, object? newValue, nuint length) {
		if (oldValue is not null)
			for (nuint idx = 0; idx < length; ++idx) {
				var original = Unsafe.Add(ref src, idx);
				Unsafe.Add(ref dst, idx) = oldValue.Equals(original) ? newValue : original;
			}
		else
			for (nuint idx = 0; idx < length; ++idx) {
				var original = Unsafe.Add(ref src, idx);
				Unsafe.Add(ref dst, idx) = original is null ? newValue : original;
			}
	}

	public static int Count<T>(scoped ref T current, IEquatable<T> value, int length) {
		int count = 0;
		ref T end = ref Unsafe.Add(ref current, (nint)(uint)length);
		while (Unsafe.IsAddressLessThan(ref current, ref end)) {
			if (value.Equals(current))
				++count;
			current = ref Unsafe.Add(ref current, 1);
		}
		return count;
	}
	public static int Count(scoped ref object? current, object? value, int length) {
		int count = 0;
		ref var end = ref Unsafe.Add(ref current, (nint)(uint)length);
		if (value is not null)
			while (Unsafe.IsAddressLessThan(ref current, ref end)) {
				if (value.Equals(current))
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