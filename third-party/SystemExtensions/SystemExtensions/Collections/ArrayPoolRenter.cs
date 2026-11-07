using System.Buffers;

namespace SystemExtensions.Collections;

/// <summary>
/// Rents an array from <see cref="ArrayPool{T}.Shared"/> and returns it when disposed.
/// </summary>
public sealed class ArrayPoolRenter<T>(int minimumLength = 0) : IDisposable {
	/// <summary>
	/// The rented array. Will be <see langword="null"/> after <see cref="Return"/> or <see cref="Dispose"/>.
	/// </summary>
	public T[] Array { get; private set; } = ArrayPool<T>.Shared.Rent(minimumLength);

	/// <summary>
	/// Rents a array with <paramref name="minimumLength"/> and returns the old one to the <see cref="ArrayPool{T}.Shared"/>.
	/// </summary>
	/// <returns><see cref="Array"/></returns>
	public T[] Resize(int minimumLength) {
		lock (this) {
			var newArray = ArrayPool<T>.Shared.Rent(minimumLength); // Rent first to check the argument
			Return();
			Array = newArray;
			return newArray;
		}
	}

	/// <summary>
	/// Rents a array with <paramref name="minimumLength"/>
	/// and copies the first <paramref name="copyLength"/> elements to the new array.
	/// And then returns the old one to the <see cref="ArrayPool{T}.Shared"/>.
	/// </summary>
	/// <returns><see cref="Array"/></returns>
	public T[] Resize(int minimumLength, int copyLength) {
		lock (this) {
			var newArray = ArrayPool<T>.Shared.Rent(minimumLength);
			try {
				new ReadOnlySpan<T>(Array, 0, copyLength).CopyTo(newArray);
			} catch {
				ArrayPool<T>.Shared.Return(newArray);
				throw;
			}
			Return();
			Array = newArray;
			return newArray;
		}
	}

	/// <summary>
	/// Returns the <see cref="Array"/> to the pool, and sets it to <see langword="null"/>.<br />
	/// Optionally clears the array before returning if <paramref name="clearArray"/>.
	/// </summary>
	public void Return(bool clearArray = false) {
		lock (this) {
			if (Array is not null) {
				ArrayPool<T>.Shared.Return(Array, clearArray);
				Array = null!;
			}
		}
	}

	/// <summary>
	/// Same as <see cref="Return"/>(<see langword="false"/>)
	/// </summary>
	public void Dispose() => Return();
}