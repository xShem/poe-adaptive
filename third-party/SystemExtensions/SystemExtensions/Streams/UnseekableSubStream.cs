using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SystemExtensions.Streams;
/// <summary>
/// Wrap a <see cref="Stream"/> to limit the bytes that can be read or written to it.
/// </summary>
/// <param name="baseStream">
/// The underlying <see cref="Stream"/> to wrap, can be unseekable.
/// </param>
/// <param name="length">
/// Size in bytes this instance can read/write from/to the <paramref name="baseStream"/>, or -1 if not limited.
/// </param>
/// <remarks>
/// This class has its own <see cref="Position"/>, and ignore the <paramref name="baseStream"/>'s one.
/// So any read/write/seek to the <paramref name="baseStream"/> may cause unexpected behavior on this instance.
/// <para>Disposing this class will also dispose the underlying <paramref name="baseStream"/>.</para>
/// </remarks>
public class UnseekableSubStream(Stream baseStream, long length = -1) : Stream {
	protected readonly Stream baseStream = baseStream ?? throw ThrowHelper.Create<ArgumentNullException>(nameof(baseStream));
	protected readonly long length = CheckLength(length);
	protected long position;
	private static long CheckLength(long length) {
		if (length != -1)
			ArgumentOutOfRangeException.ThrowIfNegative(length);
		return length;
	}

#pragma warning disable CS1734
	/// <summary>
	/// Always returns the <paramref name="length"/> passed to the constructor, or the <see cref="Stream.Length"/> of the <paramref name="baseStream"/> if <paramref name="length"/> is -1.
	/// </summary>
#pragma warning restore CS1734
	public override long Length {
		get => length == -1 ? baseStream.Length : length;
	}
	/// <summary>
	/// Total size in bytes of data has been read/written from/to the <see cref="baseStream"/>.
	/// </summary>
	/// <remarks>
	/// Setter always throws <see cref="NotSupportedException"/>
	/// </remarks>
	public override long Position {
		get => position;
		[DoesNotReturn]
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) =>
		IncreasePositionAfterRead(baseStream.Read(buffer, offset, CheckLengthBeforeRead(count)));
	public override int Read(scoped Span<byte> buffer) =>
		IncreasePositionAfterRead(baseStream.Read(buffer[..CheckLengthBeforeRead(buffer.Length)]));
	public override int ReadByte() {
		if ((ulong)position >= unchecked((ulong)length))
			return -1;
		var result = baseStream.ReadByte();
		if (result != -1)
			++position;
		return result;
	}
	public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
#pragma warning disable CA1835
		IncreasePositionAfterRead(await baseStream.ReadAsync(buffer, offset, CheckLengthBeforeRead(count), cancellationToken).ConfigureAwait(false));
#pragma warning restore CA1835
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		IncreasePositionAfterRead(await baseStream.ReadAsync(buffer[..CheckLengthBeforeRead(buffer.Length)], cancellationToken).ConfigureAwait(false));

	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) {
		CheckLengthBeforeWrite(count);
		baseStream.Write(buffer, offset, count);
		position += count;
	}
	public override void Write(scoped ReadOnlySpan<byte> buffer) {
		CheckLengthBeforeWrite(buffer.Length);
		baseStream.Write(buffer);
		position += buffer.Length;
	}
	public override void WriteByte(byte value) {
		CheckLengthBeforeWrite(1);
		baseStream.WriteByte(value);
		++position;
	}
	public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
		CheckLengthBeforeWrite(count);
#pragma warning disable CA1835
		await baseStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA1835
		position += count;
	}
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		CheckLengthBeforeWrite(buffer.Length);
		await baseStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
		position += buffer.Length;
	}

	public override void CopyTo(Stream destination, int bufferSize) {
		if (length == -1/* || length >= baseStream.Length (May throws when baseStream is unseekable) */)
			baseStream.CopyTo(destination, bufferSize);
		else
			base.CopyTo(destination, bufferSize);
	}
	public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken = default) {
		if (length == -1/* || length >= baseStream.Length (May throws when baseStream is unseekable) */)
			return baseStream.CopyToAsync(destination, bufferSize, cancellationToken);
		else
			return base.CopyToAsync(destination, bufferSize, cancellationToken);
	}

	protected override void Dispose(bool disposing) {
		if (disposing)
			baseStream.Dispose();
		base.Dispose(disposing);
	}
#pragma warning disable CA1816
	public override ValueTask DisposeAsync() => baseStream.DisposeAsync();
#pragma warning restore CA1816

	/// <returns><see langword="false"/></returns>
	public override bool CanSeek => false;
	#region Bridge only
	public override bool CanRead => baseStream.CanRead;
	public override bool CanWrite => baseStream.CanWrite;
	public override bool CanTimeout => baseStream.CanTimeout;
	public override int ReadTimeout { get => baseStream.ReadTimeout; set => baseStream.ReadTimeout = value; }
	public override int WriteTimeout { get => baseStream.WriteTimeout; set => baseStream.WriteTimeout = value; }
	public override void Flush() => baseStream.Flush();
	public override Task FlushAsync(CancellationToken cancellationToken = default) => baseStream.FlushAsync(cancellationToken);
	#endregion Bridge only

	#region Helpers
	/// <summary>
	/// Limit the <paramref name="count"/> to prevent reading beyond <see cref="Length"/>
	/// </summary>
	/// <param name="count">The length the caller wants to read</param>
	/// <returns>The length should be read from the <see cref="baseStream"/></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int CheckLengthBeforeRead(int count) {
		if (length == -1)
			return count;
		var remain = length - position;
		return remain >= count ? count : (int)remain;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int IncreasePositionAfterRead(int count) {
		position += count;
		return count;
	}
	/// <summary>
	/// Check the <paramref name="count"/> to prevent writing beyond <see cref="Length"/>
	/// </summary>
	/// <param name="count">The length the caller wants to write</param>
	/// <exception cref="EndOfStreamException"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CheckLengthBeforeWrite(int count) {
		if (length != -1 && position + count > length)
			ThrowHelper.Throw<EndOfStreamException>();
	}
	#endregion Helpers
}