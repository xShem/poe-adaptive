using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using SystemExtensions.Tasks;

namespace SystemExtensions.Streams;
/// <summary>
/// Wrap a <see cref="Stream"/> to represent a sub-range of it
/// </summary>
/// <remarks>
/// <para>This class won't limit the length that can be written to the <see cref="BaseStream"/>,
/// so the <see cref="Length"/> may increase after writing or seeking (but not reading).</para>
/// <para>Note that the <see cref="Position"/> is dependent on the one of the <see cref="BaseStream"/>,
/// if it is changed to before <see cref="Offset"/>, the read/write operations of this class may be performed outside the range.</para>
/// <para>Disposing this class will also dispose the underlying <see cref="BaseStream"/>.</para>
/// </remarks>
public class SubStream : Stream, IEquatable<SubStream> {
	private Stream? _baseStream;
	/// <summary>
	/// The underlying <see cref="Stream"/> being wrapped
	/// </summary>
	[MemberNotNull(nameof(_baseStream))]
	public virtual Stream BaseStream {
		get {
			ObjectDisposedException.ThrowIf(_baseStream is null, this);
			return _baseStream;
		}
	}

	/// <summary>
	/// The offset in <see cref="BaseStream"/> at which this <see cref="SubStream"/> begins
	/// </summary>
	public virtual long Offset { get; protected set; }
	/// <summary>
	/// The maximum position in <see cref="BaseStream"/> that can be read from this <see cref="SubStream"/> (exclusive), or -1 if not limited.
	/// <para>Note that this may increase after writing or seeking to <see cref="SubStream"/>.</para>
	/// </summary>
	public virtual long EndOffset { get; protected set; }

	public override long Length => (long)Math.Min(unchecked((ulong)EndOffset), (ulong)BaseStream.Length) - Offset;

	/// <param name="baseStream">The underlying <see cref="Stream"/> to wrap</param>
	/// <param name="offset">
	/// The offset in <paramref name="baseStream"/> at which the <see cref="SubStream"/> begins.
	/// If it is greater than the <see cref="Stream.Position"/> of the <paramref name="baseStream"/>,
	/// this will seek the <paramref name="baseStream"/> to the <paramref name="offset"/>, or throw if not <see cref="Stream.CanSeek"/>.
	/// </param>
	/// <param name="length">
	/// Length of the <see cref="SubStream"/>, or -1 if not limited.
	/// </param>
	public SubStream(Stream baseStream, long offset, long length = -1) {
		ArgumentNullException.ThrowIfNull(baseStream);
		_baseStream = baseStream;
		Rescope(offset, length);
	}

	/// <param name="baseStream">The underlying <see cref="Stream"/> to wrap</param>
	/// <param name="range">
	/// The <see cref="LongRange"/> of this <see cref="SubStream"/> in <paramref name="baseStream"/>.
	/// If the <see cref="LongRange.Start"/> is greater than the <see cref="Stream.Position"/> of the <paramref name="baseStream"/>,
	/// this will seek the <paramref name="baseStream"/> to the <see cref="Range.Start"/>, or throw if not <see cref="Stream.CanSeek"/>.
	/// </param>
	public SubStream(Stream baseStream, LongRange range) {
		ArgumentNullException.ThrowIfNull(baseStream);
		_baseStream = baseStream;
		Rescope(range);
	}

	/// <summary>
	/// Change the scope of this <see cref="SubStream"/> relative to the <see cref="BaseStream"/>, and return self
	/// </summary>
	/// <param name="length">length of the <see cref="SubStream"/>, or -1 if not limited</param>
	/// <remarks>
	/// If the <paramref name="offset"/> is greater than the <see cref="Stream.Position"/> of the <see cref="BaseStream"/>,
	/// this will seek the <see cref="BaseStream"/> to the <paramref name="offset"/>, or throw if not <see cref="Stream.CanSeek"/>.
	/// </remarks>
	public virtual void Rescope(long offset, long length = -1) {
		if (unchecked((ulong)offset) > (ulong)BaseStream.Length)
			ThrowHelper.ThrowArgumentOutOfRange(offset);
		if (length == -1) {
			EndOffset = -1;
		} else {
			EndOffset = checked(offset + length);
			if (length < 0 || EndOffset > _baseStream.Length)
				ThrowHelper.ThrowArgumentOutOfRange(length);
		}
		Offset = offset;

		if (_baseStream.Position < offset)
			_baseStream.Position = offset;
	}

	/// <summary>
	/// Change the scope of this <see cref="SubStream"/> relative to the <see cref="BaseStream"/>, and return self
	/// </summary>
	/// <remarks>
	/// If the <see cref="Range.Start"/> is greater than the <see cref="Stream.Position"/> of the <see cref="BaseStream"/>,
	/// this will seek the <see cref="BaseStream"/> to the <see cref="Range.Start"/>, or throw if not <see cref="Stream.CanSeek"/>.
	/// </remarks>
	/// <returns><see langword="this"/></returns>
	public virtual void Rescope(LongRange range) {
		(Offset, EndOffset) = range.GetOffsetAndEnd(BaseStream.Length);
		if (_baseStream.Position < Offset)
			_baseStream.Position = Offset;
	}

	public override int Read(byte[] buffer, int offset, int count) => _baseStream!.Read(buffer, offset, CheckLengthBeforeRead(count));
	public override int Read(scoped Span<byte> buffer) => _baseStream!.Read(buffer[..CheckLengthBeforeRead(buffer.Length)]);
	public override int ReadByte() => (ulong)BaseStream.Position >= unchecked((ulong)EndOffset) ? -1 : _baseStream.ReadByte();
	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _baseStream!.ReadAsync(buffer, offset, CheckLengthBeforeRead(count), cancellationToken);
	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _baseStream!.ReadAsync(buffer[..CheckLengthBeforeRead(buffer.Length)], cancellationToken);

	public override long Position {
		get => BaseStream.Position - Offset;
		set {
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			var pos = value + Offset;
			BaseStream.Position = pos;
			if (unchecked((ulong)EndOffset) < (ulong)pos)
				EndOffset = pos;
		}
	}
	public override long Seek(long offset, SeekOrigin origin) {
		var pos = BaseStream.Seek(SeekCheckOffset(offset, origin), SeekOrigin.Begin);
		if (unchecked((ulong)EndOffset) < (ulong)pos)
			EndOffset = pos;
		return pos;
	}
	private protected long SeekCheckOffset(long offset, SeekOrigin origin) {
		var pos = origin switch {
			SeekOrigin.Begin => Offset,
			SeekOrigin.Current => BaseStream.Position,
			SeekOrigin.End => EndOffset == -1 ? BaseStream.Length : EndOffset,
			_ => throw ThrowHelper.ArgumentOutOfRange(origin),
		} + offset;
		if (pos < Offset)
			ThrowHelper.ThrowArgumentOutOfRange(offset, "Attempted to seek before the beginning of the stream");
		return pos;
	}

	/// <summary>
	/// Sets the length of the current stream to <paramref name="value"/>,
	/// and sets the length of the <see cref="BaseStream"/> to <see cref="Offset"/> + <paramref name="value"/>.
	/// </summary>
	/// <remarks>
	/// <para>This will overrite the length/<see cref="EndOffset"/> set by constructor or <see cref="Rescope(long, long)"/>.</para>
	/// <para>To avoid affecting the <see cref="BaseStream"/>, use <see cref="Rescope(long, long)"/> instead.</para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="value"/> is negative</exception>
	public override void SetLength(long value) {
		ArgumentOutOfRangeException.ThrowIfNegative(value);
		var length = Offset + value;
		BaseStream.SetLength(length);
		if (EndOffset != -1)
			EndOffset = length;
	}

	public override void Write(byte[] buffer, int offset, int count) {
		BaseStream.Write(buffer, offset, count);
		CheckLengthAfterWrite();
	}
	public override void Write(scoped ReadOnlySpan<byte> buffer) {
		BaseStream.Write(buffer);
		CheckLengthAfterWrite();
	}
	public override void WriteByte(byte value) {
		BaseStream.WriteByte(value);
		CheckLengthAfterWrite();
	}
	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) => BaseStream.WriteAsync(buffer, offset, count, cancellationToken).ContinueWith(CheckLengthAfterWrite, cancellationToken);
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => BaseStream.WriteAsync(buffer, cancellationToken).ContinueWith(action: CheckLengthAfterWrite, cancellationToken);

	public override void CopyTo(Stream destination, int bufferSize) {
		if (unchecked((ulong)EndOffset) >= (ulong)BaseStream.Length)
			_baseStream.CopyTo(destination, bufferSize);
		else
			base.CopyTo(destination, bufferSize);
	}
	public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken = default) {
		if (unchecked((ulong)EndOffset) >= (ulong)BaseStream.Length)
			return _baseStream.CopyToAsync(destination, bufferSize, cancellationToken);
		else
			return base.CopyToAsync(destination, bufferSize, cancellationToken);
	}

	protected override void Dispose(bool disposing) {
		if (disposing && _baseStream is not null) {
			_baseStream.Dispose();
			_baseStream = null;
		}
		base.Dispose(disposing);
	}
#pragma warning disable CA1816
	public override ValueTask DisposeAsync() => _baseStream?.DisposeAsync() ?? default;
#pragma warning restore CA1816

	public ReadOnlySubStream AsReadOnly() => new(BaseStream, Offset, Length);

	#region IEquatable
	public bool Equals(SubStream? other) {
		return GetType() == other?.GetType()
			&& BaseStream == other.BaseStream // Throw if disposed
			&& Offset == other.Offset
			&& EndOffset == other.EndOffset;
	}
	public override bool Equals(object? obj) => Equals(obj as SubStream);
	public override int GetHashCode() {
		return BaseStream.GetHashCode() ^ Utils.CombineHashCode(
			EndOffset.GetHashCode(), Offset.GetHashCode());
	}
	#endregion IEquatable

	#region Bridge only
	public override bool CanRead => _baseStream?.CanRead ?? false;
	public override bool CanSeek => _baseStream?.CanSeek ?? false;
	public override bool CanWrite => _baseStream?.CanWrite ?? false;
	public override bool CanTimeout => _baseStream?.CanTimeout ?? false;
	public override int ReadTimeout { get => BaseStream.ReadTimeout; set => BaseStream.ReadTimeout = value; }
	public override int WriteTimeout { get => BaseStream.WriteTimeout; set => BaseStream.WriteTimeout = value; }
	public override void Flush() => BaseStream.Flush();
	public override Task FlushAsync(CancellationToken cancellationToken = default) => BaseStream.FlushAsync(cancellationToken);
	#endregion Bridge only

	#region Helpers
	/// <summary>
	/// Limit the <paramref name="count"/> to prevent reading beyond <see cref="EndOffset"/>
	/// </summary>
	/// <param name="count">The length the caller wants to read</param>
	/// <returns>The length should be read from the <see cref="BaseStream"/></returns>
	[MemberNotNull(nameof(_baseStream))]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int CheckLengthBeforeRead(int count) {
		if (EndOffset == -1) {
			_ = BaseStream; // dispose check
			return count;
		}
		var remain = EndOffset - BaseStream.Position;
		return remain >= count ? count : (int)remain;
	}
	/// <inheritdoc cref="CheckLengthAfterWrite()"/>
	[MemberNotNull(nameof(_baseStream))]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CheckLengthAfterWrite(object? _) => CheckLengthAfterWrite();
	/// <summary>
	/// Expand <see cref="Length"/> if written beyond it
	/// </summary>
	[MemberNotNull(nameof(_baseStream))]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CheckLengthAfterWrite() {
		if (unchecked((ulong)EndOffset) < (ulong)_baseStream!.Position)
			EndOffset = _baseStream.Position;
	}
	#endregion Helpers
}