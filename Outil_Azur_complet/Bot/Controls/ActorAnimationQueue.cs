using System;
using System.Collections.Generic;
using System.Linq;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Mode d'une animation ponctuelle : <see cref="Once"/> (une passe, puis la colonne <c>fin</c> de la bande décide),
    /// <see cref="Loop"/> (en boucle pendant une durée : émotes, 501) ou <see cref="Hold"/> (jusqu'à la suivante : la bande
    /// boucle, s'arrête sur sa dernière image ou enchaîne selon sa colonne <c>fin</c>).
    /// </summary>
    public enum ActorAnimationMode { Once, Loop, Hold }

    /// <summary>
    /// Animations ponctuelles des acteurs (lot AN1) : <c>hit</c>, <c>anim&lt;n&gt;</c>, émotes… jouées à la place de la pose
    /// du moment (<see cref="Override"/>, consulté par <c>UserMapControl.AddMain</c> avant walk, run, emote et static ; un
    /// déplacement l'emporte), et fantômes : acteurs sortis du modèle (mort) dessinés d'après leur
    /// <see cref="ActorSnapshot"/> jusqu'à une heure donnée. Le temps écoulé part du début de l'animation (première image
    /// affichée), pas de l'horloge globale. Fil de l'interface seulement ; aucune image n'est lue ici.
    /// </summary>
    public sealed class ActorAnimationQueue
    {
        /// <summary>Attente maximale d'une bande en lecture : au-delà, l'animation est abandonnée.</summary>
        public const double MaxLoadWait = 300;
        /// <summary>Enchaînements <c>suite:&lt;anim&gt;</c> suivis au plus (protection contre une boucle de bandes).</summary>
        public const int MaxChain = 4;

        private sealed class Playing
        {
            public string Animation;
            public ActorAnimationMode Mode;
            public double Requested;
            public double? Shown, LoopDuration, Pass;
            public SpriteEnd End;
            public string Next;
            public int Chain;
        }

        /// <summary>Acteur disparu du modèle, dessiné d'après son instantané jusqu'à <see cref="Until"/>.</summary>
        public sealed class Ghost
        {
            internal Ghost(ActorSnapshot snapshot, string animation, double requested, double until)
            {
                Snapshot = snapshot; Animation = animation; Requested = requested; Until = until;
            }
            public ActorSnapshot Snapshot { get; }
            public string Animation { get; }
            public double Requested { get; }
            public double Until { get; }
            /// <summary>Première image affichée (bande prête), ou <c>null</c>.</summary>
            public double? Shown { get; internal set; }
            /// <summary>Temps écoulé depuis la première image (0 tant que la bande n'est pas prête).</summary>
            public double Elapsed(double now) => Shown.HasValue ? Math.Max(0, now - Shown.Value) : 0;
        }

        private readonly Dictionary<long, Playing> playing = new Dictionary<long, Playing>();
        private readonly Dictionary<long, Ghost> ghosts = new Dictionary<long, Ghost>();

        /// <summary>Animations et fantômes en cours.</summary>
        public int Count => playing.Count + ghosts.Count;
        public int GhostCount => ghosts.Count;
        public IReadOnlyCollection<Ghost> Ghosts => ghosts.Values.ToArray();

        /// <summary>
        /// Joue <paramref name="animation"/> sur l'acteur à partir de <paramref name="now"/>, à la place de toute animation
        /// ponctuelle en cours. <paramref name="durationMs"/> borne <see cref="ActorAnimationMode.Loop"/> (sans elle, la boucle
        /// dure jusqu'à la suivante). <paramref name="sheet"/> : la bande déjà prête, dont la première image est à
        /// <paramref name="now"/> ; sinon la première image sera à la première pose prête vue par <see cref="Observe"/>, au plus
        /// tard 300 ms après la demande.
        /// </summary>
        public void Play(long actorId, string animation, double now, ActorAnimationMode mode, double? durationMs = null, SpriteSheet sheet = null)
        {
            if (string.IsNullOrEmpty(animation)) return;
            var entry = new Playing
            {
                Animation = animation, Mode = mode, Requested = now,
                LoopDuration = durationMs.HasValue ? Math.Max(0, durationMs.Value) : (double?)null
            };
            if (sheet != null) { entry.Shown = now; entry.Pass = sheet.DurationMs; entry.End = sheet.End; entry.Next = sheet.NextAnimation; }
            playing[actorId] = entry;
        }

        public void Cancel(long actorId) => playing.Remove(actorId);

        public bool IsPlaying(long actorId, string animation = null) =>
            playing.TryGetValue(actorId, out Playing entry) && (animation == null || string.Equals(entry.Animation, animation, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Animation à afficher pour l'acteur à <paramref name="now"/> : nom, temps écoulé depuis sa première image et
        /// boucle ou non (sinon la dernière image est tenue). Faux si aucune animation ponctuelle n'est en cours.
        /// </summary>
        public bool Override(long actorId, double now, out string animation, out double elapsed, out bool loop)
        {
            animation = null; elapsed = 0; loop = true;
            if (!playing.TryGetValue(actorId, out Playing entry)) return false;
            if (!Advance(entry, now)) { playing.Remove(actorId); return false; }
            animation = entry.Animation;
            elapsed = entry.Shown.HasValue ? Math.Max(0, now - entry.Shown.Value) : 0;
            loop = Loops(entry, elapsed);
            return true;
        }

        /// <summary>
        /// État de la bande demandée pour l'animation ponctuelle de l'acteur, tel que la carte l'a résolu : une bande absente
        /// annule l'animation (pose ordinaire), une bande prête fixe la première image et sa durée.
        /// </summary>
        public void Observe(long actorId, string animation, SpriteLoadState state, SpriteSheet sheet, double now)
        {
            if (!playing.TryGetValue(actorId, out Playing entry) || !string.Equals(entry.Animation, animation, StringComparison.Ordinal)) return;
            if (state == SpriteLoadState.Missing) { playing.Remove(actorId); return; }
            if (state != SpriteLoadState.Ready || sheet == null) return;
            if (!entry.Shown.HasValue)
            {
                if (now - entry.Requested > MaxLoadWait) { playing.Remove(actorId); return; }
                entry.Shown = now;
            }
            entry.Pass = sheet.DurationMs; entry.End = sheet.End; entry.Next = sheet.NextAnimation;
        }

        /// <summary>Ajoute (ou remplace) le fantôme de l'acteur de <paramref name="snapshot"/>, visible jusqu'à <paramref name="until"/>.</summary>
        public void AddGhost(ActorSnapshot snapshot, string animation, double now, double until)
        {
            if (snapshot == null || snapshot.CellId < 0 || until <= now) return;
            playing.Remove(snapshot.Id);
            ghosts[snapshot.Id] = new Ghost(snapshot, string.IsNullOrEmpty(animation) ? "static" : animation, now, until);
        }

        public void RemoveGhost(long actorId) => ghosts.Remove(actorId);

        /// <summary>Fantômes encore visibles à <paramref name="now"/>.</summary>
        public IEnumerable<Ghost> GhostsAt(double now) => ghosts.Values.Where(ghost => ghost.Until > now).ToArray();

        /// <summary>Retire les animations terminées et les fantômes échus ; vrai s'il reste quelque chose qui change à l'écran.</summary>
        public bool Purge(double now)
        {
            foreach (long id in ghosts.Where(pair => pair.Value.Until <= now).Select(pair => pair.Key).ToArray()) ghosts.Remove(id);
            bool moving = ghosts.Count > 0;
            foreach (KeyValuePair<long, Playing> pair in playing.ToArray())
            {
                if (!Advance(pair.Value, now)) { playing.Remove(pair.Key); continue; }
                Playing entry = pair.Value;
                double elapsed = entry.Shown.HasValue ? now - entry.Shown.Value : 0;
                // Dernière image tenue (fin « arret ») : plus rien ne bouge, la minuterie peut s'arrêter.
                bool frozen = entry.Shown.HasValue && entry.Pass.HasValue && entry.Mode != ActorAnimationMode.Loop
                    && elapsed >= entry.Pass.Value && entry.End == SpriteEnd.Stop;
                if (!frozen) moving = true;
            }
            return moving;
        }

        public void Clear()
        {
            playing.Clear();
            ghosts.Clear();
        }

        private static bool Loops(Playing entry, double elapsed)
        {
            if (entry.Mode == ActorAnimationMode.Loop) return true;
            if (!entry.Pass.HasValue || elapsed < entry.Pass.Value) return false;
            return entry.Mode == ActorAnimationMode.Hold && entry.End == SpriteEnd.Loop;
        }

        /// <summary>Fait avancer l'animation (fin, enchaînement) ; faux si elle est terminée.</summary>
        private static bool Advance(Playing entry, double now)
        {
            while (true)
            {
                if (!entry.Shown.HasValue) return now - entry.Requested <= MaxLoadWait;
                double elapsed = now - entry.Shown.Value;
                if (entry.Mode == ActorAnimationMode.Loop) return !entry.LoopDuration.HasValue || elapsed < entry.LoopDuration.Value;
                // Bande jamais revue depuis (carte non redessinée) : abandon plutôt qu'une animation sans fin.
                if (!entry.Pass.HasValue) return elapsed <= MaxLoadWait;
                if (elapsed < entry.Pass.Value) return true;
                switch (entry.End)
                {
                    case SpriteEnd.Stop: return true;
                    case SpriteEnd.Loop: return entry.Mode == ActorAnimationMode.Hold;
                    case SpriteEnd.Next:
                        if (entry.Next == null || entry.Chain >= MaxChain) return false;
                        // La bande suivante part à la fin de la précédente, dès qu'elle est prête.
                        entry.Requested = entry.Shown.Value + entry.Pass.Value;
                        entry.Animation = entry.Next; entry.Shown = null; entry.Pass = null; entry.Next = null; entry.End = SpriteEnd.Loop;
                        entry.Chain++;
                        continue;
                    default: return false; // static : retour à la pose ordinaire
                }
            }
        }
    }
}
