
using System.Net;
using MySpace.Logging;

namespace MySpace.ConfigurationSystem.EnvironmentHelpers
{
	class DnsHelper
	{
		private static readonly LogWrapper log = new LogWrapper();

		public static IPAddress[] ResolveIPAddress(string addressString)
		{
			if (string.IsNullOrEmpty(addressString))
				return null;

			IPAddress ipaddr;
			if (IPAddress.TryParse(addressString, out ipaddr))
			{
				if (log.IsDebugEnabled)
					log.DebugFormat("ResolveIPAddress({0}) found IPAdress.", addressString);

				return new[] { ipaddr };
			}

			IPHostEntry address;

			try
			{
				address = Dns.GetHostEntry(addressString);

				if (address.AddressList.Length > 0)
				{
					if (log.IsDebugEnabled)
						log.DebugFormat("ResolveIPAddress({0}) found {1} ipaddresses.", addressString, address.AddressList.Length, address.AddressList[0].ToString());

					return address.AddressList;
				}
			}
			catch (System.Net.Sockets.SocketException sex)
			{
				log.WarnFormat("ResolveIPAddress({0}) could not resolve hostname/address ({1}).", addressString, sex.Message);
			}

			return null;

		}
	}
}
