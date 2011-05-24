using System;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using MySpace.ConfigurationSystem.Encryption;
using MySpace.Logging;
using System.Threading;

namespace MySpace.ConfigurationSystem
{
	[Serializable]
	public class ConfigurationItem
	{
		[NonSerialized] MD5 _md5;
		[NonSerialized] Encryptor _encryptor;
		[NonSerialized] ReaderWriterLockSlim _itemLock;
		
		private static readonly Encoding encoding = new UTF8Encoding(false);
		private static readonly Common.IO.Compressor compressor = Common.IO.Compressor.GetInstance();
		private static readonly LogWrapper log = new LogWrapper();

		internal readonly string Name;
		private readonly string _source;
		private string _data;
		private string _encryptedData;
		private byte[] _dataBytes;
		private byte[] _encryptedDataBytes;

		private bool _generic;
		internal string Source { get { return _source; } }
		internal DateTime LastUpdate { get; private set; }
		internal bool Encrypt { get; private set; }
		internal int TimeToLive { get; set; }
		internal string ErrorMessage { get; set; }

		
		
		[OnDeserialized]
		private void RestoreFields(StreamingContext context)
		{
			_md5 = MD5.Create();
			_itemLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
			_encryptor = HttpServer.GetEncryptor();
		}

		private string GetEncryptedData(string data)
		{
			if (_encryptor != null)
				return _encryptor.Encrypt(data);
			
			return null;
		}


		internal ConfigurationItem(string name, string source, string environment, bool encrypt, int timeToLive)
		{
			RestoreFields(new StreamingContext());
			LastUpdate = DateTime.MinValue;
			TimeToLive = timeToLive;
			_source = source;
			Environment = environment;
			Encrypt = encrypt;
			Name = name;
		}

		internal void Reset()
		{
			_data = null;
			_dataBytes = null;
			LastUpdate = DateTime.MinValue;
		}

		internal void Update()
		{
			try
			{
				IConfigurationSystemSectionProvider provider = SectionProviderFactory.GetProvider(Name);
				if(provider != null)
				{
					_itemLock.EnterWriteLock();
					try
					{
						byte[] newBytes;
						string errorMessage;
						if(!provider.TryGetDataBytes(_source, out newBytes, out errorMessage))
						{
							ErrorMessage = errorMessage;
						}
						else
						{
							DataBytes = newBytes;
						}
					}
					finally
					{
						_itemLock.ExitWriteLock();
					}
				}
			
			}
			catch (Exception e)
			{
				log.ErrorFormat("Exception updating Configuration Item {0}: {1}", _source, e);
			}
		}

		// If this cache item is empty or expired, return true
		internal bool IsStale
		{
			get
			{
				if (_data == null)
					return true;
				if (LastUpdate.AddSeconds(ConfigurationSystemServerConfig.DefaultTimeToLive) < DateTime.Now)
					return true; // Data is stale
				return false;
			}
		}

		internal string Data
		{
			get { return _data; }
			set
			{
				Hash = _md5.ComputeHash(encoding.GetBytes(value));
				HashString = GetHashString(Hash);
				_data = value;
				_dataBytes = encoding.GetBytes(_data);
				CompressedDataBytes = compressor.Compress(DataBytes, true, Common.IO.CompressionImplementation.ManagedZLib);
				EncryptedData = GetEncryptedData(_data);
				LastUpdate = DateTime.Now;
			}
		}

		internal bool IsGeneric
		{
			get { return _generic; }
			set { _generic = value; }
		}

		internal byte[] DataBytes
		{
			get
			{
				return _dataBytes;
			}
			set
			{
				Hash = _md5.ComputeHash(value);
				HashString = GetHashString(Hash);
				_dataBytes = value;
				_data = encoding.GetString(value);
				CompressedDataBytes = compressor.Compress(value, true, Common.IO.CompressionImplementation.ManagedZLib);
				EncryptedData = GetEncryptedData(_data);
				
				LastUpdate = DateTime.Now;
			}
		}

		internal byte[] CompressedDataBytes { get; private set; }

		//The base 64 encoded encrypted bytes
		internal string EncryptedData
		{
			get
			{
				return _encryptedData;
			}
			private set
			{
				_encryptedData = value;
				if (value != null)
					_encryptedDataBytes = encoding.GetBytes(value);
				else
					_encryptedDataBytes = null;
			}
		}


		internal byte[] EncryptedDataBytes
		{
			get
			{
				if (_encryptedData == null && _encryptor != null)
				{
					//data was saved when an encryptor was not available, but it is currently available
					_encryptedData = GetEncryptedData(_data);
					_encryptedDataBytes = encoding.GetBytes(_encryptedData);
				}
				return _encryptedDataBytes;
			}
		}

		internal byte[] Hash { get; private set; }

		internal string HashString { get; private set; }

		internal string Environment { get; private set; }

		internal bool HashCodeEquals(ConfigurationItem item)
		{
			return byteArraysEqual(Hash, item.Hash);
		}

		private static bool byteArraysEqual(byte[] array1, byte[] array2)
		{
			if (array1 == null)
			{
				throw new ArgumentNullException("array1");
			}
			if (array2 == null)
			{
				throw new ArgumentNullException("array2");
			}

			if (array1.Length != array2.Length)
			{
				return false;
			}

			for (int i = 0; i < array1.Length; i++)
			{
				if (array1[i] != array2[i])
				{
					return false;
				}
			}
			return true;
		}

		private static string GetHashString(byte[] currentHash)
		{
			if (currentHash == null)
			{
				return string.Empty;
			}

			StringBuilder hashBuilder = new StringBuilder(currentHash.Length * 2);

			for (int i = 0; i < currentHash.Length; i++)
			{
				hashBuilder.Append(currentHash[i].ToString("x2"));
			}

			return hashBuilder.ToString();

		}

		internal bool HashCodeEquals(byte[] hash)
		{
			return byteArraysEqual(Hash, hash);
		}

		
	}
}
