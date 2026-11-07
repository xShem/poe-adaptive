using System.Buffers;
using System.Runtime.InteropServices;

namespace SystemExtensions.Spans;
/// <summary>
/// Memory wrapper of pointer
/// </summary>
public unsafe class PointerMemoryManager<T> : MemoryManager<T> where T : unmanaged {
	protected readonly T* pointer;
	protected readonly int length;

	/// <remarks>
	/// The lifetime of this object must not be longer than the given <paramref name="pointer"/>.
	/// <para>The <paramref name="pointer"/> is assumed to have been fixed.</para>
	/// </remarks>
	public PointerMemoryManager(T* pointer, int length) {
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		this.pointer = pointer;
		this.length = length;
	}

	public override Span<T> GetSpan() => MemoryMarshal.CreateSpan(ref *pointer, length);

	protected override void Dispose(bool disposing) { }
	public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);
	public override void Unpin() { }
}