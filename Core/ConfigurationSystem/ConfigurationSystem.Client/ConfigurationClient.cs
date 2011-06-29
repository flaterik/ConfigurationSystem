using System;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml.Serialization;

namespace MySpace.ConfigurationSystem
{
	
	/// <summary>
	/// A client to the MySpace configuration system servers. Provides fetching, local caching, and notification of
	/// updates to configuration sections.
	/// </summary>
	public static class ConfigurationClient
	{
		/// <summary>
		/// A delegate for handling changes in a configuration section.
		/// </summary>
		public delegate void ConfigurationChangedEventHandler(object sender, ConfigurationChangedEventArgs args);
		/// <summary>
		/// A event that fires when a configuration section has changed.
		/// </summary>
		public static event ConfigurationChangedEventHandler ConfigurationChangedEvent;
		
		private static readonly ConfigurationSystemClientConfig config;
		static readonly ConfigurationItemStore store = new ConfigurationItemStore();
		private static readonly IConfigurationClientTransport transport;
		private static readonly Timer sectionCheckTimer;
		static readonly Logging.LogWrapper log = new Logging.LogWrapper();
		
		static readonly ReaderWriterLockSlim reloadDelegatesLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
		private static readonly Dictionary<string, List<ConfigurationChangedEventHandler>> reloadDelegates = new Dictionary<string, List<ConfigurationChangedEventHandler>>();
		
		private static readonly Type t_cachedPathData;
		private static readonly MethodInfo mi_getApplicationPathData;
		static ConfigurationClient()
		{
			try
			{
				config = ConfigurationManager.GetSection("ConfigurationSystemClientConfig") as ConfigurationSystemClientConfig;
				if (config == null)
				{
					config = new ConfigurationSystemClientConfig();
					log.InfoFormat("No Configuration Client configuration found; using default server '{0}'", config.RemoteHost);
				}
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception reading configuration client configuration: {0}. ", e.Message);
				throw;
			}

			try
			{
				t_cachedPathData = Type.GetType("System.Web.CachedPathData, System.Web, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

				if (t_cachedPathData == null)
				{
					log.ErrorFormat("Will not be able to force section refreshes: Could not get Type object for CachedPathData");
				}
				else
				{
					mi_getApplicationPathData = t_cachedPathData.GetMethod("GetApplicationPathData", BindingFlags.Static | BindingFlags.NonPublic);
					if (mi_getApplicationPathData == null)
					{
						log.ErrorFormat("Will not be able to force section refreshes: Could not get MethodInfo for GetApplicationPathData");
					}
				}
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception initializing section refresh reflection information: {0}", e);
			}
			

			transport = ConfigurationClientTransportFactory.GetTransport(config);
			sectionCheckTimer = new Timer(CheckSections, null, 0, config.SectionCheckIntervalSeconds * 1000);
		}


		/// <summary>
		/// Gets the string for <paramref name="sectionName"/> from the configuration server
		/// </summary>
		public static string GetSectionString(string sectionName)
		{
			ConfigurationItem sectionItem = GetSectionItem(sectionName);

			if (sectionItem != null)
			{
				return sectionItem.SectionString;
			}
			return null;
		}

		/// <summary>
		/// Gets the string for <paramref name="sectionName"/> from the configuration server and
		/// sets <paramref name="eventHandler"/> to be called if the section is updated.
		/// </summary>
		public static string GetSectionString(string sectionName, ConfigurationChangedEventHandler eventHandler)
		{
			RegisterReloadNotification(sectionName, eventHandler);
			return GetSectionString(sectionName);
		}
		
		/// <summary>
		/// Gets an XmlNode representing the <paramref name="sectionName"/> from the configuration server.
		/// </summary>
		public static XmlNode GetSectionXml(string sectionName)
		{
			ConfigurationItem item = GetSectionItem(sectionName);
			if (item == null)
				return null;

			XmlDocument doc;

			try
			{
				doc = CreateXmlDocument(item.SectionStringBytes);
				return doc.DocumentElement;
			}
			catch (Exception e)
			{
				log.ErrorFormat(
					"Got exception {0} loading xml for section {1}. Xml data was {2}. Invalidating config and attempting retry.",
					e, sectionName, item.SectionString);

				try
				{
					InvalidateSection(sectionName);
					item = GetSectionItem(sectionName);
					doc = CreateXmlDocument(item.SectionStringBytes);
					return doc.DocumentElement;
				}
				catch (Exception e2)
				{
					log.ErrorFormat(
						"Got exception {0} on second attempt to load xml for section {1}. Xml data was {2}. Throwing exception.",
						e2, sectionName, item.SectionString);
					throw new ConfigurationSystemException(sectionName,
														   string.Format(
															"Section failed xml load with exception {0}, even after local invalidation and server refetch",
															e2.Message), e2);
				}

			}
		}

		/// <summary>
		/// Gets an XmlReader representing the <paramref name="sectionName"/> from the configuration server.
		/// </summary>
		public static XmlReader GetSectionXmlReader(string sectionName)
		{
			ConfigurationItem item = GetSectionItem(sectionName);
			if (item == null)
				return null;

			XmlReader reader;

			try
			{
				reader = CreateXmlReader(item.SectionStringBytes);
				return reader;
			}
			catch (Exception e)
			{
				log.ErrorFormat(
					"Got exception {0} creating xml reader for section {1}. Xml data was {2}. Invalidating config and attempting retry.",
					e, sectionName, item.SectionString);

				try
				{
					InvalidateSection(sectionName);
					item = GetSectionItem(sectionName);
					reader = CreateXmlReader(item.SectionStringBytes);
					return reader;
				}
				catch (Exception e2)
				{
					log.ErrorFormat(
						"Got exception {0} on second attempt to create xml reader for section {1}. Xml data was {2}. Throwing exception.",
						e2, sectionName, item.SectionString);
					throw new ConfigurationSystemException(sectionName,
														   string.Format(
															"Section failed xml reader creation with exception {0}, even after local invalidation and server refetch",
															e2.Message), e2);
				}

			}
		}

		internal static XmlDocument CreateXmlDocument(byte[] sectionStringBytes)
		{
			//TODO: synchronize access to bytes?
			//TODO: use xmlreader instead and return that for xmlnode method?
			//loading from a stream with .load is much less prone to error than reading from a string with .loadxml
			//(it has to do with text encoding and bad assumptions made by xmldocument.loadxml)
			//this has also proven to produce different results than xmlreader, especially when deserializing. removing usage
			//of XmlDocument would probably be a good idea
			XmlDocument doc = new XmlDocument();
			doc.Load(new MemoryStream(sectionStringBytes));
			return doc;
		}

		internal static XmlReader CreateXmlReader(byte[] sectionStringBytes)
		{
			return XmlReader.Create(new MemoryStream(sectionStringBytes));
		}

		/// <summary>
		/// Removes <paramref name="sectionName"/> from the local cache and forces a get from the server 
		/// on the next request. Use this as a callback if your application detects a problem with your config.
		/// </summary>
		/// <param name="sectionName"></param>
		public static void InvalidateSection(string sectionName)
		{
			store.Invalidate(sectionName);
		}

		/// <summary>
		/// Retrieves the setting <paramref name="keyName"/> from the generic section <paramref name="sectionName"/>
		/// </summary>
		public static string GetGenericSetting(string sectionName, string keyName)
		{
			ConfigurationItem item = GetSectionItem(sectionName);
			string value;
			if (item.GenericSettingCollection.TryGetValue(keyName, out value))
			{
				return value;
			}
			return null;
		}

		/// <summary>
		/// Sets <paramref name="handler"/> to be called if the section named <paramref name="sectionName"/> is updated.
		/// </summary>
		public static void RegisterReloadNotification(string sectionName, ConfigurationChangedEventHandler handler)
		{
			reloadDelegatesLock.EnterWriteLock();
			try
			{
				List<ConfigurationChangedEventHandler> eventHandlerList;
				if (reloadDelegates.TryGetValue(sectionName, out eventHandlerList))
				{
					if (!eventHandlerList.Contains(handler))
					{
						eventHandlerList.Add(handler);
					}
				}
				else
				{
					reloadDelegates.Add(sectionName, new List<ConfigurationChangedEventHandler> { handler });
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
		public static void UnregisterReloadNotification(string sectionName, ConfigurationChangedEventHandler handler)
		{
			reloadDelegatesLock.EnterWriteLock();
			try
			{
				List<ConfigurationChangedEventHandler> eventHandlerList;
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

		/// <summary>
		/// This forces a refresh of section <paramref name="sectionName"/>, even if running in a web context.
		/// </summary>
		/// <remarks>The .net framework does nothing when ConfigurationManager.RefreshSection is called, and instead assumes
		/// that the file must have changed and triggered an internal refresh. Since our file does not change, that functionality
		/// does not work for us. Instead we reflect into the internal classes and methods that accomplish the refresh manually.
		/// </remarks>
		public static void RefreshSection(string sectionName)
		{
			ConfigurationManager.RefreshSection(sectionName);

			if (System.Web.Hosting.HostingEnvironment.IsHosted) //then we're running as a web site
			{
				//in this case, configurationmanager.refreshsection does NOTHING
				//need to dig in, get the System.Configuration.BaseConfigurationRecord, 
				//and call hlClearResultRecurive, which will cause our Create method to be called again
				try
				{
					if (mi_getApplicationPathData == null)
					{
						log.ErrorFormat("Could not force refresh of section {0}: Could not get MethodInfo for GetConfigurationPathData", sectionName);
						return;
					}

					object cachedPathData = mi_getApplicationPathData.Invoke(null, null);

					if (cachedPathData == null)
					{
						log.ErrorFormat("Could not force refresh of section {0}: Could not get CachedPathData object", sectionName);
						return;
					}


					MethodInfo mi_configRecord = t_cachedPathData.GetMethod("get_ConfigRecord",
																			BindingFlags.Instance | BindingFlags.NonPublic);

					if (mi_configRecord == null)
					{
						log.ErrorFormat("Could not force refresh of section {0}: Could not get MethodInfo for get_ConfigRecord", sectionName);
						return;
					}

					object configRecord = mi_configRecord.Invoke(cachedPathData, null);

					if (configRecord == null)
					{
						log.ErrorFormat("Could not force refresh of section {0}: Could not get ConfigRecord object", sectionName);
						return;
					}

					Type t_baseConfigRecord = configRecord.GetType().BaseType;

					if (t_baseConfigRecord == null)
					{
						log.ErrorFormat("Could not force refresh of section {0}: Could not get ConfigRecord object base type", sectionName);
						return;
					}

					MethodInfo mi_clearResult = t_baseConfigRecord.GetMethod("hlClearResultRecursive",
																			 BindingFlags.Instance | BindingFlags.NonPublic);

					if (mi_clearResult == null)
					{
						log.ErrorFormat("Could not force refresh of section {0}: Could not get MethodInfo for hlClearResultRecursive", sectionName);
						return;
					}

					object[] invokeArgs = new object[2];
					invokeArgs[0] = sectionName;
					invokeArgs[1] = false;
					mi_clearResult.Invoke(configRecord, invokeArgs);
				}
				catch (TargetInvocationException tie)
				{
					ConfigurationErrorsException cee = tie.InnerException as ConfigurationErrorsException;
					if (cee != null)
					{
						ArgumentNullException ane = cee.InnerException as ArgumentNullException;
						if (ane != null && ane.ParamName == "str")
						{
							//this can happen when a config section that doesn't exist is refreshed. configuration client
							//can access config 'sections' that are totally unknown to configuration manager, so this is OK to ignore.
							log.DebugFormat("Argument 'str' null exception when forcing refresh of section {0}. Ignoring because the section likely does not exist.", sectionName);
						}
						else
						{
							log.ErrorFormat("Exception forcing refresh of section {0}: {1}", sectionName, cee);
						}
					}
					else
					{
						log.ErrorFormat("Exception forcing refresh of section {0}: {1}", sectionName, tie);
					}
				}
				catch(Exception e)
				{
					log.ErrorFormat("Exception forcing refresh of section {0}: {1}", sectionName, e);
				}
			}
		}

		private static ConfigurationItem GetSectionItem(string sectionName)
		{
			ConfigurationItem sectionItem;
			
			//see if we have a local copy
			ConfigurationItem localSectionItem = GetLocalSectionItem(sectionName);
			
			if (localSectionItem != null)
			{
				if (localSectionItem.NeverChecked)
				{
					log.DebugFormat("Checking section item {0} found in local storage last modified for update because it has not been checked yet", sectionName);
					CheckAndUpdateSectionItem(ref localSectionItem);
					sectionItem = localSectionItem;
				}
				else
				{
					if (config.CheckOnGet)
					{
						if (localSectionItem.CheckedWithinInterval(config.SectionCheckIntervalSeconds))
						{
							if (log.IsDebugEnabled)
							{
								log.DebugFormat("Using section item {0} found in local storage last modified {1}", sectionName,
								                localSectionItem.LastChecked);
							}
							sectionItem = localSectionItem;
						}
						else
						{
							if (log.IsDebugEnabled)
							{
								log.DebugFormat("Checking section item {0} found in local storage last modified {1} for update.", sectionName,
								                localSectionItem.LastChecked);
							}
							CheckAndUpdateSectionItem(ref localSectionItem);

							sectionItem = localSectionItem;
						}
					}
					else
					{
						if (log.IsDebugEnabled)
							log.DebugFormat("Using local storage for item {0} because CheckOnGet is false.", sectionName);
						sectionItem = localSectionItem;
					}
				}

			}
			else //otherwise get it remotely
			{
				string sectionString;
				string generic;
				bool genericBool;
				try
				{
					if (log.IsDebugEnabled)
					{
						log.DebugFormat("Section item {0} not found in local storage, attempting remote retrieval.", sectionName);
					}
					GetRemoteSectionStringWasNew(sectionName, null, out sectionString, out generic);
				}
				catch (ConfigurationSystemException cse)
				{
					throw new ConfigurationSystemException(sectionName, string.Format("Failed to retrieve with message \"{0}\" and no local copy found.", cse.ErrorMessage), cse);
				}
				
				sectionItem = new ConfigurationItem(sectionName, sectionString);
				sectionItem.UpdateLastChecked();

				if (bool.TryParse(generic, out genericBool))
				{
					ConfigurationClientParser.PopulateGenericSettingCollection(ref sectionItem);
				}
				
				StoreSectionItem(sectionItem);
			}
			
			return sectionItem;            
		}

		private static ConfigurationItem GetLocalSectionItem(string sectionName)
		{
			ConfigurationItem item;
			store.TryGet(sectionName, out item);
			return item;
		}

		private static void StoreSectionItem(ConfigurationItem sectionItem)
		{
			store.Add(sectionItem);
		}

		private static bool CheckAndUpdateSectionItem(ref ConfigurationItem localSectionItem)
		{
			try
			{
				string remoteSectionString;
				string generic;
				bool genericBool;
				if (GetRemoteSectionStringWasNew(localSectionItem.SectionName, localSectionItem.SectionHashString, out remoteSectionString, out generic))
				{ 
					log.InfoFormat("Configuration section {0} had updated information", localSectionItem.SectionName);
					//then the supplied hash didn't match, and the remote sectionstring var is filled
					localSectionItem.SectionString = remoteSectionString;
					//update and parse out the appsettings
					if (bool.TryParse(generic, out genericBool))
					{
						ConfigurationClientParser.PopulateGenericSettingCollection(ref localSectionItem);
					}				
					localSectionItem.UpdateLastChecked();
					StoreSectionItem(localSectionItem);
					return true;
				}

				if (bool.TryParse(generic, out genericBool) && localSectionItem.GenericSettingCollection.Count == 0)
				{
					ConfigurationClientParser.PopulateGenericSettingCollection(ref localSectionItem);
				}
				
				localSectionItem.UpdateLastChecked();
				return false;
			}
			catch (Exception e)
			{
				bool rethrow = false;
				switch (config.FailStrategy)
				{
					case FailStrategy.ThrowException:
						if (log.IsErrorEnabled)
						{
							ConfigurationSystemException cse = e as ConfigurationSystemException;
							if (cse != null)
							{
								log.ErrorFormat("Failed to retreive section {0} with message \"{1}\". Per FailStrategy, rethrowing.", cse.SectionName, cse.ErrorMessage);
							}
							else
							{
								log.ErrorFormat("Failed to retreive section {0} with exception {1}. Per FailStrategy, rethrowing.", localSectionItem.SectionName, e);
							}
						}
						rethrow = true;
						break;
					case FailStrategy.UseLocal:
						if (log.IsErrorEnabled)
						{
							ConfigurationSystemException cse = e as ConfigurationSystemException;
							if (cse != null)
							{
								log.ErrorFormat("Failed to retreive section {0} with message \"{1}\". Per FailStrategy, using local copy.", cse.SectionName, cse.ErrorMessage);
							}
							else
							{
								log.ErrorFormat("Failed to retreive section {0} with exception {1}. Per FailStrategy, using local copy.", localSectionItem.SectionName, e);
							}
						}
						return false;    
				}
				if (rethrow)
				{
					throw; //something got unhappy when this happened inside of the switch
				}
				return false;
			}
		}

		private static bool GetRemoteSectionStringWasNew(string sectionName, string sectionHash, out string remoteSectionString, out string generic)
		{
			return transport.GetSectionStringWasNew(sectionName, sectionHash, out remoteSectionString, out generic);
		}

		private static void CheckSections(object obj)
		{
			log.Debug("Checking and updating configuration sections");
			lock (sectionCheckTimer)
			{
				sectionCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
				List<ConfigurationItem> items = store.GetItemList();
				for (int i = 0; i < items.Count; i++)
				{
					ConfigurationItem thisItem = items[i];
					try
					{
						log.DebugFormat("Checking configuration section {0}", thisItem.SectionName);
						if (CheckAndUpdateSectionItem(ref thisItem))
						{
							RefreshSection(thisItem.SectionName);
							ConfigurationChangedEventArgs args = new ConfigurationChangedEventArgs(
								thisItem.SectionName, thisItem.SectionString, thisItem.SectionStringBytes);

							ConfigurationSectionChanged(thisItem.SectionName, args);

						}
					}
					catch (ConfigurationSystemException cse)
					{
						log.WarnFormat("Configuration system exception checking section {0}: {1}", cse.SectionName, cse.ErrorMessage);
						//since this happens in the background after a config has been grabbed the first time, we don't want a connection failure to cause a problem
					}
					catch (Exception e)
					{
						log.ErrorFormat("Exception checking section {0}: {1}", thisItem == null ? "[null]" : thisItem.SectionName, e);
					}
				}
				sectionCheckTimer.Change(config.SectionCheckIntervalSeconds * 1000, config.SectionCheckIntervalSeconds * 1000);
			}
		}

		internal static string BuildDelegateDescription(string sectionName, IList<ConfigurationChangedEventHandler> handlerList)
		{
			Delegate[] delegates = new Delegate[handlerList.Count];
			for (int i = 0; i < handlerList.Count; i++)
			{
				delegates[i] = handlerList[i].GetInvocationList()[0];
			}
			return BuildDelegateDescription(sectionName, delegates);
		}

		internal static string BuildDelegateDescription(string sectionName, IList<EventHandler> handlerList)
		{
			Delegate[] delegates = new Delegate[handlerList.Count];
			for (int i = 0; i < handlerList.Count; i++)
			{
				delegates[i] = handlerList[i].GetInvocationList()[0];
			}
			return BuildDelegateDescription(sectionName, delegates);
		}

		internal static string BuildDelegateDescription(string sectionName, Delegate[] handlerList)
		{
			try
			{
				if (handlerList == null || handlerList.Length == 0)
				{
					return string.Format("0 delegates for section {0}", sectionName);
				}

				StringBuilder delegateDescriptions = new StringBuilder();
				delegateDescriptions.AppendFormat("{0} delegate{1} for section {2} reload: ", handlerList.Length,
												  handlerList.Length == 1 ? string.Empty : "s",
												  sectionName);
				for (int i = 0; i < handlerList.Length; i++)
				{
					delegateDescriptions.AppendFormat("{0}.{1}", handlerList[i].Method.DeclaringType.FullName,
													  handlerList[i].Method.Name);

					if (i < handlerList.Length - 1)
					{
						delegateDescriptions.Append(", ");
					}
				}

				return delegateDescriptions.ToString();
			}
			catch (Exception e)
			{
				return string.Format("Exception generating delegate list description for section {0} reload: {1}", sectionName, e);
			}
		}

		private static void ConfigurationSectionChanged(string sectionName, ConfigurationChangedEventArgs args)
		{
			//there are two ways for a client to indicate that it wants to be updated: the Event,
			//and calling RegisterReloadNotification
			
			log.DebugFormat("Notifying clients that section {0} has updated", sectionName);

			//First check the event
			//XmlSerializerReplacementHandler uses this, and also keeps its own list of handlers similar to below
			if (ConfigurationChangedEvent != null)
			{
				if (log.IsDebugEnabled)
				{
					log.DebugFormat("Event calling {0}",BuildDelegateDescription(sectionName, ConfigurationChangedEvent.GetInvocationList()));
				}
				ConfigurationChangedEvent(null, args);
			}
			
			//then do the internally managed delegates
			reloadDelegatesLock.EnterReadLock();
			try
			{
				List<ConfigurationChangedEventHandler> handlerList;
				if (reloadDelegates.TryGetValue(sectionName, out handlerList) && handlerList != null)
				{
					if (log.IsDebugEnabled)
					{
						log.DebugFormat("Registered Handlers calling {0}", BuildDelegateDescription(sectionName, handlerList));	
					}

					foreach (ConfigurationChangedEventHandler handler in handlerList)
					{
						handler.Invoke(null, args);
					}
				}
			}
			finally
			{
				reloadDelegatesLock.ExitReadLock();
			}
		}

        /// <summary>
        /// Gets the section object using type T and sectionName
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sectionName"></param>
        /// <returns></returns>
        public static T GetSectionFromConfigServer<T>(string sectionName)
        {
            var configString = GetSectionString(sectionName);

            using (StringReader reader = new StringReader(configString))
            {
                XmlTextReader xmlReader = new XmlTextReader(reader);

                XmlSerializer serializer = new XmlSerializer(typeof(T));
                T deserializedConfig = (T)serializer.Deserialize(xmlReader);

				return deserializedConfig;
            }
        }
	}
}
