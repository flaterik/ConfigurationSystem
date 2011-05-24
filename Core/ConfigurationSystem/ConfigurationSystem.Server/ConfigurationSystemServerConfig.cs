using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System;
using MySpace.Logging;

namespace MySpace.ConfigurationSystem
{
	public class ConfigurationSystemServerConfig : ConfigurationSection
	{
		public static readonly short DefaultTimeToLive = 360;

		[ConfigurationProperty("port", DefaultValue = "9000", IsRequired = false)]
		public int Port
		{
			get { return (int)this["port"]; }
			set { this["port"] = value; }
		}
		
		[ConfigurationProperty("prefix", DefaultValue = "*", IsRequired = false)]
		public string Prefix
		{
			get { return (string)this["prefix"]; }
			set { this["prefix"] = value; }
		}

		[ConfigurationProperty("compressionThreshold", DefaultValue = "10240", IsRequired = false)]
		public int CompressionThreshold
		{
			get { return (int)this["compressionThreshold"]; }
			set { this["compressionThreshold"] = value; }
		}

		//The Tfs values are used by our Tfs Provider, which we are not including with this release because it requires the Tfs Client libraries, which we 
		//would not be allowed to redistribute in assembly form. If there is interest we can release the library with the understanding that installing the
		//client library would be necessary to run the provider.
		//It would be prefable to have an ability to provide arbitrary configuration values to plugins, similar to what Data Relay does for components,
		//but that would add complexity and is unnecessary for our internal purposes, so the time to write it is difficult to justify :)
		[ConfigurationProperty("tfsServer", DefaultValue = "tfs:8080", IsRequired = false)]
		public string TfsServer
		{
			get { return (string)this["tfsServer"]; }
			set { this["tfsServer"] = value; }
		}

		[ConfigurationProperty("tfsUserDomain", DefaultValue = "", IsRequired = false)]
		public string TfsUserDomain
		{
			get { return (string)this["tfsUserDomain"]; }
			set { this["tfsUserDomain"] = value; }
		}

		[ConfigurationProperty("tfsUserName", IsRequired = false)]
		public string TfsUserName
		{
			get { return (string)this["tfsUserName"]; }
			set { this["tfsUserName"] = value; }
		}

		[ConfigurationProperty("tfsUserPassword", IsRequired = false)]
		public string TfsUserPassword
		{
			get { return rot13((string)this["tfsUserPassword"]); }
			set { this["tfsUserPassword"] = value; }
		}

		[ConfigurationProperty("mappingConfigPath", DefaultValue = "SectionMapping.xml", IsRequired = false)]
		public string MappingConfigPath
		{
			get { return (string)this["mappingConfigPath"]; }
			set { this["mappingConfigPath"] = value; }
		}

		[ConfigurationProperty("mappingConfigProvider", DefaultValue = "FileProvider", IsRequired = false)]
		public string MappingConfigProvider
		{
			get { return (string)this["mappingConfigProvider"]; }
			set { this["mappingConfigProvider"] = value; }
		}

		[ConfigurationProperty("preCaching", DefaultValue = "true", IsRequired = false)]
		public bool PreCaching
		{
			get { return (bool)this["preCaching"]; }
			set { this["preCaching"] = value; }
		}

		/// <summary>
		/// The full type string for the implementation of MySpace.ConfigurationSystem.Encryption.IKeyProvider to use.
		/// </summary>
		[ConfigurationProperty("keyProviderTypeName", DefaultValue = "MySpace.ConfigurationSystem.Encryption.Keys.KeyProvider, MySpace.ConfigurationSystem.Encryption.Keys", IsRequired = false)]
		public string KeyProviderTypeName
		{
			get { return (string)this["keyProviderTypeName"]; }
			set { this["keyProviderTypeName"] = value; }
		}

		private static string rot13(string val)
		{
			char[] arr = val.ToCharArray();
			for (int i = 0; i<arr.Length; i++)
			{
				int num = (int)arr[i];
				if (num >= 'a' && num <= 'z')
					if (num > 'm')
						num -= 13;
					else
						num += 13;
				else if (num >= 'A' && num <= 'Z')
					if (num > 'M')
						num -= 13;
					else
						num += 13;
				arr[i] = (char)num;
			}
			return new string(arr);
		}
	}
}
