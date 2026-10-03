using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Outil_Azur_complet
{
    public partial class Menu
    {
        private void BuildEditorMenuLayout()
        {
            var layout=new Editors.BoundEditorLayout(this,"Atelier Azur","Choisissez un outil par domaine. Les éditeurs partagent les mêmes fiches, aides et actions d'enregistrement.");
            string[][] modules={
                new[]{"Administration","Comptes","Pseudonyme, sécurité, VIP, bannissement et personnages associés.","Éditeur de compte"},
                new[]{"Administration","Personnages","Identité, progression, caractéristiques, position et apparence.","Éditeur de personnage"},
                new[]{"Objets","Création et inventaires","Créer un objet ; ajouter, retirer ou modifier ses exemplaires.","Éditeur d'objets"},
                new[]{"Objets","Objets du client","Ouvrir un SWF, modifier les fiches et enregistrer une copie.","Objets du client"},
                new[]{"Objets","Modèles d'objets","Effets, armes, conditions, commerce et panoplie.","Éditeur de modèles d'objets"},
                new[]{"Objets","Panoplies","Composition et bonus selon le nombre d'objets équipés.","Éditeur de panoplies"},
                new[]{"Objets","Recettes","Objet fabriqué, ingrédients et quantités nécessaires.","Éditeur de recettes"},
                new[]{"Objets","Actions des objets","Actions déclenchées par l'utilisation des modèles d'objets.","Actions des objets"},
                new[]{"Combat et métiers","Sorts","Six niveaux, lancement, effets, zones et conditions.","Éditeur de sorts"},
                new[]{"Combat et métiers","Monstres","Grades, caractéristiques, récompenses et sorts.","Éditeur de monstres"},
                new[]{"Combat et métiers","Butins","Objets obtenus, probabilités par grade et prospection.","Éditeur de butins"},
                new[]{"Combat et métiers","Métiers","Outils, compétences et fabrications autorisées.","Éditeur de métiers"},
                new[]{"PNJ et quêtes","Définitions des PNJ","Apparence, dialogue initial, ventes et interactions.","Éditeur de PNJ"},
                new[]{"PNJ et quêtes","Questions des PNJ","Questions, réponses proposées et conditions du dialogue.","Questions des PNJ"},
                new[]{"PNJ et quêtes","Réponses et actions","Réponses et actions associées aux dialogues.","Réponses et actions des PNJ"},
                new[]{"PNJ et quêtes","Quêtes","Étapes, objectifs, PNJ, actions et conditions.","Éditeur de quêtes"},
                new[]{"PNJ et quêtes","Étapes des quêtes","Objectifs à accomplir et modes de validation.","Étapes des quêtes"},
                new[]{"PNJ et quêtes","Objectifs et récompenses","Descriptions, expérience, kamas, objets et actions.","Objectifs et récompenses des quêtes"},
                new[]{"Cartes","Éditeur graphique","Canevas, tuiles, cellules, calques et exports AME / SWF / SQL.","Éditeur de maps"},
                new[]{"Cartes","Données serveur des cartes","Toutes les propriétés stockées dans la table des cartes.","Données serveur des cartes"},
                new[]{"Cartes","Téléportations","Cellules, événements et destinations des déplacements.","Téléportations des cartes"},
                new[]{"Cartes","Fins de combat","Conditions et actions appliquées à la fin des combats.","Actions de fin de combat"},
                new[]{"Cartes","Entrées de donjons","Carte, PNJ, clef et informations d'entrée.","Entrées de donjons"},
                new[]{"Cartes","Portes et mécanismes","Emplacements activés, cellules requises et bouton.","Portes et mécanismes"},
                new[]{"Placements","PNJ sur les cartes","Carte, cellule, direction et déplacement autorisé.","Placements des PNJ"},
                new[]{"Placements","Groupes de monstres","Composition, niveaux, salles et réapparition.","Groupes de monstres fixes"},
                new[]{"Placements","Enclos","Propriété, montures et objets d'élevage.","Éditeur d'enclos"},
                new[]{"Placements","Zaaps","Cartes et cellules des zaaps.","Éditeur de zaaps"},
                new[]{"Ressources","Objets interactifs","Durées, réapparition et passage sur la cellule.","Éditeur de ressources"},
                new[]{"Ressources","Rechercher","Consulter les objets, panoplies, sorts, monstres et butins.","Outil de recherche"},
                new[]{"Ressources","Gestionnaire","Exports XML / SQL, conversion de cartes, images et journaux.","Gestionnaire"},
                new[]{"AzurBot","Client AzurBot","Ouvrir le client bot et ses outils.","AzurBot"}
            };
            foreach(var module in modules)
            {
                layout.Sheet(module[0],2);
                string target=module[3];
                layout.Notice(module[0],module[1],module[2],"Ouvrir",()=>{iTalk_ComboBox1.SelectedItem=target;iTalk_Button_11_Click(this,EventArgs.Empty);});
            }
            layout.Action("Configurer Azur",()=>iTalk_Button_13_Click(this,EventArgs.Empty),true,185);
            layout.Action("Fermer",Close,width:120);
            Action status=()=>layout.Status.Text=InitializeForm.NoDB?"Mode local · Configurez les connexions pour accéder aux données du serveur.":"Bases connectées · "+_formFactories.Count+" outils disponibles.";
            Activated+=(s,e)=>status();status();
        }
    }
}
