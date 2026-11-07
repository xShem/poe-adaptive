using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SystemExtensions;

/// <summary>
/// Represent a range has start and end indexes.
/// <para><see cref="Range"/> for <see cref="LongIndex"/></para>
/// </summary>
/// <param name="start">Represent the inclusive start <see cref="LongIndex"/> of the <see cref="LongRange"/>.</param>
/// <param name="end">Represent the exclusive end <see cref="LongIndex"/> of the <see cref="LongRange"/>.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct LongRange(LongIndex start, LongIndex end) : IEquatable<LongRange>, IEquatable<Range> {
	/// <summary>Represent the inclusive start <see cref="LongIndex"/> of the <see cref="LongRange"/>.</summary>
	public LongIndex Start { get; } = start;
	/// <summary>Represent the exclusive end <see cref="LongIndex"/> of the <see cref="LongRange"/>.</summary>
	public LongIndex End { get; } = end;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override bool Equals([NotNullWhen(true)] object? obj) =>
		obj is LongRange r && Start.Equals(r.Start) && End.Equals(r.End);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(LongRange other) => Start.Equals(other.Start) && End.Equals(other.End);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);

	/// <summary>Returns the hash code for this instance.</summary>
	public override int GetHashCode() => Utils.CombineHashCode(Start.GetHashCode(), End.GetHashCode());

	/// <summary>Converts the value of the current <see cref="LongRange"/> object to its equivalent string representation.</summary>
	public override string ToString() => $"{Start}..{End}";

	/// <summary>Create a <see cref="LongRange"/> object starting from <paramref name="start"/> to the end of the collection.</summary>
	public static LongRange StartAt(LongIndex start) => new(start, LongIndex.End);

	/// <summary>Create a <see cref="LongRange"/> object starting from first element in the collection to the <paramref name="end"/>.</summary>
	public static LongRange EndAt(LongIndex end) => new(LongIndex.Start, end);

	/// <summary>Create a <see cref="LongRange"/> object starting from first element to the end.</summary>
	public static LongRange All => new(LongIndex.Start, LongIndex.End);

	/// <summary>Calculate the start offset and length of <see cref="LongRange"/> object using a collection length.</summary>
	/// <param name="length">The length of the collection that the <see cref="LongRange"/> will be used with. length has to be a positive value.</param>
	/// <remarks>
	/// For performance reason, we don't validate the input length parameter against negative values.
	/// It is expected <see cref="LongRange"/> will be used with collections which always have non negative length/count.
	/// We validate the <see cref="LongRange"/> is inside the length scope though.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public (long Offset, long Length) GetOffsetAndLength(long length) {
		var (start, end) = GetOffsetAndEnd(length);
		return (start, end - start);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public (long Offset, long End) GetOffsetAndEnd(long length) {
		Debug.Assert(length >= 0);
		long start = Start.GetOffset(length);
		long end = End.GetOffset(length);

		if (unchecked((ulong)end) > (ulong)length || unchecked((ulong)start > (ulong)end))
			ThrowHelper.ThrowArgumentOutOfRange(length);

		return (start, end);
	}

	/// <summary>Converts <see cref="Range"/> to a <see cref="LongRange"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator LongRange(Range value) => new(value.Start, value.End);
	/// <summary>
	/// Converts <see cref="LongRange"/> to a <see cref="Range"/> if the values of <see cref="Start"/> and <see cref="End"/> are both within the range of <see cref="int"/>.
	/// </summary>
	/// <exception cref="OverflowException">The value is outside the range of <see cref="int"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static explicit operator Range(LongRange value) => new((Index)value.Start, (Index)value.End);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator ==(LongRange left, LongRange right) {
		return left.Equals(right);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator !=(LongRange left, LongRange right) {
		return !(left == right);
	}
}