extern alias corelib;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions.Spans;

namespace SystemExtensions.Collections;
public static class ArrayExtensions {
	public static T[] Add<T>(this T[] array, T item) => Insert(array, array.Length, item);
	public static T[] Insert<T>(this T[] array, int index, T item) {
		var result = GC.AllocateUninitializedArray<T>(array.Length + 1);
		var span = new Span<T>(result); // cache to avoid duplicate type checks
		if (index != 0)
			new ReadOnlySpan<T>(array, 0, index).CopyToUnchecked(ref MemoryMarshal.GetReference(span)); // index check here
		Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)index) = item;
		if (index != array.Length)
			new ReadOnlySpan<T>(array, index, array.Length - index).CopyToUnchecked(
				ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)(index + 1)));
		return result;
	}

	public static T[] RemoveAt<T>(this T[] array, int index) {
		var result = GC.AllocateUninitializedArray<T>(array.Length - 1);
		var span = new Span<T>(result); // cache to avoid duplicate type checks
		if (index != 0)
			new ReadOnlySpan<T>(array, 0, index).CopyToUnchecked(ref MemoryMarshal.GetReference(span)); // index check here
		if (index != result.Length)
			new ReadOnlySpan<T>(array, index + 1, result.Length - index).CopyToUnchecked(
				ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)index));
		return result;
	}

	/// <inheritdoc cref="AsList{T}(T[], int)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static List<T> AsList<T>(this T[] array) => AsList(array, array.Length);
	/// <summary>
	/// Returns a <see cref="List{T}"/> that use <paramref name="array"/> as its base array.
	/// </summary>
	/// <param name="array">Base array of the list</param>
	/// <param name="count">
	/// Element count of the list. (&lt;= <paramref name="array"/>.Length)<br />
	/// Don't store instances in <paramref name="array"/> outside of this range if <typeparamref name="T"/> is a reference type,
	/// otherwise they won't be collected by GC until they are overwritten.
	/// </param>
	/// <remarks>
	/// Note that the base array will be changed when <see cref="List{T}.Capacity"/> is changed.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static List<T> AsList<T>(this T[] array, int count) {
		ArgumentNullException.ThrowIfNull(array);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(unchecked((uint)count), (uint)array.Length, nameof(count));

		var result = Unsafe.As<corelib::System.Collections.Generic.List<T>>(RuntimeHelpers.GetUninitializedObject(typeof(List<T>)));
		result._items = array;
		result._size = count;
		return Unsafe.As<List<T>>(result);
	}

	/// <summary>
	/// Creates a <see cref="MemoryStream"/> that use <paramref name="buffer"/> as its underlying array.
	/// And with an additional parameter <paramref name="expandable"/> than the original constructor: <see cref="MemoryStream(byte[], int, int, bool, bool)"/>.
	/// </summary>
	/// <param name="buffer">The underlying buffer for creating the <see cref="MemoryStream"/>.</param>
	/// <param name="index">
	/// The index of the <paramref name="buffer"/> at which the stream begins.
	/// <para>Must be 0 if <paramref name="expandable"/> is <see langword="true"/> due to the internal implementation.</para>
	/// </param>
	/// <param name="count">The initial length of the stream in bytes</param>
	/// <param name="writable">The setting of the <see cref="MemoryStream.CanWrite"/> property, which determines whether the stream supports writing.</param>
	/// <param name="publiclyVisible">Whether allows the underlying array of the stream to be returned by <see cref="MemoryStream.GetBuffer"/> or <see cref="MemoryStream.TryGetBuffer"/>.</param>
	/// <param name="expandable">Whether the stream can be expanded. That is, whether the <see cref="MemoryStream.Capacity"/> of this stream can be changed.</param>
	/// <returns>The created <see cref="MemoryStream"/></returns>
	public static MemoryStream AsStream(this byte[] buffer, int index = 0, int count = -1, bool writable = true, bool publiclyVisible = false, bool expandable = false) {
		if (count == -1)
			count = unchecked(buffer.Length - index);
		if (expandable && index != 0)
			ThrowHelper.Throw<ArgumentException>("The expandable is true while index != 0", nameof(expandable));
		var ms = new MemoryStream(buffer, index, count, writable, publiclyVisible);
		Unsafe.As<corelib::System.IO.MemoryStream>(ms)._expandable = expandable;
		return ms;
	}
}