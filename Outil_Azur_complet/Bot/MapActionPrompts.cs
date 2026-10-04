using System;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Boîtes des duels et messages des actions de carte, comme le client 1.34 : duel reçu → Oui / Non / Ignorer
    /// (<c>A_CHALENGE_YOU</c> ; Oui envoie <c>GA901</c>, Non <c>GA902</c>, Ignorer ignore le joueur pour la session puis
    /// <c>GA902</c>) ; duel proposé → information « Annuler » (<c>YOU_CHALENGE_B</c>, <c>GA902&lt;soi&gt;</c>) ; les deux boîtes se
    /// referment sans réponse sur <c>GA;901</c>/<c>GA;902</c>. Agression subie (<c>YOU_ARE_ATTAC</c>), refus <c>GA;903</c> et
    /// réponses « qui est » vont au bandeau. Les événements arrivent sur le fil réseau et sont ramenés sur celui du tiroir.
    /// </summary>
    internal sealed class MapActionPrompts : IDisposable
    {
        internal const string IncomingDialog = "duel-recu";
        internal const string OutgoingDialog = "duel-propose";
        private readonly MapActions actions;
        private readonly PanelHost host;
        private bool disposed;

        internal MapActionPrompts(MapActions actions, PanelHost host)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            actions.ChallengeAsked += OnChallengeAsked;
            actions.ChallengeSent += OnChallengeSent;
            actions.ChallengeClosed += OnChallengeClosed;
            actions.Notice += OnNotice;
            actions.WhoisReceived += OnWhois;
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
            BotUi.OnUi(host, async () =>
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
            BotDialogs.Dismiss(IncomingDialog);
            BotDialogs.Dismiss(OutgoingDialog);
        }
    }
}
