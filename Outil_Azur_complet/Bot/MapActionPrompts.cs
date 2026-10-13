using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Boîtes des duels et messages des actions de carte, comme le client 1.34 : duel reçu → Oui / Non / Ignorer
    /// (<c>A_CHALENGE_YOU</c> ; Oui envoie <c>GA901</c>, Non <c>GA902</c>, Ignorer ignore le joueur pour la session puis
    /// <c>GA902</c>) ; duel proposé → information « Annuler » (<c>YOU_CHALENGE_B</c>, <c>GA902&lt;soi&gt;</c>) ; les deux boîtes se
    /// referment sans réponse sur <c>GA;901</c>/<c>GA;902</c>. Agression subie (<c>YOU_ARE_ATTAC</c>), refus <c>GA;903</c> et
    /// réponses « qui est » vont au bandeau. Lot X2 : demande en mariage (<c>GA;617</c>) → Oui / Non (<c>GA618</c> / <c>GA619</c>),
    /// fin de partie (<c>GO</c>) → « Rejouer ». Les événements arrivent sur le fil réseau et sont ramenés sur celui du tiroir.
    /// </summary>
    internal sealed class MapActionPrompts : IDisposable
    {
        internal const string IncomingDialog = "duel-recu";
        internal const string OutgoingDialog = "duel-propose";
        internal const string MarriageDialog = "mariage";
        internal const string GameOverDialog = "fin-de-partie";
        private readonly MapActions actions;
        private readonly PanelHost host;
        private readonly GameSession session;
        private bool disposed;

        /// <summary>
        /// « Rejouer » de la fin de partie (<c>kernel.changeServer(true)</c> du client) : comme « Changer de personnage » de la
        /// fenêtre de jeu, ouvre le choix du personnage du même compte et ferme la fenêtre. Remplaçable par un test.
        /// </summary>
        internal static Action<Form, AccountConfig> ReplayAction = OpenCharacterSelection;

        internal MapActionPrompts(MapActions actions, PanelHost host)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            actions.ChallengeAsked += OnChallengeAsked;
            actions.ChallengeSent += OnChallengeSent;
            actions.ChallengeClosed += OnChallengeClosed;
            actions.Notice += OnNotice;
            actions.WhoisReceived += OnWhois;
            actions.MarriageAsked += OnMarriageAsked;
            actions.MarriageClosed += OnMarriageClosed;
            session = host.Game?.Session;
            if (session != null) session.GameOver += OnGameOver;
        }

        /// <summary>Demande en mariage (<c>GA;617</c> adressé à soi) : Oui → <c>GA618&lt;ref&gt;</c>, Non → <c>GA619&lt;ref&gt;</c>.</summary>
        private void OnMarriageAsked(MarriageRequest request) => OnUi(async () =>
        {
            BotDialogResult answer = await BotDialogs.AskYesNoAsync(host, MapActionTexts.MarriageTitle, request.Text, MarriageDialog);
            // Refermée par le serveur, remplacée par une autre demande ou fin de session : plus rien à répondre.
            if (disposed || !ReferenceEquals(actions.PendingMarriage, request)) return;
            InteractionResult result;
            switch (answer)
            {
                case BotDialogResult.Yes: result = await actions.AcceptMarriageAsync(); break;
                case BotDialogResult.No: result = await actions.RefuseMarriageAsync(); break;
                default: return;
            }
            Report(result?.Message);
        });

        private void OnMarriageClosed(MarriageRequest request) => OnUi(() =>
        {
            BotDialogs.Dismiss(MarriageDialog);
            return Task.CompletedTask;
        });

        /// <summary>
        /// <c>GO</c> (mode héroïque) : la connexion est fermée juste après ; la boîte n'a qu'un bouton « Rejouer », comme
        /// l'interface <c>GameOver</c> du client (son illustration <c>gameover.swf</c> n'est pas exportée).
        /// </summary>
        private void OnGameOver() => OnUi(async () =>
        {
            BotDialogs.Dismiss(MarriageDialog);
            BotDialogResult answer = await BotDialogs.GameOverAsync(host, "Fin de partie",
                "Le serveur a mis fin à la partie de ce personnage (mode héroïque) et la connexion est fermée.", GameOverDialog);
            if (disposed || answer != BotDialogResult.Replay) return;
            Form window = host.IsDisposed ? null : host.FindForm();
            AccountConfig config = host.Account?.accountConfig;
            if (window == null || window.IsDisposed || config == null) return;
            ReplayAction?.Invoke(window, config);
        });

        private static void OpenCharacterSelection(Form window, AccountConfig config)
        {
            if (!(window is GameClientFullform)) return;
            new PersoSelection(config).Show();
            window.Close();
        }

        private void OnChallengeAsked(ChallengeInfo challenge) => OnUi(async () =>
        {
            BotDialogResult answer = await BotDialogs.AskYesNoIgnoreAsync(host, MapActionTexts.ChallengeTitle,
                MapActionTexts.ChallengeYou(challenge.ChallengerName), IncomingDialog);
            if (disposed) return;
            InteractionResult result;
            switch (answer)
            {
                case BotDialogResult.Yes: result = await actions.AcceptChallengeAsync(challenge.ChallengerId); break;
                case BotDialogResult.No: result = await actions.RefuseChallengeAsync(challenge.ChallengerId); break;
                case BotDialogResult.Ignore: result = await actions.IgnoreChallengerAsync(challenge.ChallengerId); break;
                default: return; // refermée par le serveur (GA;901, GA;902) ou par la fin de session
            }
            Report(result?.Message);
        });

        private void OnChallengeSent(ChallengeInfo challenge) => OnUi(async () =>
        {
            BotDialogResult answer = await BotDialogs.CancelAsync(host, MapActionTexts.ChallengeTitle,
                MapActionTexts.YouChallengeB(challenge.TargetName), OutgoingDialog);
            if (disposed || answer != BotDialogResult.Cancel) return;
            InteractionResult result = await actions.CancelChallengeAsync();
            Report(result?.Message);
        });

        private void OnChallengeClosed(ChallengeInfo challenge, bool accepted) => OnUi(() =>
        {
            BotDialogs.Dismiss(IncomingDialog);
            BotDialogs.Dismiss(OutgoingDialog);
            if (!accepted) Report("Duel avec " + challenge.OpponentName + " annulé.");
            return Task.CompletedTask;
        });

        private void OnNotice(MapActionNotice notice) => OnUi(() =>
        {
            // Les informations (A_CHALENGE_B, A_ATTACK_B…) restent dans le journal ; erreurs et texte centré vont au bandeau.
            if (notice.Kind != MapActionNoticeKind.Info) Report(notice.Text);
            return Task.CompletedTask;
        });

        private void OnWhois(WhoisInfo info) => OnUi(() => { Report(info?.Text); return Task.CompletedTask; });

        private void Report(string message)
        {
            if (!disposed && !string.IsNullOrEmpty(message) && !host.IsDisposed) host.Report(message);
        }

        private void OnUi(Func<Task> action)
        {
            if (disposed || host.IsDisposed) return;
            // Tiroir jamais affiché : pas de poignée, l'action serait perdue (.NET) ou exécutée sur le fil réseau (Mono).
            // Comme GamePanel.OnUi, on passe alors par la fenêtre de jeu.
            Control target = host;
            if (!target.IsHandleCreated)
            {
                Form form = host.FindForm();
                if (form != null && !form.IsDisposed && form.IsHandleCreated) target = form;
            }
            BotUi.OnUi(target, async () =>
            {
                if (disposed) return;
                try { await action(); }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    Report("Action impossible : " + error.Message);
                    host.Account?.Logger?.LogException("COMBATS", error);
                }
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            actions.ChallengeAsked -= OnChallengeAsked;
            actions.ChallengeSent -= OnChallengeSent;
            actions.ChallengeClosed -= OnChallengeClosed;
            actions.Notice -= OnNotice;
            actions.WhoisReceived -= OnWhois;
            actions.MarriageAsked -= OnMarriageAsked;
            actions.MarriageClosed -= OnMarriageClosed;
            if (session != null) session.GameOver -= OnGameOver;
            BotDialogs.Dismiss(IncomingDialog);
            BotDialogs.Dismiss(OutgoingDialog);
            BotDialogs.Dismiss(MarriageDialog);
        }
    }
}
