using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Interfaces
{
    /// <summary>
    /// Visée d'un objet sur la carte (lot X13, m-04), comme <c>switchToItemTarget</c> du client 1.34 : jamais en combat, la flèche
    /// garde l'icône de l'objet à côté (<c>setCursor(objet, {25, 25, 15, 15})</c>), toute cellule survolée reçoit le pointeur
    /// <c>C0</c> en <c>CELL_SPELL_EFFECT_COLOR</c> et la croix d'interdiction apparaît hors de la carte. Le premier clic gauche
    /// envoie <c>OU&lt;objet&gt;|&lt;sprite&gt;|&lt;cellule&gt;</c> (acteur cliqué, ou sprite posé sur la cellule) ou
    /// <c>OU&lt;objet&gt;||&lt;cellule&gt;</c> (case vide), puis la carte repasse en déplacement, même si rien n'est parti
    /// (<c>onCellRelease</c>, <c>onSpriteRelease</c>). Échap, un changement de carte ou l'entrée en combat annulent la visée.
    /// </summary>
    public partial class MapControl
    {
        private InventoryObjects itemTarget;
        private bool itemTargetHooked;

        /// <summary>Objet en cours de visée, ou null.</summary>
        public InventoryObjects TargetedItem => itemTarget;
        /// <summary>Vrai pendant la visée d'un objet (« Cibler avec cet objet »).</summary>
        public bool ItemTargetMode => itemTarget != null;

        /// <summary>
        /// Arme la visée de <paramref name="item"/> (fil de l'interface) ; faux en combat, sans carte ou sans objet. Un sort
        /// sélectionné est abandonné, comme le client remplace un mode de clic par un autre.
        /// </summary>
        public bool BeginItemTarget(InventoryObjects item)
        {
            if (item == null) return false;
            if (Account.Game?.Fight?.IsInFight == true) { ActionFeedback?.Invoke("Un objet ne se cible pas pendant un combat."); return false; }
            if (Account.Game?.Map?.HasMapData != true || Account.Game.Map.MapCells == null) { ActionFeedback?.Invoke("La carte n'est pas encore chargée : rien à viser."); return false; }
            HookItemTarget();
            if (SelectedSpellId.HasValue) SelectSpell(null);
            itemTarget = item;
            previewPending = null; UserMap.SetPathPreview(null);
            ApplyItemTarget();
            ActionFeedback?.Invoke("Cliquez sur un joueur ou une cellule pour utiliser " + item.Name + " · Échap : annuler");
            return true;
        }

        /// <summary>Échap : abandonne la visée de l'objet ; faux s'il n'y en avait pas.</summary>
        public bool CancelItemTarget()
        {
            if (itemTarget == null) return false;
            EndItemTarget();
            ActionFeedback?.Invoke("Visée de l'objet annulée.");
            return true;
        }

        private void HookItemTarget()
        {
            if (itemTargetHooked || Router == null) return;
            itemTargetHooked = true;
            Router.MoveRequested += ItemTargetClickAsync;
            Router.ActorClicked += ItemTargetClickAsync;
        }

        /// <summary>Cibles, icône, pointeur et infobulle de la visée ; rappelé par <see cref="RefreshSpellTargets"/> à chaque mise à jour de la carte.</summary>
        private void ApplyItemTarget()
        {
            InventoryObjects item = itemTarget;
            Cell[] cells = Account.Game?.Map?.MapCells;
            if (item == null || cells == null) return;
            UserMap.SetSpellTargets(cells.Where(cell => cell != null).Select(cell => cell.CellID));
            UserMap.SetSpellRange(null);
            int? type = LangData.Item.Type(item.ID), gfx = LangData.Item.Gfx(item.ID);
            if (type.HasValue && gfx.HasValue) UserMap.SetAimIcon("Items", type.Value.ToString(CultureInfo.InvariantCulture) + "/" + gfx.Value.ToString(CultureInfo.InvariantCulture));
            else UserMap.SetAimIcon(null, null);
            string name = item.Name;
            UserMap.SpellTargetReason = cell => "clic : utiliser " + name + " ici · Échap : annuler";
            UserMap.PointerShapes = cell => new[] { new[] { cell } };
        }

        /// <summary>Sortie silencieuse de la visée (clic traité, carte changée, combat) : la vue redevient celle du déplacement.</summary>
        private void EndItemTarget()
        {
            if (itemTarget == null) return;
            itemTarget = null;
            UserMap.Cursor = Cursors.Default;
            RefreshSpellTargets();
        }

        /// <summary>Entrée en combat ou changement de carte : la visée est abandonnée sans message.</summary>
        private void DisarmItemTarget(bool always)
        {
            if (itemTarget == null) return;
            if (always || Account?.Game?.Fight?.IsInFight == true) EndItemTarget();
        }

        private async Task ItemTargetClickAsync(object sender, MapClickEventArgs e)
        {
            InventoryObjects item = itemTarget;
            if (item == null || e == null || e.Handled || e.Button != MouseButtons.Left) return;
            e.Handled = true;
            EndItemTarget();
            if (Account?.Game?.Fight?.IsInFight == true) return;
            // Acteur cliqué (onSpriteRelease), sinon le sprite posé sur la cellule (onCellRelease : spriteOnID), sinon aucun.
            Entites actor = e.Actor ?? ActorsAt(e.CellId).FirstOrDefault();
            long? sprite = actor == null ? (long?)null : actor.id;
            InventoryObjects current = Account.Game?.character?.Inventory?.GetByInventoryId(item.Inventory_ID);
            if (current == null) { ActionFeedback?.Invoke(item.Name + " n'est plus dans l'inventaire."); return; }
            try
            {
                ItemUseResult result = await SpecialItems.UseOnAsync(Account.Game, current, sprite, e.CellId);
                ActionFeedback?.Invoke(result.Message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { ActionFeedback?.Invoke("Utilisation impossible : " + error.Message); }
        }
    }
}
