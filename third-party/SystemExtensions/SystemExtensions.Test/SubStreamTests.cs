namespace SystemExtensions.Tests;
[TestClass]
public class SubStreamTests {
	[TestMethod]
	[DataRow(0, 9999)]
	[DataRow(10, 1)]
	[DataRow(9999, 0)]
	[DataRow(1, -1)]
	public void Rescope_Test(long offset, long length) {
		// Arrange
		var testStream = new MemoryStream(new byte[9999]);
		var subStream = new SubStream(testStream, 0);

		// Act + Assert
		subStream.Rescope(offset, length);
		Assert.AreEqual(offset, subStream.Offset);
		if (length == -1) {
			Assert.AreEqual(-1, subStream.EndOffset);
			Assert.AreEqual(testStream.Length - offset, subStream.Length);
		} else {
			Assert.AreEqual(offset + length, subStream.EndOffset);
			Assert.AreEqual(length, subStream.Length);
		}
	}

	[TestMethod]
	[DataRow(0, 5)]
	[DataRow(0, 999999)]
	[DataRow(10, 300)]
	[DataRow(999999, 0)]
	public void Seek_Test(long offset, long length) {
		// Arrange
		var testStream = new MemoryStream(new byte[999999]);
		var subStream = new SubStream(testStream, offset, length);

		// Act + Assert
		subStream.Seek(0, SeekOrigin.Begin);
		Assert.AreEqual(testStream.Position, offset);

		subStream.Seek(0, SeekOrigin.End);
		Assert.AreEqual(testStream.Position, offset + length);

		subStream.Seek(5, SeekOrigin.Begin);
		Assert.AreEqual(testStream.Position, offset + 5);

		subStream.Seek(10, SeekOrigin.Current);
		Assert.AreEqual(testStream.Position, offset + 15);

		Assert.Throws<ArgumentOutOfRangeException>(() => subStream.Seek(-1, SeekOrigin.Begin));

		// Assert ReadOnlySubStream
		var readOnlySubStream = new ReadOnlySubStream(testStream, offset, length);
		Assert.Throws<ArgumentOutOfRangeException>(() => readOnlySubStream.Seek(-1, SeekOrigin.Begin));
		Assert.Throws<EndOfStreamException>(() => readOnlySubStream.Seek(length + 1, SeekOrigin.Begin));
	}

	[TestMethod]
	[DataRow(0, 5)]
	[DataRow(0, 999999)]
	[DataRow(10, 300)]
	[DataRow(999999, 0)]
	public void Read_Test(long offset, long length) {
		// Arrange
		var data = new byte[999999];
		Random.Shared.NextBytes(data);
		var testStream = new MemoryStream(data);
		var subStream = new SubStream(testStream, offset, length);
		var buffer1 = new byte[length];
		var buffer2 = new byte[length];
		var buffer3 = new byte[length];
		var buffer4 = new byte[length];

		// Act
		var read = 0;
		while (read < length)
			read += subStream.Read(buffer1, read, (int)length - read);
		subStream.Position = 0;
		subStream.ReadExactly(buffer2);
		subStream.Position = 0;
		subStream.ReadAsync(buffer3, 0, (int)length).Wait();
		subStream.Position = 0;
		subStream.ReadAsync(buffer4).AsTask().Wait();
		subStream.Position = 0;
		var aByte = subStream.ReadByte();

		// Assert
		var expected = data[(int)offset..(int)(offset + length)];
		CollectionAssert.AreEqual(expected, buffer1);
		CollectionAssert.AreEqual(expected, buffer2);
		CollectionAssert.AreEqual(expected, buffer3);
		CollectionAssert.AreEqual(expected, buffer4);
		if (length > 0)
			Assert.AreEqual(expected[0], aByte);
		else
			Assert.AreEqual(-1, aByte);
	}

	[TestMethod]
	[DataRow(0, 5)]
	[DataRow(0, 999999)]
	[DataRow(10, 300)]
	[DataRow(999999, 0)]
	public void Write_Test(long offset, long length) {
		// Arrange
		var buffer = new byte[999999];
		var testStream = new MemoryStream(buffer);
		var subStream = new SubStream(testStream, offset, length);
		var expected = new byte[length];
		Random.Shared.NextBytes(expected);

		// Act + Assert
		subStream.Write(expected, 0, (int)length);
		CollectionAssert.AreEqual(expected, buffer[(int)offset..(int)(offset + length)]);

		subStream.Position = 0;
		Random.Shared.NextBytes(buffer);
		subStream.Write(expected);
		CollectionAssert.AreEqual(expected, buffer[(int)offset..(int)(offset + length)]);

		subStream.Position = 0;
		Random.Shared.NextBytes(buffer);
		subStream.WriteAsync(expected, 0, (int)length).Wait();
		CollectionAssert.AreEqual(expected, buffer[(int)offset..(int)(offset + length)]);

		subStream.Position = 0;
		Random.Shared.NextBytes(buffer);
		subStream.WriteAsync(expected).AsTask().Wait();
		CollectionAssert.AreEqual(expected, buffer[(int)offset..(int)(offset + length)]);
	}

	[TestMethod]
	[DataRow(0, 5)]
	[DataRow(0, 999999)]
	[DataRow(10, 300)]
	[DataRow(999999, 0)]
	public void CopyTo_Test(long offset, long length) {
		// Arrange
		var data = new byte[999999];
		Random.Shared.NextBytes(data);
		var testStream = new MemoryStream(data);
		var subStream = new SubStream(testStream, offset, length);
		var expected = data[(int)offset..(int)(offset + length)];

		// Act + Assert
		using (var targetStream = new MemoryStream((int)length)) {
			subStream.CopyTo(targetStream);
			CollectionAssert.AreEqual(targetStream.ToArray(), expected);
		}
		subStream.Position = 0;
		using (var targetStream = new MemoryStream((int)length)) {
			subStream.CopyToAsync(targetStream).Wait();
			CollectionAssert.AreEqual(targetStream.ToArray(), expected);
		}
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(10)]
	[DataRow(999999)]
	[DataRow(-1)]
	public void SetLength_Test(long length) {
		// Arrange
		const int offset = 10;
		var testStream = new MemoryStream(100);
		testStream.SetLength(100);
		var subStream = new SubStream(testStream, offset);

		// Act
		if (length < 0) {
			Assert.Throws<ArgumentOutOfRangeException>(() => subStream.SetLength(length));
			return;
		}
		subStream.SetLength(length);

		// Assert
		Assert.AreEqual(length, subStream.Length);
		Assert.AreEqual(offset + length, testStream.Length);
		Assert.Throws<NotSupportedException>(() => new ReadOnlySubStream(testStream, offset).SetLength(length));
	}

	[TestMethod]
	[DataRow(0, 80, 0, true)]
	[DataRow(10, 80, 5, false)]
	[DataRow(10, 0, 15, true)]
	[DataRow(0, 0, 0, false)]
	public void Properties_Test(long offset, long length, long basePosition, bool writable) {
		// Arrange
		var testStream = new MemoryStream(new byte[100], writable) {
			Position = basePosition
		};
		var subStream = new SubStream(testStream, offset, length);

		// Assert
		Assert.AreEqual(offset, subStream.Offset);
		Assert.AreEqual(length, subStream.Length);
		Assert.AreEqual(offset + length, subStream.EndOffset);
		Assert.AreEqual(Math.Max(offset, basePosition), testStream.Position);
		Assert.AreEqual(testStream.CanRead, subStream.CanRead);
		Assert.AreEqual(testStream.CanSeek, subStream.CanSeek);
		Assert.AreEqual(writable, subStream.CanWrite);
		Assert.AreEqual(testStream.CanTimeout, subStream.CanTimeout);
		Assert.IsFalse(new ReadOnlySubStream(testStream, offset, length).CanWrite);

		subStream.Dispose();
		Assert.IsFalse(subStream.CanRead);
		Assert.IsFalse(testStream.CanRead);
		Assert.Throws<ObjectDisposedException>(() => subStream.BaseStream);
	}
}