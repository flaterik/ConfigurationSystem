using System;
using System.Text;
using System.Net;
using System.Configuration;
using System.Threading;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Reflection;
using MySpace.ConfigurationSystem.Encryption;

namespace MySpace.ConfigurationSystem
{
	internal enum ServerCommand : byte //be sure to update InitializeCommandLookup if commands are added here
	{
		Empty,
		Get,
		Status,
		Purge,
		Update,
		Mapping,
		FavIcon
	}
	
	
	public static class HttpServer
	{
		static readonly Logging.LogWrapper log = new Logging.LogWrapper();        
		static readonly Encoding encoding = new UTF8Encoding(false);
		//static readonly Dictionary<string,ServerCommand> serverCommandLookup = new Dictionary<string, ServerCommand>(5,StringComparer.OrdinalIgnoreCase);
		static ServerCommand[] serverCommandLookupTable;

		const HttpResponseHeader serverHeader = HttpResponseHeader.Server;
		const string serverName = "MySpace-Configuration";
		internal static readonly string AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
		static readonly Common.IO.Compressor compressor = Common.IO.Compressor.GetInstance();
		static readonly int compressionThreshold;

		static HttpListener listener;
		static bool running;
		
		static readonly bool EncryptorAvailable;
		static readonly AsyncCallback getContextCallback = GetContextCallback;
		static readonly ConfigurationSystemServerConfig config = (ConfigurationSystemServerConfig)ConfigurationManager.GetSection("ConfigurationSystemServerConfig");
		static readonly string listenPrefix;
		static readonly HttpServerCounters counters;
		static readonly string StatisticsFileName = "StatisticsFileBackup-" + AssemblyVersion;
		private static RequestStatistics clientStatistics;

		private static readonly byte[] emptyResponse = new byte[0];
		private static byte[] favIconResponse;

		public static RequestStatistics ClientStats { get { return clientStatistics; } }

		static HttpServer()
		{
			compressionThreshold = (config == null) ?
				ConfigurationSystemServerConfig.DefaultCompressionThreshold :
				config.CompressionThreshold;
			
			listenPrefix = (config == null) ?
				   string.Format("http://{0}:{1}/", ConfigurationSystemServerConfig.DefaultPrefix, ConfigurationSystemServerConfig.DefaultPort) :
				   string.Format("http://{0}:{1}/", config.Prefix, config.Port);

			counters = new HttpServerCounters();
			
			counters.Initialize((config == null) ?
				   string.Format("Port {0}", ConfigurationSystemServerConfig.DefaultPort) :
				   string.Format("Port {0}", config.Port)); //performance monitor didn't like to play nice with the special character common to the full prefixes

			if (!Encryptor.IsKeyProviderAvailable(config.KeyProviderTypeName))
			{
				log.WarnFormat("The encryption key provider {0} is not available. Section items with \"encrypt\" set will be returned empty.", config.KeyProviderTypeName);
				EncryptorAvailable = false;
			}
			else
			{
				EncryptorAvailable = true;
			}
			
			InitializeStatistics();
			InitializeCommandLookup();
			InitializeFavIcon();
		}

		internal static Encryptor GetEncryptor()
		{
			if(EncryptorAvailable)
				return new Encryptor(config.KeyProviderTypeName);

			return null;
		}

		private static void InitializeStatistics()
		{
			try
			{
				if (!File.Exists(StatisticsFileName))
				{
					clientStatistics = new RequestStatistics();
					return;
				}
				BinaryFormatter bf = new BinaryFormatter();
				FileStream fs = new FileStream(StatisticsFileName, FileMode.Open, FileAccess.Read);
				clientStatistics = bf.Deserialize(fs) as RequestStatistics;
				fs.Dispose();
				log.InfoFormat("Statistics restored from disk backup.");
			}
			catch (System.Runtime.Serialization.SerializationException se)
			{
				clientStatistics = new RequestStatistics();
				log.ErrorFormat("Serialization error reading disk cached statistics: {0}", se.Message);
			}
			catch (Exception e)
			{
				clientStatistics = new RequestStatistics();
				log.ErrorFormat("Error reading disk cached statistics: {0}", e);
			}
		}

		private static void SaveStatisticsToDisk()
		{
			try
			{
				BinaryFormatter bf = new BinaryFormatter();
				FileStream fs = new FileStream(StatisticsFileName, FileMode.Create, FileAccess.Write);
				bf.Serialize(fs, clientStatistics);
				fs.Dispose();
			}
			catch (Exception e)
			{
				log.ErrorFormat("Error saving statistics to disk: {0}", e);
			}
		}
		
		private static void IntializeSectionMapper()
		{
			try
			{
				if (config.PreCaching)
				{
					SectionMapper.LoadLocalCache();
				}
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception loading the local cache on startup:{0}", e);
			}
		}

		private static void InitializeCommandLookup()
		{
			//serverCommandLookup.Add("get",ServerCommand.Get);
			//serverCommandLookup.Add("status",ServerCommand.Status);
			//serverCommandLookup.Add("purge",ServerCommand.Purge);
			//serverCommandLookup.Add("favicon.ico",ServerCommand.FavIcon);
			//serverCommandLookup.Add("",ServerCommand.Empty);
			
			//doing the lookup table is at least 30% faster than either using the above dictionary of
			//commands or using a switch statement. Let's not even talk about enum.parse.
			//if A command with the same first letter as another needs to be added,
			//the dictionary is likely the best solution to fall back on

			string[] commandStrings = {"get", "status", "purge", "update", "favicon.ico"};
			int maxIndex = 0;
			foreach(string commandString in commandStrings)
			{
				if( (commandString[0]) > maxIndex)
				{
					maxIndex = commandString[0];
				}
				if( (commandString.ToUpperInvariant()[0]) > maxIndex)
				{
					maxIndex = commandString.ToUpperInvariant()[0];
				}
			}
			serverCommandLookupTable = new ServerCommand[maxIndex+1];
// ReSharper disable RedundantCast
			serverCommandLookupTable[(int)'g'] = ServerCommand.Get;
			serverCommandLookupTable[(int)'G'] = ServerCommand.Get;
			serverCommandLookupTable[(int)'s'] = ServerCommand.Status;
			serverCommandLookupTable[(int)'S'] = ServerCommand.Status;
			serverCommandLookupTable[(int)'P'] = ServerCommand.Purge;
			serverCommandLookupTable[(int)'p'] = ServerCommand.Purge;
			serverCommandLookupTable[(int)'F'] = ServerCommand.FavIcon;
			serverCommandLookupTable[(int)'f'] = ServerCommand.FavIcon;
			serverCommandLookupTable[(int)'U'] = ServerCommand.Update;
			serverCommandLookupTable[(int)'u'] = ServerCommand.Update;
			serverCommandLookupTable[(int)'M'] = ServerCommand.Mapping;
			serverCommandLookupTable[(int)'m'] = ServerCommand.Mapping;
// ReSharper restore RedundantCast
		}

		private static void InitializeFavIcon()
		{
			try
			{
				favIconResponse = File.ReadAllBytes("favicon.ico");
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception reading favicon file: {0}", e);
				favIconResponse = new byte[0];
			}
		}
		
		public static void Start()
		{
			if (!running)
			{
				try
				{
					if (listener == null)
					{
						listener = new HttpListener();
						listener.Prefixes.Add(listenPrefix);
					}

					log.InfoFormat("Starting Configuration System Http Server, listening on {0}", listenPrefix);
					IntializeSectionMapper();
					log.DebugFormat("Section mapper initialized");
					listener.Start();
					log.DebugFormat("Listener Started");
					running = true;
					
					listener.BeginGetContext(getContextCallback, listener);	
				}
				catch (Exception e)
				{
					log.ErrorFormat("Exception starting HttpServer: {0}", e);
					
				}
			}
			else
			{
				log.Error("Start() called on running HttpServer");
			}
		}

		public static void Stop()
		{
			if (running)
			{
				running = false;
				log.InfoFormat("Stopping Configuration System Http Server.");
				listener.Stop();
				counters.ResetCounters();
				LocalCache.ShutDown();
				SaveStatisticsToDisk();
			}
		}

		static void GetContextCallback(IAsyncResult ar)
		{
			counters.QueueRequest();
			HttpListener contextListener = ar.AsyncState as HttpListener;
			HttpListenerContext context = null;
			if (contextListener == null)
			{
				return;
			}

			try
			{
				context = contextListener.EndGetContext(ar);
			}
			catch (HttpListenerException hle)
			{
				if (hle.ErrorCode != 995) //="The I/O operation has been aborted because of either a thread exit or an application request" aka we stopped the listener
				{
					log.Error("Exception ending get context: {0}", hle);
				}
				
			}
			catch (Exception e)
			{
				log.Error("Exception ending get context: {0}", e);
			}

			try
			{
				if (contextListener.IsListening)
				{
					contextListener.BeginGetContext(getContextCallback, contextListener);
				}
				else
				{
					if(running)
						log.Error("Http Listener is not listening, can't begin another get context!");
				}
			} 
				//still rethrowing here because it's unclear if the crashing exceptions happened here or in the process method, but at least we'll get the full log now
				//it's also not entirely clear how to recover if we get one anyway
			catch (HttpListenerException hle)
			{
				if (hle.ErrorCode != 995) //995="The I/O operation has been aborted because of either a thread exit or an application request" aka we stopped the listener
				{
					log.Error("Exception beginning next http listen: {0}", hle);
					throw;
				}

			}
			catch (Exception e)
			{
				
				log.ErrorFormat("Exception beginning next http listen: {0}", e);
				throw;
			}
			

			if(context != null)
			{
				ProcessListenerContext(context);                
			}

		}

		private static void ProcessListenerContext(HttpListenerContext context)
		{
			byte[] responseBytes = null;
			int responseCode = 0;
			HttpListenerRequest request;
			HttpListenerResponse response = null;
			bool responseCompressed = false;
			bool responseGeneric = false;
			bool responseEncrypted = false;
			IPEndPoint clientEndPoint = null;

			try
			{
				request = context.Request;
				response = context.Response;
				string sectionName, environmentName, currentHash;
				ServerCommand command;
				clientEndPoint = GetClientEndPoint(request);
				bool zipAllowed = IsZipAllowed(request);
				ExtractRequestParameters(request.RawUrl, out command, out sectionName, out environmentName, out currentHash);

				if (log.IsDebugEnabled)
				{
					log.DebugFormat("Got request {0}/{1}/{2}?env={3} from {4}",
						command, sectionName, currentHash ?? "[null]", environmentName, clientEndPoint.Address);
				}

				switch (command)
				{
					case ServerCommand.Get:
						ProcessGetRequest(clientEndPoint, sectionName, currentHash, environmentName, zipAllowed,
							out responseCode, out responseBytes, out responseCompressed, out responseGeneric, out responseEncrypted);
						break;
					case ServerCommand.Status:
						ProcessStatusRequest(sectionName, environmentName, zipAllowed,
							out responseCode, out responseBytes, out responseCompressed);
						break;
					case ServerCommand.Purge:
						ProcessPurgeRequest(out responseCode, out responseBytes, out responseCompressed);
						break;
					case ServerCommand.FavIcon:
						ProcessFavIconRequest(out responseCode, out responseBytes);
						break;
					case ServerCommand.Empty:
						ProcessEmptyRequest(out responseCode, out responseBytes);
						break;
					case ServerCommand.Update:
						ProcessUpdateRequest(out responseCode, out responseBytes);
						break;
					case ServerCommand.Mapping:
						ProcessMappingRequest(out responseCode, out responseBytes);
						break;
				}

			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception encountered while processing request {0} from {1}: {2}", context.Request.RawUrl,
								clientEndPoint, e);

				responseBytes = encoding.GetBytes(string.Format("Error processing request: {0}", e.Message));
				responseCode = 500;
			}
			finally
			{
				if (response != null)
				{
					try
					{
						response.Headers.Add(serverHeader, serverName);
						
						if (responseGeneric)
						{
							response.Headers.Add("IsGeneric", "true");
						}
						
						if (responseEncrypted)
						{
							response.Headers.Add("X-IsEncrypted", "true");
						}

						response.StatusCode = responseCode;
						if (responseBytes != null)
						{
							if (responseCompressed)
							{
								response.Headers.Add(HttpResponseHeader.ContentEncoding, "gzip");
							}

							response.ContentLength64 = responseBytes.Length;
							response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
						}
						else
						{
							response.ContentLength64 = 0;
						}
						response.Close();
					}
					catch (HttpListenerException hle)
					{
						log.ErrorFormat("Error sending response to {0}: {1}", clientEndPoint, hle.Message);
					}
					catch (Exception e)
					{
						log.ErrorFormat("Exception sending response to {0}: {1}", clientEndPoint, e);
					}
					finally
					{
						counters.DequeueRequest(responseCode);
					}
				}
			}
		}

		private static bool IsZipAllowed(HttpListenerRequest request)
		{
			string acceptEncoding = request.Headers["Accept-Encoding"];
			if (string.IsNullOrEmpty(acceptEncoding))
			{
				return false;
			}
			return acceptEncoding.Contains("gzip");
		}
	
		private static void ProcessMappingRequest(out int responseCode, out byte[] responseBytes)
		{
			string mapping;
			try
			{
				mapping = SectionMapper.GetCurrentMappingXml();
				responseCode = 200;
			}
			catch (Exception ex)
			{
				responseCode = 500;
				mapping = string.Format("Exception getting mapping: {0}", ex);
			}
			
			responseBytes = encoding.GetBytes(mapping);
		}

		private static void ProcessUpdateRequest(out int responseCode, out byte[] responseBytes)
		{
			string status;
			try
			{
				LocalCache.Update();
				responseCode = 200;
				status = "All cached sections updated";
			}
			catch (Exception ex)
			{
				responseCode = 500;
				status = string.Format("Update Failed: {0}", ex);
			}

			responseBytes = encoding.GetBytes(status);
		}

		private static IPEndPoint GetClientEndPoint(HttpListenerRequest request)
		{
			string clientIPString = request.Headers.Get("Client-IP"); //if the request is coming from a netscaler, the original ip will be here
			if (string.IsNullOrEmpty(clientIPString))
			{
				return request.RemoteEndPoint;
			}
			
			IPAddress clientIP;
			if(IPAddress.TryParse(clientIPString, out clientIP))
			{
				IPEndPoint clientEndPoint = new IPEndPoint(clientIP, 80); //80 is dummy since the scaler doesn't provide it and we don't much care about the port anyway
				return clientEndPoint;
			}
			
			log.WarnFormat("Could not parse supplied IP string {0}. Using Request remote end point.");
			return request.RemoteEndPoint;

		}

		private static void ProcessFavIconRequest(out int responseCode, out byte[] responseBytes)
		{
			responseCode = 200;
			responseBytes = favIconResponse;
		}

		private static void ProcessEmptyRequest(out int responseCode, out byte[] responseBytes)
		{
			responseCode = 200;
			responseBytes = emptyResponse;
		}
		
		private static void ProcessPurgeRequest(out int responseCode, out byte[] responseBytes, out bool responseCompressed)
		{
			string status;
			try
			{
				SectionMapper.Purge();
				responseCode = 200;
				status = "Purge complete";
			}
			catch (Exception ex)
			{
				responseCode = 500;
				status = string.Format("Purge Failed: {0}", ex);
			}

			responseBytes = encoding.GetBytes(status);
			responseCompressed = false;
		}

		private static void ProcessStatusRequest(string sectionName, string environmentName, bool zipAllowed,
			out int responseCode, out byte[] responseBytes, out bool responseCompressed)
		{
			string status;
			if (String.IsNullOrEmpty(sectionName)) //get all stats
			{
				status = clientStatistics.GetStatistics();
				responseCode = 200;
			}
			else if (String.IsNullOrEmpty(environmentName)) //get stats for just this section
			{
				status = clientStatistics.GetStatistics(sectionName);
				responseCode = 200;
			}
			else  //get for only the specified environment
			{
				status = clientStatistics.GetStatistics(sectionName, environmentName);
				responseCode = 200;
			}
			
			responseBytes = encoding.GetBytes(status);
			
			if (zipAllowed && responseBytes.Length > compressionThreshold)
			{
				responseBytes = compressor.Compress(responseBytes, true, Common.IO.CompressionImplementation.ManagedZLib);
				responseCompressed = true;
			}
			else
			{
				responseCompressed = false;
			}
		}

		private static void ProcessGetRequest(EndPoint requester, string sectionName, string currentHash, string environmentName, bool zipAllowed,
			 out int responseCode, out byte[] responseBytes, out bool responseCompressed, out bool responseGeneric, out bool responseEncrypted)
		
		{
			if (string.IsNullOrEmpty(sectionName))
				throw new ArgumentException("HttpServer.ProcessGetRequest(): no sectionName provided.");

			IConfigurationSystemSectionProvider sectionProvider = SectionProviderFactory.GetProvider(sectionName);
			string currentEnvironment = environmentName;
			if (currentEnvironment == string.Empty)
				currentEnvironment = null;

			if (sectionProvider != null)
			{
				ConfigurationItem sectionItem = SectionMapper.GetConfigurationItem(sectionName, requester, ref currentEnvironment);
				if (sectionItem != null && sectionItem.DataBytes != null)
				{
					responseGeneric = sectionItem.IsGeneric;

					if (currentHash == null || currentHash != sectionItem.HashString)
					{
						if (log.IsDebugEnabled)
						{
							if (currentHash == null)
							{
								log.DebugFormat("Request for {0} from {1} found item with {2} bytes and hash {3}. 200.", sectionName, requester, sectionItem.DataBytes.Length, sectionItem.HashString);
							}
							else
							{
								log.DebugFormat("Request for {0}:{1} from {2} found item with {3} bytes and hash {4}. 200.", sectionName, currentHash, requester, sectionItem.DataBytes.Length, sectionItem.HashString);
							}
						}

						

						if (sectionItem.Encrypt)
						{
							if (sectionItem.EncryptedDataBytes != null)
							{
								responseCode = 200;
								responseEncrypted = true;
								responseBytes = sectionItem.EncryptedDataBytes;
							}
							else
							{
								responseCode = 500;
								string errorString = string.Format("Section {0} is set to encrypted, but does not have encrypted bytes available. Server likely is not properly configured for encryption.",
														   sectionName);
								log.Error(errorString);
								responseBytes = encoding.GetBytes(errorString);
								responseEncrypted = false;
							}
							responseCompressed = false; //encryption renders compression useless, and if we compressed before encrypting clients wouldn't know what to do with it

						}
						else
						{
							responseCode = 200;
							responseEncrypted = false;
							if (zipAllowed && sectionItem.DataBytes.Length > compressionThreshold)
							{
								responseBytes = sectionItem.CompressedDataBytes;
								responseCompressed = true;

							}
							else
							{
								responseBytes = sectionItem.DataBytes;
								responseCompressed = false;
							}	
						}
						

					}
					else
					{
						if (log.IsDebugEnabled)
							log.DebugFormat("Request for {0}:{1} from {2} matched hash. 304", sectionName, currentHash, requester);

						responseCode = 304;
						responseBytes = null;
						responseCompressed = false;
						responseEncrypted = false;
					}

					RegisterClientGet(requester, currentEnvironment, sectionItem.Name, sectionItem.HashString, sectionItem.Src);
				}
				else
				{
					responseCompressed = false;
					responseGeneric = false;
					responseEncrypted = false;
					
					if(sectionItem != null && !string.IsNullOrEmpty(sectionItem.ErrorMessage))
					{
						string errorString = string.Format("Request for {0} from {1} has error \"{2}\". No ConfigurationItem available.",
														   sectionName, requester, sectionItem.ErrorMessage);
						log.Error(errorString);
						
						responseCode = 500;
						responseBytes = encoding.GetBytes(errorString);
					}
					else
					{
						log.ErrorFormat("Request for {0} from {1} not found. No ConfigurationItem available.", sectionName, requester);
						responseCode = 404;
						responseBytes = encoding.GetBytes(string.Format("ConfigurationItem for item {0} not found", sectionName));	
					}
					
				}
			}
			else
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("Request for {0} from {1} not found. No SectionProvider available.", sectionName, requester);
				responseCompressed = false;
				responseGeneric = false;
				responseEncrypted = false;
				responseCode = 404;
				responseBytes = encoding.GetBytes(string.Format("SectionProvider for item {0} not found", sectionName));
			}
		}

		static readonly WaitCallback registerGetCallback = RegisterClientGetCallback;

		private static void RegisterClientGetCallback(object state)
		{
			GetResultInfo resultInfo = state as GetResultInfo; 
			if (resultInfo != null)
			{
				clientStatistics.RegisterGet(resultInfo.Requestor, resultInfo.RequestorEnvironment, resultInfo.SectionName, resultInfo.ReturnedHash, resultInfo.Src);
			}
		}
		
		private static void RegisterClientGet(EndPoint requester, string requestorEnvironment, string sectionName, string returnedHash, string src)
		{
			ThreadPool.UnsafeQueueUserWorkItem(registerGetCallback, new GetResultInfo(requester, requestorEnvironment, sectionName, returnedHash, src));
		}
		
		private static void ExtractRequestParameters(string requestUri, 
			out ServerCommand command, out string sectionName, out string environmentName, out string hash)
		{
			
			//for now sticking with manual parsing because we have a limited command set. 
			//if the command set gets much bigger we can switch to just using HttpUtility parsing and a name/value collection.
			char[] queryDelimeters = {'?', '='}; //. is a hack to get favicon to work with enum parsing
			// /{command}/{sectionName}/{hash}?env={envName}
			string[] querySplit = requestUri.Split(queryDelimeters); //0=command string, then 1/2 = name/value etc
			string[] slashSplit = querySplit[0].Split('/');

			
			if (slashSplit.Length < 2)
			{
				throw new ApplicationException("Request Uri Invalid - " + requestUri);
			}

			if(querySplit.Length == 3 && querySplit[1] == "env") //there was the one query string value we expect 
			{
				environmentName = querySplit[2];
			}
			else
			{
				environmentName = String.Empty;
			}
			
			//0- empty
			//1- command
			//2- sectionName
			//3- hash
			string commandString = slashSplit[1];

			command = commandString.Length == 0 ? ServerCommand.Empty : serverCommandLookupTable[commandString[0]];

			//if (!serverCommandLookup.TryGetValue(commandString, out command))
			//{
			//    throw new ApplicationException("Invalid command - " + commandString);
			//}

			//switch (commandString)
			//{
			//    case "get":
			//        command = ServerCommand.Get;
			//        break;
			//    case "status":
			//        command = ServerCommand.Status;
			//        break;
			//    case "purge":
			//        command = ServerCommand.Purge;
			//        break;
			//    case "favicon.ico":
			//        command = ServerCommand.FavIcon;
			//        break;
			//    case "":
			//        command = ServerCommand.Empty;
			//        break;
			//    default:
			//        throw new ApplicationException("Invalid command - " + commandString);
			//}

			

			sectionName = slashSplit.Length > 2 ? slashSplit[2] : null;
			if (sectionName != null)
			{
				sectionName.Trim();
			}
			
			hash = slashSplit.Length > 3 ? slashSplit[3] : null;
		}
	   
	}

	internal class GetResultInfo
	{
		internal EndPoint Requestor;
		internal string SectionName;
		internal string ReturnedHash;
		internal string RequestorEnvironment;
		internal string Src; // Source location of the result

		internal GetResultInfo(EndPoint requestor, string requestorEnvironment, string sectionName, string returnedHash, string src)
		{
			Requestor = requestor;
			RequestorEnvironment = requestorEnvironment;
			SectionName = sectionName;
			ReturnedHash = returnedHash;
			Src = src;
		}

	}
}
