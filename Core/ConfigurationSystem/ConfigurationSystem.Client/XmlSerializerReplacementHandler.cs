using System;
using System.Collections.Generic;
using System.Web;
using System.Xml.Serialization;
using System.Xml;
using System.Configuration;
using System.Xml.XPath;
using System.Threading;
using System.IO;
using System.Web.Configuration;
using System.Collections.Concurrent;
using MySpace.Configuration;

namespace MySpace.ConfigurationSystem
{

	/// <summary>
	/// Intended as a mostly drop-in replacement for XmlSerializerSectionHandler. Operates in the same fashion,
	/// except when registering for a reload you provide the name of the section rather than the type name of 
	/// the class that represents the section.
	/// </summary>
	public class XmlSerializerReplacementHandler : IConfigurationSectionHandler
	{
		private static readonly Logging.LogWrapper log = new Logging.LogWrapper();
		static readonly ReaderWriterLockSlim reloadDelegatesLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
		private static readonly Dictionary<string, List<EventHandler>> reloadDelegates = new Dictionary<string, List<EventHandler>>();
		static readonly ConfigurationClient.ConfigurationChangedEventHandler configurationChangedDelegate = ConfigurationSectionChanged;

		static XmlSerializerReplacementHandler()
		{
			ConfigurationClient.ConfigurationChangedEvent += configurationChangedDelegate;
		}

		/// <summary>
		/// Creates the configuration section using the configuraton server.
		/// </summary>
		public object Create(object parent, object configContext, XmlNode section)
		{
			if (section == null)
			{
				return null;
			}

			XmlNode sectionNode;

            string remoteSection = section.Name;
            string localSection = section.Name;

			if (!TryUseLocalFile(section, out sectionNode))
			{
                //see if the node contains remoteServerSection attribute
                XmlAttribute sectionAttribute = null;
				if(section.Attributes != null)
                    sectionAttribute = section.Attributes[ConfigurationLoader.REMOTE_SERVER_SECTION];
                
				if (sectionAttribute != null && string.IsNullOrWhiteSpace(sectionAttribute.Value) == false)
                {
                    remoteSection = sectionAttribute.Value;
                }
                
				sectionNode = ConfigurationClient.GetSectionXml(remoteSection);
			}

            RemoteSectionToLocalSection[remoteSection] = localSection;

			object config = GetConfigInstance(sectionNode);
			
			return config;
		}

        private static readonly ConcurrentDictionary<string, string> RemoteSectionToLocalSection = new ConcurrentDictionary<string, string>();

		#region Local File Handling

		private static readonly Dictionary<string, string> fileNameToSectionNameMapping = new Dictionary<string, string>();

		private static bool TryUseLocalFile(XmlNode section, out XmlNode config)
		{

			if (section == null || section.Attributes == null)
			{
				config = null;
				return false;
			}

			try
			{
				//first check to see if it was config sourced, for legacy config file support.
				System.Configuration.Configuration appConfig;

				//if the app is hosted you should be able to load a web.config.
				if (System.Web.Hosting.HostingEnvironment.IsHosted)
					appConfig = WebConfigurationManager.OpenWebConfiguration(HttpRuntime.AppDomainAppVirtualPath);
				else
					appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

				//SectionInformation info = config.GetSection(section.Name).SectionInformation;
				ConfigurationSection configSection = appConfig.GetSection(section.Name);

				string configSource = configSection.SectionInformation.ConfigSource;

				string fileName = null;

				if (!string.IsNullOrEmpty(configSource))
				{
					//then the config was specified as a config source. "section" contains the whole config, and we just need to set up the watcher for changes
					log.DebugFormat("Section {0} has configSource {1} specified, using that.", section.Name, configSource);
					fileName = configSource;
				}
				else
				{
					//then we might be using the new format, which allows for leaving the file name in place and simply toggling the boolean value. 
					//this is easier for deployments and testing.
					XmlAttribute localFileName;
					XmlAttribute useLocal = section.Attributes["useLocal"];
					if (useLocal != null && useLocal.Value.ToLowerInvariant() == "true")
					{
						localFileName = section.Attributes["localFileName"];
						if (localFileName != null)
						{
							fileName = localFileName.Value;
						}
						if (string.IsNullOrEmpty(fileName))
						{
							log.ErrorFormat("Tried to use local file for section {0}, but no file was specified. Use the attribute 'localFileName' to specify the local file for a config section.", section.Name);
						}
					}
				}

				if(!string.IsNullOrEmpty(fileName))
				{
					string filePath;
					if (!Path.IsPathRooted(fileName))
					{
						if (System.Web.Hosting.HostingEnvironment.IsHosted)
						{
							filePath = Path.Combine(HttpRuntime.AppDomainAppPath, fileName);
						}
						else
						{
							filePath = Path.GetFullPath(fileName);
						}
					}
					else
					{
						filePath = fileName;
					}

					if (!File.Exists(filePath))
					{
						log.ErrorFormat("Tried to use {0} for section {1}, but file did not exist. Will try remote.", filePath,
						                section.Name);
					}
					else
					{
						log.DebugFormat("Using local file {0} for configuration section {1}", filePath, section.Name);
						if (!fileNameToSectionNameMapping.ContainsKey(fileName.ToLowerInvariant()))
						{
							fileNameToSectionNameMapping.Add(fileName.ToLowerInvariant(), section.Name);
						}

						XmlDocument doc = new XmlDocument();
						doc.Load(filePath);
						config = doc.DocumentElement;
						ConfigurationWatcher.WatchFile(filePath, ReloadLocalConfig);
						return true;
					}
				}

				config = null;
				return false;
			}
			catch (Exception e)
			{
				log.ErrorFormat("Tried to use local file for section {0} but got an exception. Will try remote. Exception: {1}", section.Name, e);
				config = null;
				return false;
			}

		}
		
		private static void ReloadLocalConfig(string filePath)
		{
			try
			{
				if(string.IsNullOrEmpty(filePath))
				{
					log.ErrorFormat("Got config reload for null or empty path.");
					return;
				}
				
				XmlDocument doc = new XmlDocument();
				doc.Load(filePath);
				XmlNode configNode = doc.DocumentElement;
				
				if (configNode == null)
				{
					log.ErrorFormat("Document element for configuration file {0} was null during reload.", filePath);
					return;
				}

				string sectionName;
				string fileName = Path.GetFileName(filePath);
				if(string.IsNullOrEmpty(fileName))
				{
					log.ErrorFormat("Could not get file name from path {0} for config reload.", filePath);
					return;
				}
				if (!fileNameToSectionNameMapping.TryGetValue(fileName.ToLowerInvariant(), out sectionName))
				{
					log.ErrorFormat("Don't know setion name for file {0}, will try xml node name {1}", fileName, configNode.Name);
					sectionName = configNode.Name;
				}
				
				object configObject = GetConfigInstance(configNode);

				ConfigurationClient.RefreshSection(sectionName);
				
				ConfigurationItem dummyItem = new ConfigurationItem(sectionName, configNode.OuterXml);
				
				ConfigurationChangedEventArgs args = new ConfigurationChangedEventArgs(
				sectionName, dummyItem.SectionString, dummyItem.SectionStringBytes);

				ConfigurationSectionChanged(configObject, args);
			}
			catch (Exception e)
			{
				log.ErrorFormat("Error reloading config from file {0}: {1}", filePath, e);
			}
		}

		#endregion
		
		/// <summary>
		/// Creates a section from the identical XML used by XmlSerializationSectionHandler. The only difference is that the XML comes
		/// from the configuration system instead of the local file.
		/// </summary>
		/// <param name="section"></param>
		/// <returns></returns>
		private static object GetConfigInstance(XmlNode section)
		{
			if (section == null) return null;

			XPathNavigator nav = section.CreateNavigator();
			string typeName = (string)nav.Evaluate("string(@type)");

			if (typeName == null)
			{
				throw new ConfigurationErrorsException("Could not find type attribute on section {0}", section);
			}
			
			Type t = Type.GetType(typeName);

			if (t == null)
				throw new ConfigurationErrorsException("XmlSerializerSectionHandler failed to create type '" + typeName +
					"'.  Please ensure this is a valid type string.", section);

			bool configAndXmlAttributeMatch = false;

			try
			{
				XmlRootAttribute[] attributes = t.GetCustomAttributes(typeof(XmlRootAttribute), false) as XmlRootAttribute[];

				if (null == attributes || attributes.Length == 0)
				{
					log.ErrorFormat(
						@"Type ""{0}"" does not have an XmlRootAttribute applied. Please declare an XmlRootAttribute with the proper namespace ""{1}""",
						t.AssemblyQualifiedName, nav.NamespaceURI);
				}
				else
				{
					XmlRootAttribute attribute = attributes[0];

					//Only check for namespace compiance if both the config and the attribute have something for their namespace.
					if (!string.IsNullOrEmpty(attribute.Namespace) && !string.IsNullOrEmpty(nav.NamespaceURI))
					{
						if (!string.Equals(nav.NamespaceURI, attribute.Namespace, StringComparison.OrdinalIgnoreCase))
						{
							log.ErrorFormat(
								@"Type ""{0}"" has an XmlRootAttribute declaration with an incorrect namespace. The XmlRootAttribute specifies ""{1}"" for the namespace but the config uses ""{2}"" Please declare an XmlRootAttribute with the proper namespace ""{2}""",
								t.AssemblyQualifiedName, attribute.Namespace, nav.NamespaceURI);
						}
						else
							configAndXmlAttributeMatch = true;
					}
					else
						configAndXmlAttributeMatch = true;
				}
			}
			catch (Exception ex)
			{

				if (log.IsWarnEnabled)
				{
					log.WarnFormat("Exception thrown checking XmlRootAttribute's for \"{0}\". Config will still load normally...", t.AssemblyQualifiedName);
					log.Warn("Exception thrown checking XmlRootAttribute's", ex);
				}
			}

			System.Diagnostics.Stopwatch watch = null;
			try
			{
				if(log.IsDebugEnabled)
					watch = System.Diagnostics.Stopwatch.StartNew();
				XmlSerializer ser;
				if (configAndXmlAttributeMatch)
				{
					log.InfoFormat("Creating XmlSerializer for Type = \"{0}\", inferring namespace from Type", t.AssemblyQualifiedName);
					ser = new XmlSerializer(t);
				}
				else
				{
					log.InfoFormat("Creating XmlSerializer for Type = \"{0}\" with Namespace =\"{1}\"", t.AssemblyQualifiedName, nav.NamespaceURI);
					ser = new XmlSerializer(t, nav.NamespaceURI);
				}

				return ser.Deserialize(new XmlNodeReader(section));
			}
			catch (Exception ex)
			{
				if (ex.InnerException != null)
					throw ex.InnerException;
				throw;
			}
			finally
			{
				if (watch != null)
				{
					watch.Stop();
					log.DebugFormat("Took {0} to Create XmlSerializer and Deserialize for Type = \"{1}\"", watch.Elapsed,
						               t.AssemblyQualifiedName);
				}
			}
		}

		/// <summary>
		/// Sets <paramref name="handler"/> to be called if the section named <paramref name="sectionName"/> is updated.
		/// </summary>
		public static void RegisterReloadNotification(string sectionName, EventHandler handler)
		{
			reloadDelegatesLock.EnterWriteLock();
			try
			{
				List<EventHandler> eventHandlerList;
				if (reloadDelegates.TryGetValue(sectionName, out eventHandlerList))
				{
					if (!eventHandlerList.Contains(handler))
					{
						eventHandlerList.Add(handler);
					}
				}
				else
				{
					reloadDelegates.Add(sectionName, new List<EventHandler> { handler });
				}
			}
			finally
			{
				reloadDelegatesLock.ExitWriteLock();
			}
		}

		/// <summary>
		/// If you have registered for an event, you should call this to unregister when your 
		/// class is ready to be disposed, because otherwise a reference will be held to it by the
		/// delegate and it will never be fully garbage collected.
		/// </summary>
		public static void UnregisterReloadNotification(string sectionName, EventHandler handler)
		{
			reloadDelegatesLock.EnterWriteLock();
			try
			{
				List<EventHandler> eventHandlerList;
				if (reloadDelegates.TryGetValue(sectionName, out eventHandlerList) &&
					eventHandlerList != null)
				{
					eventHandlerList.Remove(handler);
				}
			}
			finally
			{
				reloadDelegatesLock.ExitWriteLock();
			}
		}

		internal static void ConfigurationSectionChanged(object sender, ConfigurationChangedEventArgs args)
		{
            string remoteSectionChanged = args.SectionName;
            string localSection = args.SectionName;

            if (RemoteSectionToLocalSection.ContainsKey(remoteSectionChanged))
            {
                localSection = RemoteSectionToLocalSection[remoteSectionChanged];
            }

			reloadDelegatesLock.EnterReadLock();
			try
			{
				List<EventHandler> handlerList;
                if (reloadDelegates.TryGetValue(localSection, out handlerList) && handlerList != null)
				{	
					object configurationSection;
					
					if (sender != null)
					{
						configurationSection = sender;
					}
					else
					{
						configurationSection = CreateConfigurationSection(args);
					}

					if (log.IsDebugEnabled)
					{
                        log.DebugFormat("Registered Handlers calling {0}", ConfigurationClient.BuildDelegateDescription(localSection, handlerList));
					}

					foreach (EventHandler handler in handlerList)
					{
						handler.Invoke(configurationSection, args);
					}
				}
				else
				{
                    log.DebugFormat("Section {0} changed but had no delegates to call.", localSection);
				}
			}
			finally
			{
				reloadDelegatesLock.ExitReadLock();
			}
		}

		private static object CreateConfigurationSection(ConfigurationChangedEventArgs args)
		{
			try
			{
				XmlDocument doc = ConfigurationClient.CreateXmlDocument(args.SectionStringBytes);
				return GetConfigInstance(doc.DocumentElement);
			}
			catch (Exception e)
			{
				log.ErrorFormat(
					"Got exception {0} loading xml for section {1} during reload handler. Xml data was {2}. Invalidating config.",
					e, args.SectionName, args.SectionString);
				ConfigurationClient.InvalidateSection(args.SectionName);
			}
			return null;
		}


		
	}
}

