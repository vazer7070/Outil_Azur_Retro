using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Outil_Azur_complet.Editors
{
    internal static class AdditionalResourceFields
    {
        private static readonly Dictionary<string,string[]> Labels = new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase) {
            {"monsterName",new[]{"Nom du monstre","Nom associé au butin.","Butin"}},
            {"monsterId",new[]{"Monstre","Identifiant du monstre qui donne ce butin.","Butin"}},
            {"objectName",new[]{"Nom de l'objet","Nom associé au butin.","Butin"}},
            {"objectId",new[]{"Objet obtenu","Modèle d'objet donné comme butin.","Butin"}},
            {"ceil",new[]{"Seuil de prospection","Prospection minimale requise.","Probabilités"}},
            {"responses",new[]{"Réponses proposées","Identifiants des réponses, dans l'ordre du dialogue.","Dialogue"}},
            {"params",new[]{"Paramètres de la question","Valeurs transmises au dialogue par le serveur.","Dialogue"}},
            {"ifFalse",new[]{"Alternative si la condition échoue","Question ou action de remplacement, au format du serveur.","Dialogue"}},
            {"description",new[]{"Description","Texte associé à cette fiche.","Général"}},
            {"args",new[]{"Arguments de l'action","Paramètres attendus par l'action du serveur.","Action"}},
            {"ActionsArgs",new[]{"Arguments de l'action","Pour une téléportation : carte,cellule.","Action"}},
            {"ActionID",new[]{"Action","Identifiant de l'action ; 0 correspond à la téléportation dans les exports de cartes Azur.","Action"}},
            {"EventID",new[]{"Événement déclencheur","Identifiant de l'événement traité par le serveur.","Action"}},
            {"fighttype",new[]{"Type de combat","Identifiant du type de combat concerné.","Action"}},
            {"action",new[]{"Action du serveur","Identifiant ou liste d'actions dans le format de cette table.","Action"}},
            {"etapes",new[]{"Étapes de la quête","Identifiants des étapes, dans leur ordre.","Quête"}},
            {"objectif",new[]{"Objectifs","Identifiants ou paramètres des objectifs au format du serveur.","Quête"}},
            {"npc",new[]{"PNJ associé","Identifiant du PNJ concerné.","Quête"}},
            {"monster",new[]{"Monstres concernés","Identifiants et quantités au format du serveur.","Quête"}},
            {"item",new[]{"Objets concernés","Identifiants et quantités au format du serveur.","Quête"}},
            {"validationType",new[]{"Mode de validation","Identifiant du mode de validation de cette étape.","Quête"}},
            {"xp",new[]{"Expérience offerte","Récompense d'expérience.","Récompenses"}},
            {"kamas",new[]{"Kamas offerts","Récompense en kamas.","Récompenses"}},
            {"deleteFinish",new[]{"Retirer la quête une fois terminée","Option de suppression utilisée par le serveur.","Quête"}},
            {"map",new[]{"Carte","Identifiant de la carte concernée.","Général"}},
            {"maps",new[]{"Cartes concernées","Identifiants des cartes dans le format du serveur.","Mécanisme"}},
            {"doorsEnable",new[]{"Portes à activer","Identifiants des portes ouvertes par le mécanisme.","Mécanisme"}},
            {"doorsDisable",new[]{"Portes à désactiver","Identifiants des portes fermées par le mécanisme.","Mécanisme"}},
            {"cellsEnable",new[]{"Cellules à activer","Cellules rendues accessibles par le mécanisme.","Mécanisme"}},
            {"cellsDisable",new[]{"Cellules à désactiver","Cellules rendues inaccessibles par le mécanisme.","Mécanisme"}},
            {"requiredCells",new[]{"Cellules requises","Cellules à occuper pour activer le mécanisme.","Mécanisme"}},
            {"button",new[]{"Bouton d'activation","Cellule ou identifiant du bouton ; -1 pour la valeur par défaut.","Mécanisme"}},
            {"time",new[]{"Durée du mécanisme","Durée stockée par le serveur.","Mécanisme"}},
            {"key",new[]{"Clef du donjon","Identifiant du modèle de clef requis.","Donjon"}},
            {"donjon",new[]{"Paramètres du donjon","Informations utilisées pour l'entrée dans le donjon.","Donjon"}}
        };
        internal static FieldSpec Get(ServerResourceKind kind, DataColumn column, Dictionary<string,string> names)
        {
            string name=column.ColumnName;
            if(kind==ServerResourceKind.Maps) return MapField(column);
            if ((int)kind < (int)ServerResourceKind.Drops) return null;
            if(name.Equals("responses",StringComparison.OrdinalIgnoreCase) || name.Equals("etapes",StringComparison.OrdinalIgnoreCase))
            {
                string title=name.Equals("responses",StringComparison.OrdinalIgnoreCase)?"Réponses proposées":"Étapes de la quête";
                return new FieldSpec(title,"Ajoutez les fiches liées et conservez leur ordre.","Dialogue et étapes") { Edit=(owner,data)=>DelimitedListForm.Edit(owner,title,"Une ligne par fiche liée ; les identifiants particuliers du serveur restent accessibles.",data,';','~',new[]{new FieldSpec("Identifiant de la fiche") { Suggestions=names,DefaultText="1",Validate=value=>{int id;if(!int.TryParse(value,out id))throw new FormatException("Saisissez un identifiant entier.");} }},record=>{string label;return names!=null&&names.TryGetValue(record,out label)?label+" · #"+record:"Fiche #"+record;}),Summary=data=>data==""||data=="-1"?"Aucune fiche liée":data.Split(';').Count(value=>value!="")+" fiche(s) liée(s)" };
            }
            if(kind==ServerResourceKind.InteractiveDoors && (name.StartsWith("doors",StringComparison.OrdinalIgnoreCase)||name.StartsWith("cells",StringComparison.OrdinalIgnoreCase)||name.Equals("requiredCells",StringComparison.OrdinalIgnoreCase)))
            {
                string[] doorInfo=Labels[name];
                return new FieldSpec(doorInfo[0],"Ajoutez les couples carte / cellule concernés.","Mécanisme") { Edit=(owner,data)=>DelimitedListForm.Edit(owner,doorInfo[0],"Une ligne par emplacement : carte et cellule.",data,';',':',new[]{FieldSpec.Number("Carte"),FieldSpec.Number("Cellule")},record=>{string[] parts=record.Split(':');return "Carte "+parts[0]+(parts.Length>1?" · cellule "+parts[1]:"");},2),Summary=data=>data==""?"Aucun emplacement":data.Split(';').Count(value=>value!="")+" emplacement(s)" };
            }
            if(kind==ServerResourceKind.ObjectActions && name.Equals("template",StringComparison.OrdinalIgnoreCase))return new FieldSpec("Modèle d'objet","Objet dont l'utilisation déclenche cette action."){Suggestions=names};
            if(kind==ServerResourceKind.QuestSteps && name.Equals("objectif",StringComparison.OrdinalIgnoreCase))return new FieldSpec("Objectif associé","Choisissez l'objectif ou saisissez son identifiant.","Quête"){Suggestions=names};
            if (name.StartsWith("percentGrade",StringComparison.OrdinalIgnoreCase)) return new FieldSpec("Probabilité au grade "+name.Substring("percentGrade".Length)+" (%)","Entre 0 et 100 ; les décimales sont acceptées.","Probabilités");
            if (name.Equals("type",StringComparison.OrdinalIgnoreCase)) return new FieldSpec(kind==ServerResourceKind.NpcResponses?"Type d'action de la réponse":kind==ServerResourceKind.QuestSteps?"Type de l'étape":"Type d'action","Identifiant utilisé par votre émulateur.","Action");
            if (name.Equals("cond",StringComparison.OrdinalIgnoreCase) || name.Equals("conditions",StringComparison.OrdinalIgnoreCase) || name.Equals("condition",StringComparison.OrdinalIgnoreCase)) return new FieldSpec("Conditions","Expression de conditions utilisée par le serveur.","Conditions") { Edit=ConditionExpressionForm.Edit,Summary=data=>data==""?"Aucune condition":data };
            string[] info;
            if(!Labels.TryGetValue(name,out info))return null;
            return new FieldSpec(info[0],info[1],info[2],column.DataType==typeof(string) && (name.Equals("description",StringComparison.OrdinalIgnoreCase)||name.Equals("args",StringComparison.OrdinalIgnoreCase)||name.Equals("params",StringComparison.OrdinalIgnoreCase)||name.Equals("ifFalse",StringComparison.OrdinalIgnoreCase))) { Suggestions = kind==ServerResourceKind.Drops && name.Equals("objectId",StringComparison.OrdinalIgnoreCase) || kind==ServerResourceKind.Dungeons && name.Equals("key",StringComparison.OrdinalIgnoreCase)?names:null,
                Options = name.Equals("deleteFinish",StringComparison.OrdinalIgnoreCase)?new[]{new EditorOption("0","Non"),new EditorOption("1","Oui")}:null };
        }
        private static FieldSpec MapField(DataColumn column)
        {
            switch(column.ColumnName.ToLowerInvariant())
            {
                case "date":return new FieldSpec("Version de la carte","Version utilisée dans le nom du fichier client.");
                case "width":return new FieldSpec("Largeur","2 à 100 ; -1 conserve la valeur spéciale du serveur.");
                case "height":case "heigth":return new FieldSpec("Hauteur","2 à 100 ; -1 conserve la valeur spéciale du serveur.");
                case "mappos":return new FieldSpec("Position dans le monde","Coordonnées et paramètres, dans le format du serveur.");
                case "numgroup":return new FieldSpec("Nombre de groupes de monstres","Nombre maximal de groupes sur la carte.","Monstres");
                case "minsize":return new FieldSpec("Taille minimale des groupes","Nombre minimal de monstres par groupe.","Monstres");
                case "maxsize":return new FieldSpec("Taille maximale des groupes","Nombre maximal de monstres par groupe.","Monstres");
                case "fixsize":return new FieldSpec("Taille fixe des groupes","Paramètre de taille fixe de votre émulateur.","Monstres");
                case "monsters":return new FieldSpec("Monstres disponibles","Liste et niveaux possibles, dans le format du serveur.","Monstres",true);
                case "places":return new FieldSpec("Cellules de départ des combats","Données encodées des deux équipes. Utilisez l'éditeur graphique pour les placer.","Combat",true);
                case "maxplaces":return new FieldSpec("Places de combat autorisées","Limite conservée par le serveur.","Combat");
                case "capabilities":return new FieldSpec("Autorisations de la carte","Combinaison numérique des options. L'éditeur graphique permet de les modifier séparément.","Autorisations");
                case "forbidden":return new FieldSpec("Restrictions du serveur","Restrictions supplémentaires, dans le format de votre émulateur.","Autorisations",column.DataType==typeof(string));
                case "key":return new FieldSpec("Clef de chiffrement","Clef utilisée pour décoder les données client. Vide si elles sont en clair.","Données client",true);
                case "mapdata":return new FieldSpec("Données graphiques encodées","Utilisez l'éditeur graphique ou le convertisseur du gestionnaire pour créer ces données.","Données client",true);
                case "cells":return new FieldSpec("Données des cellules","Données complémentaires des cellules au format du serveur.","Données client",true);
                case "cases":return new FieldSpec("Paramètres complémentaires des cellules","Paramètres propres à votre serveur.","Avancé",column.DataType==typeof(string));
            }
            return null;
        }
    }
}
