using System.Buffers;
using System.Data;

using SystemExtensions.Streams;

namespace SystemExtensions.Tests;
[TestClass]
public class StreamExtensionsTests {
	[TestMethod]
	[DataRow(0)]
	[DataRow(5)]
	[DataRow(99999)]
	public void ReadStringTest(int length) {
		// Arrange
		var stream = new MemoryStream();
		var expected = Random.Shared.NextString(length);
		var bytes = Encoding.Unicode.GetBytes(expected);
		stream.Write(bytes);
		stream.Position = 0;

		// Act
		var result = stream.ReadString(expected.Length);

		// Assert
		Assert.AreEqual(expected, result);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(5)]
	[DataRow(99999)]
	public void WriteStringTest(int length) {
		// Arrange
		var stream = new MemoryStream();
		var expected = Random.Shared.NextString(length);

		// Act
		stream.Write(expected);

		// Assert
		var buffer = new byte[expected.Length * 2];
		stream.Position = 0;
		stream.Read(buffer, 0, buffer.Length);
		var result = Encoding.Unicode.GetString(buffer);
		Assert.AreEqual(expected, result);
	}

	[TestMethod]
	public void ReadWriteStructTest() {
		// Arrange
		var stream = new MemoryStream();
		var expected = TestStruct.CreateRandom();
		stream.Write(expected);
		stream.Write(expected);
		stream.Position = 0;

		// Act
		var result1 = stream.Read<TestStruct>();
		stream.Read(out TestStruct result2);

		// Assert
		Assert.AreEqual(expected, result1);
		Assert.AreEqual(expected, result2);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(5)]
	[DataRow(99999)]
	public void ReadWriteArrayOfStructTest(int length) {
		// Arrange
		var stream = new MemoryStream();
		var expected = Enumerable.Range(0, length).Select(_ => TestStruct.CreateRandom()).ToArray();
		stream.Write(expected);
		stream.Write(expected.AsReadOnlySpan());
		stream.Position = 0;

		// Act
		var result1 = new TestStruct[expected.Length];
		stream.Read(result1);

		var result2 = new TestStruct[expected.Length];
		stream.Read(result2.AsSpan());

		// Assert
		CollectionAssert.AreEqual(expected, result1);
		CollectionAssert.AreEqual(expected, result2);
	}

	[TestMethod]
	[DataRow(0, 0)]
	[DataRow(1, 1)]
	[DataRow(0, 50)]
	[DataRow(25, 100)]
	public void ReadWriteListOfStructTest(int offset, int count) {
		// Arrange
		var stream = new MemoryStream();
		var expected = Enumerable.Range(0, Random.Shared.Next(offset + count, Math.Max(256, offset + count))).Select(_ => TestStruct.CreateRandom()).ToList();
		stream.Write(expected, offset, count);
		expected = expected.GetRange(offset, count); // Trim for comparing

		// Act
		stream.Position = 0;
		var result1 = new List<TestStruct>(count);
		stream.Read(result1, 0, count);

		stream.Position = 0;
		var result2 = new List<TestStruct>(offset + count);
		stream.Read(result2, offset, count);
		result2.RemoveRange(0, offset); // TrimStart for comparing

		// Assert
		CollectionAssert.AreEqual(expected, result1);
		CollectionAssert.AreEqual(expected, result2);
	}

	[TestMethod]
	public void ReadToEndTest() {
		// Arrange
		var stream = new MemoryStream();
		Assert.IsEmpty(stream.ReadToEnd());
		Assert.IsEmpty(stream.ReadToEndAsync().GetAwaiter().GetResult());

		var expected = new byte[Random.Shared.Next(1, 2000000)];
		Random.Shared.NextBytes(expected);
		stream.Write(expected);

		var pos = Random.Shared.Next(0, 2);
		stream.Position = pos;

		// Act
		var result1 = stream.ReadToEnd();
		var result2 = stream.ReadToEndAsync().GetAwaiter().GetResult();

		// Assert
		new ReadOnlySpan<byte>(expected).Slice(pos).SequenceEqual(result1);
		new ReadOnlySpan<byte>(expected).Slice(pos).SequenceEqual(result2);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void BufferWriterWrapper_Test(bool getMemory) {
		// Arrange
		using var ms = new MemoryStream(4);
		var bww = ms.AsIBufferWriter();
		ReadOnlySpan<byte> data = [1, 2, 3];

		// Act + Assert
		data.CopyTo(getMemory ? bww.GetMemory().Span : bww.GetSpan());
		bww.Advance(data.Length);
		Assert.AreEqual(4, ms.Capacity); // not changed
		Assert.AreEqual(data.Length, ms.Length);

		data.CopyTo(getMemory ? bww.GetMemory(5).Span : bww.GetSpan(5));
		Assert.IsLessThanOrEqualTo(ms.Capacity, data.Length + 5); // expanded
		bww.Advance(data.Length);
		Assert.AreEqual(data.Length + data.Length, ms.Length);

		var result = ms.ToArray().AsReadOnlySpan();
		Assert.IsTrue(data.SequenceEqual(result[..data.Length]));
		Assert.IsTrue(data.SequenceEqual(result.Slice(data.Length)));

		ms.SetLength(2);
		ms.Capacity = 2;
		Assert.AreNotEqual(0, (getMemory ? bww.GetMemory().Span : bww.GetSpan()).Length);
		Assert.IsLessThanOrEqualTo(ms.Capacity, 3); // expanded
	}

	[TestMethod]
	public async Task BufferWriterStream_Test() {
		// Arrange
		var abw = new ArrayBufferWriter<byte>();
		var bws = abw.AsStream();
		byte[] data = [1, 2, 3];

		// Act + Assert
		Assert.IsTrue(bws.CanWrite);
		Assert.IsFalse(bws.CanRead);
		Assert.IsFalse(bws.CanSeek);
		Assert.Throws<NotSupportedException>(() => bws.Position = 1);
		Assert.Throws<NotSupportedException>(() => bws.Read([], default, default));
		Assert.Throws<NotSupportedException>(() => bws.Read(default));
		await Assert.ThrowsAsync<NotSupportedException>(() => bws.ReadAsync(default).AsTask());
		await Assert.ThrowsAsync<NotSupportedException>(() => bws.ReadAsync([], default, default, default));
		Assert.Throws<NotSupportedException>(() => bws.BeginRead([], default, default, default, default));
		Assert.Throws<NotSupportedException>(() => bws.Seek(default, default));
		Assert.Throws<NotSupportedException>(() => bws.SetLength(default));

		bws.Write(data, 0, 2);
		Assert.IsTrue(abw.WrittenSpan.SequenceEqual(new(data, 0, 2)));
		abw.ResetWrittenCount();
#pragma warning disable CA1835
		await bws.WriteAsync(data, 0, 2);
#pragma warning restore CA1835
		Assert.IsTrue(abw.WrittenSpan.SequenceEqual(new(data, 0, 2)));
		abw.ResetWrittenCount();

		bws.Write(data);
		Assert.IsTrue(abw.WrittenSpan.SequenceEqual(data));
		abw.ResetWrittenCount();
		await bws.WriteAsync(data);
		Assert.IsTrue(abw.WrittenSpan.SequenceEqual(data));

		bws.WriteByte(7);
		Assert.AreEqual(data.Length + 1, abw.WrittenCount);
		Assert.AreEqual(7, abw.WrittenSpan[data.Length]);
	}
}