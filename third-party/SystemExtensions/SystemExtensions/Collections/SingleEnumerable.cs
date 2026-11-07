using System.Collections;
using System.Runtime.CompilerServices;

namespace SystemExtensions.Collections;

/// <summary>
/// An <see cref="IEnumerable{T}"/> that yields a single value for the best performance.
/// </summary>
/// <param name="value">The value to yield.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class SingleEnumerable<T>(T value) : IEnumerable<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public SingleEnumerator<T> GetEnumerator() => new(value);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// An <see cref="IEnumerator{T}"/> that yields a single value for the best performance.
/// </summary>
/// <param name="value">The value to yield.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class SingleEnumerator<T>(T value) : IEnumerator<T> {
	public bool Moved { get; private set; }

    public T Current => value;
    object? IEnumerator.Current => value;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext() {
		if (Moved)
			return false;
        Moved = true;
		return true;
    }
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => Moved = false;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() { }
}