using System;
using System.Drawing;
using System.Windows.Forms;
using Tool_BotProtocol.Game;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Volet du tiroir de la fenêtre de jeu (fiche, inventaire, dialogue, échange…). Chaque lot fonctionnel ajoute
    /// son propre fichier de volet et l'affiche avec <see cref="PanelHost.Show"/> sans modifier le formulaire principal.
    /// </summary>
    public interface IGamePanel : IDisposable
    {
        /// <summary>Titre affiché dans l'en-tête et l'onglet du tiroir.</summary>
        string Title { get; }
        /// <summary>Icône du client (image partagée de <c>ClientAssets</c>, jamais libérée par le volet) ou <c>null</c>.</summary>
        Image Icon { get; }
        /// <summary>Contenu du volet, inséré dans le tiroir à l'enregistrement.</summary>
        Control View { get; }
        /// <summary>Associe le volet à la session de jeu (ou <c>null</c>) ; le volet s'abonne ici aux événements dont il dépend.</summary>
        void Bind(GameClass game);
        /// <summary>Levé par le volet lorsqu'il demande à quitter le tiroir (fenêtre fermée par le serveur, bouton Fermer…).</summary>
        event EventHandler Closed;
    }
}
