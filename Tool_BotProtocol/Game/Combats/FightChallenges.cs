using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>État d'un défi (<c>challenge.state</c> du client 1.34).</summary>
    public enum FightChallengeState
    {
        /// <summary><c>0</c> : en cours (<c>CURRENT_FIGHT_CHALLENGE</c>).</summary>
        Running = 0,
        /// <summary><c>1</c> : réussi (<c>GdOK</c>, <c>FIGHT_CHALLENGE_DONE</c>, symbole <c>ChallengeOK</c>).</summary>
        Succeeded = 1,
        /// <summary><c>2</c> : raté (<c>GdKO</c>, <c>FIGHT_CHALLENGE_FAILED</c>, symbole <c>ChallengeKO</c>).</summary>
        Failed = 2
    }

    /// <summary>
    /// Défi d'un combat contre des monstres, reçu au début du combat par
    /// <c>Gd&lt;type&gt;;&lt;1|0&gt;;&lt;cible&gt;;&lt;xp de base&gt;;&lt;xp de groupe&gt;;&lt;butin de base&gt;;&lt;butin de groupe&gt;</c>
    /// (<c>Challenge.parseToPacket</c> de StarLoco, <c>Game.onFightChallenge</c> du client). Instance figée : un changement
    /// d'état produit une nouvelle instance. Le nom, la description (cible « nom (Niv. n) » à la place de <c>%1</c>) et le
    /// numéro d'icône (<c>clips/challenges/&lt;g&gt;.swf</c>) viennent de <c>fightChallenge.xml</c>, lus à la réception comme
    /// le constructeur du client.
    /// </summary>
    public sealed class FightChallenge
    {
        private FightChallenge() { }

        /// <summary>Type du défi (<c>FC[id]</c> de <c>fightChallenge.xml</c>).</summary>
        public int Id { get; private set; }
        /// <summary>Deuxième champ à 1 : le défi désigne une cible (bouton « voir la cible », <c>Gdi&lt;type&gt;</c>).</summary>
        public bool ShowTarget { get; private set; }
        /// <summary>Combattant visé (troisième champ) ; null s'il est vide, illisible ou nul, comme <c>!targetId</c> dans le client.</summary>
        public int? TargetId { get; private set; }
        public int BasicXpBonus { get; private set; }
        /// <summary>Bonus de groupe d'expérience : StarLoco envoie toujours 0 (le bonus de groupe est déjà ajouté au bonus de base).</summary>
        public int TeamXpBonus { get; private set; }
        public int BasicDropBonus { get; private set; }
        /// <summary>Bonus de groupe de butin : toujours 0 chez StarLoco.</summary>
        public int TeamDropBonus { get; private set; }
        /// <summary>Bonus d'expérience affiché par le client (<c>teamXpBonus + basicXpBonus</c>), en pour cent.</summary>
        public int XpBonus => BasicXpBonus + TeamXpBonus;
        /// <summary>Bonus de butin affiché par le client (<c>teamDropBonus + basicDropBonus</c>), en pour cent.</summary>
        public int DropBonus => BasicDropBonus + TeamDropBonus;
        public FightChallengeState State { get; private set; }
        /// <summary>Nom du défi (<c>n</c>) ; « Challenge &lt;type&gt; » si les textes du client manquent.</summary>
        public string Name { get; private set; } = string.Empty;
        /// <summary>Description (<c>d</c>) avec la cible à la place de <c>%1</c> quand elle est connue ; vide sans les textes.</summary>
        public string Description { get; private set; } = string.Empty;
        /// <summary>Icône du défi (<c>g</c>) : <c>clips/challenges/&lt;g&gt;.swf</c> du client ; null sans les textes.</summary>
        public int? Gfx { get; private set; }
        /// <summary>Contenu de <c>Gd</c> sans le préfixe, pour le diagnostic.</summary>
        public string Raw { get; private set; } = string.Empty;

        /// <summary>Texte d'état du client : <c>CURRENT_FIGHT_CHALLENGE</c>, <c>FIGHT_CHALLENGE_DONE</c> ou <c>FIGHT_CHALLENGE_FAILED</c>.</summary>
        public string StateText => StateTextOf(State);
        /// <summary>
        /// Ligne du chat (canal infos) d'un changement d'état, comme <c>Game.onFightChallengeUpdate</c> :
        /// « Challenge réussi : &lt;nom&gt; » ou « Challenge raté : &lt;nom&gt; » ; vide pour un défi en cours.
        /// </summary>
        public string ResultMessage => State == FightChallengeState.Running ? string.Empty : StateText + " : " + Name;

        public static string StateTextOf(FightChallengeState state)
        {
            switch (state)
            {
                case FightChallengeState.Succeeded: return TextOr("FIGHT_CHALLENGE_DONE", "Challenge réussi");
                case FightChallengeState.Failed: return TextOr("FIGHT_CHALLENGE_FAILED", "Challenge raté");
                default: return TextOr("CURRENT_FIGHT_CHALLENGE", "Challenge en cours");
            }
        }

        /// <summary>
        /// Lit le contenu de <c>Gd</c> sans le préfixe. Les sept premiers champs sont lus ; un huitième (StarLoco répète le
        /// type quand le défi est déjà terminé) est ignoré comme dans le client, qui ne passe que sept valeurs au constructeur.
        /// Renvoie null si le type ou un bonus est illisible ou s'il manque des champs.
        /// </summary>
        public static FightChallenge Parse(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            string[] fields = payload.Split(';');
            if (fields.Length < 7) return null;
            if (!TryInt(fields[0], out int id) || !TryInt(fields[3], out int basicXp) || !TryInt(fields[4], out int teamXp)
                || !TryInt(fields[5], out int basicDrop) || !TryInt(fields[6], out int teamDrop)) return null;
            int? target = TryInt(fields[2], out int targetId) && targetId != 0 ? targetId : (int?)null;
            var challenge = new FightChallenge
            {
                Id = id, ShowTarget = fields[1] == "1", TargetId = target, BasicXpBonus = basicXp, TeamXpBonus = teamXp,
                BasicDropBonus = basicDrop, TeamDropBonus = teamDrop, State = FightChallengeState.Running, Raw = payload
            };
            challenge.Name = DefaultName(id);
            return challenge;
        }

        /// <summary>
        /// Lit <c>OK&lt;type&gt;</c> ou <c>KO&lt;type&gt;</c> (contenu de <c>GdOK</c>/<c>GdKO</c> sans « Gd ») : le client teste le
        /// quatrième caractère du paquet, <c>K</c> pour réussi et <c>O</c> pour raté. Faux si ce n'est pas une mise à jour
        /// (<paramref name="isUpdate"/> faux : contenu d'un <c>Gd</c> d'ajout) ou si le type est illisible.
        /// </summary>
        public static bool TryParseUpdate(string payload, out bool isUpdate, out int id, out bool success)
        {
            id = 0; success = false; isUpdate = false;
            if (payload == null || payload.Length < 2 || (payload[1] != 'K' && payload[1] != 'O')) return false;
            isUpdate = true;
            success = payload[1] == 'K';
            return TryInt(payload.Substring(2), out id);
        }

        internal FightChallenge WithState(FightChallengeState state)
        {
            FightChallenge copy = (FightChallenge)MemberwiseClone();
            copy.State = state;
            return copy;
        }

        internal FightChallenge Described(string name, string description, int? gfx)
        {
            FightChallenge copy = (FightChallenge)MemberwiseClone();
            copy.Name = string.IsNullOrEmpty(name) ? DefaultName(Id) : name;
            copy.Description = description ?? string.Empty;
            copy.Gfx = gfx;
            return copy;
        }

        /// <summary>Défi inconnu de la liste qui change d'état (le client affiche quand même la ligne du chat).</summary>
        internal static FightChallenge Unknown(int id, FightChallengeState state)
        {
            var challenge = new FightChallenge { Id = id, State = state, Raw = string.Empty };
            return challenge.Described(LangName(id), null, LangData.IsLoaded("fightChallenge") ? LangData.Challenge.Gfx(id) : null);
        }

        internal static string LangName(int id) => LangData.IsLoaded("fightChallenge") && LangData.Challenge.Has(id) ? LangData.Challenge.Name(id) : null;
        private static string DefaultName(int id) => "Challenge " + id.ToString(CultureInfo.InvariantCulture);
        internal static string TextOr(string key, string fallback) => LangData.IsLoaded("lang") && LangData.Text.Has(key) ? LangData.Text.Get(key) : fallback;
        private static bool TryInt(string text, out int value) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public sealed partial class Fights
    {
        /// <summary>Délai minimal entre deux demandes « voir la cible » (<c>_lastShowAsk</c> de <c>FightChallengeViewer</c> : 1000 ms).</summary>
        public const int ChallengeTargetDelayMilliseconds = 1000;
        private readonly List<FightChallenge> challenges = new List<FightChallenge>();

        /// <summary>
        /// Défis du combat en cours dans l'ordre de réception (<c>Gd</c>) ; vidés à la fin du combat (copiés dans
        /// <see cref="FightResult.Challenges"/> par <c>GE</c>), à la sortie (<c>GV</c>) et au combat suivant.
        /// </summary>
        public IReadOnlyList<FightChallenge> Challenges { get { lock (sync) return challenges.ToArray(); } }

        /// <summary>
        /// Défi ajouté (<c>Gd</c>) ou changé d'état (<c>GdOK</c>/<c>GdKO</c>), levé hors du verrou sur le fil réseau. Pour un
        /// changement d'état, <see cref="FightChallenge.ResultMessage"/> est la ligne que le client écrit dans le chat.
        /// </summary>
        public event Action<FightChallenge> ChallengeChanged;

        /// <summary>
        /// <c>Gd…</c> (contenu sans « Gd ») : aiguillage du client sur le quatrième caractère du paquet — <c>K</c> réussi,
        /// <c>O</c> raté, sinon ajout d'un défi. Un paquet illisible est journalisé et ignoré.
        /// </summary>
        internal void ApplyChallengePacket(string payload)
        {
            if (FightChallenge.TryParseUpdate(payload, out bool isUpdate, out int id, out bool success))
            {
                UpdateChallenge(id, success);
                return;
            }
            if (isUpdate) { Malformed("Gd", payload); return; }
            FightChallenge parsed = FightChallenge.Parse(payload);
            if (parsed == null) { Malformed("Gd", payload); return; }
            AddChallenge(parsed);
        }

        /// <summary>
        /// Ajoute un défi. Un second <c>Gd</c> du même type remplace le premier au lieu de l'empiler comme le client (choix du bot,
        /// docs/BOT_STARLOCO.md) ; ignoré hors combat.
        /// </summary>
        private void AddChallenge(FightChallenge parsed)
        {
            FightChallenge described = Describe(parsed);
            lock (sync)
            {
                if (disposed || !InFight) return;
                int index = challenges.FindIndex(c => c.Id == described.Id);
                if (index >= 0) challenges[index] = described; else challenges.Add(described);
            }
            account.Logger?.LogInfo("COMBAT", "Challenge : " + described.Name + " (XP +" + described.XpBonus + " %, butin +" + described.DropBonus + " %).");
            ChallengeChanged?.Invoke(described);
            Changed();
        }

        /// <summary>
        /// <c>GdOK&lt;type&gt;</c> / <c>GdKO&lt;type&gt;</c> : nouvel état du défi. Un type absent de la liste ne change rien à la liste,
        /// mais l'événement est levé pour la ligne du chat, que le client écrit dans tous les cas.
        /// </summary>
        private void UpdateChallenge(int id, bool success)
        {
            FightChallengeState state = success ? FightChallengeState.Succeeded : FightChallengeState.Failed;
            FightChallenge updated;
            lock (sync)
            {
                if (disposed || !InFight) return;
                int index = challenges.FindIndex(c => c.Id == id);
                if (index >= 0) challenges[index] = updated = challenges[index].WithState(state);
                else updated = null;
            }
            if (updated == null) updated = FightChallenge.Unknown(id, state);
            account.Logger?.LogInfo("COMBAT", updated.ResultMessage);
            ChallengeChanged?.Invoke(updated);
            Changed();
        }

        /// <summary>
        /// Nom, description et icône d'après <c>fightChallenge.xml</c> ; <c>%1</c> devient « nom (Niv. n) » de la cible comme dans
        /// le constructeur du client (<c>Sprites.getItemAt(targetId).name</c>, <c>LEVEL_SMALL</c>, niveau du sprite).
        /// </summary>
        private FightChallenge Describe(FightChallenge challenge)
        {
            if (!LangData.IsLoaded("fightChallenge") || !LangData.Challenge.Has(challenge.Id)) return challenge;
            string description = LangData.Challenge.Description(challenge.Id) ?? string.Empty;
            if (challenge.TargetId.HasValue)
            {
                int target = challenge.TargetId.Value;
                int? level = LevelOf(target);
                string shown = NameOf(target) + " (" + FightChallenge.TextOr("LEVEL_SMALL", "Niv.") + " "
                    + (level.HasValue ? level.Value.ToString(CultureInfo.InvariantCulture) : "?") + ")";
                description = description.Replace("%1", shown);
            }
            return challenge.Described(LangData.Challenge.Name(challenge.Id), description, LangData.Challenge.Gfx(challenge.Id));
        }

        /// <summary>Niveau d'un combattant d'après son acteur sur la carte (monstre, joueur dont le <c>GM</c> de combat porte le niveau, percepteur).</summary>
        private int? LevelOf(int id)
        {
            MapActor actor = account.Game.Map.GetActor(id);
            if (actor is Monstres.Monstres monster && monster.Level > 0) return monster.Level;
            if (actor is PlayerActor player) return player.Level;
            if (actor is CollectorActor collector && collector.Level > 0) return collector.Level;
            return null;
        }

        /// <summary>
        /// <c>Gdi&lt;type&gt;</c> : demande au serveur de montrer la cible du défi (bouton <c>_btnView</c> de <c>FightChallengeViewer</c>,
        /// présent si <see cref="FightChallenge.ShowTarget"/>). StarLoco répond par <c>Gf&lt;cible&gt;|&lt;cellule&gt;</c> au seul
        /// demandeur, si le défi est encore en cours et sa cible sur la carte ; la réponse est dessinée comme un drapeau.
        /// </summary>
        public Task<CombatActionResult> ShowChallengeTargetAsync(int challengeId)
        {
            return SendDirectAsync("Gdi" + challengeId.ToString(CultureInfo.InvariantCulture), () => ChallengeTargetUnavailable(challengeId),
                () => "Cible du challenge demandée.");
        }
        public string GetChallengeTargetUnavailableReason(int challengeId) { lock (sync) return ChallengeTargetUnavailable(challengeId); }
        private string ChallengeTargetUnavailable(int challengeId)
        {
            string reason = Disconnected();
            if (reason != null) return reason;
            FightChallenge challenge = challenges.Find(c => c.Id == challengeId);
            if (challenge == null) return "Ce challenge n’est pas en cours.";
            return challenge.ShowTarget ? null : "Ce challenge ne désigne pas de cible.";
        }
    }
}
