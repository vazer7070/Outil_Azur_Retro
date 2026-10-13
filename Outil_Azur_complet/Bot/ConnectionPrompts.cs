using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Boîtes de la fenêtre de jeu liées au compte (lot X3) : nouveau niveau (<c>AN</c>, boîte <c>NEW_LEVEL</c> de
    /// <c>Account.onNewLevel</c>) et perte de connexion (<c>Aks.onClose</c> : titre <c>CONNECTION</c>, texte <c>DISCONNECT</c> suivi du
    /// dernier <c>M0</c> reçu). Après la boîte de perte de connexion, la fenêtre se ferme et l'écran de connexion s'ouvre avec les
    /// identifiants du compte. Une déconnexion demandée (Déconnexion, Changer de personnage) ou la fin de partie (<c>GO</c>, lot X2,
    /// sa propre boîte) n'ouvre pas cette boîte. Les événements arrivent sur le fil réseau et sont ramenés sur celui de la fenêtre.
    /// </summary>
    internal sealed class ConnectionPrompts : IDisposable
    {
        internal const string LevelDialog = "nouveau-niveau";
        internal const string LostDialog = "connexion-perdue";
        private readonly Form window;
        private readonly Accounts account;
        private readonly GameSession session;
        private readonly string levelDialog, lostDialog;
        private bool disposed;

        /// <summary>
        /// Retour après la boîte de perte de connexion : ouvre l'écran de connexion prérempli et ferme la fenêtre de jeu.
        /// Remplaçable par un test.
        /// </summary>
        internal static Action<Form, AccountConfig, ConnectionLoss> ReturnAction = OpenLogin;

        internal ConnectionPrompts(Form window, Accounts account)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            this.account = account ?? throw new ArgumentNullException(nameof(account));
            // Noms propres au compte : Dismiss ne doit pas refermer les boîtes de la fenêtre d'un autre compte.
            string suffix = "/" + (account.accountConfig?.Account ?? string.Empty);
            levelDialog = LevelDialog + suffix;
            lostDialog = LostDialog + suffix;
            account.ConnectionLost += OnConnectionLost;
            session = account.Game?.Session;
            if (session != null) session.LevelUp += OnLevelUp;
        }

        /// <summary>Un seul nouveau niveau à l'écran : plusieurs <c>AN</c> d'affilée (gros gain d'expérience) gardent le dernier.</summary>
        private void OnLevelUp(int level) => OnUi(async () =>
        {
            BotDialogs.Dismiss(levelDialog);
            await BotDialogs.InfoAsync(window, ConnectionTexts.InformationsTitle, ConnectionTexts.NewLevel(level), levelDialog);
        });

        private void OnConnectionLost(ConnectionLoss loss) => OnUi(async () =>
        {
            BotDialogs.Dismiss(levelDialog);
            BotDialogs.Dismiss(lostDialog);
            await BotDialogs.InfoAsync(window, loss.Title, loss.Text, lostDialog);
            // Fenêtre fermée pendant la boîte (par l'utilisateur ou un autre retour) : rien à rouvrir.
            if (disposed || window.IsDisposed || window.Disposing) return;
            AccountConfig config = account.accountConfig;
            if (config == null) return;
            ReturnAction?.Invoke(window, config, loss);
        });

        private static void OpenLogin(Form window, AccountConfig config, ConnectionLoss loss)
        {
            new LoginForm(config, LoginStatus(loss)).Show();
            window.Close();
        }

        /// <summary>Ligne d'état de l'écran de connexion après une perte de connexion.</summary>
        internal static string LoginStatus(ConnectionLoss loss) =>
            (loss?.Message == null ? ConnectionTexts.Disconnected : ConnectionTexts.Disconnected + " " + loss.Message.Text.Replace("\n", " "))
            + " Reconnectez-vous.";

        private void OnUi(Func<Task> action)
        {
            if (disposed || window.IsDisposed) return;
            BotUi.OnUi(window, async () =>
            {
                if (disposed) return;
                try { await action(); }
                catch (Exception error) { account.Logger?.LogException("CONNEXION", error); }
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            account.ConnectionLost -= OnConnectionLost;
            if (session != null) session.LevelUp -= OnLevelUp;
            BotDialogs.Dismiss(levelDialog);
            BotDialogs.Dismiss(lostDialog);
        }
    }
}
