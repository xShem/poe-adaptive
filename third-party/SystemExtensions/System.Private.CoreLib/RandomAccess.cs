using System.IO.Strategies;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32.SafeHandles;

namespace System.IO {
	/// <summary>
	/// Forwarded to System.Private.CoreLib.dll at runtime
	/// </summary>
	public static class RandomAccess {
		public static extern unsafe int ReadAtOffset(SafeFileHandle handle, Span<byte> buffer, long fileOffset);
		public static extern ValueTask<int> ReadAtOffsetAsync(SafeFileHandle handle, Memory<byte> buffer, long fileOffset, CancellationToken cancellationToken, OSFileStreamStrategy? strategy = null);
		public static extern unsafe void WriteAtOffset(SafeFileHandle handle, ReadOnlySpan<byte> buffer, long fileOffset);
		public static extern ValueTask WriteAtOffsetAsync(SafeFileHandle handle, ReadOnlyMemory<byte> buffer, long fileOffset, CancellationToken cancellationToken, OSFileStreamStrategy? strategy = null);
	}
}

namespace System.IO.Strategies {
	/// <summary>
	/// Forwarded to System.Private.CoreLib.dll at runtime
	/// </summary>
	public abstract class OSFileStreamStrategy { }
}