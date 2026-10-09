using System;
using System.Collections.Generic;
using System.Linq;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Étape du séquenceur visuel, comme <c>addAction(id, bloquant, …, délaiMax, forceTimeout)</c> du client 1.34 :
    /// <see cref="Start"/> part quand l'étape arrive en tête de sa file ; une étape non bloquante laisse partir la suivante
    /// aussitôt, une étape bloquante attend <see cref="Done"/> ou, au plus tard, <see cref="MaxDelay"/> ms. Avec
    /// <see cref="ForceTimeout"/>, elle dure exactement <see cref="MaxDelay"/> (<c>Die</c> : 1 500 ms, 501 : sa durée).
    /// </summary>
    public sealed class VisualStep
    {
        public VisualStep(string name, Action<double> start, bool blocking = false, double maxDelay = FightVisualSequencer.DefaultMaxDelay,
            bool forceTimeout = false, Func<double, bool> done = null)
        {
            Name = name ?? string.Empty; Start = start; Blocking = blocking; MaxDelay = Math.Max(0, maxDelay);
            ForceTimeout = forceTimeout; Done = done;
        }

        public string Name { get; }
        /// <summary>Action lancée au départ de l'étape, avec l'heure de l'horloge de la carte (ms).</summary>
        public Action<double> Start { get; }
        public bool Blocking { get; }
        /// <summary>Attente maximale d'une étape bloquante (1 000 ms par défaut).</summary>
        public double MaxDelay { get; }
        /// <summary>L'étape dure exactement <see cref="MaxDelay"/>, <see cref="Done"/> n'est pas consulté.</summary>
        public bool ForceTimeout { get; }
        /// <summary>Fin anticipée d'une étape bloquante (fin de bande, effet terminé) ; <c>null</c> : seul le délai compte.</summary>
        public Func<double, bool> Done { get; }
        /// <summary>Heure de départ, ou <c>null</c> tant que l'étape attend son tour.</summary>
        public double? StartedAt { get; internal set; }

        /// <summary>Étape non bloquante (chiffres, bulles, effet qui ne retient pas la file).</summary>
        public static VisualStep Instant(string name, Action<double> start) => new VisualStep(name, start);

        /// <summary>Étape bloquante libérée par <paramref name="done"/> ou au plus tard à <paramref name="maxDelay"/> ms.</summary>
        public static VisualStep Waiting(string name, Action<double> start, Func<double, bool> done = null,
            double maxDelay = FightVisualSequencer.DefaultMaxDelay) => new VisualStep(name, start, true, maxDelay, false, done);

        /// <summary>Étape bloquante d'exactement <paramref name="duration"/> ms (<c>forceTimeout</c>).</summary>
        public static VisualStep Timed(string name, Action<double> start, double duration) => new VisualStep(name, start, true, duration, true);

        /// <summary>Mort (<c>GA;103</c>) : étape bloquante d'exactement 1 500 ms, quelle que soit la longueur de <c>die</c>.</summary>
        public static VisualStep Die(Action<double> start) => Timed("Die", start, FightVisualSequencer.DieDuration);

        public override string ToString() => Name + (Blocking ? (ForceTimeout ? " (" + MaxDelay + " ms)" : " (≤ " + MaxDelay + " ms)") : string.Empty);
    }

    /// <summary>
    /// Séquenceur visuel de la carte (lot AN1) : une file FIFO par séquenceur du client (en combat, celle du joueur dont c'est
    /// le tour ; hors combat, celle de l'acteur du GA). <see cref="Pump"/> est appelé à chaque tick de la minuterie commune
    /// de 33 ms. Rattrapage : si la tête d'une file dépasse son échéance de plus de 2 s, ou si la file compte plus de
    /// 32 étapes, les étapes restantes partent aussitôt, sans attendre. Le protocole n'attend jamais l'affichage : le
    /// séquenceur ne retarde ni ne déclenche aucun paquet. Fil de l'interface seulement.
    /// </summary>
    public sealed class FightVisualSequencer
    {
        public const double DefaultMaxDelay = 1000;
        public const double DieDuration = 1500;
        public const double CatchUpDelay = 2000;
        public const int MaxQueuedSteps = 32;

        private readonly Dictionary<long, Queue<VisualStep>> queues = new Dictionary<long, Queue<VisualStep>>();
        private bool pumping;

        /// <summary>Une étape a levé une exception (elle est retirée, la file continue).</summary>
        public event Action<VisualStep, Exception> StepFailed;

        /// <summary>Nombre de rattrapages effectués (diagnostic, tests).</summary>
        public int CatchUps { get; private set; }

        public int TotalCount => queues.Values.Sum(queue => queue.Count);
        public bool IsIdle => TotalCount == 0;
        public int Count(long queueId) => queues.TryGetValue(queueId, out Queue<VisualStep> queue) ? queue.Count : 0;
        public IReadOnlyCollection<long> QueueIds => queues.Keys.ToArray();

        /// <summary>Ajoute une étape en fin de file (elle part au prochain <see cref="Pump"/> si la file était vide).</summary>
        public void Enqueue(long queueId, VisualStep step)
        {
            if (step == null) return;
            if (!queues.TryGetValue(queueId, out Queue<VisualStep> queue)) queues[queueId] = queue = new Queue<VisualStep>();
            queue.Enqueue(step);
        }

        /// <summary>Fait avancer chaque file jusqu'à sa prochaine attente ; vrai s'il reste du travail.</summary>
        public bool Pump(double now)
        {
            if (pumping) return !IsIdle; // une étape qui ajoute une étape ne relance pas la boucle
            pumping = true;
            try
            {
                foreach (long id in queues.Keys.ToArray())
                {
                    if (!queues.TryGetValue(id, out Queue<VisualStep> queue)) continue;
                    while (queue.Count > 0)
                    {
                        VisualStep head = queue.Peek();
                        bool late = head.StartedAt.HasValue && head.Blocking && now - (head.StartedAt.Value + head.MaxDelay) > CatchUpDelay;
                        if (late || queue.Count > MaxQueuedSteps)
                        {
                            CatchUps++;
                            RunAll(queue, now);
                            break;
                        }
                        if (!head.StartedAt.HasValue) Run(head, now);
                        if (!Finished(head, now)) break;
                        queue.Dequeue();
                    }
                    if (queue.Count == 0) queues.Remove(id);
                }
            }
            finally { pumping = false; }
            return !IsIdle;
        }

        /// <summary>Lance aussitôt toutes les étapes en attente (fin de combat affichée d'un coup).</summary>
        public void Flush(double now)
        {
            foreach (Queue<VisualStep> queue in queues.Values.ToArray()) RunAll(queue, now);
            queues.Clear();
        }

        /// <summary>Oublie toutes les étapes, sans les lancer (changement de carte, carte cachée).</summary>
        public void Clear() => queues.Clear();

        private void RunAll(Queue<VisualStep> queue, double now)
        {
            while (queue.Count > 0)
            {
                VisualStep step = queue.Dequeue();
                if (!step.StartedAt.HasValue) Run(step, now);
            }
        }

        private static bool Finished(VisualStep step, double now)
        {
            if (!step.Blocking) return true;
            double elapsed = now - step.StartedAt.Value;
            if (elapsed >= step.MaxDelay) return true;
            if (step.ForceTimeout || step.Done == null) return false;
            try { return step.Done(now); }
            catch (Exception) { return true; } // une fin illisible libère la file plutôt que de la bloquer
        }

        private void Run(VisualStep step, double now)
        {
            step.StartedAt = now;
            try { step.Start?.Invoke(now); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                try { StepFailed?.Invoke(step, error); } catch (Exception) { /* diagnostic seulement */ }
            }
        }
    }
}
