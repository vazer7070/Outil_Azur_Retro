using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet.editeur_perso
{
    public partial class editeur_perso
    {
        private BoundEditorLayout editorLayout;
        private void BuildEditorLayout()
        {
            editorLayout = new BoundEditorLayout(this,"Éditeur de personnages","Sélectionnez un personnage déconnecté. Ses modifications seront enregistrées ensemble.",textBox1,listBox1);
            editorLayout.Field("Identité",iTalk_TextBox_Small4,"Identifiant","Attribué par le serveur ; lecture seule.");
            editorLayout.Field("Identité",iTalk_TextBox_Small5,"Nom du personnage","Entre 1 et 50 caractères.");
            editorLayout.Field("Identité",iTalk_TextBox_Small6,"Compte propriétaire","Identifiant du compte qui possède le personnage.");
            editorLayout.Field("Identité",iTalk_TextBox_Small11,"Classe","Choisissez la classe du personnage.",_classMapping.Values.ToArray());
            editorLayout.Field("Identité",iTalk_TextBox_Small10,"Sexe","Apparence du personnage.",new[]{"Mâle=Masculin","Femelle=Féminin"});
            editorLayout.Field("Identité",iTalk_TextBox_Small7,"Rôle","Droits du personnage sur le serveur.",Enumerable.Range(0,4).Select(id=>GetGradeName(id)).ToArray());
            editorLayout.Field("Progression",iTalk_TextBox_Small8,"Niveau","Entier supérieur à zéro.");
            editorLayout.Field("Progression",iTalk_TextBox_Small9,"Expérience","Quantité d'expérience totale.");
            editorLayout.Field("Progression",iTalk_TextBox_Small12,"Kamas","Monnaie du personnage.");
            editorLayout.Field("Progression",iTalk_TextBox_Small13,"Points de caractéristiques","Capital disponible à répartir.");
            editorLayout.Field("Progression",iTalk_TextBox_Small14,"Énergie","Réserve d'énergie du personnage.");
            editorLayout.Field("Caractéristiques",iTalk_TextBox_Small32,"Vitalité","Points de vitalité de base.");
            editorLayout.Field("Caractéristiques",iTalk_TextBox_Small31,"Force","Points de force de base.");
            editorLayout.Field("Caractéristiques",iTalk_TextBox_Small30,"Sagesse","Points de sagesse de base.");
            editorLayout.Field("Caractéristiques",iTalk_TextBox_Small27,"Intelligence","Points d'intelligence de base.");
            editorLayout.Field("Caractéristiques",iTalk_TextBox_Small28,"Chance","Points de chance de base.");
            editorLayout.Field("Caractéristiques",iTalk_TextBox_Small29,"Agilité","Points d'agilité de base.");
            editorLayout.Field("Position",iTalk_TextBox_Small18,"Carte actuelle","Identifiant de la carte.");
            editorLayout.Field("Position",iTalk_TextBox_Small19,"Cellule actuelle","Numéro de la cellule sur la carte.");
            editorLayout.Field("Position",iTalk_TextBox_Small20,"Point de sauvegarde","Carte,cellule ; par exemple 100,42.");
            editorLayout.Field("Position",iTalk_TextBox_Small16,"En prison","État géré par le serveur ; lecture seule.");
            editorLayout.Field("Apparence",iTalk_TextBox_Small25,"Apparence graphique","Identifiant du modèle graphique.");
            editorLayout.Field("Apparence",iTalk_TextBox_Small17,"Taille (%)","100 correspond à la taille normale.");
            editorLayout.ColorField("Apparence",iTalk_TextBox_Small2,"Première couleur");
            editorLayout.ColorField("Apparence",iTalk_TextBox_Small1,"Deuxième couleur");
            editorLayout.ColorField("Apparence",iTalk_TextBox_Small3,"Troisième couleur");
            editorLayout.Field("Social",iTalk_TextBox_Small24,"Alignement","Choisissez l'alignement.",_alignMapping.Values.ToArray());
            editorLayout.Field("Social",iTalk_TextBox_Small21,"Honneur","Points d'honneur.");
            editorLayout.Field("Social",iTalk_TextBox_Small22,"Déshonneur","Points de déshonneur.");
            editorLayout.Field("Social",iTalk_TextBox_Small23,"Conjoint","Identifiant du conjoint ; 0 signifie aucun conjoint.");
            editorLayout.Field("Contenu",iTalk_ComboBox1,"Inventaire","Ouvrez l'inventaire pour ajouter, retirer ou modifier un objet.");
            editorLayout.Field("Contenu",iTalk_ComboBox2,"Sorts appris","Ouvrez Sorts pour modifier les niveaux et les positions.");
            editorLayout.Field("Contenu",iTalk_ComboBox3,"Métiers appris","Ouvrez Métiers pour modifier l'expérience.");
            editorLayout.Action(iTalk_Button_31,"Enregistrer",140);
            editorLayout.Action(iTalk_Button_21,"Inventaire",130);
            editorLayout.Action(iTalk_Button_22,"Sorts",110);
            editorLayout.Action(iTalk_Button_23,"Métiers",110);
            editorLayout.Action(iTalk_Button_11,"Bannir le personnage",185);
            editorLayout.Action(iTalk_Button_12,"Supprimer",120);
        }
    }
}
