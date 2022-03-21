using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps.Enums;

namespace Tool_BotProtocol.Utils.Extensions
{
    public static  class Extensions
    {
        public static string Player_Status(this AccountStates accountStatus)
        {
            switch (accountStatus)
            {
                case AccountStates.CONNECTED:
                    return "Connecté";
                case AccountStates.DISCONNECTED:
                    return "Deconnecté";
                case AccountStates.EXCHANGE:
                    return "Echange";
                case AccountStates.FIGHTING:
                    return "Combat";
                case AccountStates.GATHERING:
                    return "Recolte";
                case AccountStates.MOVING:
                    return "Deplacement";
                case AccountStates.CONNECTED_INACTIVE:
                    return "Inactif";
                case AccountStates.STORAGE:
                    return "Stockage";
                case AccountStates.DIALOG:
                    return "Dialogue";
                case AccountStates.BUYING:
                    return "Achat";
                case AccountStates.SELLING:
                    return "Vente";
                case AccountStates.REGENERATION:
                    return "Régéneration";
                default:
                    return "-";
            }
        }
        public static T get_Or<T>(this Table table, string key, DataType type, T orValue)
        {
            DynValue flag = table.Get(key);

            if (flag.IsNil() || flag.Type != type)
                return orValue;

            try
            {
                return (T)flag.ToObject(typeof(T));
            }
            catch
            {
                return orValue;
            }
        }
        public static Dictionary<TeleportCellsEnum, List<short>> Add(this Dictionary<TeleportCellsEnum, List<short>> cells, short cellId)
        {
            short[] topCells = new short[] { 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 36 };
            short[] rightCells = new short[] { 28, 57, 86, 115, 144, 173, 231, 202, 260, 289, 318, 347, 376, 405, 434 };
            short[] bottomCells = new short[] { 451, 452, 453, 454, 455, 456, 457, 458, 459, 460, 461, 462, 463 };
            short[] leftCells = new short[] { 15, 44, 73, 102, 131, 160, 189, 218, 247, 276, 305, 334, 363, 392, 421, 450 };

            if (topCells.Contains(cellId))
            {
                if (cells.ContainsKey(TeleportCellsEnum.TOP))
                    cells[TeleportCellsEnum.TOP].Add(cellId);
                else
                {
                    cells.Add(TeleportCellsEnum.TOP, new List<short>());
                    cells[TeleportCellsEnum.TOP].Add(cellId);
                }
            }

            if (rightCells.Contains(cellId))
            {
                if (cells.ContainsKey(TeleportCellsEnum.RIGHT))
                    cells[TeleportCellsEnum.RIGHT].Add(cellId);
                else
                {
                    cells.Add(TeleportCellsEnum.RIGHT, new List<short>());
                    cells[TeleportCellsEnum.RIGHT].Add(cellId);
                }
            }

            if (bottomCells.Contains(cellId))
            {
                if (cells.ContainsKey(TeleportCellsEnum.BOTTOM))
                    cells[TeleportCellsEnum.BOTTOM].Add(cellId);
                else
                {
                    cells.Add(TeleportCellsEnum.BOTTOM, new List<short>());
                    cells[TeleportCellsEnum.BOTTOM].Add(cellId);
                }
            }

            if (leftCells.Contains(cellId))
            {
                if (cells.ContainsKey(TeleportCellsEnum.LEFT))
                    cells[TeleportCellsEnum.LEFT].Add(cellId);
                else
                {
                    cells.Add(TeleportCellsEnum.LEFT, new List<short>());
                    cells[TeleportCellsEnum.LEFT].Add(cellId);
                }
            }

            return cells;
        }
    }
}
