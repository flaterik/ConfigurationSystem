using System;
using System.ServiceProcess;
using MySpace.Logging;

namespace MySpace.ConfigurationSystem
{
    static class Program
    {
    	private static readonly LogWrapper log = new LogWrapper();

		/// <summary>
        /// The main entry point for the application.
        /// </summary>
		static void Main()
		{
			try
			{
				ServiceBase[] ServicesToRun;
				ServicesToRun = new ServiceBase[]
				                	{
				                		new ConfigurationService()
				                	};
				ServiceBase.Run(ServicesToRun);
			}
			catch (Exception e)
			{
				log.Error(e);
				throw;
			}

		}
    }
}
