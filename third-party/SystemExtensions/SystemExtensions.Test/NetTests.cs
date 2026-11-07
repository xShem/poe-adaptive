using System.IO.Compression;
using System.Net;

using SystemExtensions.Net.Http;

namespace SystemExtensions.Tests;

[TestClass]
public class NetTests {
	[TestMethod]
	public void CompressedContentTest() {
		// Arrange
		var data = GC.AllocateUninitializedArray<byte>(Random.Shared.Next(2000000));
		Random.Shared.NextBytes(data);

		// Act + Assert
		foreach (var type in (ReadOnlySpan<DecompressionMethods>)[
			DecompressionMethods.None,
			DecompressionMethods.Brotli,
			DecompressionMethods.Deflate,
			DecompressionMethods.GZip]) {
			var compressed = new CompressedContent(new ByteArrayContent(data), type);
			using var ms = new MemoryStream();
			compressed.CopyTo(ms, null, default);
			ms.Position = 0;
			using Stream decompressed = type switch {
				DecompressionMethods.Brotli => new BrotliStream(ms, CompressionMode.Decompress),
				DecompressionMethods.Deflate => new ZLibStream(ms, CompressionMode.Decompress),
				DecompressionMethods.GZip => new GZipStream(ms, CompressionMode.Decompress),
				_ => ms
			};

			var result = decompressed.ReadToEnd();
			CollectionAssert.AreEqual(data, result, "DecompressionMethods." + type.ToString());
		}
	}
}