using System.Runtime.InteropServices;

namespace SystemExtensions.Tests;
[TestClass]
public class ArrayExtensionsTests {
	private static readonly int[] array = { 1, 2, 3, 4, 5 };

	[TestMethod]
	[DataRow(-1)]
	[DataRow(0)]
	[DataRow(1)]
	[DataRow(3)]
	[DataRow(5)]
	[DataRow(6)]
	public void Insert_Test(int index) {
		const int item = 0;
		if (index < 0 || index > array.Length) {
			Assert.Throws<ArgumentOutOfRangeException>(() => array.Insert(index, item));
			return;
		}

		// Arrange
		var expected = new List<int>(array);
		expected.Insert(index, item);

		// Act
		var result = array.Insert(index, item);

		// Assert
		CollectionAssert.AreEqual(expected, result);
	}

	[TestMethod]
	[DataRow(-1)]
	[DataRow(0)]
	[DataRow(1)]
	[DataRow(3)]
	[DataRow(5)]
	[DataRow(6)]
	public void RemoveAt_Test(int index) {
		if (index < 0 || index >= array.Length) {
			Assert.Throws<ArgumentOutOfRangeException>(() => array.RemoveAt(index));
			return;
		}

		// Act
		var expected = new List<int>(array);
		expected.RemoveAt(index);

		// Act
		var result = array.RemoveAt(index);

		// Assert
		CollectionAssert.AreEqual(expected, result);
	}

	[TestMethod]
	[DataRow(-1)]
	[DataRow(0)]
	[DataRow(5)]
	[DataRow(7)]
	public void AsList_Test(int count) {
		// Act
		if (count < 0 || count > array.Length) {
			Assert.Throws<ArgumentOutOfRangeException>(() => array.AsList(count));
			return;
		}
		var result = array.AsList(count);

		// Assert
		if (count != 0) {
			Assert.IsTrue(CollectionsMarshal.AsSpan(result).Overlaps(new(array, 0, count), out var offset));
			Assert.AreEqual(0, offset);
		} else // count == 0
			Assert.IsEmpty(result);
		Assert.AreEqual(array.Length, result.Capacity);
	}

	[TestMethod]
	public void ByteArrayAsStream_Test() {
		// Arrange
		byte[] buffer = [1, 2, 3];

		// Act
		using var ms = buffer.AsStream(0, -1, false, true, true);

		// Assert
		Assert.AreEqual(buffer.Length, ms.Length);
		Assert.AreEqual(buffer.Length, ms.Capacity);
		Assert.IsFalse(ms.CanWrite);
		Assert.AreSame(buffer, ms.GetBuffer());
		ms.Capacity = 5;
		Assert.AreNotSame(buffer, ms.GetBuffer());
	}
}