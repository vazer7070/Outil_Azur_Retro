using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Carte et acteurs : <c>GM</c> (par type, comme <c>Game.onMovement</c>), <c>GA</c> hors combat (0 refus, 1 déplacement,
    /// 2 changement de carte, les autres via <see cref="GameActionRouter"/>), <c>GDM</c>/<c>GI</c>, <c>GDF</c>, <c>GDO</c>,
    /// <c>GDC</c>, <c>eD</c>, <c>Oa</c> et <c>Gc</c>. Les paquets mal formés sont journalisés ou ignorés, jamais fatals.
    /// </summary>
    internal class MapFrame : Frame
    {
        [MessageAttribution("GM")]
        public void GetPersoMouvement(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account?.Game == null) return;
            Map map = account.Game.Map;
            CharacterClass character = account.Game.character;
            GmParseResult parsed = GmParser.Parse(message, account.Game.Fight?.IsInFight == true, character.id, character.Cell?.CellID ?? -1);
            foreach (string rejected in parsed.Rejected) account.Logger?.LogDanger("CARTE", "Entrée GM ignorée : " + rejected);
            var notifications = new List<Action>();
            foreach (GmEntry entry in parsed.Entries)
            {
                if (entry.Operation == GmOperation.Remove)
                {
                    int legacyId = unchecked((int)entry.Id);
                    account.Game.PersoInWorld.TryRemove(legacyId, out Dictionary<string, Cell> ignoredPosition);
                    account.Game.Fight?.RemoveFighter(legacyId);
                    if (map.Self != null && map.Self.Id == entry.Id) map.ClearSelf();
                    MapActor removed = map.TakeActor(entry.Id, out bool found);
                    if (removed != null) notifications.Add(() => map.NotifyActorRemoved(removed));
                    continue;
                }
                MapActor actor = entry.Actor;
                Cell cell = ResolveCell(map, actor.CellId);
                if (cell != null) actor.Cell = cell;
                account.Game.Fight?.UpdateFighterFromMap(entry.Fields);
                account.Game.PersoInWorld[unchecked((int)actor.Id)] = new Dictionary<string, Cell> { { actor.Name ?? string.Empty, cell } };
                if (actor.IsSelf)
                {
                    if (cell != null) character.Cell = cell;
                    character.Orientation = actor.Orientation;
                    if (actor.Gfx > 0) character.GFX = actor.Gfx;
                    character.GraphicsScaleX = actor.ScaleX;
                    character.GraphicsScaleY = actor.ScaleY;
                    map.SetSelf(actor);
                    notifications.Add(() => map.NotifyActorUpdated(actor));
                    continue;
                }
                bool replaced = map.PutActor(actor);
                notifications.Add(replaced ? (Action)(() => map.NotifyActorUpdated(actor)) : () => map.NotifyActorAdded(actor));
            }
            map.GetEntitiesRefreshEvent();
            foreach (Action notify in notifications) notify();
        }

        private static Cell ResolveCell(Map map, int cellId) =>
            cellId < 0 || cellId > short.MaxValue ? null : map.GetCellFromId((short)cellId);

        [MessageAttribution("GA")]
        public async Task InitGA(TcpClient client, string message)
        {
            string[] part = message.Substring(2).Split(';');
            if (part.Length < 2 || !int.TryParse(part[1], out int action)) return;
            Accounts account = client.account;
            if (account?.Game == null) return;
            if (account.Game.Fight.IsInFight)
            {
                await account.Game.Fight.ProcessActionAsync(client, part);
                return;
            }
            GameActionPacket packet = GameActionPacket.Parse(message);
            if (packet == null) return;
            switch (action)
            {
                case 0:
                    // GA;0 : action refusée par le serveur (chemin invalide, cible occupée…).
                    account.Game.Manager.Mouvements.AcutaliseMove(false);
                    account.Logger?.LogDanger("CARTE", "Action refusée par le serveur (GA;0).");
                    return;
                case 1:
                    await ServerMoveAsync(client, account, packet);
                    return;
                case 2:
                    LeaveMap(account, packet);
                    return;
            }
            await GameActionRouter.DispatchAsync(new GameActionContext(client, packet));
        }

        /// <summary>
        /// <c>GA&lt;id&gt;;1;&lt;acteur&gt;;a&lt;cellule&gt;&lt;chemin&gt;</c> : StarLoco préfixe le chemin par la cellule de départ et n'envoie
        /// pas de <c>GAF</c> hors combat ; le personnage du compte acquitte par un seul <c>GKK&lt;id&gt;</c> calculé localement.
        /// </summary>
        private static async Task ServerMoveAsync(TcpClient client, Accounts account, GameActionPacket packet)
        {
            Map map = account.Game.Map;
            CharacterClass character = account.Game.character;
            long actorId = packet.ActorId ?? (packet.Actor.Length == 0 ? character.id : long.MinValue);
            if (actorId == long.MinValue || !GmParser.TryParseMovePath(packet.Parameters, out ServerMovePath move)) return;
            List<Cell> serverPath = PathfinderUtils.DecodeServerPath(map, move.Encoded);
            if (serverPath == null || serverPath.Count == 0) return;
            Cell cell = serverPath.Last();
            map.NotifyEntityMovement(unchecked((int)actorId), serverPath, PathfinderUtils.GetTimeOnMap(serverPath[0], serverPath));
            bool self = actorId == character.id;
            if (self && !account.IsFighting() && int.TryParse(packet.GameActionId, out int movementId))
            {
                account.Game.Manager.Mouvements.ActualPath = serverPath;
                await account.Game.Manager.Mouvements.EventMoveFisnish(cell, movementId, true);
            }
            if (account.Game == null || account.AccountStates == AccountStates.DISCONNECTED
                || !ReferenceEquals(client, account.Connexion) || map.GetCellFromId(cell.CellID) != cell) return;
            MapActor actor = self ? map.Self : map.GetActor(actorId);
            if (self) character.Cell = cell;
            if (actor != null)
            {
                actor.Cell = cell;
                actor.Orientation = move.FinalOrientation;
            }
            UpdateWorldPosition(account, actorId, cell);
            map.GetEntitiesRefreshEvent();
            if (actor != null) map.NotifyActorUpdated(actor);
        }

        /// <summary>
        /// <c>GA;2;&lt;acteur&gt;;[cinématique]</c> : envoyé au joueur avant <c>GM|-</c> et <c>GDM</c> à chaque téléportation.
        /// Pour le personnage du compte, la carte est vidée comme dans le client (<c>clearGame</c>) en attendant <c>GDM</c>.
        /// </summary>
        private static void LeaveMap(Accounts account, GameActionPacket packet)
        {
            Map map = account.Game.Map;
            long actorId = packet.ActorId ?? account.Game.character.id;
            if (actorId == account.Game.character.id)
            {
                account.Game.Manager.Mouvements.CancelForMapChange();
                account.Game.PersoInWorld.Clear();
                map.ClearActors();
                map.GetEntitiesRefreshEvent();
                map.NotifyMapChanging(packet.Parameters);
                return;
            }
            account.Game.PersoInWorld.TryRemove(unchecked((int)actorId), out Dictionary<string, Cell> ignored);
            MapActor removed = map.TakeActor(actorId, out bool found);
            map.GetEntitiesRefreshEvent();
            if (removed != null) map.NotifyActorRemoved(removed);
        }

        /// <summary><c>GA;4;&lt;lanceur&gt;;&lt;cible&gt;,&lt;cellule&gt;</c> : repositionnement immédiat d'un acteur (téléportation courte).</summary>
        [GameActionHandler(4)]
        private static void Relocate(GameActionContext context)
        {
            Accounts account = context.Account;
            if (account?.Game == null) return;
            string[] relocation = context.Parameters.Split(',');
            if (relocation.Length < 2 || !long.TryParse(relocation[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actorId)
                || !short.TryParse(relocation[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out short cellId)) return;
            Map map = account.Game.Map;
            Cell cell = map.GetCellFromId(cellId);
            if (cell == null) return;
            MapActor actor = actorId == account.Game.character.id ? map.Self : map.GetActor(actorId);
            if (actorId == account.Game.character.id) account.Game.character.Cell = cell;
            if (actor != null) actor.Cell = cell;
            UpdateWorldPosition(account, actorId, cell);
            map.GetEntitiesRefreshEvent();
            if (actor != null) map.NotifyActorUpdated(actor);
        }

        private static void UpdateWorldPosition(Accounts account, long actorId, Cell cell)
        {
            int legacyId = unchecked((int)actorId);
            if (account.Game.PersoInWorld.TryGetValue(legacyId, out Dictionary<string, Cell> position))
            {
                string name = position.Keys.FirstOrDefault();
                if (name != null) account.Game.PersoInWorld[legacyId] = new Dictionary<string, Cell> { { name, cell } };
            }
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

        /// <summary><c>GDF|&lt;cellule&gt;;&lt;état&gt;[;&lt;1|0&gt;]|…</c> : tous les triplets ; états 1 à 5 conservés dans <c>Map.ObjectStates</c>.</summary>
        [MessageAttribution("GDF")]
        public void GetinteractiveState(TcpClient client, string message)
        {
            Accounts account = client.account;
            Map map = account.Game.Map;
            // Map.ObjectStateChanged : le service de récolte (M3) met à jour Interactives (image, utilisable).
            foreach (InteractiveObjectState state in GmParser.ParseObjectStates(message.Substring(3)))
                map.SetObjectState(state);
            map.GetEntitiesRefreshEvent();
        }

        /// <summary><c>GDO+&lt;cellule&gt;;&lt;modèle&gt;;&lt;type&gt;</c> / <c>GDO-&lt;cellule&gt;;…</c> : objets posés au sol.</summary>
        [MessageAttribution("GDO")]
        public void GetGroundObjects(TcpClient client, string message)
        {
            Map map = client.account?.Game?.Map;
            if (map == null) return;
            if (!GmParser.TryParseGroundObjects(message.Substring(3), out bool added, out List<GroundObject> objects))
            {
                client.account.Logger?.LogDanger("CARTE", "Objet au sol illisible : " + Shorten(message));
                return;
            }
            foreach (GroundObject item in objects)
            {
                if (added) map.SetGroundObject(item);
                else map.RemoveGroundObject(item.CellId);
            }
        }

        /// <summary><c>GDC&lt;cellule&gt;;&lt;données&gt;;&lt;permanent&gt;</c> : cellule modifiée (portes de labyrinthe…) ou restaurée.</summary>
        [MessageAttribution("GDC")]
        public void GetCellData(TcpClient client, string message)
        {
            Map map = client.account?.Game?.Map;
            if (map == null) return;
            foreach (CellUpdate update in GmParser.ParseCellUpdates(message.Substring(3))) map.ApplyCellUpdate(update);
        }

        /// <summary><c>eD&lt;acteur&gt;|&lt;direction&gt;</c> : orientation d'un acteur ; ignoré en combat comme dans le client.</summary>
        [MessageAttribution("eD")]
        public void GetDirection(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account?.Game == null || account.Game.Fight.IsInFight) return;
            if (!GmParser.TrySplitActorPayload(message.Substring(2), out long actorId, out string value)
                || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int direction) || direction < 0 || direction > 7) return;
            Map map = account.Game.Map;
            bool self = actorId == account.Game.character.id;
            if (self) account.Game.character.Orientation = direction;
            MapActor actor = self ? map.Self : map.GetActor(actorId);
            if (actor != null) actor.Orientation = direction;
            map.GetEntitiesRefreshEvent();
            if (actor != null) map.NotifyActorUpdated(actor);
        }

        /// <summary><c>Oa&lt;acteur&gt;|&lt;stuff&gt;</c> : accessoires visibles après un changement d'équipement.</summary>
        [MessageAttribution("Oa")]
        public void GetAccessories(TcpClient client, string message)
        {
            Map map = client.account?.Game?.Map;
            if (map == null || !GmParser.TrySplitActorPayload(message.Substring(2), out long actorId, out string stuff)) return;
            MapActor actor = map.GetActor(actorId);
            IReadOnlyList<ActorAccessory> accessories = ActorAccessory.ParseList(stuff);
            switch (actor)
            {
                case PlayerActor player: player.Stuff = stuff; player.Accessories = accessories; break;
                case MerchantActor merchant: merchant.Stuff = stuff; merchant.Accessories = accessories; break;
                case NpcActor npc: npc.AccessoriesRaw = stuff; npc.Accessories = accessories; break;
                case FightMonsterActor monster: monster.AccessoriesRaw = stuff; monster.Accessories = accessories; break;
                default: return;
            }
            map.NotifyActorUpdated(actor);
        }

        /// <summary><c>Gc+&lt;combat&gt;;&lt;type&gt;|&lt;équipe&gt;;&lt;cellule&gt;;&lt;type&gt;;&lt;alignement&gt;|…</c> et <c>Gc-&lt;combat&gt;</c> : épées des combats de la carte.</summary>
        [MessageAttribution("Gc")]
        public void GetFightSwords(TcpClient client, string message)
        {
            Map map = client.account?.Game?.Map;
            string payload = message.Substring(2);
            if (map == null || payload.Length == 0) return;
            if (payload[0] == '+')
            {
                FightSwordsActor swords = GmParser.ParseFightSwords(payload.Substring(1));
                if (swords == null) client.account.Logger?.LogDanger("CARTE", "Combat de la carte illisible : " + Shorten(message));
                else map.AddFightSwords(swords);
            }
            else if (payload[0] == '-')
            {
                string id = payload.Substring(1).Split('|')[0].Split(';')[0];
                if (long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fightId)) map.RemoveFightSwords(fightId);
            }
            map.GetEntitiesRefreshEvent();
        }

        /// <summary><c>GDM|&lt;carte&gt;|&lt;date&gt;|&lt;clé&gt;</c> : charge la carte puis demande toujours ses acteurs par <c>GI</c>, même si elle manque.</summary>
        [MessageAttribution("GDM")]
        public async Task GetNewMap(TcpClient client, string message)
        {
            Accounts account = client.account;
            account.Game.Manager.Mouvements.CancelForMapChange();
            account.Game.character.Cell = null;
            account.Game.PersoInWorld.Clear();
            Map map = account.Game.Map;
            try
            {
                map.SetRefreshMap(message.Substring(3).TrimStart('|'));
                if (!map.HasMapData) account.Logger?.LogError("CARTE", map.LoadError ?? "Carte sans cellules.");
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException
                || error is IndexOutOfRangeException || error is OverflowException)
            {
                // Carte vide plutôt que la géométrie de la carte précédente ; GI part quand même.
                map.Clear();
                account.Logger?.LogError("CARTE", "Données de carte inutilisables (" + Shorten(message) + ") : " + error.Message);
                map.GetMapRefreshEvent();
            }
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

        private static string Shorten(string message) => message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
