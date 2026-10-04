using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Dialogue avec un PNJ (<c>dofus.aks.Dialog</c>) : s'ouvre sur <c>DCK</c>/<c>DQ</c>, se ferme sur <c>DV</c>.
    /// Les réponses envoient <c>DR&lt;question&gt;|&lt;réponse&gt;</c>, Quitter (ou ×/Échap) envoie <c>DV</c>.
    /// </summary>
    public sealed class DialoguePanel : GamePanel
    {
        private const string NoDialogText = "Cliquez sur un personnage non joueur de la carte pour lui parler.\nLes textes des questions et réponses ne sont pas exportés dans BotNPCs : seuls leurs numéros sont affichés.";
        private Label dialogTitle, dialogQuestion, dialogStatus;
        private FlowLayoutPanel dialogAnswers;
        private Control dialogLeave;
        private string dialogAnswersKey = string.Empty;
        private bool wasOpen;

        public override string Title => "Dialogue";
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Npc?.IsOpen == true;

        protected override Control CreateView()
        {
            var page = Page();
            dialogTitle = MakeLabel("Aucun dialogue en cours", 11, true);
            dialogTitle.Dock = DockStyle.Top; dialogTitle.Height = 28; dialogTitle.AutoEllipsis = true;
            dialogQuestion = MakeLabel(NoDialogText, 9);
            dialogQuestion.Dock = DockStyle.Top; dialogQuestion.Height = 96; dialogQuestion.ForeColor = BotUi.Muted;
            dialogAnswers = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = BotUi.Paper, Padding = new Padding(0, 4, 0, 0) };
            dialogStatus = MakeStatus(string.Empty);
            dialogLeave = MakeButton("Quitter", async (s, e) => await LeaveDialog(), false, 110);
            page.Controls.Add(dialogAnswers); page.Controls.Add(dialogQuestion); page.Controls.Add(dialogTitle);
            page.Controls.Add(dialogStatus); page.Controls.Add(BotUi.Actions(dialogLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { game.Interactions.Npc.Changed += OnServerChanged; }
        protected override void OnUnbind(GameClass game) { if (game.Interactions != null) game.Interactions.Npc.Changed -= OnServerChanged; }

        private void OnServerChanged() => OnUi(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });

        public override void RefreshView()
        {
            if (Game == null || dialogTitle == null) return;
            NpcDialog npc = Game.Interactions.Npc;
            dialogTitle.Text = npc.IsOpen ? "Dialogue avec " + npc.NpcName : "Aucun dialogue en cours";
            dialogQuestion.Text = !npc.IsOpen ? NoDialogText
                : npc.QuestionId < 0 ? "En attente de la question du serveur…"
                : "Question n° " + npc.QuestionId + (npc.Parameters.Length > 0 ? " · paramètres : " + string.Join(", ", npc.Parameters) : string.Empty)
                    + (npc.IsPaused ? "\nLe serveur demande d'attendre." : string.Empty)
                    + "\nLes textes ne sont pas exportés dans BotNPCs : seuls les numéros sont affichés.";
            string key = npc.IsOpen ? npc.QuestionId + ":" + string.Join(",", npc.AnswerIds) : string.Empty;
            if (key != dialogAnswersKey)
            {
                dialogAnswersKey = key;
                dialogAnswers.SuspendLayout();
                foreach (Control old in dialogAnswers.Controls.Cast<Control>().ToArray()) { dialogAnswers.Controls.Remove(old); old.Dispose(); }
                foreach (int id in npc.AnswerIds)
                {
                    int answer = id;
                    var button = MakeButton("Réponse n° " + answer, async (s, e) => await AnswerDialog(answer), true, 300);
                    button.Margin = new Padding(0, 0, 0, 6); button.Tag = answer; dialogAnswers.Controls.Add(button);
                }
                dialogAnswers.ResumeLayout();
            }
            dialogStatus.Text = npc.LastMessage;
            bool connected = Connected;
            dialogLeave.Enabled = connected && npc.IsOpen;
            foreach (Control answer in dialogAnswers.Controls) answer.Enabled = connected && npc.IsOpen && !npc.IsPaused;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = LeaveDialog();
            return false;
        }

        private Task AnswerDialog(int answerId) => ReportAsync(() => Game.Interactions.Npc.AnswerAsync(answerId));
        private Task LeaveDialog() => ReportAsync(() => Game.Interactions.Npc.LeaveAsync());
    }
}
