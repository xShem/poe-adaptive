namespace SystemExtensions.Tests;
[TestClass]
public class UnseekableSubStreamTests {
	[TestMethod]
	[DataRow(1)]
	[DataRow(999999)]
	[DataRow(0)]
	[DataRow(-1)]
	public void Read_Test(long length) {
		// Arrange
		var data = new byte[999999];
		Random.Shared.NextBytes(data);
		var testStream = new MemoryStream(data);
		var subStream = new UnseekableSubStream(testStream, length);
		var expected = data[..(length == -1 ? data.Length : (int)length)];

		// Act
		var result = subStream.ReadToEnd();

		// Assert
		Assert.AreEqual(expected.Length, subStream.Position);
		CollectionAssert.AreEqual(expected, result);
		Assert.AreEqual(-1, subStream.ReadByte());
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(999999)]
	[DataRow(0)]
	[DataRow(-1)]
	public void Write_Test(long length) {
		// Arrange
		var data = new byte[999999];
		Random.Shared.NextBytes(data);
		var testStream = new MemoryStream();
		var subStream = new UnseekableSubStream(testStream, length);
		var expected = data[..(length == -1 ? data.Length : (int)length)];

		// Act
		if (length != -1 && length < data.Length) {
			Assert.Throws<EndOfStreamException>(() => subStream.Write(data));
			return;
		}
		subStream.Write(data);
		testStream.Position = 0;
		var result = testStream.ReadToEnd();

		// Assert
		Assert.AreEqual(expected.Length, subStream.Length);
		Assert.AreEqual(expected.Length, subStream.Position);
		CollectionAssert.AreEqual(expected, result);
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(999999)]
	[DataRow(0)]
	[DataRow(-1)]
	public void CopyTo_Test(long length) {
		// Arrange
		var data = new byte[999999];
		Random.Shared.NextBytes(data);
		var testStream = new MemoryStream(data);
		var subStream = new UnseekableSubStream(testStream, length);
		var expected = data[..(length == -1 ? data.Length : (int)length)];

		// Act + Assert
		using (var targetStream = new MemoryStream(expected.Length)) {
			subStream.CopyTo(targetStream);
			CollectionAssert.AreEqual(targetStream.ToArray(), expected);
		}
		subStream = new UnseekableSubStream(testStream, length);
		testStream.Position = 0;
		using (var targetStream = new MemoryStream(expected.Length)) {
			subStream.CopyToAsync(targetStream).GetAwaiter().GetResult();
			CollectionAssert.AreEqual(targetStream.ToArray(), expected);
		}
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(1)]
	[DataRow(9999)]
	[DataRow(-1)]
	public void Properties_Test(long length) {
		// Arrange
		var testStream = new MemoryStream(new byte[9999]);
		var subStream = new UnseekableSubStream(testStream, length);

		// Assert
		Assert.AreEqual(length == -1 ? testStream.Length : length, subStream.Length);
		Assert.AreEqual(0, subStream.Position);
		Assert.AreEqual(testStream.CanRead, subStream.CanRead);
		Assert.AreEqual(testStream.CanWrite, subStream.CanWrite);
		Assert.IsFalse(subStream.CanSeek);
		Assert.AreEqual(testStream.CanTimeout, subStream.CanTimeout);
		Assert.Throws<NotSupportedException>(() => subStream.Position = default);
		Assert.Throws<NotSupportedException>(() => subStream.Seek(default, default));
		Assert.Throws<NotSupportedException>(() => subStream.SetLength(default));

		subStream.Dispose();
		Assert.IsFalse(subStream.CanRead);
		Assert.IsFalse(testStream.CanRead);
		Assert.Throws<ObjectDisposedException>(() => subStream.Write([]));
	}
}