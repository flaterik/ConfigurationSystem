namespace MySpace.ConfigurationSystem
{
	internal interface IConfigurationClientTransport
	{
		bool GetSectionStringWasNew(string sectionName, string currentHash, out string sectionString, out string generic);
	}
}
