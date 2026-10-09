using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Managers.recoltes
{
    /// <summary>Ressource de la carte que le personnage peut récolter avec l'une de ses compétences de métier.</summary>
    public sealed class HarvestOption
    {
        public Interactives Interactive { get; internal set; }
        public short Skill { get; internal set; }
        public int JobId { get; internal set; }
        /// <summary>Niveau du métier qui porte la compétence (paquet <c>JS</c>).</summary>
        public int JobLevel { get; internal set; }
        /// <summary>Distance en cases entre le personnage et la ressource.</summary>
        public int Distance { get; internal set; }
    }

    /// <summary>
    /// Service de récolte. Il tient à jour l'état des objets interactifs de la carte à chaque <c>GDF</c>
    /// (<see cref="Map.ObjectStateChanged"/> → <see cref="Interactives.ApplyState"/>), choisit les compétences de récolte
    /// d'une ressource dans <c>Interactive.Skills</c> (BotInteractives, sinon textes du client) parmi celles que les
    /// métiers du personnage lui donnent à leur niveau (<c>JS</c>), et confie l'utilisation à
    /// <see cref="InteractiveActions.UseAsync"/> (déplacement, <c>GA500</c>, action 501, <c>GKK</c> à la fin de la durée).
    /// </summary>
    public sealed class Harvest : IDisposable
    {
        private static readonly short[] NoSkills = new short[0];
        private readonly object sync = new object();
        private Accounts.Accounts account;
        private Map map;
        private InteractiveActions actions;
        private bool disposed;

        /// <summary>Ressource visée par la dernière demande, jusqu'à son issue.</summary>
        public Interactives Target { get; private set; }
        public short TargetSkill { get; private set; }
        /// <summary>Action 501 du personnage commencée sur la ressource visée (cellule).</summary>
        public event Action<short> Started;
        /// <summary>Issue de la récolte : résultat, cellule, quantité (<c>IQ</c>, 0 sinon).</summary>
        public event Action<RecolteEnum, short, int> Finished;

        public Harvest(Accounts.Accounts A, Mouvement M, Map map)
        {
            account = A;
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            map.ObjectStateChanged += OnObjectStateChanged;
            map.RefreshMap += OnMapRefreshed;
        }

        /// <summary>Compétences de récolte de l'objet que le personnage possède (dans l'ordre de l'objet).</summary>
        public IReadOnlyList<short> SkillsFor(Interactives interactive)
        {
            CharacterClass character = account?.Game?.character;
            if (interactive == null || character?.Jobs == null || interactive.ClientType != (int)InteractiveType.Harvest) return NoSkills;
            var known = new HashSet<short>(character.GetAvailableSkills().Select(skill => skill.Id));
            return interactive.Skills.Where(known.Contains).Distinct().ToArray();
        }

        /// <summary>
        /// Ressources utilisables de la carte avec une compétence du personnage, de la plus proche à la plus lointaine ;
        /// <paramref name="wanted"/> limite aux compétences voulues.
        /// </summary>
        public IReadOnlyList<HarvestOption> Harvestables(IEnumerable<short> wanted = null)
        {
            CharacterClass character = account?.Game?.character;
            if (character?.Cell == null || map == null) return new HarvestOption[0];
            HashSet<short> filter = wanted == null ? null : new HashSet<short>(wanted);
            Jobs.Jobs[] jobs = character.GetJobsSnapshot();
            var options = new List<HarvestOption>();
            foreach (Interactives interactive in map.Interactives.Values)
            {
                if (interactive?.Cell == null || !interactive.IsUsable) continue;
                foreach (short skill in SkillsFor(interactive))
                {
                    if (filter != null && !filter.Contains(skill)) continue;
                    Jobs.Jobs job = jobs.FirstOrDefault(entry => entry.Skills != null && entry.Skills.Any(known => known.Id == skill));
                    options.Add(new HarvestOption
                    {
                        Interactive = interactive, Skill = skill, JobId = job?.ID ?? LangData.Skill.Job(skill) ?? 0, JobLevel = job?.Level ?? 0,
                        Distance = character.Cell.GetDistanceBetweenCells(interactive.Cell)
                    });
                }
            }
            return options.OrderBy(option => option.Distance).ThenBy(option => option.Interactive.Cell.CellID).ToList();
        }

        public bool CanCollect(IEnumerable<short> wanted = null) => Harvestables(wanted).Count > 0;

        /// <summary>Récolte la ressource avec la compétence (la première compétence possédée si 0).</summary>
        public async Task<InteractionResult> HarvestAsync(Interactives target, short skill = 0)
        {
            InteractiveActions interactive = EnsureSubscribed();
            if (interactive == null || target?.Cell == null) return await Refused(target, "Ressource ou session indisponible.").ConfigureAwait(false);
            if (skill == 0) skill = SkillsFor(target).FirstOrDefault();
            if (skill == 0) return await Refused(target, "Aucune compétence de vos métiers ne récolte " + target.Name + ".").ConfigureAwait(false);
            Interactives previous;
            lock (sync) { previous = Target; Target = target; TargetSkill = skill; }
            if (previous != null && !ReferenceEquals(previous, target)) RaiseFinished(RecolteEnum.ANNULÉ, previous.Cell.CellID, 0);
            InteractionResult result = await interactive.UseAsync(target.Cell.CellID, skill).ConfigureAwait(false);
            if (!result.Sent) Finish(target.Cell.CellID, RecolteEnum.REFUSÉ, 0);
            return result;
        }

        /// <summary>Récolte la ressource la plus proche (compétences <paramref name="wanted"/> si précisées).</summary>
        public Task<InteractionResult> HarvestNearestAsync(IEnumerable<short> wanted = null)
        {
            HarvestOption option = Harvestables(wanted).FirstOrDefault();
            if (option == null)
            {
                account?.Logger?.LogDanger("RÉCOLTE", "Aucune ressource récoltable sur cette carte.");
                return Refused(null, "Aucune ressource récoltable sur cette carte.");
            }
            return HarvestAsync(option.Interactive, option.Skill);
        }

        /// <summary>Oublie la ressource visée sans rien envoyer (déconnexion, changement de personnage).</summary>
        public void Clear()
        {
            Interactives previous;
            lock (sync) { previous = Target; Target = null; TargetSkill = 0; }
            if (previous?.Cell != null) RaiseFinished(RecolteEnum.ANNULÉ, previous.Cell.CellID, 0);
        }

        private Task<InteractionResult> Refused(Interactives target, string message)
        {
            if (target?.Cell != null) RaiseFinished(RecolteEnum.REFUSÉ, target.Cell.CellID, 0);
            return Task.FromResult(new InteractionResult(false, message));
        }

        private InteractiveActions EnsureSubscribed()
        {
            InteractiveActions current = account?.Game?.Interactions?.Interactive;
            lock (sync)
            {
                if (disposed || current == null) return null;
                if (ReferenceEquals(actions, current)) return current;
                Unsubscribe();
                actions = current;
                current.ActionStarted += OnActionStarted;
                current.QuantityReceived += OnQuantity;
                current.UseAbandoned += OnUseAbandoned;
                return current;
            }
        }

        private void Unsubscribe()
        {
            if (actions == null) return;
            actions.ActionStarted -= OnActionStarted;
            actions.QuantityReceived -= OnQuantity;
            actions.UseAbandoned -= OnUseAbandoned;
            actions = null;
        }

        private void OnObjectStateChanged(int cellId)
        {
            Map current = map;
            if (current == null || !current.ObjectStates.TryGetValue(cellId, out InteractiveObjectState state)) return;
            if (current.Interactives.TryGetValue(cellId, out Interactives interactive)) interactive?.ApplyState(state);
        }

        private void OnMapRefreshed() => Clear();

        private void OnActionStarted(InteractiveActionInfo info)
        {
            Interactives target = Target;
            if (target?.Cell == null || info.CellId != target.Cell.CellID) return;
            if (info.IsSelf) { try { Started?.Invoke(info.CellId); } catch (Exception error) { account?.Logger?.LogException("RÉCOLTE", error); } }
            else
            {
                account?.Logger?.LogInfo("RÉCOLTE", "Le personnage " + info.ActorId + " récolte cette ressource avant vous.");
                Finish(info.CellId, RecolteEnum.VOLÉ, 0);
            }
        }

        private void OnQuantity(long sprite, int quantity)
        {
            Interactives target = Target;
            if (target?.Cell == null || sprite != (account?.Game?.character?.id ?? long.MinValue)) return;
            Finish(target.Cell.CellID, RecolteEnum.RECOLTÉ, quantity);
        }

        private void OnUseAbandoned(short cellId) => Finish(cellId, RecolteEnum.FALL, 0);

        private void Finish(short cellId, RecolteEnum result, int quantity)
        {
            lock (sync)
            {
                if (Target?.Cell == null || Target.Cell.CellID != cellId) return;
                Target = null;
                TargetSkill = 0;
            }
            RaiseFinished(result, cellId, quantity);
        }

        private void RaiseFinished(RecolteEnum result, short cellId, int quantity)
        {
            try { Finished?.Invoke(result, cellId, quantity); }
            catch (Exception error) { account?.Logger?.LogException("RÉCOLTE", error); }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                Unsubscribe();
                Target = null;
            }
            if (map != null) { map.ObjectStateChanged -= OnObjectStateChanged; map.RefreshMap -= OnMapRefreshed; }
            map = null;
            account = null;
            Started = null;
            Finished = null;
        }
    }
}
