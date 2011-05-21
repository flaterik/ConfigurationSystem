using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Xml.Serialization;
using MySpace.XmlSerializerExample;

namespace MySpace.ConfigurationSystem.TestConsole
{
	class Program
	{

		static void Main(string[] args)
		{

			object configObject = null;
			ConsoleKeyInfo cki;
			XmlSerializerReplacementHandler.RegisterReloadNotification("MySection", MyReload);
			do
			{
				try
				{					
					configObject = ConfigurationManager.GetSection("MySection");					
				}
				catch (Exception e)
				{
					Console.WriteLine("Error getting config: " + e);
				}
				if (configObject != null)
				{
					MySection section = configObject as MySection;
					DisplaySection(section);

				}
				cki = Console.ReadKey(true);
			}
			while (cki.KeyChar == 'g');

		}

		private static void DisplaySection(MySection section)
		{
			if (section == null)
			{
				Console.WriteLine("Null Section");
			}
			else
			{
				Console.WriteLine("MyString: {0}", section.MyString);
				Console.WriteLine("MyInt: {0}", section.MyInt);
				for (int i = 0; i < section.MyListOfStuffs.Length; i++)
				{
					Console.WriteLine("MyStuff {0} : {1}", i, section.MyListOfStuffs[i]);
				}
			}
		}
		
		public static void MyReload(object sender, EventArgs args)
		{
			Console.WriteLine("Got an updated section!");
			MySection section = sender as MySection;
			DisplaySection(section);
			
		}
	}
		
}
