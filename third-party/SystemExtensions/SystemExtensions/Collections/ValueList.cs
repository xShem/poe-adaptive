using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions.Spans;

using MemoryExtensions = System.MemoryExtensions;

namespace SystemExtensions.Collections;
/// <summary>
/// <see cref="List{T}"/> without heap allocations
/// </summary>
/// <remarks>
/// Do not make any copies of this struct, pass it by reference instead.<br />
/// If the buffer isn't provided to the constructor, or if the capacity grows after that,
/// make sure to call <see cref="Dispose"/> to return the buffer to the <see cref="ArrayPool{T}"/>
/// </remarks>
public ref struct ValueList<T> {
	public static ValueList<T> Empty => new([]);

	private T[]? rentedBuffer;
	private Span<T> Buffer;
	public int Count { readonly get; private set; }
	private ref T[]? self;

	public readonly int Capacity => Buffer.Length;

	public readonly T this[int index] {
		get => AsReadOnlySpan()[index];
		set => AsSpan()[index] = value;
	}

	/// <summary>
	/// Use <paramref name="initialBuffer"/> as the backing buffer for the list,
	/// and use <see cref="ArrayPool{T}.Shared"/> if more capacity is needed.
	/// </summary>
	/// <param name="initialCount">
	/// The number of items in <paramref name="initialBuffer"/> that are already in use.
	/// </param>
	public ValueList(Span<T> initialBuffer, int initialCount = 0) {
		ArgumentOutOfRangeException.ThrowIfGreaterThan(unchecked((uint)initialCount), (uint)initialBuffer.Length, nameof(initialCount));
		Buffer = initialBuffer;
		Count = initialCount;
		self = ref Unsafe.AsRef(in rentedBuffer);
	}

	/// <param name="initialCapacity">
	/// Specifies that the initial capacity of the list should be at least <paramref name="initialCapacity"/>.
	/// </param>
	public ValueList(int initialCapacity) {
		if (initialCapacity > 0)
			Buffer = rentedBuffer = ArrayPool<T>.Shared.Rent(initialCapacity);
		self = ref Unsafe.AsRef(in rentedBuffer);
	}

	public ValueList() : this(0) { }

	public void Add(T item) {
		EnsureRemainingCapacity(1);
		Unsafe.Add(ref GetPinnableReference(), (nint)(uint)Count) = item;
		++Count;
	}
	public void AddRange(scoped ReadOnlySpan<T> items) {
		EnsureRemainingCapacity(items.Length);
		items.CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)Count));
		Count += items.Length;
	}
	public void AddRange(scoped in ReadOnlySequence<T> items) {
		EnsureRemainingCapacity(checked((int)items.Length));
		foreach (var segment in items) {
			segment.Span.CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)Count));
			Count += segment.Length;
		}
	}
	public void AddRange(IEnumerable<T> collection) {
		if (typeof(T) == typeof(char) && collection is string str) {
			AddRange(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<char, T>(ref Unsafe.AsRef(in str.GetPinnableReference())), str.Length));
			return;
		}
		if (rentedBuffer is not null) {
			if (collection is ICollection<T> c) {
				EnsureRemainingCapacity(c.Count);
				c.CopyTo(rentedBuffer, Count);
				Count += c.Count;
				return;
			}
		} else {
			if (collection is List<T> list) {
				EnsureRemainingCapacity(list.Count);
				list.CopyTo(Buffer.Slice(Count));
				Count += list.Count;
				return;
			}
			if (collection is T[] array) {
				AddRange(new ReadOnlySpan<T>(array));
				return;
			}
		}
		if (collection.TryGetNonEnumeratedCount(out var count))
			EnsureRemainingCapacity(count);

		using var en = collection.GetEnumerator();
		while (en.MoveNext())
			Add(en.Current);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void EnsureRemainingCapacity(int capacity) {
		EnsureCapacity(Count + capacity);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void EnsureCapacity(int capacity) {
		if (capacity > Capacity)
			Grow(capacity);
	}
	[MethodImpl(MethodImplOptions.NoInlining)] // Uncommon path
	private void Grow(int capacity) {
		if (!Unsafe.AreSame(ref self, ref rentedBuffer))
			ThrowHelper.Throw<InvalidOperationException>("A copy of ValueList<T> is detected, this type can only be passed by reference");
		var newBuffer = ArrayPool<T>.Shared.Rent(capacity);
		AsReadOnlySpan().CopyTo(newBuffer);
		Buffer = newBuffer;
		if (rentedBuffer is not null)
			ArrayPool<T>.Shared.Return(rentedBuffer);
		rentedBuffer = newBuffer;
	}

	public readonly ReadOnlySpan<T>.Enumerator GetEnumerator() => AsReadOnlySpan().GetEnumerator();

	public readonly Span<T> AsSpan() => MemoryMarshal.CreateSpan(ref GetPinnableReference(), Count); // Skip bounds check
	public readonly Span<T> AsSpan(int index) => AsSpan().Slice(index);
	public readonly Span<T> AsSpan(int index, int length) => AsSpan().Slice(index, length);
	public readonly ReadOnlySpan<T> AsReadOnlySpan() => MemoryMarshal.CreateReadOnlySpan(ref GetPinnableReference(), Count); // Skip bounds check
	public readonly ReadOnlySpan<T> AsReadOnlySpan(int index) => AsReadOnlySpan().Slice(index);
	public readonly ReadOnlySpan<T> AsReadOnlySpan(int index, int length) => AsReadOnlySpan().Slice(index, length);

	public readonly bool Contains(T item) => Count != 0 && IndexOf(item) >= 0;

	public readonly int IndexOf(T item) => SystemExtensions.System.MemoryExtensions.IndexOf(AsReadOnlySpan(), item);

	public readonly void CopyTo(T[] array, int arrayIndex) => CopyTo(array.AsSpan(arrayIndex));
	public readonly void CopyTo(scoped Span<T> span) => AsReadOnlySpan().CopyTo(span);

	public void Insert(int index, T item) {
		EnsureRemainingCapacity(1);
		AsReadOnlySpan(index).CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)(index + 1)));
		Unsafe.Add(ref GetPinnableReference(), (nint)(uint)index) = item;
		++Count;
	}
	/// <remarks>
	/// The <paramref name="items"/> should not overlap with the backing buffer of this list.
	/// </remarks>
	public void InsertRange(int index, scoped ReadOnlySpan<T> items) {
		EnsureRemainingCapacity(items.Length);
		// There's a rare case that the `items` overlaps with the destination below line copying to,
		// but the memory there is uninitialized and shouldn't be used by the user,
		// so we treat it as an unsafe behavior caused by the user themselves and don't check it.
		var span = AsSpan(index);
		span.CopyToUnchecked(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)items.Length));
		items.CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)index));
		Count += items.Length;
	}
	/// <remarks>
	/// The <paramref name="items"/> should not overlap with the backing buffer of this list.
	/// </remarks>
	public void InsertRange(int index, scoped in ReadOnlySequence<T> items) {
		var len = checked((int)items.Length);
		EnsureRemainingCapacity(len);
		var span = AsSpan(index);
		span.CopyToUnchecked(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)len));
		foreach (var segment in items) {
			segment.Span.CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)index));
			index += segment.Length;
		}
		Count += len;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(T item) {
		var index = IndexOf(item);
		if (index < 0)
			return false;
		RemoveAt(index);
		return true;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RemoveAt(int index) {
		AsReadOnlySpan(index + 1).CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)index));
		--Count;

		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			Unsafe.Add(ref GetPinnableReference(), (nint)(uint)Count) = default!;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RemoveRange(int startIndex) {
		var span = AsSpan(startIndex); // Bounds check here
		Count = startIndex;
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			span.Clear();
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RemoveRange(int startIndex, int length) {
		if (Environment.Is64BitProcess) {
			if ((ulong)(uint)startIndex + (ulong)(uint)length > (ulong)(uint)Count)
				ThrowHelper.Throw<ArgumentOutOfRangeException>();
		} else {
			if ((uint)startIndex > (uint)Count || (uint)length > (uint)(Count - startIndex))
				ThrowHelper.Throw<ArgumentOutOfRangeException>();
		}

		AsReadOnlySpan().SliceUnchecked(startIndex + length).CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)startIndex));
		Count -= length;

		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			Buffer.Slice(Count, length).Clear();
	}
	public void RemoveRange(Range range) {
		var (offset, length) = range.GetOffsetAndLength(Count);
		RemoveRange(offset, length);
	}
	/// <param name="ranges">
	/// Ranges to remove. Must be sorted in ascending order, and must not overlap.
	/// </param>
	/// <remarks>
	/// The <paramref name="ranges"/> must be sorted in ascending order, and must not overlap.
	/// </remarks>
	public void RemoveRanges(IEnumerable<Range> ranges) {
		using var it = ranges.GetEnumerator();
		if (!it.MoveNext())
			return;
		var (pos, lastEnd) = it.Current.GetOffsetAndEnd(Count);
		while (it.MoveNext()) {
			var (offset, end) = it.Current.GetOffsetAndEnd(Count);
			var length = offset - lastEnd;
			if (length < 0)
				ThrowHelper.Throw<ArgumentException>("The provided ranges must be sorted in ascending order, and must not overlap");
			AsReadOnlySpan(lastEnd, length).CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)pos));
			pos += length;
			lastEnd = end;
		}
		AsReadOnlySpan(lastEnd).CopyToUnchecked(ref Unsafe.Add(ref GetPinnableReference(), (nint)(uint)pos));
		pos += Count - lastEnd;
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			AsSpan(pos).Clear();
		Count = pos;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear() {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			AsSpan().Clear();
		Count = 0;
	}

	/// <inheritdoc cref="MemoryExtensions.BinarySearch{T, TComparable}(ReadOnlySpan{T}, TComparable)"/>
	public readonly int BinarySearch<TComparable>(TComparable comparable) where TComparable : IComparable<T> => AsReadOnlySpan().BinarySearch(comparable);
	/// <inheritdoc cref="MemoryExtensions.BinarySearch{T, TComparer}(ReadOnlySpan{T}, TComparer)"/>
	public readonly int BinarySearch<TComparer>(T value, TComparer comparer) where TComparer : IComparer<T> => AsReadOnlySpan().BinarySearch(value, comparer);
	/// <inheritdoc cref="MemoryExtensions.Sort{T}(Span{T})"/>
	public readonly void Sort() => AsSpan().Sort();
	/// <inheritdoc cref="MemoryExtensions.Sort{T, TComparer}(Span{T}, TComparer)"/>
	public readonly void Sort<TComparer>(TComparer comparer) where TComparer : IComparer<T> => AsSpan().Sort(comparer);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose() {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			Buffer.Clear();
		if (rentedBuffer is not null && Unsafe.AreSame(ref self, ref rentedBuffer))
			ArrayPool<T>.Shared.Return(rentedBuffer);
		this = default;
	}

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public readonly ref T GetPinnableReference() => ref MemoryMarshal.GetReference(Buffer);
}