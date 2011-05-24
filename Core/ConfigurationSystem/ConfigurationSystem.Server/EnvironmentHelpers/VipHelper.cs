using System;
using System.Net;
using MySpace.Logging;
using System.Threading;
using System.IO;
using System.Text;

namespace MySpace.ConfigurationSystem.EnvironmentHelpers
{
	//TODO Make this a general purpose web service helper
	public class VipHelper
	{
		private static readonly LogWrapper log = new LogWrapper();
		private readonly AutoResetEvent synchronizerEvent = new AutoResetEvent(false);
		private const string webrequestFormat = @"http://skynetapi.msprod.msp/getvip/{0}"; 
		
		public IPAddress[] GetIPAddressesForVip(string vipName)
		{
			if (string.IsNullOrEmpty(vipName))
				return null;

			log.DebugFormat("Getting ip addresses for vip {0}", vipName);

			IPAddress[][] addressCollections;
			HandleWithCount synchronizer;
			lock(synchronizerEvent) //need to make sure we're not doing two parallelized attempts at once
			{
				//first, make a web request to get the list of server names
				string webRequestUrl = string.Format(webrequestFormat, vipName);
				
				log.DebugFormat("Using url {0} for vip {1}", webRequestUrl, vipName);
				
				string serverListResult;
				string[] serverList = null;

				HttpWebRequest request = WebRequest.Create(webRequestUrl) as HttpWebRequest;               
				
				if (request != null)
				{
					try
					{
						using (WebResponse response = request.GetResponse())
						{
							using (Stream responseStream = response.GetResponseStream())
							{
								//response.ContentLength is a long, but memorystreams use int (presumeably because they can't be more than 2 gigs!). 
								//it's theoretically possible that the vip list could be over 2048 megs and we like coding defensively around here
								if (response.ContentLength <= Int32.MaxValue) 
								{
									MemoryStream theData = new MemoryStream((int)response.ContentLength);
									byte[] buffer = new byte[1024];
									if (responseStream != null)
									{
										int bytesRead;
										do
										{
											bytesRead = responseStream.Read(buffer, 0, buffer.Length);
											theData.Write(buffer, 0, bytesRead);
										} while (bytesRead > 0);
										
										
										serverListResult = Encoding.UTF8.GetString(theData.GetBuffer());
										serverList = serverListResult.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
										if(serverList.Length > 0)
											log.DebugFormat("Got {0} server names for vip {1}", serverList.Length, vipName);
										else
										{
											log.WarnFormat("Did not find any server names for vip {0}. Does it exist?", vipName);
										}
									}	
								}
								else
								{
									log.ErrorFormat("Request length from {0} was {1}. That's too big to handle here.", webRequestUrl, response.ContentLength);
									return null;
								}
								
							}
						}
					}
					catch(Exception e)
					{
						log.ErrorFormat("Error getting server name list for vip {0}: {1}", vipName, e);
						return null;
					}
				}

				//assuming we have a list of servers, queue a work item to do a dns look up for each to get the ip address. dns is slow 
				//and a vip might have hundreds of servers!
				if (serverList != null && serverList.Length > 0)
				{

					try
					{
						synchronizer = new HandleWithCount(synchronizerEvent, serverList.Length);
						addressCollections = new IPAddress[serverList.Length][];

						for (int i = 0; i < serverList.Length; i++)
						{
							VipSearchResultState resultState = new VipSearchResultState
																{
																	_addressResults = addressCollections,
																	_handle = synchronizer,
																	_host = serverList[i],
																	_index = i
																};
							ThreadPool.UnsafeQueueUserWorkItem(ProcessHost, resultState);
						}

						synchronizerEvent.WaitOne();

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
					catch (Exception e)
					{
						log.ErrorFormat("Error resolving IPs for vip {0}: {1}", vipName, e);
					}
				}
			}

			return null;

		}


		internal static void ProcessHost(object state)
		{
			VipSearchResultState resultState = state as VipSearchResultState;
			if(resultState == null)
				return;

			int count = 0;
			try
			{


				string hostName = resultState._host;
				if (!string.IsNullOrEmpty(hostName))
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
	
		

	}
}
