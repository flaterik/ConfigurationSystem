using System.Collections.Generic;
using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using MySpace.Logging;
using System.Threading;

namespace MySpace.ConfigurationSystem
{
	internal static class LocalCache
	{   
		private static readonly LogWrapper log = new LogWrapper();

		private static Dictionary<string, ConfigurationItem> cache = new Dictionary<string, ConfigurationItem>();
		private static readonly ReaderWriterLockSlim cacheLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
		private static readonly string BackupFileName = "LocalCacheFileBackup-" + HttpServer.AssemblyVersion;
		
		static LocalCache()
		{
			cacheLock.EnterWriteLock();
			try
			{
				LoadDictionaryFromDisk();
			}
			finally
			{
				cacheLock.ExitWriteLock();
			}
			
			Update();
		}

		internal static void ShutDown()
		{
			cacheLock.EnterWriteLock();
			try
			{
				SaveDictionaryToDisk();
			}
			finally
			{
				cacheLock.ExitWriteLock();
			}
		}

		private static void LoadDictionaryFromDisk()
		{
			try
			{
				if (!File.Exists(BackupFileName))
				{
					return;
				}
				BinaryFormatter bf = new BinaryFormatter();
				FileStream fs = new FileStream(BackupFileName, FileMode.Open, FileAccess.Read);
				cache = bf.Deserialize(fs) as Dictionary<string, ConfigurationItem>;
				fs.Dispose();
				log.InfoFormat("Local cache restored from disk backup.");
			}
			catch (System.Runtime.Serialization.SerializationException se)
			{	
				log.ErrorFormat("Serialization error reading disk cached configs: {0}", se.Message);
			}
			catch (Exception e)
			{
				log.ErrorFormat("Error reading disk cached configs: {0}", e);
			}
			
		}

		private static void SaveDictionaryToDisk()
		{
			try
			{
				BinaryFormatter bf = new BinaryFormatter();
				FileStream fs = new FileStream(BackupFileName, FileMode.Create, FileAccess.Write);
				bf.Serialize(fs, cache);
				fs.Dispose();
			}
			catch (Exception e)
			{
				log.ErrorFormat("Error saving cache to disk: {0}", e);
			}
		}

		public static bool TryGet(string key, out ConfigurationItem item)
		{
			bool success = false;
			ConfigurationItem foundItem = null;
			cacheLock.EnterReadLock();
			try
			{
				if (cache.ContainsKey(key))
				{
					foundItem = cache[key];
					success = true;
				}
			}
			finally
			{
				cacheLock.ExitReadLock();
			}
			item = foundItem;
			return success;
		}

		public static ConfigurationItem Get(string name, string key, string environment, bool encrypt, IConfigurationSystemSectionProvider handler)
		{
			ConfigurationItem item;
			cacheLock.EnterReadLock();
			try
			{
				if (cache.TryGetValue(key, out item))
				{
					log.DebugFormat("Item with key {0} and name {1} found in cache", key, name);
				}
				else
				{
					log.DebugFormat("Cache did not contain item with key {0} and name {1}, fetching.", key, name);
				}
			}
			finally
			{
				cacheLock.ExitReadLock();
			}

			if (item == null) //it was not found and we need to get it
			{
				cacheLock.EnterWriteLock();
				try
				{
					if (!cache.ContainsKey(key))
					{
						item = new ConfigurationItem(name, key, environment, encrypt, 
													 ConfigurationSystemServerConfig.
														DefaultTimeToLive);

						byte[] dataBytes;
						string errorMessage;
						if(handler.TryGetDataBytes(key, out dataBytes, out errorMessage))
						{
							item.DataBytes = dataBytes;
							cache.Add(key, item);
						}
						else
						{
							item.ErrorMessage = errorMessage;
						}
							
					}
					else //it was added while we were waiting for the lock
					{
						item = cache[key];
					}
				}
				finally
				{
					cacheLock.ExitWriteLock();
				}
			}

			//I don't know how the cache can get into this state
			//but I observed it once during testing. The item was in cache, but had 
			//no source value and no data bytes, so it would always return empty, and
			//could not be updated. This is a guard against that condition.
			if(item != null && string.IsNullOrEmpty(item.Source)) 
			{
				log.ErrorFormat("Item with key {0} found with no source. Removing from cache. ", key);
				cacheLock.EnterWriteLock();
				try
				{
					if (cache.ContainsKey(key))
						cache.Remove(key);
				}
				finally
				{
					cacheLock.ExitWriteLock();
				}
			}

			return item;
		}

		public static void Purge()
		{
			cacheLock.EnterWriteLock();
			try
			{
				cache.Clear();
			}
			finally
			{
				cacheLock.ExitWriteLock();
			}
		}

		public static void Update()
		{
			List<ConfigurationItem> obsoleteItems = new List<ConfigurationItem>();
			cacheLock.EnterReadLock();
			try
			{
				foreach (ConfigurationItem item in cache.Values)
				{
					
					if(!SectionMapper.IsItemValid(item))
					{
						log.WarnFormat("Cached item for section {0} is not valid; removing from cache.",
									   item.Name);
						obsoleteItems.Add(item);
					}
					else
					{
						item.Update();	
					}
				}
			}
			finally
			{
				cacheLock.ExitReadLock();
			}

			if (obsoleteItems.Count > 0)
			{
				cacheLock.EnterWriteLock();
				try
				{
					for (int i = 0; i < obsoleteItems.Count; i++)
					{
						if (cache.ContainsKey(obsoleteItems[i].Source))
							cache.Remove(obsoleteItems[i].Source);
					}
				}
				finally
				{
					cacheLock.ExitWriteLock();
				}
			}
		}

	}
}
