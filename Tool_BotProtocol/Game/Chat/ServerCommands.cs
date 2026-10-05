using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Chat
{
    /// <summary>Famille d'une commande joueur du serveur, pour classer l'aide.</summary>
    public enum ServerCommandCategory { Information, Teleportation, Personnage, Objets, Groupe, Banque, Alignement, Divers }

    /// <summary>
    /// Commande joueur de StarLoco (<c>CommandPlayer.analyse</c>) : nom reconnu sans « . », syntaxe, effet et conditions
    /// relevées dans les sources du serveur. Le serveur ne la lit que dans le canal général : <c>BM*|.&lt;nom&gt; [arguments]|</c>.
    /// </summary>
    public sealed class ServerCommand
    {
        internal ServerCommand(string name, ServerCommandCategory category, string help, string argument = null,
            bool argumentRequired = false, string note = null, string[] choices = null, bool freeText = false, bool outOfFight = false)
        {
            Name = name;
            Category = category;
            Help = help;
            Argument = argument;
            ArgumentRequired = argumentRequired;
            Note = note;
            Choices = Array.AsReadOnly(choices ?? new string[0]);
            FreeText = freeText;
            OutOfFight = outOfFight;
        }

        /// <summary>Nom tel que le serveur le compare (sans casse), sans le point.</summary>
        public string Name { get; }
        public ServerCommandCategory Category { get; }
        /// <summary>Effet décrit d'après le code du serveur.</summary>
        public string Help { get; }
        /// <summary>Forme de l'argument (« &lt;niveau&gt; »), ou <c>null</c> quand la commande n'en prend pas.</summary>
        public string Argument { get; }
        public bool ArgumentRequired { get; }
        /// <summary>Conditions que le serveur vérifie (combat, prison, banque ouverte…), ou <c>null</c>.</summary>
        public string Note { get; }
        /// <summary>Valeurs que le serveur accepte (comparées sans casse), vide quand l'argument est libre.</summary>
        public IReadOnlyList<string> Choices { get; }
        /// <summary>Argument transmis tel quel (message de <c>.all</c>) : ses espaces ne sont pas normalisés.</summary>
        public bool FreeText { get; }
        /// <summary>
        /// Refusée par StarLoco en combat (<c>player.getFight() != null</c>), souvent sans réponse : le bot ne l'envoie pas
        /// pendant un combat.
        /// </summary>
        public bool OutOfFight { get; }
        public bool HasArgument => Argument != null;
        /// <summary>Syntaxe affichée : « .level &lt;niveau&gt; », « .house [all] ».</summary>
        public string Usage => "." + Name + (Argument == null ? string.Empty : " " + (ArgumentRequired ? Argument : "[" + Argument + "]"));

        /// <summary>
        /// Ligne envoyée dans le canal général : « .nom » ou « .nom argument ». Les espaces d'un argument structuré sont
        /// ramenés à un seul, car StarLoco lit les valeurs à une position fixe (<c>msg.substring(12, …)</c> pour <c>.boost vita</c>).
        /// </summary>
        public string BuildLine(string argument)
        {
            string value = Normalize(argument);
            return value.Length == 0 ? "." + Name : "." + Name + " " + value;
        }

        /// <summary>Raison d'un refus local (argument manquant, valeur que le serveur rejetterait), ou <c>null</c>.</summary>
        public string Validate(string argument)
        {
            string value = Normalize(argument);
            if (!HasArgument) return value.Length == 0 ? null : "." + Name + " ne prend pas d'argument.";
            if (value.Length == 0) return ArgumentRequired ? "Syntaxe : " + Usage : null;
            if (FreeText) return null;
            string[] words = value.Split(' ');
            switch (Name)
            {
                case "level":
                    return words.Length == 1 && int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out int level) && level > 0
                        ? null : "Syntaxe : " + Usage + " (nombre entier positif).";
                case "boost":
                    return words.Length == 2 && Choices.Contains(words[0], StringComparer.OrdinalIgnoreCase)
                        && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int points) && points > 0
                        ? null : "Syntaxe : " + Usage + " (caractéristique : " + string.Join(", ", Choices) + ").";
                case "exo":
                    return words.Length == 2 && Choices.Contains(words[0], StringComparer.OrdinalIgnoreCase)
                        && (words[1].Equals("pa", StringComparison.OrdinalIgnoreCase) || words[1].Equals("pm", StringComparison.OrdinalIgnoreCase))
                        ? null : "Syntaxe : " + Usage + " (emplacement : " + string.Join(", ", Choices) + ").";
                case "maitre":
                    return words.Length == 1 && words[0].Length >= 2 && words[0].IndexOfAny(new[] { '|', ';', '<', '>' }) < 0
                        ? null : "Syntaxe : " + Usage + " (nom d'un membre du groupe, sans espace).";
            }
            if (Choices.Count > 0 && !(words.Length == 1 && Choices.Contains(words[0], StringComparer.OrdinalIgnoreCase)))
                return "Valeur refusée par le serveur : " + value + " (choix : " + string.Join(", ", Choices) + ").";
            return null;
        }

        private string Normalize(string argument)
        {
            string value = (argument ?? string.Empty).Trim();
            return FreeText ? value : Regex.Replace(value, @"\s+", " ");
        }

        public override string ToString() => Usage;
    }

    /// <summary>
    /// Table d'aide des commandes joueur de StarLoco (<c>CommandPlayer.java</c> du kit, lignes 43 à 2559) et envoi dans le
    /// canal général. Le serveur intercepte toute ligne « .mot » du canal <c>*</c> avant de la diffuser
    /// (<c>GameClient.tchat</c>) et répond par <c>cs&lt;font color=…&gt;texte&lt;/font&gt;</c>, lu par <see cref="ChatService"/> ;
    /// une commande inconnue (<c>.commandes</c>, <c>.x</c>) renvoie sa propre liste d'aide. Aucune commande ne part seule :
    /// chaque envoi vient d'une action de l'utilisateur.
    /// </summary>
    public static class ServerCommands
    {
        /// <summary>Commande inconnue du serveur, à laquelle il répond par sa liste d'aide (<c>Lang.get(player, 12)</c>).</summary>
        public const string ServerHelp = "commandes";

        private static readonly string[] Slots = { "coiffe", "cape", "ceinture", "bottes", "amulette", "anneauG", "anneauD", "cac" };
        private const string NoFightNoPrison = "Hors combat et hors prison ; sinon StarLoco ne répond rien.";
        private static readonly ServerCommand[] commands =
        {
            new ServerCommand("infos", ServerCommandCategory.Information, "Durée de fonctionnement du serveur et nombre de joueurs connectés."),
            new ServerCommand("staff", ServerCommandCategory.Information, "Membres de l'équipe du serveur connectés et visibles."),
            new ServerCommand("house", ServerCommandCategory.Information, "Identifiant de la maison la plus proche sur la carte ; « all » liste celles de la carte.",
                "all", choices: new[] { "all" }),
            new ServerCommand("all", ServerCommandCategory.Information, "Message à tous les joueurs connectés (« ; » devient « : », « ~ », « | », « < » et « > » sont retirés).",
                "<message>", true, "Toutes les 10 s hors groupe d'administration ; refusé en prison ou après .noall.", freeText: true),
            new ServerCommand("noall", ServerCommandCategory.Information, "Active ou coupe la réception des messages .all."),
            new ServerCommand("start", ServerCommandCategory.Teleportation, "Téléporte sur la carte de départ du serveur.", note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("poutch", ServerCommandCategory.Teleportation, "Téléporte près du Poutch Ingball.", note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("phoenix", ServerCommandCategory.Teleportation, "Téléporte près d'une statue du Phénix.", note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("enclos", ServerCommandCategory.Teleportation, "Téléporte près d'un enclos.", note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("pvp", ServerCommandCategory.Teleportation, "Téléporte sur la carte JcJ du serveur.", note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("pvm", ServerCommandCategory.Teleportation, "Téléporte sur la carte JcM du serveur.", note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("deblo", ServerCommandCategory.Teleportation, "Débloque le personnage : cellule libre de la même carte.",
                note: "Seulement sur une cellule non praticable, hors combat et hors prison.", outOfFight: true),
            new ServerCommand("vie", ServerCommandCategory.Personnage, "Rend tous les points de vie."),
            new ServerCommand("level", ServerCommandCategory.Personnage, "Fixe le niveau du personnage (refusé s'il ne dépasse pas le niveau actuel).", "<niveau>", true,
                "Hors combat.", outOfFight: true),
            new ServerCommand("restat", ServerCommandCategory.Personnage, "Remet les caractéristiques à zéro et rend le capital.", note: "Hors combat.", outOfFight: true),
            new ServerCommand("boost", ServerCommandCategory.Personnage, "Dépense des points de capital dans une caractéristique.", "<caractéristique> <points>", true,
                "Hors combat ; au plus le capital disponible.", new[] { "vita", "sagesse", "force", "intel", "chance", "agi" }, outOfFight: true),
            new ServerCommand("parcho", ServerCommandCategory.Personnage, "Porte les six caractéristiques parchemin à 101.", note: "Hors combat.", outOfFight: true),
            new ServerCommand("spellmax", ServerCommandCategory.Personnage, "Monte tous les sorts au niveau 5 (6 au-delà du niveau 99)."),
            new ServerCommand("jetmax", ServerCommandCategory.Objets, "Jets maximaux sur l'objet équipé à cet emplacement.", "<emplacement>", true, "Hors combat.",
                Slots.Concat(new[] { "familier", "dofus", "bouclier", "all" }).ToArray(), outOfFight: true),
            new ServerCommand("exo", ServerCommandCategory.Objets, "Ajoute un PA ou un PM à l'objet équipé à cet emplacement.", "<emplacement> <pa|pm>", true,
                "Hors combat.", Slots, outOfFight: true),
            new ServerCommand("fmcac", ServerCommandCategory.Objets, "Change les dégâts neutres de l'arme équipée en dégâts d'un élément.", "<élément>", true,
                "Hors combat ; arme à dégâts neutres.", new[] { "air", "terre", "feu", "eau" }, outOfFight: true),
            new ServerCommand("onboard", ServerCommandCategory.Objets, "Ajoute au sac le lot d'objets de test du serveur (jets maximaux)."),
            new ServerCommand("groupe", ServerCommandCategory.Groupe, "Groupe les personnages de la même adresse IP qui ne sont pas en groupe.",
                note: NoFightNoPrison, outOfFight: true),
            new ServerCommand("maitre", ServerCommandCategory.Groupe, "Mode maître : groupe les personnages de la même IP, qui suivent ensuite le maître (le personnage nommé, sinon le chef) ; sans nom, désactive le mode s'il est actif.",
                "<personnage>", note: "Chef du groupe, hors combat et hors prison.", outOfFight: true),
            new ServerCommand("tp", ServerCommandCategory.Groupe, "Téléporte les personnages qui suivent le maître auprès de lui.",
                note: "Toutes les 5 s, hors combat et hors échange.", outOfFight: true),
            new ServerCommand("ipdrop", ServerCommandCategory.Groupe, "Attribue ou non au personnage les butins de son adresse IP."),
            new ServerCommand("pass", ServerCommandCategory.Groupe, "Passe ou non automatiquement les tours du personnage en combat."),
            new ServerCommand("banque", ServerCommandCategory.Banque, "Ouvre la banque à distance (coût d'ouverture prélevé) : le serveur répond ECK5 et EL.",
                note: "Hors combat ; refusé avec du déshonneur (Im183).", outOfFight: true),
            new ServerCommand("transfert", ServerCommandCategory.Banque, "Dépose en banque les objets sans effets du sac (ressources), sauf pierres d'âme, documents, potions et objets de quête.",
                note: "Banque ouverte, hors combat.", outOfFight: true),
            new ServerCommand("ange", ServerCommandCategory.Alignement, "Passe le personnage dans l'alignement bontarien."),
            new ServerCommand("demon", ServerCommandCategory.Alignement, "Passe le personnage dans l'alignement brâkmarien."),
            new ServerCommand("neutre", ServerCommandCategory.Alignement, "Passe le personnage dans l'alignement neutre."),
            new ServerCommand("KralaO", ServerCommandCategory.Divers, "Ouvre les portes de l'antre du Kralamour (cellules 286, 300, 315 et 328 de la carte actuelle)."),
            new ServerCommand("KralaC", ServerCommandCategory.Divers, "Ferme les portes de l'antre du Kralamour (cellules 286, 300, 315 et 328 de la carte actuelle)."),
        };

        /// <summary>Commandes connues, dans l'ordre de l'aide (par famille).</summary>
        public static IReadOnlyList<ServerCommand> All { get; } = Array.AsReadOnly(commands);

        /// <summary>Commande par son nom, avec ou sans « . », sans tenir compte de la casse.</summary>
        public static bool TryGet(string name, out ServerCommand command)
        {
            string key = (name ?? string.Empty).Trim().TrimStart('.');
            command = commands.FirstOrDefault(entry => entry.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            return command != null;
        }

        /// <summary>
        /// Envoie la commande dans le canal général (<c>BM*|.nom [argument]|</c>) après les contrôles locaux : argument valide,
        /// canal général permis par <c>AR</c>, pas en spectateur (le client écrirait alors dans le canal d'équipe, que StarLoco
        /// n'analyse pas) et canal général actif quand le serveur a annoncé les abonnements (sinon il ignore la ligne).
        /// </summary>
        public static Task<ChatResult> SendAsync(ChatService chat, ServerCommand command, string argument = null)
        {
            if (chat == null) throw new ArgumentNullException(nameof(chat));
            if (command == null) return Task.FromResult(chat.Refuse("Choisissez une commande du serveur.", false));
            string invalid = command.Validate(argument);
            if (invalid != null) return Task.FromResult(chat.Refuse(invalid, true));
            if (command.OutOfFight && IsFighting(chat))
                return Task.FromResult(chat.Refuse("." + command.Name + " : StarLoco refuse cette commande en combat.", true));
            return SendLineAsync(chat, command.BuildLine(argument));
        }

        /// <summary>Envoie une ligne « .commande » telle quelle (par exemple <c>.commandes</c>, aide du serveur).</summary>
        public static Task<ChatResult> SendLineAsync(ChatService chat, string line)
        {
            if (chat == null) throw new ArgumentNullException(nameof(chat));
            string text = (line ?? string.Empty).Trim();
            // StarLoco : « msg.charAt(0) == '.' && msg.charAt(1) != '.' », puis au moins un caractère de nom.
            if (text.Length < 2 || text[0] != '.' || text[1] == '.' || char.IsWhiteSpace(text[1]))
                return Task.FromResult(chat.Refuse("Une commande du serveur commence par « . » suivi de son nom.", true));
            string refusal = Refusal(chat);
            if (refusal != null) return Task.FromResult(chat.Refuse(refusal, true));
            return chat.SendAsync('*', text);
        }

        /// <summary>Envoie la commande inconnue <c>.commandes</c> : le serveur répond par sa propre liste d'aide.</summary>
        public static Task<ChatResult> RequestServerHelpAsync(ChatService chat) => SendLineAsync(chat, "." + ServerHelp);

        /// <summary>Vrai pendant un combat (placement compris), comme <c>player.getFight() != null</c> chez StarLoco.</summary>
        public static bool IsFighting(ChatService chat)
        {
            var account = chat?.Account;
            return (account?.Game?.Fight?.IsInFight ?? false) || (account?.IsFighting() ?? false);
        }

        private static string Refusal(ChatService chat)
        {
            var game = chat.Account?.Game;
            if (game?.Fight?.IsSpectator ?? false)
                return "En spectateur, le canal général devient celui de l'équipe : StarLoco n'y lit pas les commandes.";
            if (!chat.CanChatToAll) return "Les restrictions du personnage interdisent le canal général : commande non envoyée.";
            string subscribed = chat.SubscribedChannels;
            if (subscribed.Length > 0 && subscribed.IndexOf('*') < 0)
                return "Le canal général est désactivé : StarLoco ignore alors les commandes. Réactivez-le dans les filtres du chat.";
            return null;
        }
    }
}
