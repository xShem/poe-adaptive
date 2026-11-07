extern alias corelib;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions.Spans;

namespace SystemExtensions;
/// <summary>
/// Miscellaneous or not yet classified methods
/// </summary>
public static class Utils {
	/// <inheritdoc cref="FastAllocateString(int)"/>
	[Obsolete("This method is only supported in .NET 9 and earlier versions. See also the nint overload.")]
	private static string FastAllocateStringNET9(int length) => corelib::System.String.FastAllocateString(length);
	/// <inheritdoc cref="FastAllocateString(int)"/>
	[Obsolete("This method is only supported after .NET 10. See also the int overload.")]
	private static string FastAllocateStringNET10(nint length) => corelib::System.String.FastAllocateString(length);
	/// <summary>
	/// Allocates a new string with <paramref name="length"/> characters which may not be initialized.
	/// </summary>
	/// <param name="length"><see cref="string.Length"/></param>
    public static string FastAllocateString(int length) => Environment.Version.Major < 10 // int was changed to nint in .NET 10
#pragma warning disable CS0618 // obsolete
		? FastAllocateStringNET9(length) : FastAllocateStringNET10(length);
#pragma warning restore CS0618

    /// <summary>
    /// Calls <see cref="ReverseBytes"/> if the current architecture is big-endian.
    /// </summary>
    /// <returns>
    /// Whether the endianness is reversed.<br />
    /// Equivalent to !<see cref="BitConverter.IsLittleEndian"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool EnsureLittleEndian<T>(ref T value) where T : unmanaged {
		// All if and switch statements here are optimized out by JIT
		if (BitConverter.IsLittleEndian)
			return false;
		ReverseBytes(ref value);
		return true;
	}
	/// <summary>
	/// Calls <see cref="ReverseBytes"/> if the current architecture is little-endian.
	/// </summary>
	/// <returns>
	/// Whether the endianness is reversed.<br />
	/// Equivalent to <see cref="BitConverter.IsLittleEndian"/>.
	/// </returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool EnsureBigEndian<T>(ref T value) where T : unmanaged {
		// All if and switch statements here are optimized out by JIT
		if (!BitConverter.IsLittleEndian)
			return false;
		ReverseBytes(ref value);
		return true;
	}

	/// <summary>
	/// Reverses the bytes of <paramref name="value"/> on memory.<br />
	/// Likes what <see cref="MemoryExtensions.Reverse"/> does on Span&lt;byte&gt;.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void ReverseBytes<T>(ref T value) where T : unmanaged {
		if (sizeof(T) is sizeof(ushort) or sizeof(uint) or sizeof(ulong) or sizeof(ulong) * 2/*sizeof(UInt128)*/) {
			Unsafe.SkipInit(out T result);
			switch (sizeof(T)) {
				case sizeof(ushort):
					Unsafe.As<T, ushort>(ref result) = BinaryPrimitives.ReverseEndianness(Unsafe.As<T, ushort>(ref value));
					break;
				case sizeof(uint):
					Unsafe.As<T, uint>(ref result) = BinaryPrimitives.ReverseEndianness(Unsafe.As<T, uint>(ref value));
					break;
				case sizeof(ulong):
					Unsafe.As<T, ulong>(ref result) = BinaryPrimitives.ReverseEndianness(Unsafe.As<T, ulong>(ref value));
					break;
				case sizeof(ulong) * 2/*sizeof(UInt128)*/:
					Unsafe.As<T, UInt128>(ref result) = BinaryPrimitives.ReverseEndianness(Unsafe.As<T, UInt128>(ref value));
					break;
			}
			value = result;
		} else if (sizeof(T) != sizeof(byte))
			MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), sizeof(T)).Reverse();
	}

	/// <summary>
	/// <see cref="Index.GetOffset"/> but with <see cref="long"/> <paramref name="length"/>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static long GetOffset(this scoped in Index index, long length) {
		return ((LongIndex)index).GetOffset(length);
	}

	/// <summary>
	/// <see cref="Range.GetOffsetAndLength"/> but with <see cref="long"/> <paramref name="length"/>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (long Offset, long Length) GetOffsetAndLength(this scoped in Range range, long length) {
		return ((LongRange)range).GetOffsetAndLength(length);
	}

	/// <summary>
	/// <see cref="Range.GetOffsetAndLength"/> but returns End position instead of Length (which is End - Offset)
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (int Offset, int End) GetOffsetAndEnd(this Range range, int length) {
		Debug.Assert(length >= 0);
		// Since Range is actually two Index (which is actually an int)
		ref int start = ref Unsafe.As<Range, int>(ref range);
		ref int end = ref Unsafe.Add(ref start, 1);

		unchecked {
			// See Index.GetOffset
			// We don't cache length + 1 here because it's rare that both Start and End are IsFromEnd
			if (start < 0)
				start += length + 1; // length - ~start == start + length + 1
			if (end < 0)
				end += length + 1;

			if ((uint)end > (uint)length || (uint)start > (uint)end)
				ThrowHelper.ThrowArgumentOutOfRange(length);
		}
		return Unsafe.As<int, (int, int)>(ref start);
	}

	/// <summary>
	/// <see cref="GetOffsetAndEnd(Range, int)"/> but with <see cref="long"/> <paramref name="length"/>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (long Offset, long End) GetOffsetAndEnd(this scoped in Range range, long length) {
		return ((LongRange)range).GetOffsetAndEnd(length);
	}

	/// <summary>
	/// On Windows, equivalent to <see cref="Environment.ExpandEnvironmentVariables"/>.<br />
	/// On Unix, at the beginning of the <paramref name="path"/>, expands "~" or "~currentUsername" to the user's home directory,
	/// or "~+" to the <see cref="Environment.CurrentDirectory"/>.<br />
	/// Otherwise (for example: Browser), return <paramref name="path"/> as is.
	/// </summary>
	public static string ExpandPath(string path) {
		if (OperatingSystem.IsWindows())
			return Environment.ExpandEnvironmentVariables(path);
		if (path.StartsWith('~') && Environment.OSVersion.Platform == PlatformID.Unix) {
			if (path.Length == 1 || path[1] == Path.DirectorySeparatorChar || path.AsSpan(1).SequenceEqual(Environment.UserName)) { // "~" || "~/" || "~currentUsername"
				var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // When failed, fallbacks to "/"
				if (userProfile != "/")
					return string.Concat(userProfile, path.AsSpan(1));
			} else if (path[1] == '+') {
				if (path.Length == 2 || path[2] == Path.DirectorySeparatorChar) // "~+" || "~+/"
					return string.Concat(Environment.CurrentDirectory, path.AsSpan(2));
				// "~+......" => return as is
			}/* else // "~otherUsername/..."
				try {
					using var p = Process.Start(new ProcessStartInfo("sh") {
						CreateNoWindow = true,
						RedirectStandardInput = true,
						RedirectStandardOutput = true,
						WindowStyle = ProcessWindowStyle.Hidden
					});
					if (p is not null) {
						p.StandardInput.WriteLine("echo " + path);
						p.StandardInput.Flush();
						var tmp = p.StandardOutput.ReadLine();
						p.Kill();
						if (Path.Exists(tmp))
							return tmp;
					}
				} catch { }
			*/
		}
		return path;
	}

	public static int CombineHashCode(int left, int right) {
		return unchecked((int)BitOperations.RotateLeft((uint)left, 5) + left) ^ right;
	}
}