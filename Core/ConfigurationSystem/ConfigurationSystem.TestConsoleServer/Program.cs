using System;

namespace MySpace.ConfigurationSystem
{
    class Program
    {
        static void Main(string[] args)
        {
			HttpServer.Start();
            Console.ReadLine();
            HttpServer.Stop();
        }
    }
}
