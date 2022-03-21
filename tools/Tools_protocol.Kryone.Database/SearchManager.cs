using System;
using System.IO;

namespace Tools_protocol.Kryone.Database
{
	public class SearchManager
	{
		public SearchManager()
		{
		}

		public static string Search_MonsterPicture(string id)
		{
			string path = string.Concat(".\\ressources\\monster\\", id, ".png");
			return (!File.Exists(path) ? "" : path);
		}

		public static string Search_pictureItem(int id, int type)
		{
			string path = string.Format(".\\ressources\\{0}\\{1}.png", type, id);
			return (!File.Exists(path) ? "" : path);
		}

		public static string Search_SpellsPicture(string id)
		{
			string path = string.Concat(".\\ressources\\sorts\\", id, ".png");
			return (!File.Exists(path) ? "" : path);
		}
	}
}