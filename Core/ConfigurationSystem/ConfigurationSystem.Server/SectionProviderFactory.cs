
namespace MySpace.ConfigurationSystem
{
    internal class SectionProviderFactory
    {
        internal static IConfigurationSystemSectionProvider GetProvider(string section)
        {
            return SectionMapper.GetProviderForSection(section);
        }
    }
}
