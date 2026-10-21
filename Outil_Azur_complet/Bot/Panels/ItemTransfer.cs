using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Objet glissé depuis une grille d'échange, de coffre, de sacoches ou d'atelier (format <see cref="ItemTransfer.DragFormat"/>) :
    /// la grille d'origine, l'identifiant de l'exemplaire et la quantité de la case. Le format diffère de celui de la barre de
    /// raccourcis : un objet du coffre ou d'une proposition n'est pas dans le sac et ne doit pas atterrir sur la barre ni sur
    /// un emplacement d'équipement.
    /// </summary>
    public sealed class ExchangeItemDrag
    {
        public ExchangeItemDrag(Control source, uint id, int templateId, int quantity)
        {
            Source = source; Id = id; TemplateId = templateId; Quantity = Math.Max(0, quantity);
        }

        public Control Source { get; }
        public uint Id { get; }
        public int TemplateId { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// Glisser-déposer entre le sac et les volets d'échange, de coffre, de sacoches et d'atelier, comme les interfaces
    /// <c>Exchange</c>, <c>Storage</c> et <c>Craft</c> du client 1.34 (<c>dropItem</c>, <c>validateDrop</c>) : boîte de quantité
    /// (valeur 1, maximum la pile) quand la pile dépasse 1, sinon 1 sans question ; quantité nulle ou boîte abandonnée : rien.
    /// </summary>
    internal static class ItemTransfer
    {
        /// <summary>Format des objets glissés depuis une grille d'échange.</summary>
        public const string DragFormat = "AzurClientRetro.ExchangeItem";

        /// <summary>Teinte d'une liste qui peut recevoir l'objet survolé : parchemin clair mêlé d'un peu d'olive.</summary>
        public static readonly Color DropTint = Color.FromArgb(
            (BotUi.PaperLight.R * 85 + BotUi.Olive.R * 15) / 100, (BotUi.PaperLight.G * 85 + BotUi.Olive.G * 15) / 100, (BotUi.PaperLight.B * 85 + BotUi.Olive.B * 15) / 100);

        /// <summary>Contenu glissé : <see cref="ExchangeItemDrag"/>, <see cref="ShortcutPayload"/> ou <c>null</c>.</summary>
        public static object Payload(IDataObject data)
        {
            if (data == null) return null;
            try
            {
                if (data.GetDataPresent(DragFormat)) return data.GetData(DragFormat) as ExchangeItemDrag;
                if (data.GetDataPresent(ShortcutBar.DragFormat)) return data.GetData(ShortcutBar.DragFormat) as ShortcutPayload;
            }
            catch (Exception error) when (error is ExternalException || error is InvalidOperationException || error is ArgumentException) { }
            return null;
        }

        /// <summary>Objet du sac (non équipé) glissé depuis la liste d'un volet ou la grille d'inventaire.</summary>
        public static bool IsBagItem(object payload) =>
            payload is ShortcutPayload item && item.Kind == ShortcutSlotKind.Item && item.FromPosition == (int)InventorySlots.NOT_EQUIPPED && item.Id > 0 && item.Id <= uint.MaxValue;

        /// <summary>Identifiant d'inventaire d'un objet du sac glissé (voir <see cref="IsBagItem"/>).</summary>
        public static uint BagItemId(object payload) => payload is ShortcutPayload item ? (uint)item.Id : 0u;

        /// <summary>Objet glissé depuis la grille <paramref name="source"/>.</summary>
        public static bool IsFrom(object payload, Control source) => payload is ExchangeItemDrag item && source != null && ReferenceEquals(item.Source, source);

        /// <summary>Lance le glisser d'une case de grille ; un refus du système (aucune session graphique) n'envoie rien.</summary>
        public static void StartDrag(Control source, ExchangeItem item)
        {
            if (source == null || source.IsDisposed || item == null) return;
            var data = new DataObject();
            data.SetData(DragFormat, new ExchangeItemDrag(source, item.Id, item.TemplateId, item.Quantity));
            try { source.DoDragDrop(data, DragDropEffects.Move); }
            catch (Exception error) when (error is InvalidOperationException || error is ExternalException) { /* Glisser refusé : rien à envoyer. */ }
        }

        /// <summary>
        /// Fait d'une liste du sac (lignes dont <c>Tag</c> est l'identifiant d'inventaire) une source de glisser au format de la
        /// barre de raccourcis, avec la quantité encore disponible de la ligne.
        /// </summary>
        public static void MakeBagSource(ListView list, Func<uint, InventoryObjects> lookup, Func<InventoryObjects, int> available)
        {
            list.ItemDrag += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || !(e.Item is ListViewItem row) || !(row.Tag is uint id)) return;
                InventoryObjects item = lookup(id);
                int quantity = item == null ? 0 : available(item);
                if (quantity <= 0) return;
                try { list.DoDragDrop(ItemCellPainter.DragData(item, quantity), DragDropEffects.Move); }
                catch (Exception error) when (error is InvalidOperationException || error is ExternalException) { /* Glisser refusé : rien à envoyer. */ }
            };
        }

        /// <summary>
        /// Quantité à déplacer, comme <c>dropItem</c> du client : rien sous 1, 1 sans question pour un exemplaire seul, sinon la
        /// boîte de quantité (valeur 1, maximum <paramref name="maximum"/>) ; la réponse est bornée comme <c>validateDrop</c>.
        /// </summary>
        public static Task<int?> AskAsync(Control owner, int maximum)
        {
            if (maximum <= 0) return Task.FromResult<int?>(null);
            if (maximum == 1) return Task.FromResult<int?>(1);
            return BotDialogs.AskQuantityAsync(owner, 1, maximum);
        }

        /// <summary>
        /// <c>Player.getPossibleItemReceiveQuantity</c> : exemplaires d'un poids unitaire donné qui tiennent dans la place libre,
        /// bornés à la pile. Un poids nul ne limite pas ; une place inconnue (<c>null</c>, aucune charge reçue) non plus : le serveur
        /// décide alors.
        /// </summary>
        public static int Receivable(int quantity, int unitWeight, long? freeWeight)
        {
            if (quantity <= 0) return 0;
            if (unitWeight <= 0 || freeWeight == null) return quantity;
            return (int)Math.Min(quantity, Math.Max(0, freeWeight.Value) / unitWeight);
        }

        /// <summary>Place libre du personnage (<c>Ow</c>), <c>null</c> tant que sa charge maximale n'est pas connue.</summary>
        public static long? FreePods(InventoryClass inventory) =>
            inventory == null || inventory.Pods_Max <= 0 ? (long?)null : (long)inventory.Pods_Max - inventory.Actual_pods;

        /// <summary>Poids unitaire d'un modèle d'après <c>LangData.Item</c>, 0 s'il est inconnu.</summary>
        public static int UnitWeight(int templateId) => templateId > 0 ? Math.Max(0, LangData.Item.Pods(templateId) ?? 0) : 0;
    }

    /// <summary>Un seul déplacement à la fois par volet : un second glisser pendant la boîte de quantité est ignoré.</summary>
    internal sealed class TransferGate
    {
        private bool busy;

        public bool Busy => busy;

        public async Task RunAsync(Func<Task> work)
        {
            if (busy || work == null) return;
            busy = true;
            try { await work(); }
            finally { busy = false; }
        }
    }

    /// <summary>
    /// Zone de dépôt posée sur une liste du sac : teinte olive pendant le survol d'un contenu accepté, appel du gestionnaire au
    /// dépôt. <see cref="AcceptDrop"/> sert aussi aux tests, qui n'ont pas de glisser réel du système.
    /// </summary>
    internal sealed class ItemDropZone
    {
        private readonly Control target;
        private readonly Func<object, bool> accepts;
        private readonly Action<object> drop;
        private Color normal;
        private bool lit;

        public ItemDropZone(Control target, Func<object, bool> accepts, Action<object> drop)
        {
            this.target = target; this.accepts = accepts; this.drop = drop;
            target.AllowDrop = true;
            target.DragEnter += OnDrag;
            target.DragOver += OnDrag;
            target.DragLeave += (s, e) => Light(false);
            target.DragDrop += (s, e) => AcceptDrop(ItemTransfer.Payload(e.Data));
        }

        /// <summary>Vrai pendant le survol d'un contenu accepté.</summary>
        public bool Highlighted => lit;

        /// <summary>Dépose un contenu glissé : faux (et rien d'appelé) s'il n'est pas accepté ici.</summary>
        public bool AcceptDrop(object payload)
        {
            Light(false);
            if (payload == null || target.IsDisposed || !accepts(payload)) return false;
            drop(payload);
            return true;
        }

        private void OnDrag(object sender, DragEventArgs e)
        {
            bool fits = !target.IsDisposed && accepts(ItemTransfer.Payload(e.Data));
            e.Effect = fits ? DragDropEffects.Move : DragDropEffects.None;
            Light(fits);
        }

        private void Light(bool value)
        {
            if (lit == value || target.IsDisposed) return;
            if (value) normal = target.BackColor;
            lit = value;
            target.BackColor = value ? ItemTransfer.DropTint : normal;
        }
    }
}
