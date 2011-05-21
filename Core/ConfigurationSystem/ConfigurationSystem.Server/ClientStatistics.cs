using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using MySpace.Logging;
using System.Runtime.Serialization;

namespace MySpace.ConfigurationSystem
{
	/// <summary>
	/// Keeps track of information about all requests made to this server. 
	/// </summary>
	[Serializable]
	public class RequestStatistics
	{
		private readonly Dictionary<string, SectionStatistics> _allStatistics =
			new Dictionary<string, SectionStatistics>(StringComparer.InvariantCultureIgnoreCase);
		[NonSerialized] ReaderWriterLockSlim _allStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		const string statisticsHeader = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><ConfigurationSystemStatistics>";
		const string statisticsFooter = "</ConfigurationSystemStatistics>";
		private readonly DateTime InitializationDate = DateTime.Now;
		private static readonly LogWrapper log = new LogWrapper();

		[OnDeserialized]
		private void RestoreFields(StreamingContext context)
		{
			_allStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		}

		public string GetLastServedSrcForClient(string environmentName, string sectionName, IPAddress addr)
		{
			SectionStatistics sectionStat;
			string ret = null;
			_allStatsLock.EnterReadLock();
			try
			{
				if (_allStatistics.TryGetValue(sectionName, out sectionStat))
				{
					ret = sectionStat.GetLastServedSrcForClient(environmentName, addr);
				}
			}
			finally
			{
				_allStatsLock.ExitReadLock();
			}
			
			return ret;
		}

		internal void RegisterGet(EndPoint requester, string requestorEnvironment, string sectionName, string returnedHash, string src)
		{
			SectionStatistics sectionStats;
			_allStatsLock.EnterReadLock();
			try
			{
				if (_allStatistics.TryGetValue(sectionName, out sectionStats))
				{
					sectionStats.RegisterGet(requester, requestorEnvironment, returnedHash, src);
					return;
				}
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception registering statistics for {0} | {1} | {2} | {3} | {4}: {5}",
				                requester,
				                requestorEnvironment,
				                sectionName,
				                returnedHash,
				                src,
				                e);
			}
			finally
			{
				_allStatsLock.ExitReadLock();
			}
			
			//if we made it here the section hadn't been added.
			_allStatsLock.EnterWriteLock();
			try
			{
				if (!_allStatistics.ContainsKey(sectionName)) //but it might've been while we were getting the lock
				{
					sectionStats = new SectionStatistics(this, sectionName, requester, requestorEnvironment, returnedHash, src);
					_allStatistics.Add(sectionName, sectionStats);
				}
				else
				{
					_allStatistics[sectionName].RegisterGet(requester, requestorEnvironment, returnedHash, src);
				}
			}
			finally
			{
				_allStatsLock.ExitWriteLock();
			}

		}

		internal string GetStatistics()
		{
			StringBuilder statsBuilder = new StringBuilder();
			AppendHeader(statsBuilder);
			_allStatsLock.EnterReadLock();
			try
			{
				foreach (SectionStatistics sectionStats in _allStatistics.Values)
				{
					sectionStats.GetStatistics(statsBuilder);
				}
			}
			finally
			{
				_allStatsLock.ExitReadLock();
			}
			statsBuilder.AppendLine(statisticsFooter);
			return statsBuilder.ToString();
		}

		internal string GetStatistics(string sectionName)
		{
			StringBuilder statsBuilder = new StringBuilder();
			AppendHeader(statsBuilder);
			_allStatsLock.EnterReadLock();
			try
			{
				SectionStatistics sectionStats;
				if (_allStatistics.TryGetValue(sectionName, out sectionStats))
				{
					sectionStats.GetStatistics(statsBuilder);
				}
			}
			finally
			{
				_allStatsLock.ExitReadLock();
			}
			
			statsBuilder.AppendLine(statisticsFooter);
			return statsBuilder.ToString();
		}

		internal string GetStatistics(string sectionName, string environmentName)
		{
			StringBuilder statsBuilder = new StringBuilder();
			AppendHeader(statsBuilder);
			_allStatsLock.EnterReadLock();
			try
			{
				SectionStatistics sectionStats;
				if (_allStatistics.TryGetValue(sectionName, out sectionStats))
				{
					sectionStats.GetStatistics(statsBuilder, environmentName);
				}
			}
			finally
			{
				_allStatsLock.ExitReadLock();
			}
			
			statsBuilder.AppendLine(statisticsFooter);
			return statsBuilder.ToString();
		}

		private void AppendHeader(StringBuilder statsBuilder)
		{
			statsBuilder.AppendLine(statisticsHeader);
			statsBuilder.AppendFormat("<InitializationTime>{0}</InitializationTime>", InitializationDate);
			statsBuilder.AppendFormat("<CurrentTime>{0}</CurrentTime>\n", DateTime.Now);
		}

		internal void RemoveSection(string sectionName)
		{
			//most likely this was called from inside of a looped read. execute it on a background thread to avoid deadlocking
			//or modifying a collection that's being enumerated
			if(!string.IsNullOrEmpty(sectionName))
				ThreadPool.UnsafeQueueUserWorkItem(DoRemoveSection, sectionName);
		}

		private void DoRemoveSection(object sender)
		{
			string sectionName = sender as string;
			if (string.IsNullOrEmpty(sectionName))
			{
				log.WarnFormat("DoRemoveSection was called with a null sectionName");
				return;
			}
			_allStatsLock.EnterWriteLock();
			try
			{
				if (_allStatistics.ContainsKey(sectionName))
				{
					log.InfoFormat("Removing section {0} from statistics", sectionName);
					_allStatistics.Remove(sectionName);
				}
			}
			finally
			{
				_allStatsLock.ExitWriteLock();
			}
		}
	}

	/// <summary>
	/// Keeps track of information about requests for a particular section hosted on this server.
	/// </summary>
	[Serializable]
	public class SectionStatistics
	{
		readonly string _name;
		long _getCount;
		private readonly RequestStatistics _parent;
		private readonly Dictionary<string, EnvironmentStatistics> _environmentStatistics =
			new Dictionary<string, EnvironmentStatistics>(StringComparer.InvariantCultureIgnoreCase);
		[NonSerialized] ReaderWriterLockSlim _sectionStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		const string sectionHeader = "<ConfigurationSection name=\"{0}\">\n<GetCount>{1}</GetCount>";
		const string sectionFooter = "</ConfigurationSection>";

		[OnDeserialized]
		private void RestoreFields(StreamingContext context)
		{
			_sectionStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		}

		internal SectionStatistics(RequestStatistics parent, string sectionName)
		{
			_parent = parent;
			_name = sectionName;
		}

		internal SectionStatistics(RequestStatistics parent, string sectionName, EndPoint requestor, string requestorEnvironment, string returnedHash, string src) : this(parent, sectionName)
		{
			RegisterGet(requestor, requestorEnvironment, returnedHash, src);
		}

		public string GetLastServedSrcForClient(string environmentName, IPAddress addr)
		{
			EnvironmentStatistics envStat;
			string ret = null;
			_sectionStatsLock.EnterReadLock();
			try
			{
				if (_environmentStatistics.TryGetValue(environmentName, out envStat))
				{
					ret = envStat.GetLastServedSrcForClient(addr);
				}
			}
			finally
			{
				_sectionStatsLock.ExitReadLock();
			}
			
			return ret;
		}

		internal void RegisterGet(EndPoint requestor, string requestorEnvironment, string returnedHash, string src)
		{
			EnvironmentStatistics environmentStats;
			_sectionStatsLock.EnterReadLock();
			try
			{
				if (_environmentStatistics.TryGetValue(requestorEnvironment, out environmentStats))
				{
					environmentStats.RegisterGet(requestor, returnedHash, src);
					return;
				}
			}
			finally
			{
				_sectionStatsLock.ExitReadLock();
				Interlocked.Increment(ref _getCount);
			}
		
			//if we got here, then the environment hadn't already been added. we've already incremented the total get count in the finally, but we still
			//need to register the actual get
			_sectionStatsLock.EnterWriteLock();
			try
			{
				if (!_environmentStatistics.ContainsKey(requestorEnvironment)) //but it might've been while we were getting the lock
				{
					environmentStats = new EnvironmentStatistics(this, _name, requestorEnvironment, requestor, returnedHash, src);
					_environmentStatistics.Add(requestorEnvironment, environmentStats);
				}
				else
				{
					_environmentStatistics[requestorEnvironment].RegisterGet(requestor, returnedHash, src);
				}
			}
			finally
			{
				_sectionStatsLock.ExitWriteLock();
			}
		}

		internal void GetStatistics(StringBuilder statsBuilder)
		{
			statsBuilder.AppendFormat(sectionHeader, _name, _getCount);
			_sectionStatsLock.EnterReadLock();
			try
			{
				foreach (EnvironmentStatistics envStats in _environmentStatistics.Values)
				{
					envStats.GetStatistics(statsBuilder);
				}
			}
			finally
			{
				_sectionStatsLock.ExitReadLock();
			}
			
			statsBuilder.AppendFormat(sectionFooter);
		}
		
		internal void GetStatistics(StringBuilder statsBuilder, string enviornmentName)
		{
			statsBuilder.AppendFormat(sectionHeader, _name, _getCount);
			_sectionStatsLock.EnterReadLock();
			try
			{
				EnvironmentStatistics envStats;
				if (_environmentStatistics.TryGetValue(enviornmentName, out envStats))
				{
					envStats.GetStatistics(statsBuilder);
				}
			}
			finally
			{
				_sectionStatsLock.ExitReadLock();
			}
				
			statsBuilder.AppendFormat(sectionFooter);
		}

		internal void RemoveSection(string sectionName)
		{
			_parent.RemoveSection(sectionName);
		}
	}

	/// <summary>
	/// Keeps track of a combination of request statistics for a given section in a particular environment.
	/// </summary>
	[Serializable]
	public class EnvironmentStatistics
	{
		readonly SectionStatistics _parent;
		readonly string _sectionName;
		readonly string _environmentName;

		long _getCount;
		
		static readonly LogWrapper log = new LogWrapper();
		readonly Dictionary<IPAddress, ClientStatistics> _clientStatistics = new Dictionary<IPAddress, ClientStatistics>();
		[NonSerialized] ReaderWriterLockSlim _environmentStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		const string sectionHeader = "<Environment name=\"{0}\">\n<GetCount>{1}</GetCount>\n<CurrentHash>{2}</CurrentHash>";
		const string sectionFooter = "</Environment>";

		[OnDeserialized]
		private void RestoreFields(StreamingContext context)
		{
			_environmentStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		}

		internal EnvironmentStatistics(SectionStatistics parent, string sectionName, string environmentName)
		{
			_parent = parent;
			_sectionName = sectionName;
			_environmentName = environmentName;
		}

		internal EnvironmentStatistics(SectionStatistics parent, string sectionName, string environmentName, EndPoint requestor, string returnedHash, string src) : this(parent, sectionName, environmentName)
		{
			RegisterGet(requestor, returnedHash, src);
		}

		public string GetLastServedSrcForClient(IPAddress addr)
		{
			ClientStatistics clientStat;
			string ret = null;
			_environmentStatsLock.EnterReadLock();
			try
			{
				if (_clientStatistics.TryGetValue(addr, out clientStat))
				{
					ret = clientStat.LastSrc;
				}
			}
			finally
			{
				_environmentStatsLock.ExitReadLock();
			}
			
			return ret;
		}

		internal void RegisterGet(EndPoint requester, string returnedHash, string src)
		{
			IPEndPoint ipRequester = requester as IPEndPoint;
			ClientStatistics clientStats;

			if (ipRequester != null)
			{
				IPAddress address = ipRequester.Address;
				_environmentStatsLock.EnterReadLock();
				try
				{
					if (_clientStatistics.TryGetValue(address, out clientStats))
					{
						clientStats.RegisterGet(returnedHash, src);
					}

				}
				finally
				{
					_environmentStatsLock.ExitReadLock();
					Interlocked.Increment(ref _getCount);
				}

				//if we got here, the address is new
				_environmentStatsLock.EnterWriteLock();
				try
				{
					if (!_clientStatistics.ContainsKey(address)) //but it might've been added while we got the lock
					{
						clientStats = new ClientStatistics(this, address, returnedHash, src);
						_clientStatistics.Add(address, clientStats);
					}
					else
					{
						_clientStatistics[address].RegisterGet(returnedHash, src);
					}
				}
				finally
				{
					_environmentStatsLock.ExitWriteLock();
				}
			}
		}

		internal void GetStatistics(StringBuilder statsBuilder)
		{
			statsBuilder.AppendFormat(sectionHeader, _environmentName, _getCount, GetCurrentHash());

			_environmentStatsLock.EnterReadLock();
			try
			{
				foreach (ClientStatistics clientStats in _clientStatistics.Values)
				{
					clientStats.GetStatistics(statsBuilder);
				}
			}
			finally
			{
				_environmentStatsLock.ExitReadLock();
			}

			statsBuilder.AppendFormat(sectionFooter);
		}

		private string GetCurrentHash()
		{
			try
			{
				string environmentName = _environmentName;
				bool encrypt;
				string path = SectionMapper.GetPathForSectionAndEndpoint(_sectionName, null, out encrypt, ref environmentName);
				if (string.IsNullOrEmpty(path))
				{
					log.WarnFormat("No path found for section {0}. Section likely no longer exists; removing from statistics.",
					               _sectionName);
					_parent.RemoveSection(_sectionName);
					return "No current hash found (section path null)";
				}

				ConfigurationItem currentItem;
				if (LocalCache.TryGet(path, out currentItem))
				{
					return currentItem.HashString;
				}

				log.WarnFormat("Could not find current hash for section {0} in environment {1}", _sectionName, _environmentName);
				return "No current hash found";
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception getting current hash for section {0} and environment {1}: {2}", _sectionName, _environmentName, e);
				return string.Format("Exception getting current hash: {0}", e.Message);
			}
			

		}

	}

	/// <summary>
	/// Keeps track of statistics for a particular client to a given section in a single environment.
	/// </summary>
	[Serializable]
	public class ClientStatistics
	{
		readonly EnvironmentStatistics _parent;
		long _getCount;
		DateTime _lastGet = DateTime.MinValue;
		string _lastHash;
		string _lastSrc;
		readonly IPAddress _address;
		[NonSerialized] ReaderWriterLockSlim _clientStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

		[OnDeserialized]
		private void RestoreFields(StreamingContext context)
		{
			_clientStatsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		}

		internal ClientStatistics(EnvironmentStatistics parent, IPAddress address)
		{
			_parent = parent;
			_address = address;
		}

		internal ClientStatistics(EnvironmentStatistics parent, IPAddress address, string returnedHash, string src) : this(parent, address)
		{
			RegisterGet(returnedHash, src);
		}

		public string LastSrc { get { return _lastSrc; } }

		internal void RegisterGet(string returnedHash, string src)
		{
			_clientStatsLock.EnterWriteLock();
			try
			{
				_getCount++;
				DateTime now = DateTime.Now;
				if (now > _lastGet) //no guarantee of in order get resistrations
				{
					_lastGet = now;
					_lastHash = returnedHash;
					_lastSrc = src;
				}
			}
			finally
			{
				_clientStatsLock.ExitWriteLock();
			}
		}

		internal void GetStatistics(StringBuilder statsBuilder)
		{
			_clientStatsLock.EnterReadLock();
			try
			{
				statsBuilder.AppendFormat(
					"<Client><Address>{0}</Address><GetCount>{1}</GetCount><LastGet>{2}</LastGet><LastReturnedHash>{3}</LastReturnedHash></Client>\n",
					_address, _getCount, _lastGet, _lastHash);
			}
			finally
			{
				_clientStatsLock.ExitReadLock();
			}
		}
	}
}
