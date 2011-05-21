using System;
using System.Configuration;

namespace MySpace.ConfigurationSystem
{
    /// <summary>
    /// How the configuration client should handle communication failures with the server.
    /// </summary>
	public enum FailStrategy
    {
        /// <summary>
        /// If there is a communication problem, throw an exception to the requestor.
        /// </summary>
		ThrowException,
		/// <summary>
		/// If there is a communication problem, but a local copy exists, use the local copy and log the exception.
		/// </summary>
        UseLocal
    }
    
    /// <summary>
    /// Configuration information for the configuration system client.
    /// </summary>
	public class ConfigurationSystemClientConfig : ConfigurationSection
    {
        /// <summary>
		/// Controls what the client will do if there is an problem when updating a locally cached config. 
        /// </summary>
		[ConfigurationProperty("failStrategy", DefaultValue = "UseLocal", IsRequired = false)]
        public FailStrategy FailStrategy
        {
            get { return (FailStrategy)this["failStrategy"]; }
            set { this["failStrategy"] = value; }
        }

		/// <summary>
		/// The URI of the configuration server.
		/// </summary>
		[ConfigurationProperty("remoteHost", DefaultValue = "configserver", IsRequired = false)]
        public string RemoteHost
        {
            get { return (string)this["remoteHost"]; }
			set { this["remoteHost"] = value; }
        }

        /// <summary>
        /// How often to poll the configuration server for updates.
        /// </summary>
		[ConfigurationProperty("sectionCheckIntervalSeconds", DefaultValue = "60", IsRequired = false)]
        public int SectionCheckIntervalSeconds
        {
            get { return (int)this["sectionCheckIntervalSeconds"]; }
            set { this["sectionCheckIntervalSeconds"] = value; }
        }

		/// <summary>
		/// Whether to poll the server actively on each request for a config section that is more than SectionCheckIntervalSeconds old. 
		/// </summary>
		[ConfigurationProperty("checkOnGet", DefaultValue = "false", IsRequired = false)]
		public bool CheckOnGet
		{
			get { return (bool)this["checkOnGet"]; }
			set { this["checkOnGet"] = value; }
		}

		/// <summary>
		/// The full type string for the implementation of MySpace.ConfigurationSystem.Encryption.IKeyProvider to use
		/// </summary>
		[ConfigurationProperty("keyProviderTypeName", DefaultValue = "MySpace.ConfigurationSystem.Encryption.Keys.KeyProvider, MySpace.ConfigurationSystem.Encryption.Keys", IsRequired = false)]
    	public string KeyProviderTypeName
    	{
			get { return (string)this["keyProviderTypeName"]; }
			set { this["keyProviderTypeName"] = value; }
    	}
    }
}
