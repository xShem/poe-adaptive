using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SystemExtensions.Tests;
[TestClass]
public class ValueListTests {
	[TestMethod]
	public void Properties_Test() {
		// Arrange
		int[] array = [1, 2, 3, 4];
		using var list = new ValueList<int>(array, 4);

		// Assert
		Assert.AreEqual(4, list.Count);
		Assert.AreEqual(4, list.Capacity);

		Assert.AreEqual(array[0], list[0]);
		Assert.AreEqual(array[1], list[1]);
		Assert.AreEqual(array[2], list[2]);
		Assert.AreEqual(array[3], list[3]);

		ref var reference = ref MemoryMarshal.GetArrayDataReference(array);
		Assert.IsTrue(Unsafe.AreSame(ref list.GetPinnableReference(), ref reference));
		Assert.IsTrue(Unsafe.AreSame(ref MemoryMarshal.GetReference(list.AsReadOnlySpan()), ref reference));
		Assert.IsTrue(Unsafe.AreSame(ref MemoryMarshal.GetReference(list.AsSpan()), ref reference));

		Assert.IsTrue(list.Contains(3));
		Assert.IsFalse(list.Contains(5));

		var index = Random.Shared.Next(array.Length);
		Assert.AreEqual(Array.IndexOf(array, array[index]), list.IndexOf(array[index]));

		list.Dispose();
		Assert.AreEqual(0, list.Count);
		Assert.AreEqual(0, list.Capacity);
		Assert.IsTrue(Unsafe.IsNullRef(ref list.GetPinnableReference()));
	}

	[TestMethod]
	public void Add_Test() {
		// Arrange
		using var list = new ValueList<int>([0], 1);

		// Act
		list.Add(1);
		list.Add(2);
		list.AddRange([3, 4]);
		list.AddRange(new ReadOnlySequence<int>([5, 6]));
		list.AddRange(collection: [7, 8]);

		// Assert
		Assert.AreEqual(9, list.Count);
		Assert.IsLessThanOrEqualTo(list.Capacity, 9);
		Assert.IsTrue(list.AsReadOnlySpan().SequenceEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]));
	}

	[TestMethod]
	public void Insert_Test() {
		// Arrange
		using var list = new ValueList<int>([1, 2, 3], 3);

		// Act
		list.Insert(0, 4);
		list.InsertRange(2, [5, 6]);
		list.InsertRange(list.Count, new ReadOnlySequence<int>([7, 8]));

		// Assert
		Assert.IsTrue(list.AsReadOnlySpan().SequenceEqual([4, 1, 5, 6, 2, 3, 7, 8]));
	}

	[TestMethod]
	public void Remove_Test() {
		// Arrange
		using var list = new ValueList<int>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 11);

		// Act
		list.Remove(1);
		list.RemoveAt(2);
		list.RemoveRange(3..5);
#pragma warning disable IDE0057
		list.RemoveRange(5);
#pragma warning restore IDE0057

		// Assert
		Assert.IsTrue(list.AsReadOnlySpan().SequenceEqual([2, 3, 5, 8, 9]));
	}

	[TestMethod]
	public void RemoveRanges_Test() {
		// Arrange
		using var list = new ValueList<int>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13], 13);

		// Act
		list.RemoveRanges([0..0, ^12..2, 3..5, 6..^4, 11..]);

		// Assert
		Assert.IsTrue(list.AsReadOnlySpan().SequenceEqual([1, 3, 6, 10, 11]));
	}

	[TestMethod]
	public void Clear_Test() {
		// Arrange
		using var list = new ValueList<int>([1, 2, 3, 4, 5], 5);

		// Act
		list.Clear();

		// Assert
		Assert.AreEqual(0, list.Count);
		try {
			_ = list[0]; // IndexOutOfRangeException
		} catch {
			return;
		}
		Assert.Fail();
	}

	[TestMethod]
	public void BoundsCheck_Test() {
		// Arrange
		using var list = new ValueList<int>([1, 2, 3, 4, 5], 5);

		// Act + Assert
		try {
			_ = list[-1];
		} catch {
			try {
				_ = list.AsSpan(list.Count + 1);
			} catch {
				try {
					list.Insert(list.Count + 1, 6);
				} catch {
					try {
						list.RemoveAt(list.Count);
					} catch {
						return;
					}
				}
			}
		}
		Assert.Fail();
	}

	[TestMethod]
	public void EnsureCapacity_Test() {
		// Arrange
		using var list = new ValueList<int>([1, 2, 3, 4, 5], 5);
		var cap1 = Random.Shared.Next(999999);
		var cap2 = Random.Shared.Next(999999);
		var cap3 = Random.Shared.Next(999999);

		// Act + Assert
		list.EnsureCapacity(cap1);
		Assert.IsLessThanOrEqualTo(list.Capacity, cap1);
		list.EnsureCapacity(cap2);
		Assert.IsTrue(cap1 <= list.Capacity && cap2 <= list.Capacity);
		list.EnsureCapacity(cap3);
		Assert.IsTrue(cap1 <= list.Capacity && cap2 <= list.Capacity && cap3 <= list.Capacity);
	}

	[TestMethod]
	public void PreventCopy_Test() {
		var list = new ValueList<int>(); // original
		try {
			list.EnsureCapacity(1);

			ref var list2 = ref list; // pass by reference
			list2.EnsureCapacity(list2.Capacity + 1);

			var list_copy = list; // copy
			try {
				list_copy.EnsureCapacity(list_copy.Capacity + 1);
			} catch {
				return;
			}
			Assert.Fail();
		} finally {
			list.Dispose();
		}
	}
}