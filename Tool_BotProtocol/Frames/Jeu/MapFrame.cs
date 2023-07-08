using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Frames.Jeu
{
    internal class MapFrame : Frame
    {
        [MessageAttribution("GM")]
        public Task GetPersoMouvement(TcpClient client, string message) => Task.Factory.StartNew(async () =>
        {
            Accounts A = client.account;
            string[] part = message.Substring(3).Split('|'), Info;
            string loc, TemplateName, type, monsterstar;
            for (int i = 0; i < part.Length; ++i)
            {
                loc = part[i];
                if (loc.Length != 0)
                {
                    Info = loc.Substring(1).Split(';');
                    if (loc[0].Equals('+'))
                    {
                        Cell cell = A.Game.Map.GetCellFromId(short.Parse(Info[0]));
                        Fights fight = A.Game.Fight;
                        int id = int.Parse(Info[3]);
                        Dictionary<string, Cell> Predico = new Dictionary<string, Cell>();
                        Predico.Add(message.Split(';')[4], cell);
                        if (!A.Game.PersoInWorld.ContainsKey(id))
                            A.Game.PersoInWorld.GetOrAdd(id, Predico);
                        TemplateName = Info[4];
                        type = Info[5];
                        if (type.Contains(","))
                            type = type.Split(',')[0];

                        switch (int.Parse(type))
                        {
                            case -1:
                            case -2:
                                if (A.AccountStates == AccountStates.FIGHTING)
                                {
                                    int vie = int.Parse(Info[12]);
                                    byte PA = byte.Parse(Info[13]);
                                    byte PM = byte.Parse(Info[14]);
                                    byte E = byte.Parse(Info[15]);

                                    //mettre combat
                                }
                                break;

                            case -3: //monstres
                                string[] Template = TemplateName.Split(',');
                                string[] Level = Info[7].Split(',');
                                monsterstar = Info[2];
                                int star = int.Parse(monsterstar);
                                Monstres monstres = new Monstres(id, int.Parse(Template[0]), cell, int.Parse(Level[0]), star);
                                monstres.GroupeLeader = monstres;

                                for (int m = 0; m < Template.Length; ++m)
                                    monstres.MobsInGroupe.Add(new Monstres(id, int.Parse(Template[m]), cell, int.Parse(Level[m]), monstres.GroupeLeader.Star));
                                A.Game.Map.Entites.TryAdd(id, monstres);

                                break;
                            case -4: //PNJ
                                A.Game.Map.Entites.TryAdd(id, new PNJ(id, int.Parse(TemplateName), cell));
                                break;
                            case -5:
                            case -6:
                            case -8:
                            case -9:
                            case -10:
                                break;
                            default:
                                if (A.AccountStates != AccountStates.FIGHTING)
                                {
                                    if (A.Game.character.id != id)
                                        A.Game.Map.Entites.GetOrAdd(id, new Personnages(id, TemplateName, byte.Parse(Info[7].ToString()), cell));
                                    else
                                        A.Game.character.Cell = cell;

                                }
                                else
                                {
                                    int vie = int.Parse(Info[14]);
                                    byte PA = byte.Parse(Info[15]);
                                    byte PM = byte.Parse(Info[16]);
                                    byte E = byte.Parse(Info[24]);

                                    //ajouter combat ici

                                    await Task.Delay(1800);
                                    await A.Connexion.SendPacket("GR1");
                                }
                                break;
                        }
                    } else if (loc[0].Equals('-'))
                    {
                        if (A.AccountStates != AccountStates.FIGHTING)
                        {
                            int id = int.Parse(loc.Substring(1));
                            A.Game.Map.Entites.TryRemove(id, out Entites E);
                        }
                    }
                }
            }

        }, TaskCreationOptions.LongRunning);

        [MessageAttribution("GA")]
        public Task InitGA(TcpClient client, string message) => Task.Run(async () =>
        {
            string[] part = message.Substring(2).Split(';');
            int idACtion = int.Parse(part[1]);
            Accounts A = client.account;
            CharacterClass perso = A.Game.character;

            if (idACtion > 0)
            {
                Dictionary<string, Cell> T = new Dictionary<string, Cell>();
                string name = "";

                int IDEntite = int.Parse(part[2]);
                byte MoveType;
                Cell cell;
                //combattants
                Map map = A.Game.Map;
                Fights fight = A.Game.Fight;
                if (T.Count > 0)
                    T.Clear();
                switch (idACtion)
                {
                    case 1:
                        cell = map.GetCellFromId(Hash.Get_Cell_From_Hash(part[3].Substring(part[3].Length - 2)));
                        if (!A.IsFighting())
                        {
                            if (IDEntite == perso.id && cell.CellID > 0 && perso.Cell.CellID != cell.CellID)
                            {
                                MoveType = byte.Parse(part[0]);
                                await A.Game.Manager.Mouvements.EventMoveFisnish(cell, MoveType, true);
                            }
                            else if (map.Entites.TryGetValue(IDEntite, out Entites E))
                            {
                                E.Cell = cell;
                                if (A.Game.PersoInWorld.ContainsKey(IDEntite))
                                {
                                    T = A.Game.PersoInWorld[IDEntite];
                                    name = T.Keys.FirstOrDefault();
                                    Cell U = T.Values.FirstOrDefault();

                                    if (U != null || T.Count != 0)
                                    {
                                        if (U != cell)
                                        {
                                            U = cell;
                                            T.Remove(name);
                                            T.Add(name, U);
                                            A.Game.PersoInWorld.TryUpdate(IDEntite, T, T);
                                        }
                                    }
                                    A.Logger.LogInfo("MONDE", $"Mouvement détecté sur la cellule {cell.CellID} par l'entité {name}");
                                }
                                else
                                    A.Logger.LogInfo("MONDE", $"Mouvement détecté sur la cellule {cell.CellID} par l'entité {E.id}");
                            }
                            else
                            {
                                A.Logger.LogInfo("MONDE", $"Mouvement détecté sur la cellule {cell.CellID} par l'entité {IDEntite}");
                            }
                            map.GetEntitiesRefreshEvent();
                        }
                        else
                        {
                            //combattants ici
                        }
                        break;
                    case 4:
                        part = part[3].Split(';');
                        cell = map.GetCellFromId(short.Parse(part[1]));
                        if (!A.IsFighting() && IDEntite == perso.id && cell.CellID > 0 && perso.Cell.CellID != cell.CellID)
                        {
                            perso.Cell = cell;
                            await Task.Delay(150);
                            await A.Connexion.SendPacket("GKK1");
                            map.GetEntitiesRefreshEvent();
                            A.Game.Manager.Mouvements.AcutaliseMove(true);
                        }
                        break;

                }
            }

        });

        [MessageAttribution("GAF")]
        public Task FinalizeAction(TcpClient client, string message) => Task.Run(async () =>
        {
            {
                string[] idEndAction = message.Substring(3).Split('|');
                await client.account.Connexion.SendPacket($"GKK{idEndAction[0]}");
            }
        });
        [MessageAttribution("GAS")]
        public async Task GetAction(TcpClient client, string message) => await Task.Delay(200);

        [MessageAttribution("GDF")]
        public Task GetinteractiveState(TcpClient client, string message) => Task.Factory.StartNew(() =>
        {
            if (message.Contains("|"))
            {
                foreach (string I in message.Substring(4).Split('|'))
                {
                    string[] part = I.Split(';');
                    if (part.Length < 2)
                        return;
                    Accounts A = client.account;
                    short cellid = short.Parse(part[0]);
                    byte state = byte.Parse(part[1]);

                    switch (state)
                    {
                        case 2:
                            A.Game.Map.Interactives[cellid].IsUsable = false;
                            break;
                        case 3:
                            if (A.Game.Map.Interactives.TryGetValue(cellid, out var value))
                            {
                                if (value.Interactive.Capacities[0] == 157)
                                    A.Game.Manager.Teleport.InitTeleport();
                                else
                                {
                                    value.IsUsable = false;
                                    if (A.IsGathering())
                                        A.Game.Manager.Recolte.EventEndRecolte(Game.Managers.recoltes.RecolteEnum.RECOLTÉ, cellid);
                                    else
                                        A.Game.Manager.Recolte.EventEndRecolte(Game.Managers.recoltes.RecolteEnum.VOLÉ, cellid);
                                }
                            }
                            break;
                        case 4:
                            A.Game.Map.Interactives[cellid].IsUsable = false;
                            break;
                    }
                }
            }
        }, TaskCreationOptions.LongRunning);

        [MessageAttribution("GDM")]
        public Task GetNewMap(TcpClient client, string message) => Task.Run(async () =>
        {
            { 
            await client.account.Connexion.SendPacket("GI");
            client.account.Game.Map.SetRefreshMap(message.Substring(4));

        } });
        [MessageAttribution("GDK")]
        public void GetMap(TcpClient client, string message) => client.account.Game.Map.GetMapRefreshEvent();
        [MessageAttribution("GV")]
        public Task Reinit(TcpClient client, string message) => Task.Run(async() => await client.account.Connexion.SendPacket("GC1"));
    }
}
