using System.Security.Cryptography;
using System;

/// <summary>
/// Uses the SymmetricAlgorithm class to generate base 64 encoded initialization vectors and keys. These can be copied from the output to use in key providers.
/// </summary>
public class KeyGenerator
{
	static readonly SymmetricAlgorithm algorithm;
	static KeyGenerator()
	{
		algorithm = SymmetricAlgorithm.Create("Rijndael");
		algorithm.KeySize = 256;
		algorithm.GenerateIV();
		algorithm.GenerateKey();
	}

	/// <summary>
	/// Returns a base 64 encoded key.
	/// </summary>
	public static string GetBase64Key()
	{
		byte[] key = algorithm.Key;
		return Convert.ToBase64String(key);
	}

	/// <summary>
	/// Returns a base 64 encoded initialization vector.
	/// </summary>
	public static string GetBase64IV()
	{
		byte[] iv = algorithm.IV;
		return Convert.ToBase64String(iv);
	}

}