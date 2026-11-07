using System.IO.Compression;
using System.Net;

namespace SystemExtensions.Net.Http;

public sealed class CompressedContent : HttpContent {
	private readonly HttpContent content;
	private readonly DecompressionMethods compressionType;
	private readonly CompressionLevel compressionLevel;
	private readonly bool leaveOpen;

	public CompressedContent(HttpContent content, DecompressionMethods compressionType, CompressionLevel compressionLevel = CompressionLevel.Optimal, bool leaveOpen = false) {
		ArgumentNullException.ThrowIfNull(content);
		if (unchecked((uint)compressionLevel) > (uint)CompressionLevel.SmallestSize)
			ThrowHelper.ThrowArgumentOutOfRange(compressionLevel);

		switch (compressionType) {
			case DecompressionMethods.None:
				break;
			case DecompressionMethods.Brotli:
				Headers.ContentEncoding.Add("br");
				break;
			case DecompressionMethods.Deflate:
				Headers.ContentEncoding.Add("deflate");
				break;
			case DecompressionMethods.GZip:
				Headers.ContentEncoding.Add("gzip");
				break;
			default:
				throw ThrowHelper.ArgumentOutOfRange(compressionType);
		}

		this.content = content;
		this.compressionType = compressionType;
		this.compressionLevel = compressionLevel;
		this.leaveOpen = leaveOpen;
		foreach (var (key, values) in content.Headers)
			Headers.Add(key, values);
	}

	protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) {
		Stream? compressedStream = compressionType switch {
			DecompressionMethods.Brotli => new BrotliStream(stream, compressionLevel, true),
			DecompressionMethods.Deflate => new ZLibStream(stream, compressionLevel, true), // See: https://github.com/dotnet/runtime/blob/b42426ca625424cc959956bec8171740021b6804/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/DecompressionHandler.cs#L231
			DecompressionMethods.GZip => new GZipStream(stream, compressionLevel, true),
			_ => null
		};
		using (compressedStream)
			content.CopyTo(compressedStream ?? stream, context, cancellationToken);
	}

	protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) {
		Stream? compressedStream = compressionType switch {
			DecompressionMethods.Brotli => new BrotliStream(stream, compressionLevel, true),
			DecompressionMethods.Deflate => new ZLibStream(stream, compressionLevel, true), // See: https://github.com/dotnet/runtime/blob/b42426ca625424cc959956bec8171740021b6804/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/DecompressionHandler.cs#L231
			DecompressionMethods.GZip => new GZipStream(stream, compressionLevel, true),
			_ => null
		};
		if (compressedStream is null)
			await content.CopyToAsync(stream, context, cancellationToken).ConfigureAwait(false);
		else
			await using (compressedStream.ConfigureAwait(false))
				await content.CopyToAsync(compressedStream, context, cancellationToken).ConfigureAwait(false);
	}
	protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
		SerializeToStreamAsync(stream, context, CancellationToken.None);

	protected override bool TryComputeLength(out long length) {
		length = 0;
		return false;
	}

	protected override void Dispose(bool disposing) {
		if (!leaveOpen)
			content.Dispose();
		base.Dispose(disposing);
	}
}