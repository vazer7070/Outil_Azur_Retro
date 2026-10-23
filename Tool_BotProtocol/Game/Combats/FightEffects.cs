using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>Origine d'un effet de combat.</summary>
    public enum FightEffectSource
    {
        /// <summary><c>GIE</c> (<c>Game.onEffect</c> → <c>EffectsManager</c> du client : icônes du volet <c>Buff</c>).</summary>
        EffectPacket,
        /// <summary><c>GA</c> de caractéristique (<c>&lt;cible&gt;,&lt;valeur&gt;,&lt;durée&gt;</c>, <c>CharacteristicsManager</c> du client).</summary>
        GameAction
    }

    /// <summary>
    /// Effet temporaire porté par un combattant (envoûtement, bonus ou malus de caractéristique, changement d'apparence).
    /// Champs du constructeur <c>Effect(lanceur, type, param1, param2, param3, param4, toursRestants, sort)</c> du client 1.34.
    /// </summary>
    public sealed class FightEffect
    {
        internal FightEffect(FightEffectSource source, int targetId, int effectId, int? param1, int? param2, int? param3,
            string param4, int remainingTurns, int spellId, string casterId)
        {
            Source = source; TargetId = targetId; EffectId = effectId; Param1 = param1; Param2 = param2; Param3 = param3;
            Param4 = param4 ?? string.Empty; RemainingTurns = remainingTurns; SpellId = spellId; CasterId = casterId ?? string.Empty;
        }

        public FightEffectSource Source { get; }
        public int TargetId { get; }
        /// <summary>Numéro d'effet (<c>GIE</c>) ou numéro d'action <c>GA</c> (116 portée, 117 PO…).</summary>
        public int EffectId { get; }
        /// <summary>Valeur principale ; null si le serveur n'a pas envoyé de nombre.</summary>
        public int? Param1 { get; internal set; }
        public int? Param2 { get; }
        public int? Param3 { get; }
        public string Param4 { get; }
        /// <summary>Tours restants ; -1 = sans fin. Décrémenté quand commence le tour qui suit celui du porteur (<c>cleanPlayer</c>).</summary>
        public int RemainingTurns { get; internal set; }
        public int SpellId { get; }
        /// <summary>Lanceur (9ᵉ champ facultatif de <c>GIE</c>, jamais envoyé par StarLoco) ; vide si inconnu.</summary>
        public string CasterId { get; }

        internal FightEffect Copy() => (FightEffect)MemberwiseClone();
    }

    /// <summary>Zone colorée posée au sol (glyphe, piège visible) : <c>GDZ+&lt;cellule&gt;;&lt;taille&gt;;&lt;couleur&gt;</c>.</summary>
    public sealed class FightZone
    {
        internal FightZone(int cellId, int size, int color) { CellId = cellId; Size = size; Color = color; }
        public int CellId { get; }
        /// <summary>Rayon de la zone en cellules (0 = la cellule seule).</summary>
        public int Size { get; }
        /// <summary>Indice de couleur (<c>ZONE_COLOR</c> du client).</summary>
        public int Color { get; }
    }

    /// <summary>Changement de zone lu dans <c>GDZ</c> : <see cref="Visible"/> vrai pour <c>+</c>, faux pour <c>-</c>.</summary>
    public sealed class FightZoneChange
    {
        internal FightZoneChange(bool visible, FightZone zone) { Visible = visible; Zone = zone; }
        public bool Visible { get; }
        public FightZone Zone { get; }
    }

    /// <summary>
    /// Options d'une équipe (<c>Go±&lt;option&gt;&lt;équipe&gt;</c>, <c>Game.onFightOption</c>) et demandes correspondantes
    /// (<c>Fights.blockJoiner/blockSpectators/blockJoinerExceptParty/needHelp</c> du client : <c>fN</c>, <c>fS</c>, <c>fP</c>, <c>fH</c>).
    /// </summary>
    [Flags]
    public enum FightOptions
    {
        None = 0,
        /// <summary>Lettre <c>A</c>, demande <c>fN</c> : combat fermé aux nouveaux arrivants.</summary>
        BlockJoiner = 1,
        /// <summary>Lettre <c>S</c>, demande <c>fS</c> : spectateurs refusés.</summary>
        BlockSpectators = 2,
        /// <summary>Lettre <c>P</c>, demande <c>fP</c> : seuls les membres du groupe peuvent rejoindre.</summary>
        PartyOnly = 4,
        /// <summary>Lettre <c>H</c>, demande <c>fH</c> : demande d'aide.</summary>
        NeedHelp = 8
    }

    /// <summary>Cellule signalée par un combattant (<c>Gf&lt;combattant&gt;|&lt;cellule&gt;</c>, drapeau <c>flag.swf</c> du client).</summary>
    public sealed class FightFlag
    {
        internal FightFlag(int actorId, int cellId, DateTime receivedUtc) { ActorId = actorId; CellId = cellId; ReceivedUtc = receivedUtc; }
        public int ActorId { get; }
        public int CellId { get; }
        public DateTime ReceivedUtc { get; }
    }

    /// <summary>Ligne du journal de combat (équivalent des messages <c>INFO_FIGHT_CHAT</c> du client), en français.</summary>
    public sealed class FightLogEntry
    {
        internal FightLogEntry(int actionId, int actorId, int targetId, string text)
        {
            TimeUtc = DateTime.UtcNow; ActionId = actionId; ActorId = actorId; TargetId = targetId; Text = text ?? string.Empty;
        }
        public DateTime TimeUtc { get; }
        /// <summary>Action <c>GA</c> d'origine, ou 0 pour un autre paquet (<c>Gf</c>…).</summary>
        public int ActionId { get; }
        public int ActorId { get; }
        public int TargetId { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>Lecture de <c>GIE</c>, <c>GDZ</c>, <c>Go</c> et <c>Gf</c> ; aucune méthode ne lève d'exception sur un paquet mal formé.</summary>
    public static class FightEffectPackets
    {
        /// <summary>
        /// <c>GIE&lt;effet&gt;;&lt;cibles séparées par ,&gt;;&lt;p1&gt;;&lt;p2&gt;;&lt;p3&gt;;&lt;p4&gt;;&lt;tours&gt;;&lt;sort&gt;[;&lt;lanceur&gt;]</c>
        /// (<c>SocketManager.GAME_SEND_FIGHT_GIE_TO_FIGHT</c> : 8 champs, une seule cible). <paramref name="payload"/> est le paquet sans « GIE ».
        /// Un effet par cible ; liste vide si l'effet ou une cible est illisible.
        /// </summary>
        public static List<FightEffect> ParseEffect(string payload)
        {
            var effects = new List<FightEffect>();
            if (string.IsNullOrEmpty(payload)) return effects;
            string[] fields = payload.Split(';');
            if (fields.Length < 7 || !TryInt(fields[0], out int effectId)) return effects;
            int remaining = TryInt(fields[6], out int turns) ? turns : -1;
            int spellId = fields.Length > 7 && TryInt(fields[7], out int spell) ? spell : 0;
            string caster = fields.Length > 8 ? fields[8] : string.Empty;
            var targets = new List<int>();
            foreach (string entry in fields[1].Split(','))
            {
                if (entry.Length == 0) continue;
                if (!TryInt(entry, out int target)) return effects;
                targets.Add(target);
            }
            foreach (int target in targets)
                effects.Add(new FightEffect(FightEffectSource.EffectPacket, target, effectId, OptionalInt(fields[2]), OptionalInt(fields[3]),
                    OptionalInt(fields[4]), fields[5], remaining, spellId, caster));
            return effects;
        }

        /// <summary>
        /// <c>GDZ±&lt;cellule&gt;;&lt;taille&gt;;&lt;couleur&gt;[|±…]</c> (<c>Game.onZoneData</c> ; StarLoco : <c>Trap</c>, <c>Glyph</c>, <c>SpellEffect</c>,
        /// souvent encapsulé dans <c>GA;999</c>). <paramref name="payload"/> est le paquet sans « GDZ ». Les entrées illisibles sont ignorées.
        /// </summary>
        public static List<FightZoneChange> ParseZones(string payload)
        {
            var changes = new List<FightZoneChange>();
            if (string.IsNullOrEmpty(payload)) return changes;
            foreach (string entry in payload.Split('|'))
            {
                if (entry.Length < 2 || (entry[0] != '+' && entry[0] != '-')) continue;
                string[] fields = entry.Substring(1).Split(';');
                if (fields.Length < 3 || !TryInt(fields[0], out int cell) || cell < 0
                    || !TryInt(fields[1], out int size) || !TryInt(fields[2], out int color)) continue;
                changes.Add(new FightZoneChange(entry[0] == '+', new FightZone(cell, size, color)));
            }
            return changes;
        }

        /// <summary>
        /// <c>Go&lt;+|-&gt;&lt;A|S|P|H&gt;&lt;équipe&gt;</c> (<c>SocketManager.GAME_SEND_FIGHT_CHANGE_OPTION_PACKET_TO_MAP</c> ; l'équipe est
        /// l'identifiant de son initiateur). <paramref name="payload"/> est le paquet sans « Go ».
        /// </summary>
        public static bool TryParseOption(string payload, out bool enabled, out FightOptions option, out long teamId)
        {
            enabled = false; option = FightOptions.None; teamId = 0;
            if (payload == null || payload.Length < 3 || (payload[0] != '+' && payload[0] != '-')) return false;
            option = OptionFromLetter(payload[1]);
            if (option == FightOptions.None
                || !long.TryParse(payload.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out teamId))
            {
                option = FightOptions.None; teamId = 0;
                return false;
            }
            enabled = payload[0] == '+';
            return true;
        }

        /// <summary><c>Gf&lt;combattant&gt;|&lt;cellule&gt;</c> (<c>SocketManager.GAME_SEND_FIGHT_SHOW_CASE</c>). <paramref name="payload"/> est le paquet sans « Gf ».</summary>
        public static bool TryParseFlag(string payload, out int actorId, out int cellId)
        {
            actorId = 0; cellId = -1;
            if (string.IsNullOrEmpty(payload)) return false;
            string[] fields = payload.Split('|');
            if (fields.Length < 2 || !TryInt(fields[0], out actorId) || !TryInt(fields[1], out cellId) || cellId < 0)
            {
                actorId = 0; cellId = -1;
                return false;
            }
            return true;
        }

        /// <summary>Lettre de <c>Go</c> (A, S, P, H) → option ; <see cref="FightOptions.None"/> sinon.</summary>
        public static FightOptions OptionFromLetter(char letter)
        {
            switch (letter)
            {
                case 'A': return FightOptions.BlockJoiner;
                case 'S': return FightOptions.BlockSpectators;
                case 'P': return FightOptions.PartyOnly;
                case 'H': return FightOptions.NeedHelp;
                default: return FightOptions.None;
            }
        }

        /// <summary>Paquet envoyé par le client pour basculer une option : <c>fN</c>, <c>fS</c>, <c>fP</c> ou <c>fH</c> ; null si l'option n'est pas unique.</summary>
        public static string RequestFor(FightOptions option)
        {
            switch (option)
            {
                case FightOptions.BlockJoiner: return "fN";
                case FightOptions.BlockSpectators: return "fS";
                case FightOptions.PartyOnly: return "fP";
                case FightOptions.NeedHelp: return "fH";
                default: return null;
            }
        }

        internal static bool TryInt(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        internal static int? OptionalInt(string text) => TryInt(text, out int value) ? value : (int?)null;
    }
}
