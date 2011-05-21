using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;



namespace MySpace.ConfigurationSystem
{
    [RunInstaller(true)]
    public partial class CounterInstaller : Installer
    {
        private static readonly MySpace.Logging.LogWrapper log = new MySpace.Logging.LogWrapper();
        
        public CounterInstaller()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Installs forwarding counters
        /// </summary>		
        public override void Install(System.Collections.IDictionary stateSaver)
        {
            InstallCounters();
            base.Install(stateSaver);
        }

        /// <summary>
        /// Uninstalls forwarding counters
        /// </summary>
        public override void Uninstall(System.Collections.IDictionary savedState)
        {
            RemoveCounters();
            base.Uninstall(savedState);
        }

        /// <summary>
        /// Installs forwarding counters
        /// </summary>		
        public static bool InstallCounters()
        {
            string message = String.Empty;
            try
            {
                if (log.IsInfoEnabled)
                    log.InfoFormat("Creating performance counter category {0}", HttpServerCounters.PerformanceCategoryName);
                Console.WriteLine("Creating performance counter category " + HttpServerCounters.PerformanceCategoryName);
                CounterCreationDataCollection counterDataCollection = new CounterCreationDataCollection();

                for (int i = 0; i < HttpServerCounters.PerformanceCounterNames.Length; i++)
                {
                    counterDataCollection.Add(new CounterCreationData(HttpServerCounters.PerformanceCounterNames[i], HttpServerCounters.PerformanceCounterHelp[i], HttpServerCounters.PerformanceCounterTypes[i]));
                    message = "Creating perfomance counter " + HttpServerCounters.PerformanceCounterNames[i];
                    Console.WriteLine(message);
                    if (log.IsInfoEnabled)
                        log.Info(message);
                }

                PerformanceCounterCategory.Create(HttpServerCounters.PerformanceCategoryName, "Counters for the MySpace Http Configuration Server", PerformanceCounterCategoryType.MultiInstance, counterDataCollection);
                return true;
            }
            catch (System.Security.SecurityException)
            {
                message = "Cannot create Performance Counters for " + HttpServerCounters.PerformanceCategoryName + ". Please run installutil against MySpace.ConfigurationSystem.Server with sufficient permissions.";
                Console.WriteLine(message);
                if (log.IsWarnEnabled)
                    log.Warn(message);
                return false;
            }
            catch (Exception ex)
            {
                message = "Error creating Perfomance Counter Category " + HttpServerCounters.PerformanceCategoryName + ": " + ex.ToString() + ". Counter category will not be used.";
                Console.WriteLine(message);
                if (log.IsErrorEnabled)
                    log.Error(message);
                return false;
            }




        }

        /// <summary>
        /// Removes forwarding counters
        /// </summary>		
        public static void RemoveCounters()
        {
            string message = string.Empty;
            try
            {
                message = "Removing performance counter category " + HttpServerCounters.PerformanceCategoryName;
                if (log.IsInfoEnabled)
                    log.Info(message);
                Console.WriteLine(message);
                PerformanceCounter.CloseSharedResources();
                PerformanceCounterCategory.Delete(HttpServerCounters.PerformanceCategoryName);
            }
            catch (Exception ex)
            {
                message = "Exception removing counters: " + ex.ToString();
                Console.WriteLine(message);
                if (log.IsInfoEnabled)
                    log.Info(message);
            }
        }
    }
}
