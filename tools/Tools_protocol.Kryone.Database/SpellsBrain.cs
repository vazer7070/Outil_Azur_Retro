using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tools_protocol.Kryone.Database
{
    public class SpellsBrain
    {
        public string Zone { get; set; }
        public string PA { get; set; }
        public string PO { get; set; }
        public string CC { get; set; }
        public string EC { get; set; }
        public string NBL { get; set; }
        public string INTERVAL { get; set; }
        public List<string> EFFECT;
        public List<string> EFFECT_CRIT;
        public string PMODIF { get; set; }
        public string LINE { get; set; }
        public string LINE_SEE { get; set; }
        public string EMPTY_CELL { get; set; }
        public string EC_TURN { get; set; }
        public string LEVEL { get; set; }

        // Listes réutilisables pour éviter les allocations répétées
        private readonly List<string> h = new List<string>();
        private readonly List<string> v = new List<string>();

        public SpellsBrain()
        {
            EFFECT = new List<string>();
            EFFECT_CRIT = new List<string>();
        }

        public SpellsBrain(string zone, string pA, string pO, string cC, string eC, string nBL,
            string iNTERVAL, List<string> eFFECT, List<string> eFFECT_CRIT, string pMODIF,
            string lINE, string lINE_SEE, string eMPTY_CELL, string eC_TURN, string lEVEL)
        {
            Zone = zone;
            PA = pA;
            PO = pO;
            CC = cC;
            EC = eC;
            NBL = nBL;
            INTERVAL = iNTERVAL;
            EFFECT = eFFECT ?? new List<string>();
            EFFECT_CRIT = eFFECT_CRIT ?? new List<string>();
            PMODIF = pMODIF;
            LINE = lINE;
            LINE_SEE = lINE_SEE;
            EMPTY_CELL = eMPTY_CELL;
            EC_TURN = eC_TURN;
            LEVEL = lEVEL;
        }

        private void ClearAll()
        {
            Zone = null;
            PA = null;
            PO = null;
            CC = null;
            EC = null;
            NBL = null;
            INTERVAL = null;
            h.Clear();
            v.Clear();
            EFFECT = new List<string>();
            EFFECT_CRIT = new List<string>();
            PMODIF = null;
            LINE = null;
            LINE_SEE = null;
            EMPTY_CELL = null;
            EC_TURN = null;
            LEVEL = null;
        }

        private void ProcessSpellData(string lvl)
        {
            if (!lvl.Contains("?")) return;

            string[] parts = lvl.Split('?');
            if (parts.Length < 15) return;

            try
            {
                // Traitement des données de base du sort
                PA = parts[1]?.Trim() ?? "0";
                PO = parts[2]?.Trim() ?? "0";
                CC = !string.IsNullOrEmpty(parts[3]) ? $"1/{parts[3].Trim()}" : "0";
                EC = parts[4]?.Equals("0") ?? true ? "0" : $"1/{parts[4].Trim()}";

                // Traitement des propriétés de ligne et visibilité
                LINE = parts[5]?.Trim() ?? "0";
                LINE_SEE = parts[6]?.Trim() ?? "0";
                EMPTY_CELL = parts[7]?.Trim() ?? "0";

                // Traitement des modificateurs et restrictions
                PMODIF = parts[8]?.Trim() ?? "0";
                NBL = parts[9]?.Trim() ?? "0";
                INTERVAL = parts[10]?.Trim() ?? "0";

                // Traitement de la zone et du niveau
                Zone = parts[12]?.Trim() ?? "0";
                LEVEL = parts[13]?.Trim() ?? "1";
                EC_TURN = parts[14]?.Trim() ?? "0";

                // Vérification des valeurs nulles ou vides
                if (string.IsNullOrEmpty(PA)) PA = "0";
                if (string.IsNullOrEmpty(PO)) PO = "0";
                if (string.IsNullOrEmpty(CC)) CC = "0";
                if (string.IsNullOrEmpty(EC)) EC = "0";
                if (string.IsNullOrEmpty(LINE)) LINE = "0";
                if (string.IsNullOrEmpty(LINE_SEE)) LINE_SEE = "0";
                if (string.IsNullOrEmpty(EMPTY_CELL)) EMPTY_CELL = "0";
                if (string.IsNullOrEmpty(PMODIF)) PMODIF = "0";
                if (string.IsNullOrEmpty(NBL)) NBL = "0";
                if (string.IsNullOrEmpty(INTERVAL)) INTERVAL = "0";
                if (string.IsNullOrEmpty(Zone)) Zone = "0";
                if (string.IsNullOrEmpty(LEVEL)) LEVEL = "1";
                if (string.IsNullOrEmpty(EC_TURN)) EC_TURN = "0";
            }
            catch 
            {
                // En cas d'erreur, initialiser avec des valeurs par défaut
                PA = "0"; PO = "0"; CC = "0"; EC = "0";
                LINE = "0"; LINE_SEE = "0"; EMPTY_CELL = "0";
                PMODIF = "0"; NBL = "0"; INTERVAL = "0";
                Zone = "0"; LEVEL = "1"; EC_TURN = "0";
            }
        }

        private void ProcessEffects(string lvl)
        {
            if (lvl.Contains("~"))
            {
                string[] mainParts = lvl.Split('@');
                if (mainParts.Length > 0)
                {
                    string normalEffect = mainParts[0].Trim();
                    h.Add(normalEffect);

                    if (mainParts.Length > 1)
                    {
                        string[] critParts = mainParts[1].Split('~');
                        if (critParts.Length > 1)
                        {
                            string[] finalParts = critParts[1].Split('§');
                            if (finalParts.Length > 0)
                                v.Add(finalParts[0].Trim());
                        }
                    }
                }
            }
            else if (lvl.Contains('@'))
            {
                string[] parts = lvl.Split('@');
                if (parts.Length > 1)
                {
                    string normalEffect = parts[0].Trim();
                    h.Add(normalEffect);
                    v.Add($"{normalEffect} ({parts[1].Trim()})");
                }
            }
            else if (lvl.Contains("§"))
            {
                string[] parts = lvl.Split('§');
                if (parts.Length > 0)
                    v.Add(parts[0].Trim());
            }
        }

        private Task ProcessLevelAsync(List<string> effectLevel, int level, int id)
        {
            // Réinitialiser les valeurs avant de traiter un nouveau niveau
            ClearAll();

            foreach (string lvl in effectLevel)
            {
                ProcessSpellData(lvl);
                ProcessEffects(lvl);
            }

            // S'assurer que toutes les propriétés sont définies
            if (string.IsNullOrEmpty(PA)) PA = "0";
            if (string.IsNullOrEmpty(PO)) PO = "0";
            if (string.IsNullOrEmpty(CC)) CC = "0";
            if (string.IsNullOrEmpty(EC)) EC = "0";
            if (string.IsNullOrEmpty(LINE)) LINE = "0";
            if (string.IsNullOrEmpty(LINE_SEE)) LINE_SEE = "0";
            if (string.IsNullOrEmpty(EMPTY_CELL)) EMPTY_CELL = "0";
            if (string.IsNullOrEmpty(PMODIF)) PMODIF = "0";
            if (string.IsNullOrEmpty(NBL)) NBL = "0";
            if (string.IsNullOrEmpty(INTERVAL)) INTERVAL = "0";
            if (string.IsNullOrEmpty(Zone)) Zone = "0";
            if (string.IsNullOrEmpty(LEVEL)) LEVEL = level.ToString();
            if (string.IsNullOrEmpty(EC_TURN)) EC_TURN = "0";

            // Copier les effets dans les listes finales
            EFFECT = new List<string>(h);
            EFFECT_CRIT = new List<string>(v);

            var key = new StringBuilder()
                .Append(id)
                .Append('|')
                .Append(SpellsList.Return_spells(id))
                .Append('|')
                .Append(level)
                .ToString();

            SpellsList.ParsedSPells.Add(key,
                new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL,
                    EFFECT, EFFECT_CRIT,
                    PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
            return Task.CompletedTask;
        }

        public async Task TradAndUnderstandSpellsAsync()
        {
            try
            {
                foreach (int id in SpellsList.AllSpells.Keys)
                {
                    SpellsList.ParseLevel(id, true);

                    // Traiter tous les niveaux en parallèle
                    var tasks = new[]
                    {
                        ProcessLevelAsync(SpellsList.EffectLvl1, 1, id),
                        ProcessLevelAsync(SpellsList.EffectLvl2, 2, id),
                        ProcessLevelAsync(SpellsList.EffectLvl3, 3, id),
                        ProcessLevelAsync(SpellsList.EffectLvl4, 4, id),
                        ProcessLevelAsync(SpellsList.EffectLvl5, 5, id)
                    };

                    await Task.WhenAll(tasks);

                    // Nettoyer les listes après chaque sort
                    SpellsList.EffectLvl1.Clear();
                    SpellsList.EffectLvl2.Clear();
                    SpellsList.EffectLvl3.Clear();
                    SpellsList.EffectLvl4.Clear();
                    SpellsList.EffectLvl5.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Une erreur s'est produite lors du traitement des sorts : {ex.Message}",
                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
