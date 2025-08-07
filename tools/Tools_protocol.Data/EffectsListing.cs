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
        #region LonguesVariables
        
        private static List<string> Effets = new List<string>
{
    "Ajoute $ PM=78",
    "La cible perd $ PA=101",
    "Ajoute $ PDV=110",
    "Ajoute $ PA=111",
    "Multiplie les dommages par $=114",
    "Ajoute $ aux Coups critiques=115",
    "Retire $ a la portee=116",
    "Ajoute $ a la portee=117",
    "Ajoute $ en force=118",
    "Ajoute $ en agilite=119",
    "Ajoute +$ PA=120",
    "Ajoute $ dommage(s)=112",
    "Ajoute $ aux echecs critiques=122",
    "Ajoute $ a la chance=123",
    "Ajoute $ en sagesse=124",
    "Ajoute $ en vitalite=125",
    "Ajoute $ en intelligence=126",
    "PM perdus: $=127",
    "Ajoute $ PM=128",
    "Augmente les dommages de $=138",
    "Ajoute $ aux dommages physiques=142",
    "Retire $ aux dommages=145",
    "Retire $ en chance=152",
    "Retire $ en vitalite=153",
    "Retire $ en agilite=154",
    "Retire $ en intelligence=155",
    "Retire $ en sagesse=156",
    "Retire $ en force=157",
    "Augmente le poids portable de $=158",
    "Reduit le poids portable de $=159",
    "$ de chance d'esquiver les pertes de PA=160",
    "$ de chance d'esquiver les pertes de PM=161",
    "$ de chance d'esquiver les pertes de PA=162",
    "$ de chance d'esquiver les pertes de PM=163",
    "ADD_MAITRISE$=165",
    "Retire $ PA=168",
    "Retire $ PM=169",
    "-$ aux coups critiques=171",
    "Ajoute $ en initiative=174",
    "Retire $ en initiative=175",
    "Ajoute $ en prospection=176",
    "Retire $ en prospection=177",
    "Ajoute $ en soins=178",
    "Retire $ en soins=179",
    "+$ creatures invocables=182",
    "Ajoute $% de resistance Terre=210",
    "Ajoute $% de resistance Eau=211",
    "Ajoute $% de resistance Air=212",
    "Ajoute $% de resistance Feu=213",
    "Ajoute $% de resistance Neutre=214",
    "Retire $% de resistance Terre=215",
    "Retire $% de resistance Eau=216",
    "Retire $% de resistance Air=217",
    "Retire $% de resistance Feu=218",
    "Retire $% de resistance Neutre=219",
    "Renvoie $ dommages=220",
    "Ajoute $ de dommages aux pieges=225",
    "Ajoute $ de dommages aux pieges=226",
    "Ajoute $ en resistance Feu=240",
    "Ajoute $ en resistance Neutre=241",
    "Ajoute $ en resistance Terre=242",
    "Ajoute $ en resistance Eau=243",
    "Ajoute $ en resistance Air=244",
    "Retire $ en resistance Feu=245",
    "Retire $ en resistance Neutre=246",
    "Retire $ en resistance Terre=247",
    "Retire $ en resistance Eau=248",
    "Retire $ en resistance Air=249",
    "Ajoute $ en resistance Terre (pvp)=250",
    "Ajoute $ en resistance Eau (pvp)=251",
    "Ajoute $ en resistance Air (pvp)=252",
    "Ajoute $ en resistance Feu (pvp)=253",
    "Ajoute $ en resistance Neutre (pvp)=254",
    "+$% en faiblesse Terre (pvp)=255",
    "+$% en faiblesse Eau (pvp)=256",
    "+$% en faiblesse Air (pvp)=257",
    "+$% en faiblesse Feu (pvp)=258",
    "+$% en faiblesse Neutre (pvp)=259",
    "Ajoute $ en resistance Terre=260",
    "Ajoute $ en resistance Eau=261",
    "Ajoute $ en resistance Air=262",
    "Ajoute $ en resistance Feu=263",
    "Ajoute $ en resistance Neutre=264",
    "Reduction des degats physique de $=184",
    "Reduction des degats magique de $=183",
    "Vole $1 à $2 PDV (eau)=91",
    "Vole $1 à $2 PDV (terre)=92",
    "Vole $1 à $2 PDV (air)=93",
    "Vole $1 à $2 PDV (feu)=94",
    "Vole $1 à $2 PDV (neutre)=95",
    "Dommages $1 à $2 (eau)=96",
    "Dommages $1 à $2 (terre)=97",
    "Dommages $1 à $2 (air)=98",
    "Dommages $1 à $2 (feu)=99",
    "Dommages $1 à $2 (neutre)=100",
    "PDV rendus : $1 à $2=108"
};
        private static List<string> ItemsEffets = new List<string>
{
    "+$1 a $2 en vitalite=7d",
    "+$1 a $2 en sagesse=7c",
    "+$1 a $2 en force=76",
    "+$1 a $2 en intelligence=7e",
    "+$1 a $2 en chance=7b",
    "+$1 a $2 en agilite=77",
    "+$1 a $2 PA=6f",
    "+$1 a $2 PM=80",
    "+$1 a $2 en initiative=ae",
    "+$1 a $2 en dommage=70",
    "+$1 a $2 en dommage(%)=8a",
    "+$1 a $2 en coup critique=73",
    "+$1 a $2 en echec critique=7a",
    "+$1 a $2 en portee=75",
    "+$1 a $2 en creature invocable=b6",
    "+$1 a $2 en pod=9e",
    "+$1 a $2 en prospection=b0",
    "+$1 a $2 en soin=b2",
    "+$1 a $2 dommages neutres=64",
    "arme de chasse=31b",
    "+$1 a $2 en renvoi de dommages=dc",
    "+$1 a $2 en resistance neutre(%)=d6",
    "+$1 a $2 en resistance terre(%)=d2",
    "+$1 a $2 en resistance feu(%)=d5",
    "+$1 a $2 en resistance eau(%)=d3",
    "+$1 a $2 en resistance air(%)=d4",
    "+$1 a $2 en resistance neutre=f4",
    "+$1 a $2 en resistance terre=f0",
    "+$1 a $2 en resistance feu=f3",
    "+$1 a $2 en resistance eau=f1",
    "+$1 a $2 en resistance air=f2",
    "+$1 a $2 en dommages piege=e1",
    "+$1 a $2 en dommages piege(%)=e2",
    "+$1 a $2 en % de chance d'esquiver les pertes de PA=a0",
    "+$1 a $2 en % de chance d'esquiver les pertes de PM=a1",
    "+$1 a $2 en resistance neutre (pvp)=108",
    "+$1 a $2 en resistance terre (pvp)=104",
    "+$1 a $2 en resistance feu (pvp)=107",
    "+$1 a $2 en resistance eau (pvp)=105",
    "+$1 a $2 en resistance air (pvp)=106",
    "- $1 a $2 en vitalite=99",
    "- $1 a $2 en sagesse=9c",
    "- $1 a $2 en force=9d",
    "- $1 a $2 en intelligence=9b",
    "- $1 a $2 en chance=98",
    "- $1 a $2 en agilite=9a",
    "- $1 a $2 PA=65",
    "- $1 a $2 PM =7f",
    "- $1 a $2 en initiative=af",
    "- $1 a $2 en dommage=91",
    "- $1 a $2 en dommage(%)=ba",
    "- $1 a $2 en coup critique=ab",
    "Apprend le metier en lien avec le parchemin=266",
    "Donne de l'experience=25b"
};
        private static List<string> SpellEffets = new List<string>
{
    "4=Teleportation",
    "5=Repousse de $ case",
    "6=Attire de $ case",
    "8=Echange les places de 2 joueur",
    "9=Esquive une attaque en reculant de 1 case",
    "50=Porter",
    "51=jeter",
    "77=Vol de PM",
    "78=Bonus PM",
    "79=+Attribut% dommages subis * jetmin sinon soigne de jetmax",
    "82=Vol de Vie fixe",
    "84=Vol de PA",
    "85=Dommage Eau $%vie",
    "86=Dommage Terre $%vie",
    "87=Dommage Air $%vie",
    "88=Dommage feu $%vie",
    "89=Dommage neutre $%vie",
    "90=Donne $% de sa vie",
    "91=Vol de Vie Eau",
    "92=Vol de Vie Terre",
    "93=Vol de Vie Air",
    "94=Vol de Vie feu",
    "95=Vol de Vie neutre",
    "96=Dommage Eau",
    "97=Dommage Terre",
    "98=Dommage Air",
    "99=Dommage feu",
    "100=Dommage neutre",
    "101=Retrait PA",
    "105=Dommages reduits de $",
    "106=Renvoie de sort",
    "107=Renvoie de dom",
    "108=Soin",
    "109=Dommage pour le lanceur",
    "110=+$ vie",
    "111=+$ PA",
    "112=+$ Dom",
    "114=Multiplie les dommages par X",
    "115=+$ Cc",
    "116=Malus PO",
    "117=Bonus PO",
    "118=Bonus force",
    "119=Bonus Agilite",
    "120=Bonus PA",
    "121=+$ Dom",
    "122=+$ EC",
    "123=+$ Chance",
    "124=+$ Sagesse",
    "125=+$ Vitalite",
    "126=+$ Intelligence",
    "127=Retrait PM",
    "128=+$PM",
    "131=Poison = $ Pdv par PA",
    "132=Enleve les envoutements",
    "138=$%dom",
    "140=Passer le tour",
    "141=Tue la cible",
    "142=Dommages physique",
    "145=Malus Dommage",
    "149=Change l'apparence",
    "150=Invisibilite",
    "155=-$Intell",
    "160=+$Esquive PA",
    "161=+$Esquive PM",
    "162=-Esquive PA",
    "163=-Esquive PM",
    "168=Perte PA non esquivable",
    "169=Perte PM non esquivable",
    "171=Malus CC",
    "181=Invoque une creature",
    "182=+ Crea Invoc",
    "183=Resist Magique",
    "184=Resist Physique",
    "185=Invoque une creature statique",
    "210=Resist $% terre",
    "211=Resist $% eau",
    "212=Resist $% air",
    "213=Resist $% feu",
    "214=Resist $% neutre",
    "215=Faiblesse $% terre",
    "216=Faiblesse $% eau",
    "217=Faiblesse $% air",
    "218=Faiblesse $% feu",
    "219=Faiblesse $% neutre",
    "265=Reduit les Dom de $",
    "266=Vol Chance",
    "267=Vol vitalite",
    "268=Vol agitlite",
    "269=Vol intell",
    "270=Vol sagesse",
    "271=Vol force",
    "293=Augmente les degâts de base du sort $1 de $2",
    "320=Vol de PO",
    "400=Creer un  piège",
    "401=Creer un glyphe",
    "666=Pas d'effet complementaire",
    "672=Dommages = $% de la vie de l'attaquant",
    "783=Pousse jusqu'a la vise",
    "788=Chatiment de $1 sur $2 tours",
    "776=$% de degâts subis",
    "951=Enleve l'Etat $",
    "950=Etat"
};



        #endregion

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
            if (!File.Exists(path_effects))
                File.WriteAllLines(path_effects, Effets.ToArray());

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

		public static void Load_ItemEffects(string path_file)
		{
			if (!File.Exists(path_file))
                File.WriteAllLines(path_file, ItemsEffets.ToArray());
            string[] strArrays = File.ReadAllLines(path_file);
            for (int i = 0; i < (int)strArrays.Length; i++)
            {
                string effet = strArrays[i];
                EffectsListing.ItemEffectList.Add(effet.Split(new char[] { '=' })[1], effet.Split(new char[] { '=' })[0]);
            }
            EffectsListing.ItemEffects_count = EffectsListing.ItemEffectList.Count<KeyValuePair<string, string>>();
        }

		public static void Load_SpellsEffects(string path)
		{
			if (!File.Exists(path))
                File.WriteAllLines(path, SpellEffets.ToArray());
            string[] strArrays = File.ReadAllLines(path);
            for (int i = 0; i < (int)strArrays.Length; i++)
            {
                string effet = strArrays[i];
                EffectsListing.SpellsEffectList.Add(effet.Split(new char[] { '=' })[0], effet.Split(new char[] { '=' })[1]);
            }
            EffectsListing.SpellsEffects_count = EffectsListing.SpellsEffectList.Count<KeyValuePair<string, string>>();
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