using System;
using System.Configuration;
using System.Net;
using System.Text;
using System.IO;
using MySpace.ConfigurationSystem.Encryption;
using MySpace.ResourcePool;
using System.Diagnostics;

namespace MySpace.ConfigurationSystem
{
	internal class HttpTransport : IConfigurationClientTransport
	{
		static readonly Logging.LogWrapper log = new Logging.LogWrapper();
		static readonly MemoryStreamPool streamPool = new MemoryStreamPool(1024, 1000, 10);
		static readonly ResourcePool<byte[]> bufferPool = new ResourcePool<byte[]>(BuildBuffer,1000);
		static readonly Encoding encoding = new UTF8Encoding(false);
		static readonly Common.IO.Compressor compressor = Common.IO.Compressor.GetInstance();
		
		readonly Encryptor _encryptor;
		readonly string _remoteHost;


		internal HttpTransport(string remoteHost, string keyProviderType)
		{
			_remoteHost = remoteHost;
			if(Encryptor.IsKeyProviderAvailable(keyProviderType))
			{
				try
				{
					_encryptor = new Encryptor(keyProviderType);
				}
				catch (Exception e)
				{
					log.WarnFormat("Exception creating encryptor. Encrypted sections will not be available. {0}", e);
				}
			}
		}

		internal static string GetRequestUri(string requestHostName, string sectionName, string currentHash)
		{
			if (currentHash != null)
			{
				return string.Format("http://{0}/get/{1}/{2}", requestHostName, sectionName, currentHash);
			}
			
			return string.Format("http://{0}/get/{1}", requestHostName, sectionName);
		}


		/// <summary>
		/// Checks the section remotely to see if it matches currentHash.
		/// </summary>
		/// <param name="sectionName">The name of the section to retrieve</param>
		/// <param name="currentHash">The md5 hash of the section string you have already, if any.</param>
		/// <param name="sectionString">The retrieved section string, if the hash did not match.</param>
		/// <param name="generic">Whether the retrived section has been defined as generic.</param>
		/// <remarks>This method is not thread safe; do not call it on the same instance from multiple threads simultaneously.</remarks>
		/// <returns>True if the section string did not match currentHash and the sectionString was returned</returns>
		bool IConfigurationClientTransport.GetSectionStringWasNew(string sectionName, string currentHash, out string sectionString, out string generic)
		{	
			
			string requestUri = GetRequestUri(_remoteHost, sectionName, currentHash);

			int responseCode;
			bool sectionWasNew;
			Stopwatch stopwatch = null;
			if (log.IsDebugEnabled)
			{
				stopwatch = Stopwatch.StartNew();
			}
			
			ProcessRequest(sectionName, requestUri, out sectionString, out responseCode,out generic);

			if (log.IsDebugEnabled && stopwatch != null)
			{
				stopwatch.Stop();
				log.DebugFormat("Processed request {0} with response code {1} in {2} ms", requestUri, responseCode, stopwatch.ElapsedMilliseconds);
			}
			
			switch (responseCode)
			{
				case 200: //OK
					sectionWasNew = true;
					break;
				case 304: //Not Modified
					sectionWasNew = false;
					break;
				default:
					//case 404: //Not Found
					//case 500: //Error
					throw new ConfigurationSystemException(sectionName, string.Format("Server returned status {0}", responseCode));
			}

			return sectionWasNew;

		}

		private void ProcessRequest(string sectionName, string requestUri, out string response, out int responseCode,out string generic)
		{
			HttpWebRequest sectionRequest = (HttpWebRequest)WebRequest.Create(requestUri);
			sectionRequest.Headers.Add("Accept-Encoding", "gzip");
			HttpWebResponse sectionResponse = null;
			
			try
			{
				sectionResponse = (HttpWebResponse)sectionRequest.GetResponse();
				responseCode = (int)sectionResponse.StatusCode;
				generic = sectionResponse.Headers.Get("IsGeneric");
				
				switch (sectionResponse.StatusCode)
				{
					case HttpStatusCode.OK: 
						response = ExtractResponseString(sectionName, sectionResponse);
						break;
					default:
						response = null;
						if (log.IsErrorEnabled)
						{
							log.ErrorFormat("Unexpected HttpStatus {0} received when processing request {1}", sectionResponse.StatusCode, requestUri);
						}
						break;

				}
			}
			catch (WebException webex)
			{
				if (webex.Response == null)
				{
					throw new ConfigurationSystemException(sectionName, string.Format("No response from server ({0}).", requestUri));
				}

				HttpWebResponse exResponse = (HttpWebResponse)webex.Response;
				
				responseCode = (int)(exResponse).StatusCode;
				generic = exResponse.Headers.Get("IsGeneric");
				response = null;
				if (responseCode != 304) //why it considers 304 an exception I don't know                
				{
					
					switch(exResponse.StatusCode)
					{
						case HttpStatusCode.InternalServerError:
							string error = ExtractResponseString(sectionName, exResponse);
							throw new ConfigurationSystemException(sectionName, string.Format("Server returned error message: {0}", error));                            
						case HttpStatusCode.NotFound:
							log.ErrorFormat("Request {0} resulted in 404.", requestUri);
							break;
						default:
							log.ErrorFormat("Exception processing request to {0}: {1}", requestUri, webex.ToString());
							break;
					}
					
				}
			}
			finally
			{
				if (sectionResponse != null)
				{
					sectionResponse.Close();
				}
			}
		}

		private string ExtractResponseString(string sectionName, HttpWebResponse sectionResponse)
		{
			Stream responseReadStream = sectionResponse.GetResponseStream();
			string response;
			MemoryStream responseWriteStream;
			ResourcePoolItem<MemoryStream> streamItem = null;
			ResourcePoolItem<byte[]> bufferItem = null;
			try
			{
				string isEncryptedHeader = sectionResponse.Headers.Get("X-IsEncrypted");
				bool encrypted = isEncryptedHeader != null && isEncryptedHeader.ToLowerInvariant() == "true";
				streamItem = streamPool.GetItem();
				bufferItem = bufferPool.GetItem();
				int readCount;
				responseWriteStream = streamItem.Item;
				byte[] readBuffer = bufferItem.Item;
				do
				{
					readCount = responseReadStream.Read(readBuffer, 0, readBuffer.Length);
					responseWriteStream.Write(readBuffer, 0, readCount);
				}
				while (readCount > 0);
				if (responseWriteStream.Length > 0)
				{
					string contentEncoding = sectionResponse.ContentEncoding;
					byte[] responseBytes;
					int responseLength; //because the length of the byte array in the memory stream is not necessary the content length
					if (contentEncoding == "gzip")
					{
						responseBytes = compressor.Decompress(responseWriteStream.GetBuffer(), true, Common.IO.CompressionImplementation.ManagedZLib);
						responseLength = responseBytes.Length;
					}
					else
					{
						responseBytes = responseWriteStream.GetBuffer();
						responseLength = (int)responseWriteStream.Length;
					}

					response = encoding.GetString(responseBytes, 0, responseLength);	
					
					if (encrypted)
					{
						if(_encryptor != null)
							response = _encryptor.Decrypt(response);
						else
						{
							throw new ConfigurationSystemException(sectionName, string.Format("Got encrypted data for section {0}, but no encryptor is available.", sectionName));
						}
					}

				}
				else
				{
					response = null;
				}
			}
			finally
			{
				if (streamItem != null)
				{
					streamItem.Release();
				}
				if (bufferItem != null)
				{
					bufferItem.Release();
				}
			}
			return response;
		}
		
		private static byte[] BuildBuffer()
		{
			return new byte[1024];
		}
	}
}
