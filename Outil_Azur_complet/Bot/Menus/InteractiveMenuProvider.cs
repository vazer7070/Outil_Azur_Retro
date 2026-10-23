using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Menu des objets interactifs de la carte, comme <c>onObjectRelease</c> du client 1.34 : nom de l'objet en tête, puis une
    /// entrée par compétence (<c>IO.d[id].sk</c>) dont l'état vient du critère de la compétence (<see cref="SkillState"/>) :
    /// cachée (« X »), grisée ou active (« V »). Les paramètres du critère dépendent du type de l'objet :
    /// ressources, ateliers, zaap, fontaine, marmite, zaapi, liste des artisans, levier et statue (types 1, 2, 3, 4, 7, 10, 12,
    /// 14, 15) : J = la compétence appartient au métier de l'outil équipé, N = niveau ≤ 5 ; porte (5) : J, puis propriétaire,
    /// à vendre et verrouillée ; coffre (6) : J, chez soi, S vrai, verrouillé (données des maisons de <c>hP</c>/<c>hL</c> par
    /// <see cref="HouseDoorMenuProvider"/>, lot F8 ; faux sans elles) ; enclos (13) : J, enclos de la guilde, à vendre, accessible,
    /// N = public (<see cref="MountParkInfo"/>). Une porte connue titre « &lt;objet&gt; &lt;maison&gt; » et affiche « Chez moi ! » / « Chez X » / « Maison abandonnée ».
    /// Un autre type n'a pas de menu : le clic reste un déplacement. Maj + clic utilise la première compétence active
    /// (sauf « Sauvegarder » d'un zaap et les ressources, pour lesquelles le client affiche toujours le menu).
    /// Ce n'est pas un <see cref="IActorMenuProvider"/> : un objet interactif n'est pas un acteur de la carte.
    /// </summary>
    public static class InteractiveMenuProvider
    {
        public const string MenuName = "interactive-context-menu";
        /// <summary>Niveau en deçà duquel le personnage est novice (<c>dofus.Constants.NOVICE_LEVEL</c>).</summary>
        public const int NoviceLevel = 5;
        private static readonly int[] JobTypes = { 1, 2, 3, 4, 7, 10, 12, 14, 15 };
        private static readonly RetroMenuRenderer renderer = new RetroMenuRenderer();
        private static readonly ConditionalWeakTable<MapControl, Attachment> attachments = new ConditionalWeakTable<MapControl, Attachment>();

        /// <summary>Vrai pour un type d'objet qui a un menu dans le client.</summary>
        public static bool HasMenu(Interactives interactive)
        {
            if (interactive?.Cell == null) return false;
            int type = interactive.ClientType;
            return JobTypes.Contains(type) || type == 5 || type == 6 || type == 13;
        }

        /// <summary>
        /// Entrées du menu de l'objet (sans la ligne de titre) : ligne d'information de l'enclos s'il est connu, puis une entrée
        /// par compétence non cachée. Chaque entrée active appelle <see cref="InteractiveActions.UseAsync"/>.
        /// </summary>
        public static IReadOnlyList<MenuEntry> Entries(Interactives interactive, GameClass game)
        {
            var entries = new List<MenuEntry>();
            if (!HasMenu(interactive) || game == null) return entries;
            int type = interactive.ClientType;
            short cell = interactive.Cell.CellID;
            MountParkInfo park = type == 13 ? game.Interactions?.Interactive?.MountPark : null;
            if (park != null) entries.Add(MenuEntry.Statique(park.Describe()));
            ISet<short> jobSkills = CurrentJobSkills(game.character);
            string guild = OwnGuild(game);
            HouseInfo house = HouseDoorMenuProvider.HouseOf(interactive, game);
            bool shortcutChosen = false;
            foreach (short skill in interactive.ClientSkills)
            {
                string state = StateOf(type, skill, jobSkills, game.character, park, guild, house);
                if (state == SkillState.Hidden) continue;
                bool enabled = state == SkillState.Enabled && interactive.IsUsable;
                string name = LangData.Skill.Name(skill);
                var entry = new MenuEntry(name, context => UseAsync(context?.Game ?? game, cell, skill))
                {
                    Activé = enabled, Icône = Icon(type),
                    Infobulle = !interactive.IsUsable ? interactive.Name + " n'est pas utilisable pour l'instant."
                        : state == SkillState.Enabled ? "« " + name + " » (GA500" + cell + ";" + skill + ")" : "Compétence indisponible pour ce personnage."
                };
                if (!shortcutChosen && enabled && IsShortcut(type, skill))
                {
                    shortcutChosen = true;
                    entry.ParDéfaut = true; entry.Raccourci = "Maj + clic";
                }
                entries.Add(entry);
            }
            return entries;
        }

        /// <summary>
        /// Compétence que Maj + clic peut lancer sans menu : jamais pour une ressource (type 1) ni pour « Sauvegarder » (44) ;
        /// pour une porte, seulement « Entrer » (84).
        /// </summary>
        public static bool IsShortcut(int type, short skill) =>
            type != 1 && skill != InteractiveGfx.ZaapSaveSkill && (type != 5 || skill == InteractiveGfx.EnterHouseSkill);

        /// <summary>Entrée lancée par Maj + clic (marquée « par défaut » dans <see cref="Entries"/>), ou <c>null</c> : le menu s'affiche alors.</summary>
        public static MenuEntry ShortcutEntry(IReadOnlyList<MenuEntry> entries) =>
            entries?.FirstOrDefault(entry => entry.ParDéfaut && entry.Activé && entry.Action != null);

        /// <summary>État d'une compétence pour ce type d'objet (<c>Skill.getState</c> avec les paramètres du client).</summary>
        public static string StateOf(int type, short skill, ISet<short> jobSkills, CharacterClass character, MountParkInfo park, string guild) =>
            StateOf(type, skill, jobSkills, character, park, guild, null);

        /// <summary>Même état, avec les données de la maison d'une porte ou d'un coffre (<c>null</c> : propriétaire, vente et verrou faux).</summary>
        public static string StateOf(int type, short skill, ISet<short> jobSkills, CharacterClass character, MountParkInfo park, string guild, HouseInfo house)
        {
            string criterion = SkillState.Criterion(skill);
            switch (type)
            {
                case 5: case 6: return HouseDoorMenuProvider.StateOf(type, criterion, house);
                case 13:
                    bool mine = park != null && park.IsMine(guild);
                    return SkillState.Evaluate(criterion, true, mine, park != null && park.Price > 0, park != null && (park.IsPublic || mine), false, park != null && park.IsPublic);
                default:
                    bool novice = character != null && character.Level <= NoviceLevel;
                    return SkillState.Evaluate(criterion, jobSkills != null && jobSkills.Contains(skill), false, false, false, false, novice);
            }
        }

        /// <summary>Compétences du métier de l'outil équipé (<c>currentJobID</c>), vide sans outil.</summary>
        public static ISet<short> CurrentJobSkills(CharacterClass character)
        {
            var skills = new HashSet<short>();
            int? job = character?.CurrentJobTool;
            if (!job.HasValue) return skills;
            foreach (var entry in character.GetJobsSnapshot().Where(item => item.ID == job.Value))
                foreach (var skill in entry.Skills.ToArray()) skills.Add(skill.Id);
            return skills;
        }

        /// <summary>Guilde du personnage d'après son <c>GM</c> (<c>Map.Self</c>), comme <c>Player.guildInfos.name</c>.</summary>
        private static string OwnGuild(GameClass game) => (game?.Map?.Self as PlayerActor)?.GuildName;

        private static Image Icon(int type)
        {
            switch (type)
            {
                case 3: case 10: return ClientAssets.Icon("zaap", 16);
                default: return null;
            }
        }

        private static async Task<string> UseAsync(GameClass game, short cell, short skill)
        {
            InteractiveActions actions = game?.Interactions?.Interactive;
            if (actions == null) return "La session n’est plus disponible.";
            InteractionResult result = await actions.UseAsync(cell, skill);
            return result?.Message;
        }

        /// <summary>Menu Retro de l'objet (titre, entrées), sans l'afficher ; l'appelant le libère.</summary>
        public static ContextMenuStrip BuildMenu(Interactives interactive, IReadOnlyList<MenuEntry> entries, InteractionRouter router, ActorMenuContext context)
        {
            var strip = new ContextMenuStrip { Renderer = renderer, Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper, ForeColor = BotUi.Ink,
                ShowImageMargin = true, AccessibleName = "Menu de l'objet interactif", Name = MenuName };
            HouseInfo house = HouseDoorMenuProvider.HouseOf(interactive, context?.Game ?? router?.Account?.Game);
            string title = HouseDoorMenuProvider.Title(interactive, house);
            strip.Items.Add(new ToolStripLabel(title.Replace("&", "&&")) { Tag = ActorContextMenu.HeaderTag, Font = BotFonts.Get(8.25f, FontStyle.Bold),
                ForeColor = BotUi.Ink, AccessibleName = "Objet : " + title });
            string ownerLine = HouseDoorMenuProvider.OwnerLine(interactive, house);
            if (ownerLine != null)
                strip.Items.Add(new ToolStripLabel(ownerLine.Replace("&", "&&")) { Name = "house-owner", Font = BotFonts.Get(8.25f), ForeColor = BotUi.Muted, AccessibleName = ownerLine });
            if (entries == null || entries.Count == 0) { strip.Items.Add(new ToolStripMenuItem("Aucune action disponible") { Enabled = false }); return strip; }
            foreach (MenuEntry entry in entries)
            {
                if (entry.EstSéparateur) { strip.Items.Add(new ToolStripSeparator()); continue; }
                var item = new ToolStripMenuItem((entry.Texte ?? string.Empty).Replace("&", "&&"), entry.Icône)
                { Tag = entry, ToolTipText = entry.Infobulle, AccessibleName = entry.Texte, Enabled = entry.Activé && entry.Action != null };
                if (!string.IsNullOrEmpty(entry.Raccourci)) { item.ShortcutKeyDisplayString = entry.Raccourci; item.ShowShortcutKeys = true; }
                if (entry.ParDéfaut) item.Font = BotFonts.Get(8.25f, FontStyle.Bold);
                if (entry.Action != null && router != null) item.Click += async (s, e) => await router.ExecuteAsync(entry, context);
                strip.Items.Add(item);
            }
            return strip;
        }

        /// <summary>
        /// Branche les objets interactifs sur la carte : un clic gauche sur un objet qui a un menu l'affiche (Maj : première
        /// compétence active) au lieu de déplacer le personnage. Les volets Zaapis, Code et Document sont enregistrés par la
        /// fenêtre de jeu (<c>BuildDrawer</c>). Le branchement se défait quand la carte est libérée.
        /// </summary>
        public static void Attach(MapControl map)
        {
            if (map == null || map.IsDisposed || map.Router == null) return;
            lock (attachments)
            {
                if (attachments.TryGetValue(map, out _)) return;
                attachments.Add(map, new Attachment(map));
            }
        }

        /// <summary>Dernier menu d'objet construit pour cette carte (diagnostic et tests), ou <c>null</c>.</summary>
        public static ContextMenuStrip LastMenu(MapControl map)
        {
            if (map == null) return null;
            lock (attachments) return attachments.TryGetValue(map, out Attachment attachment) ? attachment.LastMenu : null;
        }

        private sealed class Attachment
        {
            private readonly MapControl map;
            private readonly InteractionRouter router;
            private bool detached;

            internal Attachment(MapControl map)
            {
                this.map = map; router = map.Router;
                router.MoveRequested += OnMoveRequested;
                map.Disposed += (s, e) => Detach();
            }

            internal ContextMenuStrip LastMenu { get; private set; }

            private async Task OnMoveRequested(object sender, MapClickEventArgs e)
            {
                if (detached || e.Handled || e.Button != MouseButtons.Left) return;
                GameClass current = router.Account?.Game;
                if (current?.Map?.Interactives == null || current.Fight?.IsInFight == true || map.SelectedSpellId.HasValue) return;
                if (!current.Map.Interactives.TryGetValue(e.CellId, out Interactives interactive) || !HasMenu(interactive)) return;
                e.Handled = true;
                IReadOnlyList<MenuEntry> entries = Entries(interactive, current);
                ActorMenuContext context = router.CreateContext(null, null, e.CellId, e.Modifiers);
                if (e.Shift)
                {
                    MenuEntry shortcut = ShortcutEntry(entries);
                    if (shortcut != null) { await router.ExecuteAsync(shortcut, context); return; }
                }
                Show(BuildMenu(interactive, entries, router, context));
            }

            private void Show(ContextMenuStrip strip)
            {
                ContextMenuStrip previous = LastMenu;
                LastMenu = strip;
                if (previous != null && !previous.IsDisposed) { if (previous.Visible) previous.Close(); previous.Dispose(); }
                try { ActorContextMenu.Show(strip, map, map.PointToClient(Cursor.Position)); }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
                { router.Account?.Logger?.LogException("CARTE", error); }
            }

            private void Detach()
            {
                if (detached) return;
                detached = true;
                router.MoveRequested -= OnMoveRequested;
                ContextMenuStrip last = LastMenu; LastMenu = null;
                if (last != null && !last.IsDisposed) last.Dispose();
            }
        }
    }
}
