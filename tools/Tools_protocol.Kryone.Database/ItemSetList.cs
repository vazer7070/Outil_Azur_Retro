using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tools_protocol.Data;
using Tools_protocol.Emulators;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class ItemSetList
	{

		public static Dictionary<int, ItemSetList> AllItemsInSet = new Dictionary<int, ItemSetList>();

		public static List<string> SetName = new List<string>();

		public static List<string> Name_Effects = new List<string>();

		public static int Count_Pano;

		public static string E;

		public static string[] L;

		public string Bonus
		{
			get;
			set;
		}

		public int Id
		{
			get;
			set;
		}

		public string Items
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		/// <summary>Table des panoplies selon le profil d'émulateur courant.</summary>
		public static string TableSet
		{
			get
			{
				return EmulatorRegistry.Current.Table("panoplies");
			}
		}

		public ItemSetList(IDataReader reader)
		{
			Id = (int)reader["id"];
			Name = (string)reader["name"];
			Items = (string)reader["items"];
			Bonus = (string)reader["bonus"];
		}

        public static void LoadPano()
        {
            var loaded = new Dictionary<int, ItemSetList>();
            using (var connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("panoplies")))
            using (var command = new MySqlCommand(QueryBuilder.SelectFromQuery(new[] { "*" },TableSet,"",""),connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader()) while(reader.Read()) { var set = new ItemSetList(reader); loaded.Add(set.Id,set); }
            }
            AllItemsInSet = loaded; SetName = loaded.Values.Select(set=>set.Name).ToList(); Count_Pano = loaded.Count;
        }

		public static void ReturnEffect(int id, int surplus)
		{
			if (surplus <= 0)
			{
				E = ReturnItems(id, 2).Split(new char[] { ';' })[0];
			}
			else
			{
				E = ReturnItems(id, 2).Split(new char[] { ';' })[surplus];
			}
			string[] strArrays = E.Split(new char[] { ',' });
			for (int num = 0; num < (int)strArrays.Length; num++)
			{
				string l = strArrays[num];
				if (!l.Contains(";"))
				{
					string eff = l.Split(new char[] { ':' })[0];
					int V = Convert.ToInt32(l.Split(new char[] { ':' })[1]);
					string Def = EffectsListing.ReturnDef(eff);
					string S = Def.Replace("$", string.Format("{0}", V));
					Name_Effects.Add(S);
				}
				else
				{
					string[] strArrays1 = l.Split(new char[] { ';' });
					for (int j = 0; j < (int)strArrays1.Length; j++)
					{
						string i = strArrays1[j];
						string[] strArrays2 = i.Split(new char[] { ',' });
						for (int k = 0; k < (int)strArrays2.Length; k++)
						{
							string R = strArrays2[k];
							string eff = "";
							int V = 0;
							if (!string.IsNullOrEmpty(R.Split(new char[] { ':' })[0]))
							{
								if (!string.IsNullOrEmpty(R.Split(new char[] { ':' })[1]))
								{
									eff = l.Split(new char[] { ':' })[0];
									V = Convert.ToInt32(R.Split(new char[] { ':' })[1]);
									string Def = EffectsListing.ReturnDef(eff);
									string S = Def.Replace("$", V.ToString());
									Name_Effects.Add(S);
								}
							}
						}
					}
				}
			}
		}

		public static string ReturnItems(int id, int sw)
		{
			ItemSetList set;
			string items;
			if (!AllItemsInSet.TryGetValue(id, out set))
			{
				items = null;
			}
			else
			{
				int num = sw;
				if (num == 1)
				{
					items = set.Items;
				}
				else
				{
					items = (num == 2 ? set.Bonus : id.ToString());
				}
			}
			return items;
		}

		public static int ReturnPanoId(string name)
		{
			return AllItemsInSet.FirstOrDefault(x => x.Value.Name == name).Key;
		}
	}
}
