using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;

namespace MySpace.ConfigurationSystem
{
    internal class ConfigurationItem
    {
        static readonly MD5 md5 = MD5.Create();
        static readonly Encoding encoding = new UTF8Encoding(false);

        private readonly string sectionName;
        private string sectionString;
    	private byte[] sectionBytes;
        private byte[] sectionHash;
        private string sectionHashString;
		internal Dictionary<string, string> GenericSettingCollection = new Dictionary<string, string>();

        private DateTime lastChecked;

        internal DateTime LastChecked
        {
            get { return lastChecked; }
        }
        
        internal string SectionName
        {
            get { return sectionName; }
        }

        internal string SectionString
        {
            get { return sectionString; }
			set
			{
				sectionBytes = encoding.GetBytes(value);
				sectionHash = md5.ComputeHash(sectionBytes);
				sectionHashString = GetHashString(sectionHash);
				sectionString = value;
			}
        }

        internal void UpdateLastChecked()
        {
            lastChecked = DateTime.Now;
        }

        internal byte[] SectionStringBytes
        {
            get
            {
                return sectionString == null ? null : encoding.GetBytes(sectionString);
            }
        }
        
        internal byte[] SectionHash
        {
            get { return sectionHash; }
        }

        internal string SectionHashString
        {
            get { return sectionHashString; }
        }

		//TODO: make the fetch process skip the intermediate string steps, and only do byte/string conversion a minimum number of times
        internal ConfigurationItem(string sectionName, string sectionString)
        {
            this.sectionName = sectionName;
            this.sectionString = sectionString;

        	sectionBytes = encoding.GetBytes(sectionString);
            sectionHash = md5.ComputeHash(sectionBytes);
            sectionHashString = GetHashString(sectionHash);
            lastChecked = DateTime.MinValue;
        }

        internal ConfigurationItem(string sectionName, byte[] sectionStringBytes) :
            this(sectionName, encoding.GetString(sectionStringBytes))
        {
        }

        internal bool CheckedWithinInterval(int intervalSeconds)
        {
            return (lastChecked.AddSeconds(intervalSeconds) > DateTime.Now);
        }

    	internal bool NeverChecked
    	{
			get { return lastChecked == DateTime.MinValue; }
    	}

        internal bool HashCodeEquals(ConfigurationItem item)
        {
            return byteArraysEqual(sectionHash, item.SectionHash);
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
            if (currentHash != null)
            {
                StringBuilder hashBuilder = new StringBuilder(currentHash.Length * 2);
                for (int i = 0; i < currentHash.Length; i++)
                {
                    hashBuilder.Append(currentHash[i].ToString("x2"));
                }
                return hashBuilder.ToString();
            }
            
            
            return string.Empty;
        }

        internal bool HashCodeEquals(byte[] hash)
        {
            return byteArraysEqual(sectionHash, hash);
        }
    }
}
