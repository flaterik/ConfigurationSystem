using System.IO;
using System;

namespace MySpace.ConfigurationSystem
{
	public class FileProvider : IConfigurationSystemSectionProvider
	{
		public string GetProviderName()
		{
			return "FileProvider";
		}

		public bool TryGetDataBytes(string itemPath, out byte[] dataBytes, out string errorMessage)
		{
			FileStream itemFile = null;
			if (File.Exists(itemPath))
			{
				try
				{
					byte[] buffer = new byte[1024];
					int read;
					itemFile = new FileStream(itemPath, FileMode.Open, FileAccess.Read);
					MemoryStream ms = new MemoryStream();
					do
					{
						read = itemFile.Read(buffer, 0, 1024);
						ms.Write(buffer, 0, read);
					} while (read > 0);
					dataBytes = ms.ToArray();
					errorMessage = null;
					return true;
				}
				catch (Exception e)
				{
					dataBytes = null;
					errorMessage = e.Message;
					return false;
				}
				finally
				{
					if (itemFile != null)
					{
						itemFile.Dispose();
					}
				}
			} 
			//else the file does not exist
			errorMessage = string.Format(
				"FileProvider could not find file {0}.", itemPath);
			
			dataBytes = null;
			return false;
		}
	}
}
