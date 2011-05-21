using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.IO;
using System.Reflection;

namespace MySpace.ConfigurationSystem
{
    public partial class ConfigurationService : ServiceBase
    {
        private static readonly MySpace.Logging.LogWrapper log = new MySpace.Logging.LogWrapper();        

        public ConfigurationService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                Directory.SetCurrentDirectory(baseDir);
                HttpServer.Start();
            }
            catch(Exception e)
            {
                log.ErrorFormat("Exception starting configuration server: {0}", e);
            }
        }

        protected override void OnStop()
        {
            try
            {
                HttpServer.Stop();
            }
            catch (Exception e)
            {
                log.ErrorFormat("Exception stopping configuration server: {0}", e);
            }
        }
    }
}
