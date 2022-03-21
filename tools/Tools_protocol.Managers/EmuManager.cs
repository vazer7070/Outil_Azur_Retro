using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Linq;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;

namespace Tools_protocol.Managers
{
    public static class EmuManager
	{
		public static string EMUSELECTED;

		 public static bool NOEMU = false;

		private static string[] Emu = new string[] { "Kryone", "Codebreak" };


        public static void InitEmu(string emu)
		{
			if (!Emu.Contains(emu))
			{
				NOEMU = true;
			}
		}

		public static string ReturnAccountsInfo(string emu, string name, int sw)
		{
			string str;
			int points;
			if (emu.Equals("Kryone"))
			{
				switch (sw)
				{
					case 1:
					{
						str = AccountList.Informations(name).Guid.ToString();
						break;
					}
					case 2:
					{
						str = AccountList.Informations(name).Account;
						break;
					}
					case 3:
					{
						str = AccountList.Informations(name).Pseudo;
						break;
					}
					case 4:
					{
						str = AccountList.Informations(name).Pass;
						break;
					}
					case 5:
					{
						str = AccountList.Informations(name).Banned.ToString();
						break;
					}
					case 6:
					{
						str = AccountList.Informations(name).Question;
						break;
					}
					case 7:
					{
						str = AccountList.Informations(name).Reponse;
						break;
					}
					case 8:
					{
						points = AccountList.Informations(name).Points;
						str = points.ToString();
						break;
					}
					case 9:
					{
						str = AccountList.Informations(name).lastIp;
						break;
					}
					case 10:
					{
						points = AccountList.Informations(name).Vip;
						str = points.ToString();
						break;
					}
					default:
					{
						str = "";
						return str;
					}
				}
			}
			else
			{
				str = "";
				return str;
			}
			return str;
		}
		public static string RecupPanoRow(string panocol, string panoname)
        {
			string row = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
				MySqlDataReader R = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { panocol }, JsonManager.SearchAuth("panoplies"), "name", panoname));
                    while (R.Read())
                    {
						row = R.GetString(panocol);
                    }
					R.Close();
					R.Dispose();
					return row;
            }
			return row;
        }
		public static string ReturnInfoCol(string table)
        {
			string colSelected = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
                    switch (table)
                    {
						case "pano":
							colSelected = "name";
							break;
                    }
					break;
            }
			return colSelected;
        }
		public static string UpdateRowPano(string panorow, string IDtemplate)
        {
			List<string> iteminpano = new List<string>();
			string newRow = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
                    if (!string.IsNullOrEmpty(panorow))
                    {
                        if (panorow.Contains(","))
                        {
							foreach (string h in panorow.Split(','))
							{
								iteminpano.Add(h);
								
							}
							iteminpano.Add(IDtemplate);
							newRow = string.Join(",", iteminpano);
						}
                        else
                        {
							iteminpano.Add(panorow);
							iteminpano.Add(IDtemplate);
							newRow = string.Join(",", iteminpano);
						}
                    }
                    else
                    {
						newRow = IDtemplate;
                    }
					return newRow;
            }
			return newRow;
        }
		public static string ReturnPanoCol()
        {
			string C = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
					C = "items";
					return C;
            }
			return C;
        }
		public static void ExecuteQueryByEmu(string key, string query)
        {
            switch (EMUSELECTED)
            {
				case "Kryone":
                    switch (key)
                    {
						case "pano":
							DatabaseManager.UpdateQuery(query);
							break;
						case "craft":
							DatabaseManager.UpdateQuery(query);
							break;
						case "template":
							DatabaseManager.UpdateQuery(query);
							break;
						case "item":
							DatabaseManager2.UpdateQuery(query);
							break;
                    }
					break;
            }
        }
		public static string ReturnTable(string table, string emu)
		{
            switch (emu)
            {
				case "Kryone":
					switch (table)
					{
						case "comptes":
							return JsonManager.SearchAuth(table);
						case "perso":
							return JsonManager.SearchAuth(table);
						case "Template":
							return JsonManager.SearchAuth(table);
						case "crafts":
							return JsonManager.SearchAuth(table);
						case "panoplies":
							return JsonManager.SearchAuth(table);
						case "cellule":
							return JsonManager.SearchAuth(table);
						case "items":
							return JsonManager.SearchWorld(table);
						case "endfight":
							return JsonManager.SearchAuth(table);
						case "groupe_monstre":
							return JsonManager.SearchAuth(table);
					}
					break;
				case "Codebreak":
                    switch (table)
                    {
						case "comptes":
							return JsonManager.SearchAuth(table);
						case "perso":
							return JsonManager.SearchWorld(table);
					}
					break;
            }
			return "";
		}
	}
}