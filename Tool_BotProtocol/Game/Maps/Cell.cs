using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Maps.Enums;
using Tool_BotProtocol.Game.Maps.Interactives;

namespace Tool_BotProtocol.Game.Maps
{
    public class Cell
    {
        public short CellID { get; set; } = 0;
        public bool IsActive { get; private set; } = false;
        public CellTypes C_Types { get; set; } = CellTypes.NOT_WALKABLE;
        public bool LineofSight { get; set; } = false;
        public byte layer_ground_Level { get; private set; }
        public byte layer_ground_slope { get; private set; }
        public short layer_object_1_num { get; private set; }
        public short layer_object_2_num { get; private set; }
        public Interactives.Interactives Interactives { get; private set; }
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int H { get; set; } = 0;
        public int F { get; set; } = 0;
        public int G { get; set; } = 0;
        public Cell Node { get; set; } = null;
        public static readonly int[] TeleportTexturesSpritesId = { 1030, 1029, 1764, 2298, 745 };

        public Cell(short cellid, bool active, CellTypes cellTypes, bool sight, byte level, byte slope, short interactiveID, short layer1, short layer2, Map M)
        {
            CellID = cellid;
            IsActive = active;
            C_Types = cellTypes;
            layer_object_1_num = layer1;
            layer_object_2_num = layer2;
            LineofSight = sight;
            layer_ground_Level = level;
            layer_ground_slope = slope;

            Interactives = new Interactives.Interactives(interactiveID, this);
            if(interactiveID != -1)
            {
                Interactives = new Interactives.Interactives(interactiveID, this);
                M.Interactives.TryAdd(interactiveID, Interactives);
            }
            byte mapwidth = M.MapWidth;
            int L5 = cellid / ((mapwidth * 2) - 1);
            int L6  = cellid - (L5 *((mapwidth * 2) - 1));
            int L7 = L6 % mapwidth;
            Y = L5 - L7;
            X = (cellid - ((mapwidth - 1) * Y)) / mapwidth;
            
        }
        public int GetDistanceBetweenCells (Cell Destination) => Math.Abs(X - Destination.X) + Math.Abs(Y - Destination.Y);
        public bool AreCellsOnline (Cell Destination) => X == Destination.X || Y == Destination.Y;

        public char GetCharDirection (Cell Cell)
        {
            if(X == Cell.X)
            {
                return Cell.Y < Y ?(char)(3 + 'a') : (char)(7 + 'a');
            }else if(Y == Cell.Y)
            {
                return Cell.X < X ? (char)(1 + 'a') : (char)(5 + 'a');
            }else if(X > Cell.X)
            {
                return Y > Cell.Y ? (char)(2 + 'a') : (char)(0 + 'a');
            }else if(X < Cell.X)
            {
                return Y < Cell.Y ? (char)(6 + 'a') : (char)(4 + 'a');
            }
            throw new Exception("Direction non trouvée");
        }
        public bool IsTrigger()
        {
            if (TeleportTexturesSpritesId.Contains(layer_object_1_num) || TeleportTexturesSpritesId.Contains(layer_object_2_num))
                return true;
            return false;
        }
            
        public bool IsInteractiveCell() => C_Types == CellTypes.INTERACTIVE_OBJECT || Interactives.gfx != -1;
        public bool IsWalkable() => IsActive && C_Types != CellTypes.NOT_WALKABLE;

    }
}
