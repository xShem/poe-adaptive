extern alias corelib;

using System.Buffers;
using System.Runtime.CompilerServices;

namespace SystemExtensions.Streams;

/// <summary>
/// Wrap a <see cref="MemoryStream"/> to implement <see cref="IBufferWriter{T}"/> of <see cref="byte"/>
/// which can be written starting from the current <see cref="MemoryStream.Position"/>.
/// </summary>
/// <remarks>
/// Do not seek/read/write the stream between calls to <see cref="GetMemory"/>/<see cref="GetSpan"/> and <see cref="Advance"/>,
/// otherwise the results will not be as expected.
/// </remarks>
public readonly struct BufferWriterWrapper(MemoryStream baseStream) : IBufferWriter<byte> {
	private readonly corelib::System.IO.MemoryStream baseStream = Unsafe.As<corelib::System.IO.MemoryStream>(baseStream);
	/// <summary>
	/// The underlying <see cref="MemoryStream"/> passed to the constructor.
	/// </summary>
	public MemoryStream BaseStream => Unsafe.As<MemoryStream>(baseStream);

	public readonly void Advance(int count) {
		baseStream.EnsureNotClosed();
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		var pos = unchecked(baseStream._position + count);
		if (pos < 0)
			ThrowHelper.ThrowArgumentOutOfRange(count);
		baseStream._position = pos;
		if (pos > baseStream._length)
			baseStream._length = pos;
	}

	public readonly Memory<byte> GetMemory(int sizeHint = 0) {
		baseStream.EnsureNotClosed();
		if (sizeHint >= 0)
			baseStream.EnsureCapacity(unchecked(baseStream._position + (sizeHint == 0 ? 1 : sizeHint)));
		return baseStream._buffer.AsMemory(baseStream._position);
	}

	public readonly Span<byte> GetSpan(int sizeHint = 0) {
		baseStream.EnsureNotClosed();
		if (sizeHint >= 0)
			baseStream.EnsureCapacity(unchecked(baseStream._position + (sizeHint == 0 ? 1 : sizeHint)));
		return baseStream._buffer.AsSpan(baseStream._position);
	}
}