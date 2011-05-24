using System;
using System.Collections.Generic;
using System.Xml;
using System.Net;
using System.Diagnostics;
using MySpace.Logging;

namespace MySpace.ConfigurationSystem.EnvironmentHelpers
{
	internal static class EndpointProcessor
	{
		private static readonly LogWrapper log = new LogWrapper();
		internal static void ProcessEndPointNode(object state)
		{
			HandleWithNode handle = state as HandleWithNode;
			XmlNode endpointNode = null;

			if (handle != null)
			{
				try
				{
					endpointNode = handle._node;
					EnvironmentInfo environment = handle._environment;

					if (endpointNode.Name == "endpoint")
					{
						IPAddress[] addresses = DnsHelper.ResolveIPAddress(endpointNode.Attributes.GetAttributeOrNull("address"));

						if (addresses != null)
						{
							lock (handle._endpointToEnvironmentMapping)
							{
								foreach (IPAddress addr in addresses)
								{
									AddAddressToMapping(addr, environment, handle._endpointToEnvironmentMapping);
								}
							}
						}
						else
							log.WarnFormat("Failed to get any address for 'endpoint' node {0} in {1}",
										   endpointNode.Attributes.GetAttributeOrNull("address"), environment.Name);
					}
					else if (endpointNode.Name == "directorygroup")
					{
						IPAddress[] addresses =
							DirectoryHelper.GetIPAddressesForDirectoryFilter(endpointNode.Attributes.GetAttributeOrNull("root"),
																			 endpointNode.Attributes.GetAttributeOrNull("filter"));
						if (addresses != null)
						{
							lock (handle._endpointToEnvironmentMapping)
							{
								foreach (IPAddress addr in addresses)
								{
									AddAddressToMapping(addr, environment, handle._endpointToEnvironmentMapping);
								}
							}
						}
						else
							log.WarnFormat(
								"Failed to get any address for 'directorygroup' node {0} in {1}",
								endpointNode.Attributes.GetAttributeOrNull("address"), environment.Name);
					}
					else if (endpointNode.Name == "vip")
					{
						VipHelper vipHelper = new VipHelper();
						IPAddress[] addresses =
							vipHelper.GetIPAddressesForVip(endpointNode.Attributes.GetAttributeOrNull("name"));
						if (addresses != null)
						{
							lock (handle._endpointToEnvironmentMapping)
							{
								foreach (IPAddress addr in addresses)
								{
									AddAddressToMapping(addr, environment, handle._endpointToEnvironmentMapping);
								}
							}
						}
						else
							log.WarnFormat(
								"Failed to get any address for 'vip' node {0} in {1}",
								endpointNode.Attributes.GetAttributeOrNull("name"), environment.Name);
					}
				}
				catch (Exception e)
				{
					log.ErrorFormat("Exception processing endpoint {0}: {1}", endpointNode, e);
				}
				finally
				{
					handle._handle.Decrement();
				}
			}
		}


		[DebuggerStepThrough]
		private static string GetAttributeOrNull(this XmlAttributeCollection collection, string attrName)
		{
			try
			{
				return collection[attrName].Value;
			}
			catch
			{
				return null;
			}
		}

		private static void AddAddressToMapping(IPAddress addr, EnvironmentInfo environment, Dictionary<IPAddress, EnvironmentInfo> mapping)
		{
			EnvironmentInfo existingEnvironment;
			if (!mapping.TryGetValue(addr, out existingEnvironment))
			{
				mapping[addr] = environment;
			}
			else
			{
				if (existingEnvironment.Name != environment.Name)
				{
					log.WarnFormat("Endpoint {0} is defined in both '{1}' and '{2}'. It will be treated as a member of '{1}'", addr, existingEnvironment.Name, environment.Name);
				}
			}
		}
	}
}
