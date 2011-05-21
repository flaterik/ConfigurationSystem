using System.DirectoryServices;
using System.Net;

namespace MySpace.ConfigurationSystem.EnvironmentHelpers
{
	internal class DirectorySearchResultState
	{
		internal HandleWithCount _handle;
		internal SearchResult _host;
		internal int _index;
		internal IPAddress[][] _addressResults;
	}

	internal class VipSearchResultState
	{
		internal HandleWithCount _handle;
		internal string _host;
		internal int _index;
		internal IPAddress[][] _addressResults;
	}
}
