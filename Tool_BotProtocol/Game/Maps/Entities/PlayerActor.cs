using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Joueur d'une carte : <c>GM|+cell;dir;0;id;nom;classe[,titre];gfx^taille;sexe;alignement;c1;c2;c3;stuff;aura;émote;
    /// minuteur;guilde;emblème;restrictions;monture</c> hors combat (<c>Player.parseToGM</c>, <c>Game.onMovement</c>),
    /// <c>…;sexe;niveau;alignement;c1;c2;c3;stuff;PV;PA;PM;résistances×7;équipe;monture</c> en combat (<c>Fighter.getGmPacket</c>).
    /// </summary>
    public sealed class PlayerActor : Personnages
    {
        public override ActorKind Kind => ActorKind.Player;
        /// <summary>Numéro de classe (champ type) ; <see cref="Personnages.Race_ID"/> le reprend sur un octet.</summary>
        public int ClassId { get; set; }
        /// <summary>Titre placé après la virgule du champ type (<c>classe,titre</c> chez StarLoco, <c>titre*paramètre</c> dans le client).</summary>
        public int TitleId { get; set; }
        public string TitleParameter { get; set; }
        public ActorAlignment Alignment { get; set; } = new ActorAlignment();
        /// <summary>Niveau : transmis seulement en combat.</summary>
        public int? Level { get; set; }
        /// <summary>Couleurs en hexadécimal telles que reçues (<c>-1</c> = couleur par défaut).</summary>
        public string Color1 { get; set; } = "-1";
        public string Color2 { get; set; } = "-1";
        public string Color3 { get; set; } = "-1";
        /// <summary>Champ « stuff » brut (<c>arme,coiffe,cape,familier,bouclier</c> en hexadécimal) ; mis à jour par <c>Oa</c>.</summary>
        public string Stuff { get; set; } = string.Empty;
        public IReadOnlyList<ActorAccessory> Accessories { get; set; } = new ActorAccessory[0];
        /// <summary>Aura : 0 aucune, 1 niveau 100, 2 niveau 200 (StarLoco), 3 aura d'objet.</summary>
        public int Aura { get; set; }
        public string Emote { get; set; } = string.Empty;
        public string EmoteTimer { get; set; } = string.Empty;
        public string GuildName { get; set; } = string.Empty;
        public string GuildEmblem { get; set; } = string.Empty;
        /// <summary>Restrictions du sprite (décimal : bit 1 non agressable, bit 2 non défiable…), texte brut conservé.</summary>
        public string RestrictionsRaw { get; set; } = string.Empty;
        public int? Restrictions { get; set; }
        /// <summary>Monture : <c>modèle,c1,c2,c3</c> (hexadécimal) ou <c>modèle</c> ; vide sans monture.</summary>
        public string MountRaw { get; set; } = string.Empty;
        public int? MountModelId { get; set; }
        public int? Life { get; set; }
        public int? ActionPoints { get; set; }
        public int? MovementPoints { get; set; }
        public int[] Resistances { get; set; }
        public int? Team { get; set; }
        /// <summary>Vrai si l'entrée a été lue avec la disposition des champs de combat.</summary>
        public bool InFight { get; set; }

        public bool HasGuild => !string.IsNullOrEmpty(GuildName);
        public bool HasMount => !string.IsNullOrEmpty(MountRaw);
    }

    /// <summary>Champ alignement de <c>GM</c> : <c>côté,valeur,grade,niveau+id[,déchu]</c>.</summary>
    public sealed class ActorAlignment
    {
        public int Side { get; set; }
        public int Value { get; set; }
        /// <summary>Grade affiché par les ailes (0 lorsque le joueur les cache).</summary>
        public int Grade { get; set; }
        /// <summary>Quatrième valeur : niveau + identifiant du joueur, utilisée par le client pour estimer le gain JcJ.</summary>
        public long LevelCode { get; set; }
        public bool FallenAngelDemon { get; set; }

        public static ActorAlignment Parse(string value)
        {
            var alignment = new ActorAlignment();
            if (string.IsNullOrEmpty(value)) return alignment;
            string[] parts = value.Split(',');
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int side)) alignment.Side = side;
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) alignment.Value = parsed;
            if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade)) alignment.Grade = grade;
            if (parts.Length > 3 && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long code)) alignment.LevelCode = code;
            alignment.FallenAngelDemon = parts.Length > 4 && parts[4] == "1";
            return alignment;
        }
    }
}
