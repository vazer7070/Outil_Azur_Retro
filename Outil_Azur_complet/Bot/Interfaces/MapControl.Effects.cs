using System;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Interfaces
{
    /// <summary>Famille d'animation d'un <see cref="VisualEvent"/> (plan des animations, § 3).</summary>
    public enum VisualFamily
    {
        /// <summary>Sans affichage (165, action inconnue).</summary>
        None,
        /// <summary>Chiffres au-dessus des têtes : PV, PA, PM (GA 100, 108, 110, 101, 102, 111, 120, 168, 78, 127, 128, 129, 169) et <c>IQ</c>.</summary>
        Points,
        /// <summary>Coup reçu : GA 104 (les PV perdus jouent aussi <c>hit</c>, voir <see cref="MapControl.ShowVisual"/>).</summary>
        Hit,
        /// <summary>Mort : GA 103, avec l'instantané de l'acteur.</summary>
        Death,
        /// <summary>Sort lancé (300), attaque à l'arme (303), piège et glyphe déclenchés (306, 307).</summary>
        Spell,
        /// <summary>Coup critique (301, 304) et échec critique (302, 305).</summary>
        Critical,
        /// <summary>Émote <c>eUK</c> (durée facultative).</summary>
        Emote,
        /// <summary>Utilisation d'un objet interactif : GA 501 (cellule, durée).</summary>
        Harvest,
        /// <summary>Ballons et feux d'artifice : GA 208 et 228.</summary>
        MapEffect
    }

    /// <summary>
    /// Effets visuels de la carte (lot AN1) : abonnements aux événements visuels du protocole (combat, objets interactifs,
    /// carte hors combat, émotes), relais sur le fil de l'interface et aiguillage vers une méthode par famille. Les méthodes
    /// de famille sont vides ici : chaque lot remplit les siennes (AN2 : chiffres, coups, morts ; AN4 : sorts, critiques,
    /// récolte, effets de carte ; AN8 : émotes), avec les aides <see cref="TryEnqueueVisual"/>, <see cref="TryAddEffect"/>,
    /// <see cref="TryPlayAnimation"/> et <see cref="TryAddGhost"/>. Carte cachée (<see cref="MapShown"/> faux) : les files
    /// sont vidées et rien n'y entre ; seuls les chiffres restent transmis à <c>OnPoints</c>, qui décide.
    /// Le protocole n'attend jamais l'affichage.
    /// </summary>
    public partial class MapControl
    {
        private Func<bool> mapShown;
        private bool visualsHooked;

        /// <summary>
        /// Condition « carte affichée », injectable (tests : la carte se dessine dans un Bitmap, hors de toute fenêtre).
        /// Par défaut : poignée créée, contrôle visible et fenêtre non réduite.
        /// </summary>
        public Func<bool> MapShown
        {
            get => mapShown ?? DefaultMapShown;
            set => mapShown = value;
        }

        /// <summary>Évaluation sûre de <see cref="MapShown"/> (une condition qui échoue vaut « cachée »).</summary>
        public bool IsMapShown
        {
            get
            {
                try { return MapShown(); }
                catch (Exception error) when (!(error is OutOfMemoryException)) { return false; }
            }
        }

        /// <summary>Un événement visuel vient d'être aiguillé (fil de l'interface) : famille, événement, carte affichée ou non.</summary>
        public event Action<VisualFamily, VisualEvent, bool> VisualRouted;

        /// <summary>Séquenceur, effets et animations ponctuelles de la vue.</summary>
        public FightVisualSequencer Sequencer => UserMap.Sequencer;
        public MapEffectSet Effects => UserMap.Effects;
        public ActorAnimationQueue AnimationQueue => UserMap.AnimationQueue;
        /// <summary>Vue de la carte (dessin dans un Bitmap, horloge, <see cref="UserMapControl.TickAnimations"/>).</summary>
        public UserMapControl MapSurface => UserMap;

        private bool DefaultMapShown()
        {
            if (IsDisposed || !IsHandleCreated || !Visible) return false;
            Form form = FindForm();
            return form == null || form.WindowState != FormWindowState.Minimized;
        }

        /// <summary>Famille d'un événement visuel (aiguillage de <see cref="ShowVisual"/>).</summary>
        public static VisualFamily FamilyOf(VisualEvent visual)
        {
            if (visual == null) return VisualFamily.None;
            switch (visual.Source)
            {
                case VisualSource.Quantity: return VisualFamily.Points;
                case VisualSource.Emote: return VisualFamily.Emote;
            }
            switch (visual.ActionId)
            {
                case 100: case 108: case 110:
                case 101: case 102: case 111: case 120: case 168:
                case 78: case 127: case 128: case 129: case 169:
                    return VisualFamily.Points;
                case 104: return VisualFamily.Hit;
                case 103: return VisualFamily.Death;
                case 300: case 303: case 306: case 307: return VisualFamily.Spell;
                case 301: case 302: case 304: case 305: return VisualFamily.Critical;
                case 501: return VisualFamily.Harvest;
                case 208: case 228: return VisualFamily.MapEffect;
                default: return VisualFamily.None;
            }
        }

        private void SubscribeVisuals()
        {
            if (visualsHooked) return;
            GameClass game = Account.Game;
            if (game == null) return;
            visualsHooked = true;
            if (game.Fight != null) { game.Fight.VisualEvent += OnProtocolVisual; game.Fight.CombatChanged += ResetVisualsOnCombat; }
            if (game.Map != null) game.Map.VisualEvent += OnProtocolVisual;
            if (game.Interactions?.Interactive != null) game.Interactions.Interactive.VisualEvent += OnProtocolVisual;
            if (game.Chat != null) game.Chat.EmoteVisualReceived += OnProtocolVisual;
        }

        private void UnsubscribeVisuals(GameClass game)
        {
            if (!visualsHooked) return;
            visualsHooked = false;
            if (game.Fight != null) { game.Fight.VisualEvent -= OnProtocolVisual; game.Fight.CombatChanged -= ResetVisualsOnCombat; }
            if (game.Map != null) game.Map.VisualEvent -= OnProtocolVisual;
            if (game.Interactions?.Interactive != null) game.Interactions.Interactive.VisualEvent -= OnProtocolVisual;
            if (game.Chat != null) game.Chat.EmoteVisualReceived -= OnProtocolVisual;
        }

        /// <summary>Fil réseau → fil de l'interface ; l'événement est immuable, rien n'est relu du modèle ici.</summary>
        private void OnProtocolVisual(VisualEvent visual)
        {
            if (visual != null) OnUi(() => ShowVisual(visual));
        }

        /// <summary>Début ou fin de combat : le client vide ses séquenceurs et ses effets ; la carte fait de même.</summary>
        private void ResetVisualsOnCombat() => OnUi(UserMap.ClearVisuals);

        /// <summary>
        /// Aiguille un événement visuel vers sa famille (fil de l'interface ; utilisable par les tests). Les PV perdus ou
        /// gagnés (100, 108, 110) vont à <c>OnPoints</c> puis à <c>OnHit</c>, comme <c>updateLP</c> du client. Une méthode de
        /// famille qui échoue est journalisée sans interrompre la carte.
        /// </summary>
        public void ShowVisual(VisualEvent visual)
        {
            if (visual == null || IsDisposed) return;
            VisualFamily family = FamilyOf(visual);
            bool shown = IsMapShown;
            try
            {
                if (!shown)
                {
                    // Carte cachée : aucune file ne grossit ; seuls les chiffres sont transmis (OnPoints décide).
                    UserMap.ClearVisuals();
                    if (family == VisualFamily.Points) OnPoints(visual);
                }
                else
                    switch (family)
                    {
                        case VisualFamily.Points:
                            OnPoints(visual);
                            if (visual.Source == VisualSource.GameAction && (visual.ActionId == 100 || visual.ActionId == 108 || visual.ActionId == 110)) OnHit(visual);
                            break;
                        case VisualFamily.Hit: OnHit(visual); break;
                        case VisualFamily.Death: OnDeath(visual); break;
                        case VisualFamily.Spell: OnSpell(visual); break;
                        case VisualFamily.Critical: OnCritical(visual); break;
                        case VisualFamily.Emote: OnEmote(visual); break;
                        case VisualFamily.Harvest: OnHarvest(visual); break;
                        case VisualFamily.MapEffect: OnMapEffect(visual); break;
                    }
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                try { Account.Logger?.LogError("CARTE", "Effet visuel " + visual + " non affiché : " + error.Message); }
                catch (Exception) { /* journal fermé */ }
            }
            try { VisualRouted?.Invoke(family, visual, shown); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account.Logger?.LogException("CARTE", error); }
        }

        // ------------------------------------------------------------ aides des familles (fil de l'interface)

        /// <summary>Ajoute une étape à la file <paramref name="queueId"/> ; faux (rien n'est ajouté) si la carte est cachée.</summary>
        public bool TryEnqueueVisual(long queueId, VisualStep step)
        {
            if (step == null) return false;
            if (!IsMapShown) { UserMap.ClearVisuals(); return false; }
            UserMap.EnqueueVisual(queueId, step);
            return true;
        }

        /// <summary>Ajoute un effet ; faux si la carte est cachée (l'effet refusé est libéré).</summary>
        public bool TryAddEffect(IMapEffect effect)
        {
            if (effect == null) return false;
            if (!IsMapShown)
            {
                UserMap.ClearVisuals();
                (effect as IDisposable)?.Dispose();
                return false;
            }
            UserMap.AddEffect(effect);
            return true;
        }

        /// <summary>Animation ponctuelle sur un acteur ; faux si la carte est cachée, l'acteur absent ou la bande manquante.</summary>
        public bool TryPlayAnimation(long actorId, string animation, ActorAnimationMode mode, double? durationMs = null)
        {
            if (!IsMapShown) { UserMap.ClearVisuals(); return false; }
            return UserMap.PlayActorAnimation(actorId, animation, mode, durationMs);
        }

        /// <summary>Fantôme d'un acteur disparu (mort) jusqu'à <paramref name="durationMs"/> ms d'ici ; faux si la carte est cachée.</summary>
        public bool TryAddGhost(ActorSnapshot snapshot, string animation, double durationMs)
        {
            if (snapshot == null) return false;
            if (!IsMapShown) { UserMap.ClearVisuals(); return false; }
            UserMap.AddGhost(snapshot, animation, UserMap.Now + Math.Max(0, durationMs));
            return true;
        }

        // ------------------------------------------------------------ familles : remplies par AN2, AN4 et AN8

        /// <summary>Chiffres PV, PA, PM et quantité récoltée (AN2). Appelée même carte cachée.</summary>
        private void OnPoints(VisualEvent visual) { }

        /// <summary>Coup reçu : <c>hit</c> (AN2).</summary>
        private void OnHit(VisualEvent visual) { }

        /// <summary>Mort : fantôme <c>die</c> d'après <see cref="VisualEvent.Snapshot"/>, étape de 1 500 ms (AN2).</summary>
        private void OnDeath(VisualEvent visual) { }

        /// <summary>Sort, arme, piège, glyphe : animation du lanceur et effet (AN4, AN5).</summary>
        private void OnSpell(VisualEvent visual) { }

        /// <summary>Coup critique (clip du dessus) et échec critique (bulle) (AN4).</summary>
        private void OnCritical(VisualEvent visual) { }

        /// <summary>Émote, durée facultative (AN8).</summary>
        private void OnEmote(VisualEvent visual) { }

        /// <summary>Utilisation d'un objet interactif : boucle d'outil pendant la durée (AN4).</summary>
        private void OnHarvest(VisualEvent visual) { }

        /// <summary>Ballons (208) et feux d'artifice (228) (AN4).</summary>
        private void OnMapEffect(VisualEvent visual) { }
    }
}
