using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SystemExtensions.Tests;
[TestClass]
public class UtilsTests {
	[TestMethod]
	public void FastAllocateString_Test() {
		// Arrange
		int length = Random.Shared.Next(1000000);

		// Act
		var str = Utils.FastAllocateString(length);

		// Assert
		Assert.AreEqual(length, str.Length);
	}

	[TestMethod]
	public void ReverseBytes_Test() {
		unchecked {
			// Arrange
			byte b = (byte)Random.Shared.Next();
			short s = (short)Random.Shared.Next();
			int i = Random.Shared.Next();
			long l = Random.Shared.NextInt64();
			Int128 ll = new((ulong)Random.Shared.NextInt64(), (ulong)Random.Shared.NextInt64());
			KeyValuePair<Vector256<uint>, Vector256<long>> kvp = new();
			Span<KeyValuePair<Vector256<uint>, Vector256<long>>> span = new(ref kvp);
			Random.Shared.NextBytes(MemoryMarshal.AsBytes(span));

			// Snapshots
			var b2 = b;
			var s2 = s;
			var i2 = i;
			var l2 = l;
			var ll2 = ll;
			var kvp2 = kvp;

			// Act
			Utils.ReverseBytes(ref b);
			Utils.ReverseBytes(ref s);
			Utils.ReverseBytes(ref i);
			Utils.ReverseBytes(ref l);
			Utils.ReverseBytes(ref ll);
			Utils.ReverseBytes(ref kvp);

			// Assert
			Assert.AreEqual(BinaryPrimitives.ReverseEndianness(b2), b);
			Assert.AreEqual(BinaryPrimitives.ReverseEndianness(s2), s);
			Assert.AreEqual(BinaryPrimitives.ReverseEndianness(s2), s);
			Assert.AreEqual(BinaryPrimitives.ReverseEndianness(i2), i);
			Assert.AreEqual(BinaryPrimitives.ReverseEndianness(l2), l);
			Assert.AreEqual(BinaryPrimitives.ReverseEndianness(ll2), ll);
			MemoryMarshal.AsBytes(span).Reverse();
			Assert.AreEqual(kvp2, kvp);
		}
	}

	private static readonly Range[] ranges = [default, Range.All, ^0..^0, 40..^41, 5.., (..3)];

	[TestMethod]
	[DataRow(0L)]
	[DataRow(10L)]
	[DataRow(1000L)]
	[DataRow(long.MaxValue)]
	public void GetOffset_LongTest(long length) {
		foreach (var index in (ReadOnlySpan<Index>)[0, 1, ^2, int.MaxValue, ^int.MaxValue]) {
			var expected = index.IsFromEnd ? length - index.Value : index.Value;

			var actual = index.GetOffset(length);

			Assert.AreEqual(expected, actual);
		}
	}

	[TestMethod]
	[DataRow(100)]
	[DataRow(int.MaxValue)]
	public void GetOffsetAndEnd_Test(int length) {
		foreach (var range in ranges) {
			// Arrange
			var (Offset, Length) = range.GetOffsetAndLength(length);

			// Act
			var (Offset2, End) = range.GetOffsetAndEnd(length);

			// Assert
			Assert.AreEqual(Offset, Offset2);
			Assert.AreEqual(Offset + Length, End);
		}
	}

	[TestMethod]
	[DataRow(100L)]
	[DataRow(long.MaxValue)]
	public void GetOffsetAndLength_LongTest(long length) {
		foreach (var range in ranges) {
			// Arrange
			var Offset = range.Start.IsFromEnd ? length - range.Start.Value : range.Start.Value;
			var End = range.End.IsFromEnd ? length - range.End.Value : range.End.Value;

			// Act
			var (Offset2, Length) = range.GetOffsetAndLength(length);

			// Assert
			Assert.AreEqual(Offset, Offset2);
			Assert.AreEqual(End - Offset, Length);
		}
	}

	[TestMethod]
	[DataRow(100L)]
	[DataRow(long.MaxValue)]
	public void GetOffsetAndEnd_LongTest(long length) {
		foreach (var range in ranges) {
			// Arrange
			var Offset = range.Start.IsFromEnd ? length - range.Start.Value : range.Start.Value;
			var End = range.End.IsFromEnd ? length - range.End.Value : range.End.Value;

			// Act
			var (Offset2, End2) = range.GetOffsetAndEnd(length);

			// Assert
			Assert.AreEqual(Offset, Offset2);
			Assert.AreEqual(End, End2);
		}
	}
}