using System.Threading.Tasks;

namespace System.IO;
/// <summary>
/// Forwarded to System.Private.CoreLib.dll at runtime
/// </summary>
public class MemoryStream {
	public byte[] _buffer;
	public readonly int _origin;
	public int _position;
	public int _length;
	public int _capacity;
	public bool _expandable;
	public bool _writable;
	public bool _exposable;
	public bool _isOpen;
	public Task<int>? _lastReadTask;
	public const int MemStreamMaxLength = int.MaxValue;

	public extern void EnsureNotClosed();
	public extern bool EnsureCapacity(int value);
}