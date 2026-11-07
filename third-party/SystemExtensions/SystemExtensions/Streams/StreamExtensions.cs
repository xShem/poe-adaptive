using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions.Collections;
using SystemExtensions.Spans;
using SystemExtensions.Tasks;

namespace SystemExtensions.Streams;
/// <summary>
/// High performance methods for read/write structs/arrays from/to <see cref="Stream"/>
/// </summary>
/// <remarks>
/// All methods here do not process endianness, and are not thread-safe.
/// </remarks>
public static class StreamExtensions {
	/// <summary>
	/// Read an UTF-16 <see cref="string"/> with the length of <paramref name="charCount"/> from the <paramref name="stream"/>
	/// </summary>
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string ReadString(this Stream stream, int charCount) {
		var result = Utils.FastAllocateString(charCount);
		Read(stream, result.AsSpan().AsWritable());
		return result;
	}
	/// <exception cref="EndOfStreamException"/>
	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Read<T>(this Stream stream) where T : unmanaged {
		Read(stream, out T value);
		return value;
	}
	/// <exception cref="EndOfStreamException"/>
	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Read<T>(this Stream stream, out T value) where T : unmanaged {
		Unsafe.SkipInit(out value);
		stream.ReadExactly(value.AsSpan());
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Read<T>(this Stream stream, T[]? array) where T : unmanaged { // T is not byte
		Read(stream, array.AsSpan());
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Read<T>(this Stream stream, scoped Span<T> span) where T : unmanaged { // T is not byte
		stream.ReadExactly(MemoryMarshal.AsBytes(span));
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Read<T>(this Stream stream, List<T> list, int offset, int length) where T : unmanaged {
		var end = unchecked(offset + length);
		if ((uint)offset > (uint)end) {
			ArgumentOutOfRangeException.ThrowIfNegative(offset);
			ThrowHelper.ThrowArgumentOutOfRange(length);
		}
		if ((uint)list.Count < (uint)end)
			CollectionsMarshal.SetCount(list, end); // throw if end < 0
		Read(stream, CollectionsMarshal.AsSpan(list).SliceUnchecked(offset, length));
	}

	public static byte[] ReadToEnd(this Stream stream) {
		if (!stream.CanSeek)
			return ReadToEndUnseekable();
		var buffer = GC.AllocateUninitializedArray<byte>(checked((int)(stream.Length - stream.Position)));
		stream.ReadExactly(buffer);
		return buffer;

		byte[] ReadToEndUnseekable() {
			using var renter = new ArrayPoolRenter<byte>(4096);
			var buffer = renter.Array;
			var l = 0;
			int lastRead;
			do {
				if (l == buffer.Length)
					buffer = renter.Resize(buffer.Length + 1, buffer.Length);
				lastRead = stream.Read(buffer, l, buffer.Length - l);
				l += lastRead;
			} while (lastRead > 0);
			return buffer[..l];
		}
	}
	public static Task<byte[]> ReadToEndAsync(this Stream stream) {
		if (!stream.CanSeek)
			return ReadToEndUnseekableAsync();
		var buffer = GC.AllocateUninitializedArray<byte>(checked((int)(stream.Length - stream.Position)));
		return stream.ReadExactlyAsync(buffer).ContinueWith(_ => buffer).AsTask();

		async Task<byte[]> ReadToEndUnseekableAsync() {
			using var renter = new ArrayPoolRenter<byte>(4096);
			var memory = new Memory<byte>(renter.Array);
			int lastRead;
			do {
				if (memory.IsEmpty) {
					var length = renter.Array.Length;
					memory = new Memory<byte>(renter.Resize(length + 1, length)).Slice(length);
				}
				lastRead = await stream.ReadAsync(memory).ConfigureAwait(false);
				memory = memory.Slice(lastRead);
			} while (lastRead > 0);
			return renter.Array[..(renter.Array.Length - memory.Length)];
		}
	}

	/// <summary>
	/// Write <paramref name="str"/> as an UTF-16 string to the <paramref name="stream"/>
	/// </summary>
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write(this Stream stream, string? str) {
		Write(stream, str.AsSpan());
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Write<T>(this Stream stream, scoped in T value) where T : unmanaged {
		stream.Write(SpanExtensions.AsReadOnlySpan(in value));
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Write<T>(this Stream stream, T[]? array) where T : unmanaged { // T is not byte
		Write(stream, array.AsReadOnlySpan());
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Write<T>(this Stream stream, scoped ReadOnlySpan<T> span) where T : unmanaged { // T is not byte
		stream.Write(MemoryMarshal.AsBytes(span));
	}
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write<T>(this Stream stream, List<T>? list, int offset, int length) where T : unmanaged {
		Write(stream, CollectionsMarshal.AsSpan(list).AsReadOnly().Slice(offset, length));
	}

	/// <summary>
	/// Write <paramref name="str"/> as an UTF-16 string to the <paramref name="writer"/>
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write(this IBufferWriter<byte> writer, string? str) {
		Write(writer, str.AsSpan());
	}
	/// <exception cref="ArgumentOutOfRangeException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write<T>(this IBufferWriter<byte> writer, scoped in T value) where T : unmanaged {
		BuffersExtensions.Write(writer, SpanExtensions.AsReadOnlySpan(in value));
	}
	/// <exception cref="ArgumentOutOfRangeException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write<T>(this IBufferWriter<byte> writer, T[]? array) where T : unmanaged { // T is not byte
		Write(writer, array.AsReadOnlySpan());
	}
	/// <exception cref="ArgumentOutOfRangeException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write<T>(this IBufferWriter<byte> writer, scoped ReadOnlySpan<T> span) where T : unmanaged { // T is not byte
		BuffersExtensions.Write(writer, MemoryMarshal.AsBytes(span));
	}
	/// <exception cref="ArgumentOutOfRangeException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write<T>(this IBufferWriter<byte> writer, List<T>? list, int offset, int length) where T : unmanaged {
		Write(writer, CollectionsMarshal.AsSpan(list).AsReadOnly().Slice(offset, length));
	}

	/// <summary>
	/// See <see cref="SubStream"/>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static SubStream Slice(this Stream stream, long offset, long length = -1) => new(stream, offset, length);
	/// <summary>
	/// See <see cref="SubStream"/>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static SubStream Slice(this Stream stream, LongRange range) => new(stream, range);
	/// <summary>
	/// See <see cref="BufferWriterWrapper"/>
	/// </summary>
	/// <returns><see cref="IBufferWriter{T}"/> of <see cref="byte"/></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static BufferWriterWrapper AsIBufferWriter(this MemoryStream stream) => new(stream);
	/// <summary>
	/// See <see cref="BufferWriterStream(IBufferWriter{byte})"/>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static BufferWriterStream AsStream(this IBufferWriter<byte> writer) => new(writer);
}