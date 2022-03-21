using Infragistics.Win.UltraWinLiveTileView;
using Syncfusion.WinForms.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AzurToolRetro.Forms
{
    public partial class MainForm : SfForm
    {
        public Color GridAlternateRowBackColor = Color.FromArgb(128, 54, 54, 54);
        public Color GridRowBackColor = Color.FromArgb(128, 41, 39, 39);
        public Color TracksSoldBackColor = Color.FromArgb(61, 167, 172);
        public Color NewsBackColor = Color.FromArgb(112, 29, 91);
        public MainForm()
        {
            InitializeComponent();
            InitTiles();
        }
        private void InitTiles()
        {
            ultraLiveTileView1.Groups.Add("search").Text = "Recherches";
            ultraLiveTileView1.Groups.Add("editor").Text = "Éditeurs";
            ultraLiveTileView1.Groups.Add("options").Text = "Paramètres";

            TileGroup TAccounts = ultraLiveTileView1.Groups["editor"];
            LiveTile AccountTiles = new LiveTile();
            AccountTiles.Key = "accounteditor";
            AccountTiles.Appearance.Normal.BackColor = TracksSoldBackColor;
            AccountTiles.Sizing = TileSizing.Wide;
            AccountTiles.CurrentSize = TileSize.Wide;
            TAccounts.Tiles.Add(AccountTiles);

            LiveTileFrameWide topTracksSoldWideFrame = new LiveTileFrameWide();
            LiveTileWideCustomContent liveTileWideCustomContent = new LiveTileWideCustomContent();
            LiveTileContentTextElement blockText = new LiveTileContentTextElement();
            blockText.Text = "Éditeur de comptes";
            blockText.TextSize = TileTemplateTextSize.Block;
            blockText.Location = new Point(10, 10);
            liveTileWideCustomContent.Elements.Add(blockText);
            topTracksSoldWideFrame.Content = liveTileWideCustomContent;
            AccountTiles.DefaultView.WideFrames.Add(topTracksSoldWideFrame);


        }
    }
}
