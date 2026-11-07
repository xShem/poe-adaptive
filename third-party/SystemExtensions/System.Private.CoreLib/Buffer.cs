namespace System;
/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
public static class Buffer {
	public static extern void Memmove<T>(ref T destination, ref T source, nuint elementCount);
}