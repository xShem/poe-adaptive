namespace System;
/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
public class String {
	public static extern string FastAllocateString(int length);
	public static extern string FastAllocateString(nint length);
}