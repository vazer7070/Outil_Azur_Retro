using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tools_protocol.Data;

namespace Outil_Azur_complet.Editors
{
    internal static class ItemFieldCatalog
    {
        private static FieldSpec Hex(string title, string help = "")
        {
            return new FieldSpec(title,help) {
                FormatInput = value => { int number; return int.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out number) && number >= 0 ? number.ToString(CultureInfo.InvariantCulture) : value; },
                ParseInput = value => { int number; if (!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out number) || number < 0) throw new FormatException("Saisissez un entier positif ou nul."); return number.ToString("x",CultureInfo.InvariantCulture); }
            };
        }
        internal static FieldSpec Get(ServerResourceKind kind, DataColumn column, Dictionary<string,string> names)
        {
            string name = column.ColumnName.ToLowerInvariant();
            if (kind == ServerResourceKind.ItemTemplates)
            {
                switch (name)
                {
                    case "name": return new FieldSpec("Nom de l'objet","Nom affiché par le serveur.");
                    case "type": return new FieldSpec("Type d'objet","Choisissez une catégorie connue ou saisissez l'identifiant de votre serveur.") { Suggestions = ItemTypes().ToDictionary(item=>item.Value,item=>item.Text) };
                    case "level": return new FieldSpec("Niveau requis","Niveau minimal pour équiper l'objet.");
                    case "pod": return new FieldSpec("Poids (pods)","Poids d'un exemplaire.");
                    case "panoplie": return new FieldSpec("Panoplie","-1 signifie aucune panoplie.") { Suggestions = names };
                    case "statstemplate": return new FieldSpec("Effets de l'objet","Choisissez les effets et leurs valeurs ; les nombres sont présentés en décimal.","Effets") { Edit = EditEffects, Summary = CountEffects };
                    case "armesinfos": return new FieldSpec("Réglages de l'arme","Coût, portée, critiques et maniement.","Arme") { Edit = EditWeapon, Summary = data => data == "" ? "Aucun réglage d'arme" : "Paramètres de l'arme — Modifier pour les détailler" };
                    case "conditions": return new FieldSpec("Conditions d'utilisation","Construisez les conditions ou conservez une expression propre au serveur.","Conditions") { Edit = ConditionExpressionForm.Edit, Summary = value => value == "" ? "Aucune condition" : value };
                    case "prix": return new FieldSpec("Prix de vente du PNJ (kamas)","Prix payé pour acheter l'objet.","Commerce");
                    case "avgprice": return new FieldSpec("Prix moyen (kamas)","Prix moyen conservé par le serveur.","Commerce");
                    case "sold": return new FieldSpec("Nombre d'exemplaires vendus","Compteur de ventes du serveur.","Commerce");
                    case "points": return new FieldSpec("Prix en points","Coût dans les boutiques utilisant des points.","Commerce");
                    case "doplons": return new FieldSpec("Prix en doplons","Coût en doplons.","Commerce");
                    case "boutique": return Flag("Disponible en boutique","Autoriser cet objet dans la boutique.");
                    case "exchangeable": return Flag("Échange autorisé","Permettre l'échange entre personnages.");
                    case "heroique": return Flag("Option héroïque","Option utilisée par les serveurs héroïques.");
                    case "exchangesobject": return new FieldSpec("Paramètre d'échange","Valeur spécifique à l'émulateur.","Avancé");
                }
            }
            if (kind == ServerResourceKind.ItemSets)
            {
                if (name == "name") return new FieldSpec("Nom de la panoplie","Nom affiché par le serveur.");
                if (name == "items") return new FieldSpec("Objets de la panoplie","Ajoutez les modèles d'objets qui composent la panoplie.","Composition") { Edit = (owner,data) => DelimitedListForm.Edit(owner,"Composition de la panoplie","Une ligne par modèle d'objet. Les noms facilitent la sélection.",data,',','~',new[]{new FieldSpec("Modèle d'objet") { Suggestions = names, Validate = value => PositiveIds(value,',') }},value => Name(names,value)), Summary = data => data == "" ? "Aucun objet" : data.Split(',').Length + " objet(s)" };
                if (name == "bonus") return new FieldSpec("Bonus selon le nombre d'objets équipés","Les paliers suivent l'ordre : 2 objets, 3 objets, etc.","Bonus") { Edit = EditSetBonus, Summary = data => data == "" ? "Aucun bonus" : data.Split(';').Length + " palier(s) de bonus" };
            }
            if (kind == ServerResourceKind.Crafts)
            {
                if (name == "id") return new FieldSpec("Objet fabriqué","Identifiant du modèle obtenu avec cette recette.") { Suggestions = names };
                if (name == "craft") return new FieldSpec("Ingrédients de la recette","Ajoutez les ingrédients et les quantités nécessaires.","Recette") { Edit = (owner,data) => DelimitedListForm.Edit(owner,"Ingrédients de la recette","Une ligne par ingrédient. Les quantités doivent être supérieures à zéro.",data,';','*',new[]{new FieldSpec("Ingrédient") { Suggestions = names, Validate = value => PositiveIds(value,',') }, new FieldSpec("Quantité") { DefaultText = "1", Validate = value => { int number; if (!int.TryParse(value,out number) || number <= 0) throw new FormatException("La quantité doit être supérieure à zéro."); } }},record => { string[] parts = record.Split('*'); return Name(names,parts[0]) + (parts.Length > 1 ? " × " + parts[1] : ""); },2), Summary = data => data == "" ? "Aucun ingrédient" : data.Split(';').Count(value => value != "") + " ingrédient(s)" };
            }
            return null;
        }
        private static FieldSpec Flag(string title,string help) { return new FieldSpec(title,help,"Commerce") { Options = new[]{new EditorOption("0","Non"),new EditorOption("1","Oui")} }; }
        private static string Name(Dictionary<string,string> names,string id) { string name; return names != null && names.TryGetValue(id,out name) ? name + " · #" + id : "Objet #" + id; }
        internal static EditorOption[] ItemTypes()
        {
            // Types supported by the existing creator, with their actual server IDs.
            string[] types = { "1=Amulette","2=Arc","3=Baguette","4=Bâton","5=Dagues","6=Épée","7=Marteau","8=Pelle","9=Anneau","10=Ceinture","11=Bottes","12=Potion","15=Ressource","16=Chapeau","17=Cape","18=Familier","19=Hache","20=Outil","23=Dofus","24=Objet de quête","28=Bonbon","33=Pain","34=Ingrédient","43=Potion d'oubli","61=Peluche","75=Parchemin de sort","76=Parchemin de caractéristiques","78=Rune","82=Bouclier","83=Pierre d'âme","84=Clef","85=Gemme spirituelle","88=Rune de métier","89=Cadeau","97=Dragodinde","113=Obvijevan","116=Potion d'amélioration" };
            return types.Select(value => { int split=value.IndexOf('='); return new EditorOption(value.Substring(0,split),value.Substring(split+1)); }).ToArray();
        }
        internal static string CountEffects(string data) { return data == "" ? "Aucun effet" : data.Split(',').Count(value => value != "") + " effet(s)"; }
        internal static string EditEffects(IWin32Window owner,string data)
        {
            var effect = Hex("Type d'effet","Choisissez un effet ou saisissez son identifiant numérique.");
            effect.Suggestions = EffectsListing.ItemEffectList.ToDictionary(pair=>HexValue(pair.Key),pair=>pair.Value.Replace("$1","valeur minimale").Replace("$2","valeur maximale"));
            string result = DelimitedListForm.Edit(owner,"Effets de l'objet","Les valeurs sont en décimal. Le jet est recalculé lorsque seules les bornes changent.",data,',','#',new[]{effect,Hex("Valeur minimale"),Hex("Valeur maximale","0 peut désigner une valeur fixe selon l'effet."),Hex("Paramètre complémentaire","Valeur conservée pour les effets particuliers."),new FieldSpec("Jet de dés ou donnée de l'effet","Par exemple 1d6+4. Les effets textuels peuvent conserver une autre valeur.") { DefaultText = "0d0+0" }},record => { string[] parts=record.Split('#'); string name; return (EffectsListing.ItemEffectList.TryGetValue(parts[0],out name) ? name.Replace("$1",parts.Length>1?HexValue(parts[1]):"?").Replace("$2",parts.Length>2?HexValue(parts[2]):"?") : "Effet #"+HexValue(parts[0])); },5);
            if (result == null) return null;
            var original = data.Split(',').Select(record=>record.Split('#')).Where(parts=>parts.Length>=5).ToArray();
            string[] records=result.Split(',');
            for(int index=0;index<records.Length;index++)
            {
                string[] parts=records[index].Split('#'); if(parts.Length<5) continue;
                string[] old = original.FirstOrDefault(value=>value[0]==parts[0] && value[4]==parts[4]);
                if(old!=null && (old[1]!=parts[1] || old[2]!=parts[2]) && int.TryParse(parts[1],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int min) && int.TryParse(parts[2],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int max))
                { if(max==0) max=min; if(max>=min) parts[4]=Tool_Editor.items.EditorManager.CreateJet(min,max); records[index]=string.Join("#",parts); }
            }
            string updated=string.Join(",",records); ValidateEffects(updated); return updated;
        }
        private static string HexValue(string data) { int value; return int.TryParse(data,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value) && value>=0 ? value.ToString() : data; }
        private static string EditWeapon(IWin32Window owner,string data)
        { return DelimitedListForm.Edit(owner,"Réglages de l'arme","Une entrée contient tous les paramètres de l'arme, dans l'ordre du serveur.",data,'|',';',new[]{FieldSpec.Number("Coût en PA"),FieldSpec.Number("Portée minimale"),FieldSpec.Number("Portée maximale"),FieldSpec.Number("Taux de coup critique (1 sur N)"),FieldSpec.Number("Taux d'échec critique (1 sur N)"),FieldSpec.Number("Bonus aux coups critiques"),new FieldSpec("Arme à deux mains") { Options = new[]{new EditorOption("0","Non"),new EditorOption("1","Oui")} }},record=>"Paramètres de l'arme",7); }
        private static string EditSetBonus(IWin32Window owner,string data)
        { return DelimitedListForm.Edit(owner,"Paliers de bonus de la panoplie","La première entrée correspond à 2 objets équipés, la suivante à 3, puis 4, etc. Conservez cet ordre.",data,';','~',new[]{new FieldSpec("Effets du palier","Ajoutez les effets accordés par ce palier.") { Edit = (window,value)=>DelimitedListForm.Edit(window,"Bonus du palier","Chaque effet associe un identifiant numérique à une valeur.",value,',',':',new[]{new FieldSpec("Type d'effet") { Suggestions = EffectsListing.SpellsEffectList },FieldSpec.Number("Valeur du bonus")},record=>"Effet "+record,2), Summary = CountEffects }},record=>record==""?"Palier sans bonus":"Bonus : "+record); }
        internal static void PositiveIds(string data,char separator)
        { var used=new HashSet<int>(); foreach(string entry in data.Split(separator).Where(value=>value!="")) { int id; if(!int.TryParse(entry,out id) || id<=0 || !used.Add(id)) throw new FormatException("Chaque identifiant doit être positif et apparaître une seule fois."); } }
        internal static void ValidateRecipe(string data)
        { var used=new HashSet<int>(); foreach(string record in data.Split(';').Where(value=>value!="")) { string[] fields=record.Split('*'); int id,count; if(fields.Length!=2 || !int.TryParse(fields[0],out id) || id<=0 || !used.Add(id) || !int.TryParse(fields[1],out count) || count<=0) throw new FormatException("Recette invalide : ingrédients uniques et quantités positives requis."); } }
        internal static void ValidateEffects(string data)
        { foreach(string record in data.Split(',').Where(value=>value!="")) { string[] fields=record.Split('#'); if(fields.Length<2 || !int.TryParse(fields[0],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int id) || id<=0) throw new FormatException("Un effet d'objet contient un identifiant invalide."); } }
        internal static void ValidateSetBonus(string data)
        { foreach(string tier in data.Split(';')) foreach(string record in tier.Split(',').Where(value=>value!="")) { string[] fields=record.Split(':'); int id,value; if(fields.Length!=2 || !int.TryParse(fields[0],out id) || id<=0 || !int.TryParse(fields[1],out value)) throw new FormatException("Bonus invalide : chaque effet doit associer un identifiant et une valeur entiers."); } }
    }
}
