using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Tools_protocol.Codebreak.Database
{
	public class CharactersListi
	{
		public static Dictionary<long, CharactersListi> CharacterList;

		public static List<string> PersoName;

		public long AccountId
		{
			get;
			set;
		}

		public int Agility
		{
			get;
			set;
		}

		public int Ap
		{
			get;
			set;
		}

		public short Breed
		{
			get;
			set;
		}

		public int CaracPoint
		{
			get;
			set;
		}

		public int CellId
		{
			get;
			set;
		}

		public int Chance
		{
			get;
			set;
		}

		public int Color1
		{
			get;
			set;
		}

		public int Color2
		{
			get;
			set;
		}

		public int Color3
		{
			get;
			set;
		}

		public short Dead
		{
			get;
			set;
		}

		public int DeathCount
		{
			get;
			set;
		}

		public int DeathType
		{
			get;
			set;
		}

		public int EmoteCapacity
		{
			get;
			set;
		}

		public int Energy
		{
			get;
			set;
		}

		public int EquippedMount
		{
			get;
			set;
		}

		public long Experience
		{
			get;
			set;
		}

		public long Id
		{
			get;
			set;
		}

		public int Intelligence
		{
			get;
			set;
		}

		public long Kamas
		{
			get;
			set;
		}

		public int Level
		{
			get;
			set;
		}

		public int Life
		{
			get;
			set;
		}

		public int MapId
		{
			get;
			set;
		}

		public int MaxLevel
		{
			get;
			set;
		}

		public byte Merchant
		{
			get;
			set;
		}

		public int Mp
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public int Restriction
		{
			get;
			set;
		}

		public int SavedCellId
		{
			get;
			set;
		}

		public int SavedMapId
		{
			get;
			set;
		}

		public short Sex
		{
			get;
			set;
		}

		public int Skin
		{
			get;
			set;
		}

		public int SkinSize
		{
			get;
			set;
		}

		public int SpellPoint
		{
			get;
			set;
		}

		public int Strength
		{
			get;
			set;
		}

		public static string TableCharacters
		{
			get
			{
				return EmuManager.ReturnTable("perso", EmuManager.EMUSELECTED);
			}
		}

		public int TitleId
		{
			get;
			set;
		}

		public string TitleParams
		{
			get;
			set;
		}

		public int Vitality
		{
			get;
			set;
		}

		public int Wisdom
		{
			get;
			set;
		}

		public CharactersListi()
		{
			CharactersListi.CharacterList = new Dictionary<long, CharactersListi>();
			CharactersListi.PersoName = new List<string>();
		}

		public CharactersListi(IDataReader reader)
		{
			this.Id = (long)reader["Id"];
			this.Name = (string)reader["Name"];
			this.Breed = (short)reader["Breed"];
			this.Color1 = (int)reader["Color1"];
			this.Color2 = (int)reader["Color2"];
			this.Color3 = (int)reader["Color3"];
			this.Skin = (int)reader["Skin"];
			this.SkinSize = (int)reader["SkinSize"];
			this.Vitality = (int)reader["Vitality"];
			this.Wisdom = (int)reader["Wisdom"];
			this.Strength = (int)reader["Strength"];
			this.Intelligence = (int)reader["Intelligence"];
			this.Agility = (int)reader["Agility"];
			this.Chance = (int)reader["Chance"];
			this.Ap = (int)reader["Ap"];
			this.Mp = (int)reader["Mp"];
			this.Life = (int)reader["Life"];
			this.Energy = (int)reader["Energy"];
			this.SpellPoint = (int)reader["SpellPoint"];
			this.CaracPoint = (int)reader["CaracPoint"];
			this.MapId = (int)reader["MapId"];
			this.CellId = (int)reader["CellId"];
			this.Restriction = (int)reader["Restriction"];
			this.Experience = (long)reader["Experience"];
			this.AccountId = (long)reader["AccountId"];
			this.Dead = (short)reader["Dead"];
			this.MaxLevel = (int)reader["MaxLevel"];
			this.DeathCount = (int)reader["DeathCount"];
			this.Level = (int)reader["Level"];
			this.Sex = (short)reader["Sex"];
			this.Kamas = (long)reader["Kamas"];
			this.SavedMapId = (int)reader["SavedMapId"];
			this.SavedCellId = (int)reader["SavedCellId"];
			this.Merchant = (byte)reader["Merchant"];
			this.TitleId = (int)reader["TitleId"];
			this.TitleParams = (string)reader["TitleParams"];
			this.EmoteCapacity = (int)reader["EmoteCapacity"];
			this.DeathType = (int)reader["DeathType"];
			this.EquippedMount = (int)reader["EquippedMount"];
		}

		public static CharactersListi PersoById(long id)
		{
			KeyValuePair<long, CharactersListi> keyValuePair = CharactersListi.CharacterList.FirstOrDefault<KeyValuePair<long, CharactersListi>>((KeyValuePair<long, CharactersListi> x) => x.Key == id);
			return keyValuePair.Value;
		}

		public static void PersoLoad()
		{
			CharactersListi CL = null;
			MySqlDataReader R = DatabaseManager2.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, CharactersListi.TableCharacters, "", ""));
			while (R.Read())
			{
				CL = new CharactersListi(R);
				CharactersListi.CharacterList.Add(CL.Id, CL);
				CharactersListi.PersoName.Add(CL.Name);
			}
			R.Close();
			R.Dispose();
		}
	}
}