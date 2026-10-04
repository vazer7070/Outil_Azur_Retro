using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Métiers connus (<c>JS</c>/<c>JX</c>) : nom et niveau. Volet provisoire repris de l'ancien tiroir ;
    /// la fiche métiers et l'artisanat appartiennent au lot de l'artisanat.
    /// </summary>
    public sealed class JobsPanel : GamePanel
    {
        private ListView jobs;
        private CharacterClass character;

        public override string Title => "Métiers";

        protected override Control CreateView()
        {
            var page = Page();
            jobs = MakeList(9, "Métier", "Niveau");
            jobs.Columns[0].Width = 236; jobs.Columns[1].Width = 94;
            page.Controls.Add(jobs);
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            character = game.character;
            if (character != null) character.Jobs_Refresh += OnJobsChanged;
        }
        protected override void OnUnbind(GameClass game)
        {
            if (character != null) character.Jobs_Refresh -= OnJobsChanged;
            character = null;
        }
        private void OnJobsChanged() => OnUi(RefreshView);

        public override void RefreshView()
        {
            if (Game == null || jobs == null) return;
            jobs.BeginUpdate(); jobs.Items.Clear();
            foreach (var job in Game.character.GetJobsSnapshot())
            {
                var row = jobs.Items.Add(string.IsNullOrEmpty(job.name) ? "Métier #" + job.ID : job.name);
                row.SubItems.Add(job.Level.ToString());
            }
            jobs.EndUpdate();
        }
    }
}
