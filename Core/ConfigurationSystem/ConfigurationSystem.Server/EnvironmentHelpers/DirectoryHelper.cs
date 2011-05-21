using System.Net;
using System.DirectoryServices;
using System.Threading;

namespace MySpace.ConfigurationSystem.EnvironmentHelpers
{
	public static class DirectoryHelper
	{
		private static readonly AutoResetEvent synchronizerEvent = new AutoResetEvent(false);
		
		public static IPAddress[] GetIPAddressesForDirectoryFilter(string root, string filter)
		{
			if (root == string.Empty)
				root = null;

			if (string.IsNullOrEmpty(filter))
				return null;

			IPAddress[][] addressCollections;
			HandleWithCount synchronizer;
			lock(synchronizerEvent) //need to make sure we're not doing two parallelized attempts at once
			{
				DirectorySearcher searcher = GetDirectorySearcher(root, filter);
				SearchResultCollection searchResults = searcher.FindAll();
				
				synchronizer = new HandleWithCount(synchronizerEvent, searchResults.Count);
				addressCollections = new IPAddress[searchResults.Count][];

				for (int i = 0; i < searchResults.Count; i++)
				{
					DirectorySearchResultState resultState = new DirectorySearchResultState
					                                	{
					                                		_addressResults = addressCollections,
					                                		_handle = synchronizer,
					                                		_host = searchResults[i],
					                                		_index = i
					                                	};
					ThreadPool.UnsafeQueueUserWorkItem(ProcessHost, resultState);
				}
				
				synchronizerEvent.WaitOne();
			}

			IPAddress[] results = new IPAddress[synchronizer.AccumulatorValue];
			for (int i = 0, resultIndex = 0; i < addressCollections.Length; i++)
			{
				IPAddress[] addressCollection = addressCollections[i];
				if (addressCollection != null)
				{
					for (int j = 0; j < addressCollection.Length; j++)
					{
						results[resultIndex++] = addressCollection[j];
					}
				}
			}
			return results;
		}


		internal static void ProcessHost(object state)
		{
			
			DirectorySearchResultState resultState = state as DirectorySearchResultState;
			if(resultState == null)
				return;

			int count = 0;
			try
			{
				DirectoryEntry entry = resultState._host.GetDirectoryEntry();
				PropertyValueCollection hostNameValues = entry.Properties["dnshostname"];
				string hostName = hostNameValues.Value as string;
				if(!string.IsNullOrEmpty(hostName))
				{
					IPAddress[] addresses = DnsHelper.ResolveIPAddress(hostName);
					if (addresses != null)
					{
						resultState._addressResults[resultState._index] = addresses;
						count = addresses.Length;
					}
				}
			}
			finally
			{
				resultState._handle.Decrement(count);
			}
		}
		

		private static DirectorySearcher GetDirectorySearcher(string root, string basefilter)
		{
			DirectorySearcher searcher;
			if (root == null)
				searcher = new DirectorySearcher();
			else
				searcher = new DirectorySearcher(new DirectoryEntry(root));
			searcher.Tombstone = false;
			searcher.CacheResults = false;
			searcher.PropertiesToLoad.AddRange(new[] { "cn", "dnshostname" });
			searcher.Filter = BuildFullFilter(basefilter);
			return searcher;
		}

		private static string BuildFullFilter(string baseFilter)
		{
			//return string.Format("(&(objectClass=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)){0})", baseFilter);
			//return string.Format("(&(objectClass=computer){0})", baseFilter);
			return baseFilter;
		}
	}
}
