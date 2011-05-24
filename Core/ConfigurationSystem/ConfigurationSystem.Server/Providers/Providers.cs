using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;

namespace MySpace.ConfigurationSystem
{
	static class Providers
	{
		private static readonly Logging.LogWrapper log = new Logging.LogWrapper();

		private static readonly Dictionary<string, IConfigurationSystemSectionProvider> _providers;
		private static readonly string _path;

		static Providers()
		{
			AppDomain currentDomain = AppDomain.CurrentDomain;
			_path = currentDomain.BaseDirectory;
			_providers = GetAvailableProviders();
		}
	
		internal static IConfigurationSystemSectionProvider GetProviderByName(string providerName)
		{
			IConfigurationSystemSectionProvider provider;
			if (!_providers.TryGetValue(providerName, out provider))
			{
				log.ErrorFormat("No provider '{0}' found. Please ensure a class that implements IConfigurationSystemSectionProvider with name '{0}' is defined in an assembly in '{1}'.", providerName, _path);
			}
			return provider;
		}

		private static Dictionary<string, IConfigurationSystemSectionProvider> GetAvailableProviders()
		{
			//iterates through all of the assemblies in the current folder looking for implementations of IConfigurationSystemSectionProvider
			DirectoryInfo binDir;
			var availableTypes = new Dictionary<string, IConfigurationSystemSectionProvider>(StringComparer.InvariantCultureIgnoreCase);

			try
			{
				binDir = new DirectoryInfo(_path);

				foreach (FileInfo file in binDir.GetFiles("*.dll", SearchOption.TopDirectoryOnly))
				{
					Assembly assembly = null;
					Type[] types;

					try
					{
						assembly = Assembly.LoadFrom(file.FullName);

						if (AssemblyReferencesServer(assembly)) //no point in looking if it doesn't reference this assembly, because this assembly defines the interface.
						{
							types = assembly.GetTypes();
						}
						else
						{
							continue;
						}
					}
					catch (ReflectionTypeLoadException rtle)
					{
						types = rtle.Types;
					}
					catch (Exception x)
					{
						if (!x.Message.Contains("The module was expected to contain an assembly manifest.")) //indicates non-dotnet assembly
						{
							log.WarnFormat("Exception looking for providers in file {0}: {1}", file.FullName, x);
						}
						continue;
					}

					//  Find all the types that implement IConfigurationSystemSectionProvider
					foreach (Type type in types)
					{
						if (assembly != null && 
							(type.IsClass)
							&& (type.IsAbstract == false) &&
							(type.GetInterface(typeof(IConfigurationSystemSectionProvider).Name) != null)
							)
						{
							try
							{
								object providerObject = assembly.CreateInstance(type.FullName);
								IConfigurationSystemSectionProvider provider = providerObject as IConfigurationSystemSectionProvider;
								if (provider != null)
								{
									log.DebugFormat("Found provider '{0}' in assembly {1}", type.Name, assembly.FullName);
									if (availableTypes.ContainsKey(type.Name))
									{
										log.WarnFormat("Provider '{0}' was found in more than one assembly. Please ensure that names are unique. The first detected provider with this name will be used; the one found in '{1}' is being ignored.", type.Name, assembly.FullName );
									}
									else
									{
										availableTypes.Add(type.Name, provider);	
									}
									
								}
							}
							catch (TypeInitializationException)
							{
								log.ErrorFormat("Type Initialization exception creating instance of provider {0} in {1}", type.FullName, assembly.FullName);
							}
							catch (Exception x)
							{
								log.ErrorFormat("Error creating instance of provider {0} in {1}: {2}", type.FullName, assembly.FullName, x);
							}
						}
					}
				}
			}
			catch (Exception x)
			{
				log.ErrorFormat("Exception looking for providers: {0}", x);
			}

			return availableTypes;
		}

		static bool AssemblyReferencesServer(Assembly a)
		{
			if (a.FullName.Contains("MySpace.ConfigurationSystem.Server"))
				return true;

			foreach (AssemblyName name in a.GetReferencedAssemblies())
			{
				if (string.Compare(name.Name, "MySpace.ConfigurationSystem.Server") == 0) return true;
			}

			return false;
		}
	}

}
