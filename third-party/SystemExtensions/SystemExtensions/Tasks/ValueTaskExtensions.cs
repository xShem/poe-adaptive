namespace SystemExtensions.Tasks;
public static class ValueTaskExtensions {
	private static async ValueTask<ValueTask> ContinueWith_Core(ValueTask task, CancellationToken cancellation = default) {
		var result = ValueTask.CompletedTask;
		try {
			await task.ConfigureAwait(false);
		} catch (OperationCanceledException oce) {
			var tcs = new TaskCompletionSource();
			tcs.SetCanceled(oce.CancellationToken);
			result = new(tcs.Task);
		} catch (Exception ex) {
			result = ValueTask.FromException(ex);
		}
		cancellation.ThrowIfCancellationRequested();
		return result;
	}
	private static async ValueTask<ValueTask<T>> ContinueWith_Core<T>(ValueTask<T> task, CancellationToken cancellation = default) {
		ValueTask<T> result;
		try {
			result = ValueTask.FromResult(await task.ConfigureAwait(false));
		} catch (OperationCanceledException oce) {
			var tcs = new TaskCompletionSource<T>();
			tcs.SetCanceled(oce.CancellationToken);
			result = new(tcs.Task);
		} catch (Exception ex) {
			result = ValueTask.FromException<T>(ex);
		}
		cancellation.ThrowIfCancellationRequested();
		return result;
	}

	public static async ValueTask ContinueWith(this ValueTask task, Action action, CancellationToken cancellation = default) {
		await task.ConfigureAwait(false);
		cancellation.ThrowIfCancellationRequested();
		action();
	}
	public static async ValueTask ContinueWith(this ValueTask task, Action<ValueTask> continuation, CancellationToken cancellation = default) {
		continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false));
	}
	public static async ValueTask ContinueWith<T>(this ValueTask<T> task, Action<ValueTask<T>> continuation, CancellationToken cancellation = default) {
		continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false));
	}
	public static async ValueTask<TResult> ContinueWith<TResult>(this ValueTask task, Func<ValueTask, TResult> continuation, CancellationToken cancellation = default) {
		return continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false));
	}
	public static async ValueTask<TResult> ContinueWith<T, TResult>(this ValueTask<T> task, Func<ValueTask<T>, TResult> continuation, CancellationToken cancellation = default) {
		return continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false));
	}

	public static async ValueTask ContinueWith(this ValueTask task, Action<ValueTask, CancellationToken> continuation, CancellationToken cancellation = default) {
		continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false), cancellation);
	}
	public static async ValueTask ContinueWith<T>(this ValueTask<T> task, Action<ValueTask<T>, CancellationToken> continuation, CancellationToken cancellation = default) {
		continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false), cancellation);
	}
	public static async ValueTask<TResult> ContinueWith<TResult>(this ValueTask task, Func<ValueTask, CancellationToken, TResult> continuation, CancellationToken cancellation = default) {
		return continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false), cancellation);
	}
	public static async ValueTask<TResult> ContinueWith<T, TResult>(this ValueTask<T> task, Func<ValueTask<T>, CancellationToken, TResult> continuation, CancellationToken cancellation = default) {
		return continuation(await ContinueWith_Core(task, cancellation).ConfigureAwait(false), cancellation);
	}
}