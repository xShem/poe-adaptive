namespace SystemExtensions.Tests;
using File = SystemExtensions.System.IO.File;
using System = global::System;

[TestClass]
public class FileTests {
	[TestMethod]
	public void ReadAllBytes_Test() {
		// Arrange
		var content = new byte[513];
		Random.Shared.NextBytes(content);
		var path = Path.GetTempFileName();

		try {
			System.IO.File.WriteAllBytes(path, content);

			var smallBuffer = new byte[16];
			var buffer = new byte[content.Length];

			// Act + Assert
			Assert.IsFalse(File.TryReadAllBytes(path, smallBuffer, out int bytesRead));
			Assert.AreEqual(16, bytesRead);
			Assert.IsTrue(content.AsSpan(0, 16).SequenceEqual(smallBuffer));

			Assert.IsTrue(File.TryReadAllBytes(path, buffer, out bytesRead));
			Assert.AreEqual(content.Length, bytesRead);
			Assert.IsTrue(content.AsSpan().SequenceEqual(buffer));

			Array.Clear(smallBuffer);
			Assert.AreEqual(16, File.ReadAllBytesAsync(path, smallBuffer).AsTask().GetAwaiter().GetResult());
			Assert.IsTrue(content.AsSpan(0, 16).SequenceEqual(smallBuffer));

			Array.Clear(buffer);
			Assert.AreEqual(content.Length, File.ReadAllBytesAsync(path, buffer).AsTask().GetAwaiter().GetResult());
			Assert.IsTrue(content.AsSpan().SequenceEqual(buffer));
		} finally {
			System.IO.File.Delete(path);
		}
	}

	[TestMethod]
	public void WriteAllBytes_Test() {
		// Arrange
		var content = new byte[513];
		Random.Shared.NextBytes(content);
		var path = Path.GetTempFileName();

		try {
			// Act
			File.WriteAllBytes(path, content);

			// Assert
			Assert.IsTrue(content.AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(path)));
		} finally {
			System.IO.File.Delete(path);
		}
	}

	[TestMethod]
	public void WriteAllBytesAsync_Test() {
		// Arrange
		var content = new byte[513];
		Random.Shared.NextBytes(content);
		var path = Path.GetTempFileName();

		try {
			// Act
			File.WriteAllBytesAsync(path, content).AsTask().GetAwaiter().GetResult();

			// Assert
			Assert.IsTrue(content.AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(path)));
		} finally {
			System.IO.File.Delete(path);
		}
	}
}