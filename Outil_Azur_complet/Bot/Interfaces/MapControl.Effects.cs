using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso;

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
    /// carte hors combat, émotes), relais sur le fil de l'interface et aiguillage vers une méthode par famille. Chaque lot
    /// remplit les siennes (AN2 : chiffres, coups, morts, remplies ; AN4 : sorts, critiques, récolte, effets de carte ;
    /// AN8 : émotes), avec les aides <see cref="TryEnqueueVisual"/>, <see cref="TryAddEffect"/>,
    /// <see cref="TryPlayAnimation"/> et <see cref="TryAddGhost"/>. Carte cachée (<see cref="MapShown"/> faux) : les files
    /// sont vidées et rien n'y entre ; seuls les chiffres restent transmis à <c>OnPoints</c>, qui décide.
    /// Le protocole n'attend jamais l'affichage.
    /// </summary>
    public partial class MapControl
    {
        private Func<bool> mapShown;
        private bool visualsHooked;
        /// <summary>Combat en cours au dernier <c>CombatChanged</c> vu par la carte.</summary>
        private bool visualsInFight;

        /// <summary>
        /// Condition « carte affichée », injectable (tests : la carte se dessine dans un Bitmap, hors de toute fenêtre).
        /// Par défaut : poignée créée, contrôle visible dans une fenêtre affichée et non réduite.
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
            // Contrôle détaché de toute fenêtre (Mono crée parfois la poignée dès la construction) : rien n'est affiché.
            Form form = FindForm();
            return form != null && form.Visible && form.WindowState != FormWindowState.Minimized;
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
            visualsInFight = game.Fight?.IsInFight == true;
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

        /// <summary>
        /// Début ou fin de combat : le client vide ses séquenceurs et ses effets ; la carte fait de même. <c>CombatChanged</c> est
        /// levé à chaque changement de l'état du combat (après chaque GA, à chaque tour) : seule l'entrée dans un combat ou sa
        /// sortie vide les files, sinon les chiffres, coups et morts d'un GA seraient effacés aussitôt ajoutés (lot AN2).
        /// </summary>
        private void ResetVisualsOnCombat() => OnUi(() =>
        {
            bool inFight = Account.Game?.Fight?.IsInFight == true;
            if (inFight == visualsInFight) return;
            visualsInFight = inFight;
            UserMap.ClearVisuals();
        });

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

        // Chiffres, coups reçus et morts (lot AN2) : GameActions.onActions, updateLP/updateAP/updateMP, addSpritePoints,
        // Infos.onQuantity et l'action Die du client 1.34.

        /// <summary>
        /// Attente maximale du fantôme immobile d'un mort dont l'étape <c>Die</c> n'a pas encore commencé (garde-fou : il est
        /// remplacé au départ de l'étape, ou effacé avec les files au changement de carte ou de combat).
        /// </summary>
        public const double PendingDeathMaxWait = 30000;
        /// <summary>Sans bande <c>die</c>, le fantôme garde <c>static</c> pendant 300 ms (choix du bot : pas de disparition sèche).</summary>
        public const double DeathWithoutStripMs = 300;

        private PointsLayer points;
        private Tool_BotProtocol.Config.BotOptions options;

        /// <summary>Options du client, relues à chaque chiffre (<c>PointsOverHead</c>) ; <c>null</c> : <see cref="Tool_BotProtocol.Config.BotOptions.Current"/>.</summary>
        public Tool_BotProtocol.Config.BotOptions Options
        {
            get => options ?? Tool_BotProtocol.Config.BotOptions.Current;
            set => options = value;
        }

        /// <summary>Couche des chiffres en cours, ou <c>null</c> s'il n'y en a pas (tests, diagnostic).</summary>
        public PointsLayer Points => points != null && !points.IsDisposed ? points : null;

        /// <summary>
        /// Chiffres PV (100, 108, 110), PA (101, 102, 111, 120, 168), PM (78, 127, 128, 129, 169) et quantité récoltée (<c>IQ</c>).
        /// Comme le client : une variation nulle n'affiche rien, 102 et 129 seulement sur le joueur dont c'est le tour, texte
        /// <c>String(v)</c>. Les chiffres d'un <c>GA</c> passent par la file de son séquenceur (étape non bloquante) ;
        /// <c>IQ</c> est affiché aussitôt (<c>Infos.onQuantity</c> n'utilise pas de séquenceur). Carte cachée : rien n'est
        /// retenu (le client n'ajoute aucun chiffre quand sa fenêtre n'a pas le focus).
        /// </summary>
        private void OnPoints(VisualEvent visual)
        {
            if (!IsMapShown) return;
            if (visual.Source == VisualSource.Quantity)
            {
                string quantity = visual.Fields.Count > 1 ? visual.Fields[1] : visual.Value.ToString(CultureInfo.InvariantCulture);
                AddPoints(visual.ActorId, quantity, PointsLayer.QuantityColor, false, UserMap.Now);
                return;
            }
            if (visual.Source != VisualSource.GameAction || visual.Value == 0) return;
            Color color;
            bool currentTurnOnly = false;
            switch (visual.ActionId)
            {
                case 100: case 108: case 110: color = PointsLayer.LifeColor; break;
                case 101: case 102: case 111: case 120: case 168: color = PointsLayer.ActionPointsColor; currentTurnOnly = visual.ActionId == 102; break;
                case 78: case 127: case 128: case 129: case 169: color = PointsLayer.MovementPointsColor; currentTurnOnly = visual.ActionId == 129; break;
                default: return;
            }
            long target = visual.TargetId;
            string text = visual.Value.ToString(CultureInfo.InvariantCulture);
            TryEnqueueVisual(visual.QueueId, VisualStep.Instant("Points", now => AddPoints(target, text, color, currentTurnOnly, now)));
        }

        /// <summary><c>addSpritePoints</c> : option relue à chaque ajout, condition « joueur du tour » relue au départ de l'étape.</summary>
        private void AddPoints(long target, string text, Color color, bool currentTurnOnly, double now)
        {
            if (!Options.PointsOverHead) return;
            if (currentTurnOnly && (Account.Game?.Fight?.CurrentActorId ?? 0) != target) return;
            if (points == null || points.IsDisposed) points = new PointsLayer();
            if (points.Add(target, text, color, now)) TryAddEffect(points);
        }

        /// <summary>
        /// Coup reçu : <c>hit</c> sur la cible d'une perte ou d'un gain de PV non nul (<c>updateLP</c>, quel que soit le signe) et
        /// sur l'acteur de GA 104 (« ne peut pas se déplacer »), étape non bloquante. Bande <c>hitR</c>/<c>hitL</c> d'après
        /// l'orientation <c>d | 1</c> (choix du bot : le client attacherait <c>staticF</c> faute de <c>hitF</c>), puis retour à
        /// <c>static</c>. Sans bande <c>hit</c> pour ce gfx, rien ne change.
        /// </summary>
        private void OnHit(VisualEvent visual)
        {
            if (visual.Source != VisualSource.GameAction) return;
            bool life = visual.ActionId == 100 || visual.ActionId == 108 || visual.ActionId == 110;
            if (!life && visual.ActionId != 104) return;
            if (life && visual.Value == 0) return;
            long target = visual.ActionId == 104 && visual.TargetId == 0 ? visual.ActorId : visual.TargetId;
            TryEnqueueVisual(visual.QueueId, VisualStep.Instant("Hit", now => TryPlayAnimation(target, "hit", ActorAnimationMode.Once)));
        }

        /// <summary>
        /// Mort (GA 103) : le modèle a déjà retiré l'acteur ; un fantôme tiré de <see cref="VisualEvent.Snapshot"/> le garde
        /// immobile jusqu'au départ de l'étape <c>Die</c> de sa file (le client laisse le sprite en place jusque-là), puis joue
        /// <c>die</c> pendant exactement 1 500 ms (<c>forceTimeout</c>) avant de disparaître (<c>mc.clear</c>) ; une bande plus
        /// courte tient sa dernière image. Sans bande <c>die</c>, le fantôme garde <c>static</c> 300 ms ; l'étape dure 1 500 ms
        /// dans tous les cas. Sans instantané ni cellule, rien (le client ne fait rien sans clip).
        /// </summary>
        private void OnDeath(VisualEvent visual)
        {
            ActorSnapshot body = visual.Snapshot;
            if (body == null || body.CellId < 0) return;
            UserMap.Sprites.Prefetch(body.Gfx, body.Direction, "die");
            if (!TryAddGhost(body, "static", PendingDeathMaxWait)) return;
            if (!TryEnqueueVisual(visual.QueueId, VisualStep.Die(now => StartDeath(body))))
                UserMap.AnimationQueue.RemoveGhost(body.Id);
        }

        private void StartDeath(ActorSnapshot body)
        {
            try
            {
                bool strip = HasStrip(body, "die");
                TryAddGhost(body, strip ? "die" : "static", strip ? FightVisualSequencer.DieDuration : DeathWithoutStripMs);
            }
            catch
            {
                // Le fantôme d'attente ne doit pas survivre à une étape en échec.
                UserMap.AnimationQueue.RemoveGhost(body.Id);
                throw;
            }
        }

        /// <summary>Bande présente pour ce gfx : prête, ou en lecture avec sa ligne dans <c>ancres.tsv</c>.</summary>
        private bool HasStrip(ActorSnapshot body, string animation)
        {
            SpritePose pose = UserMap.Sprites.Resolve(body.Gfx, body.Direction, body.NoFlip, animation);
            if (pose.State == SpriteLoadState.Ready) return pose.Sheet != null;
            if (pose.State == SpriteLoadState.Missing) return false;
            return UserMap.Sprites.Duration(body.Gfx, animation, body.Direction).HasValue;
        }

        // Sorts, armes, coups et échecs critiques, récolte, ballons et feux d'artifice (lot AN4) : GameActions.onActions
        // (300 à 305, 501, 208, 228), SpriteHandler.launchVisualEffect et VisualEffectHandler.addEffect du client 1.34.

        /// <summary>Texte de la bulle d'échec critique quand <c>lang.xml</c> n'a pas <c>CRITICAL_MISS</c>.</summary>
        public const string CriticalMissFallback = "Échec critique";

        private readonly SpellEffects spellEffects = new SpellEffects();
        /// <summary>Clip du coup critique en cours, par acteur (un nouveau remplace l'ancien, comme le clip du dessus du client).</summary>
        private readonly Dictionary<long, IMapEffect> criticalClips = new Dictionary<long, IMapEffect>();

        /// <summary>Index et profondeurs des effets de sorts de la carte (tests, diagnostic).</summary>
        public SpellEffects SpellEffects => spellEffects;

        /// <summary>
        /// Sort lancé (GA300) et coup d'arme (GA303). GA300 <c>sort,cellule,fichier,niveau,type,anim,devant</c> : <c>anim</c>
        /// <c>-1</c> n'affiche rien, <c>-2</c> tourne le lanceur et pose l'effet sans animation ; sinon, dans la file propre du
        /// lanceur : direction vers la cellule, <c>anim&lt;n&gt;</c> bloquante (fin de la bande, au plus 1 000 ms), puis l'effet
        /// du type (10 et 12 au lanceur, 11 à la cellule ; 12 retient la file jusqu'à sa fin, au plus 1 000 ms). GA303 sans
        /// fichier (StarLoco n'envoie que la cellule) : direction puis <c>ToolAnimation</c> bloquante, dans la file du GA ;
        /// avec fichier : comme un sort de niveau 1 animé par <c>ToolAnimation</c>. 306 et 307 (pièges, glyphes) : rien,
        /// StarLoco y envoie le fichier 0, qui n'existe pas.
        /// </summary>
        private void OnSpell(VisualEvent visual)
        {
            if (visual.Source != VisualSource.GameAction) return;
            SpellLaunch launch;
            switch (visual.ActionId)
            {
                case 300:
                    if (!SpellLaunch.TryParseSpell(visual.Fields, out launch)) { IgnoredVisual(visual); return; }
                    if (!SpellEffects.TryParseAnimation(launch.Animation, true, out string animation)) return;
                    LaunchSpellVisual(visual.ActorId, launch.CellId, launch.File, launch.Type, launch.InFront, animation);
                    break;
                case 303:
                    if (!SpellLaunch.TryParseWeapon(visual.Fields, out launch)) { IgnoredVisual(visual); return; }
                    string tool = ToolAnimation(visual.ActorId);
                    if (!string.IsNullOrEmpty(launch.File))
                    {
                        LaunchSpellVisual(visual.ActorId, launch.CellId, launch.File, launch.Type, launch.InFront, tool);
                        break;
                    }
                    long actor = visual.ActorId;
                    short cell = launch.CellId;
                    PrefetchAnimation(actor, cell, tool);
                    TryEnqueueVisual(visual.QueueId, VisualStep.Instant("Direction", now => TurnTowards(actor, cell)));
                    TryEnqueueVisual(visual.QueueId, CasterAnimationStep("Arme", actor, tool));
                    break;
            }
        }

        /// <summary>
        /// Coup critique (301, 304) : clip <c>extra/5</c> au-dessus du lanceur pendant 5 000 ms, visible sur un personnage
        /// seulement en <c>staticF</c> (<c>xtraClipTopAnimations</c>). Échec critique (302, 305) : bulle « Échec critique » au-dessus
        /// du lanceur. Étapes non bloquantes de la file du GA.
        /// </summary>
        private void OnCritical(VisualEvent visual)
        {
            if (visual.Source != VisualSource.GameAction) return;
            long actor = visual.ActorId;
            switch (visual.ActionId)
            {
                case 301: case 304:
                    using (UserMap.Sprites.ResolveFixed(SpellEffects.ExtraFamily, SpellEffects.CriticalHitClip, SpellEffects.SceneAnimation)) { }
                    TryEnqueueVisual(visual.QueueId, VisualStep.Instant("CoupCritique", now => AddCriticalClip(actor, now)));
                    break;
                case 302: case 305:
                    TryEnqueueVisual(visual.QueueId, VisualStep.Instant("EchecCritique", now => UserMap.ShowBubble(actor, CriticalMissText())));
                    break;
            }
        }

        /// <summary>Émote, durée facultative (AN8).</summary>
        private void OnEmote(VisualEvent visual) { }

        /// <summary>
        /// Utilisation d'un objet interactif (GA501 <c>cellule,durée[,anim]</c>), dans la file du GA : direction vers l'objet,
        /// puis <c>anim&lt;n&gt;</c> (troisième champ) ou <c>ToolAnimation</c> en boucle pendant la durée, puis retour à
        /// <c>static</c>. L'étape dure exactement la durée pour le personnage du compte et ne retient pas la file des autres.
        /// </summary>
        private void OnHarvest(VisualEvent visual)
        {
            if (visual.Source != VisualSource.GameAction || visual.ActionId != 501) return;
            int duration = visual.DurationMs ?? (visual.TryField(1, out int field) ? field : -1);
            if (visual.CellId < 0 || duration < 0) { IgnoredVisual(visual); return; }
            long actor = visual.ActorId;
            short cell = visual.CellId;
            // Troisième champ : anim<n> s'il est lisible (un nom illisible ne donne aucune animation), sinon ToolAnimation.
            string extra = visual.Fields.Count > 2 ? visual.Fields[2] : null;
            string animation = string.IsNullOrEmpty(extra) ? ToolAnimation(actor)
                : SpellEffects.IsAnimationName("anim" + extra) ? "anim" + extra : null;
            bool self = actor == (Account.Game?.character?.id ?? long.MinValue);
            PrefetchAnimation(actor, cell, animation);
            TryEnqueueVisual(visual.QueueId, VisualStep.Instant("Direction", now => TurnTowards(actor, cell)));
            Action<double> loop = now => { if (animation != null) TryPlayAnimation(actor, animation, ActorAnimationMode.Loop, duration); };
            TryEnqueueVisual(visual.QueueId, self ? VisualStep.Timed("Recolte", loop, duration) : VisualStep.Instant("Recolte", loop));
        }

        /// <summary>
        /// Ballons (208) et feux d'artifice (228) <c>cellule,fichier,type,anim[,niveau]</c>, hors combat : comme un sort, dans la
        /// file propre de l'acteur, effet toujours devant le sprite ; <c>anim</c> vaut <c>anim</c> suivi du champ, sans les codes
        /// <c>-1</c> et <c>-2</c> du GA300. La couleur à 200 % de 208 n'est pas reproduite.
        /// </summary>
        private void OnMapEffect(VisualEvent visual)
        {
            if (visual.Source != VisualSource.GameAction) return;
            if (!SpellLaunch.TryParseMapEffect(visual.Fields, out SpellLaunch launch)) { IgnoredVisual(visual); return; }
            if (!SpellEffects.TryParseAnimation(launch.Animation, false, out string animation)) return;
            LaunchSpellVisual(visual.ActorId, launch.CellId, launch.File, launch.Type, true, animation);
        }

        /// <summary>
        /// <c>launchVisualEffect</c> : direction (non bloquante), animation du lanceur (bloquante, au plus 1 000 ms) puis effet,
        /// dans la file propre du lanceur. Lanceur absent de la carte : rien ; invisible : pas d'effet (<c>_visible</c> du
        /// client lu à la réception). L'effet et l'animation sont demandés au pool dès la réception.
        /// </summary>
        private void LaunchSpellVisual(long caster, short cell, string file, int type, bool inFront, string animation)
        {
            if (!UserMap.TryGetActorAnchor(caster, out ActorAnchor anchor) || anchor.IsGhost) return;
            bool visible = anchor.IsVisible;
            int gfx = int.TryParse(file, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
            bool hasEffect = SpellEffects.HasEffect(type);
            // Lot AN5 : les projectiles (20 à 51) sont dessinés aussi, par SpellEffects.Launch.
            bool drawn = hasEffect && (SpellEffects.IsDrawn(type) || SpellProjectiles.IsProjectile(type)) && gfx >= 0 && visible;
            if (drawn) SpellEffects.Prefetch(UserMap.Sprites, gfx, type);
            if (animation != null) PrefetchAnimation(caster, cell, animation);
            if (!TryEnqueueVisual(caster, VisualStep.Instant("Direction", now => TurnTowards(caster, cell)))) return;
            if (animation != null) TryEnqueueVisual(caster, CasterAnimationStep("Sort", caster, animation));
            if (!hasEffect) return;
            IMapEffect effect = null;
            Action<double> start = now => { if (drawn) effect = AddSpellEffect(caster, cell, gfx, type, inFront, now); };
            TryEnqueueVisual(caster, SpellEffects.IsBlocking(type)
                ? VisualStep.Waiting("Effet", start, now => SpellEffects.Released(effect, now))
                : VisualStep.Instant("Effet", start));
        }

        /// <summary>Étape bloquante de l'animation du lanceur : libérée à la fin de la bande, au plus tard à 1 000 ms ; sautée sans bande.</summary>
        private VisualStep CasterAnimationStep(string name, long actor, string animation)
        {
            bool started = false;
            return VisualStep.Waiting(name, now => started = TryPlayAnimation(actor, animation, ActorAnimationMode.Once),
                now => !started || !AnimationQueue.Override(actor, now, out string playing, out double _, out bool _)
                    || !string.Equals(playing, animation, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Scène du sort au lanceur (types 10 et 12, pied au départ de l'étape) ou au centre de la cellule (11) ; projectile du
        /// pied du lanceur vers le centre de la cellule (20 à 51, lot AN5).
        /// </summary>
        private IMapEffect AddSpellEffect(long caster, short cell, int gfx, int type, bool inFront, double now)
        {
            PointF? from = UserMap.TryGetActorAnchor(caster, out ActorAnchor anchor) ? anchor.WorldFoot : (PointF?)null;
            PointF? to = UserMap.TryGetCellCenter(cell, out PointF center) ? center : (PointF?)null;
            return spellEffects.Launch(UserMap.Sprites, Effects, TryAddEffect, gfx, type, from, to, cell, inFront, now);
        }

        /// <summary>Clip du coup critique au-dessus de l'acteur pendant 5 000 ms, à la place du précédent s'il est encore là.</summary>
        private void AddCriticalClip(long actor, double now)
        {
            if (criticalClips.TryGetValue(actor, out IMapEffect previous)) { criticalClips.Remove(actor); Effects.Remove(previous); }
            var clip = new ActorAttachedEffect(actor, UserMap.Sprites.ResolveFixed(SpellEffects.ExtraFamily, SpellEffects.CriticalHitClip,
                SpellEffects.SceneAnimation), true, now, SpellEffects.CriticalHitDuration);
            if (TryAddEffect(clip)) criticalClips[actor] = clip;
            foreach (long id in criticalClips.Where(pair => !Effects.Active.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
                criticalClips.Remove(id);
        }

        private static string CriticalMissText()
        {
            try { if (LangData.Text.Has("CRITICAL_MISS")) return LangData.Text.Get("CRITICAL_MISS"); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { /* textes illisibles : repli */ }
            return CriticalMissFallback;
        }

        /// <summary>
        /// <c>ToolAnimation</c> du client : <c>anim</c> suivi de l'attribut <c>an</c> de l'arme (premier accessoire du
        /// <c>GM</c>, <c>items.xml</c>), sinon <c>anim0</c> en combat et <c>anim3</c> hors combat ; null si l'attribut n'est
        /// pas un nom lisible (<see cref="SpellEffects.IsAnimationName"/>).
        /// </summary>
        private string ToolAnimation(long actorId)
        {
            Tool_BotProtocol.Game.Maps.Map map = Account.Game?.Map;
            MapActor actor = map?.GetActor(actorId);
            if (actor == null && actorId == (Account.Game?.character?.id ?? long.MinValue)) actor = map?.Self;
            IReadOnlyList<ActorAccessory> accessories = (actor as PlayerActor)?.Accessories ?? (actor as NpcActor)?.Accessories
                ?? (actor as FightMonsterActor)?.Accessories;
            ActorAccessory weapon = accessories?.FirstOrDefault(accessory => accessory.Slot == 0);
            string an = null;
            if (weapon != null)
            {
                IReadOnlyDictionary<string, string> item = LangData.Raw("items", "objet", weapon.TemplateId.ToString(CultureInfo.InvariantCulture));
                if (item != null) item.TryGetValue("an", out an);
            }
            // Attribut illisible (le nom deviendrait un nom de fichier) : aucune animation, le lanceur garde sa pose.
            if (!string.IsNullOrEmpty(an)) return SpellEffects.IsAnimationName("anim" + an) ? "anim" + an : null;
            return Account.Game?.Fight?.IsInFight == true ? "anim0" : "anim3";
        }

        /// <summary>
        /// <c>autoCalculateSpriteDirection</c> : tourne l'acteur vers la cellule (1, 3, 5 ou 7), rien sur sa propre cellule.
        /// L'orientation n'est qu'un état d'affichage du modèle (aucun paquet n'est envoyé).
        /// </summary>
        private void TurnTowards(long actorId, short cell)
        {
            Tool_BotProtocol.Game.Maps.Map map = Account.Game?.Map;
            if (map == null) return;
            CharacterClass self = Account.Game.character;
            bool isSelf = self != null && self.id == actorId;
            MapActor actor = map.GetActor(actorId) ?? (isSelf ? map.Self : null);
            int from = isSelf ? self.Cell?.CellID ?? -1 : actor?.Cell?.CellID ?? actor?.CellId ?? -1;
            int direction = SpellEffects.DirectionTo(from, cell, map.MapWidth);
            if (direction < 0) return;
            if (isSelf) self.Orientation = direction;
            if (actor != null) actor.Orientation = direction;
        }

        /// <summary>Lecture anticipée de la bande du lanceur dans l'orientation qu'il prendra (rien n'est décodé sur ce fil).</summary>
        private void PrefetchAnimation(long actorId, short cell, string animation)
        {
            if (string.IsNullOrEmpty(animation)) return;
            UserMapControl.ActorVisualState state = UserMap.GetActorVisualStates()
                .FirstOrDefault(s => s.ActorId == actorId && s.MemberIndex < 0 && !s.IsGhost);
            if (state == null || state.GFX <= 0) return;
            int direction = SpellEffects.DirectionTo(state.CellId, cell, Account.Game?.Map?.MapWidth ?? 0);
            UserMap.Sprites.Prefetch(state.GFX, direction < 0 ? state.Orientation : direction, animation);
        }

        private void IgnoredVisual(VisualEvent visual)
        {
            try { Account.Logger?.LogDebug("CARTE", "Effet visuel illisible ignoré : " + visual); }
            catch (Exception) { /* journal fermé */ }
        }
    }
}
