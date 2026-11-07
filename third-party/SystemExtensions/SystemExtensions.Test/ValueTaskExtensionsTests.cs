using SystemExtensions.Tasks;

namespace SystemExtensions.Tests;
[TestClass]
public class ValueTaskExtensionsTests {
	[TestMethod]
	public void ContinueWith_Test() {
		bool first = false, second = false;

		async ValueTask ValueTaskAsync() {
			await Task.Run(() => {
				Thread.Sleep(50);
				first = true;
			});
		}
		async ValueTask<int> ValueTaskIntAsync() {
			await Task.Run(() => {
				Thread.Sleep(50);
				first = true;
			});
			return 1;
		}

		void DoAssert(in ValueTask task) {
			task.AsTask().GetAwaiter().GetResult();
			Assert.IsTrue(first);
			Assert.IsTrue(second);
			first = second = false;
		}
		void DoAssertWithResult(ValueTask<string> task) {
			Assert.AreEqual("2", task.AsTask().GetAwaiter().GetResult());
			Assert.IsTrue(first);
			Assert.IsTrue(second);
			first = second = false;
		}

		DoAssert(ValueTaskAsync().ContinueWith(() => second = true));
		DoAssert(ValueTaskAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			second = true;
		}));
		DoAssert(ValueTaskIntAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(1, v.Result);
			second = true;
		}));
		DoAssertWithResult(ValueTaskAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			second = true;
			return "2";
		}));
		DoAssertWithResult(ValueTaskIntAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(1, v.Result);
			second = true;
			return "2";
		}));
	}

	[TestMethod]
	public void ContinueWith_Cancel_Test() {
		bool first = false, second = false;
		var cancel = new CancellationToken(true);

		async ValueTask ValueTaskAsync() {
			await Task.Run(() => {
				Thread.Sleep(50);
				first = true;
			});
		}
		async ValueTask<int> ValueTaskIntAsync() {
			await Task.Run(() => {
				Thread.Sleep(50);
				first = true;
			});
			return 1;
		}

		void DoAssert(ValueTask task) {
			Assert.Throws<OperationCanceledException>(() => task.AsTask().GetAwaiter().GetResult());
			Assert.IsTrue(first);
			Assert.IsFalse(second);
			first = false;
		}
		void DoAssertWithResult(ValueTask<string> task) {
			Assert.Throws<OperationCanceledException>(() => Assert.AreEqual("2", task.AsTask().GetAwaiter().GetResult()));
			Assert.IsTrue(first);
			Assert.IsFalse(second);
			first = false;
		}

		DoAssert(ValueTaskAsync().ContinueWith(() => { second = true; }, cancel));
		DoAssert(ValueTaskAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			second = true;
		}, cancel));
		DoAssert(ValueTaskIntAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(1, v.Result);
			second = true;
		}, cancel));
		DoAssertWithResult(ValueTaskAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			second = true;
			return "2";
		}, cancel));
		DoAssertWithResult(ValueTaskIntAsync().ContinueWith(v => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(1, v.Result);
			second = true;
			return "2";
		}, cancel));
	}

	[TestMethod]
	public void ContinueWith_CancelSecond_Test() {
		bool first = false, second = false;
		var cancel = new CancellationTokenSource();

		async ValueTask ValueTaskAsync() {
			await Task.Run(() => {
				Thread.Sleep(50);
				first = true;
			});
		}
		async ValueTask<int> ValueTaskIntAsync() {
			await Task.Run(() => {
				Thread.Sleep(50);
				first = true;
			});
			return 1;
		}

		void DoAssert(ValueTask task) {
			Assert.Throws<OperationCanceledException>(() => task.AsTask().GetAwaiter().GetResult());
			Assert.IsTrue(first);
			Assert.IsFalse(second);
			first = false;
		}
		void DoAssertWithResult(ValueTask<string> task) {
			Assert.Throws<OperationCanceledException>(() => Assert.AreEqual("2", task.AsTask().GetAwaiter().GetResult()));
			Assert.IsTrue(first);
			Assert.IsFalse(second);
			first = false;
		}

		DoAssert(ValueTaskAsync().ContinueWith((v, c) => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(cancel.Token, c);
			cancel.Cancel(); // Simulate a cancellation during the continuation
			c.ThrowIfCancellationRequested();
			second = true;
		}, cancel.Token));
		cancel = new CancellationTokenSource();
		DoAssert(ValueTaskIntAsync().ContinueWith((v, c) => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(1, v.Result);
			Assert.AreEqual(cancel.Token, c);
			cancel.Cancel(); // Simulate a cancellation during the continuation
			c.ThrowIfCancellationRequested();
			second = true;
		}, cancel.Token));
		cancel = new CancellationTokenSource();
		DoAssertWithResult(ValueTaskAsync().ContinueWith((v, c) => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(cancel.Token, c);
			cancel.Cancel(); // Simulate a cancellation during the continuation
			c.ThrowIfCancellationRequested();
			second = true;
			return "2";
		}, cancel.Token));
		cancel = new CancellationTokenSource();
		DoAssertWithResult(ValueTaskIntAsync().ContinueWith((v, c) => {
			Assert.IsTrue(v.IsCompletedSuccessfully);
			Assert.AreEqual(1, v.Result);
			Assert.AreEqual(cancel.Token, c);
			cancel.Cancel(); // Simulate a cancellation during the continuation
			c.ThrowIfCancellationRequested();
			second = true;
			return "2";
		}, cancel.Token));
	}
}