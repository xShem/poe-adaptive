using System.Runtime.CompilerServices;

[module: SkipLocalsInit]
[assembly: IgnoresAccessChecksTo("System.Private.CoreLib")] // Hacking for using .Net internal members

namespace System.Runtime.CompilerServices;
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute {
	public string AssemblyName { get; } = assemblyName;
}