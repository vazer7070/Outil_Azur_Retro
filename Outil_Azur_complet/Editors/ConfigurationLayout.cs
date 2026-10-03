using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Tools_protocol.Json;

namespace Outil_Azur_complet
{
    public partial class SettingsForm
    {
        private void BuildEditorLayout()
        {
            var layout=new Editors.BoundEditorLayout(this,"Configuration d'Azur","Gérez les connexions aux bases, les noms des tables et les versions de vos outils.");
            layout.Field("Bases de données",iTalk_TextBox_Small1,"Serveur SQL","Nom ou adresse du serveur MySQL.");
            layout.Field("Bases de données",iTalk_TextBox_Small2,"Utilisateur SQL","Compte utilisé pour accéder aux bases.");
            iTalk_TextBox_Small3.UseSystemPasswordChar=true;
            layout.Field("Bases de données",iTalk_TextBox_Small3,"Mot de passe SQL","Mot de passe du compte SQL ; saisie masquée.");
            layout.Field("Bases de données",iTalk_TextBox_Small4,"Base de données auth","Base contenant les comptes et les définitions du serveur.");
            layout.Field("Bases de données",iTalk_TextBox_Small5,"Base de données world","Base contenant les exemplaires d'objets.");
            layout.Field("Émulateur",iTalk_ComboBox1,"Émulateur","Choisissez le format de base utilisé par votre serveur.");
            layout.Field("Versions",iTalk_Label10,"Version du logiciel","Version d'Azur actuellement installée.");
            layout.Field("Versions",iTalk_Label11,"Version du protocole","Version de la bibliothèque de données.");
            layout.Field("Versions",iTalk_Label14,"Version du bot","Version d'AzurBot actuellement installée.");
            layout.Field("Versions",iTalk_Label16,"Version des éditeurs","Version de la bibliothèque des cartes et objets.");
            layout.Field("Versions",iTalk_LinkLabel1,"Journal des versions","Ouvrir le journal des modifications.");
            layout.Action(iTalk_Button_21,"Connecter les bases",190);layout.Action(iTalk_Button_23,"Configurer les tables",190);layout.Action(iTalk_Button_22,"Appliquer l'émulateur",190);layout.Action(iTalk_Button_12,"Vérifier les mises à jour",215);layout.Action(iTalk_Button_11,"Fermer",110);
            layout.Status.Text="Les changements de configuration peuvent nécessiter un redémarrage d'Azur.";
        }
    }
}
namespace Outil_Azur_complet.Annexes
{
    public partial class JsonModifier
    {
        private void BuildEditorLayout()
        {
            var layout=new Editors.BoundEditorLayout(this,"Noms des tables du serveur","Associez chaque fonction d'Azur au nom de sa table dans votre base de données.");
            var labels=new Dictionary<string,string>{
                {"comptes","Comptes"},{"animations","Animations"},{"areadata","Zones"},{"bandits","Bandits"},{"banip","Adresses IP bannies"},{"banque","Banques"},{"challenge","Challenges"},{"coffre","Coffres"},{"commandes","Commandes"},{"crafts","Recettes"},{"donjons","Donjons"},{"drops","Butins"},{"endfight","Actions de fin de combat"},{"extra","Monstres supplémentaires"},{"morphs","Transformations"},{"groupes","Groupes"},{"hdvs","Hôtels de vente"},{"maisons","Maisons"},{"Iporte","Portes et mécanismes"},{"interactions","Objets interactifs"},{"Template","Modèles d'objets"},{"panoplies","Panoplies"},{"metiers","Métiers"},{"cartes","Cartes"},{"groupe_monstre","Groupes de monstres fixes"},{"enclos","Enclos"},{"monstres","Monstres"},{"npc_questions","Questions des PNJ"},{"npc_reponse","Réponses et actions des PNJ"},{"npc_template","Définitions des PNJ"},{"npcs","Placements des PNJ"},{"objets_actions","Actions des objets"},{"paroli","Dialogues complémentaires"},{"familiers","Familiers"},{"perso","Personnages"},{"prismes","Prismes"},{"quete","Quêtes"},{"quete_etape","Étapes des quêtes"},{"quete_objectif","Objectifs des quêtes"},{"rss","Messages RSS"},{"runes","Runes"},{"schema_fight","Schémas des combats"},{"cellule","Cellules scriptées"},{"serveurs","Serveurs"},{"sort","Sorts"},{"subarea_data","Sous-zones"},{"titres","Titres"},{"tuto","Tutoriels"},{"zaapi","Zaapis"},{"zaaps","Zaaps"},{"items","Exemplaires d'objets"},{"drops_world","Butins des personnages"},{"personnages","Instances des personnages"},{"gifts","Cadeaux"}
            };
            iTalk_ComboBox2.FormattingEnabled=true;iTalk_ComboBox2.Format+=(s,e)=>{string label;if(labels.TryGetValue(System.Convert.ToString(e.ListItem),out label))e.Value=label;};
            layout.Field("Correspondances",iTalk_ComboBox1,"Base concernée","Les noms auth et world correspondent aux deux connexions SQL.");
            layout.Field("Correspondances",iTalk_ComboBox2,"Fonction du logiciel","Sélectionnez la fonction dont vous souhaitez modifier la table.");
            layout.Field("Correspondances",iTalk_TextBox_Small1,"Nom de la table SQL","Nom réel de la table, sans requête SQL ni nom de base.");
            layout.Action(iTalk_Button_21,"Enregistrer les tables",205);layout.Action(iTalk_Button_11,"Fermer",120);
            layout.Status.Text="Les modifications restent en attente jusqu'à l'enregistrement.";
            iTalk_TextBox_Small1.TextChanged+=(s,e)=>{if(!_loadingSelection)layout.Status.Text="Nom modifié. Enregistrer applique les correspondances et redémarre Azur.";};
            FormClosing+=(s,e)=>{SaveCurrentField();if((_authChanges.Count>0||_worldChanges.Count>0)&&MessageBox.Show(this,"Abandonner les noms de tables modifiés ?","Modifications en attente",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)e.Cancel=true;};
        }
    }
}
