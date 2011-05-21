using System;
using System.Linq;

namespace MySpace.Common.Runtime.InteropServices
{
	/// <summary>
	/// DynamicDllImportAttribute allows runtime binding to native assemblies based on platform type.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
	public class DynamicDllImportAttribute : Attribute
	{
		/// <summary>
		/// TargetPlatform is used by DynamicDllImportBinder to determine the platform the element
		/// embued with this attribute is relevant for.
		/// </summary>
		public Platform TargetPlatform { get; private set; }
		/// <summary>
		/// AssemblyName identifies the path to the native assembly that represents the platform-specific
		/// native assembly.  The full path isn't required as DynamicDllImportBinder will search various
		/// well-known locations to find the assembly.  (Deployment directory and envirnoment path).
		/// </summary>
		public String AssemblyName { get; private set; }

		/// <summary>
		/// EntryPoint is optional.  If unspecified, the EntryPoint is determined by the reflected name
		/// of the element that binds to the native assembly.  Specify an EntryPoint if the actual entrypoint
		/// is not syntactically possible to write.  For exmaple, native assemblies may specify entry points
		/// such as "_MyEntryPoint@16", which cannot be a valid symbol name in C#.
		/// </summary>
		public String EntryPoint { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DynamicDllImportAttribute"/> class.
		/// </summary>
		/// <param name="targetPlatform">The target platform.</param>
		/// <param name="assemblyName">Name of the native assembly for the specified platform.</param>
		public DynamicDllImportAttribute(Platform targetPlatform, string assemblyName)
		{
			AssemblyName = assemblyName;
			TargetPlatform = targetPlatform;
		}
	}
}
