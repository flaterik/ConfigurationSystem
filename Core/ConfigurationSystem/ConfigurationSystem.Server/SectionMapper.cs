using System;
using System.Collections.Generic;
using System.Net;
using System.Xml;
using System.IO;
using System.Diagnostics;
using System.Configuration;
using MySpace.Logging;
using System.Threading;
using MySpace.ConfigurationSystem.EnvironmentHelpers;
using System.Text;

namespace MySpace.ConfigurationSystem
{
	
	public static class SectionMapper
	{
		private static readonly LogWrapper log = new LogWrapper();
		private static readonly ConfigurationSystemServerConfig config = (ConfigurationSystemServerConfig)ConfigurationManager.GetSection("ConfigurationSystemServerConfig");
		private const int SECTION_MAPPING_LIFETIME = 60; // TTL before reload in seconds
		private static readonly object sectionMappingLock = new object();

		private static readonly Timer sectionMappingReloadTimer = new Timer(ReloadSectionMapper, null, Timeout.Infinite, Timeout.Infinite);

		private static MappingDetails mappingDetails = new MappingDetails();
		
		private static readonly AutoResetEvent lookupSynchronizerEvent = new AutoResetEvent(false);
		private static readonly Encoding encoding = new UTF8Encoding(false);

		static SectionMapper()
		{
			ReloadSectionMapper();
		}

		internal static bool IsItemValid(ConfigurationItem item)
		{
			string environment = item.Environment;
			bool encrypt;
			string path = GetPathForSectionAndEndpoint(item.Name, null, out encrypt, ref environment);
			if (string.IsNullOrEmpty(path))
				return false;
			return path == item.Src && item.Encrypt == encrypt;
		}

		public static string GetPathForSectionAndEndpoint(string sectionName, IPEndPoint endPoint, out bool encrypt, ref string environmentName)
		{
			if (mappingDetails._sectionMapping == null)
				throw new ApplicationException("No Sections Defined");
			if (mappingDetails._endpointToEnvironmentMapping == null)
				throw new ApplicationException("No Environments Defined");

			if (sectionName == null)
			{
				encrypt = false;
				return null;
			}

			EnvironmentInfo environment;
			SectionInfo section;
			string path;

			if (!mappingDetails._sectionMapping.TryGetValue(sectionName, out section))
			{
				encrypt = false;
				return null;
			}

			encrypt = section.Encrypt;

			//first, attempt to see if this endpoint has a defined environment
			if (environmentName == null && endPoint != null && mappingDetails._endpointToEnvironmentMapping.TryGetValue(endPoint.Address, out environment))
			{
				if (!section.Exceptions.TryGetValue(environment.Name, out path))
					path = section.DefaultSrc;
			}
			else
			{
				//no environment defined for this endpoint, use the specified environment or default
				if (environmentName != null)
				{
					if (mappingDetails._environmentMapping.TryGetValue(environmentName, out environment))
					{
						if(false == section.Exceptions.TryGetValue(environmentName, out path))
							path = section.DefaultSrc;
					} 
					else 
					{
						throw new ArgumentException(string.Format("requested environment '{0}' does not exist. env parameter is case sensitve.", environmentName));
					}
				}
				else
				{
					if (mappingDetails._defaultEnvironment == null)
					{
						log.WarnFormat("SectionMapper.GetPathForSectionAndEndpoint({0}, {1}) no mapping and NO default environment in config.", section, endPoint);
						return null;
					}
					path = section.DefaultSrc;
					environment = mappingDetails._defaultEnvironment;
				}

			}

			environmentName = environment.Name;

			string slowRollSrc = IsParticipatingInSlowRoll(environment, section, endPoint);
			
			if (slowRollSrc == null)
				return ReplacePathTokens(path, environment.Name);
			
			return ReplacePathTokens(slowRollSrc, environment.Name);

		}

		private static readonly Random rand = new Random();
		private static string IsParticipatingInSlowRoll(EnvironmentInfo environment, SectionInfo section, IPEndPoint endPoint)
		{
			if (section.SlowRolls == null || section.SlowRolls.Count <= 0 || endPoint == null)
				return null;

			foreach (SlowRollInfo slowroll in section.SlowRolls.Values)
			{
				if (slowroll.Environment == environment.Name &&
					slowroll.Start <= DateTime.Now)
				{
					if (slowroll.End <= DateTime.Now)
						return slowroll.Src; // Slow roll complete, all hosts now recieve this value

					// Is this host already assigned the slowroll src?
					string lastSrc = HttpServer.ClientStats.GetLastServedSrcForClient(environment.Name, section.Name, endPoint.Address);
					if (lastSrc != null)
						return lastSrc;

					// Otherwise, Is this host randomly elected to participate
					double pct = ((DateTime.Now - slowroll.Start).Ticks) / (double)((slowroll.End - slowroll.Start).Ticks);
					if (rand.NextDouble() < pct)
						return slowroll.Src;
				}
			}

			return null;
		}

		private static string ReplacePathTokens(string path, string environment)
		{
			if (path == null)
				return null;

			return path.Replace("{ENVIRONMENT}", environment);
		}

		/// <summary>
		/// Purge current cache, then reload mapping
		/// </summary>
		public static void Purge()
		{
			LocalCache.Purge();
			ReloadSectionMapper();
		}
		
		public static void LoadLocalCache()
		{
			if (mappingDetails._sectionMapping != null)
			{
				try
				{
					IPAddress[] myAddresses = Dns.GetHostAddresses(Dns.GetHostName());
					IPEndPoint ep = new IPEndPoint(myAddresses[0], ConfigurationSystemServerConfig.DefaultPort);
					foreach (KeyValuePair<string, SectionInfo> section in mappingDetails._sectionMapping)
					{
						SectionInfo Section = section.Value;
						string environment = null;

						GetConfigurationItem(Section.Name, ep, ref environment);
					}
				}
				catch (Exception ex)
				{
					log.Error(ex);
				}
				
			}
		}

		internal static ConfigurationItem GetConfigurationItem(string sectionName, EndPoint requestor, ref string environmentName)
		{
			bool encrypt;
			string itemPath = GetPathForSectionAndEndpoint(sectionName, requestor as IPEndPoint, out encrypt, ref environmentName);
			if (itemPath == null)
			{
				log.WarnFormat("No item path found for section {0} and requestor {1}", sectionName, requestor);
				return null;
			}

			IConfigurationSystemSectionProvider provider = GetProviderForSection(sectionName);

			if (provider == null)
			{
				log.WarnFormat("No provider found for section {0} and requestor {1}", sectionName, requestor);
				return null;
			}

			ConfigurationItem item = LocalCache.Get(sectionName, itemPath, environmentName, encrypt, provider);

			if (item == null)
			{
				log.WarnFormat("No ConfigurationItem returned ", sectionName, requestor.ToString());
				return null;
			}

			item.IsGeneric = GetGenericValueForSection(sectionName);

			return item;
		}

		public static IConfigurationSystemSectionProvider GetProviderForSection(string section)
		{
			if (string.IsNullOrEmpty(section) || mappingDetails._sectionMapping == null)
				return null;
			
			SectionInfo sectionInfo;

			if (mappingDetails._sectionMapping.TryGetValue(section, out sectionInfo))
			{
				return sectionInfo.ConfigurationSystemSectionProvider;
			}

			return null;
		}

		public static bool GetGenericValueForSection(string section)
		{
			if (string.IsNullOrEmpty(section) || mappingDetails._sectionMapping == null)
				return false;

			SectionInfo sectionInfo;

			if (mappingDetails._sectionMapping.TryGetValue(section, out sectionInfo))
			{
				return sectionInfo.IsGeneric;
			}

			return false;
		}


		private static Dictionary<string, SectionInfo> BuildSectionMapping(XmlDocument sourceDoc, string defaultEnvironmentName)
		{
			Dictionary<string, SectionInfo> newSectionMapping =
				new Dictionary<string, SectionInfo>(StringComparer.InvariantCultureIgnoreCase);
			
			foreach (XmlNode sectionNode in sourceDoc.GetElementsByTagName("section"))
			{
				string providerName = sectionNode.Attributes.GetAttributeOrNull("provider");
				if (providerName == null)
				{
					//"providers" were inconsistently named earlier and were sometimes called "handlers". 
					//This is confusing because they're not section handlers. This is to support old format configurations.
					providerName = sectionNode.Attributes.GetAttributeOrNull("handler"); 
				}
					
				SectionInfo info = new SectionInfo
									{
										Name = sectionNode.Attributes.GetAttributeOrNull("name"),
										ConfigurationSystemSectionProvider =
											Providers.GetProviderByName(providerName),
										DefaultSrc = sectionNode.Attributes.GetAttributeOrNull("src"),
										IsGeneric = sectionNode.Attributes.GetAttributeOrFalse("generic"),
										Encrypt =  sectionNode.Attributes.GetAttributeOrFalse("encrypt")
									};
				
				foreach (XmlNode exceptionNode in sectionNode.ChildNodes)
				{
					if (exceptionNode.Name == "exception")
					{
						string environment = exceptionNode.Attributes.GetAttributeOrNull("environment");
						string src = exceptionNode.Attributes.GetAttributeOrNull("src");
						
						if (environment != null && src != null)
						{
							info.Exceptions[environment] = src;
						}
						else
						{
							log.WarnFormat("SectionMapper.ReloadSectionMapper(): failed to load environment or src from exception element in {0} {1} {2}", info.Name, environment, src);
						}
					}
					else if (exceptionNode.Name == "slowroll")
					{
						string environment = exceptionNode.Attributes.GetAttributeOrNull("environment");
						string src = exceptionNode.Attributes.GetAttributeOrNull("src");
						string startString = exceptionNode.Attributes.GetAttributeOrNull("start");
						string endString = exceptionNode.Attributes.GetAttributeOrNull("end");

						if (environment == null)
							environment = defaultEnvironmentName;

						DateTime start, end;

						if (startString == null || !DateTime.TryParse(startString, out start))
						{
							log.WarnFormat("SectionMapper.ReloadSectionMapper(): failed to load START date in slowroll tag name={0} env={1}", info.Name, environment);
							start = DateTime.Now;
						}

						if (endString == null || !DateTime.TryParse(endString, out end))
						{
							log.WarnFormat("SectionMapper.ReloadSectionMapper(): failed to load END date in slowroll tag name={0} env={1}", info.Name, environment);
							end = DateTime.Now;
						}

						SlowRollInfo slowroll = new SlowRollInfo
						{
							Environment = environment,
							Src = src,
							Start = start,
							End = end
						};

						info.SlowRolls[environment] = slowroll;
					}
				}

				newSectionMapping[info.Name] = info;
			}

			return newSectionMapping;
		}

		private static void ReloadSectionMapper(object state) //just to support the timer
		{
			try
			{
				log.DebugFormat("SectionMapper beginning reload.");
				ReloadSectionMapper();
				log.DebugFormat("Beginning local cache update.");
				LocalCache.Update();
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception reloading section mapper: {0}", e);
			}
			
		}

		private static void ReloadSectionMapper()
		{
			sectionMappingReloadTimer.Change(Timeout.Infinite, Timeout.Infinite);
			lock (sectionMappingLock)
			{
				MappingDetails savedMappingDetails = mappingDetails;

				try
				{

					Dictionary<IPAddress, EnvironmentInfo> newEndpointToEnvironmentMapping = new Dictionary<IPAddress, EnvironmentInfo>();
					Dictionary<string, EnvironmentInfo> newEnvironmentMapping = new Dictionary<string, EnvironmentInfo>(StringComparer.InvariantCultureIgnoreCase);
					EnvironmentInfo newDefaultEnvironment = null;
					string newDefaultEnvironmentName = null;
					
					string newMapping = GetCurrentMappingXml();
					if (newMapping == null)
					{
						log.ErrorFormat("Could not reload mapping because no mapping was found!");
						return;
					}
					int newMappingHash = newMapping.GetHashCode();
					if (newMappingHash == mappingDetails._sectionMappingHash)
					{
						log.DebugFormat("Section mapping hash ({0}) has not changed. Skipping reload.", newMappingHash);
						return;
					}
					else
					{
						log.InfoFormat("Section mapping changed, reloading");
					}
					
					XmlDocument mappingDoc = new XmlDocument();
					
					mappingDoc.LoadXml(newMapping);

					int environmentEntries = 0;
					foreach (XmlNode environmentNode in mappingDoc.GetElementsByTagName("environment"))
					{
						EnvironmentInfo environment = new EnvironmentInfo(environmentNode.Attributes.GetAttributeOrNull("name"));
						bool isDefault = environmentNode.Attributes.GetAttributeOrTrue("default");

						if (environmentNode.ChildNodes.Count > 0)
						{
							HandleWithCount lookupSynchronizer = new HandleWithCount(lookupSynchronizerEvent,
																					 environmentNode.ChildNodes.Count);

							foreach (XmlNode endpointNode in environmentNode.ChildNodes)
							{
								ThreadPool.QueueUserWorkItem(EndpointProcessor.ProcessEndPointNode,
																   new HandleWithNode(lookupSynchronizer, endpointNode, environment, newEndpointToEnvironmentMapping));
							}

							lookupSynchronizerEvent.WaitOne();
						}
						if (isDefault)
						{
							newDefaultEnvironment = environment;
							newDefaultEnvironmentName = environment.Name;
						}


						newEnvironmentMapping[environment.Name] = environment;

						environmentEntries++;

					}

					Dictionary<string, SectionInfo> newSectionMapping = BuildSectionMapping(mappingDoc, newDefaultEnvironmentName);
					
					mappingDetails = new MappingDetails(newMappingHash, newSectionMapping, newDefaultEnvironment, newEndpointToEnvironmentMapping, newEnvironmentMapping);
					
					log.DebugFormat("SectionMapper reload complete {0} sections {1} environments.",
									mappingDetails._sectionMapping.Count, environmentEntries);

				}
				catch (Exception ex)
				{
					log.ErrorFormat("Error loading mapper xml: {0}", ex);
					mappingDetails = savedMappingDetails;
				}
				finally
				{
					sectionMappingReloadTimer.Change(SECTION_MAPPING_LIFETIME*1000, SECTION_MAPPING_LIFETIME*1000);
				}
			}
		}

		

		

	
		internal static string GetCurrentMappingXml()
		{
			string mappingConfigLocation = config.MappingConfigPath;
			string mappingConfigProvider = config.MappingConfigProvider;

			IConfigurationSystemSectionProvider mappingProvider = Providers.GetProviderByName(mappingConfigProvider);
			
			byte[] mappingBytes;
			string errorMessage = String.Empty;
			string mappingXml;
			
			if(mappingProvider != null && mappingProvider.TryGetDataBytes(mappingConfigLocation, out mappingBytes, out errorMessage))
			{
				mappingXml = encoding.GetString(mappingBytes);
				BackUpMappingXml(mappingXml);
			}
			else
			{
				if (mappingProvider == null)
				{
					errorMessage = string.Format("No provider \"{0}\" available", mappingConfigProvider);
				}
				
				mappingXml = GetMappingXmlBackup();
				if(string.IsNullOrEmpty(mappingXml))
					log.ErrorFormat("Could not get mapping from {0}:{1}, with error {2}, and backup is not available.", mappingConfigProvider, mappingConfigLocation, errorMessage);
				else
					log.WarnFormat("Could not get mapping from {0}:{1}, with error {2}. Using backup.", mappingConfigProvider, mappingConfigLocation, errorMessage);
			}

			return mappingXml;
		}

		private static readonly string mappingXmlFileName = "MappingXml.backup." + HttpServer.AssemblyVersion + ".xml";
		private static void BackUpMappingXml(string mappingXml)
		{
			try
			{
				StreamWriter backupFileStreamWriter = new StreamWriter(mappingXmlFileName, false);
				backupFileStreamWriter.Write(mappingXml);
				backupFileStreamWriter.Dispose();
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception backing up mapping xml: {0}", e);
			}
		}

		private static string GetMappingXmlBackup()
		{
			try
			{
				if (File.Exists(mappingXmlFileName))
				{
					StreamReader backupFileStreamReader = new StreamReader(mappingXmlFileName);
					return backupFileStreamReader.ReadToEnd();
				}
				return null;
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception reading back up mapping xml: {0}", e);
				return null;
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

		[DebuggerStepThrough]
		private static bool GetAttributeOrTrue(this XmlAttributeCollection collection, string attrName)
		{
			try
			{
				string val = collection[attrName].Value;
				return bool.Parse(val);
			}
			catch
			{
				return true;
			}
		}
		[DebuggerStepThrough]
		private static bool GetAttributeOrFalse(this XmlAttributeCollection collection, string attrName)
		{
			try
			{
				bool value;
				string val = collection[attrName].Value;
				bool.TryParse(val, out value);
				return value;
			}
			catch
			{
				return false;
			}
		}


		
	}

	public class SectionInfo
	{
		public SectionInfo()
		{
			Exceptions = new Dictionary<string, string>();
			SlowRolls = new Dictionary<string, SlowRollInfo>();
		}
		public string Name { get; set; }
		public IConfigurationSystemSectionProvider ConfigurationSystemSectionProvider { get; set; }
		public string DefaultSrc { get; set; }
		public bool IsGeneric { get; set; }
		public Dictionary<string, string> Exceptions { get; set; }
		public Dictionary<string, SlowRollInfo> SlowRolls { get; set; }
		public int TimeToLive { get; set; }
		public bool Encrypt { get; set; }
	}

	public class EnvironmentInfo
	{
		public EnvironmentInfo(string name)
		{
			Name = name;
			Endpoints = new Dictionary<IPAddress, string>();
		}

		public string Name { get; set; }
		public bool IsDefault { get; set; }
		public Dictionary<IPAddress, string> Endpoints { get; set; }
	}

	public class SlowRollInfo
	{
		public string Environment { get; set; }
		public string Src { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
	}

	internal static class EndpointProcessor
	{
		private static readonly LogWrapper log = new LogWrapper();
		internal static void ProcessEndPointNode(object state)
		{
			HandleWithNode handle = state as HandleWithNode;
			XmlNode endpointNode =null;

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
				if(existingEnvironment.Name != environment.Name)
				{
					log.WarnFormat("Endpoint {0} is defined in both '{1}' and '{2}'. It will be treated as a member of '{1}'", addr, existingEnvironment.Name, environment.Name);	
				}
			}
		}
	}

	internal class HandleWithNode
	{
		internal HandleWithCount _handle;
		internal XmlNode _node;
		internal EnvironmentInfo _environment;
		internal Dictionary<IPAddress, EnvironmentInfo> _endpointToEnvironmentMapping;

		internal HandleWithNode(HandleWithCount handle, XmlNode node, EnvironmentInfo environment, Dictionary<IPAddress, EnvironmentInfo> endpointToEnvironmentMapping)
		{
			_handle = handle;
			_node = node;
			_environment = environment;
			_endpointToEnvironmentMapping = endpointToEnvironmentMapping;
		}
	}

	/// <summary>
	/// This is a container class so that all of the various settings can be swapped out atomically
	/// </summary>
	internal class MappingDetails
	{
		internal MappingDetails()
		{
		}

		internal MappingDetails(
			int sectionMappingHash,
			Dictionary<string, SectionInfo> sectionMapping,
			EnvironmentInfo defaultEnvironment,
			Dictionary<IPAddress, EnvironmentInfo> endpointToEnvironmentMapping,
			Dictionary<string, EnvironmentInfo> environmentMapping
			)
		{
			_sectionMappingHash = sectionMappingHash;
			_sectionMapping = sectionMapping;
			_defaultEnvironment = defaultEnvironment;
			_endpointToEnvironmentMapping = endpointToEnvironmentMapping;
			_environmentMapping = environmentMapping;
		}

		internal readonly int _sectionMappingHash;
		internal readonly Dictionary<string, SectionInfo> _sectionMapping;
		internal readonly EnvironmentInfo _defaultEnvironment;
		internal readonly Dictionary<IPAddress, EnvironmentInfo> _endpointToEnvironmentMapping;
		internal readonly Dictionary<string, EnvironmentInfo> _environmentMapping;
	}

}
