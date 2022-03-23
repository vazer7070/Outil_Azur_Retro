using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Tools_protocol.Json
{
	public static class JsonManager
	{
		static JObject defaut_auth = new JObject(new JProperty("Tables_auth", new JObject(new object[] { new JProperty("comptes", "accounts"), new JProperty("animations", "animations"), new JProperty("areadata", "area_data"), new JProperty("bandits", "bandits"), new JProperty("bannis(ip)", "banip"), new JProperty("banque", "banks"), new JProperty("challenge", "challenge"), new JProperty("coffre", "coffre"), new JProperty("commandes", "commandes"), new JProperty("crafts", "crafts"), new JProperty("donjons", "donjons"), new JProperty("drops", "drops"), new JProperty("endfight", "endfight_action"), new JProperty("extra_monstres", "extra_monsters"), new JProperty("morphs", "full_morphs"), new JProperty("groupes", "groupes"), new JProperty("hdvs", "hdvs"), new JProperty("maisons", "houses"), new JProperty("interaction_portes", "interactive_doors"), new JProperty("interactions", "interactive_objects_data"), new JProperty("Template_objets", "item_template"), new JProperty("panoplies", "itemsets"), new JProperty("métiers", "jobs_data"), new JProperty("cartes", "maps"), new JProperty("groupes_monstres", "mobgroups_fix"), new JProperty("enclos", "mountpark_data"), new JProperty("monstres", "monsters"), new JProperty("npc_questions", "npc_questions"), new JProperty("npc_reponse", "npc_reponses_actions"), new JProperty("template_npc", "npc_template"), new JProperty("npcs", "npcs"), new JProperty("object_action", "objectactions"), new JProperty("paroli", "paroli"), new JProperty("familiers", "pets"), new JProperty("personnages", "players"), new JProperty("prismes", "prismes"), new JProperty("quêtes", "quest_data"), new JProperty("quêtes_étapes", "quest_etapes"), new JProperty("quêtes_objectif", "quest_objectifs"), new JProperty("rss", "rss"), new JProperty("runes", "runes"), new JProperty("schema_fight", "schemafight"), new JProperty("cellules_script", "scripted_cells"), new JProperty("serveurs", "servers"), new JProperty("sorts", "sorts"), new JProperty("subarea_data", "subarea_data"), new JProperty("titres", "titres"), new JProperty("tuto", "tutoriel"), new JProperty("zaapi", "zaapi"), new JProperty("zaaps", "zaaps") })));

	    static JObject defaut_config = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("hote", "localhost"), new JProperty("user", "root"), new JProperty("mdp", ""), new JProperty("Aname", "kauth"), new JProperty("Wname", "kworld"), new JProperty("Emu", "Kryone") })));

		static JObject defaut_word = new JObject(new JProperty("Tables_world", new JObject(new object[] { new JProperty("items", "items"), new JProperty("drops", "drops"), new JProperty("monstres", "monsters"), new JProperty("personnages", "characterinstance"), new JProperty("gifts", "gifts") })));

		static Dictionary<string, string> Configuration = new Dictionary<string, string>();

		public static Dictionary<string, string> Auth_dico = new Dictionary<string, string>();

		public static Dictionary<string, string> World_dico = new Dictionary<string, string>();

		public static string Aauth => SearchConfig("auth");

		public static string Aworld => SearchConfig("world");

		public static string Hôte => SearchConfig("hote");

		public static string MDP => SearchConfig("mdp");

		public static string User => SearchConfig("user");

        public static bool Initialize(string authpath, string worldpath, string configpath)
		{
			bool caninit = false;
			try
			{
				if(LectureConfig(configpath) && LectureAuth(authpath) && LectureWorld(worldpath))
					caninit = true;
				else
					caninit = false;
			}
            catch (Exception exception)
			{
				caninit = false;
				Exception ee = exception;
				MessageBox.Show(string.Concat(ee.Message, "\n", ee.StackTrace));
			}
			return caninit;
		}

		public static bool LectureAuth(string path)
		{
			Auth_dico.Clear();
			bool confok = false;
			if (!File.Exists(path))
			{
				WriteConfig("auth", path);
			}
			try
			{
				string lectu = File.ReadAllText(path);
				if (!VerifJson(lectu))
				{
					WriteConfig("auth", path);
					LectureAuth(path);
				}
				else
				{
					AuthEnums.AuthEntries auth = JsonConvert.DeserializeObject<AuthEnums.Data>(lectu).AuthEntries;
					Auth_dico.Add("comptes", auth.comptes);
					Auth_dico.Add("animations", auth.animations);
					Auth_dico.Add("areadata", auth.areadata);
					Auth_dico.Add("bandits", auth.bandits);
					Auth_dico.Add("banip", auth.banip);
					Auth_dico.Add("banque", auth.banque);
					Auth_dico.Add("challenge", auth.challenge);
					Auth_dico.Add("coffre", auth.coffre);
					Auth_dico.Add("commandes", auth.commandes);
					Auth_dico.Add("crafts", auth.crafts);
					Auth_dico.Add("donjons", auth.donjons);
					Auth_dico.Add("drops", auth.drops);
					Auth_dico.Add("endfight", auth.endfight);
					Auth_dico.Add("extra", auth.extra);
					Auth_dico.Add("morphs", auth.morphs);
					Auth_dico.Add("groupes", auth.groupes);
					Auth_dico.Add("hdvs", auth.hdvs);
					Auth_dico.Add("maisons", auth.maisons);
					Auth_dico.Add("Iporte", auth.Iportes);
					Auth_dico.Add("interactions", auth.interactions);
					Auth_dico.Add("Template", auth.Template_objets);
					Auth_dico.Add("panoplies", auth.panoplies);
					Auth_dico.Add("metiers", auth.job);
					Auth_dico.Add("cartes", auth.cartes);
					Auth_dico.Add("groupe_monstre", auth.groupes_monstres);
					Auth_dico.Add("enclos", auth.enclos);
					Auth_dico.Add("monstres", auth.monstres);
					Auth_dico.Add("npc_questions", auth.npc_questions);
					Auth_dico.Add("npc_reponse", auth.npc_reponse);
					Auth_dico.Add("npc_template", auth.template_npc);
					Auth_dico.Add("npcs", auth.npcs);
					Auth_dico.Add("objets_actions", auth.object_action);
					Auth_dico.Add("paroli", auth.paroli);
					Auth_dico.Add("familiers", auth.familiers);
					Auth_dico.Add("perso", auth.personnages);
					Auth_dico.Add("prismes", auth.prismes);
					Auth_dico.Add("quete", auth.quêtes);
					Auth_dico.Add("quete_etape", auth.quêtes_étapes);
					Auth_dico.Add("quete_objectif", auth.quêtes_objectif);
					Auth_dico.Add("rss", auth.rss);
					Auth_dico.Add("runes", auth.runes);
					Auth_dico.Add("schema_fight", auth.schema_fight);
					Auth_dico.Add("cellule", auth.cellules_script);
					Auth_dico.Add("serveurs", auth.serveurs);
					Auth_dico.Add("sort", auth.sorts);
					Auth_dico.Add("subarea_data", auth.subarea_data);
					Auth_dico.Add("titres", auth.titres);
					Auth_dico.Add("tuto", auth.tuto);
					Auth_dico.Add("zaapi", auth.zaapi);
					Auth_dico.Add("zaaps", auth.zaaps);
					confok = true;
				}
			}
			catch (Exception exception)
			{
				MessageBox.Show(exception.Message + "auth");
				confok=false;
			}
			return confok;
		}

		public static bool LectureConfig(string path)
		{
			
			bool confok = false;
			if (!File.Exists(path))
			{
				WriteConfig("config", path);
			}
			try
			{
				string lect = File.ReadAllText(path);
				if (!VerifJson(lect))
				{
					WriteConfig("config", path);
					LectureConfig(path);
				}
				else
				{
					ConfigEnums.ConfigEntries config = JsonConvert.DeserializeObject<ConfigEnums.Data>(lect).ConfigEntries;
					Configuration.Add("hote", config.hote);
					Configuration.Add("user", config.user);
					Configuration.Add("mdp", config.mdp);
					Configuration.Add("auth", config.Cauth);
					Configuration.Add("world", config.Cworld);
					Configuration.Add("emu", config.emu);
					confok = true;
				}
				
			}
            catch (Exception exception)
			{
				confok=false;
				MessageBox.Show(exception.Message + "config");
			}
			return confok;
		}

		public static bool LectureWorld(string path)
		{
			World_dico.Clear();
			bool confok = false;
			if (!File.Exists(path))
			{
				WriteConfig("world", path);
			}
			try
			{
				string lect = File.ReadAllText(path);
				if (!VerifJson(lect))
				{
					WriteConfig("world", path);
					LectureWorld(path);
				}
				else
				{
					WorldEnums.Worldentries world = JsonConvert.DeserializeObject<WorldEnums.Data>(lect).worldentries;
					World_dico.Add("items", world.Items);
					World_dico.Add("drops", world.Drops);
					World_dico.Add("monstres", world.Monstres);
					World_dico.Add("perso", world.personnages);
					World_dico.Add("gifts", world.Gifts);
					confok = true;
				}
			}
			catch (Exception exception)
			{
				MessageBox.Show(exception.Message + "world");
				confok=false;
			}
			return confok;
		}

		public static string SearchAuth(string key)
		{
			return Auth_dico.FirstOrDefault(x => x.Key == key).Value;
		}

		public static string SearchConfig(string key)
		{
			return Configuration.FirstOrDefault(x => x.Key == key).Value;
		}

		public static string SearchWorld(string key)
		{
			return World_dico.FirstOrDefault(x => x.Key == key).Value;
		}

		public static bool VerifJson(string json)
		{
			bool flag;
			try
			{
				JObject.Parse(json);
				flag = true;
			}
			catch
			{
				flag = false;
			}
			return flag;
		}
		public static void RewriteConfig(string h, string u, string p, string a, string w, string e = null)
        {
			Configuration.Clear();
			JObject newconfig;
			if (e == null)
				newconfig = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("hote", h), new JProperty("user", u), new JProperty("mdp", p), new JProperty("Aname", a), new JProperty("Wname", w), new JProperty("Emu", "Kryone") })));
			else
				newconfig = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("hote", h), new JProperty("user", u), new JProperty("mdp", p), new JProperty("Aname", a), new JProperty("Wname", w), new JProperty("Emu", e) })));

			JObject Parse = JObject.Parse(newconfig.ToString());
			File.WriteAllText(@".\config.json", Parse.ToString());

		}
		public static void RewriteTableJson(string json, bool isAuth)
        {
			JObject parsing;
			if (json != null)
            {
                if (isAuth)
                {
					parsing = JObject.Parse(json);
					File.WriteAllText(@".\auth\auth_tables.json", parsing.ToString());
				}
                else
                {
					parsing = JObject.Parse(json);
					File.WriteAllText(@".\world\world_tables.json", parsing.ToString());
				}
            }
        }
		public static void WriteConfig(string modele, string path)
		{
			JObject parsing;
			string str = modele;
			if (str != null)
			{
				if (str == "auth")
				{
					parsing = JObject.Parse(defaut_auth.ToString());
					File.WriteAllText(path, parsing.ToString());
				}
				else if (str == "world")
				{
					parsing = JObject.Parse(defaut_word.ToString());
					File.WriteAllText(path, parsing.ToString());
				}
				else if (str == "config")
				{
					parsing = JObject.Parse(defaut_config.ToString());
					File.WriteAllText(path, parsing.ToString());
				}
			}
		}
	}
}