using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Threading;
using System;

namespace MySpace.ConfigurationSystem
{
	internal class ConfigurationItemStore : KeyedCollection<string, ConfigurationItem>
	{

		readonly ReaderWriterLockSlim locker = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
		static readonly Logging.LogWrapper log = new Logging.LogWrapper();

		protected override string GetKeyForItem(ConfigurationItem item)
		{
			return item.SectionName;
		}

		/// <summary>
		/// Gets a list of every item in the collection. The list is build inside of a lock and returned so than it can be safely
		/// enumerated without holding a lock.
		/// </summary>
		/// <returns></returns>
		internal List<ConfigurationItem> GetItemList()
		{
			List<ConfigurationItem> items = new List<ConfigurationItem>(Count);
			if (Count > 0)
			{
				locker.EnterReadLock();
				try
				{
					foreach (ConfigurationItem item in Dictionary.Values)
					{
						items.Add(item);
					}
				}
				finally
				{
					locker.ExitReadLock();
				}
			}

			return items;
		}



		internal bool TryGet(string sectionName, out ConfigurationItem item)
		{
			ConfigurationItem foundItem = null;
			locker.EnterReadLock();
			try
			{
				if (Contains(sectionName))
				{
					foundItem = this[sectionName];
				}
			}
			finally
			{
				locker.ExitReadLock();
			}

			if (foundItem != null)
			{
				if (log.IsDebugEnabled)
				{
					log.DebugFormat("Found item for {0} in memory.", sectionName);
				}
				item = foundItem;
				return true;
			}

			foundItem = ReadFromFile(sectionName); 
			//not synchronizing this, because we don't want I/O inside of a lock if we can help it
			//It's better to read from the file multiple times instead
			if (log.IsDebugEnabled && foundItem != null)
			{
				log.DebugFormat("Found item for {0} on disk.", sectionName);
			}
			
			if (foundItem != null)
			{
				locker.EnterWriteLock();
				try
				{
					if (!Contains(sectionName)) //could've been inserted by another reader while we were getting the lock
					{
						base.Add(foundItem);
					}
				}
				finally
				{
					locker.ExitWriteLock();
				}
				item = foundItem;
				return true;
			}

			item = null;
			return false;
		}

		internal new void Add(ConfigurationItem item)
		{
			locker.EnterWriteLock();
			try
			{
				if (Contains(item))
				{
					Remove(item);
				}
				base.Add(item);
			}
			finally
			{
				locker.ExitWriteLock();
			}
			
			StoreToFile(item);
		}

		internal void Invalidate(string sectionName)
		{
			locker.EnterWriteLock();
			try
			{
				RemoveFromFile(sectionName);
				if (Contains(sectionName))
				{
					Remove(sectionName);
				}
			}
			finally
			{
				locker.ExitWriteLock();
			}
		}

		private static ConfigurationItem ReadFromFile(string sectionName)
		{
			Stream itemFile = null;
			string itemPath = GetItemPath(sectionName);
			MemoryStream ms;
			using (IsolatedStorageFile isoStore = GetIsoStore())
			{
				try
				{
					byte[] buffer = new byte[1024];
					int read;
					itemFile = new IsolatedStorageFileStream(itemPath, FileMode.Open, FileAccess.Read, isoStore);
					ms = new MemoryStream();
					do
					{
						read = itemFile.Read(buffer, 0, 1024);
						ms.Write(buffer, 0, read);
					}
					while (read > 0);

					return new ConfigurationItem(sectionName, ms.ToArray());
				}
				catch (FileNotFoundException)
				{
					return null;
				}
				finally
				{
					if (itemFile != null)
					{
						itemFile.Dispose();
					}
				}
			}
		}

		private static void RemoveFromFile(string sectionName)
		{
			string itemPath = GetItemPath(sectionName);
			using (IsolatedStorageFile isoStore = GetIsoStore())
			{
				try
				{
					string[] foundNames = isoStore.GetFileNames(itemPath);
					if(foundNames.Length > 0)
						isoStore.DeleteFile(itemPath);
				}
				catch (IsolatedStorageException ise)
				{
					log.WarnFormat("Could not remove file {0} from isolated storage: {1}", itemPath, ise.Message);
				}
				catch (Exception e)
				{
					log.ErrorFormat("Error removing file {0} from isolated storage: {1}", itemPath, e);
					throw;
				}
			}
		}

		private static void StoreToFile(ConfigurationItem item)
		{
			Stream itemFile = null;

			using (IsolatedStorageFile isoStore = GetIsoStore())
			{
				try
				{
					lock (item)
					{
						string path = GetItemPath(item);
						try
						{
							itemFile = new IsolatedStorageFileStream(path, FileMode.Truncate, FileAccess.ReadWrite, isoStore);
						}
						catch (FileNotFoundException)
						{
							itemFile = new IsolatedStorageFileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, isoStore);
						}
						byte[] sectionStringBytes = item.SectionStringBytes;
						if (sectionStringBytes != null)
						{
							itemFile.Write(sectionStringBytes, 0, sectionStringBytes.Length);
						}
					}

				}
				finally
				{
					if (itemFile != null)
					{
						itemFile.Flush();
						itemFile.Dispose();
					}
				}
			}
		}

		private static IsolatedStorageFile GetIsoStore()
		{
			return IsolatedStorageFile.GetMachineStoreForAssembly();
		}

		private static string GetItemPath(string sectionName)
		{
			return "ConfigurationSystem-" + sectionName + ".config";
		}
		private static string GetItemPath(ConfigurationItem item)
		{
			return GetItemPath(item.SectionName);
		}
	}
}
