using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;

namespace MySpace.Common.Runtime.InteropServices
{
	/// <summary>
	/// DynamicDllImportBinder uses reflection to bind a native assembly to managed delegated.  
	/// <see cref="DynamicDllImportAttribute"/> depends on the binder for the attribute to function.
	/// </summary>
	public static class DynamicDllImportBinder 
	{
		private static readonly Dictionary<string, IntPtr> Libraries = new Dictionary<string, IntPtr>();
		private static readonly Platform EffectivePlatform = IntPtr.Size == 4 ? Platform.Win32 : Platform.X64;

		/// <summary>
		/// Binds the delegate properties of the type to native assemblies. Properties bound are marked with <see cref="DynamicDllImportAttribute"/>.
		/// Classes would normally call DynamicDllImportBinder.Bind() in a static constructor.
		/// </summary>
		/// <param name="type">The type that contains delegates that have the DynamicDllImport attribute applied to them..</param>
		public static void Bind(Type type)
		{
			// refelect over the type class find all properties that have the DynamicDllImportAttribute, and bind
			// them to the assembly referenced.
			var properites = type.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (var property in properites)
			{
				var attributes = property.GetCustomAttributes(typeof(DynamicDllImportAttribute), false);
				if (attributes.Length > 0)
				{
					foreach (DynamicDllImportAttribute attribute in attributes)
					{
						if (attribute.TargetPlatform == EffectivePlatform)
						{
							property.SetValue(null, GetUnmanagedDelegate(attribute.AssemblyName, attribute.EntryPoint ?? property.Name, property.PropertyType), null);
						}
					}
				}
			}
		}

		private static Delegate GetUnmanagedDelegate(string unmanagedAssembly, string entryPoint, Type delegateType)
		{
			IntPtr hModule;
			if (Libraries.ContainsKey(unmanagedAssembly))
			{
				hModule = Libraries[unmanagedAssembly];
			}
			else
			{
				hModule = AttemptLoadLibrary(unmanagedAssembly);
				if (hModule == IntPtr.Zero)
				{
					throw new ApplicationException(
						String.Format("Cannot load assembly {0} with error code 0x{1:x}.  Deploy this native assembly with the managed assemblies (the .NET framework may not automatically copy it for you), or that it is in the environment Path of the process.", unmanagedAssembly, Marshal.GetLastWin32Error()));
				}
				Libraries[unmanagedAssembly] = hModule;
			}

			IntPtr procAddr = GetProcAddress(hModule, entryPoint);
			if (procAddr == IntPtr.Zero)
			{
				throw new ApplicationException(String.Format("Could not bind to entry point {0} in assembly {1}.  The assembly was found, but binding to the entry point failed with code 0x{2:x}.", entryPoint, unmanagedAssembly, Marshal.GetLastWin32Error()));
			}
			return Marshal.GetDelegateForFunctionPointer(procAddr, delegateType);
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr LoadLibrary(String dllName);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr GetProcAddress(IntPtr hModule, String procName);

		private static IntPtr AttemptLoadLibrary(string unmanagedAssembly)
		{
			var hModule = IntPtr.Zero;

			// first, attempt shadow copying the file before attempting to bind to 
			// locations that are not shadow copied.  This will favor xcopy deployments.
			if (ShadowCopier.CopyAssemblyFile(unmanagedAssembly) != ShadowCopyStatus.CopyNotRequired)
			{
				hModule = LoadLibrary(Path.Combine(ShadowCopier.ShadowDirectory, unmanagedAssembly));
			}

			if (hModule == IntPtr.Zero)
			{
				// try to load it in the current environment PATH (normal for location native assemblies).
				hModule = LoadLibrary(unmanagedAssembly);
			}

			if (hModule == IntPtr.Zero)
			{
				// next, try the current working directory
				hModule = LoadLibrary(Path.Combine(Environment.CurrentDirectory, unmanagedAssembly));
			}

			if (hModule == IntPtr.Zero)
			{
				// next, try the base directory for the app domain
				hModule = LoadLibrary(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, unmanagedAssembly));
			}

			return hModule;
		}
	}
}
