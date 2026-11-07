global using System.Text;

global using Microsoft.VisualStudio.TestTools.UnitTesting;

global using SystemExtensions.Collections;
global using SystemExtensions.Spans;
global using SystemExtensions.Streams;

global using static SystemExtensions.Tests.TestHelper;

[assembly: Parallelize]

namespace SystemExtensions.Tests;

public static class TestHelper {
	public static unsafe string NextString(this Random random, int length) {
		var str = new string('\0', length);
		fixed (char* p = str)
			for (int i = 0; i < length; ++i) {
				char c;
				do {
					c = (char)random.Next(1, ushort.MaxValue);
				} while (char.IsSurrogate(c)); // Avoid invalid string
				p[i] = c;
			}
		return str;
	}

	/// <summary>
	/// A value type with random fields (using <see cref="CreateRandom"/>) for testing
	/// </summary>
	public struct TestStruct {
		public DayOfWeek A;
		public KeyValuePair<short, bool> B;
		public long C;

		public static TestStruct CreateRandom() {
			var enums = Enum.GetValues<DayOfWeek>();
			return new() {
				A = enums[Random.Shared.Next(enums.Length)],
				B = new((short)Random.Shared.Next(short.MinValue, short.MaxValue + 1), Random.Shared.Next(2) == 1),
				C = Random.Shared.NextInt64()
			};
		}
	}
}