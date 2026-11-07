extern alias corelib;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions.Spans;

namespace SystemExtensions.System;

public static class MemoryExtensions {
	// Most of the code in this region was copied from System.MemoryExtensions and removed the IEquatable<T> limitation.
	// Licensed to the .NET Foundation under the MIT license.

	/// <summary>
	/// Searches for the specified <paramref name="value"/> and returns <see langword="true"/> if found. If not found, returns <see langword="false"/>.
	/// </summary>
	/// <param name="span">The span to search.</param>
	/// <param name="value">The value to search for.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this scoped ReadOnlySpan<T> span, T value) => IndexOf(span, value) >= 0;
	/// <inheritdoc cref="Contains{T}(ReadOnlySpan{T}, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> value) => IndexOf(span, value) >= 0;
	/// <inheritdoc cref="Contains{T}(ReadOnlySpan{T}, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this scoped Span<T> span, T value) => Contains((ReadOnlySpan<T>)span, value);
	/// <inheritdoc cref="Contains{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> value) => Contains((ReadOnlySpan<T>)span, value);
	/// <summary>
	/// Searches for any occurance of any of the specified <paramref name="values"/> and returns <see langword="true"/> if found. If not found, returns <see langword="false"/>.
	/// </summary>
	/// <param name="span">The span to search.</param>
	/// <param name="values">The set of values to search for.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool ContainsAny<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> values) => IndexOfAny(span, values) >= 0;
	/// <inheritdoc cref="ContainsAny{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool ContainsAny<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> values) => ContainsAny((ReadOnlySpan<T>)span, values);

	/// <summary>
	/// Searches for the specified <paramref name="value"/> and returns the index of its first occurrence. If not found, returns -1.
	/// </summary>
	/// <param name="span">The span to search.</param>
	/// <param name="value">The value to search for.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int IndexOf<T>(this scoped ReadOnlySpan<T> span, T value) {
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte))
				return corelib::System.SpanHelpers.IndexOfValueType(
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, byte>(ref value),
					span.Length);
			else if (sizeof(T) == sizeof(short))
				return corelib::System.SpanHelpers.IndexOfValueType(
					ref Unsafe.As<T, short>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, short>(ref value),
					span.Length);
			else if (sizeof(T) == sizeof(int))
				return corelib::System.SpanHelpers.IndexOfValueType(
					ref Unsafe.As<T, int>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, int>(ref value),
					span.Length);
			else if (sizeof(T) == sizeof(long))
				return corelib::System.SpanHelpers.IndexOfValueType(
					ref Unsafe.As<T, long>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, long>(ref value),
					span.Length);
#pragma warning restore CS8500
		}
		return SpanHelpersWithoutIEquatable.IndexOf(ref MemoryMarshal.GetReference(span), value, span.Length);
	}
	/// <inheritdoc cref="IndexOf{T}(ReadOnlySpan{T}, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int IndexOf<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> value) {
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte))
				return corelib::System.SpanHelpers.IndexOf(
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
					span.Length,
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)),
					value.Length);
			else if (sizeof(T) == sizeof(char))
				return corelib::System.SpanHelpers.IndexOf(
					ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(span)),
					span.Length,
					ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(value)),
					value.Length);
#pragma warning restore CS8500
		}
		return SpanHelpersWithoutIEquatable.IndexOf(ref MemoryMarshal.GetReference(span), span.Length, ref MemoryMarshal.GetReference(value), value.Length);
	}
	/// <inheritdoc cref="IndexOf{T}(ReadOnlySpan{T}, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int IndexOf<T>(this scoped Span<T> span, T value) => IndexOf((ReadOnlySpan<T>)span, value);
	/// <inheritdoc cref="IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int IndexOf<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> value) => IndexOf((ReadOnlySpan<T>)span, value);

	/// <summary>
	/// Searches for the first index of any of the specified <paramref name="values"/> similar to calling <see cref="IndexOf{T}(ReadOnlySpan{T}, T)"/> several times with the logical OR operator. If not found, returns -1.
	/// </summary>
	/// <param name="span">The span to search.</param>
	/// <param name="values">The set of values to search for.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int IndexOfAny<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> values) {
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte)) {
				ref byte spanRef = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span));
				ref byte valueRef = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(values));
				switch (values.Length) {
					case 0:
						return -1;
					case 1:
						return corelib::System.SpanHelpers.IndexOfValueType(ref spanRef, valueRef, span.Length);
					case 2:
						return corelib::System.SpanHelpers.IndexOfAnyValueType(
							ref spanRef,
							valueRef,
							Unsafe.Add(ref valueRef, 1),
							span.Length);
					case 3:
						return corelib::System.SpanHelpers.IndexOfAnyValueType(
							ref spanRef,
							valueRef,
							Unsafe.Add(ref valueRef, 1),
							Unsafe.Add(ref valueRef, 2),
							span.Length);
					case 4:
						return corelib::System.SpanHelpers.IndexOfAnyValueType(
							ref spanRef,
							valueRef,
							Unsafe.Add(ref valueRef, 1),
							Unsafe.Add(ref valueRef, 2),
							Unsafe.Add(ref valueRef, 3),
							span.Length);
					case 5:
						return corelib::System.SpanHelpers.IndexOfAnyValueType(
							ref spanRef,
							valueRef,
							Unsafe.Add(ref valueRef, 1),
							Unsafe.Add(ref valueRef, 2),
							Unsafe.Add(ref valueRef, 3),
							Unsafe.Add(ref valueRef, 4),
							span.Length);
				}
			} else if (sizeof(T) == sizeof(short)) {
				ref short spanRef = ref Unsafe.As<T, short>(ref MemoryMarshal.GetReference(span));
				ref short valueRef = ref Unsafe.As<T, short>(ref MemoryMarshal.GetReference(values));
				return values.Length switch {
					0 => -1,
					1 => corelib::System.SpanHelpers.IndexOfValueType(ref spanRef, valueRef, span.Length),
					2 => corelib::System.SpanHelpers.IndexOfAnyValueType(
													ref spanRef,
													valueRef,
													Unsafe.Add(ref valueRef, 1),
													span.Length),
					3 => corelib::System.SpanHelpers.IndexOfAnyValueType(
													ref spanRef,
													valueRef,
													Unsafe.Add(ref valueRef, 1),
													Unsafe.Add(ref valueRef, 2),
													span.Length),
					4 => corelib::System.SpanHelpers.IndexOfAnyValueType(
													ref spanRef,
													valueRef,
													Unsafe.Add(ref valueRef, 1),
													Unsafe.Add(ref valueRef, 2),
													Unsafe.Add(ref valueRef, 3),
													span.Length),
					5 => corelib::System.SpanHelpers.IndexOfAnyValueType(
													ref spanRef,
													valueRef,
													Unsafe.Add(ref valueRef, 1),
													Unsafe.Add(ref valueRef, 2),
													Unsafe.Add(ref valueRef, 3),
													Unsafe.Add(ref valueRef, 4),
													span.Length),
					_ => corelib::System.Buffers.ProbabilisticMap.IndexOfAny(ref Unsafe.As<short, char>(ref spanRef), span.Length, ref Unsafe.As<short, char>(ref valueRef), values.Length),
				};
			}
		}
		return SpanHelpersWithoutIEquatable.IndexOfAny(ref MemoryMarshal.GetReference(span), span.Length, ref MemoryMarshal.GetReference(values), values.Length);
	}
	/// <inheritdoc cref="IndexOfAny{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int IndexOfAny<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> values) => IndexOfAny((ReadOnlySpan<T>)span, values);

	/// <summary>
	/// Searches for the specified <paramref name="value"/> and returns the index of its last occurrence. If not found, returns -1.
	/// </summary>
	/// <param name="span">The span to search.</param>
	/// <param name="value">The value to search for.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int LastIndexOf<T>(this scoped ReadOnlySpan<T> span, T value) {
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte)) {
				return corelib::System.SpanHelpers.LastIndexOfValueType(
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, byte>(ref value),
					span.Length);
			} else if (sizeof(T) == sizeof(short)) {
				return corelib::System.SpanHelpers.LastIndexOfValueType(
					ref Unsafe.As<T, short>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, short>(ref value),
					span.Length);
			} else if (sizeof(T) == sizeof(int)) {
				return corelib::System.SpanHelpers.LastIndexOfValueType(
					ref Unsafe.As<T, int>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, int>(ref value),
					span.Length);
			} else if (sizeof(T) == sizeof(long)) {
				return corelib::System.SpanHelpers.LastIndexOfValueType(
					ref Unsafe.As<T, long>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, long>(ref value),
					span.Length);
			}
#pragma warning restore CS8500
		}
		return SpanHelpersWithoutIEquatable.LastIndexOf(ref MemoryMarshal.GetReference(span), value, span.Length);
	}
	/// <summary>
	/// Searches for the specified sequence and returns the index of its last occurrence. If not found, returns -1.
	/// </summary>
	/// <param name="span">The span to search.</param>
	/// <param name="value">The sequence to search for.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int LastIndexOf<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> value) {
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte)) {
				return corelib::System.SpanHelpers.LastIndexOf(
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
					span.Length,
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)),
					value.Length);
			} else if (sizeof(T) == sizeof(char)) {
				return corelib::System.SpanHelpers.LastIndexOf(
					ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(span)),
					span.Length,
					ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(value)),
					value.Length);
			}
#pragma warning restore CS8500
		}
		return SpanHelpersWithoutIEquatable.LastIndexOf(ref MemoryMarshal.GetReference(span), span.Length, ref MemoryMarshal.GetReference(value), value.Length);
	}
	/// <inheritdoc cref="LastIndexOf{T}(ReadOnlySpan{T}, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int LastIndexOf<T>(this scoped Span<T> span, T value) => LastIndexOf((ReadOnlySpan<T>)span, value);

	/// <summary>
	/// Determines whether the specified sequence appears at the start of the <paramref name="span"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool StartsWith<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> value) {
		int valueLength = value.Length;
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>())
			return valueLength <= span.Length &&
			corelib::System.SpanHelpers.SequenceEqual(
				ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
				ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)),
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
				((uint)valueLength) * (nuint)sizeof(T));  // If this multiplication overflows, the Span we got overflows the entire address range. There's no happy outcome for this api in such a case so we choose not to take the overhead of checking.
#pragma warning restore CS8500
		return valueLength <= span.Length && SpanHelpersWithoutIEquatable.SequenceEqual(ref MemoryMarshal.GetReference(span), ref MemoryMarshal.GetReference(value), valueLength);
	}
	/// <inheritdoc cref="StartsWith{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool StartsWith<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> value) => StartsWith((ReadOnlySpan<T>)span, value);
	/// <summary>
	/// Determines whether the specified sequence appears at the end of the <paramref name="span"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool EndsWith<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> value) {
		int spanLength = span.Length;
		int valueLength = value.Length;
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>())
			return valueLength <= spanLength &&
				corelib::System.SpanHelpers.SequenceEqual(
				ref Unsafe.As<T, byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)(spanLength - valueLength) /* force zero-extension */)),
				ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)),
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
				((uint)valueLength) * (nuint)sizeof(T));  // If this multiplication overflows, the Span we got overflows the entire address range. There's no happy outcome for this api in such a case so we choose not to take the overhead of checking.
#pragma warning restore CS8500
		return valueLength <= spanLength &&
			SpanHelpersWithoutIEquatable.SequenceEqual(
				ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nint)(uint)(spanLength - valueLength) /* force zero-extension */),
				ref MemoryMarshal.GetReference(value),
				valueLength);
	}
	/// <inheritdoc cref="EndsWith{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool EndsWith<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> value) => EndsWith((ReadOnlySpan<T>)span, value);

	/// <summary>
	/// Replaces all occurrences of <paramref name="oldValue"/> with <paramref name="newValue"/>.
	/// </summary>
	/// <typeparam name="T">The type of the elements in the span.</typeparam>
	/// <param name="span">The span in which the elements should be replaced.</param>
	/// <param name="oldValue">The value to be replaced with <paramref name="newValue"/>.</param>
	/// <param name="newValue">The value to replace all occurrences of <paramref name="oldValue"/>.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Replace<T>(this scoped Span<T> span, T oldValue, T newValue) where T : IEquatable<T>? {
		nuint length = (uint)span.Length;

		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte)) {
				ref byte src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span));
				corelib::System.SpanHelpers.ReplaceValueType(
					ref src,
					ref src,
					Unsafe.As<T, byte>(ref oldValue),
					Unsafe.As<T, byte>(ref newValue),
					length);
				return;
			} else if (sizeof(T) == sizeof(ushort)) {
				// Use ushort rather than short, as this avoids a sign-extending move.
				ref ushort src = ref Unsafe.As<T, ushort>(ref MemoryMarshal.GetReference(span));
				corelib::System.SpanHelpers.ReplaceValueType(
					ref src,
					ref src,
					Unsafe.As<T, ushort>(ref oldValue),
					Unsafe.As<T, ushort>(ref newValue),
					length);
				return;
			} else if (sizeof(T) == sizeof(int)) {
				ref int src = ref Unsafe.As<T, int>(ref MemoryMarshal.GetReference(span));
				corelib::System.SpanHelpers.ReplaceValueType(
					ref src,
					ref src,
					Unsafe.As<T, int>(ref oldValue),
					Unsafe.As<T, int>(ref newValue),
					length);
				return;
			} else if (sizeof(T) == sizeof(long)) {
				ref long src = ref Unsafe.As<T, long>(ref MemoryMarshal.GetReference(span));
				corelib::System.SpanHelpers.ReplaceValueType(
					ref src,
					ref src,
					Unsafe.As<T, long>(ref oldValue),
					Unsafe.As<T, long>(ref newValue),
					length);
				return;
			}
#pragma warning restore CS8500
		}
		SpanHelpersWithoutIEquatable.Replace(ref MemoryMarshal.GetReference(span), ref MemoryMarshal.GetReference(span), oldValue, newValue, length);
	}
	/// <summary>
	/// Copies <paramref name="source"/> to <paramref name="destination"/>, replacing all occurrences of <paramref name="oldValue"/> with <paramref name="newValue"/>.
	/// </summary>
	/// <typeparam name="T">The type of the elements in the spans.</typeparam>
	/// <param name="source">The span to copy.</param>
	/// <param name="destination">The span into which the copied and replaced values should be written.</param>
	/// <param name="oldValue">The value to be replaced with <paramref name="newValue"/>.</param>
	/// <param name="newValue">The value to replace all occurrences of <paramref name="oldValue"/>.</param>
	/// <exception cref="ArgumentException">The <paramref name="destination"/> span was shorter than the <paramref name="source"/> span.</exception>
	/// <exception cref="ArgumentException">The <paramref name="source"/> and <paramref name="destination"/> were overlapping but not referring to the same starting location.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Replace<T>(this scoped ReadOnlySpan<T> source, scoped Span<T> destination, T oldValue, T newValue) where T : IEquatable<T>? {
		nuint length = (uint)source.Length;
		if (length == 0)
			return;

		if (length > (uint)destination.Length)
			ThrowHelper.Throw<ArgumentException>("Destination is too short", nameof(destination));

		ref T src = ref MemoryMarshal.GetReference(source);
		ref T dst = ref MemoryMarshal.GetReference(destination);

		nint byteOffset = Unsafe.ByteOffset(ref src, ref dst);
		unchecked {
			if (byteOffset != 0 &&
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
				((nuint)byteOffset < (nuint)((nint)(uint)source.Length * sizeof(T)) ||
				 (nuint)byteOffset > (nuint)(-((nint)(uint)destination.Length * sizeof(T))))) {
#pragma warning restore CS8500
				ThrowHelper.Throw<ArgumentException>("Source and destination span overlapped", nameof(destination));
			}
		}

		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte)) {
				corelib::System.SpanHelpers.ReplaceValueType(
					ref Unsafe.As<T, byte>(ref src),
					ref Unsafe.As<T, byte>(ref dst),
					Unsafe.As<T, byte>(ref oldValue),
					Unsafe.As<T, byte>(ref newValue),
					length);
				return;
			} else if (sizeof(T) == sizeof(ushort)) {
				// Use ushort rather than short, as this avoids a sign-extending move.
				corelib::System.SpanHelpers.ReplaceValueType(
					ref Unsafe.As<T, ushort>(ref src),
					ref Unsafe.As<T, ushort>(ref dst),
					Unsafe.As<T, ushort>(ref oldValue),
					Unsafe.As<T, ushort>(ref newValue),
					length);
				return;
			} else if (sizeof(T) == sizeof(int)) {
				corelib::System.SpanHelpers.ReplaceValueType(
					ref Unsafe.As<T, int>(ref src),
					ref Unsafe.As<T, int>(ref dst),
					Unsafe.As<T, int>(ref oldValue),
					Unsafe.As<T, int>(ref newValue),
					length);
				return;
			} else if (sizeof(T) == sizeof(long)) {
				corelib::System.SpanHelpers.ReplaceValueType(
					ref Unsafe.As<T, long>(ref src),
					ref Unsafe.As<T, long>(ref dst),
					Unsafe.As<T, long>(ref oldValue),
					Unsafe.As<T, long>(ref newValue),
					length);
				return;
			}
#pragma warning restore CS8500
		}
		SpanHelpersWithoutIEquatable.Replace(ref src, ref dst, oldValue, newValue, length);
	}
	/// <inheritdoc cref="Replace{T}(ReadOnlySpan{T}, Span{T}, T, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void Replace<T>(this scoped Span<T> source, scoped Span<T> destination, T oldValue, T newValue) where T : IEquatable<T>? => Replace((ReadOnlySpan<T>)source, destination, oldValue, newValue);

	/// <summary>Counts the number of times the specified <paramref name="value"/> occurs in the <paramref name="span"/>.</summary>
	/// <typeparam name="T">The element type of the span.</typeparam>
	/// <param name="span">The span to search.</param>
	/// <param name="value">The value for which to search.</param>
	/// <returns>The number of times <paramref name="value"/> was found in the <paramref name="span"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int Count<T>(this scoped ReadOnlySpan<T> span, T value) {
		if (corelib::System.Runtime.CompilerServices.RuntimeHelpers.IsBitwiseEquatable<T>()) {
#pragma warning disable CS8500 // BitwiseEquatable is always unmanged
			if (sizeof(T) == sizeof(byte)) {
				return corelib::System.SpanHelpers.CountValueType(
					ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, byte>(ref value),
					span.Length);
			} else if (sizeof(T) == sizeof(short)) {
				return corelib::System.SpanHelpers.CountValueType(
					ref Unsafe.As<T, short>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, short>(ref value),
					span.Length);
			} else if (sizeof(T) == sizeof(int)) {
				return corelib::System.SpanHelpers.CountValueType(
					ref Unsafe.As<T, int>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, int>(ref value),
					span.Length);
			} else if (sizeof(T) == sizeof(long)) {
				return corelib::System.SpanHelpers.CountValueType(
					ref Unsafe.As<T, long>(ref MemoryMarshal.GetReference(span)),
					Unsafe.As<T, long>(ref value),
					span.Length);
			}
#pragma warning restore CS8500
		}
		return SpanHelpersWithoutIEquatable.Count(ref MemoryMarshal.GetReference(span), value, span.Length);
	}
	/// <summary>Counts the number of times the specified <paramref name="value"/> occurs in the <paramref name="span"/>.</summary>
	/// <typeparam name="T">The element type of the span.</typeparam>
	/// <param name="span">The span to search.</param>
	/// <param name="value">The value for which to search.</param>
	/// <returns>The number of times <paramref name="value"/> was found in the <paramref name="span"/>.</returns>
	public static int Count<T>(this scoped ReadOnlySpan<T> span, scoped ReadOnlySpan<T> value) {
		switch (value.Length) {
			case 0:
				return 0;
			case 1:
				return Count(span, value[0]);
			default:
				int count = 0;
				int pos;
				while ((pos = IndexOf(span, value)) >= 0) {
					span = span.Slice(pos + value.Length);
					count++;
				}
				return count;
		}
	}
	/// <inheritdoc cref="Count{T}(ReadOnlySpan{T}, T)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Count<T>(this scoped Span<T> span, T value) => Count((ReadOnlySpan<T>)span, value);
	/// <inheritdoc cref="Count{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Count<T>(this scoped Span<T> span, scoped ReadOnlySpan<T> value) => Count((ReadOnlySpan<T>)span, value);
}