using System.Runtime.InteropServices;

namespace System.Buffers;
/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ProbabilisticMap {
	private readonly uint _e0, _e1, _e2, _e3, _e4, _e5, _e6, _e7;
	public static extern int IndexOfAny(ref char searchSpace, int searchSpaceLength, ref char values, int valuesLength);
}