using System;

namespace MySpace.ConfigurationSystem
{
	/// <summary>
	/// Indicates an exception with a configuration section that is stored on the configuration system
	/// </summary>
	public class ConfigurationSystemException : Exception
	{
		private readonly string _sectionName;
		private readonly string _errorMessage;

		/// <summary>
		/// The name of the section that experienced a problem.
		/// </summary>
		public string SectionName
		{
			get { return _sectionName; }
		}

		/// <summary>
		/// Any information available about the problem with the section.
		/// </summary>
		public string ErrorMessage
		{
			get { return _errorMessage; }
		}
		
		/// <summary>
		/// Creates a new ConfigurationSystemException for section <paramref name="sectionName"/> with message <paramref name="errorMessage"/>
		/// </summary>
		public ConfigurationSystemException(string sectionName, string errorMessage) : base(GetErrorMessage(sectionName, errorMessage))
		{
			_sectionName = sectionName;
			_errorMessage = errorMessage;
		}

		/// <summary>
		/// Creates a new ConfigurationSystemException for section <paramref name="sectionName"/> with message <paramref name="errorMessage"/> 
		/// and inner exception <paramref name="innerException"></paramref>
		/// </summary>
		public ConfigurationSystemException(string sectionName, string errorMessage, Exception innerException)
			: base(GetErrorMessage(sectionName, errorMessage), innerException)
		{
			_sectionName = sectionName;
			_errorMessage = errorMessage;
		}

		private static string GetErrorMessage(string sectionName, string errorMessage)
		{
			return string.Format("Error in section {0}: {1}", sectionName, errorMessage);
		}
	}

}
