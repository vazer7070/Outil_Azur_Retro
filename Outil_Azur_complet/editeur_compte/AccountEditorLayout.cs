using System.Windows.Forms;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet.editeur_compte
{
    public partial class editeurcompte
    {
        private BoundEditorLayout editorLayout;
        private void BuildEditorLayout()
        {
            editorLayout = new BoundEditorLayout(this, "Éditeur de comptes", "Modifiez les informations d'un compte, puis enregistrez les changements en attente.", textBox1, listBox1);
            editorLayout.Field("Compte",iTalk_TextBox_Small1,"Identifiant du compte","Attribué par le serveur ; lecture seule.");
            editorLayout.Field("Compte",iTalk_TextBox_Small2,"Nom de connexion","Nom utilisé pour se connecter ; lecture seule.");
            editorLayout.Field("Compte",iTalk_TextBox_Small3,"Pseudonyme","Nom public du compte.");
            editorLayout.Field("Compte",iTalk_TextBox_Small9,"Points du compte","Solde entier positif ou nul.");
            editorLayout.Field("Sécurité",iTalk_TextBox_Small4,"Mot de passe enregistré","Valeur attendue par le serveur, déjà hachée si nécessaire.");
            editorLayout.Field("Sécurité",iTalk_TextBox_Small10,"Dernière adresse IP","Adresse conservée par le serveur.");
            editorLayout.Field("Sécurité",iTalk_TextBox_Small6,"Question secrète","Question utilisée pour la récupération du compte.");
            editorLayout.Field("Sécurité",iTalk_TextBox_Small7,"Réponse secrète","Réponse attendue par le serveur.");
            editorLayout.Field("Statut",iTalk_TextBox_Small5,"Compte banni","Utilisez le bouton Bannir / Débannir pour changer ce statut.");
            editorLayout.Field("Statut",iTalk_TextBox_Small11,"Compte VIP","Utilisez le bouton Rendre VIP / Retirer VIP.");
            editorLayout.Field("Statut",iTalk_Label9,"Connexion","Un compte connecté doit être déconnecté avant les opérations de modération.");
            editorLayout.Field("Personnages",iTalk_ComboBox1,"Personnages associés","Sélectionnez un personnage, puis ouvrez sa fiche.");
            editorLayout.Action(iTalk_Button_22,"Enregistrer",140);
            editorLayout.Action(iTalk_Button_14,"Créer un compte",150);
            editorLayout.Action(iTalk_Button_21,"Ouvrir le personnage",185);
            editorLayout.Action(iTalk_Button_13,"Rendre VIP",130);
            editorLayout.Action(iTalk_Button_12,"Bannir le compte",155);
            editorLayout.Action(iTalk_Button_11,"Supprimer",120);
            editorLayout.MirrorStatus(iTalk_Label14);
        }
    }
    public partial class CreateForm
    {
        private void BuildEditorLayout()
        {
            var layout = new BoundEditorLayout(this,"Créer un compte","Remplissez les quatre champs et choisissez le format de mot de passe utilisé par votre serveur.");
            layout.Status.Text = "Le compte sera créé lorsque vous cliquerez sur Créer le compte.";
            layout.Field("Informations",iTalk_TextBox_Small4,"Nom de connexion","Au moins 5 caractères.");
            layout.Field("Informations",iTalk_TextBox_Small3,"Mot de passe","Au moins 8 caractères ; saisie masquée.");
            layout.Field("Informations",iTalk_TextBox_Small2,"Question secrète","Au moins 6 caractères.");
            layout.Field("Informations",iTalk_TextBox_Small1,"Réponse secrète","Au moins 6 caractères.");
            layout.Field("Mot de passe",radioButton1,"Texte en clair","À utiliser uniquement si votre serveur attend ce format.");
            layout.Field("Mot de passe",radioButton2,"Hachage MD5","Choisissez le même format que celui du serveur.");
            layout.Field("Mot de passe",radioButton3,"Hachage SHA-512","Choisissez le même format que celui du serveur.");
            layout.Action(iTalk_Button_21,"Créer le compte",180);
            layout.Action(iTalk_Button_11,"Annuler",120);
            foreach(var radio in new[]{radioButton1,radioButton2,radioButton3})radio.CheckedChanged+=(s,e)=>{if(!radio.Checked)return;foreach(var other in new[]{radioButton1,radioButton2,radioButton3})if(other!=radio)other.Checked=false;};
        }
    }
}
