using System.Runtime.InteropServices;

namespace SystemExtensions.Tests;

[TestClass]
public class CollectionExtensionsTests {
	private static readonly int[] enumerable = [1, 2, 3, 4, 5];
	[TestMethod]
	public void IndexOf_Test() {
		// Act + Assert
		for (var i = 0; i < enumerable.Length; ++i)
			Assert.AreEqual(i, Collections.CollectionExtensions.IndexOf(enumerable, enumerable[i]));
	}

	[TestMethod]
	public unsafe void ListAsSpanAsMemory_Test() {
		// Arrange
		var list = new List<int>(enumerable);

		// Act
		var span = list.AsSpan();
		var memory = list.AsMemory();

		// Assert
		Assert.AreEqual(list.Count, span.Length);
		Assert.AreEqual(list.Count, memory.Length);
		fixed (int* expected = CollectionsMarshal.AsSpan(list), actual1 = span) {
			var actual2 = (int*)memory.Pin().Pointer;
			Assert.IsTrue(expected == actual1);
			Assert.IsTrue(expected == actual2);
		}
	}

	[TestMethod]
	public void SingleEnumerable_Test() {
		// Arrange
		const int expected1 = 42;
		const string expected2 = "HelloWorld('print')";
		var expected3 = new[] { 1, 2, 3 };

		// Act
		var singleValue = new SingleEnumerable<int>(expected1);
		var singleString = new SingleEnumerable<string>(expected2);
		var singleClass = new SingleEnumerable<int[]>(expected3);

		// Assert
		DoAssert(singleValue, expected1);
		DoAssert(singleString, expected2);
		DoAssert(singleClass, expected3);

		static void DoAssert<T>(IEnumerable<T> enumerable, T expected) {
			// IEnumerable
			Assert.AreEqual(1, enumerable.Count());
			Assert.AreEqual(expected, enumerable.First());

			// IEnumerator
			using var itr = enumerable.GetEnumerator();
			Assert.IsTrue(itr.MoveNext());
			Assert.AreEqual(expected, itr.Current);
			Assert.IsFalse(itr.MoveNext());
			itr.Reset();
			Assert.IsTrue(itr.MoveNext());
			Assert.AreEqual(expected, itr.Current);
			Assert.IsFalse(itr.MoveNext());
		}
	}
}
