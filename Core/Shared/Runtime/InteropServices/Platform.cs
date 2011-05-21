

namespace MySpace.Common.Runtime.InteropServices
{
	/// <summary>
	/// Platform is used by DynamicImportAttribute to specify supported platforms for runtime binding
	/// of native assemblies.
	/// </summary>
	public enum Platform
	{
		/// <summary>Win32 platform, for use in [DynamicDllImport].</summary>
		Win32,
		/// <summary>x64 platform, for use in [DynamicDllImport].</summary>
		X64
	}
}