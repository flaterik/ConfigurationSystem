namespace MySpace.ConfigurationSystem.Encryption
{
	/// <summary>
	/// An interface used by Encryptor for supplying keys to its encryption algorithm.
	/// </summary>
	public interface IKeyProvider
	{
		/// <summary>
		/// Returns the key to be used by the encryption algorithm.
		/// </summary>
		byte[] GetKey();
		
		/// <summary>
		/// Returns the initialization vector to be used by the encryption algorithm.
		/// </summary>
		byte[] GetIV();
	}
}