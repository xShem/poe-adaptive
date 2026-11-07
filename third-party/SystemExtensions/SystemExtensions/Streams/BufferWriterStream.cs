using System.Buffers;

namespace SystemExtensions.Streams;

/// <summary>
/// A write-only <see cref="Stream"/> that wrap the <paramref name="writer"/>.
/// </summary>
/// <remarks>
/// <see cref="Stream.Dispose()"/> of this class is no action.
/// </remarks>
public class BufferWriterStream(IBufferWriter<byte> writer) : Stream, IBufferWriter<byte> {
	public void Advance(int count) => writer.Advance(count);
	public Memory<byte> GetMemory(int sizeHint = 0) => writer.GetMemory(sizeHint);
	public Span<byte> GetSpan(int sizeHint = 0) => writer.GetSpan(sizeHint);

	public override void Flush() { }
	public override Task FlushAsync(CancellationToken cancellationToken) {
		if (cancellationToken.IsCancellationRequested)
			return Task.FromCanceled(cancellationToken);
		return Task.CompletedTask;
	}
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => Write(new(buffer, offset, count));
	public override void Write(ReadOnlySpan<byte> buffer) {
		buffer.CopyTo(writer.GetSpan(buffer.Length));
		writer.Advance(buffer.Length);
	}
	public override void WriteByte(byte value) {
		writer.GetSpan(sizeof(byte))[0] = value;
		writer.Advance(sizeof(byte));
	}
	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
		if (cancellationToken.IsCancellationRequested)
			return Task.FromCanceled(cancellationToken);
		try {
			Write(buffer, offset, count);
		} catch (Exception ex) {
			return Task.FromException(ex);
		}
		return Task.CompletedTask;
	}
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		if (cancellationToken.IsCancellationRequested)
			return ValueTask.FromCanceled(cancellationToken);
		try {
			Write(buffer.Span);
		} catch (Exception ex) {
			return ValueTask.FromException(ex);
		}
		return ValueTask.CompletedTask;
	}

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => writer is ArrayBufferWriter<byte> abw ? abw.WrittenCount : throw new NotSupportedException();
	public override long Position {
		get => Length;
		set => throw new NotSupportedException();
	}
}