namespace MySpace.ConfigurationSystem
{
    public interface IConfigurationSystemSectionProvider
    {
		bool TryGetDataBytes(string itemPath, out byte[] dataBytes, out string errorMessage);
    }
}
