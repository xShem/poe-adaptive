extern alias corelib;

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using corelib::System.Runtime.CompilerServices;

using SystemExtensions.Spans;

namespace SystemExtensions.Unsafes;
using System = global::System;
/// <summary>
/// Helper methods to treat a space as an <see cref="object"/> to avoid heap allocation.
/// <br />*Haven't tested any yet. Be careful using this class*
/// </summary>
/// <remarks>
/// <para>The length of the buffer must be at least <see cref="Size(Type)"/> / <see cref="SizeOfString"/> / <see cref="SizeOfArray{T}"/>.</para>
/// <para>The object simulated by this class have a lifetime same as the buffer passed to the funtions, when it's on stack, don't store it anywhere else like a field of other object, a collection or captured by a lambda expression.</para>
/// <para>The object must not contain any reference type or they must always be <see langword="null"/>, otherwise it will randomly throw <see cref="AccessViolationException"/> on access.</para>
/// <para>The destructor <see cref="object.Finalize"/> of the object won't be called cause that it's not managed by GC.</para>
/// <para>This class is experimental and may not work as expected.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static unsafe class ValueObject {
	[StructLayout(LayoutKind.Explicit)]
	private readonly struct MethodTable { // Subset of src/vm/methodtable.h
		/// <summary>
		/// The low WORD of the first field is the component size for array and string types.
		/// </summary>
		[FieldOffset(0)]
		public readonly ushort ComponentSize;
		/// <summary>
		/// The flags for the current method table (only for not array or string types).
		/// </summary>
		[FieldOffset(0)]
		private readonly uint Flags;
		/// <summary>
		/// The base size of the type (used when allocating an instance on the heap).
		/// </summary>
		[FieldOffset(4)]
		public readonly uint BaseSize;

		public readonly bool HasComponentSize => (Flags & 0x80000000U/*enum_flag_HasComponentSize*/) != 0;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static MethodTable* GetMethodTable(object instance) {
		return *(MethodTable**)Unsafe.As<object, nint>(ref instance);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AsObject<T>(Span<byte> buffer, out object obj) {
		Debug.Assert(buffer.Length >= sizeof(nint) * 2);

		ref var p = ref Unsafe.As<byte, nint>(ref MemoryMarshal.GetReference(buffer));
		p = 0; // ObjectHeader
		p = ref Unsafe.Add(ref p, 1);
		p = typeof(T).TypeHandle.Value; // MethodTable

		Unsafe.SkipInit(out obj);
		Unsafe.As<object, nint>(ref obj) = (nint)Unsafe.AsPointer(ref p);
	}

	/// <summary>
	/// Returns an simulated object of type <typeparamref name="T"/> that points to the <paramref name="buffer"/><br />
	/// The first two <see cref="nint"/>s of the <paramref name="buffer"/> must be reserved for ObjectHeader and MethodTable, which will be set by this method.<br />
	/// </summary>
	/// <remarks>
	/// <para>Note that this won't check the contents in <paramref name="buffer"/>, carefullly use it or it may cause undefined behavior.</para>
	/// <para>See remarks of <see cref="ValueObject"/></para>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T AsObject<T>(Span<byte> buffer) where T : class {
		if (buffer.Length < Size<T>())
			ThrowHelper.Throw<ArgumentException>("The length of the buffer is less than ValueObject.Size(Type)");
		AsObject<T>(buffer, out var result);
		return Unsafe.As<T>(result);
	}
	/// <summary>
	/// Returns an simulated object which is boxed value of type <typeparamref name="T"/> that points to the <paramref name="buffer"/><br />
	/// The first two <see cref="nint"/>s of the <paramref name="buffer"/> must be reserved for ObjectHeader and MethodTable, which will be set by this method.<br />
	/// </summary>
	/// <remarks>
	/// <para>See remarks of <see cref="ValueObject"/></para>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static object AsBoxedObject<T>(Span<byte> buffer) where T : struct {
		if (buffer.Length < Size<T>())
			ThrowHelper.Throw<ArgumentException>("The length of the buffer is less than ValueObject.Size(Type)");
		AsObject<T>(buffer, out var result);
		return result;
	}

	/// <summary>
	/// Instantiates an simulated object of type <typeparamref name="T"/> to the <paramref name="buffer"/>
	/// </summary>
	/// <param name="constructor">
	/// Constructor of <typeparamref name="T"/> to call,
	/// or <see langword="null"/> to return an uninitialized object which like calling <see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject"/>
	/// </param>
	/// <remarks>
	/// See remarks of <see cref="ValueObject"/>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T CreateObject<T>(Span<byte> buffer, ConstructorInfo? constructor = null, params object?[]? parameters) where T : class {
		if (buffer.Length < Size<T>())
			ThrowHelper.Throw<ArgumentException>("The length of the buffer is less than ValueObject.Size(Type)");
		buffer.Clear();

		AsObject<T>(buffer, out var result);
		constructor?.Invoke(result, parameters);
		return Unsafe.As<T>(result);
	}

	/// <summary>
	/// Memberwise copies an existing <paramref name="instance"/> of type <typeparamref name="T"/> to the <paramref name="buffer"/>
	/// and returns an simulated object that points to it
	/// </summary>
	/// <remarks>
	/// See remarks of <see cref="ValueObject"/>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T FromObject<T>(Span<byte> buffer, T instance) where T : class {
		if (instance.GetType() != typeof(T))
			ThrowHelper.Throw<ArgumentException>("The type of the instance is not the same as the generic type parameter", nameof(instance));
		var size = checked((int)Size(instance));
		if (buffer.Length < size)
			ThrowHelper.Throw<ArgumentException>("The length of the buffer is less than ValueObject.Size(object)", nameof(buffer));
		buffer.Clear();

		AsObject<T>(buffer, out var result);
		GetFieldsSpan(instance, size).CopyToUnchecked(ref GetFieldRef(instance));
		return Unsafe.As<T>(result);
	}

	/// <summary>
	/// Box a value type <typeparamref name="T"/> to the <paramref name="buffer"/>
	/// and returns an simulated object that points to it
	/// </summary>
	/// <remarks>
	/// See remarks of <see cref="ValueObject"/>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static object Box<T>(Span<byte> buffer, in T value) where T : struct {
#pragma warning disable CS8500
		var size = sizeof(nint) * 2 + sizeof(T);
		if (buffer.Length < size)
			ThrowHelper.Throw<ArgumentException>("The length of the buffer is less than ValueObject.Size(Type)");
		buffer.Clear();

		AsObject<T>(buffer, out var result);
		GetFieldsSpan(in value).CopyToUnchecked(ref GetFieldRef(result));
		return Unsafe.As<object, T>(ref result);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ref byte GetFieldRef(object instance) {
		return ref Unsafe.As<RawData>(instance).Data;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<byte> GetFieldsSpan<T>(in T value) where T : struct {
		return MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)), sizeof(T));
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Span<byte> GetFieldsSpan(object instance, int knownObjectSize /*= Size(instance)*/) {
		return MemoryMarshal.CreateSpan(ref GetFieldRef(instance), knownObjectSize - sizeof(nint) * 2);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<byte> GetFieldsSpan(object instance) {
		return GetFieldsSpan(instance, checked((int)Size(instance)));
	}

	/// <summary>
	/// Get the size of a instance of <paramref name="type"/> on heap in bytes
	/// </summary>
	/// <remarks>
	/// For non-empty <see cref="string"/>, use <see cref="SizeOfString"/> instead.<br />
	/// For non-empty <see cref="Array"/>, use <see cref="SizeOfArray"/> instead.<br />
	/// Or use <see cref="Size(object)"/> to handle all types with an existing instance.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[RequiresDynamicCode("Use the Size<T> overload instead.")]
	public static uint Size(Type type) => ((MethodTable*)type.TypeHandle.Value)->BaseSize; // MethodTable->BaseSize
	/// <summary>
	/// Get the size of a instance of <typeparamref name="T"/> on heap in bytes
	/// </summary>
	/// <remarks>
	/// For non-empty <see cref="string"/>, use <see cref="SizeOfString"/> instead.<br />
	/// For non-empty <see cref="Array"/>, use <see cref="SizeOfArray"/> instead.<br />
	/// Or use <see cref="Size(object)"/> to handle all types with an existing instance.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.")]
	public static uint Size<T>() => Size(typeof(T));

	/// <summary>
	/// Get the size of <paramref name="instance"/> on heap in bytes
	/// </summary>
	/// <remarks>
	/// For types other than <see cref="string"/> and <see cref="Array"/>, equivalent to <see cref="Size(Type)"/>.<br />
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint Size(object instance) {
		var mt = GetMethodTable(instance);
		var result = mt->BaseSize;
		if (mt->HasComponentSize)
			result = checked(result + mt->ComponentSize * Unsafe.As<RawArrayData>(instance).Length);
		return result;
	}

	/// <summary>
	/// <see cref="Size(Type)"/> for <see cref="string"/> type
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint SizeOfString(int length) {
		// ObjectHeader + MethodTable + Length(int) + Chars + NullTerminator
		return checked((uint)sizeof(nint) * 2U + sizeof(int) + sizeof(char) + sizeof(char) * (uint)length);
		/* Equivalent to:
		var mt = (MethodTable*)typeof(string).TypeHandle.Value;
		return (int)mt->BaseSize + mt->ComponentSize * length; // 22 + sizeof(char) * length
		*/
	}
	/// <summary>
	/// <see cref="Size(Type)"/> for <see cref="Array"/> type (<typeparamref name="T"/>[], <typeparamref name="T"/>[,] ...)
	/// <para>All reference types of <typeparamref name="T"/> with the same parameters return the same value.</para>
	/// </summary>
	/// <typeparam name="T">Element type of the array</typeparam>
	/// <param name="length">Length of the array (product of length of all dimension)</param>
	/// <param name="dimension">Number of dimensions of the array</param>
	/// <remarks>
	/// Note that for one-dimensional array created from <see cref="Array.CreateInstance(Type, int[], int[])"/>, if the lowerbound is not 0,
	/// the size should be the result of this method plus <c><see langword="sizeof"/>(int) * 2</c><br />
	/// Or a better way is to use <see cref="Size(object)"/> instead.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint SizeOfArray<T>(int length, int dimension = 1) {
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
		checked {
			// ObjectHeader + MethodTable + Length(nint) + Elements
			var size = (uint)sizeof(nint) * 3U + (uint)sizeof(T) * (uint)length;
			if (dimension != 1)
				size += sizeof(int) * 2U * (uint)dimension; // (DimensionLength + LowerBound) * Rank
			return size;
		}
		/* Equivalent to:
		var mt = (MethodTable*)typeof(T).MakeArrayType(dimension).TypeHandle.Value;
		return (int)mt->BaseSize + mt->ComponentSize * length;
		*/
	}
}