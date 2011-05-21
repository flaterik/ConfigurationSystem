using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MySpace.Logging;
using MySpace.Shared.Configuration;
using MySpace.Configuration;

namespace MySpace.ConfigurationSystem
{
	/// <summary>
	/// Watches config files and config sections hosted on the configuration server system for changes.
	/// </summary>
	public class ConfigurationWatcher
	{
		/// <summary>
		/// A delegate that will be called when a configuration file or remotely hosted section changes.
		/// </summary>
		/// <param name="name">The file path or remote section name that changed.</param>
		public delegate void ReloadDelegate(string name);
		
		private static readonly LogWrapper log = new LogWrapper();

		private static Timer FileReloadTimer;
		private static Timer SectionReloadTimer;

		private static readonly Dictionary<string, FileSystemWatcher> WatchedFolders = new Dictionary<string, FileSystemWatcher>(StringComparer.InvariantCultureIgnoreCase);
		private static readonly Dictionary<string, ReloadDelegate> WatchedFiles = new Dictionary<string, ReloadDelegate>(StringComparer.InvariantCultureIgnoreCase);
		private static readonly Dictionary<string, ReloadDelegate> WatchedRemoteSections = new Dictionary<string, ReloadDelegate>(StringComparer.InvariantCultureIgnoreCase);
		
		private static readonly HashSet<string> PendingFileChanges = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
		private static readonly HashSet<string> PendingSectionChanges = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

		/// <summary>
		/// Watches the section <paramref name="sectionName"/> from the Configuration System for changes. <paramref name="reloadDelegate"/> will be called when the section changes, after any other changes that happen within a second of the first change.
		/// </summary>
		public static void WatchRemoteSection(string sectionName, ReloadDelegate reloadDelegate)
		{
			if (!WatchedRemoteSections.ContainsKey(sectionName))
			{
				ConfigurationClient.RegisterReloadNotification(sectionName, ConfigFromServerChanged);
				WatchedRemoteSections.Add(sectionName, reloadDelegate);
			}
		}

		/// <summary>
		/// Watches the file <paramref name="path"/> for changes. <paramref name="reloadDelegate"/> will be called when the file changes, after any other changes that happen within a second of the first change.
		/// </summary>
		public static void WatchFile(string path, ReloadDelegate reloadDelegate)
		{
			string folder = Path.GetDirectoryName(path);
			lock (WatchedFolders)
			{
				if (!string.IsNullOrEmpty(folder) && !WatchedFolders.ContainsKey(folder))
				{
					AddWatchedFolder(folder);
				}

				if (!WatchedFiles.ContainsKey(path))
				{
					WatchedFiles.Add(path, reloadDelegate);
				}
			}
		}

		private static void AddWatchedFolder(string folder)
		{
			FileSystemWatcher newWatcher = new FileSystemWatcher(folder, "*.config");

			newWatcher.Changed += ConfigDirChanged;
			newWatcher.Created += ConfigDirChanged;

			newWatcher.EnableRaisingEvents = true;

			WatchedFolders.Add(folder, newWatcher);
		}

		private static void ProcessConfigReload(string name, HashSet<string> pendingChanges, Dictionary<string, ReloadDelegate> delegates, ref Timer timer)
		{
			log.Debug("Processing Config Reload");
			lock (pendingChanges)
			{
				if (pendingChanges.Count == 0)
				{
					log.Warn("Process config reload called with no pending reloads.");
					return;
				}

				pendingChanges.Clear();

				ReloadDelegate reload;
				if (delegates.TryGetValue(name, out reload) && reload != null)
					reload(name);

				if (timer != null)
				{
					timer.Dispose();
					timer = null;
				}

			}
		}

		private static void ProcessConfigFileReload(object state)
		{
			string filename = state as string;

			if (string.IsNullOrEmpty(filename))
			{
				log.Warn("Config reload called with empty file name.");
			}
			else
			{
				ProcessConfigReload(filename, PendingFileChanges, WatchedFiles, ref FileReloadTimer);
			}
		}

		private static void ProcessConfigSectionReload(object state)
		{
			string sectionName = state as string;

			if (String.IsNullOrEmpty(sectionName))
			{
				log.Warn("Config reload called with empty section name.");
			}
			else
			{
				ProcessConfigReload(sectionName, PendingSectionChanges, WatchedRemoteSections, ref SectionReloadTimer);
			}
		}

		private static void QueueConfigReload(string name, HashSet<string> pendingChanges, Dictionary<string, ReloadDelegate> watchedConfigs, TimerCallback reloadCallback, ref Timer timer)
		{
			lock (pendingChanges)
			{
				if (!watchedConfigs.ContainsKey(name))
				{
					log.DebugFormat("Unwatched config {0} changed.", name);
					return;
				}

				if (pendingChanges.Contains(name))
				{
					log.DebugFormat("Config {0} changed, delaying additional second", name);
					//just keep delaying 1 second until the changes stop
					timer.Change(1000, Timeout.Infinite);
					return;
				}

				if (pendingChanges.Count == 0)
				{
					if (log.IsInfoEnabled)
						log.InfoFormat("Config {0} changed. Processing in one second.", name);

					pendingChanges.Add(name);

					timer = new Timer(reloadCallback, name, 1000, Timeout.Infinite);
				}
				else
				{
					timer.Change(1000, Timeout.Infinite);

					if (log.IsInfoEnabled)
						log.InfoFormat(
							"Config {0} changed. Processing with current unprocessed changes and delaying reload for one second.",
							name);
					pendingChanges.Add(name);
				}
			}
		}

		private static void ConfigFromServerChanged(object sender, ConfigurationChangedEventArgs args)
		{
			QueueConfigReload(args.SectionName, PendingSectionChanges, WatchedRemoteSections, ProcessConfigSectionReload, ref SectionReloadTimer);
		}

		private static void ConfigDirChanged(object sender, FileSystemEventArgs e)
		{
			QueueConfigReload(e.FullPath, PendingFileChanges, WatchedFiles, ProcessConfigFileReload, ref FileReloadTimer);
		}

        /// <summary>
        /// Watches a perticular section within config.  If sectionName points to a remote config server, it will watch the remote server.
        /// </summary>
        /// <param name="sectionName">section to watch</param>
        /// <param name="reloadConfig">delegate to execute when reload happens</param>
        public static void WatchConfig(string sectionName, ReloadDelegate reloadConfig)
        {
            string configSource = ConfigurationLoader.GetConfigSource(sectionName);

            string configPath = String.Empty;
            if (!String.IsNullOrEmpty((configSource)))
            {
                configPath = Path.Combine(ConfigurationLoader.BaseFolder, configSource);
            }

            WatchFile(ConfigurationLoader.MainConfigPath, reloadConfig);//always watch for any changes within app/web.config.

            if (!string.IsNullOrEmpty(configPath))
            {
                WatchFile(configPath, reloadConfig);
            }
            else
            {
                string remoteSection = ConfigurationLoader.GetSectionName(sectionName);
                WatchRemoteSection(remoteSection, reloadConfig);
            }
        }
	}
}
