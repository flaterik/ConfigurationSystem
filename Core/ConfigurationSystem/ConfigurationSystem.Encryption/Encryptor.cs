
using System.Security.Cryptography;
using System.Reflection;
using System;
using System.Text;

namespace MySpace.ConfigurationSystem.Encryption
{
	/// <summary>
	/// Provides encryption and decryption using the RijndaelManaged algorithm.
	/// </summary>
	public class Encryptor
	{
		/// <summary>
		/// The type string for the key provider that will be used if none is supplied.
		/// </summary>
		/// 
		public const string DefaultKeyProviderType = "MySpace.ConfigurationSystem.Encryption.Keys.KeyProvider, MySpace.ConfigurationSystem.Encryption.Keys";

		ICryptoTransform _encryptTransformer;
		ICryptoTransform _decryptTransformer;
		IKeyProvider _keyProvider;
		readonly Encoding _encoding = new UTF8Encoding();

		/// <summary>
		/// Creates a new encryptor using the default key provider.
		/// </summary>
		public Encryptor() : this(DefaultKeyProviderType)
		{
		}
		
		/// <summary>
		/// Creates a new encryptor using the supplied key provider.
		/// </summary>
		/// <param name="keyProvider">The implementation of MySpace.ConfigurationSystem.Encryption.IKeyProvider that will supply the keys.</param>
		public Encryptor(IKeyProvider keyProvider)
		{
			if(keyProvider == null)
				throw new ArgumentNullException("keyProvider");
			Init(keyProvider);
		}

		/// <summary>
		/// Creates a new encryptor using the supplied key provider type.
		/// </summary>
		/// <param name="keyProviderType">A fully qualified type,assembly string for the implementation of MySpace.ConfigurationSystem.Encryption.IKeyProvider that will supply the keys. /></param>
		public Encryptor(string keyProviderType)
		{
			IKeyProvider keyProvider = GetKeyProvider(keyProviderType);
			if (keyProvider != null)
			{
				Init(keyProvider);
			}
			else
			{
				throw new Exception(String.Format("Could not get key provider with type name {0}, please ensure that the designated assembly is available.", keyProviderType));
			}
		}

		/// <summary>
		/// If the default provider type can be loaded, then <paramref name="encryptor"/> will be instantiated with it, otherwise encryptor will be null. 
		/// </summary>
		/// <param name="encryptor">The instance of Encryptor that was created, if any.</param>
		/// <returns>Whether creating the encryptor was successful.</returns>
		public static bool TryGetEncryptor(out Encryptor encryptor)
		{
			return TryGetEncryptor(DefaultKeyProviderType, out encryptor);
		}

		/// <summary>
		/// If the type defined by <paramref name="keyProviderType"/> can be loaded as a key provider, then <paramref name="encryptor"/> will be instantiated with it, otherwise encryptor will be null. 
		/// </summary>
		/// <param name="keyProviderType">A fully qualified type,assembly string for the implementation of MySpace.ConfigurationSystem.Encryption.IKeyProvider that will supply the keys. /></param>
		/// <param name="encryptor">The instance of Encryptor that was created, if any.</param>
		/// <returns>Whether creating the encryptor was successful.</returns>
		public static bool TryGetEncryptor(string keyProviderType, out Encryptor encryptor)
		{
			IKeyProvider keyProvider = GetKeyProvider(keyProviderType);
			if (keyProvider != null)
			{
				encryptor = new Encryptor(keyProvider);
				return true;
			}
			
			encryptor = null;
			return false;
		}
		
		/// <summary>
		/// Returns whether the default key provider type string is avaiable.
		/// </summary>
		public static bool IsKeyProviderAvailable()
		{
			return IsKeyProviderAvailable(DefaultKeyProviderType);
		}

		/// <summary>
		/// Returns whether the supplied key provider type string is avaiable.
		/// </summary>
		public static bool IsKeyProviderAvailable(string keyProviderType)
		{
			return (GetKeyProvider(keyProviderType) != null);
		}

		private void Init(IKeyProvider keyProvider)
		{
			if (keyProvider == null)
			{
				throw new ArgumentNullException("keyProvider");
			}
			_keyProvider = keyProvider;
			SymmetricAlgorithm algorithm = SymmetricAlgorithm.Create("Rijndael");
			if (algorithm != null)
			{
				_encryptTransformer = algorithm.CreateEncryptor(_keyProvider.GetKey(), _keyProvider.GetIV());
				_decryptTransformer = algorithm.CreateDecryptor(_keyProvider.GetKey(), _keyProvider.GetIV());
			}
			else
			{
				throw new Exception(String.Format("Algorithm {0} could not be instantiated", "Rijndael"));
			}
		}

		private static IKeyProvider GetKeyProvider(string keyProviderTypeName)
		{
			Type keyProviderType = Type.GetType(keyProviderTypeName);
			if (keyProviderType == null)
			{
				return null;
			}
			Assembly keyProviderAssembly = Assembly.GetAssembly(keyProviderType);
			IKeyProvider keyProvider = keyProviderAssembly.CreateInstance(keyProviderType.FullName) as IKeyProvider;
			return keyProvider;
		}

		/// <summary>
		/// Encrypts the UTF8 encoded bytes of <paramref name="unencryptedString"/> and returns the result as a base 64 encoded string.
		/// </summary>
		/// <param name="unencryptedString">The string to encrypt.</param>
		/// <returns>The encrypted string in a base 64 encoding.</returns>
		public string Encrypt(string unencryptedString)
		{
			byte[] encryptedBytes = Encrypt(_encoding.GetBytes(unencryptedString));
			return Convert.ToBase64String(encryptedBytes);
		}

		/// <summary>
		/// Decrypts <paramref name="encryptedBase64String"/> and returns the result.
		/// </summary>
		/// <param name="encryptedBase64String">The string to decrypt. It must be a base 64 encoding of the results of encrypting the UTF8 encoded bytes of a string.</param>
		/// <returns>The decrypted string.</returns>
		public string Decrypt(string encryptedBase64String)
		{
			byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64String);
			return _encoding.GetString(Decrypt(encryptedBytes));
		}

		/// <summary>
		/// Encrypts <paramref name="unencryptedBytes"/>.
		/// </summary>
		/// <param name="unencryptedBytes">The bytes to encrypt.</param>
		/// <returns>The encrypted bytes.</returns>
		public byte[] Encrypt(byte[] unencryptedBytes)
		{
			return _encryptTransformer.TransformFinalBlock(unencryptedBytes, 0, unencryptedBytes.Length);
		}

		/// <summary>
		/// Decrypts <paramref>encryptedBytes</paramref>.
		/// </summary>
		/// <param name="encryptedBytes">The bytes to decrypt.</param>
		/// <returns>The decrypted bytes.</returns>
		public byte[] Decrypt(byte[] encryptedBytes)
		{
			return _decryptTransformer.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
		}
	}
}
