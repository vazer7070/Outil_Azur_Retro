using System;
using System.Collections.Generic;
using System.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Maps.Interactives
{
    /// <summary>
    /// Image d'un objet interactif reçue dans <c>GDF|&lt;cellule&gt;;&lt;image&gt;[;&lt;1|0&gt;]</c>, numérotée comme chez StarLoco
    /// et dans le client : 1 plein, 2 en cours d'utilisation (porte : ouverture), 3 vide (porte ouverte), 4 vide bis
    /// (porte : fermeture), 5 repousse. <see cref="Unknown"/> tant qu'aucun <c>GDF</c> n'a visé la cellule.
    /// </summary>
    public enum InteractiveState { Unknown = 0, Full = 1, InUse = 2, Empty = 3, EmptyAlternate = 4, Respawning = 5 }

    /// <summary>
    /// Objet interactif d'une cellule (couche objet 2 marquée interactive). La définition vient de <c>BotInteractives</c>
    /// (export du serveur, <see cref="InteractivesParent"/>) ; à défaut, le nom, le type et les compétences sont lus dans
    /// les textes du client (<c>interactiveobjects</c> de <see cref="LangData"/>), comme <c>getInteractiveObjectDataByGfxText</c>.
    /// </summary>
    public class Interactives
    {
        private static readonly short[] NoSkills = new short[0];

        public short gfx { get; set; }
        public Cell Cell { get; set; }
        public InteractivesParent Interactive { get; set; }
        /// <summary>
        /// Utilisable : vrai à l'arrivée sur la carte pour un objet connu, puis troisième champ de <c>GDF</c> quand il est
        /// présent (<c>setObject2Interactive</c> du client) ; un <c>GDF</c> sans ce champ (portes) ne le change pas.
        /// </summary>
        public bool IsUsable { get; set; }
        /// <summary>Dernière image reçue dans <c>GDF</c> ; 0 avant le premier (image d'origine de la carte).</summary>
        public int Frame { get; private set; }
        /// <summary>État lisible de <see cref="Frame"/>.</summary>
        public InteractiveState State => Frame >= 1 && Frame <= 5 ? (InteractiveState)Frame : InteractiveState.Unknown;

        public Interactives(short gfx_id, Cell cell)
        {
            gfx = gfx_id;
            Cell = cell;
            if (gfx_id <= 0) return;
            InteractivesParent M = InteractivesParent.ReturnByGFX(gfx_id);
            if (M != null) Interactive = M;
            IsUsable = M != null || LangData.Interactive.IdFromGfx(gfx_id).HasValue;
        }

        /// <summary>Nom de l'objet : définition du serveur, sinon texte du client, sinon « Objet interactif &lt;gfx&gt; ».</summary>
        public string Name
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Interactive?.Name)) return Interactive.Name;
                return LangData.Interactive.IdFromGfx(gfx).HasValue ? LangData.Interactive.Name(gfx) : "Objet interactif " + gfx;
            }
        }

        /// <summary>
        /// Type du client (<c>IO.d[id].t</c> : 1 ressource, 2 atelier, 3 zaap, 4 fontaine, 5 porte, 6 coffre, 7 marmite,
        /// 10 zaapi, 12 liste des artisans, 13 enclos, 14 levier, 15 statue) ; type de la définition du serveur à défaut ; 0 inconnu.
        /// </summary>
        public int ClientType
        {
            get
            {
                int? type = gfx > 0 ? LangData.Interactive.Type(gfx) : null;
                if (type.HasValue) return type.Value;
                return Interactive == null ? 0 : (int)Interactive.Type;
            }
        }

        /// <summary>Type connu de <see cref="InteractiveType"/> (les types du client sans équivalent donnent <c>Other</c>).</summary>
        public InteractiveType Type => Enum.IsDefined(typeof(InteractiveType), ClientType) ? (InteractiveType)ClientType : InteractiveType.Other;

        /// <summary>Compétences acceptées par le serveur (<c>BotInteractives</c>), sinon celles du client (<c>IO.d[id].sk</c>).</summary>
        public IReadOnlyList<short> Skills
        {
            get
            {
                if (Interactive != null && Interactive.Capacities.Length > 0) return Interactive.Capacities;
                return ClientSkills;
            }
        }

        /// <summary>Compétences du menu du client (<c>IO.d[id].sk</c>, dans l'ordre), sinon celles du serveur.</summary>
        public IReadOnlyList<short> ClientSkills
        {
            get
            {
                int[] lang = gfx > 0 ? LangData.Interactive.Skills(gfx) : new int[0];
                if (lang.Length == 0) return Interactive?.Capacities ?? NoSkills;
                return lang.Where(skill => skill > 0 && skill <= short.MaxValue).Select(skill => (short)skill).Distinct().ToArray();
            }
        }

        public bool HasSkill(short skill) => Skills.Contains(skill) || ClientSkills.Contains(skill);
        /// <summary>Zaap : gfx 7000/7026/7029/4287 (<c>GameCase.canDoAction</c> de StarLoco) ou compétence 114.</summary>
        public bool IsZaap => InteractiveGfx.IsZaap(gfx) || HasSkill(InteractiveGfx.ZaapSkill);
        /// <summary>Zaapi : gfx 7030/7031 ou compétence 157.</summary>
        public bool IsZaapi => InteractiveGfx.IsZaapi(gfx) || HasSkill(InteractiveGfx.ZaapiSkill);

        /// <summary>Applique un triplet <c>GDF</c> de la cellule.</summary>
        public void ApplyState(InteractiveObjectState state)
        {
            if (state == null) return;
            Frame = state.State;
            if (state.Interactive.HasValue) IsUsable = state.Interactive.Value;
        }
    }
}
