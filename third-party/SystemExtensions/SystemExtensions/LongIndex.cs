using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SystemExtensions;
/// <summary>
/// Represent a type can be used to index a collection either from the start or the end.
/// <para><see cref="Index"/> for <see cref="long"/>.</para>
/// </summary>
public readonly struct LongIndex : IEquatable<LongIndex>, IEquatable<Index> {
	private readonly long _value;

	/// <summary>Construct a <see cref="LongIndex"/> using a value and indicating if the index is from the start or from the end.</summary>
	/// <param name="value">The index value. it has to be zero or positive number.</param>
	/// <param name="fromEnd">Indicating if the index is from the start or from the end.</param>
	/// <remarks>
	/// If the <see cref="LongIndex"/> constructed from the end, index value 1 means pointing at the last element and index value 0 means pointing at beyond last element.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LongIndex(long value, bool fromEnd = false) {
		ArgumentOutOfRangeException.ThrowIfNegative(value);
		if (fromEnd)
			_value = ~value;
		else
			_value = value;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LongIndex(long value) {
		_value = value;
	}

	/// <summary>Create a <see cref="LongIndex"/> pointing at first element.</summary>
	public static LongIndex Start => new(0L);

	/// <summary>Create a <see cref="LongIndex"/> pointing at beyond last element.</summary>
	public static LongIndex End => new(~0L);

	/// <summary>Create a <see cref="LongIndex"/> from the start at the position indicated by the value.</summary>
	/// <param name="value">The index value from the start.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static LongIndex FromStart(long value) {
		ArgumentOutOfRangeException.ThrowIfNegative(value);
		return new(value);
	}

	/// <summary>Create a <see cref="LongIndex"/> from the end at the position indicated by the value.</summary>
	/// <param name="value">The index value from the end.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static LongIndex FromEnd(long value) {
		ArgumentOutOfRangeException.ThrowIfNegative(value);
		return new(~value);
	}

	/// <summary>Returns the index value.</summary>
	public long Value => IsFromEnd ? ~_value : _value;

	/// <summary>Indicates whether the index is from the start or the end.</summary>
	public bool IsFromEnd => _value < 0L;

	/// <summary>Calculate the offset from the start using the giving collection length.</summary>
	/// <param name="length">The length of the collection that the index will be used with. length has to be a positive value</param>
	/// <remarks>
	/// For performance reason, we don't validate the input length parameter and the returned offset value against negative values.
	/// we don't validate either the returned offset is greater than the input length.
	/// It is expected <see cref="LongIndex"/> will be used with collections which always have non negative length/count. If the returned offset is negative and
	/// then used to index a collection will get out of range exception which will be same affect as the validation.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public long GetOffset(long length) {
		var offset = _value;
		if (offset < 0L /*IsFromEnd*/) unchecked {
			offset += length + 1L;
			// offset = length - (~value)
			// offset = length + (~(~value) + 1)
			// offset = length + value + 1
		}
		return offset;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is LongIndex li && _value == li._value;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(LongIndex other) => _value == other._value;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(Index other) => _value == Unsafe.As<Index, int>(ref other);

	/// <summary>Returns the hash code for this instance.</summary>
	public override int GetHashCode() => _value.GetHashCode();

	/// <summary>Converts the value of the current <see cref="LongIndex"/> object to its equivalent string representation.</summary>
	public override string ToString() {
		if (IsFromEnd)
			return '^' + ((ulong)~_value).ToString();
		return ((ulong)_value).ToString();
	}

	/// <summary>Converts integer number to a <see cref="LongIndex"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator LongIndex(long value) => FromStart(value);
	/// <summary>Converts integer number to a <see cref="LongIndex"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator LongIndex(int value) => (long)value;
	/// <summary>Converts <see cref="Index"/> to a <see cref="LongIndex"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator LongIndex(Index value) => new(Unsafe.As<Index, int>(ref value));
	/// <summary>
	/// Converts <see cref="LongIndex"/> to an <see cref="Index"/> if the value is within the range of <see cref="int"/>.
	/// </summary>
	/// <exception cref="OverflowException">The value is outside the range of <see cref="int"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable CS9193
	public static explicit operator Index(LongIndex value) => Unsafe.As<int, Index>(ref Unsafe.AsRef(checked((int)value._value)));
#pragma warning restore CS9193

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator ==(LongIndex left, LongIndex right) {
		return left.Equals(right);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator !=(LongIndex left, LongIndex right) {
		return !(left == right);
	}
}