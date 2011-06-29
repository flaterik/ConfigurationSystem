using System;
using System.Configuration;
using System.IO;
using System.Web;
using System.Xml;
using MySpace.Configuration;
using MySpace.Logging;

namespace MySpace.ConfigurationSystem
{
	/// <summary>
	/// <para>
	/// A replacement for the <see cref="ConfigurationSection"/> base class that adds support
	/// for a <see cref="GetSection"/> method, Configuration System and automatic refresh 
	/// (either from local files or Configuration System).
	/// </para>
	/// </summary>
	/// <typeparam name="TImpl">
	///	<para>
	///	The non-abstract descendent of this class.
	///	The implementation class must be decorated with a <see cref="ConfigurationSectionNameAttribute"/>
	///	to specify the section name.
	///	</para>
	/// </typeparam>
	public abstract class ConfigurationSectionHandler<TImpl> : ConfigurationSection
		where TImpl : ConfigurationSectionHandler<TImpl>
	{
		//Because this class is generic, the static members below apply to the specific
		//type of generic so ConfigurationSectionHandler<Dog> will have different static
		//members than ConfigurationSectionHandler<Cat> 
		private static readonly LogWrapper _log = new LogWrapper();
		private static readonly object _syncRoot = new object();
		private static string _sectionName;
		private static volatile bool _watcherInitialized;
		private static TImpl _section;

		private static string GetSectionName()
		{
			if (_sectionName == null)
			{
				object[] attributes =
					typeof(TImpl).GetCustomAttributes(typeof(ConfigurationSectionNameAttribute), false);
				if (attributes.Length > 0)
				{
					var attribute = (ConfigurationSectionNameAttribute)attributes[0];
					if (string.IsNullOrEmpty(attribute.Name))
						throw new ApplicationException(
							string.Format("The ConfigurationSectionName attribute for {0} has a null or empty name",
							              typeof (TImpl).FullName));

					_sectionName = attribute.Name;
				}
				else
				{
					throw new ApplicationException(
						string.Format("The {0} class must be marked with a ConfigurationSectionName attribute.",
						              typeof (TImpl).FullName));
				}
			}
			return _sectionName;
		}

		private static void LoadSection(bool throwOnSectionNotFound)
		{
			try
			{
				_section = (TImpl) ConfigurationManager.GetSection(GetSectionName()) ?? LoadFromConfigServer();
			}
			catch (Exception x)
			{
				if (throwOnSectionNotFound)
				{
					throw new ApplicationException(
						String.Format("Exception encountered when opening configuration section {0}.", GetSectionName()), x);
				}
			}

			if (_section == null)
			{
				if (throwOnSectionNotFound)
				{
					throw new ApplicationException(
						String.Format("Configuration Section {0} is expected but not found.", GetSectionName()));
				}
			}
		}

		private static TImpl LoadFromConfigServer()
		{
			var now = DateTime.UtcNow;
			if (_lastGetAttempt != null && (now - _lastGetAttempt) < MissingSectionRetryInterval)
				return null;
			_lastGetAttempt = now;

			var sectionXml = ConfigurationClient.GetSectionXml(GetSectionName());
			if (sectionXml != null)
			{
				var reader = new XmlNodeReader(sectionXml);
				if (!reader.IsStartElement(GetSectionName()))
					throw new ConfigurationSystemException(GetSectionName(),
						string.Format("Configuration does not contain the {0} element.", GetSectionName()));

				var type = typeof (TImpl);

				TImpl section;
				try
				{
					section = (TImpl)Activator.CreateInstance(type, true);
				}
				catch (Exception e)
				{
					_log.Error(GetSectionName(), e);
					throw;
				}

				section.DeserializeElement(reader, false);
				RegisterWatch(null, GetSectionName());

				return section;
			}

			return null;
		}

		/// <summary>
		/// Gets or sets the missing section retry interval.  If a section is not defined in
		/// app.config/web.config and is not found on the configuration server, subsequent calls to 
		/// <see cref="GetSection"/> will not hit the configuration server for at least this
		/// interval.
		/// </summary>
		/// <value>
		/// A <see cref="TimeSpan"/> that specifies missing section retry interval.  The default is 5 minutes.
		/// </value>
		public static TimeSpan MissingSectionRetryInterval
		{
			get { return _missingSectionRetryInterval; }
			set { _missingSectionRetryInterval = value; }
		}
		private static TimeSpan _missingSectionRetryInterval = new TimeSpan(0, 5, 0);
		private static DateTime? _lastGetAttempt;

		/// <summary>
		/// Loads the <typeparamref name="TImpl"/> section or returns a cached copy.  This method
		/// be used in favor of <see cref="ConfigurationManager.GetSection"/> because it will attempt
		/// to load the section from the configuration server even if it is not listed in the app config
		/// file.
		/// </summary>
		/// <param name="throwOnSectionNotFound">
		/// <see langword="true"/> to throw an exception rather than returning <see langword="null"/>
		/// when a valid configuration section is not found; <see langword="false"/> to return
		/// <see langword="null"/> when the section is not found.
		/// </param>
		/// <returns>
		/// The <typeparamref name="TImpl"/> read from the app config file;	<see langword="null"/>
		/// if no such config section is found.
		/// </returns>
		/// <exception cref="ApplicationException">
		/// The implementation class is not marked with a <see cref="ConfigurationSectionNameAttribute"/>.
		/// </exception>
		/// <exception cref="ConfigurationSystemException">
		/// The section was not successfully loaded and <paramref name="throwOnSectionNotFound"/> is 
		/// <see langword="true"/>.
		/// </exception>
		public static TImpl GetSection(bool throwOnSectionNotFound)
		{
			// Verify class is properly decorated with a ConfigurationSectionNameAttribute.
			GetSectionName();

			if (_section == null)
			{
				lock (_syncRoot)
				{
					if (_section == null)
					{
						LoadSection(throwOnSectionNotFound);
					}
				}
			}

			return _section;
		}

		/// <summary>
		/// Reads XML from the configuration file.
		/// </summary>
		/// <param name="reader">The <see cref="T:System.Xml.XmlReader"/> that reads from the configuration file.</param>
		/// <param name="serializeCollectionKey">true to serialize only the collection key properties; otherwise, false.</param>
		/// <exception cref="T:System.Configuration.ConfigurationErrorsException">The element to read is locked.- or -An attribute of the current node is not recognized.- or -The lock status of the current node cannot be determined.  </exception>
		protected override void DeserializeElement(XmlReader reader, bool serializeCollectionKey)
		{
			string watchFilePath = null;
			string watchRemoteSection = null;

			if (reader.HasAttributes || reader.IsEmptyElement)
			{
				try
				{
					var remoteSectionName = reader.HasAttributes ? reader.GetAttribute("remoteSectionName") : GetSectionName();
					if (remoteSectionName != null)
					{
						if (remoteSectionName == string.Empty) remoteSectionName = GetSectionName();
						var xml = ConfigurationClient.GetSectionXml(remoteSectionName);
						reader = new XmlNodeReader(xml);
						if (!reader.IsStartElement(GetSectionName()))
							throw new ConfigurationSystemException(GetSectionName(), 
								string.Format("Configuration does not contain the {0} element.", GetSectionName()));
						watchRemoteSection = remoteSectionName;
					}
				}
				catch (Exception ex)
				{
					_log.Debug(ex);
					throw;
				}
			}
			
			if (watchRemoteSection == null && !string.IsNullOrEmpty(SectionInformation.ConfigSource))
			{
				watchFilePath = SectionInformation.ConfigSource;
			}
			
			base.DeserializeElement(reader, serializeCollectionKey);

			RegisterWatch(watchFilePath, watchRemoteSection);
		}

		// I’m somewhat skeptical that this will yield the right answer for web applications.  If this
		// is ever used in a web application and it is determined that updates to local config files
		// are not being detected, make sure this is returning the right directory path.
		private static string ConfigRoot
		{
			get
			{
				var cfgRoot = System.Web.Hosting.HostingEnvironment.IsHosted
				              	? HttpRuntime.AppDomainAppPath
				              	: string.IsNullOrEmpty(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile)
				              	  	? AppDomain.CurrentDomain.SetupInformation.ApplicationBase
				              	  	: Path.GetDirectoryName(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile);

				return string.IsNullOrEmpty(cfgRoot) ? Environment.CurrentDirectory : cfgRoot;
			}
		}

		private static void RegisterWatch(string filePath, string remoteSection)
		{
			if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(remoteSection))
				throw new ArgumentException("Only one of filePath and remoteSection may be specified.");

			try
			{
				lock (_syncRoot)
				{
					if (!_watcherInitialized)
					{
						if (!string.IsNullOrEmpty(filePath))
						{
							ConfigurationWatcher.WatchFile(Path.Combine(ConfigRoot, filePath), ConfigChanged);
							_watcherInitialized = true;
						}
						else if (!string.IsNullOrEmpty(remoteSection))
						{
							ConfigurationWatcher.WatchRemoteSection(remoteSection, ConfigChanged);
							_watcherInitialized = true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				_log.Error(ex);
			}
		}

		/// <summary>
		/// 	<para>Causes the <see cref="Refreshed"/> event to be manually raised.
		///		Should be called whenever a configuration element is modified.</para>
		/// </summary>
		public static void TriggerRefresh()
		{
			var refreshed = Refreshed;
			if (refreshed != null)
			{
				refreshed();
			}
		}

		/// <summary>
		///		<para>Invoked when this configuration section has been modified and refreshed.</para>
		/// </summary>
		public static event ConfigurationSectionRefreshedEventHandler Refreshed;

		private static void ConfigChanged(string name)
		{
			_log.InfoFormat("Configuration section '{0}' is being refreshed...", GetSectionName());

			ConfigurationManager.RefreshSection(GetSectionName());
			//clear to cause a reload
			_section = null;
			_lastGetAttempt = null;
			TriggerRefresh();
		}

		private bool? _isReadOnlyOverride;

		/// <summary>
		/// 	<para>Overriden. Gets a value indicating whether the
		///		<see cref="ConfigurationElement"/> object is read-only.</para>
		/// </summary>
		/// <returns>
		/// 	<para>true if the <see cref="ConfigurationElement"/> object is
		///		read-only; otherwise, false.</para>
		/// </returns>
		public override bool IsReadOnly()
		{
			return _isReadOnlyOverride.HasValue ? _isReadOnlyOverride.Value :
				base.IsReadOnly();
		}

		/// <summary>
		/// Allows modifications of this instance for test purposes.
		/// </summary>
		/// <param name="modifications">Delegate containing modifications to this
		/// instance.</param>
		public void ModifyForTest(Action modifications)
		{
			if (modifications == null) throw new ArgumentNullException("modifications");
			try
			{
				_isReadOnlyOverride = false;
				modifications();
			}
			finally
			{
				_isReadOnlyOverride = null;
			}
		}
	}
}