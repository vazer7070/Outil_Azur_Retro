using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Enums;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Maps
{
    /// <summary>
    /// Acteurs et objets dynamiques de la carte, alimentés par <c>GM</c>, <c>Gc</c>, <c>GDO</c>, <c>GDC</c>, <c>GDF</c>, <c>eD</c> et <c>Oa</c>.
    /// Les événements sont levés sur le fil réseau, après la mise à jour de l'état : l'interface doit les remettre sur son fil.
    /// </summary>
    public partial class Map
    {
        private ConcurrentDictionary<long, Entites> actors;
        // Alloués à la première écriture : les milliers de cartes de BotMaps n'en ont jamais besoin.
        private ConcurrentDictionary<int, CellSnapshot> originalCells;
        private ConcurrentDictionary<long, FightSwordsActor> fightSwords;
        private ConcurrentDictionary<int, GroundObject> groundObjects;
        private ConcurrentDictionary<int, InteractiveObjectState> objectStates;

        /// <summary>Acteurs de la carte par identifiant de sprite, sans le personnage du compte (voir <see cref="Self"/>).</summary>
        public ConcurrentDictionary<long, Entites> Actors => actors;
        /// <summary>Nom historique de <see cref="Actors"/> (même dictionnaire).</summary>
        public ConcurrentDictionary<long, Entites> Entites => actors;
        /// <summary>Dernière entrée <c>GM</c> du personnage du compte (couleurs, titre, alignement…) ; null avant le premier <c>GM</c>.</summary>
        public MapActor Self { get; private set; }
        /// <summary>Épées des combats en cours sur la carte, par identifiant de combat (<c>Gc+</c> / <c>Gc-</c>).</summary>
        public ConcurrentDictionary<long, FightSwordsActor> FightSwords => LazyInitializer.EnsureInitialized(ref fightSwords);
        /// <summary>Objets posés au sol par cellule (<c>GDO+</c> / <c>GDO-</c>).</summary>
        public ConcurrentDictionary<int, GroundObject> GroundObjects => LazyInitializer.EnsureInitialized(ref groundObjects);
        /// <summary>Dernier état reçu dans <c>GDF</c> pour chaque cellule d'objet interactif.</summary>
        public ConcurrentDictionary<int, InteractiveObjectState> ObjectStates => LazyInitializer.EnsureInitialized(ref objectStates);

        public event Action<MapActor> ActorAdded;
        public event Action<MapActor> ActorRemoved;
        /// <summary>Acteur remplacé (<c>GM|~</c>, nouvel ajout du même identifiant) ou modifié (<c>eD</c>, <c>Oa</c>, fin de chemin).</summary>
        public event Action<MapActor> ActorUpdated;
        /// <summary>Tous les acteurs ont été retirés (changement de carte, <c>GA;2</c>).</summary>
        public event Action ActorsCleared;
        public event Action<int> CellUpdated;
        public event Action<int> GroundObjectChanged;
        public event Action<int> ObjectStateChanged;
        /// <summary><c>GA;2</c> du personnage : la carte va changer ; l'argument est la cinématique annoncée (vide sinon).</summary>
        public event Action<string> MapChanging;

        private void InitActors()
        {
            actors = new ConcurrentDictionary<long, Entites>();
        }

        public IEnumerable<MapActor> AllActors => actors?.Values.OfType<MapActor>() ?? Enumerable.Empty<MapActor>();
        public IReadOnlyList<T> ActorsOf<T>() where T : MapActor => AllActors.OfType<T>().ToList();
        public IReadOnlyList<MapActor> ActorsOfKind(ActorKind kind) => AllActors.Where(actor => actor.Kind == kind).ToList();
        public IReadOnlyList<PlayerActor> Players => ActorsOf<PlayerActor>();
        public IReadOnlyList<NpcActor> Npcs => ActorsOf<NpcActor>();
        public IReadOnlyList<MonsterGroupActor> MonsterGroups => ActorsOf<MonsterGroupActor>();
        public IReadOnlyList<MapActor> ActorsOnCell(int cellId) => AllActors.Where(actor => actor.CellId == cellId).ToList();

        /// <summary>Acteur par identifiant, y compris le personnage du compte.</summary>
        public MapActor GetActor(long id)
        {
            MapActor self = Self;
            if (self != null && self.Id == id) return self;
            return actors != null && actors.TryGetValue(id, out Entites entity) ? entity as MapActor : null;
        }

        /// <summary>Ajoute ou remplace un acteur ; lève <see cref="ActorAdded"/> ou <see cref="ActorUpdated"/>.</summary>
        public void AddActor(MapActor actor)
        {
            if (actor == null) return;
            if (PutActor(actor)) NotifyActorUpdated(actor); else NotifyActorAdded(actor);
        }

        public bool RemoveActor(long id)
        {
            MapActor removed = TakeActor(id, out bool found);
            if (removed != null) NotifyActorRemoved(removed);
            return found;
        }

        /// <summary>Range l'acteur sans lever d'événement ; vrai s'il en remplace un autre.</summary>
        internal bool PutActor(MapActor actor)
        {
            if (actor == null || actors == null) return false;
            bool replaced = false;
            actors.AddOrUpdate(actor.Id, actor, (key, previous) => { replaced = true; return actor; });
            return replaced;
        }

        /// <summary>Retire l'acteur sans lever d'événement.</summary>
        internal MapActor TakeActor(long id, out bool found)
        {
            Entites removed = null;
            found = actors != null && actors.TryRemove(id, out removed);
            return removed as MapActor;
        }

        internal void SetSelf(MapActor actor) => Self = actor;

        internal void ClearSelf() => Self = null;

        internal void NotifyActorAdded(MapActor actor)
        {
            if (actor != null) ActorAdded?.Invoke(actor);
        }

        internal void NotifyActorRemoved(MapActor actor)
        {
            if (actor != null) ActorRemoved?.Invoke(actor);
        }

        internal void NotifyActorUpdated(MapActor actor)
        {
            if (actor != null) ActorUpdated?.Invoke(actor);
        }

        internal void NotifyMapChanging(string cinematic) => MapChanging?.Invoke(cinematic ?? string.Empty);

        public void AddFightSwords(FightSwordsActor swords)
        {
            if (swords == null) return;
            foreach (FightTeamFlag team in swords.Teams) team.Cell = GetCellFromId((short)Math.Max(-1, Math.Min(short.MaxValue, team.CellId)));
            swords.Cell = swords.Teams.Select(team => team.Cell).FirstOrDefault(cell => cell != null);
            if (swords.Cell == null && swords.Teams.Count > 0) swords.CellId = swords.Teams[0].CellId;
            bool replaced = false;
            FightSwords.AddOrUpdate(swords.Id, swords, (key, previous) => { replaced = true; return swords; });
            if (replaced) ActorUpdated?.Invoke(swords); else ActorAdded?.Invoke(swords);
        }

        public bool RemoveFightSwords(long fightId)
        {
            if (fightSwords == null || !fightSwords.TryRemove(fightId, out FightSwordsActor removed)) return false;
            ActorRemoved?.Invoke(removed);
            return true;
        }

        public void SetGroundObject(GroundObject item)
        {
            if (item == null) return;
            GroundObjects[item.CellId] = item;
            GroundObjectChanged?.Invoke(item.CellId);
        }

        public bool RemoveGroundObject(int cellId)
        {
            if (groundObjects == null || !groundObjects.TryRemove(cellId, out GroundObject ignored)) return false;
            GroundObjectChanged?.Invoke(cellId);
            return true;
        }

        public void SetObjectState(InteractiveObjectState state)
        {
            if (state == null) return;
            ObjectStates[state.CellId] = state;
            ObjectStateChanged?.Invoke(state.CellId);
        }

        /// <summary>
        /// Applique <c>GDC</c> comme <c>MapHandler.updateCell</c> : le masque hexadécimal choisit les champs remplacés.
        /// Le bot tient compte de la ligne de vue (4096) et du type de déplacement (2048) ; les bits graphiques ne changent
        /// que l'affichage du client. Sans données, la cellule revient à son état d'origine (<c>initializeCell</c>).
        /// </summary>
        public bool ApplyCellUpdate(CellUpdate update)
        {
            if (update == null || update.CellId < 0 || update.CellId > short.MaxValue) return false;
            Cell cell = GetCellFromId((short)update.CellId);
            if (cell == null) return false;
            if (update.Data == null)
            {
                if (originalCells == null || !originalCells.TryRemove(update.CellId, out CellSnapshot original)) return false;
                cell.C_Types = original.Movement;
                cell.LineofSight = original.LineOfSight;
                CellUpdated?.Invoke(update.CellId);
                return true;
            }
            if (update.Data.Length < 10 || update.Data.Any(character => Array.IndexOf(Hash.caracteres_array, character) < 0)) return false;
            if (!int.TryParse(update.Mask, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int mask)) mask = 0;
            LazyInitializer.EnsureInitialized(ref originalCells).TryAdd(update.CellId, new CellSnapshot(cell.C_Types, cell.LineofSight));
            int[] data = update.Data.Select(character => (int)Hash.get_Hash(character)).ToArray();
            if ((mask & 4096) != 0) cell.LineofSight = (data[0] & 1) == 1;
            if ((mask & 2048) != 0) cell.C_Types = (CellTypes)((data[2] & 56) >> 3);
            CellUpdated?.Invoke(update.CellId);
            return true;
        }

        /// <summary>Retire acteurs, épées, objets au sol, états et modifications de cellules (changement de carte).</summary>
        public void ClearActors()
        {
            bool any = (actors?.Count ?? 0) > 0 || (fightSwords?.Count ?? 0) > 0 || Self != null;
            actors?.Clear();
            fightSwords?.Clear();
            groundObjects?.Clear();
            objectStates?.Clear();
            originalCells?.Clear();
            Self = null;
            if (any) ActorsCleared?.Invoke();
        }

        private void DisposeActors()
        {
            ClearActors();
            actors = null;
            ActorAdded = null;
            ActorRemoved = null;
            ActorUpdated = null;
            ActorsCleared = null;
            CellUpdated = null;
            GroundObjectChanged = null;
            ObjectStateChanged = null;
            MapChanging = null;
        }

        private struct CellSnapshot
        {
            public CellSnapshot(CellTypes movement, bool lineOfSight) { Movement = movement; LineOfSight = lineOfSight; }
            public CellTypes Movement { get; }
            public bool LineOfSight { get; }
        }
    }
}
