using System;

namespace MySpace.ConfigurationSystem
{
	/// <summary>
	/// Event data for a configuration section changing
	/// </summary>
	public class ConfigurationChangedEventArgs : EventArgs
	{
		/// <summary>
		/// The name of the section that changed
		/// </summary>
		public string SectionName
		{ 
			get;
			private set;
		}

		/// <summary>
		/// The string representing the section that changed
		/// </summary>
		public string SectionString
		{
			get; private set;
		}

		/// <summary>
		/// The bytes of the section that changed
		/// </summary>
		public byte[] SectionStringBytes
		{
			get; private set;
		}

		/// <summary>
		/// Creates a new instance of ConfigurationChangedEventArgs
		/// </summary>
		public ConfigurationChangedEventArgs(string sectionName, string sectionString, byte[] sectionStringBytes)
		{
			SectionName = sectionName;
			SectionString = sectionString;
			SectionStringBytes = sectionStringBytes;
		}

	}
}
