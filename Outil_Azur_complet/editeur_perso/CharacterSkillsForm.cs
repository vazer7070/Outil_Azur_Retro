using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet.editeur_perso
{
    public sealed class CharacterSkillsForm : AzurEditorWindow
    {
        private readonly int characterId;
        private readonly CharacterSkillKind kind;
        private readonly ListBox list = EditorUi.List();
        private readonly iTalk.iTalk_TextBox_Small search = EditorUi.TextBox();
        private readonly TabControl tabs = EditorUi.Tabs();
        private readonly Panel workspace = new Panel { Dock = DockStyle.Fill };
        private readonly List<CharacterSkillEntry> entries = new List<CharacterSkillEntry>();
        private readonly Dictionary<string, EditorField> fields = new Dictionary<string, EditorField>();
        private CharacterSkillsSnapshot snapshot;
        private CharacterSkillEntry current;
        private bool busy, dirty, selecting;
        public string SavedData { get; private set; }
        public CharacterSkillsForm(int characterId, CharacterSkillKind kind)
            : base(kind == CharacterSkillKind.Spells ? "Sorts du personnage" : "Métiers du personnage", "Sélectionnez une entrée pour modifier ses options. Enregistrer applique les changements au personnage hors ligne.", new Size(1140, 750))
        {
            this.characterId = characterId; this.kind = kind; MinimumSize = new Size(1020, 620);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
            var label = EditorUi.Label("Rechercher par nom ou numéro", 9); label.Dock = DockStyle.Top; label.Height = 24;
            search.Dock = DockStyle.Top; search.AccessibleName = label.Text; left.Controls.Add(list); left.Controls.Add(search); left.Controls.Add(label);
            layout.Controls.Add(left, 0, 0); layout.Controls.Add(tabs, 1, 0); workspace.Controls.Add(layout); Body.Controls.Add(workspace);
            ActionButton("Enregistrer", async () => await SaveAsync(), true); ActionButton("Ajouter", Add, width: 125); ActionButton("Retirer", Remove, width: 125); ActionButton("Fermer", Close, width: 125);
            list.SelectedIndexChanged += (s, e) => { if (selecting) return; var next = list.SelectedItem as EntryItem; if (!Commit()) { selecting = true; list.SelectedItem = list.Items.Cast<EntryItem>().FirstOrDefault(item => item.Entry == current); selecting = false; return; } RefreshList(next?.Entry); };
            search.TextChanged += (s, e) => { if (!selecting && Commit()) RefreshList(current); };
            KeyDown += async (s, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; await SaveAsync(); } };
            Shown += async (s, e) => await LoadAsync(); Actions.Enabled = false;
        }
        private sealed class EntryItem
        { internal CharacterSkillEntry Entry; internal string Text; public override string ToString() { return Text; } }
        private string Describe(CharacterSkillEntry entry)
        { string name = snapshot.Choices.FirstOrDefault(choice => choice.Id == entry.Id)?.Name ?? "Ressource inconnue"; return name + " · #" + entry.Id + (kind == CharacterSkillKind.Spells ? " · niv. " : " · XP ") + entry.Value; }
        private async Task LoadAsync()
        {
            SetBusy(true);
            try {
                snapshot = await Task.Run(() => CharacterSkillsService.Load(characterId, kind));
                entries.AddRange(snapshot.Entries.Select(entry => new CharacterSkillEntry { Id = entry.Id, Value = entry.Value, Tail = entry.Tail }));
                Text += " — " + snapshot.CharacterName; Heading.Text = Text; RefreshList();
            }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
        }
        private void SetBusy(bool value) { busy = value; Actions.Enabled = !value && snapshot != null; workspace.Enabled = !value && snapshot != null; }
        private void RefreshList(CharacterSkillEntry prefer = null)
        {
            if (snapshot == null) return;
            selecting = true; list.BeginUpdate(); list.Items.Clear();
            foreach (var entry in entries) { string text = Describe(entry); if (text.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) list.Items.Add(new EntryItem { Entry = entry, Text = text }); }
            var selected = list.Items.Cast<EntryItem>().FirstOrDefault(item => item.Entry == prefer) ?? list.Items.Cast<EntryItem>().FirstOrDefault();
            list.SelectedItem = selected; list.EndUpdate(); selecting = false; ShowEntry(selected?.Entry);
            SetStatus(entries.Count + (kind == CharacterSkillKind.Spells ? " sort(s)" : " métier(s)") + (dirty ? " · modifications en attente" : " · aucune modification en attente"));
        }
        private void AddField(TableLayoutPanel sheet, string key, object value, FieldSpec spec, Type type = null)
        { var field = new EditorField(new DataColumn(key, type ?? typeof(string)) { AllowDBNull = false }, value, spec); fields[key] = field; EditorUi.AddField(sheet, field); field.Changed += (s, e) => SetStatus("Valeurs modifiées. Enregistrez pour les appliquer au personnage."); }
        private void ShowEntry(CharacterSkillEntry entry)
        {
            current = entry; fields.Clear(); EditorUi.Clear(tabs);
            var sheet = EditorUi.Sheet(); EditorUi.Page(tabs, "Options", sheet);
            if (entry == null) { var label = EditorUi.Label("Ajoutez une entrée ou sélectionnez un résultat dans la liste."); label.Height = 60; EditorUi.AddField(sheet, label); return; }
            var choices = snapshot.Choices.Select(choice => new EditorOption(choice.Id.ToString(CultureInfo.InvariantCulture), choice.Name + " · #" + choice.Id)).ToArray();
            AddField(sheet, "id", entry.Id, new FieldSpec(kind == CharacterSkillKind.Spells ? "Sort" : "Métier", "Nom et identifiant de la ressource. La recherche dans la liste porte sur les entrées du personnage.") { Options = choices }, typeof(int));
            AddField(sheet, "valeur", entry.Value, kind == CharacterSkillKind.Spells ? new FieldSpec("Niveau du sort", "Le niveau doit être compris entre 1 et 6.") { Options = Enumerable.Range(1, 6).Select(level => new EditorOption(level.ToString(), "Niveau " + level)).ToArray() } : new FieldSpec("Expérience du métier (XP)", "De 0 à 2 147 483 647. Le niveau du métier est calculé par le serveur à partir de l'XP."), typeof(long));
            string[] tail = (entry.Tail ?? "").Split(';');
            if (kind == CharacterSkillKind.Spells) AddField(sheet, "position", tail[0], new FieldSpec("Position dans la barre de sorts", "Valeur stockée par le serveur ; -1 correspond à la position automatique ou non attribuée."));
            var advanced = EditorUi.Sheet(); EditorUi.Page(tabs, "Avancé", advanced);
            AddField(advanced, "suite", kind == CharacterSkillKind.Spells ? string.Join(";", tail.Skip(1)) : entry.Tail ?? "", new FieldSpec("Paramètres complémentaires", kind == CharacterSkillKind.Spells ? "Valeurs supplémentaires séparées par « ; ». Elles sont conservées et modifiables." : "Valeurs supplémentaires séparées par des virgules. Leur rôle dépend de l'émulateur.", multiline: true));
        }
        private bool Commit()
        {
            if (current == null) return true;
            try {
                int id = Convert.ToInt32(fields["id"].Read()); long value = Convert.ToInt64(fields["valeur"].Read()); string tail = (string)fields["suite"].Read();
                if (kind == CharacterSkillKind.Spells) {
                    string position = (string)fields["position"].Read();
                    if (position != "" && position != (current.Tail ?? "").Split(';')[0] && (!int.TryParse(position, out int number) || number < -1)) throw new FormatException("La position doit être un nombre positif ou nul, ou -1 pour la valeur automatique.");
                    tail = position + (tail == "" ? "" : ";" + tail);
                }
                var pending = new CharacterSkillEntry { Id = id, Value = value, Tail = tail };
                CharacterSkillsService.Serialize(entries.Select(entry => entry == current ? pending : entry), kind);
                if (id != current.Id || value != current.Value || tail != (current.Tail ?? "")) { current.Id = id; current.Value = value; current.Tail = tail; dirty = true; }
                return true;
            }
            catch (Exception error) { Status.Text = error.Message; Status.ForeColor = Color.Firebrick; return false; }
        }
        private void Add()
        {
            if (snapshot == null || !Commit()) return;
            var choice = snapshot.Choices.FirstOrDefault(item => !entries.Any(existing => existing.Id == item.Id));
            if (choice == null) { SetStatus("Toutes les ressources disponibles sont déjà présentes."); return; }
            var entry = new CharacterSkillEntry { Id = choice.Id, Value = kind == CharacterSkillKind.Spells ? 1 : 0, Tail = kind == CharacterSkillKind.Spells ? "-1" : "" };
            entries.Add(entry); dirty = true; selecting = true; search.Text = ""; selecting = false; RefreshList(entry);
        }
        private void Remove() { if (current == null) return; entries.Remove(current); current = null; dirty = true; RefreshList(); }
        private async Task SaveAsync()
        {
            if (snapshot == null || busy || !Commit()) return;
            SetBusy(true);
            try { SavedData = await Task.Run(() => CharacterSkillsService.Save(snapshot, entries)); dirty = false; busy = false; DialogResult = DialogResult.OK; Close(); }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) e.Cancel = true;
            else { bool valid = Commit(); if ((!valid || dirty) && MessageBox.Show(this, "Fermer sans enregistrer les modifications ?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; }
            base.OnFormClosing(e);
        }
    }
}
