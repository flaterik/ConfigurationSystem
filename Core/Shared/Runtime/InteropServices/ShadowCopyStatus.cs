namespace MySpace.Common.Runtime.InteropServices
{
	/// <summary>
	/// Indicates the result of <see cref="ShadowCopier.CopyAssemblyFile"/>.
	/// </summary>
	public enum ShadowCopyStatus
	{
		/// <summary>File was not copied to the shadow directory because it was not required. For example, if 
		/// the file already exists in the shadow directory (and it is the same version, if a dll).
		/// </summary>
		CopyNotRequired,
		/// <summary>File was successfully copied to the shadow directory.</summary>
		Updated,
		/// <summary>File was not copied to the shadow directory because it already existed there and was locked.</summary>
		Locked
	}
}