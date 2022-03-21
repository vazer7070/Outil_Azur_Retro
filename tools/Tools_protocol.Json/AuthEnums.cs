using Newtonsoft.Json;
using System;
using System.Runtime.CompilerServices;

namespace Tools_protocol.Json
{
	public class AuthEnums
	{
		
		public class AuthEntries
		{
			[JsonProperty("animations")]
			public string animations
			{
				get;
				set;
			}

			[JsonProperty("areadata")]
			public string areadata
			{
				get;
				set;
			}

			[JsonProperty("bandits")]
			public string bandits
			{
				get;
				set;
			}

			[JsonProperty("bannis(ip)")]
			public string banip
			{
				get;
				set;
			}

			[JsonProperty("banque")]
			public string banque
			{
				get;
				set;
			}

			[JsonProperty("cartes")]
			public string cartes
			{
				get;
				set;
			}

			[JsonProperty("cellules_script")]
			public string cellules_script
			{
				get;
				set;
			}

			[JsonProperty("challenge")]
			public string challenge
			{
				get;
				set;
			}

			[JsonProperty("coffre")]
			public string coffre
			{
				get;
				set;
			}

			[JsonProperty("commande")]
			public string commandes
			{
				get;
				set;
			}

			[JsonProperty("comptes")]
			public string comptes
			{
				get;
				set;
			}

			[JsonProperty("crafts")]
			public string crafts
			{
				get;
				set;
			}

			[JsonProperty("donjons")]
			public string donjons
			{
				get;
				set;
			}

			[JsonProperty("drops")]
			public string drops
			{
				get;
				set;
			}

			[JsonProperty("enclos")]
			public string enclos
			{
				get;
				set;
			}

			[JsonProperty("endfight")]
			public string endfight
			{
				get;
				set;
			}

			[JsonProperty("extra_monstres")]
			public string extra
			{
				get;
				set;
			}

			[JsonProperty("familiers")]
			public string familiers
			{
				get;
				set;
			}

			[JsonProperty("groupes")]
			public string groupes
			{
				get;
				set;
			}

			[JsonProperty("groupes_monstres")]
			public string groupes_monstres
			{
				get;
				set;
			}

			[JsonProperty("hdvs")]
			public string hdvs
			{
				get;
				set;
			}

			[JsonProperty("interactions")]
			public string interactions
			{
				get;
				set;
			}

			[JsonProperty("interaction_portes")]
			public string Iportes
			{
				get;
				set;
			}

			[JsonProperty("métiers")]
			public string job
			{
				get;
				set;
			}

			[JsonProperty("maisons")]
			public string maisons
			{
				get;
				set;
			}

			[JsonProperty("monstres")]
			public string monstres
			{
				get;
				set;
			}

			[JsonProperty("morphs")]
			public string morphs
			{
				get;
				set;
			}

			[JsonProperty("npc_questions")]
			public string npc_questions
			{
				get;
				set;
			}

			[JsonProperty("npc_reponse")]
			public string npc_reponse
			{
				get;
				set;
			}

			[JsonProperty("npcs")]
			public string npcs
			{
				get;
				set;
			}

			[JsonProperty("object_action")]
			public string object_action
			{
				get;
				set;
			}

			[JsonProperty("panoplies")]
			public string panoplies
			{
				get;
				set;
			}

			[JsonProperty("paroli")]
			public string paroli
			{
				get;
				set;
			}

			[JsonProperty("personnages")]
			public string personnages
			{
				get;
				set;
			}

			[JsonProperty("prismes")]
			public string prismes
			{
				get;
				set;
			}

			[JsonProperty("quêtes")]
			public string quêtes
			{
				get;
				set;
			}

			[JsonProperty("quêtes_étapes")]
			public string quêtes_étapes
			{
				get;
				set;
			}

			[JsonProperty("quêtes_objectif")]
			public string quêtes_objectif
			{
				get;
				set;
			}

			[JsonProperty("rss")]
			public string rss
			{
				get;
				set;
			}

			[JsonProperty("runes")]
			public string runes
			{
				get;
				set;
			}

			[JsonProperty("schema_fight")]
			public string schema_fight
			{
				get;
				set;
			}

			[JsonProperty("serveurs")]
			public string serveurs
			{
				get;
				set;
			}

			[JsonProperty("sorts")]
			public string sorts
			{
				get;
				set;
			}

			[JsonProperty("subarea_data")]
			public string subarea_data
			{
				get;
				set;
			}

			[JsonProperty("template_npc")]
			public string template_npc
			{
				get;
				set;
			}

			[JsonProperty("Template_objets")]
			public string Template_objets
			{
				get;
				set;
			}

			[JsonProperty("titres")]
			public string titres
			{
				get;
				set;
			}

			[JsonProperty("tuto")]
			public string tuto
			{
				get;
				set;
			}

			[JsonProperty("zaapi")]
			public string zaapi
			{
				get;
				set;
			}

			[JsonProperty("zaaps")]
			public string zaaps
			{
				get;
				set;
			}

			public AuthEntries()
			{
			}
		}

		public class Data
		{
			[JsonProperty("Tables_auth")]
			public AuthEntries AuthEntries
			{
				get;
				set;
			}

		}
	}
}