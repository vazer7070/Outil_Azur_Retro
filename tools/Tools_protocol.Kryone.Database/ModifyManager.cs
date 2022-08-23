using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class ModifyManager
	{
		public static string TablePerso => JsonManager.SearchAuth("perso");
		public static Dictionary<string, string> Query = new Dictionary<string, string>(); // id|[type modif], query modif
		public static int QueryCount = 0;

		public static bool ModifyAccepted(int sw, string id, string firstvalue, string newvalue)
		{
			bool VALUE = false;
            try
            {
				switch (sw)
				{
					case 1: //nom
						if (!string.IsNullOrEmpty(newvalue) && firstvalue != newvalue)
						{
							CharacterList CL;
							CL = CharacterList.PersoAll.FirstOrDefault(x => x.Key == firstvalue).Value;
							CharacterList.PersoAll.Remove(firstvalue);
							CharacterList.PersoAll.Add(newvalue, CL);
							if(!Query.ContainsKey($"{id}|name"))
								Query.Add($"{id}|name", QueryBuilder.UpdateFromQuery(TablePerso, "name", 1, newvalue, "name", firstvalue));
                            else
                            {
								Query.Remove($"{id}|name");
								Query.Add($"{id}|name", QueryBuilder.UpdateFromQuery(TablePerso, "name", 1, newvalue, "name", firstvalue));
							}
							QueryCount = Query.Count;
							return true;
						}
						break;
				}
            }
            catch
            {
				VALUE = false;
            }
			return VALUE;
		}
	}
}