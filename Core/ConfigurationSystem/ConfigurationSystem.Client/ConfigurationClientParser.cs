using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using MySpace.Logging;

namespace MySpace.ConfigurationSystem
{
	internal class  ConfigurationClientParser
	{
		static readonly Logging.LogWrapper log = new Logging.LogWrapper();
		internal static void PopulateGenericSettingCollection(ref ConfigurationItem item)
		{
			if (item.SectionString != null )
			{
				try
				{
					XmlDocument doc = new XmlDocument();
					doc.LoadXml(item.SectionString);
					XmlNode sectionXmlNode = doc.DocumentElement;
					//clear off the dictionary before populating it
					item.GenericSettingCollection.Clear();
					XmlNode xmlnode = sectionXmlNode.SelectSingleNode("//appSettings");
					if (xmlnode != null)
					{

						foreach (XmlNode appsettingnode in xmlnode.ChildNodes)
						{
							if (appsettingnode.Name.Equals("add"))
							{
								item.GenericSettingCollection[appsettingnode.Attributes[0].Value] = appsettingnode.Attributes[1].Value;
							}
						}
					}
					
				}
				catch (Exception e)
				{
					log.ErrorFormat("ConfigurationClient : ConfigurationClientParser Exception  :{0}", e.InnerException);
				}
			}
			else
			{
				//log that the section is empty
				log.WarnFormat("ConfigurationClient : ConfigurationClientParser SectionString is empty, SectionName :{0}", item.SectionName);
			}
			
		}	

	}
}
