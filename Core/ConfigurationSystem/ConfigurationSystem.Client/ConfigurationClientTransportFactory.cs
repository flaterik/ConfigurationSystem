using System;

namespace MySpace.ConfigurationSystem
{
    internal static class ConfigurationClientTransportFactory
    {
        internal static IConfigurationClientTransport GetTransport(ConfigurationSystemClientConfig config)
        {
            if(config == null)
            {
            	throw new ApplicationException("Cannot get transport with no configuration. Please ensure that ConfigurationSystemClientConfig is properly defined.");
            }
			return new HttpTransport(config.RemoteHost, config.KeyProviderTypeName);
        }
    }
}
