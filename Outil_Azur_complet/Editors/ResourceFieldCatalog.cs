using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Outil_Azur_complet.Editors
{
    internal static class ResourceFieldCatalog
    {
        private static EditorOption[] Options(params string[] pairs)
        { return pairs.Select(pair => { int index = pair.IndexOf('='); return new EditorOption(pair.Substring(0, index), pair.Substring(index + 1)); }).ToArray(); }
        private static readonly Dictionary<string, FieldSpec> Common = new Dictionary<string, FieldSpec>(StringComparer.OrdinalIgnoreCase) {
            { "id", new FieldSpec("Identifiant", "Numéro unique de la ressource. Choisissez un identifiant libre pour une nouvelle fiche.") },
            { "name", new FieldSpec("Nom", "Nom utilisé pour reconnaître cette ressource.") }, { "nom", new FieldSpec("Nom", "Nom affiché dans les ressources du serveur.") },
            { "Name IO", new FieldSpec("Nom de l'objet interactif", "Libellé de l'élément utilisable sur la carte.") },
            { "mapid", new FieldSpec("Carte", "Identifiant de la carte. Dans un éditeur ouvert depuis une carte, cette valeur est liée à cette carte.") },
            { "cellid", new FieldSpec("Cellule de placement", "Numéro de la cellule, à partir de 0. La cellule sélectionnée sur la carte est proposée à l'ajout.") },
            { "npcid", new FieldSpec("Personnage non joueur (PNJ)", "Identifiant d'une définition de PNJ existante dans les ressources du serveur.") },
            { "orientation", new FieldSpec("Direction du regard", "Orientation du personnage sur la carte.") { Options = Options("0=Est", "1=Sud-est", "2=Sud", "3=Sud-ouest", "4=Ouest", "5=Nord-ouest", "6=Nord", "7=Nord-est") } },
            { "isMovable", new FieldSpec("Autoriser le déplacement", "Permettre au PNJ de se déplacer sur la carte.") { Options = Options("0=Non", "1=Oui") } },
            { "sprite", new FieldSpec("Animation du sort", "Identifiant graphique de l'animation ; -1 correspond à la valeur spéciale du serveur.", "Apparence") },
            { "spriteInfos", new FieldSpec("Réglages de l'animation", "Trois paramètres séparés par des virgules, par exemple 0,0,0. Leur interprétation dépend du client.", "Apparence") },
            { "effectTarget", new FieldSpec("Restrictions des cibles", "Paramètres des cibles pour les effets, dans le format du serveur. Vide conserve l'absence de restrictions.", "Avancé", true) },
            { "durer", new FieldSpec("Durée du sort", "Paramètre de durée utilisé par certaines variantes du serveur.", "Avancé") },
            { "gfxID", new FieldSpec("Apparence graphique", "Identifiant du modèle affiché dans le client.", "Apparence") },
            { "scaleX", new FieldSpec("Échelle horizontale (%)", "100 correspond à la taille normale.", "Apparence") },
            { "scaleY", new FieldSpec("Échelle verticale (%)", "100 correspond à la taille normale.", "Apparence") },
            { "size", new FieldSpec("Taille (%)", "100 correspond à la taille normale.", "Apparence") },
            { "sex", new FieldSpec("Sexe", "Valeur utilisée pour l'apparence du PNJ.", "Apparence") { Options = Options("0=Masculin", "1=Féminin") } },
            { "color1", new FieldSpec("Première couleur", "Couleur numérique du client ; -1 utilise la couleur par défaut.", "Apparence") },
            { "color2", new FieldSpec("Deuxième couleur", "Couleur numérique du client ; -1 utilise la couleur par défaut.", "Apparence") },
            { "color3", new FieldSpec("Troisième couleur", "Couleur numérique du client ; -1 utilise la couleur par défaut.", "Apparence") },
            { "colors", new FieldSpec("Couleurs", "Trois couleurs séparées par des virgules. -1 utilise la couleur par défaut.", "Apparence") },
            { "accessories", new FieldSpec("Accessoires", "Identifiants des accessoires dans leur ordre d'affichage, séparés par des virgules.", "Apparence") },
            { "extraClip", new FieldSpec("Élément graphique supplémentaire", "Identifiant du complément visuel ; -1 pour la valeur par défaut.", "Apparence") },
            { "customArtWork", new FieldSpec("Illustration personnalisée", "Identifiant de l'illustration du PNJ.", "Apparence") },
            { "bonusValue", new FieldSpec("Valeur de bonus", "Paramètre de bonus de la définition du PNJ.", "Avancé") },
            { "initQuestion", new FieldSpec("Question initiale du dialogue", "Identifiant de la question affichée au début de la conversation.", "Interactions") },
            { "ventes", new FieldSpec("Objets proposés à la vente", "Identifiants des objets vendus, dans le format du serveur.", "Interactions", true) },
            { "quests", new FieldSpec("Quêtes associées", "Identifiants et options des quêtes, dans le format du serveur.", "Interactions", true) },
            { "exchanges", new FieldSpec("Échanges proposés", "Paramètres des échanges avec le PNJ, dans le format du serveur.", "Interactions", true) },
            { "path", new FieldSpec("Parcours du PNJ", "Itinéraire de déplacement dans le format du serveur.", "Interactions", true) },
            { "groupData", new FieldSpec("Composition du groupe", "Une ligne par monstre, avec son niveau minimal et maximal.", "Groupe") { Edit = EditGroup, Summary = value => value.Split(';').Count(part => part != "") + " entrée(s) de monstre" } },
            { "Donjon", new FieldSpec("Identifiant du donjon", "Donjon auquel appartient le groupe ; 0 pour la valeur habituelle hors donjon.", "Groupe") },
            { "Salle", new FieldSpec("Salle du donjon", "Numéro de salle utilisé par le serveur.", "Groupe") },
            { "Timer", new FieldSpec("Délai de réapparition (millisecondes)", "30 000 ms = 30 secondes. 0 conserve un délai nul.", "Groupe") },
            { "message", new FieldSpec("Message associé", "Message de ce groupe de monstres.", "Groupe", true) },
            { "owner", new FieldSpec("Propriétaire de l'enclos", "Identifiant du propriétaire. Les valeurs spéciales, notamment -1, dépendent des règles du serveur.", "Propriété") },
            { "guild", new FieldSpec("Guilde propriétaire", "Identifiant de la guilde ; -1 indique la valeur par défaut du serveur.", "Propriété") },
            { "price", new FieldSpec("Prix de l'enclos (kamas)", "Prix positif ou nul.", "Propriété") },
            { "data", new FieldSpec("Données de l'étable", "Données des montures et de l'étable dans le format du serveur.", "Montures", true) },
            { "enclos", new FieldSpec("Montures présentes dans l'enclos", "Identifiants et données des montures placées.", "Montures", true) },
            { "ObjetPlacer", new FieldSpec("Objets d'élevage placés", "Une ligne par objet : cellule, objet et paramètre complémentaire.", "Élevage") { Edit = EditPaddockObjects, Summary = CountPaddock } },
            { "durabilite", new FieldSpec("Durabilité des objets d'élevage", "Une ligne par objet : cellule, durabilité et paramètre complémentaire.", "Élevage") { Edit = EditPaddockDurability, Summary = CountPaddock } },
            { "respawn", new FieldSpec("Délai de réapparition (millisecondes)", "10 000 ms = 10 secondes. Temps avant que l'objet soit à nouveau disponible.", "Utilisation") },
            { "duration", new FieldSpec("Durée de l'action (millisecondes)", "1 500 ms = 1,5 seconde.", "Utilisation") },
            { "unknow", new FieldSpec("Paramètre complémentaire du serveur", "Option historique de l'objet interactif ; son rôle dépend de l'émulateur.", "Avancé") },
            { "walkable", new FieldSpec("Cellule praticable", "Autoriser le passage sur la cellule de l'objet.", "Utilisation") { Options = Options("0=Non", "1=Oui") } },
            { "tools", new FieldSpec("Outils du métier", "Identifiants des objets utilisables comme outils.", "Activités") { Edit = EditTools, Summary = value => value == "" ? "Aucun outil" : value.Split(',').Length + " outil(s)" } },
            { "crafts", new FieldSpec("Fabrications autorisées", "Chaque entrée associe une compétence aux identifiants des objets à fabriquer.", "Activités") { Edit = EditCrafts, Summary = value => value == "" ? "Aucune fabrication" : value.Split('|').Length + " entrée(s) de fabrication" } },
            { "skills", new FieldSpec("Compétences du métier", "Associations entre les objets interactifs et les compétences.", "Activités") { Edit = EditJobSkills, Summary = value => value == "" ? "Aucune compétence" : value.Split('|').Length + " association(s)" } },
            { "AP", new FieldSpec("Paramètres d'action du métier (AP)", "Paramètres propres au serveur, séparés par des virgules ; par exemple 5,10,20,30.", "Avancé") },
            { "align", new FieldSpec("Alignement", "Identifiant d'alignement utilisé par le serveur.", "Combat") },
            { "grades", new FieldSpec("Niveaux et résistances", "Données de chaque grade de monstre, séparées par « | ».", "Combat", true) },
            { "stats", new FieldSpec("Caractéristiques par grade", "Force, sagesse, intelligence, chance, agilité ; grades séparés par « | ».", "Combat", true) },
            { "statsInfos", new FieldSpec("Bonus de combat", "Dommages ; dommages en % ; soins ; créatures invocables.", "Combat") },
            { "spells", new FieldSpec("Sorts par grade", "Associations sort@niveau séparées par « ; » ; grades séparés par « | ».", "Combat", true) },
            { "pdvs", new FieldSpec("Points de vie par grade", "Valeurs séparées par « | ».", "Combat") },
            { "points", new FieldSpec("PA et PM par grade", "PA;PM pour chaque grade, séparés par « | ».", "Combat") },
            { "inits", new FieldSpec("Initiative par grade", "Valeurs séparées par « | ».", "Combat") },
            { "minKamas", new FieldSpec("Kamas gagnés : minimum", "Récompense minimale du combat.", "Récompenses") },
            { "maxKamas", new FieldSpec("Kamas gagnés : maximum", "Récompense maximale du combat.", "Récompenses") },
            { "exps", new FieldSpec("Expérience par grade", "Récompenses d'expérience séparées par « | ».", "Récompenses") },
            { "AI_Type", new FieldSpec("Comportement du monstre", "Type d'intelligence artificielle.", "Combat") { Options = Options("0=Immobile (poutch)", "1=Agressif", "2=Fuyard", "3=Soutien", "4=Spécial") } },
            { "capturable", new FieldSpec("Capture autorisée", "Le monstre peut être capturé.", "Combat") { Options = Options("0=Non", "1=Oui") } },
            { "aggroDistance", new FieldSpec("Distance d'agression", "Distance à laquelle le groupe déclenche un combat.", "Combat") },
            { "iaModels", new FieldSpec("Modèles de comportement", "Paramètres complémentaires de l'intelligence artificielle.", "Avancé", true) }
        };
        internal static FieldSpec Get(ServerResourceKind kind, DataColumn column, int? cellCount = null, Dictionary<string, string> referenceNames = null)
        {
            string name = column.ColumnName;
            var item = ItemFieldCatalog.Get(kind,column,referenceNames);
            if (item != null) return item;
            var additional = AdditionalResourceFields.Get(kind,column,referenceNames);
            if (additional != null) return additional;
            if (kind == ServerResourceKind.Spells && name.StartsWith("lvl", StringComparison.OrdinalIgnoreCase) && name.Length == 4 && name[3] >= '1' && name[3] <= '6')
                return new FieldSpec("Niveau " + name[3], "Coût, portée, effets, zones et conditions de ce niveau.", "Niveaux") { Edit = (owner, data) => SpellLevelForm.Edit(owner, "Sort — niveau " + name[3], data), Summary = SpellLevelData.Describe };
            if (name.Equals("type", StringComparison.OrdinalIgnoreCase)) {
                if (kind == ServerResourceKind.Monsters) return new FieldSpec("Catégorie du monstre") { Options = Options("1=Monstre", "2=Mascotte", "3=Archimonstre") };
                return new FieldSpec("Catégorie du sort", "Paramètre de classification du serveur.", "Avancé");
            }
            FieldSpec known;
            if (Common.TryGetValue(name, out known)) {
                if (name.Equals("cellid", StringComparison.OrdinalIgnoreCase) && cellCount.HasValue) return new FieldSpec(known.Label, "Cellule comprise entre 0 et " + (cellCount.Value - 1) + ".", known.Section);
                var spec = new FieldSpec(known.Label, known.Help, known.Section, known.Multiline) { Options = known.Options, Edit = known.Edit, Summary = known.Summary };
                if (name.Equals("npcid", StringComparison.OrdinalIgnoreCase)) spec.Suggestions = referenceNames;
                if(kind==ServerResourceKind.NpcTemplates && name.Equals("initQuestion",StringComparison.OrdinalIgnoreCase))spec.Suggestions=referenceNames;
                if (name.Equals("groupData", StringComparison.OrdinalIgnoreCase)) spec.Edit = (owner, data) => EditGroup(owner, data, referenceNames);
                if (kind == ServerResourceKind.Jobs && name.Equals("tools", StringComparison.OrdinalIgnoreCase)) spec.Edit = (owner, data) => EditTools(owner, data, referenceNames);
                if (kind == ServerResourceKind.Monsters) ConfigureMonsterField(name, spec, referenceNames);
                if(name.Equals("color1",StringComparison.OrdinalIgnoreCase)||name.Equals("color2",StringComparison.OrdinalIgnoreCase)||name.Equals("color3",StringComparison.OrdinalIgnoreCase)) {
                    spec.Edit=ColorValueForm.Edit;spec.Summary=value=>value=="-1"?"Couleur par défaut":int.TryParse(value,out int color)&&color>=0?"#"+color.ToString("X6"):value;
                }
                if(name.Equals("colors",StringComparison.OrdinalIgnoreCase))spec.Edit=(owner,data)=>DelimitedListForm.Edit(owner,"Couleurs du PNJ","Trois couleurs : utilisez -1 pour conserver la couleur par défaut.",data,'|',',',new[]{new FieldSpec("Première couleur"){Edit=ColorValueForm.Edit},new FieldSpec("Deuxième couleur"){Edit=ColorValueForm.Edit},new FieldSpec("Troisième couleur"){Edit=ColorValueForm.Edit}},record=>"Couleurs du PNJ",3);
                return spec;
            }
            return new FieldSpec("Option additionnelle : " + Humanize(name), "Champ propre à votre serveur (« " + name + " »). Sa valeur est conservée et reste modifiable.", "Avancé", column.DataType == typeof(string));
        }
        private static string Humanize(string name) { return System.Text.RegularExpressions.Regex.Replace(name.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2"); }
        private static string CountPaddock(string value) { return value == "" ? "Aucune entrée" : value.Split('|').Length + " entrée(s)"; }
        private static string EditGroup(IWin32Window owner, string value) { return EditGroup(owner, value, null); }
        private static string EditGroup(IWin32Window owner, string value, Dictionary<string, string> names)
        { return DelimitedListForm.Edit(owner, "Composition du groupe de monstres", "Ajoutez chaque monstre et sa plage de niveaux. Plusieurs lignes peuvent utiliser le même monstre.", value, ';', ',', new[] { new FieldSpec("Monstre", "Choisissez son nom ou saisissez l'identifiant de sa définition.") { Suggestions = names }, new FieldSpec("Niveau minimal", "Plus petit niveau autorisé."), new FieldSpec("Niveau maximal", "Plus grand niveau autorisé.") }, record => { string[] p = record.Split(','); string name; return (names != null && names.TryGetValue(p[0], out name) ? name : "Monstre #" + p[0]) + (p.Length >= 3 ? " · niveaux " + p[1] + " à " + p[2] : ""); }, 3); }
        private static string EditTools(IWin32Window owner, string value) { return EditTools(owner, value, null); }
        private static string EditTools(IWin32Window owner, string value, Dictionary<string, string> names)
        { return DelimitedListForm.Edit(owner, "Outils du métier", "Une ligne par outil. Choisissez l'objet dans la liste de noms ou saisissez son identifiant.", value, ',', '~', new[] { new FieldSpec("Objet utilisé comme outil", "Identifiant du modèle d'objet.") { Suggestions = names } }, record => { string name; return names != null && names.TryGetValue(record, out name) ? name + " · #" + record : "Objet #" + record; }); }
        private static void ConfigureMonsterField(string column, FieldSpec spec, Dictionary<string, string> spells)
        {
            switch (column.ToLowerInvariant())
            {
                case "stats":
                    SetGradeEditor(spec, "Caractéristiques des monstres", ',', new[] { FieldSpec.Number("Force"), FieldSpec.Number("Sagesse"), FieldSpec.Number("Intelligence"), FieldSpec.Number("Chance"), FieldSpec.Number("Agilité") }); break;
                case "points":
                    SetGradeEditor(spec, "PA et PM des monstres", ';', new[] { FieldSpec.Number("Points d'action (PA)"), FieldSpec.Number("Points de mouvement (PM)") }); break;
                case "pdvs": SetGradeEditor(spec, "Points de vie des monstres", ';', new[] { FieldSpec.Number("Points de vie") }); break;
                case "inits": SetGradeEditor(spec, "Initiative des monstres", ';', new[] { FieldSpec.Number("Initiative") }); break;
                case "exps": SetGradeEditor(spec, "Expérience des monstres", ';', new[] { FieldSpec.Number("Expérience gagnée") }); break;
                case "grades":
                    SetGradeEditor(spec, "Niveaux des monstres", '@', new[] { FieldSpec.Number("Niveau du grade"), new FieldSpec("Résistances et esquives", "Paramètres séparés par « ; », dans l'ordre attendu par votre serveur.") }); break;
                case "spells":
                    SetGradeEditor(spec, "Sorts des monstres", '~', new[] { new FieldSpec("Sorts de ce grade", "Ajoutez chaque sort et son niveau.") { Edit = (owner, data) => DelimitedListForm.Edit(owner, "Sorts du grade", "Une ligne par sort. Choisissez le sort puis son niveau.", data, ';', '@', new[] { new FieldSpec("Sort", "Choisissez son nom ou saisissez son identifiant.") { Suggestions = spells }, new FieldSpec("Niveau du sort") { Options = Enumerable.Range(1, 6).Select(level => new EditorOption(level.ToString(), "Niveau " + level)).ToArray(), DefaultText = "1" } }, record => { string[] p = record.Split('@'); string name; return (spells != null && spells.TryGetValue(p[0], out name) ? name : "Sort #" + p[0]) + (p.Length > 1 ? " · niveau " + p[1] : ""); }, 2), Summary = data => string.IsNullOrEmpty(data) ? "Aucun sort" : data.Split(';').Length + " sort(s)" } }); break;
            }
        }
        private static void SetGradeEditor(FieldSpec spec, string title, char separator, FieldSpec[] fields)
        {
            spec.Multiline = false; spec.Summary = value => value == "" ? "Aucun grade défini" : value.Split('|').Length + " grade(s) — Modifier pour régler les valeurs";
            spec.Edit = (owner, data) => DelimitedListForm.Edit(owner, title, "Une ligne par grade, dans le même ordre que les niveaux du monstre. Les autres paramètres sont conservés.", data, '|', separator, fields, record => record == "" ? "Grade sans valeur" : "Valeurs : " + record, fields.Length);
        }
        private static string EditCrafts(IWin32Window owner, string value)
        { return DelimitedListForm.Edit(owner, "Fabrications du métier", "Une entrée par compétence de fabrication. Les variantes du serveur restent accessibles dans les autres paramètres.", value, '|', ';', new[] { new FieldSpec("Identifiant de la compétence"), new FieldSpec("Objets à fabriquer", "Identifiants séparés par des virgules.", multiline: true) }, record => "Compétence " + record.Split(';')[0], 2); }
        private static string EditJobSkills(IWin32Window owner, string value)
        { return DelimitedListForm.Edit(owner, "Compétences du métier", "Chaque association lie un objet interactif à une ou plusieurs compétences.", value, '|', ';', new[] { new FieldSpec("Identifiant de l'objet interactif"), new FieldSpec("Compétences associées", "Identifiants séparés par des virgules.") }, record => "Objet interactif #" + record.Split(';')[0], 2); }
        private static string EditPaddockObjects(IWin32Window owner, string value)
        { return DelimitedListForm.Edit(owner, "Objets d'élevage", "Une ligne par objet placé dans l'enclos.", value, '|', ';', new[] { new FieldSpec("Cellule de placement"), new FieldSpec("Identifiant de l'objet"), new FieldSpec("Paramètre complémentaire", "Option d'état interprétée par le serveur.") }, record => "Cellule " + record.Split(';')[0], 3); }
        private static string EditPaddockDurability(IWin32Window owner, string value)
        { return DelimitedListForm.Edit(owner, "Durabilité des objets d'élevage", "Une ligne par objet placé ; conservez les paramètres complémentaires attendus par le serveur.", value, '|', ';', new[] { new FieldSpec("Cellule de placement"), new FieldSpec("Durabilité"), new FieldSpec("Paramètre complémentaire", "Option historique dont l'interprétation dépend du serveur.") }, record => "Cellule " + record.Split(';')[0], 3); }
    }
}
