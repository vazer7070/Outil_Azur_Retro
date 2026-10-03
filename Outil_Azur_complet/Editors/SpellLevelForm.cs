using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Outil_Azur_complet.Editors
{
    public sealed class SpellLevelData
    {
        public string[] Fields { get; private set; }
        public int ZoneIndex { get { return Fields.Length - 5; } }
        public static SpellLevelData Parse(string data)
        {
            if (data == "-1" || string.IsNullOrWhiteSpace(data)) data = "-1,-1,3,1,1,0,0,false,true,false,false,0,0,0,Pa,-1,-1,1,false";
            ServerDataService.ValidateSpellLevel(data);
            return new SpellLevelData { Fields = data.Split(',') };
        }
        public string Serialize() { string data = string.Join(",", Fields); ServerDataService.ValidateSpellLevel(data); return data; }
        public static string Describe(string data)
        {
            if (string.IsNullOrWhiteSpace(data) || data == "-1") return "Niveau désactivé — cliquez sur Modifier pour le définir";
            try {
                var level = Parse(data); string[] p = level.Fields;
                int count = p.Take(2).Sum(value => value == "" || value == "-1" ? 0 : value.Split('|').Length);
                return p[2] + " PA · portée " + p[3] + " à " + p[4] + " · " + count + " effet(s) · niveau requis " + p[level.ZoneIndex + 3];
            }
            catch (Exception) { return "Format à vérifier — les données existantes sont conservées"; }
        }
    }

    internal sealed class SpellLevelForm : AzurEditorWindow
    {
        private readonly TabControl tabs = EditorUi.Tabs();
        private readonly Dictionary<int, EditorField> fields = new Dictionary<int, EditorField>();
        private readonly CheckBox enabled = new CheckBox { Text = "Ce niveau de sort est disponible", AutoSize = true, Dock = DockStyle.Top, Height = 32 };
        private readonly iTalk.iTalk_TextBox_Small raw;
        private SpellLevelData model;
        private readonly string original;
        private bool rawDirty, updatingRaw;
        internal string Result { get; private set; }
        private SpellLevelForm(string title, string data) : base(title, "Réglez le lancement, les effets et les conditions. Appliquer reporte les options dans la fiche du sort.", new Size(1080, 780))
        {
            original = data; raw = EditorUi.TextBox(data, true); raw.Dock = DockStyle.Top; raw.Height = 170;
            enabled.Checked = !string.IsNullOrWhiteSpace(data) && data != "-1";
            Body.Controls.Add(tabs); Body.Controls.Add(enabled);
            ActionButton("Appliquer", Apply, true); ActionButton("Annuler", () => { DialogResult = DialogResult.Cancel; Close(); });
            try { Build(SpellLevelData.Parse(data)); }
            catch (Exception error) { Build(SpellLevelData.Parse("-1")); raw.Text = data; rawDirty = true; tabs.SelectedIndex = tabs.TabCount - 1; SetStatus("Format existant à corriger : " + error.Message); }
            raw.TextChanged += (s, e) => { if (!updatingRaw) rawDirty = true; };
            tabs.Selecting += (s, e) => {
                if (e.TabPageIndex != tabs.TabCount - 1 || rawDirty) return;
                try { UpdateRaw(Read()); } catch (Exception error) { e.Cancel = true; Status.Text = error.Message; Status.ForeColor = Color.Firebrick; }
            };
            enabled.CheckedChanged += (s, e) => { for (int i = 0; i < tabs.TabCount - 1; i++) tabs.TabPages[i].Enabled = enabled.Checked; };
            for (int i = 0; i < tabs.TabCount - 1; i++) tabs.TabPages[i].Enabled = enabled.Checked;
        }
        internal static string Edit(IWin32Window owner, string title, string data)
        { using (var form = new SpellLevelForm(title, data ?? "")) return form.ShowDialog(owner) == DialogResult.OK ? form.Result : null; }
        private void Build(SpellLevelData level)
        {
            model = level; fields.Clear(); if (raw.Parent != null) raw.Parent.Controls.Remove(raw); EditorUi.Clear(tabs);
            var casting = EditorUi.Sheet(2); EditorUi.Page(tabs, "Lancement", casting);
            Add(casting, 2, "Coût en points d'action (PA)", "PA nécessaires. -1 : valeur spéciale du serveur.");
            Add(casting, 13, "Intervalle entre deux lancers (tours)", "Tours à attendre avant de relancer le sort.");
            Add(casting, 3, "Portée minimale", "Distance minimale de lancement.");
            Add(casting, 4, "Portée maximale", "Distance maximale, avant les bonus de portée.");
            Add(casting, 5, "Coup critique : 1 sur…", "0 : désactivé ; 30 : une chance sur 30.");
            Add(casting, 6, "Échec critique : 1 sur…", "0 : désactivé ; 100 : une chance sur 100.");
            Add(casting, 11, "Lancers par tour", "0 : habituellement sans limite.");
            Add(casting, 12, "Lancers sur une même cible", "Limite par tour et par cible ; 0 : sans limite.");
            var effects = EditorUi.Sheet(); EditorUi.Page(tabs, "Effets", effects);
            Add(effects, 0, "Effets lors d'un lancer normal", "Ajoutez ou modifiez chaque effet avec ses valeurs, sa durée et son jet de dés.", edit: DelimitedListForm.EditEffects, summary: EffectSummary);
            Add(effects, 1, "Effets lors d'un coup critique", "Effets utilisés lorsque le lancer est critique.", edit: DelimitedListForm.EditEffects, summary: EffectSummary);
            Add(effects, level.ZoneIndex, "Zones des effets", "Ordre : effets normaux, puis critiques. Une zone unique est partagée par tous les effets.", edit: EditZones, summary: ZoneSummary);
            var conditions = EditorUi.Sheet(); EditorUi.Page(tabs, "Conditions", conditions);
            Add(conditions, 7, "Lancer en ligne uniquement", "La cible doit être alignée avec le lanceur.", true);
            Add(conditions, 8, "Exiger une ligne de vue", "Un obstacle peut empêcher le lancement.", true);
            Add(conditions, 9, "Exiger une cellule vide", "La cellule ciblée doit être libre.", true);
            Add(conditions, 10, "Portée modifiable", "Autoriser les bonus et malus de portée.", true);
            Add(conditions, level.ZoneIndex + 1, "États requis", "Identifiants séparés par « ; ». -1 signifie aucune condition d'état.");
            Add(conditions, level.ZoneIndex + 2, "États interdits", "Identifiants séparés par « ; ». -1 signifie aucun état interdit.");
            Add(conditions, level.ZoneIndex + 3, "Niveau du personnage requis", "Niveau minimal pour utiliser ce niveau de sort.");
            Add(conditions, level.ZoneIndex + 4, "L'échec critique termine le tour", "Le personnage ne peut plus agir après un échec critique.", true);
            if (level.Fields.Length == 20) Add(conditions, 14, "Paramètre supplémentaire du serveur", "Option du format à 20 champs. Son interprétation dépend de la version de l'émulateur.");
            var advanced = EditorUi.Sheet(); var help = EditorUi.Label("Le format complet reste modifiable pour les variantes du serveur.\r\nLire les données remplit les champs ; Actualiser le texte reflète les champs actuels."); help.Height = 52; EditorUi.AddField(advanced, help); EditorUi.AddField(advanced, raw);
            var buttons = new FlowLayoutPanel { Height = 40, Dock = DockStyle.Top };
            var read = EditorUi.Button("Lire les données", false, 155); read.Click += (s, e) => { try { string value = raw.Text; var parsed = SpellLevelData.Parse(value); rawDirty = false; Build(parsed); enabled.Checked = value != "-1" && !string.IsNullOrWhiteSpace(value); SetStatus("Données chargées dans les champs."); } catch (Exception error) { ShowError(error); } };
            var write = EditorUi.Button("Actualiser le texte", false, 170); write.Click += (s, e) => { try { UpdateRaw(Read()); SetStatus("Texte actualisé. Les options sont toujours en attente."); } catch (Exception error) { ShowError(error); } };
            buttons.Controls.Add(read); buttons.Controls.Add(write); EditorUi.AddField(advanced, buttons); EditorUi.Page(tabs, "Format serveur", advanced);
            SetStatus("Tous les paramètres du niveau sont accessibles ; aucun champ supplémentaire n'est supprimé.");
        }
        private void Add(TableLayoutPanel sheet, int index, string label, string help, bool boolean = false, Func<IWin32Window, string, string> edit = null, Func<string, string> summary = null)
        {
            var spec = new FieldSpec(label, help) { Edit = edit, Summary = summary };
            if (boolean) { bool numeric = model.Fields[index] == "0" || model.Fields[index] == "1"; spec.Options = new[] { new EditorOption(numeric ? "0" : "false", "Non"), new EditorOption(numeric ? "1" : "true", "Oui") }; }
            var field = new EditorField(new DataColumn("param" + index, typeof(string)) { AllowDBNull = false }, model.Fields[index], spec); fields[index] = field; EditorUi.AddField(sheet, field);
        }
        private string Read()
        {
            if (!enabled.Checked) return original == "" || string.IsNullOrWhiteSpace(original) ? original : "-1";
            var parsed = SpellLevelData.Parse(string.Join(",", model.Fields));
            foreach (var pair in fields) parsed.Fields[pair.Key] = (string)pair.Value.Read();
            return parsed.Serialize();
        }
        private void UpdateRaw(string text) { updatingRaw = true; try { raw.Text = text; rawDirty = false; } finally { updatingRaw = false; } }
        private void Apply()
        {
            if (enabled.Checked && rawDirty && tabs.SelectedIndex != tabs.TabCount - 1) throw new FormatException("Le texte du format serveur a changé. Cliquez sur Lire les données dans cet onglet avant d'appliquer les champs.");
            string value = enabled.Checked && rawDirty ? raw.Text : Read();
            ServerDataService.ValidateSpellLevel(value); Result = value; DialogResult = DialogResult.OK; Close();
        }
        private static string EffectSummary(string value) { return value == "" || value == "-1" ? "Aucun effet" : value.Split('|').Length + " effet(s) — " + DelimitedListForm.EffectName(value.Split('|')[0]); }
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
        private static readonly EditorOption[] Shapes = new[] { new EditorOption("P", "Cellule unique"), new EditorOption("C", "Cercle"), new EditorOption("L", "Ligne"), new EditorOption("X", "Croix"), new EditorOption("O", "Anneau"), new EditorOption("R", "Rectangle"), new EditorOption("T", "Ligne perpendiculaire") };
        private static string ZoneSummary(string data)
        {
            if (data.Length == 2) { var shape = Shapes.FirstOrDefault(option => option.Value == data[0].ToString()); int size = Alphabet.IndexOf(data[1]); return (shape?.Text ?? "Forme " + data[0]) + " · taille " + size; }
            return data.Length / 2 + " zones d'effet";
        }
        private static string EditZones(IWin32Window owner, string data)
        {
            if (data.Length % 2 != 0 || data.Where((c, i) => i % 2 == 1).Any(c => Alphabet.IndexOf(c) < 0)) throw new FormatException("Code de zone invalide. Corrigez-le dans l'onglet Format serveur.");
            string expanded = string.Join("|", Enumerable.Range(0, data.Length / 2).Select(i => data[i * 2] + ";" + Alphabet.IndexOf(data[i * 2 + 1]).ToString(CultureInfo.InvariantCulture)));
            string result = DelimitedListForm.Edit(owner, "Zones des effets", "Une ligne par zone, dans l'ordre des effets normaux puis critiques. Une zone unique s'applique à tous les effets.", expanded, '|', ';', new[] { new FieldSpec("Forme de la zone") { Options = Shapes, DefaultText = "P" }, FieldSpec.Number("Taille de la zone (0 à 63)", "Distance autour de la cellule ciblée, selon la forme.") }, record => { string[] p = record.Split(';'); var shape = Shapes.FirstOrDefault(option => option.Value == p[0]); return (shape?.Text ?? p[0]) + " · taille " + (p.Length > 1 ? p[1] : "?"); }, 2);
            if (result == null) return null;
            return string.Concat(result.Split('|').Where(record => record != "").Select(record => { string[] p = record.Split(';'); if (p.Length != 2 || p[0].Length != 1 || !int.TryParse(p[1], out int size) || size < 0 || size >= 64) throw new FormatException("Chaque zone exige une forme et une taille de 0 à 63."); return p[0] + Alphabet[size]; }));
        }
    }
}
