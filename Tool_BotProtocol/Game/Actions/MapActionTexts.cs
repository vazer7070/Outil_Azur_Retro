using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Actions
{
    /// <summary>
    /// Textes des actions sur la carte (défi, agression, combats, « qui est »). Chaque texte est celui de la clé de
    /// <c>lang_fr</c> utilisée par le client 1.34 quand <c>lang.xml</c> est chargé (HTML retiré) ; sinon le bot affiche son
    /// propre libellé de repli, avec les mêmes paramètres.
    /// </summary>
    public static class MapActionTexts
    {
        /// <summary>Codes d'erreur de <c>GA;903;&lt;id&gt;;&lt;code&gt;</c> et clés lues par <c>GameActions.onActions</c> du client.</summary>
        private static readonly Dictionary<string, KeyValuePair<string, string>> JoinErrors = new Dictionary<string, KeyValuePair<string, string>>
        {
            { "c", Pair("CHALENGE_FULL", "Le combat est complet.") },
            { "t", Pair("TEAM_FULL", "Cette équipe est complète.") },
            { "a", Pair("TEAM_DIFFERENT_ALIGNMENT", "Votre alignement ne permet pas de rejoindre cette équipe.") },
            { "g", Pair("CANT_DO_BECAUSE_GUILD", "Action impossible à cause de la guilde.") },
            { "l", Pair("CANT_DO_TOO_LATE", "Trop tard : le combat a déjà commencé.") },
            { "m", Pair("CANT_U_ARE_MUTANT", "Action impossible sous forme de mutant.") },
            { "p", Pair("CANT_BECAUSE_MAP", "Cette carte ne le permet pas.") },
            { "r", Pair("CANT_BECAUSE_ON_RESPAWN", "Action impossible sur un point de résurrection.") },
            { "o", Pair("CANT_YOU_R_OCCUPED", "Votre personnage est déjà occupé.") },
            { "z", Pair("CANT_YOU_OPPONENT_OCCUPED", "L'adversaire est déjà occupé.") },
            { "h", Pair("CANT_FIGHT", "Combat impossible.") },
            { "i", Pair("CANT_FIGHT_NO_RIGHTS", "Vous n'avez pas le droit de combattre ici.") },
            { "s", Pair("ERROR_21", "Action refusée par le serveur.") },
            { "n", Pair("SUBSCRIPTION_OUT", "Un abonnement est nécessaire.") },
            { "b", Pair("A_NOT_SUBSCRIB", "L'autre joueur n'a pas d'abonnement.") },
            { "f", Pair("TEAM_CLOSED", "Cette équipe n'accepte plus personne.") },
            { "d", Pair("NO_ZOMBIE_ALLOWED", "Les personnages à l'état de fantôme ne peuvent pas combattre.") },
        };

        /// <summary>Les dix-sept codes connus du client, dans l'ordre de son aiguillage.</summary>
        public static IReadOnlyCollection<string> JoinErrorCodes => JoinErrors.Keys;

        /// <summary>Texte d'une clé du client ; le repli (paramètres <c>%1</c>…<c>%n</c>) si la clé est absente.</summary>
        public static string Get(string key, string fallback, params string[] args)
        {
            string value = key != null && LangData.Text.Has(key) ? LangData.Text.Plain(LangData.Text.Get(key, args)) : null;
            if (!string.IsNullOrEmpty(value)) return value.Trim();
            string text = fallback ?? key ?? string.Empty;
            if (args != null)
                for (int i = 0; i < args.Length; i++) text = text.Replace("%" + (i + 1).ToString(CultureInfo.InvariantCulture), args[i] ?? string.Empty);
            return text;
        }

        /// <summary>Clé du client pour un code d'erreur de <c>GA;903</c>, ou <c>null</c> si le client ne le connaît pas.</summary>
        public static string JoinErrorKey(string code) => code != null && JoinErrors.TryGetValue(code, out var entry) ? entry.Key : null;

        /// <summary>Message d'un code d'erreur de <c>GA;903</c> ; un code inconnu (que le client ignore) reste signalé.</summary>
        public static string JoinError(string code)
        {
            if (code != null && JoinErrors.TryGetValue(code, out var entry)) return Get(entry.Key, entry.Value);
            return "Le serveur refuse l'action de combat (code « " + (code ?? string.Empty) + " »).";
        }

        // Libellés des menus et des volets (clés du client).
        public static string Whois => Get("WHOIS", "Informations sur le joueur");
        public static string PrivateMessage => Get("WISPER_MESSAGE", "Envoyer un message privé");
        public static string InviteToParty => Get("ADD_TO_PARTY", "Inviter dans le groupe");
        public static string Exchange => Get("EXCHANGE", "Proposer un échange");
        public static string Challenge => Get("CHALLENGE", "Proposer un duel");
        public static string Assault => Get("ASSAULT", "Agresser (JcJ)");
        public static string Attack => Get("ATTACK", "Attaquer");
        public static string Join => Get("JOIN_SMALL", "Rejoindre");
        public static string Spectator => Get("SPECTATOR", "Regarder le combat");
        public static string CurrentFights => Get("CURRENT_FIGTHS", "Combats sur la carte");
        public static string FightersCount => Get("FIGHTERS_COUNT", "Combattants");
        public static string Duration => Get("DURATION", "Durée");
        public static string Level => Get("LEVEL", "Niveau");
        public static string Team => Get("TEAM", "Équipe");
        public static string Close => Get("CLOSE", "Fermer");
        public static string SelectFight => Get("SELECT_FIGHT_FOR_SPECTATOR", "Choisissez un combat pour voir ses équipes et le regarder.");
        public static string ChallengeTitle => Get("CHALENGE", "Duel");

        // Messages des actions de jeu GA 900 à 909.
        public static string ChallengeYou(string challenger) => Get("A_CHALENGE_YOU", "%1 vous propose un duel. L'acceptez-vous ?", challenger);
        public static string AChallengesB(string a, string b) => Get("A_CHALENGE_B", "%1 propose un duel à %2.", a, b);
        public static string YouChallengeB(string b) => Get("YOU_CHALENGE_B", "Duel proposé à %1 : en attente de sa réponse.", b);
        public static string AAttacksB(string a, string b) => Get("A_ATTACK_B", "%1 attaque %2.", a, b);
        public static string YouAreAttacked => Get("YOU_ARE_ATTAC", "Votre personnage est attaqué : le combat commence.");
        public static string TemporaryIgnored(string name) => Get("TEMPORARY_BLACKLISTED", "%1 est ignoré(e) jusqu'à la fin de la session.", name);

        /// <summary>Question de <c>GameManager.askAttack</c> : avertissements (ailes repliées, cible sans grade) puis <c>DO_U_ATTACK</c>.</summary>
        public static string AskAttack(string target, bool pvpDisabled, bool neutralTarget)
        {
            string text = string.Empty;
            if (pvpDisabled) text += Get("DO_U_ATTACK_WHEN_PVP_DISABLED", "Continuer réactivera votre mode joueur contre joueur.");
            if (neutralTarget)
            {
                if (text.Length > 0) text += "\n\n";
                text += Get("DO_U_ATTACK_NEUTRAL", "Ce personnage n'affiche pas de grade : l'agresser peut vous valoir un point de déshonneur.");
            }
            if (text.Length > 0) text += "\n\n";
            return text + Get("DO_U_ATTACK", "Agresser %1 ?", target);
        }

        // Réponses de « qui est » (Basics.onWhoIs).
        public static string UnknownArea => Get("UNKNOWN_AREA", "lieu inconnu");
        public static string WhoisNotFound(string name) => Get("CANT_FIND_ACCOUNT_OR_CHARACTER", "%1 est introuvable ou hors ligne.", name);

        /// <summary>Texte de <c>BWK</c> : soi-même (pseudo = identifiant du compte) ou un autre joueur, sur une carte ou en combat.</summary>
        public static string WhoisAnswer(WhoisInfo info, bool self)
        {
            if (info == null) return string.Empty;
            string area = info.AreaName ?? UnknownArea;
            if (self)
                return info.State == 2
                    ? Get("I_AM_IN_GAME", "Vous jouez %1 (%2), en combat.", info.CharacterName, info.Pseudo, area)
                    : Get("I_AM_IN_SINGLE_GAME", "Vous jouez %1 (%2), à %3.", info.CharacterName, info.Pseudo, area);
            return info.State == 2
                ? Get("IS_IN_GAME", "%1 (%2) est en combat.", info.CharacterName, info.Pseudo, area)
                : Get("IS_IN_SINGLE_GAME", "%1 (%2) se trouve à %3.", info.CharacterName, info.Pseudo, area);
        }

        private static KeyValuePair<string, string> Pair(string key, string fallback) => new KeyValuePair<string, string>(key, fallback);
    }
}
