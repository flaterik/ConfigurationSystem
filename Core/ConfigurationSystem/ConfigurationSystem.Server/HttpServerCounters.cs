using System;
using System.Diagnostics;
using System.Threading;

namespace MySpace.ConfigurationSystem
{
	class HttpServerCounters
	{
		internal static readonly string PerformanceCategoryName = "MySpace Http Configuration Server";
		PerformanceCounter[] PerformanceCounters;

		private bool countersInitialized;
		private System.Timers.Timer timer;
		private static readonly Logging.LogWrapper log = new Logging.LogWrapper();
		private int requestsQueued;

		#region counter definitions
		internal enum PerformanceCounterIndexes
		{
			TotalRequests,
			RequestsQueued,
			RequestsPerSecond,
			OKPerSecond,
			NotModifiedPerSecond,
			NotFoundPerSecond,
			ErrorPerSecond,                        
			TotalErrors
		}

		internal static readonly string[] PerformanceCounterNames = 
		{
		  "Total Requests",
		  "Requests Queued",
		  "Requests per Second",
		  "OK per Second",
		  "Not Modified per Second",
		  "Not Found per Second",          
		  "Errors per Second",
		  "Total Errors"
		};

		internal static readonly string[] PerformanceCounterHelp =
		{
			"The total number of requests that the server has processed",
			"The number of queued requests",
			"The number of requests per second",
			"The number of requests per second returning OK (200)",
			"The number of requests per second returning Not Modified (304)",
			"The number of requests per second returning Not Found (404)",
			"The number of requests per second returning InternalSereverError (500)",
			"The total number of errors that have occurred"
		};

		internal static readonly PerformanceCounterType[] PerformanceCounterTypes = 
		{
			PerformanceCounterType.NumberOfItems64,
			PerformanceCounterType.NumberOfItems32,
			PerformanceCounterType.RateOfCountsPerSecond32,
			PerformanceCounterType.RateOfCountsPerSecond32,
			PerformanceCounterType.RateOfCountsPerSecond32,
			PerformanceCounterType.RateOfCountsPerSecond32,
			PerformanceCounterType.RateOfCountsPerSecond32,
			PerformanceCounterType.NumberOfItems32
		};
		#endregion 

		internal void Initialize(string instanceName)
		{

			try
			{
				if (!PerformanceCounterCategory.Exists(PerformanceCategoryName))
				{
					InstallCounters();
				}
				else
				{
					countersInitialized = true;
				}
				
				if (countersInitialized)
				{
					int numCounters = PerformanceCounterNames.Length;
					PerformanceCounters = new PerformanceCounter[numCounters];

					for (int i = 0; i < numCounters; i++)
					{
						PerformanceCounters[i] = new PerformanceCounter(
							PerformanceCategoryName,
							PerformanceCounterNames[i],
							instanceName,
							false
							);
					}
					ResetCounters();
					StartTimer();
				}
			}
			catch (System.Security.SecurityException)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("Could not automatically install {0} counters. Please run InstallUtil against MySpace.ConfigurationSystem.Server.dll to install counters manually.", PerformanceCategoryName);
				countersInitialized = false;
			}
			catch (UnauthorizedAccessException)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("Could not automatically install {0} counters. Please run InstallUtil against MySpace.ConfigurationSystem.Server.dll to install counters manually.", PerformanceCategoryName);
				countersInitialized = false;
			}
			catch (Exception ex)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("Error initializing {0} counters: {1}", PerformanceCategoryName, ex);
				countersInitialized = false;
			}
		}

		internal void ResetCounters()
		{
			if (countersInitialized)
			{
				PerformanceCounters[(int)PerformanceCounterIndexes.RequestsQueued].RawValue = 0;
				PerformanceCounters[(int)PerformanceCounterIndexes.TotalErrors].RawValue = 0;
				PerformanceCounters[(int)PerformanceCounterIndexes.TotalRequests].RawValue = 0;                
			}
		}

		private void StartTimer()
		{
			timer = new System.Timers.Timer {Interval = 1000, AutoReset = true};
			timer.Elapsed += timer_Elapsed;
			timer.Start();
		}

		void timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			if (countersInitialized)
			{
				PerformanceCounters[(int)PerformanceCounterIndexes.RequestsQueued].RawValue = requestsQueued;
			}
		}

		private void RemoveCounters()
		{
			CounterInstaller.RemoveCounters();
			countersInitialized = false;
		}

		private void InstallCounters()
		{
			countersInitialized = CounterInstaller.InstallCounters();
		}

		internal void QueueRequest()
		{
			Interlocked.Increment(ref requestsQueued);
		}

		internal void DequeueRequest(int status)
		{
			Interlocked.Decrement(ref requestsQueued);
			if (countersInitialized)
			{
				PerformanceCounters[(int)PerformanceCounterIndexes.TotalRequests].Increment();
				PerformanceCounters[(int)PerformanceCounterIndexes.RequestsPerSecond].Increment();
				switch (status)
				{
					case 200:
						PerformanceCounters[(int)PerformanceCounterIndexes.OKPerSecond].Increment();
						break;
					case 304:
						PerformanceCounters[(int)PerformanceCounterIndexes.NotModifiedPerSecond].Increment();
						break;
					case 404:
						PerformanceCounters[(int)PerformanceCounterIndexes.NotFoundPerSecond].Increment();
						break;
					case 500:
						PerformanceCounters[(int)PerformanceCounterIndexes.TotalErrors].Increment();
						PerformanceCounters[(int)PerformanceCounterIndexes.ErrorPerSecond].Increment();
						break;
				}
			}
		}
	}
}
