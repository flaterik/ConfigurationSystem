using System.Xml.Serialization;

namespace MySpace.XmlSerializerExample
{
	[XmlRoot("MySection", Namespace = "http://myspace.com/MySection.xsd")]
	public class MySection
	{
		[XmlElement("MyString")]
		public string MyString;

		[XmlElement("MyInt")] public int MyInt;

		[XmlArray("MyListOfStuffs")]
		[XmlArrayItem("Stuff")]
		public string[] MyListOfStuffs { get; set; }
	}
}
