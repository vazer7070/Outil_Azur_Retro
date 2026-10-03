using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Outil_Azur_complet.maps
{
    public partial class MainEditeur : Form
    {
        private readonly Random mapIdGenerator = new Random();
        private MapForm propertiesMap;
        private MapForm selectedCellMap;
        private int selectedCellId = -1;
        private bool loadingCellProperties;
        private readonly ToolStripButton serverPlacementsButton = new ToolStripButton("Placements serveur")
        { Name = "serverPlacementsButton", DisplayStyle = ToolStripItemDisplayStyle.Text };
        private readonly CheckBox activeCellCheckBox = new CheckBox { Name = "activeCellCheckBox", Text = "Cellule active", AutoSize = true, Location = new Point(16, 680) };
        private readonly ComboBox movementComboBox = new ComboBox { Name = "movementComboBox", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(16, 726), Width = 140 };

        public MainEditeur()
        {
            InitializeComponent();
            var exportSwf = new ToolStripButton("Exporter SWF") { Name = "exportSwfButton", DisplayStyle = ToolStripItemDisplayStyle.Text };
            exportSwf.Click += (sender, args) => ExportSwf(ActiveMdiChild as MapForm ?? MapSelected);
            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(toolStripButton2) + 1, exportSwf);
            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(exportSwf) + 1, serverPlacementsButton);
            serverPlacementsButton.Click += async (sender, args) => await ToggleServerPlacementsAsync();
            tabPageAdv2.Controls.Add(activeCellCheckBox);
            pNJToolStripMenuItem.Click += (sender, args) => OpenServerPlacement(ServerResourceKind.Npcs, "PNJ de la carte");
            monstresToolStripMenuItem.Click += (sender, args) => OpenServerPlacement(ServerResourceKind.MonsterGroups, "Groupes de monstres fixes");
            var paddocksMenu = new ToolStripMenuItem("Enclos") { Name = "paddocksMenu" };
            paddocksMenu.Click += (sender, args) => OpenServerPlacement(ServerResourceKind.Paddocks, "Enclos de la carte");
            var zaapsMenu = new ToolStripMenuItem("Zaap") { Name = "zaapsMenu" };
            zaapsMenu.Click += (sender, args) => OpenServerPlacement(ServerResourceKind.Zaaps, "Zaap de la carte");
            var interactivesMenu = new ToolStripMenuItem("Définitions des interactifs");
            interactivesMenu.Click += (sender, args) => new ServerDataForm(ServerResourceKind.Interactives, "Définitions des interactifs").Show(this);
            toolStripDropDownButton2.DropDownItems.AddRange(new ToolStripItem[] { paddocksMenu, zaapsMenu, interactivesMenu });
            tabPageAdv2.Controls.Add(new Label { Text = "Déplacement", AutoSize = true, Location = new Point(16, 706) });
            movementComboBox.Items.AddRange(new object[] { "0 : Bloquée", "1 : Porte", "2 : Trigger", "3 : Type 3", "4 : Marchable", "5 : Enclos", "6 : Type 6", "7 : Chemin" });
            tabPageAdv2.Controls.Add(movementComboBox);
            activeCellCheckBox.CheckedChanged += (sender, args) => ApplyCellProperties();
            movementComboBox.SelectedIndexChanged += (sender, args) =>
            {
                if (loadingCellProperties || selectedCellMap?.MyMap?.Cells == null || selectedCellId < 0 || movementComboBox.SelectedIndex < 0) return;
                selectedCellMap.MyMap.Cells[selectedCellId].Type(movementComboBox.SelectedIndex);
                selectedCellMap.Edited = true;
                SelectCell(selectedCellMap, selectedCellId);
                selectedCellMap.DrawAll();
            };
            MdiChildActivate += MainEditeur_MdiChildActivate;
            toolStripButton6.Click += (sender, args) => SelectTool(Tools.Selector, CellMode.Null, toolStripButton6);
            toolStripButton7.Click += (sender, args) => SelectTool(Tools.CellMode, CellMode.UnWalkable, toolStripButton7);
            toolStripButton8.Click += (sender, args) => SelectTool(Tools.CellMode, CellMode.LoS, toolStripButton8);
            toolStripButton9.Click += (sender, args) => SelectTool(Tools.CellMode, CellMode.Path, toolStripButton9);
            toolStripButton10.Click += (sender, args) => SelectTool(Tools.CellMode, CellMode.Paddock, toolStripButton10);
            toolStripButton11.Click += (sender, args) => SelectTool(Tools.CellMode, CellMode.Fight1, toolStripButton11);
            toolStripButton12.Click += (sender, args) => SelectTool(Tools.CellMode, CellMode.Fight2, toolStripButton12);
            toolStripButton13.Click += (sender, args) => SelectLayer(1);
            toolStripButton14.Click += (sender, args) => SelectLayer(2);
            toolStripButton15.Click += (sender, args) => { SelectedFlip = !SelectedFlip; toolStripButton15.Checked = SelectedFlip; };
            toolStripButton16.Click += (sender, args) =>
            {
                SelectedRotate = (SelectedRotate + 1) % 4;
                toolStripButton16.Checked = SelectedRotate != 0;
                toolStripButton16.ToolTipText = $"Rotation : {SelectedRotate * 90}°";
            };
            SelectLayer(1);
            SelectTool(Tools.Brush, CellMode.Null, toolStripButton5);
            tabPageAdv1.AutoScroll = true;
            tabPageAdv2.AutoScroll = true;
            iTalk_NumericUpDown1.Minimum = 1;
            iTalk_NumericUpDown1.Maximum = int.MaxValue;
            foreach (var number in new[] { iTalk_NumericUpDown5, iTalk_NumericUpDown6,
                iTalk_NumericUpDown7, iTalk_NumericUpDown8,
                iTalk_NumericUpDown9, iTalk_NumericUpDown10 }) number.Maximum = int.MaxValue;
            iTalk_NumericUpDown9.Minimum = int.MinValue;
            iTalk_NumericUpDown10.Minimum = int.MinValue;
            iTalk_NumericUpDown3.Maximum = 15;
            iTalk_NumericUpDown4.Maximum = 15;
            foreach (var check in new[] { iTalk_CheckBox6, iTalk_CheckBox7, iTalk_CheckBox8,
                iTalk_CheckBox9, iTalk_CheckBox10, iTalk_CheckBox11, iTalk_CheckBox12 })
                check.CheckedChanged += sender => ApplyCellProperties();
            foreach (var number in new[] { iTalk_NumericUpDown3, iTalk_NumericUpDown4 })
            {
                number.MouseUp += (sender, args) => ApplyCellProperties();
                number.Leave += (sender, args) => ApplyCellProperties();
                number.KeyUp += (sender, args) => ApplyCellProperties();
            }
            sfButton1.Click += (sender, args) => DeleteSelectedLayer(1);
            sfButton2.Click += (sender, args) => DeleteSelectedLayer(2);
            sfButton3.Click += (sender, args) => DeleteSelectedLayer(3);
            iTalk_Button_23.Click -= iTalk_Button_21_Click;
            iTalk_Button_23.Text = "Fermer la carte";
            iTalk_Button_23.Click += (sender, args) => MapSelected?.Close();
            BuildEditorLayout();
        }
        public TilesData SelectedTile = null;
        public Tools T = Tools.Brush;
        public CellMode CellMod = CellMode.Null;

        public int Calque = 1;
        public bool SelectedFlip = false;
        public int SelectedRotate = 0;

        public int CellTrigger = -1;
        private MapForm triggerSource;
        public int MapTrigger = 0;
        public int NbTrigger = 0;

        public string FolderSols = @".\ressources\maps\sols\";
        public string FolderObjets = @".\ressources\maps\objets\";
        public List<MapForm> OpenMap = new List<MapForm>();
        public int Mapcount;
        public MapForm MapSelected;

        public bool Show_Grid = true;
        public bool Show_CellID = false;
        public bool Show_Back = true;
        public bool Show_ground = true;
        public bool Show_calque1 = true;
        public bool Show_calque2 = true;
        public Image Bi = null;

        public Map EndFightMap;


        public enum Tools
        {
            Brush,
            Selector,
            CellMode
        }
        public enum CellMode
        {
            Null,
            UnWalkable,
            LoS,
            Path,
            Paddock,
            Fight1,
            Fight2
        }
        public bool AlreadyOpen (int ID)
        {
            foreach(MapForm f in OpenMap)
                if(f.ID == ID)
                    return true;
            return false;
        }
        public void OpenNewMap(int x, int y)
        {
            if (x < 2 || x > 100 || y < 2 || y > 100)
                throw new ArgumentOutOfRangeException("Les dimensions de carte doivent être comprises entre 2 et 100.");
            int r;
            do
            {
                r = mapIdGenerator.Next(1, 1000000);
            }
            while (AlreadyOpen(r) || Map.GetByID(r) != null);

            var project = new Map
            {
                ID = r,
                Width = x,
                Height = y,
                Cells = new CellsData[Map.CellCount(x, y)],
                HasProjectCells = true
            };
            MapForm NewMap = new MapForm();
            NewMap.New(project);
            NewMap.MdiParent = this;
            MapSelected = NewMap;
            NewMap.W = x;
            NewMap.H = y;
            NewMap.Text = $"MapID: {r}";
            NewMap.ID = r;
            Mapcount += 1;
            NewMap.Show();
            OpenMap.Add(NewMap);
            iTalk_NotificationNumber3.Value = MapSelected.W;
            iTalk_NotificationNumber2.Value = MapSelected.H;

        }
        private void petiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNewMap(15, 17);
        }
        public void AddTrigger(int mapid, int cellid, MapForm M)
        {
            if (CellTrigger < 0)
            {
                NbTrigger += 1;
                CellTrigger = cellid;
                MapTrigger = mapid;
                triggerSource = M;
                M.MyMap.Cells[cellid].Trigger = true;
                M.MyMap.Cells[cellid].TriggerName = $"{NbTrigger}D";
                M.Edited = true;
                M.DrawAll();
            }
            else
            {
                string path;
                try
                {
                    if (triggerSource == null || triggerSource.IsDisposed)
                        throw new InvalidOperationException("La carte de départ du trigger a été fermée.");
                    string table = EmuManager.ReturnTable("cellule", EmuManager.EMUSELECTED);
                    path = MapActionSqlBuilder.Export(@".\creations", "triggers",
                        MapActionSqlBuilder.BuildTrigger(table, triggerSource.MyMap.ID, CellTrigger, mapid, cellid));
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "Export du trigger impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                CellTrigger = -1;
                MapTrigger = 0;
                triggerSource = null;
                M.MyMap.Cells[cellid].Trigger = true;
                M.MyMap.Cells[cellid].TriggerName = $"{NbTrigger}A";
                M.Edited = true;
                M.DrawAll();

                DialogResult DR = MessageBox.Show($"Le trigger a été exporté dans {Path.GetFullPath(path)}. Continuer ?", "Ajout des triggers", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (DR == DialogResult.Yes)
                {
                    MessageBox.Show("Merci de cliquer sur la cellule initiale", "Ajout de triggers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (DR == DialogResult.No)
                {
                    SelectTool(Tools.Brush, CellMode.Null, toolStripButton5);
                    RefreshAllMap();
                }
                else
                {
                    SelectTool(Tools.Brush, CellMode.Null, toolStripButton5);
                    RefreshAllMap();
                }
            }
        }
        public void AddEndFightAction(Map M, int cellid)
        {
            if (EndFightMap == null)
            {
                EndFightMap = M;
                MessageBox.Show("Merci de selectionner la cellule initiale", "Selection de la cellule de départ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                if (M.ID != EndFightMap.ID)
                {
                    string path;
                    try
                    {
                        string table = EmuManager.ReturnTable("endfight", EmuManager.EMUSELECTED);
                        path = MapActionSqlBuilder.Export(@".\creations", "donjons",
                            MapActionSqlBuilder.BuildEndFight(table, EndFightMap.ID, M.ID, cellid));
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(error.Message, "Export du donjon impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    EndFightMap.NextRoom = M.ID;
                    EndFightMap.NextCell = cellid;
                    foreach (MapForm form in OpenMap) if (ReferenceEquals(form.MyMap, EndFightMap)) form.Edited = true;
                    MessageBox.Show($"La sortie vers la carte {M.ID}, cellule {cellid}, a été exportée dans {Path.GetFullPath(path)}.");
                }
                else
                {
                    MessageBox.Show("La carte de départ et d'arrivée doivent être différentes", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
                EndFightMap = M;
                MessageBox.Show("Veuillez selectionner la cellule suivante", "Placement de cellule", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            SaveMap(ActiveMdiChild as MapForm ?? MapSelected);
        }

        internal bool CommitPropertiesBeforeClose(MapForm form)
        {
            try
            {
                if (ReferenceEquals(propertiesMap, form)) ApplyMapProperties(form);
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Propriétés de carte invalides", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        internal bool SaveMap(MapForm selected)
        {
            try
            {
                if (selected?.MyMap == null)
                {
                    MessageBox.Show("Créez ou ouvrez une carte avant de la sauvegarder.", "Aucune carte", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                if (ReferenceEquals(propertiesMap, selected)) ApplyMapProperties(selected);
                selected.MyMap.ID = selected.ID;
                selected.MyMap.Width = selected.W;
                selected.MyMap.Height = selected.H;
                string creationDirectory = Path.GetFullPath(@".\creations");
                Directory.CreateDirectory(creationDirectory);
                string version = new string((selected.MyMap.DateMap ?? "AZ").Select(character =>
                    Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
                using (var dialog = new SaveFileDialog
                {
                    InitialDirectory = creationDirectory,
                    Filter = "Projet de carte Azur (*.ame)|*.ame",
                    DefaultExt = "ame", AddExtension = true,
                    FileName = $"{selected.MyMap.ID}_{version}.ame"
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    MapProjectSerializer.Save(dialog.FileName, selected.MyMap);
                    selected.Edited = false;
                    return true;
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Sauvegarde impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            AboutForm about = new AboutForm();
            about.ShowDialog();
        }

        private void ExportSwf(MapForm selected)
        {
            try
            {
                if (selected?.MyMap == null) throw new InvalidOperationException("Créez ou ouvrez une carte avant de l'exporter.");
                if (ReferenceEquals(propertiesMap, selected)) ApplyMapProperties(selected);
                selected.MyMap.ID = selected.ID;
                selected.MyMap.Width = selected.W;
                selected.MyMap.Height = selected.H;
                string directory = Path.GetFullPath(@".\creations");
                Directory.CreateDirectory(directory);
                string version = new string((selected.MyMap.DateMap ?? "AZ").Select(character =>
                    Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
                using (var dialog = new SaveFileDialog
                {
                    InitialDirectory = directory, Filter = "Carte client SWF (*.swf)|*.swf",
                    DefaultExt = "swf", AddExtension = true, FileName = $"{selected.ID}_{version}.swf"
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    MapSwfSerializer.Save(dialog.FileName, selected.MyMap);
                    MessageBox.Show("La carte SWF a été exportée. Sauvegardez aussi le projet AME pour conserver les cellules de combat, les coordonnées et les paramètres serveur.",
                        "Export terminé", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception error)
            { MessageBox.Show(error.Message, "Export SWF impossible", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenServerPlacement(ServerResourceKind kind, string title)
        {
            MapForm map = ActiveMdiChild as MapForm ?? MapSelected;
            if (map?.MyMap == null) { MessageBox.Show("Ouvrez une carte, puis sélectionnez une cellule."); return; }
            if (ReferenceEquals(propertiesMap, map) && !CommitPropertiesBeforeClose(map)) return;
            var form = new ServerDataForm(kind, title, map.ID, map.MyMap.Cells.Length, Math.Max(0, map.SelectedCell));
            form.Saved += async (sender, args) =>
            {
                if (!map.IsDisposed && map.ShowServerPlacements)
                    await RefreshServerPlacementsAsync(map);
            };
            form.Show(this);
        }

        private async Task ToggleServerPlacementsAsync()
        {
            MapForm map = ActiveMdiChild as MapForm ?? MapSelected;
            if (map?.MyMap?.Cells == null) return;
            if (map.ShowServerPlacements)
            {
                map.HideServerPlacements();
                UpdateWorkspace();
                return;
            }
            await RefreshServerPlacementsAsync(map);
        }

        private async Task RefreshServerPlacementsAsync(MapForm map)
        {
            if (map.IsDisposed || map.MyMap?.Cells == null) return;
            int mapId = map.ID;
            int cellCount = map.MyMap.Cells.Length;
            serverPlacementsButton.Enabled = false;
            workspaceStatus.Text = "Chargement des placements de la carte " + mapId + "…";
            try
            {
                MapServerPlacementLayer layer = await Task.Run(() => MapServerPlacementLayer.Load(mapId, cellCount));
                if (map.IsDisposed || map.ID != mapId) return;
                map.SetServerPlacements(layer);
                if (ReferenceEquals(ActiveMdiChild, map)) UpdateWorkspace();
                if (!string.IsNullOrEmpty(layer.Notice))
                    MessageBox.Show(this, layer.Notice, "Placements partiellement chargés", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                    MessageBox.Show(this, error.Message, "Placements indisponibles", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (!map.IsDisposed && ReferenceEquals(ActiveMdiChild, map)) UpdateWorkspace();
            }
            finally { if (!IsDisposed) serverPlacementsButton.Enabled = true; }
        }

        private void sWFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                InitialDirectory = Path.GetFullPath(@".\swf"),
                Filter = "Cartes prises en charge (*.ame;*.swf)|*.ame;*.swf|Projets Azur (*.ame)|*.ame|Cartes SWF (*.swf)|*.swf",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string file in dialog.FileNames)
                {
                    try
                    {
                        if (string.Equals(Path.GetExtension(file), ".ame", StringComparison.OrdinalIgnoreCase))
                            OpenMapProject(MapProjectSerializer.Load(file));
                        else
                            TradMap(file);
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show($"{Path.GetFileName(file)} : {error.Message}", "Ouverture impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
        public void TradMap(string name, string key = "")
        {
           
           Map mapcharged = SwfReader.UnPackerSwf(name);
            if(mapcharged == null)
            {
                MessageBox.Show("La carte demandée n'est pas valide.", "Carte ilisible", MessageBoxButtons.OK, MessageBoxIcon.Error );
                return;
            }
            mapcharged.Key = key;
            OpenMapProject(mapcharged);
        }
        public void OpenMapProject(Map m)
        {
            if (m == null)
                throw new ArgumentNullException(nameof(m));
            if (m.IsEditing || AlreadyOpen(m.ID))
                MessageBox.Show($"La carte {m.ID} est déjà en cours d'édition.", "Carte déjà ouverte", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            else 
            {
                MapForm NewMap = new MapForm();
                NewMap.New(m);
                NewMap.MdiParent = this;
                MapSelected = NewMap;
                NewMap.W = m.Width;
                NewMap.H = m.Height;
                NewMap.Text = $"MapID: {m.ID}";
                NewMap.ID = m.ID;
                Mapcount += 1;
                NewMap.Show();
                OpenMap.Add(NewMap);
                iTalk_NotificationNumber3.Value = MapSelected.W;
                iTalk_NotificationNumber2.Value = MapSelected.H;
            }
        }

        private void MainEditeur_MdiChildActivate(object sender, EventArgs e)
        {
            if (propertiesMap != null && !propertiesMap.IsDisposed && propertiesMap.Loaded)
            {
                try { ApplyMapProperties(propertiesMap); }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "Propriétés de carte invalides", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            MapSelected = ActiveMdiChild as MapForm;
            propertiesMap = MapSelected;
            if (MapSelected == null) return;
            iTalk_NotificationNumber3.Value = MapSelected.W;
            iTalk_NotificationNumber2.Value = MapSelected.H;
            PopulateMapProperties(MapSelected);
        }

        internal void NotifyMapClosed(MapForm mapForm)
        {
            if (ReferenceEquals(triggerSource, mapForm)) { triggerSource = null; CellTrigger = -1; MapTrigger = 0; }
            if (ReferenceEquals(EndFightMap, mapForm.MyMap)) EndFightMap = null;
            OpenMap.Remove(mapForm);
            Mapcount = OpenMap.Count;
            if (ReferenceEquals(MapSelected, mapForm))
                MapSelected = ActiveMdiChild as MapForm;
            if (ReferenceEquals(propertiesMap, mapForm)) propertiesMap = null;
            if (ReferenceEquals(selectedCellMap, mapForm)) { selectedCellMap = null; selectedCellId = -1; }
        }

        private void PopulateMapProperties(MapForm form)
        {
            Map map = form.MyMap;
            if (map == null) return;
            iTalk_NumericUpDown1.Value = map.ID;
            iTalk_TextBox_Small2.Text = map.DateMap;
            iTalk_NumericUpDown5.Value = map.Ambiance;
            iTalk_NumericUpDown6.Value = map.NbGroups;
            iTalk_NumericUpDown7.Value = map.GroupMaxSize;
            iTalk_NumericUpDown8.Value = map.SuperArea;
            sfComboBox1.Text = map.Area.ToString();
            sfComboBox2.Text = map.SubArea.ToString();
            iTalk_NumericUpDown9.Value = map.X;
            iTalk_NumericUpDown10.Value = map.Y;
            iTalk_CheckBox1.Checked = map.IsOutDoor;
            capabilitiesMask.Value = map.Capabilities;
            iTalk_CheckBox2.Checked = (map.Capabilities & 1) == 0;
            iTalk_CheckBox3.Checked = (map.Capabilities & 2) == 0;
            iTalk_CheckBox4.Checked = (map.Capabilities & 4) == 0;
            iTalk_CheckBox5.Checked = (map.Capabilities & 8) == 0;
            string music = string.IsNullOrEmpty(map.MusiqueName) ? map.Musique.ToString() : map.MusiqueName;
            iTalk_ComboBox2.SelectedIndex = iTalk_ComboBox2.Items.IndexOf(music);
        }

        private void ApplyMapProperties(MapForm form)
        {
            Map map = form.MyMap;
            if (map == null) return;
            int id = checked((int)iTalk_NumericUpDown1.Value);
            Map existing = Map.GetByID(id);
            if (id <= 0 || OpenMap.Any(other => !ReferenceEquals(other, form) && other.ID == id) ||
                existing != null && !ReferenceEquals(existing, map))
                throw new InvalidOperationException($"L'identifiant de carte {id} est déjà utilisé ou invalide.");
            if (!int.TryParse(sfComboBox1.Text, out int area) || area < 0 ||
                !int.TryParse(sfComboBox2.Text, out int subArea) || subArea < 0)
                throw new FormatException("Area et SubArea doivent être des identifiants entiers positifs ou nuls.");
            int capabilities = (checked((int)capabilitiesMask.Value) & ~15) | Convert.ToInt32(Map.Get_Capabilities(
                iTalk_CheckBox5.Checked, iTalk_CheckBox4.Checked, iTalk_CheckBox3.Checked, iTalk_CheckBox2.Checked), 2);
            bool changed = map.ID != id || map.DateMap != iTalk_TextBox_Small2.Text ||
                map.Ambiance != iTalk_NumericUpDown5.Value || map.NbGroups != iTalk_NumericUpDown6.Value ||
                map.GroupMaxSize != iTalk_NumericUpDown7.Value || map.SuperArea != iTalk_NumericUpDown8.Value ||
                map.Area != area || map.SubArea != subArea || map.X != iTalk_NumericUpDown9.Value ||
                map.Y != iTalk_NumericUpDown10.Value || map.IsOutDoor != iTalk_CheckBox1.Checked ||
                map.Capabilities != capabilities;
            if (map.ID != id)
            {
                form.ClearServerPlacements();
                if (ReferenceEquals(Map.GetByID(map.ID), map)) Map.MapList.Remove(map.ID);
                map.ID = id;
                form.ID = id;
                form.Text = $"MapID: {id}";
                Map.AddMap(map);
            }
            map.DateMap = iTalk_TextBox_Small2.Text;
            map.Ambiance = checked((int)iTalk_NumericUpDown5.Value);
            map.NbGroups = checked((int)iTalk_NumericUpDown6.Value);
            map.GroupMaxSize = checked((int)iTalk_NumericUpDown7.Value);
            map.SuperArea = checked((int)iTalk_NumericUpDown8.Value);
            map.Area = area;
            map.SubArea = subArea;
            map.X = checked((int)iTalk_NumericUpDown9.Value);
            map.Y = checked((int)iTalk_NumericUpDown10.Value);
            map.IsOutDoor = iTalk_CheckBox1.Checked;
            map.Capabilities = capabilities;
            capabilitiesMask.Value = capabilities;
            if (iTalk_ComboBox2.SelectedItem != null)
            {
                string music = iTalk_ComboBox2.SelectedItem.ToString();
                changed |= map.MusiqueName != music;
                map.MusiqueName = music;
                if (int.TryParse(music, out int musicId)) map.Musique = musicId;
            }
            form.Edited |= changed;
        }

        internal void SelectCell(MapForm form, int cellId)
        {
            if (form.MyMap?.Cells == null || cellId < 0 || cellId >= form.MyMap.Cells.Length) return;
            CellsData cell = form.MyMap.Cells[cellId];
            if (cell == null) return;
            selectedCellMap = form;
            selectedCellId = cellId;
            form.SelectedCell=cellId;
            if(T==Tools.Selector && propertySection!=null && propertySection.SelectedIndex<5)propertySection.SelectedIndex=5;
            loadingCellProperties = true;
            try
            {
                iTalk_Label19.Text = cellId.ToString();
                iTalk_Label21.Text = cell.GFX1?.ID.ToString() ?? "-";
                iTalk_Label23.Text = cell.GFX2?.ID.ToString() ?? "-";
                iTalk_Label25.Text = cell.GFX3?.ID.ToString() ?? "-";
                iTalk_CheckBox10.Checked = !cell.UnWalk;
                iTalk_CheckBox9.Checked = cell.Los;
                iTalk_CheckBox8.Checked = cell.Path;
                iTalk_CheckBox7.Checked = cell.Paddock;
                iTalk_CheckBox6.Checked = cell.Door;
                iTalk_CheckBox11.Checked = cell.TriggerCell;
                iTalk_CheckBox12.Checked = cell.IO;
                activeCellCheckBox.Checked = cell.Active;
                movementComboBox.SelectedIndex = cell.Type();
                iTalk_NumericUpDown3.Value = cell.NivSol;
                iTalk_NumericUpDown4.Value = cell.IncliSol;
                PopulateOrientation(cell);
            }
            finally { loadingCellProperties = false; }
        }

        private void ApplyCellProperties()
        {
            if (loadingCellProperties || selectedCellMap?.MyMap?.Cells == null || selectedCellId < 0) return;
            CellsData cell = selectedCellMap.MyMap.Cells[selectedCellId];
            cell.UnWalk = !iTalk_CheckBox10.Checked;
            cell.Los = iTalk_CheckBox9.Checked;
            cell.Path = !cell.UnWalk && iTalk_CheckBox8.Checked;
            cell.Paddock = !cell.UnWalk && iTalk_CheckBox7.Checked;
            cell.Door = iTalk_CheckBox6.Checked;
            cell.TriggerCell = iTalk_CheckBox11.Checked;
            cell.IO = iTalk_CheckBox12.Checked;
            cell.Active = activeCellCheckBox.Checked;
            cell.NivSol = checked((int)iTalk_NumericUpDown3.Value);
            cell.IncliSol = checked((int)iTalk_NumericUpDown4.Value);
            if (cell.UnWalk) cell.FightCell = 0;
            selectedCellMap.Edited = true;
            SelectCell(selectedCellMap, selectedCellId);
            selectedCellMap.DrawAll();
        }

        private void DeleteSelectedLayer(int layer)
        {
            if (selectedCellMap == null || selectedCellId < 0) return;
            selectedCellMap.SelectedCell = selectedCellId;
            selectedCellMap.DeleteTile(layer);
            selectedCellMap.Edited = true;
            SelectCell(selectedCellMap, selectedCellId);
        }

        private void SelectTool(Tools tool, CellMode mode, ToolStripButton selectedButton)
        {
            CellTrigger = -1;
            MapTrigger = 0;
            triggerSource = null;
            EndFightMap = null;
            T = tool;
            CellMod = mode;
            triggersToolStripMenuItem.Checked = false;
            donjonsToolStripMenuItem.Checked = false;
            foreach (var button in new[] { toolStripButton5, toolStripButton6, toolStripButton7,
                toolStripButton8, toolStripButton9, toolStripButton10, toolStripButton11, toolStripButton12 })
                button.Checked = ReferenceEquals(button, selectedButton);
            foreach (MapForm map in OpenMap)
            {
                map.IsBrushTool = tool == Tools.Brush;
                map.IsCellTool = tool == Tools.CellMode;
                map.ModeTrigger = false;
                map.EndFight = false;
            }
            RefreshAllMap();
        }

        private void SelectLayer(int layer)
        {
            Calque = layer;
            toolStripButton13.Checked = layer == 1;
            toolStripButton14.Checked = layer == 2;
        }
        public void GlobalLoading()
        {
            treeView1.Nodes.Clear();
            treeView1.Nodes.Add("Grounds");
            treeView1.Nodes[0].Text = "Exterieur";
            treeView1.Nodes.Add("Objects");
            treeView1.Nodes[1].Text = "Props";
            SearchManager.SearchBackground(@".\ressources\maps\backgrounds");
            SearchManager.SearchGrounds(FolderSols, treeView1.Nodes[0]);
            SearchManager.SearchObject(FolderObjets, treeView1.Nodes[1]);
            SearchManager.SearchZik(@".\ressources\maps\musics");

            foreach (string h in SearchManager.Song)
            {
                iTalk_ComboBox2.Items.Add(h);
            }
            afficherCalque1ToolStripMenuItem.Checked = true;
            afficherCalque2ToolStripMenuItem.Checked = true;
            afficherFondToolStripMenuItem.Checked = true;
            afficherSolToolStripMenuItem.Checked = true;
            afficherGrilleToolStripMenuItem.Checked = Show_Grid;

        }
        public Bitmap Image(string path)
        {
            using (var image = System.Drawing.Image.FromFile(path))
                return new Bitmap(image);

        }






        private void MainEditeur_Load(object sender, EventArgs e)
        {
            GlobalLoading();
            GC.Collect();
        }

        private void treeView1_AfterSelect_1(object sender, TreeViewEventArgs e)
        {
            PopulateTileList(e.Node);
        }
        public void DrawMiniBack(bool D = false)
        {
            if (D)
            {
                pictureBox1.BackgroundImage = null;
            }
            else
            {
                pictureBox1.BackgroundImage = Bi;
            }

        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (MapSelected == null)
            {
                MessageBox.Show("Merci de créer ou d'ouvrir une map avant de selectionner un fond", "Application d'un fond impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            else
            {
                BG_Select B = new BG_Select();
                B.New(MapSelected);
                B.ShowDialog();
                if (B.I != null)
                    Bi = B.I;
                DrawMiniBack();

            }
        }
        private void iTalk_Button_22_Click(object sender, EventArgs e)
        {
            if (MapSelected == null)
            {
                return;
            }
            else
            {
                MapSelected.DrawBackground(null);
                DrawMiniBack(true);

            }
        }

        private void afficherGrilleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Show_Grid == false)
            {
                Show_Grid = true;
                afficherGrilleToolStripMenuItem.Checked = true;
            }
            else
            {
                Show_Grid = false;
                afficherGrilleToolStripMenuItem.Checked = false;
            }
            UpdateGrid(Show_Grid);
            RefreshAllMap();
        }
        private void UpdateGrid(bool grid)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_Grid = grid;
            }
        }

        private void RefreshAllMap()
        {
            foreach (MapForm m in OpenMap)
            {
                m.DrawAll();
            }
        }
        private void UpdateCellIDShowed(bool showCell)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_CellID = showCell;
            }
        }


        private void afficherCellIDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Show_CellID == false)
            {
                Show_CellID = true;
                afficherCellIDToolStripMenuItem.Checked = true;
            }
            else
            {
                Show_CellID = false;
                afficherCellIDToolStripMenuItem.Checked = false;
            }
            UpdateCellIDShowed(Show_CellID);
            RefreshAllMap();
        }

        private void personnaliséeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNewMap(19, 22);
        }

        private void taillePersonnaliséeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var sizeForm = new OtherSizeForm())
                if (sizeForm.ShowDialog(this) == DialogResult.OK && sizeForm.IsValid)
                    OpenNewMap(sizeForm.MapWidth, sizeForm.MapHeight);
        }

        private void toolStripButton20_Click(object sender, EventArgs e)
        {

        }

        private void triggersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MapSelected == null) return;
            bool enable = !triggersToolStripMenuItem.Checked;
            SelectTool(enable ? Tools.CellMode : Tools.Brush, CellMode.Null, enable ? null : toolStripButton5);
            triggersToolStripMenuItem.Checked = enable;
            foreach (MapForm map in OpenMap) map.ModeTrigger = enable;
            if (enable)
            {
                MessageBox.Show("Sélectionnez la cellule de départ puis la cellule d'arrivée du trigger.", "Ajout de triggers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            RefreshAllMap();
        }

        private void donjonsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenMap.Count >= 2 && MapSelected != null)
            {
                bool enable = !donjonsToolStripMenuItem.Checked;
                SelectTool(enable ? Tools.CellMode : Tools.Brush, CellMode.Null, enable ? null : toolStripButton5);
                donjonsToolStripMenuItem.Checked = enable;
                Donjonmode(enable, enable);
                if (enable)
                {
                    MessageBox.Show("Merci de cliquer sur la carte initiale", "Selection de la carte initale EndfightAction", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                RefreshAllMap();
            }
            else
            {
                MessageBox.Show("Il vous faut avoir plus d'une carte ouverte pour créer un donjon.", "Impossible de créer un donjon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        private void Donjonmode(bool iscelltool, bool endfight)
        {

            foreach (MapForm m in OpenMap)
            {
                m.IsCellTool = iscelltool;
                m.EndFight = endfight;
            }

        }
        private void afficherFondToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (afficherFondToolStripMenuItem.Checked == true)
            {
                afficherFondToolStripMenuItem.Checked = false;
                Show_Back = false;
            }
            else
            {
                afficherFondToolStripMenuItem.Checked = true;
                Show_Back = true;
            }
            UpdateShowBack(Show_Back);
            RefreshAllMap();

        }
        private void UpdateShowBack(bool showback)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_Back = showback;
            }

        }
        private void afficherSolToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (afficherSolToolStripMenuItem.Checked == true)
            {
                afficherSolToolStripMenuItem.Checked = false;
                Show_ground = false;
            }
            else
            {
                afficherSolToolStripMenuItem.Checked = true;
                Show_ground = true;
            }
            ShowGround(Show_ground);
            RefreshAllMap();

        }
        private void ShowGround(bool showground)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_ground = showground;
            }

        }
        private void afficherCalque1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (afficherCalque1ToolStripMenuItem.Checked == true)
            {
                afficherCalque1ToolStripMenuItem.Checked = false;
                Show_calque1 = false;
            }
            else
            {
                afficherCalque1ToolStripMenuItem.Checked = true;
                Show_calque1 = true;
            }
            UpdateCalque1(Show_calque1);
            RefreshAllMap();
        }
        private void UpdateCalque1(bool calque1)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_calque1 = calque1;
            }
        }
        private void UpdateCalque2(bool calque2)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_calque2 = calque2;
            }
        }
        private void afficherCalque2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (afficherCalque2ToolStripMenuItem.Checked == true)
            {
                afficherCalque2ToolStripMenuItem.Checked = false;
                Show_calque2 = false;
            }
            else
            {
                afficherCalque2ToolStripMenuItem.Checked = true;
                Show_calque2 = true;
            }
            UpdateCalque2(Show_calque2);
            RefreshAllMap();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            SelectedTile = listView1.SelectedItems[0].Tag as TilesData;
            if (SelectedTile == null) return;
            TilesData.SelectedTiles = SelectedTile;
            SelectTool(Tools.Brush, CellMode.Null, toolStripButton5);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {

        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            PopulateTileList(treeView1.SelectedNode);
        }

        private void PopulateTileList(TreeNode node)
        {
            listView1.Items.Clear();
            string directory = node?.Tag as string;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            TreeNode root = node;
            while (root.Parent != null) root = root.Parent;
            TilesData[] registry = ReferenceEquals(root, treeView1.Nodes[0])
                ? TilesData.ListGrounds : TilesData.ListObject;
            var images = new ImageList { ImageSize = new Size(56, 56), ColorDepth = ColorDepth.Depth32Bit };
            IntPtr imageHandle=images.Handle; // Copy thumbnails before releasing their source bitmaps.
            foreach (TilesData tile in registry.Where(tile => tile != null &&
                string.Equals(Path.GetDirectoryName(tile.Path), directory, StringComparison.OrdinalIgnoreCase))
                .OrderBy(tile => tile.ID))
            {
                try
                {
                    using (Bitmap bitmap = Image(tile.Path)) images.Images.Add(bitmap);
                    listView1.Items.Add(new ListViewItem(tile.ID.ToString(), images.Images.Count - 1) { Tag = tile });
                }
                catch (Exception) { }
            }
            ImageList previous = listView1.LargeImageList;
            listView1.LargeImageList = images;
            listView1.View = View.LargeIcon;
            previous?.Dispose();
        }

        private void afficherFightCellsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (afficherFightCellsToolStripMenuItem.Checked == true)
            {
                afficherFightCellsToolStripMenuItem.Checked = false;
            }
            else
            {
                afficherFightCellsToolStripMenuItem.Checked = true;
            }
        }

        private void toolStripButton5_Click(object sender, EventArgs e)
        {
            SelectTool(Tools.Brush, CellMode.Null, toolStripButton5);
        }

        private void iTalk_Label8_Click(object sender, EventArgs e)
        {

        }

        private void toolStripDropDownButton1_Click(object sender, EventArgs e)
        {

        }

        private void xpTaskBarBox1_ItemClick(object sender, Syncfusion.Windows.Forms.Tools.XPTaskBarItemClickArgs e)
        {

        }

        private void iTalk_Label7_Click(object sender, EventArgs e)
        {

        }

        private void tabPageAdv2_Click(object sender, EventArgs e)
        {

        }


        private void afficherMonstresToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {

        }

        private void toolStripButton3_Click(object sender, EventArgs e)
        {
            using (var dialog = new MapPreferencesForm(SettingsManager.LockSize))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                { SettingsManager.LockSize = dialog.LockSize;foreach(var map in OpenMap)map.FitCanvas(); }
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {

        }
    }
}
