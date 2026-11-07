namespace System.Runtime.CompilerServices;
/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
public static class RuntimeHelpers {
	public static extern bool IsBitwiseEquatable<T>();
}

/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
public sealed class RawData {
	public byte Data;
}

/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
public sealed class RawArrayData {
	public uint Length; // Array._numComponents padded to IntPtr
#if TARGET_64BIT
	public uint Padding;
#endif
	public byte Data;
}