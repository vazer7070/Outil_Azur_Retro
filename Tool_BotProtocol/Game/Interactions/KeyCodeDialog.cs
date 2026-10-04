using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>
    /// Saisie d'un code de coffre ou de maison, comme <c>dofus.aks.Key</c> et l'interface <c>KeyCode</c> du client 1.34.
    /// Le serveur l'ouvre par <c>KCK&lt;type&gt;|&lt;cases&gt;</c> (type 0 : déverrouiller, 1 : changer le code ; 8 cases chez
    /// StarLoco) ; le bot envoie <c>KK&lt;type&gt;|&lt;code&gt;</c> où chaque case vide vaut « _ » et un code entièrement vide « - »,
    /// ou <c>KV</c> pour fermer. Réponses : <c>KKE</c> (code erroné), <c>KK…</c> (code changé), <c>KV</c> (fermeture).
    /// </summary>
    public sealed class KeyCodeDialog : InteractionWindow
    {
        /// <summary>Nombre maximal de cases accepté par le client (<c>slotsCount</c>).</summary>
        public const int MaxSlots = 8;
        public const char EmptySlot = '_';

        /// <summary>0 : déverrouiller ; 1 : changer le code (le bouton « Aucun code » n'existe qu'alors) ; -1 sans fenêtre.</summary>
        public int ChangeType { get; private set; } = -1;
        public int SlotCount { get; private set; }
        public bool IsChange => ChangeType == 1;
        /// <summary>Vrai après <c>KKE</c> jusqu'au prochain envoi.</summary>
        public bool LastCodeRefused { get; private set; }

        protected override string Reference => "CODE";
        protected override AccountStates OpenState => AccountStates.DIALOG;

        internal KeyCodeDialog(Accounts.Accounts account) : base(account) { }

        /// <summary>
        /// Code tel que l'interface du client l'envoie : les chiffres saisis dans l'ordre des cases, « _ » pour une case vide
        /// (un code plus court que les cases est complété par « _ ») ; « - » si aucune case n'est remplie. <c>null</c> si
        /// le texte contient autre chose que des chiffres et des « _ » ou dépasse le nombre de cases.
        /// </summary>
        public static string BuildCode(string digits, int slotCount)
        {
            if (slotCount < 1 || slotCount > MaxSlots) return null;
            string code = digits ?? string.Empty;
            if (code.Length > slotCount || code.Any(c => c != EmptySlot && (c < '0' || c > '9'))) return null;
            code = code.PadRight(slotCount, EmptySlot);
            return code.All(c => c == EmptySlot) ? "-" : code;
        }

        /// <summary>Envoie <c>KK&lt;type&gt;|&lt;code&gt;</c> (« Déverrouiller » ou « Changer »).</summary>
        public Task<InteractionResult> SendCodeAsync(string digits)
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre de code n'est pas ouverte."));
            string code = BuildCode(digits, SlotCount);
            if (code == null) return Task.FromResult(Refuse("Le code ne peut contenir que " + SlotCount + " chiffres au plus."));
            LastCodeRefused = false;
            return SendAsync("KK" + ChangeType.ToString(CultureInfo.InvariantCulture) + "|" + code,
                IsChange ? "Nouveau code envoyé." : "Code envoyé ; le serveur ouvre ou refuse.");
        }

        /// <summary>« Aucun code » (changement de code seulement) : <c>KK1|-</c>.</summary>
        public Task<InteractionResult> NoCodeAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre de code n'est pas ouverte."));
            if (!IsChange) return Task.FromResult(Refuse("« Aucun code » n'existe que pour changer le code."));
            LastCodeRefused = false;
            return SendAsync("KK1|-", "Suppression du code demandée.");
        }

        /// <summary>Fermeture : <c>KV</c> ; le serveur confirme par <c>KV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre de code n'est pas ouverte."));
            return SendAsync("KV", "Fermeture de la saisie du code demandée.");
        }

        /// <summary><c>KCK&lt;type&gt;|&lt;cases&gt;</c> (sans « KCK ») : ouvre la saisie.</summary>
        internal void OnCreate(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slots))
            {
                LogError("Fenêtre de code illisible : KC" + payload);
                Notify();
                return;
            }
            if (slots < 1 || slots > MaxSlots)
            {
                // Le client refuse plus de 8 cases ; le bot garde 8 cases pour que la fenêtre puisse être fermée.
                Account?.Logger?.LogError(Reference, "Nombre de cases inattendu (" + slots + ") : 8 cases utilisées.");
                slots = MaxSlots;
            }
            ChangeType = type == 1 ? 1 : 0;
            SlotCount = slots;
            LastCodeRefused = false;
            MarkOpen();
            Log(Text("TYPE_CODE", "Saisie du code") + " (" + slots + " chiffres).");
            Notify();
        }

        /// <summary><c>KKE</c> : code erroné (la fenêtre reste ouverte jusqu'à <c>KV</c>).</summary>
        internal void OnCodeRefused()
        {
            LastCodeRefused = true;
            LogError(Text("BAD_CODE", "Code refusé."));
            Notify();
        }

        /// <summary><c>KK</c> sans erreur : code changé.</summary>
        internal void OnCodeChanged()
        {
            LastCodeRefused = false;
            Log(Text("CODE_CHANGED", "Code modifié."));
            Notify();
        }

        /// <summary><c>KV</c> : fermeture.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Saisie du code fermée.");
            Notify();
        }

        protected override void Reset()
        {
            ChangeType = -1;
            SlotCount = 0;
            LastCodeRefused = false;
        }

        private static string Text(string key, string fallback) => LangData.Text.Has(key) ? LangData.Text.Get(key).Trim() : fallback;
    }
}
