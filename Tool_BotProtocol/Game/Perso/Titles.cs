using System;
using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Perso
{
    /// <summary>
    /// Titres des personnages (lot F10) : textes et couleurs de <c>titles_fr</c> (<c>PT[id] = {t, c, pt}</c>), lus comme la classe
    /// de titre du client 1.34 : <c>« t »</c>, <c>%1</c> remplacé par le paramètre (nom du monstre quand <c>pt</c> vaut 1), couleur
    /// <c>c</c>. Le titre d'un joueur vient du champ type de son <c>GM</c> (<see cref="PlayerActor.TitleId"/>, lot S2) ; StarLoco
    /// n'envoie aucun paramètre et n'offre aucun paquet de choix de titre. Le sous-titre au-dessus des personnages reste à
    /// <c>OverheadLayer</c> (lot M1) ; ces accesseurs servent la fiche du personnage. Sans textes du client, les replis sont
    /// « Titre n° X » et le blanc.
    /// </summary>
    public static class Titles
    {
        /// <summary>Blanc (<c>0xFFFFFF</c>), couleur de la plupart des titres et repli quand <c>PT[id].c</c> manque.</summary>
        public const int DefaultColor = 0xFFFFFF;

        /// <summary>Vrai pour un titre attribué (identifiant strictement positif).</summary>
        public static bool IsTitle(int id) => id > 0;

        /// <summary>Vrai si les textes du client décrivent ce titre.</summary>
        public static bool IsKnown(int id) => IsTitle(id) && Text(id) != null;

        /// <summary>
        /// Texte affiché du titre, guillemets compris, comme le client ; « Titre n° X » entre guillemets si les textes l'ignorent,
        /// chaîne vide pour 0 (aucun titre).
        /// </summary>
        public static string Name(int id, string parameter = null)
        {
            if (!IsTitle(id)) return string.Empty;
            if (IsKnown(id))
            {
                try { return LangData.Title.Name(id, parameter); }
                catch (Exception) { /* Textes en cours de rechargement : repli ci-dessous. */ }
            }
            return "« Titre n° " + id.ToString(CultureInfo.InvariantCulture) + " »";
        }

        /// <summary>Couleur RVB du titre (<c>PT[id].c</c>), <see cref="DefaultColor"/> si elle manque ou si le titre est inconnu.</summary>
        public static int Color(int id)
        {
            if (!IsTitle(id)) return DefaultColor;
            try
            {
                int? color = LangData.Title.Color(id);
                return color.HasValue && color.Value >= 0 && color.Value <= 0xFFFFFF ? color.Value : DefaultColor;
            }
            catch (Exception) { return DefaultColor; }
        }

        /// <summary>Type du paramètre (<c>PT[id].pt</c>) : 0 texte, 1 identifiant de monstre ; <c>null</c> si inconnu.</summary>
        public static int? ParameterType(int id)
        {
            IReadOnlyDictionary<string, string> raw = Raw(id);
            return raw != null && raw.TryGetValue("typeParametre", out string value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) ? type : (int?)null;
        }

        /// <summary>Titre porté par un joueur de la carte (<c>null</c> sans titre).</summary>
        public static PlayerTitle Of(PlayerActor player)
            => player == null || !IsTitle(player.TitleId) ? null : new PlayerTitle(player.TitleId, player.TitleParameter);

        private static string Text(int id)
        {
            IReadOnlyDictionary<string, string> raw = Raw(id);
            return raw != null && raw.TryGetValue("texte", out string text) && !string.IsNullOrEmpty(text) ? text : null;
        }

        private static IReadOnlyDictionary<string, string> Raw(int id)
        {
            try { return LangData.Raw("titles", "titre", id.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Titre d'un joueur : identifiant, paramètre éventuel, texte et couleur résolus à la lecture.</summary>
    public sealed class PlayerTitle
    {
        public int Id { get; }
        public string Parameter { get; }
        public string Name => Titles.Name(Id, Parameter);
        public int Color => Titles.Color(Id);
        public bool IsKnown => Titles.IsKnown(Id);
        public PlayerTitle(int id, string parameter = null) { Id = id; Parameter = parameter; }
    }
}
