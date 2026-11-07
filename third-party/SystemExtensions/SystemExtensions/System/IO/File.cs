extern alias corelib;

using Microsoft.Win32.SafeHandles;

using SystemExtensions.Spans;

namespace SystemExtensions.System.IO;
using System = global::System;

using RandomAccess = corelib::System.IO.RandomAccess;
public static class File {
	/// <summary>
	/// <see cref="System.IO.File.ReadAllBytes"/> but writes to a <see cref="Span{T}"/>
	/// </summary>
	/// <param name="path">Path of the file to read</param>
	/// <param name="bytes">Where to write the contents read</param>
	/// <param name="bytesRead">The number of bytes read</param>
	/// <returns><see langword="true"/> if the file has been read completely, <see langword="false"/> if <paramref name="bytes"/> is too small to hold the entire file</returns>
	public static bool TryReadAllBytes(string path, Span<byte> bytes, out int bytesRead) {
		// SequentialScan is a perf hint that requires extra sys-call on non-Windows OSes.
		using SafeFileHandle sfh = System.IO.File.OpenHandle(path, FileMode.Open,
			FileAccess.Read, FileShare.Read, OperatingSystem.IsWindows() ? FileOptions.SequentialScan : FileOptions.None);

		int l;
		for (bytesRead = 0; bytesRead < bytes.Length; bytesRead += l)
			if ((l = RandomAccess.ReadAtOffset(sfh, bytes.SliceUnchecked(bytesRead), bytesRead)) == 0)
				return true;

		return RandomAccess.ReadAtOffset(sfh, stackalloc byte[1], bytesRead) == 0;
	}
	/// <summary>
	/// <see cref="System.IO.File.ReadAllBytesAsync"/> but writes to a <see cref="Memory{T}"/>
	/// </summary>
	/// <param name="path">Path of the file to read</param>
	/// <param name="bytes">Where to write the contents read</param>
	/// <returns>The number of bytes read</returns>
	/// <remarks>This may not read the entire file if <paramref name="bytes"/> is not large enough</remarks>
	public static ValueTask<int> ReadAllBytesAsync(string path, Memory<byte> bytes, CancellationToken cancellationToken = default) {
		return cancellationToken.IsCancellationRequested
			? ValueTask.FromCanceled<int>(cancellationToken)
			: Core(path, bytes, cancellationToken);

		static async ValueTask<int> Core(string path, Memory<byte> bytes, CancellationToken cancellationToken = default) {
			// SequentialScan is a perf hint that requires extra sys-call on non-Windows OSes.
			using SafeFileHandle sfh = System.IO.File.OpenHandle(path, FileMode.Open,
				FileAccess.Read, FileShare.Read, OperatingSystem.IsWindows() ? FileOptions.SequentialScan | FileOptions.Asynchronous : FileOptions.Asynchronous);

			var bytesRead = 0;
			while (bytesRead < bytes.Length) {
				var l = await RandomAccess.ReadAtOffsetAsync(sfh, bytes.Slice(bytesRead), bytesRead, cancellationToken).ConfigureAwait(false);
				bytesRead += l;
				if (l == 0)
					break;
			}
			return bytesRead;
		}
	}

	/// <summary>
	/// <see cref="System.IO.File.WriteAllBytes"/> but accepts a <see cref="ReadOnlySpan{T}"/>
	/// </summary>
	public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes) {
		using SafeFileHandle sfh = System.IO.File.OpenHandle(path, FileMode.Create,
			FileAccess.Write, FileShare.Read, FileOptions.None, bytes.Length);
		RandomAccess.WriteAtOffset(sfh, bytes, 0);
	}
	/// <summary>
	/// <see cref="System.IO.File.WriteAllBytesAsync"/> but accepts a <see cref="ReadOnlyMemory{T}"/>
	/// </summary>
	public static ValueTask WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) {
		return cancellationToken.IsCancellationRequested
			? ValueTask.FromCanceled(cancellationToken)
			: Core(path, bytes, cancellationToken);

		static async ValueTask Core(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) {
			using SafeFileHandle sfh = System.IO.File.OpenHandle(path, FileMode.Create,
				FileAccess.Write, FileShare.Read, FileOptions.Asynchronous, bytes.Length);
			await RandomAccess.WriteAtOffsetAsync(sfh, bytes, 0, cancellationToken).ConfigureAwait(false);
		}
	}
}