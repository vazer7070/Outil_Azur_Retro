using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Spells;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Liste des sorts appris (<c>SL</c>) et amélioration manuelle <c>SB&lt;id&gt;</c>, confirmée par <c>SUK</c> ou refusée par <c>SUE</c>.
    /// Volet provisoire repris de l'ancien tiroir ; la fiche complète du client appartient au lot des fiches.
    /// </summary>
    public sealed class SpellsPanel : GamePanel
    {
        private ListView spells;
        private Label spellHelp;
        private Control upgradeSpell;
        private bool upgradingSpell;
        private CharacterClass character;

        public override string Title => "Sorts";
        public override Image Icon => ClientAssets.Icon("icone-sorts", 24);

        protected override Control CreateView()
        {
            var page = Page();
            spells = MakeList(9, "Sort", "Niveau");
            spells.Columns[0].Width = 236; spells.Columns[1].Width = 94;
            page.Controls.Add(spells);
            spellHelp = MakeStatus("Sélectionnez un sort à améliorer."); spellHelp.Height = 66;
            upgradeSpell = MakeButton("Améliorer de 1 niveau", async (s, e) => await UpgradeSpell(), true, 210);
            page.Controls.Add(spellHelp); page.Controls.Add(BotUi.Actions(upgradeSpell));
            spells.SelectedIndexChanged += (s, e) => UpdateSelection();
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            character = game.character;
            if (character != null) character.Spells_Refresh += OnSpellsChanged;
        }
        protected override void OnUnbind(GameClass game)
        {
            if (character != null) character.Spells_Refresh -= OnSpellsChanged;
            character = null;
        }
        private void OnSpellsChanged() => OnUi(RefreshView);

        public override void RefreshView()
        {
            if (Game == null || spells == null) return;
            short selected = spells.SelectedItems.Count == 0 ? (short)-1 : (short)spells.SelectedItems[0].Tag;
            spells.BeginUpdate(); spells.Items.Clear();
            foreach (var spell in Game.character.Spells.OrderBy(x => x.Value.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = spells.Items.Add(string.IsNullOrEmpty(spell.Value.Name) ? "Sort #" + spell.Key : spell.Value.Name);
                row.SubItems.Add(spell.Value.Level.ToString()); row.Tag = spell.Key;
                if (spell.Key == selected) row.Selected = true;
            }
            spells.EndUpdate();
            UpdateSelection();
        }

        /// <summary>Sélectionne la ligne d'un sort (clic droit sur un raccourci du bandeau).</summary>
        public void SelectSpell(short id)
        {
            if (spells == null) return;
            foreach (ListViewItem row in spells.Items)
                if ((short)row.Tag == id) { row.Selected = true; row.EnsureVisible(); break; }
        }

        private void UpdateSelection()
        {
            if (Game == null || upgradeSpell == null) return;
            upgradeSpell.Enabled = false; int points = Game.character.SpellPoints; Spell spell;
            if (spells.SelectedItems.Count == 0 || !Game.character.Spells.TryGetValue((short)spells.SelectedItems[0].Tag, out spell))
            { spellHelp.Text = points + " point(s) de sort disponible(s). Sélectionnez un sort à améliorer."; return; }
            if (!spell.HasMetadata) { spellHelp.Text = "Les données de ce sort sont absentes. Exportez les sorts depuis le parseur."; return; }
            if (spell.Level >= 6) { spellHelp.Text = "Ce sort est au niveau maximal (6)."; return; }
            spellHelp.Text = points + " point(s) disponible(s). Niveau suivant : " + (spell.Level + 1) + ", coût : " + spell.Level + " point(s). Le serveur confirme l’amélioration.";
            AccountStates state = Account?.AccountStates ?? AccountStates.DISCONNECTED;
            upgradeSpell.Enabled = !upgradingSpell && Connected && points >= spell.Level
                && (state == AccountStates.CONNECTED_INACTIVE || state == AccountStates.REGENERATION);
        }

        private async Task UpgradeSpell()
        {
            if (!upgradeSpell.Enabled || spells.SelectedItems.Count == 0 || Account?.Connexion == null) return;
            short id = (short)spells.SelectedItems[0].Tag; upgradingSpell = true; UpdateSelection();
            try { await Account.Connexion.SendPacket("SB" + id, true); await Task.Delay(500); }
            catch (Exception error) { Account?.Logger?.LogError("INTERFACE", error.Message); }
            finally { upgradingSpell = false; if (!IsDisposed) UpdateSelection(); }
        }
    }
}
