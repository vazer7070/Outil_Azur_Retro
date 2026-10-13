using System;
using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Accounts
{
    /// <summary>Origine de la dernière déconnexion d'un compte (lot X3).</summary>
    public enum DisconnectCause
    {
        /// <summary>Aucune déconnexion depuis la création du compte.</summary>
        None,
        /// <summary>Demandée par le bot ou l'utilisateur (<see cref="Accounts.Disconnect()"/> : Déconnexion, Changer de personnage, Retenter).</summary>
        Requested,
        /// <summary>Refus du serveur pendant la connexion (<c>AlE…</c>, <c>AXE…</c>, <c>ATE</c>, <c>M030</c>…), déjà écrit dans l'état de connexion.</summary>
        Refused,
        /// <summary>Fin de partie du mode héroïque (<c>GO</c>), annoncée par sa propre boîte (lot X2).</summary>
        GameOver,
        /// <summary>Connexion fermée par le serveur ou coupée par le réseau : c'est la perte de connexion du client (<c>Aks.onClose</c>).</summary>
        Lost
    }

    /// <summary>
    /// Message système <c>M0&lt;id&gt;|&lt;a;b;…&gt;</c> : comme <c>Basics.onServerMessage</c> du client (branche « 0 »), il n'est pas affiché
    /// à sa réception mais gardé (<c>serverMessageID</c>, <c>serverMessageParams</c>) jusqu'à la coupure qui suit, puis ajouté au texte
    /// <c>DISCONNECT</c> de la boîte de connexion. StarLoco envoie <c>M04</c> (maintenance) à tous les joueurs avant de les expulser
    /// (<c>GameServer.kickAll</c> : arrêt du serveur, commande <c>KICKALL</c>) et <c>M018|&lt;modérateur&gt;;&lt;br&gt;raison</c> au
    /// joueur expulsé par la commande <c>KICK</c>.
    /// </summary>
    public sealed class SystemMessage
    {
        internal SystemMessage(int id, string[] args)
        {
            Id = id;
            Args = Array.AsReadOnly(args ?? new string[0]);
            Text = ConnectionTexts.SystemMessage(id, args ?? new string[0], out bool resolved);
            Resolved = resolved;
        }

        /// <summary>Numéro du texte <c>SRV_MSG_&lt;id&gt;</c> de <c>lang_fr</c> (4 : maintenance, 18 : expulsion).</summary>
        public int Id { get; }
        public IReadOnlyList<string> Args { get; }
        /// <summary>Texte sans HTML (<c>&lt;br&gt;</c> devient un retour à la ligne).</summary>
        public string Text { get; }
        /// <summary>Vrai quand le texte vient de <c>lang.xml</c> ; faux pour le repli du bot.</summary>
        public bool Resolved { get; }
        public override string ToString() => "M0" + Id.ToString(CultureInfo.InvariantCulture) + " : " + Text;
    }

    /// <summary>Déconnexion d'un compte, telle que l'interface doit la présenter.</summary>
    public sealed class ConnectionLoss
    {
        internal ConnectionLoss(DisconnectCause cause, bool wasInGame, SystemMessage message, string status)
        {
            Cause = cause;
            WasInGame = wasInGame;
            Message = message;
            Status = status ?? string.Empty;
            Title = ConnectionTexts.ConnectionTitle;
            Text = ConnectionTexts.Disconnected + (message == null ? string.Empty : "\n\n" + message.Text);
        }

        public DisconnectCause Cause { get; }
        /// <summary>Vrai si un personnage était sélectionné (<c>ASK</c> reçu) au moment de la coupure.</summary>
        public bool WasInGame { get; }
        /// <summary>Dernier <c>M0</c> reçu avant la coupure, ou <c>null</c>.</summary>
        public SystemMessage Message { get; }
        /// <summary>État de connexion du compte juste avant la coupure (« En jeu », « Connexion impossible : … »).</summary>
        public string Status { get; }
        /// <summary>Titre de la boîte du client (<c>CONNECTION</c>).</summary>
        public string Title { get; }
        /// <summary><c>DISCONNECT</c>, puis le texte du <c>M0</c> séparé par une ligne vide, comme <c>Aks.onClose</c>.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// Textes de la connexion (lot X3) : clé de <c>lang_fr</c> du client quand <c>lang.xml</c> est chargé, sinon repli identique au
    /// texte du client 1.34 (paramètres <c>%1</c>…<c>%n</c>).
    /// </summary>
    public static class ConnectionTexts
    {
        /// <summary>Textes <c>SRV_MSG_&lt;id&gt;</c> que StarLoco envoie en <c>M0</c>, repris de <c>lang_fr_807</c> pour un bot sans fichier de langue.</summary>
        private static readonly Dictionary<int, string> SystemMessageFallbacks = new Dictionary<int, string>
        {
            { 4, "Votre connexion a été coupée pour des raisons de maintenance." },
            { 18, "Vous avez été kické par %1.%2" },
        };

        public static string ConnectionTitle => MapActionTexts.Get("CONNECTION", "Connexion");
        public static string Disconnected => MapActionTexts.Get("DISCONNECT", "Connexion interrompue avec le serveur.");
        public static string InformationsTitle => MapActionTexts.Get("INFORMATIONS", "Informations");
        /// <summary><c>NEW_LEVEL</c> : boîte INFORMATIONS de <c>Account.onNewLevel</c>.</summary>
        public static string NewLevel(int level) => MapActionTexts.Get("NEW_LEVEL",
            "Tu passes niveau %1\n \nTu gagnes 5 points pour faire évoluer tes caractéristiques et 1 point pour tes sorts.",
            level.ToString(CultureInfo.InvariantCulture));
        /// <summary>Ligne ajoutée au chat avec la boîte du nouveau niveau (le client n'écrit rien dans le chat).</summary>
        public static string NewLevelLine(int level) => "Nouveau niveau : " + level.ToString(CultureInfo.InvariantCulture) + ".";

        // Interface ChooseNickName du client (pseudo d'un compte neuf).
        public static string NicknameTitle => MapActionTexts.Get("CHOOSE_NICKNAME", "Choix du pseudo");
        public static string NicknameHelp => MapActionTexts.Get("CHOOSE_NICKNAME_HELP",
            "C'est la première fois que vous vous connectez à Dofus. \nAfin d'avoir une identité dans le jeu, vous devez choisir un pseudo. \nC'est sous cette dénomination que vous apparaîtrez aux autres joueurs.");
        public static string NicknameConfirm(string nickname) => MapActionTexts.Get("DO_CHOOSE_NICKNAME",
            "Une fois choisi, votre pseudo ne peut pas être changé.\nConfirmez-vous l'enregistrement du pseudo %1 ?", nickname ?? string.Empty);
        public static string NicknameAlreadyUsed => MapActionTexts.Get("NICKNAME_ALREADY_USED", "Ce pseudo n'est pas disponible");
        public static string NicknameEqualsLogin => MapActionTexts.Get("NICKNAME_EQUALS_LOGIN", "Le pseudo doit être différent du nom de compte");
        public static string NicknameTooShort => "Le pseudo doit compter au moins trois caractères.";
        public static string Ok => MapActionTexts.Get("OK", "OK");
        public static string Cancel => MapActionTexts.Get("CANCEL_SMALL", "Annuler");

        /// <summary><c>SRV_MSG_&lt;id&gt;</c> avec ses paramètres, HTML retiré ; repli du bot, puis texte générique pour un numéro inconnu.</summary>
        internal static string SystemMessage(int id, string[] args, out bool resolved)
        {
            string key = "SRV_MSG_" + id.ToString(CultureInfo.InvariantCulture);
            resolved = LangData.Text.Has(key);
            string fallback;
            if (!SystemMessageFallbacks.TryGetValue(id, out fallback))
                fallback = "Message du serveur n° " + id.ToString(CultureInfo.InvariantCulture) + (args.Length > 0 ? " : " + string.Join(" ; ", args) : string.Empty);
            return LangData.Text.Plain(MapActionTexts.Get(key, fallback, args)).Trim();
        }
    }
}
