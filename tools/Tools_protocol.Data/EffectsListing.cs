using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Tools_protocol.Data
{
	public class EffectsListing
	{
		public static Dictionary<string, string> EffectList;

		public static Dictionary<string, string> ItemEffectList;

		public static Dictionary<string, string> SpellsEffectList;

		public static int Effects_count;

		public static int ItemEffects_count;

		public static int SpellsEffects_count;

		static EffectsListing()
		{
			EffectsListing.EffectList = new Dictionary<string, string>();
			EffectsListing.ItemEffectList = new Dictionary<string, string>();
			EffectsListing.SpellsEffectList = new Dictionary<string, string>();
		}

		public EffectsListing()
		{
		}

		public static void Load_effects(string path_effects)
		{
			if (File.Exists(path_effects))
			{
				string[] strArrays = File.ReadAllLines(path_effects);
				for (int i = 0; i < (int)strArrays.Length; i++)
				{
					string line = strArrays[i];
					if (!EffectsListing.EffectList.ContainsKey(line.Split(new char[] { '=' })[1]))
					{
						EffectsListing.EffectList.Add(line.Split(new char[] { '=' })[1], line.Split(new char[] { '=' })[0]);
					}
				}
				EffectsListing.Effects_count = EffectsListing.EffectList.Count<KeyValuePair<string, string>>();
			}
		}

		public static void Load_ItemEffects(string path_file)
		{
			if (File.Exists(path_file))
			{
				string[] strArrays = File.ReadAllLines(path_file);
				for (int i = 0; i < (int)strArrays.Length; i++)
				{
					string effet = strArrays[i];
					EffectsListing.ItemEffectList.Add(effet.Split(new char[] { '=' })[1], effet.Split(new char[] { '=' })[0]);
				}
				EffectsListing.ItemEffects_count = EffectsListing.ItemEffectList.Count<KeyValuePair<string, string>>();
			}
		}

		public static void Load_SpellsEffects(string path)
		{
			if (File.Exists(path))
			{
				string[] strArrays = File.ReadAllLines(path);
				for (int i = 0; i < (int)strArrays.Length; i++)
				{
					string effet = strArrays[i];
					EffectsListing.SpellsEffectList.Add(effet.Split(new char[] { '=' })[0], effet.Split(new char[] { '=' })[1]);
				}
				EffectsListing.SpellsEffects_count = EffectsListing.SpellsEffectList.Count<KeyValuePair<string, string>>();
			}
		}

		public static string Return_SpellsEffects(string id)
		{
			string s;
			string str;
			str = (!EffectsListing.SpellsEffectList.TryGetValue(id, out s) ? string.Concat("Effet inconnu (IDSpellEffect: ", id, ")") : s);
			return str;
		}

		public static string ReturnDef(string id)
		{
			string G;
			string str;
			str = (!EffectsListing.EffectList.TryGetValue(id, out G) ? string.Concat("Effet inconnu (IDdef: ", id, ")") : G);
			return str;
		}

		public static string ReturnIdItemEffect(string stat)
		{
			KeyValuePair<string, string> keyValuePair = EffectsListing.ItemEffectList.FirstOrDefault<KeyValuePair<string, string>>((KeyValuePair<string, string> x) => x.Value == stat);
			return keyValuePair.Key;
		}

		public static string ReturnStatItem(string id)
		{
			string definition;
			string str;
			str = (!EffectsListing.ItemEffectList.TryGetValue(id, out definition) ? string.Concat("Effet inconnu (IDStatItem: ", id, ")") : definition);
			return str;
		}
	}
}