using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SystemExtensions.Streams;
/// <summary>
/// A <see cref="SubStream"/> that can only be read,
/// and disallow to seek beyond <see cref="SubStream.EndOffset"/>.
/// </summary>
#pragma warning disable CA1067 // base class already overrides it
public class ReadOnlySubStream : SubStream, IEquatable<ReadOnlySubStream> {
#pragma warning restore CA1067
	/// <summary>
	/// See <see cref="SubStream(Stream, long, long)"/>
	/// </summary>
	public ReadOnlySubStream(Stream baseStream, long offset, long length = -1) : base(baseStream, offset, length) { }
	/// <summary>
	/// See <see cref="SubStream(Stream, LongRange)"/>
	/// </summary>
	public ReadOnlySubStream(Stream baseStream, LongRange range) : base(baseStream, range) { }

	#region Block_Writing
	/// <returns><see langword="false"/></returns>
	public override bool CanWrite => false;
	private const string ReadOnlyMessage = "Attempted to write to a ReadOnlySubStream";
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override void SetLength(long value) => throw new NotSupportedException(ReadOnlyMessage);
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(ReadOnlyMessage);
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override void Write(scoped ReadOnlySpan<byte> buffer) => throw new NotSupportedException(ReadOnlyMessage);
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override void WriteByte(byte value) => throw new NotSupportedException(ReadOnlyMessage);
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException(ReadOnlyMessage);
	/// <exception cref="NotSupportedException"/>
	[DoesNotReturn]
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException(ReadOnlyMessage);

	public override long Position {
		set {
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			BaseStream.Position = CheckEnd(value + Offset);
		}
	}
	public override long Seek(long offset, SeekOrigin origin) {
		return BaseStream.Seek(CheckEnd(SeekCheckOffset(offset, origin)), SeekOrigin.Begin);
	}
	private long CheckEnd(long position) {
		if (unchecked((ulong)position > (ulong)EndOffset))
			Throw();
		return position;

		[DoesNotReturn, DebuggerNonUserCode]
		static void Throw() {
			throw new EndOfStreamException("Attempted to seek after the EndOffset of the ReadOnlySubStream");
		}
	}
	#endregion Block_Writing

	#region IEquatable
	public bool Equals(ReadOnlySubStream? other) => Equals(other as SubStream);
	public override int GetHashCode() => ~base.GetHashCode();
	#endregion IEquatable
}