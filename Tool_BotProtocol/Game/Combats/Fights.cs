using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Combats
{
    public enum CombatPhase { None, Placement, Active, Finished }
    public sealed class CombatActionResult
    {
        public bool Sent { get; private set; }
        public string Message { get; private set; }
        internal CombatActionResult(bool sent, string message) { Sent = sent; Message = message; }
    }
    public sealed class CombatFighter
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public short CellId { get; internal set; } = -1;
        public int Type { get; internal set; }
        public int Gfx { get; internal set; }
        public int Orientation { get; internal set; }
        public int Team { get; internal set; } = -1;
        public int Life { get; internal set; } = -1;
        public int MaximumLife { get; internal set; } = -1;
        public int ActionPoints { get; internal set; } = -1;
        public int MovementPoints { get; internal set; } = -1;
        public bool IsDead { get; internal set; }
        internal int BaseActionPoints = -1;
        internal int BaseMovementPoints = -1;
        internal CombatFighter Copy() { return (CombatFighter)MemberwiseClone(); }
    }
    // Sending a request never spends PA/PM locally: only server packets change combat values.
    public sealed class Fights : IDisposable
    {
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly Dictionary<int, CombatFighter> fighters = new Dictionary<int, CombatFighter>();
        private readonly Dictionary<short, int> lastSpellTurn = new Dictionary<short, int>();
        private readonly Dictionary<short, int> castsThisTurn = new Dictionary<short, int>();
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool disposed;
        private int generation;
        private string pendingKind;
        private bool pendingConfirmed;
        private DateTime pendingAt;
        private CombatPhase phase;
        private bool spectator, ready;
        private int actor, turn, turnDuration;
        private int pa = -1, pm = -1;
        private short[] places = new short[0];
        private string lastMessage = "Aucun combat en cours.";
        internal Fights(Accounts.Accounts owner) { account = owner; }
        public CombatPhase Phase { get { lock (sync) return phase; } }
        public bool IsInFight { get { lock (sync) return InFight; } }
        public bool IsPlacement { get { lock (sync) return phase == CombatPhase.Placement; } }
        public bool IsSpectator { get { lock (sync) return spectator; } }
        public bool IsReady { get { lock (sync) return ready; } }
        public bool IsMyTurn { get { lock (sync) return MyTurn; } }
        public bool IsActionPending { get { lock (sync) { ExpirePending(); return pendingKind != null; } } }
        public int CurrentActorId { get { lock (sync) return actor; } }
        public int TurnNumber { get { lock (sync) return turn; } }
        public int TurnDurationMilliseconds { get { lock (sync) return turnDuration; } }
        public int ActionPoints { get { lock (sync) return pa; } }
        public int MovementPoints { get { lock (sync) return pm; } }
        public string LastActionMessage { get { lock (sync) return lastMessage; } }
        public short[] PlacementCells { get { lock (sync) return (short[])places.Clone(); } }
        public IReadOnlyDictionary<int, CombatFighter> Fighters
        { get { lock (sync) return fighters.ToDictionary(entry => entry.Key, entry => entry.Value.Copy()); } }
        public event Action CombatChanged;
        public event Action CombatReady;
        public event Action CombatFinished;
        private bool InFight => phase == CombatPhase.Placement || phase == CombatPhase.Active;
        private bool MyTurn => phase == CombatPhase.Active && !spectator && actor == account.Game.character.id;
        private void Changed() { CombatChanged?.Invoke(); }
        private CombatFighter GetFighter(int id)
        {
            CombatFighter value;
            if (!fighters.TryGetValue(id, out value)) fighters[id] = value = new CombatFighter { Id = id, Name = "Combattant #" + id };
            return value;
        }
        private void ExpirePending()
        {
            if (pendingKind != null && DateTime.UtcNow - pendingAt > TimeSpan.FromSeconds(8))
            { pendingKind = null; lastMessage = "Aucune confirmation reçue : vous pouvez réessayer."; }
        }
        private string ActionUnavailable(bool placement)
        {
            ExpirePending();
            if (disposed || account.isdisposed || account.Connexion == null || !account.Connexion.IsConnected()) return "Le serveur est déconnecté.";
            if (!InFight) return "Aucun combat en cours.";
            if (spectator) return "Les spectateurs ne peuvent pas agir.";
            if (placement ? phase != CombatPhase.Placement : !MyTurn) return placement ? "Le placement est terminé." : "Attendez votre tour.";
            CombatFighter self;
            if (fighters.TryGetValue(account.Game.character.id, out self) && self.IsDead) return "Votre personnage est hors combat.";
            if (pendingKind != null) return "Attendez la confirmation de l’action précédente.";
            return null;
        }
        public string GetSpellUnavailableReason(short spellId, short? targetCell = null)
        { lock (sync) return SpellUnavailable(spellId, targetCell); }
        private string SpellUnavailable(short spellId, short? targetCell)
        {
            string reason = ActionUnavailable(false);
            if (reason != null) return reason;
            Spell spell;
            if (!account.Game.character.Spells.TryGetValue(spellId, out spell)) return "Ce sort n’est pas appris.";
            SpellStats stats = spell.GetStats();
            if (stats != null && pa >= 0 && stats.PA > pa) return "PA insuffisants (" + stats.PA + " requis).";
            int count, previous;
            if (stats != null && stats.PerTurn > 0 && castsThisTurn.TryGetValue(spellId, out count) && count >= stats.PerTurn)
                return "Nombre de lancers de ce sort atteint pour ce tour.";
            if (stats != null && stats.Interval > 0 && lastSpellTurn.TryGetValue(spellId, out previous) && previous + stats.Interval > turn)
                return "Ce sort doit encore attendre " + (previous + stats.Interval - turn) + " tour(s).";
            if (!targetCell.HasValue) return null;
            Cell target = account.Game.Map.GetCellFromId(targetCell.Value), origin = account.Game.character.Cell;
            if (target == null || !target.IsActive) return "Cette cellule ne fait pas partie du terrain de combat.";
            if (origin == null) return "La position de votre personnage n’est pas encore connue.";
            if (stats == null) return null;
            int distance = origin.GetDistanceBetweenCells(target), maximum = stats.Max_portee;
            if (stats.portee_modifiable) maximum = Math.Max(stats.Min_portee + 1, maximum + account.Game.character.stats.Atteignable.StatsTotal);
            if (distance < stats.Min_portee || distance > maximum) return "Cible hors portée (" + stats.Min_portee + "–" + maximum + ").";
            if (stats.IsInLine && !origin.AreCellsOnline(target)) return "Ce sort se lance en ligne.";
            if (stats.EmptyCell && fighters.Values.Any(f => !f.IsDead && f.CellId == target.CellID)) return "Ce sort nécessite une cellule vide.";
            // LoS, equipment modifiers, states and per-target limits remain authoritative on the server.
            return null;
        }
        public Task<CombatActionResult> CastSpellAsync(short spellId, short targetCell)
        { return RequestAsync("GA300" + spellId + ";" + targetCell, "sort", () => SpellUnavailable(spellId, targetCell)); }
        public Task<CombatActionResult> SetReadyAsync(bool value)
        { return RequestAsync("GR" + (value ? "1" : "0"), "prêt", () => ActionUnavailable(true)); }
        public Task<CombatActionResult> PlaceAsync(short targetCell)
        { return RequestAsync("Gp" + targetCell, "placement", () => ActionUnavailable(true) ?? (places.Contains(targetCell) ? null : "Choisissez une cellule de placement de votre équipe.")); }
        public Task<CombatActionResult> PassTurnAsync()
        { return RequestAsync("Gt", "tour", () => ActionUnavailable(false)); }
        public Task<CombatActionResult> MoveAsync(short targetCell)
        {
            string packet;
            lock (sync)
            {
                string reason = ActionUnavailable(false);
                Cell destination = account.Game.Map.GetCellFromId(targetCell), origin = account.Game.character.Cell;
                if (reason == null && (origin == null || destination == null || !destination.IsWalkable())) reason = "Cette cellule n’est pas accessible.";
                if (reason == null && origin == destination) reason = "Votre personnage est déjà sur cette cellule.";
                if (reason != null) return Task.FromResult(new CombatActionResult(false, reason));
                var blocked = fighters.Values.Where(f => !f.IsDead && f.CellId >= 0 && f.Id != account.Game.character.id)
                    .Select(f => account.Game.Map.GetCellFromId(f.CellId)).Where(c => c != null).ToList();
                if (blocked.Contains(destination)) return Task.FromResult(new CombatActionResult(false, "Cette cellule est occupée par un combattant."));
                List<Cell> path;
                using (var finder = new Pathfinder()) path = finder.GetPath(origin, destination, blocked, false, 0, account.Game.Map);
                if (path == null || path.Count < 2) return Task.FromResult(new CombatActionResult(false, "Aucun chemin libre vers cette cellule."));
                if (pm >= 0 && path.Count - 1 > pm) return Task.FromResult(new CombatActionResult(false, "PM insuffisants pour ce trajet."));
                packet = "GA001" + PathfinderUtils.GetCleanRoad(path);
            }
            return RequestAsync(packet, "déplacement", () => ActionUnavailable(false));
        }
        private Task<CombatActionResult> RequestAsync(string packet, string kind, Func<string> validate)
        {
            int version; TcpClient connection;
            lock (sync)
            {
                string reason = validate();
                if (reason != null) return Task.FromResult(new CombatActionResult(false, reason));
                StartPending(kind); version = generation; connection = account.Connexion;
            }
            Changed(); return SendActionAsync(connection, packet, version);
        }
        private void StartPending(string kind)
        { pendingKind = kind; pendingAt = DateTime.UtcNow; pendingConfirmed = false; lastMessage = "Action envoyée, en attente du serveur."; }
        private async Task<CombatActionResult> SendActionAsync(TcpClient connection, string packet, int version)
        {
            try
            {
                lock (sync) { if (disposed || version != generation || !ReferenceEquals(connection, account.Connexion)) return new CombatActionResult(false, "Le combat ou la connexion a changé."); }
                await connection.SendPacket(packet).ConfigureAwait(false);
                lock (sync) { if (disposed || version != generation || !ReferenceEquals(connection, account.Connexion) || !connection.IsConnected()) return new CombatActionResult(false, "La connexion a été interrompue."); }
                return new CombatActionResult(true, "Action envoyée au serveur.");
            }
            catch (Exception error)
            {
                lock (sync) { if (!disposed && version == generation) { pendingKind = null; lastMessage = "Envoi impossible : " + error.Message; } }
                Changed(); return new CombatActionResult(false, "Envoi impossible : " + error.Message);
            }
        }
        public int GetSpellCooldownRemaining(short spellId)
        {
            lock (sync)
            {
                Spell spell; int previous;
                if (!account.Game.character.Spells.TryGetValue(spellId, out spell) || spell.GetStats() == null || !lastSpellTurn.TryGetValue(spellId, out previous)) return 0;
                return Math.Max(0, previous + spell.GetStats().Interval - turn);
            }
        }
        public int GetSpellCastsThisTurn(short spellId) { lock (sync) { int count; return castsThisTurn.TryGetValue(spellId, out count) ? count : 0; } }
        internal void Join(string payload)
        {
            string[] fields = payload.Split('|'); int state, viewing;
            if (fields.Length < 4 || !int.TryParse(fields[0], out state) || state < 1 || state > 3 || !int.TryParse(fields[3], out viewing)) return;
            account.Game.Manager.Mouvements.CancelForMapChange(); Clear(false);
            lock (sync)
            {
                if (disposed) return;
                phase = state >= 3 ? CombatPhase.Active : CombatPhase.Placement; spectator = viewing == 1;
                lastMessage = spectator ? "Combat observé en spectateur." : "Choisissez votre position puis indiquez que vous êtes prêt.";
            }
            account.AccountStates = AccountStates.FIGHTING; Changed(); CombatReady?.Invoke();
        }
        internal void SetPlaces(string payload)
        {
            string[] fields = payload.Split('|'); int team;
            if (fields.Length < 3 || !int.TryParse(fields[2], out team) || team < 0 || team > 1) return;
            string encoded = fields[team];
            if (encoded.Length % 2 != 0 || encoded.Any(c => !Hash.caracteres_array.Contains(c))) return;
            var cells = new List<short>();
            for (int i = 0; i < encoded.Length; i += 2) cells.Add(Hash.Get_Cell_From_Hash(encoded.Substring(i, 2)));
            lock (sync) { if (!InFight) return; places = cells.Distinct().ToArray(); } Changed();
        }
        internal void SetReady(string payload)
        {
            int id;
            if (payload.Length < 2 || (payload[0] != '0' && payload[0] != '1') || !int.TryParse(payload.Substring(1), out id)) return;
            lock (sync)
            {
                if (!InFight || id != account.Game.character.id) return;
                ready = payload[0] == '1'; if (pendingKind == "prêt") pendingKind = null;
                lastMessage = ready ? "Vous êtes prêt. Le serveur démarrera le combat." : "Vous pouvez choisir votre position.";
            }
            Changed();
        }
        internal void Start()
        { lock (sync) { if (!InFight) return; phase = CombatPhase.Active; places = new short[0]; pendingKind = null; lastMessage = "Combat commencé. Attendez votre tour."; } Changed(); }
        internal void StartTurn(string payload)
        {
            string[] fields = payload.Split('|'); int id, duration;
            if (fields.Length < 2 || !int.TryParse(fields[0], out id) || !int.TryParse(fields[1], out duration) || duration < 0) return;
            lock (sync)
            {
                if (!InFight) return;
                phase = CombatPhase.Active; actor = id; turnDuration = duration; pendingKind = null;
                if (id == account.Game.character.id && !spectator)
                {
                    turn++; castsThisTurn.Clear(); CombatFighter self = GetFighter(id);
                    pa = self.ActionPoints = self.BaseActionPoints; pm = self.MovementPoints = self.BaseMovementPoints;
                    lastMessage = "Votre tour : choisissez un sort ou déplacez votre personnage.";
                }
                else lastMessage = "Tour de " + GetFighter(id).Name + ".";
            }
            Changed();
        }
        internal void EndTurn(string payload)
        {
            int id; if (!int.TryParse(payload, out id)) return;
            lock (sync) { if (!InFight || id != actor) return; actor = 0; turnDuration = 0; pendingKind = null; lastMessage = "Tour terminé. Attendez le suivant."; } Changed();
        }
        internal void Finish()
        {
            lock (sync) { if (!InFight) return; }
            Clear(false); lock (sync) { if (disposed) return; phase = CombatPhase.Finished; lastMessage = "Combat terminé."; }
            if (account.AccountStates == AccountStates.FIGHTING) account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            Changed(); CombatFinished?.Invoke();
        }
        public void UpdateFighterFromMap(string[] info)
        {
            int id, type; short cell;
            if (info == null || info.Length < 7 || !int.TryParse(info[3], out id) || !short.TryParse(info[0], out cell) || !int.TryParse(info[5].Split(',')[0], out type)) return;
            lock (sync)
            {
                if (!InFight) return;
                CombatFighter fighter = GetFighter(id); fighter.Name = info[4]; fighter.Type = type; fighter.CellId = cell;
                int value;
                if (int.TryParse(info[6].Split('^')[0], out value)) fighter.Gfx = value;
                if (int.TryParse(info[1], out value)) fighter.Orientation = value;
                int lifeIndex = type > 0 ? 14 : type == -2 ? 12 : -1;
                if (lifeIndex >= 0 && info.Length > lifeIndex + 2)
                {
                    if (int.TryParse(info[lifeIndex], out value)) fighter.Life = value;
                    if (int.TryParse(info[lifeIndex + 1], out value)) fighter.ActionPoints = fighter.BaseActionPoints = value;
                    if (int.TryParse(info[lifeIndex + 2], out value)) fighter.MovementPoints = fighter.BaseMovementPoints = value;
                }
                int teamIndex = type > 0 ? 24 : type == -2 ? Array.FindLastIndex(info, field => !string.IsNullOrEmpty(field)) : -1;
                if (teamIndex >= 0 && info.Length > teamIndex && int.TryParse(info[teamIndex], out value)) fighter.Team = value;
                if (id == account.Game.character.id) { pa = fighter.ActionPoints; pm = fighter.MovementPoints; }
            }
            Changed();
        }
        public void RemoveFighter(int id) { lock (sync) { if (!InFight) return; fighters.Remove(id); } Changed(); }
        internal void UpdatePositions(string payload)
        {
            foreach (string entry in payload.TrimStart('|').Split('|'))
            {
                string[] fields = entry.Split(';'); int id; short cell;
                if (fields.Length < 2 || !int.TryParse(fields[0], out id) || !short.TryParse(fields[1], out cell)) continue;
                lock (sync)
                {
                    if (!InFight) return; GetFighter(id).CellId = cell;
                    if (id == account.Game.character.id && pendingKind == "placement") { pendingKind = null; lastMessage = "Position confirmée par le serveur."; }
                }
                UpdateMapCell(id, cell);
            }
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
        }
        internal void UpdateTeamStats(string payload)
        {
            foreach (string entry in payload.TrimStart('|').Split('|'))
            {
                string[] fields = entry.Split(';'); int id, dead;
                if (fields.Length < 2 || !int.TryParse(fields[0], out id) || !int.TryParse(fields[1], out dead)) continue;
                short cell = -1;
                lock (sync)
                {
                    if (!InFight) return;
                    CombatFighter fighter = GetFighter(id); fighter.IsDead = dead == 1;
                    if (dead == 1) { fighter.Life = 0; fighter.CellId = -1; }
                    else if (fields.Length >= 6)
                    {
                        int life, actionPoints, movementPoints;
                        if (!int.TryParse(fields[2], out life) || !int.TryParse(fields[3], out actionPoints) || !int.TryParse(fields[4], out movementPoints) || !short.TryParse(fields[5], out cell)) continue;
                        fighter.Life = life; fighter.ActionPoints = fighter.BaseActionPoints = actionPoints;
                        fighter.MovementPoints = fighter.BaseMovementPoints = movementPoints; fighter.CellId = cell;
                        int maximum; if (fields.Length > 7 && int.TryParse(fields[7], out maximum)) fighter.MaximumLife = maximum;
                    }
                    else continue;
                    if (id == account.Game.character.id) { pa = fighter.ActionPoints; pm = fighter.MovementPoints; }
                }
                UpdateMapCell(id, dead == 1 ? (short)-1 : cell);
            }
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
        }
        private void UpdateMapCell(int id, short cellId)
        {
            Cell cell = account.Game.Map.GetCellFromId(cellId);
            if (id == account.Game.character.id) account.Game.character.Cell = cell;
            else { Entites entity; if (account.Game.Map.Entites.TryGetValue(id, out entity)) entity.Cell = cell; }
        }
        internal void BeginAction(string payload)
        {
            int id; if (!int.TryParse(payload, out id)) return;
            lock (sync) { if (!InFight || id != account.Game.character.id) return; if (pendingKind == null) StartPending("action"); } Changed();
        }
        internal void EndAction(int id)
        {
            lock (sync)
            {
                if (!InFight || id != account.Game.character.id) return;
                if (pendingKind != "sort") pendingKind = null;
                // StarLoco clears its casting lock later with GA;102;guid;guid,-0.
                else if (!pendingConfirmed && !lastMessage.StartsWith("Le serveur a refusé", StringComparison.Ordinal)) lastMessage = "Le serveur n’a pas confirmé le lancement du sort.";
            }
            Changed();
        }
        internal void Refuse(string message)
        { lock (sync) { if (!InFight) return; lastMessage = "Le serveur a refusé l’action : " + message; } account.Logger.LogDanger("COMBAT", LastActionMessage); Changed(); }
        internal async Task ProcessActionAsync(TcpClient connection, string[] parts)
        {
            int action; if (parts.Length < 2 || !int.TryParse(parts[1], out action)) return;
            if (action == 0) { lock (sync) { pendingKind = null; lastMessage = "Le serveur a refusé l’action."; } Changed(); return; }
            int source; if (parts.Length < 3 || !int.TryParse(parts[2], out source)) return;
            string data = parts.Length >= 4 ? parts[3] : string.Empty;
            if (action == 1) { await ProcessMovementAsync(connection, parts[0], source, data).ConfigureAwait(false); return; }
            string[] args = data.Split(','); int target, delta;
            lock (sync)
            {
                if (!InFight) return;
                if (action == 300 || action == 302)
                {
                    short spellId;
                    if (source == account.Game.character.id && short.TryParse(args[0], out spellId))
                    {
                        pendingConfirmed = true;
                        if (action == 300)
                        { lastMessage = "Sort confirmé par le serveur."; lastSpellTurn[spellId] = turn; int count; castsThisTurn.TryGetValue(spellId, out count); castsThisTurn[spellId] = count + 1; }
                        else lastMessage = "Échec critique confirmé par le serveur.";
                    }
                }
                else if ((action == 102 || action == 129) && args.Length >= 2 && int.TryParse(args[0], out target) && int.TryParse(args[1], out delta))
                {
                    CombatFighter fighter = GetFighter(target);
                    if (action == 102 && fighter.ActionPoints >= 0) fighter.ActionPoints = Math.Max(0, fighter.ActionPoints + delta);
                    if (action == 129 && fighter.MovementPoints >= 0) fighter.MovementPoints = Math.Max(0, fighter.MovementPoints + delta);
                    if (target == account.Game.character.id)
                    {
                        if (action == 102 && pa >= 0) pa = Math.Max(0, pa + delta);
                        if (action == 129 && pm >= 0) pm = Math.Max(0, pm + delta);
                        if (action == 102 && delta == 0 && pendingKind == "sort") pendingKind = null;
                    }
                }
                else if ((action == 100 || action == 103) && int.TryParse(args[0], out target))
                {
                    CombatFighter fighter = GetFighter(target);
                    if (action == 103) { fighter.IsDead = true; fighter.Life = 0; fighter.CellId = -1; }
                    else if (args.Length >= 2 && int.TryParse(args[1], out delta) && fighter.Life >= 0) fighter.Life = Math.Max(0, fighter.Life + delta);
                }
            }
            short cellId;
            if (action == 4 && args.Length >= 2 && int.TryParse(args[0], out target) && short.TryParse(args[1], out cellId))
            { lock (sync) GetFighter(target).CellId = cellId; UpdateMapCell(target, cellId); }
            if (action == 51 && short.TryParse(data, out cellId))
            { lock (sync) GetFighter(source).CellId = cellId; UpdateMapCell(source, cellId); }
            if (action == 103 && int.TryParse(args[0], out target)) UpdateMapCell(target, -1);
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
        }
        private async Task ProcessMovementAsync(TcpClient connection, string actionIdText, int id, string encoded)
        {
            List<Cell> path = PathfinderUtils.DecodeServerPath(account.Game.Map, encoded);
            if (path == null || path.Count == 0) return;
            int version; CancellationToken token;
            lock (sync) { if (!InFight || disposed) return; version = generation; token = cancellation.Token; GetFighter(id).CellId = path.Last().CellID; }
            Cell destination = path.Last();
            int duration = PathfinderUtils.GetTimeOnMap(path[0], path);
            account.Game.Map.NotifyEntityMovement(id, path, duration);
            UpdateMapCell(id, path.Last().CellID); account.Game.Map.GetEntitiesRefreshEvent(); Changed();
            int actionId;
            if (id != account.Game.character.id || !int.TryParse(actionIdText, out actionId) || actionId < 0) return;
            try { await Task.Delay(duration, token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            lock (sync) { if (disposed || version != generation || !MyTurn || !ReferenceEquals(connection, account.Connexion) || account.Game.Map.GetCellFromId(destination.CellID) != destination) return; }
            await connection.SendPacket("GKK" + actionId).ConfigureAwait(false);
        }
        public void Clear(bool notify = true)
        {
            CancellationTokenSource previous;
            lock (sync)
            {
                generation++; previous = cancellation; cancellation = disposed ? null : new CancellationTokenSource();
                phase = CombatPhase.None; spectator = ready = false; actor = turn = turnDuration = 0; pa = pm = -1;
                places = new short[0]; fighters.Clear(); lastSpellTurn.Clear(); castsThisTurn.Clear();
                pendingKind = null; pendingConfirmed = false; lastMessage = "Aucun combat en cours.";
            }
            if (previous != null) { previous.Cancel(); previous.Dispose(); }
            if (notify && !disposed) Changed();
        }
        public void Dispose()
        { lock (sync) { if (disposed) return; disposed = true; } Clear(false); CombatChanged = null; CombatReady = null; CombatFinished = null; }
    }
}
