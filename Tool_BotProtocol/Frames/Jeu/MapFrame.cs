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
        public void GetPersoMouvement(TcpClient client, string message)
        {
            Accounts account = client.account;
            Map map = account.Game.Map;
            foreach (string entry in message.Substring(2).TrimStart('|').Split('|'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry[0] == '-')
                {
                    if (int.TryParse(entry.Substring(1), out int removed))
                    {
                        map.Entites.TryRemove(removed, out Entites ignored);
                        account.Game.PersoInWorld.TryRemove(removed, out Dictionary<string, Cell> ignoredPosition);
                        account.Game.Fight?.RemoveFighter(removed);
                    }
                    continue;
                }
                if (entry[0] != '+' && entry[0] != '~') continue;
                string[] info = entry.Substring(1).Split(';');
                if (info.Length < 6 || !short.TryParse(info[0], out short cellId)
                    || !int.TryParse(info[3], out int id) || !int.TryParse(info[5].Split(',')[0], out int type)) continue;
                Cell cell = map.GetCellFromId(cellId);
                if (cell == null) continue;
                account.Game.Fight?.UpdateFighterFromMap(info);
                int.TryParse(info[1], out int orientation);
                orientation = orientation >= 0 && orientation <= 7 ? orientation : 2;
                int gfx = 0, scaleX = 100, scaleY = 100;
                if (info.Length >= 7) ReadGraphics(info[6].Split(',')[0], out gfx, out scaleX, out scaleY);
                string name = info[4];
                account.Game.PersoInWorld[id] = new Dictionary<string, Cell> { { name, cell } };
                if (id == account.Game.character.id)
                {
                    account.Game.character.Cell = cell;
                    account.Game.character.Orientation = orientation;
                    if (gfx > 0) account.Game.character.GFX = gfx;
                    account.Game.character.GraphicsScaleX = scaleX;
                    account.Game.character.GraphicsScaleY = scaleY;
                    continue;
                }
                if (type == -3 && info.Length >= 8)
                {
                    string[] templates = info[4].Split(','), levels = info[7].Split(',');
                    if (templates.Length != levels.Length || !int.TryParse(templates[0], out int template)
                        || !int.TryParse(levels[0], out int level)) continue;
                    int.TryParse(info[2], out int stars);
                    var group = new Monstres(id, template, cell, level, stars);
                    group.Orientation = orientation; group.GFX = gfx > 0 ? gfx : group.GFX;
                    group.GraphicsScaleX = scaleX; group.GraphicsScaleY = scaleY;
                    group.GroupeLeader = group;
                    string[] graphics = info[6].Split(',');
                    for (int i = 0; i < templates.Length; i++)
                    {
                        if (!int.TryParse(templates[i], out template) || !int.TryParse(levels[i], out level)) continue;
                        var member = new Monstres(id, template, cell, level, stars) { Orientation = orientation };
                        if (i < graphics.Length)
                        {
                            ReadGraphics(graphics[i], out int memberGfx, out int memberScaleX, out int memberScaleY);
                            if (memberGfx > 0) member.GFX = memberGfx;
                            member.GraphicsScaleX = memberScaleX; member.GraphicsScaleY = memberScaleY;
                        }
                        group.MobsInGroupe.Add(member);
                    }
                    map.Entites[id] = group;
                }
                else if (type == -2 && int.TryParse(name, out int monsterTemplate))
                {
                    int level = 1; if (info.Length > 7) int.TryParse(info[7], out level);
                    var monster = new Monstres(id, monsterTemplate, cell, level, 0)
                    { Orientation = orientation, GraphicsScaleX = scaleX, GraphicsScaleY = scaleY };
                    if (gfx > 0) monster.GFX = gfx;
                    monster.GroupeLeader = monster; monster.MobsInGroupe.Add(monster);
                    map.Entites[id] = monster;
                }
                else if (type == -4 && int.TryParse(name, out int npc))
                {
                    var entity = new PNJ(id, npc, cell) { Orientation = orientation, GraphicsScaleX = scaleX, GraphicsScaleY = scaleY };
                    if (gfx > 0) entity.GFX = gfx;
                    map.Entites[id] = entity;
                }
                else if ((type > 0 || type == -5) && info.Length >= 8)
                {
                    byte.TryParse(info[7], out byte sex);
                    byte race = type > 0 && type <= byte.MaxValue ? (byte)type : (byte)0;
                    map.Entites[id] = new Personnages(id, name, sex, cell)
                    { Race_ID = race, GFX = gfx > 0 ? gfx : race * 10 + sex, Orientation = orientation,
                        GraphicsScaleX = scaleX, GraphicsScaleY = scaleY };
                }
            }
            map.GetEntitiesRefreshEvent();
        }

        private static void ReadGraphics(string value, out int gfx, out int scaleX, out int scaleY)
        {
            gfx = 0; scaleX = scaleY = 100;
            string[] graphic = (value ?? "").Split('^');
            if (!int.TryParse(graphic[0], out gfx) || gfx < 0) gfx = 0;
            if (graphic.Length < 2) return;
            string[] scale = graphic[1].Split('x');
            if (int.TryParse(scale[0], out int horizontal)) scaleX = scaleY = Math.Max(0, Math.Min(500, horizontal));
            if (scale.Length > 1 && int.TryParse(scale[1], out int vertical)) scaleY = Math.Max(0, Math.Min(500, vertical));
        }

        [MessageAttribution("GA")]
        public async Task InitGA(TcpClient client, string message)
        {
            string[] part = message.Substring(2).Split(';');
            if (part.Length < 2 || !int.TryParse(part[1], out int action)) return;
            Accounts account = client.account;
            Map map = account.Game.Map;
            if (account.Game.Fight.IsInFight)
            {
                await account.Game.Fight.ProcessActionAsync(client, part);
                return;
            }
            if (action == 0)
            {
                account.Game.Manager.Mouvements.AcutaliseMove(false);
                return;
            }
            if (part.Length < 4 || !int.TryParse(part[2], out int entityId)) return;
            Cell cell = null;
            if (action == 1 && part[3].Length >= 3 && part[3].Length % 3 == 0)
            {
                List<Cell> serverPath = PathfinderUtils.DecodeServerPath(map, part[3]);
                if (serverPath == null || serverPath.Count == 0) return;
                cell = serverPath.Last();
                map.NotifyEntityMovement(entityId, serverPath, PathfinderUtils.GetTimeOnMap(serverPath[0], serverPath));
                if (entityId == account.Game.character.id && !account.IsFighting()
                    && int.TryParse(part[0], out int movementId))
                {
                    account.Game.Manager.Mouvements.ActualPath = serverPath;
                    await account.Game.Manager.Mouvements.EventMoveFisnish(cell, movementId, true);
                }
                if (account.Game == null || account.AccountStates == AccountStates.DISCONNECTED
                    || !ReferenceEquals(client, account.Connexion) || map.GetCellFromId(cell.CellID) != cell) return;
            }
            else if (action == 4)
            {
                // StarLoco uses <target-guid>,<cell> for relocation effects.
                string[] relocation = part[3].Split(',');
                if (relocation.Length < 2 || !int.TryParse(relocation[0], out entityId)
                    || !short.TryParse(relocation[1], out short cellId)) return;
                cell = map.GetCellFromId(cellId);
            }
            if (cell == null) return;
            if (entityId == account.Game.character.id) account.Game.character.Cell = cell;
            else if (map.Entites.TryGetValue(entityId, out Entites entity)) entity.Cell = cell;
            if (account.Game.PersoInWorld.TryGetValue(entityId, out Dictionary<string, Cell> position))
            {
                string name = position.Keys.FirstOrDefault();
                if (name != null) account.Game.PersoInWorld[entityId] = new Dictionary<string, Cell> { { name, cell } };
            }
            map.GetEntitiesRefreshEvent();
        }

        [MessageAttribution("GAF")]
        public Task FinalizeAction(TcpClient client, string message)
        {
            string[] parts = message.Substring(3).Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int actionId)
                || actionId < 0 || !int.TryParse(parts[1], out int actorId) || actorId != client.account.Game.character.id)
                return Task.CompletedTask;
            client.account.Game.Fight.EndAction(actorId);
            return client.SendPacket("GKK" + actionId);
        }
        [MessageAttribution("GAS")]
        public void GetAction(TcpClient client, string message) => client.account.Game.Fight.BeginAction(message.Substring(3));

        [MessageAttribution("GDF")]
        public void GetinteractiveState(TcpClient client, string message)
        {
            Accounts account = client.account;
            foreach (string entry in message.Substring(3).TrimStart('|').Split('|'))
            {
                string[] parts = entry.Split(';');
                if (parts.Length < 2 || !short.TryParse(parts[0], out short cellId)
                    || !byte.TryParse(parts[1], out byte state)
                    || !account.Game.Map.Interactives.TryGetValue(cellId, out var value)) continue;
                value.IsUsable = state == 1;
                if (state == 3 && value.Interactive?.Capacities != null)
                {
                    if (value.Interactive.Capacities.Contains((short)157)) account.Game.Manager.Teleport.InitTeleport();
                    else account.Game.Manager.Recolte.EventEndRecolte(account.IsGathering()
                        ? Game.Managers.recoltes.RecolteEnum.RECOLTÉ : Game.Managers.recoltes.RecolteEnum.VOLÉ, cellId);
                }
            }
            account.Game.Map.GetEntitiesRefreshEvent();
        }
        [MessageAttribution("GDM")]
        public async Task GetNewMap(TcpClient client, string message)
        {
            Accounts account = client.account;
            account.Game.Manager.Mouvements.CancelForMapChange();
            account.Game.character.Cell = null;
            account.Game.PersoInWorld.Clear();
            account.Game.Map.SetRefreshMap(message.Substring(3).TrimStart('|'));
            if (!account.Game.Map.HasMapData) account.Logger.LogError("CARTE", account.Game.Map.LoadError);
            await client.SendPacket("GI");
        }
        [MessageAttribution("GDK")]
        public void GetMap(TcpClient client, string message) => client.account.Game.Map.GetMapRefreshEvent();
        [MessageAttribution("GV")]
        public Task Reinit(TcpClient client, string message)
        {
            client.account.Game.Fight.Finish();
            return client.SendPacket("GC1");
        }
    }
}
