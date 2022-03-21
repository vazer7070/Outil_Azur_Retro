using Outil_Azur_complet.Bot.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Mouvements;

namespace Outil_Azur_complet.Bot.Interfaces
{
    public partial class MapControl : UserControl
    {
        private Accounts Account = null;
        public MapControl(Accounts A)
        {
            InitializeComponent();
            Account = A;
           UserMap.SetAccount(Account);

        }

        private void MapControl_Load(object sender, EventArgs e)
        {
            UserMap.CellClicked += UserMapClic;
            Account.Game.Map.RefreshMap += MapChange;
            Account.Game.Map.RefreshEntities += () => UserMap.Invalidate();
            Account.Game.character.MoveMinimapPathfinding += GetPathfinding;
            BeginInvoke((Action)(() =>
            {
                MapChange();
                iTalk_Label2.Text = Account.Game.Map.GetCoordinates;
            }));

        }
        
        private void MapChange()
        {
            Map M = Account.Game.Map;

            int Actual_largeur = UserMap.W, Actual_Longueur = UserMap.H;
            int NewLargeur = M.MapWidth, NewLongueur = M.MapHeight;

            if(Actual_largeur != NewLargeur || Actual_Longueur != NewLongueur)
            {
                UserMap.W = NewLargeur;
                UserMap.H = NewLongueur;

                UserMap.SetCellNum();
                UserMap.DrawGrille();
            }
            BeginInvoke((Action)(() =>
            {
                iTalk_Label2.Text = Account.Game.Map.GetCoordinates;
            }));
            UserMap.RefreshMap();
        }

        private void UserMapClic(UserMapCell cell, MouseButtons buttons, bool A)
        {
            Map M = Account.Game.Map;
            
            Cell C = Account.Game.character.Cell, C2 = M.GetCellFromId(cell.id);

            if(buttons == MouseButtons.Left && C.CellID != 0 && C2.CellID != 0 && !A)
            {
               // MessageBox.Show($"C: {C.CellID} // C2: {C2.CellID}");
              switch(Account.Game.Manager.Mouvements.GetCellsMove(C2, M.CellsOccuped()))
                {
                    case MoveResults.EXIT:
                        Account.Logger.LogInfo("MAP", $"Le personnage se déplace vers la cellule {C2.CellID}");
                        break;
                    case MoveResults.SAMECELL:
                        Account.Logger.LogError("MAP", "Vous êtes déjà sur la cellule de destination");
                        break;
                        default:
                        Account.Logger.LogError("MAP", $"Impossible de se déplacer sur la cellule {C2.CellID} cause: [{Account.Game.Manager.Mouvements.GetCellsMove(C2, M.CellsOccuped())}]");
                        break;
                }
            }
        }
        private void GetPathfinding(List<Cell> Cellslist) => Task.Run(() => UserMap.AddAnimations(Account.Game.character.id, Cellslist, PathfinderUtils.GetTimeOnMap(Cellslist.First(), Cellslist), AnimationType.PERSONNAGE));

    }
}
