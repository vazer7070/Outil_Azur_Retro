using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.Perso;
using Map = Tool_BotProtocol.Game.Maps.Map;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Acteurs de la carte : un état visuel par sprite (sprite principal, membres liés d'un groupe, épées d'équipe), posé
    /// comme le client 1.34 (orientation et miroir, ancre au pied, profondeur cellule × 100 + 30), animé le long des chemins,
    /// avec survol au pixel près, surtêtes, bulles, smileys et émotes. Les PNG sont lus hors du fil de l'interface
    /// (<see cref="ActorSprites"/>, <see cref="OverheadImages"/>) : un acteur dont l'image arrive est redessiné ensuite.
    /// </summary>
    public partial class UserMapControl
    {
        private readonly ActorSprites sprites;
        private readonly OverheadImages overheadImages;
        private readonly BubbleLayer bubbles;
        /// <summary>Sprites absents (gfx → raison), pour <see cref="ArtworkStatus"/>.</summary>
        private readonly Dictionary<int, string> missingSprites = new Dictionary<int, string>();
        /// <summary>Acteurs qui ont bougé depuis leur entrée <c>GM</c> : l'émote qu'elle annonçait est terminée.</summary>
        private readonly HashSet<long> staleEntryEmotes = new HashSet<long>();
        private HashSet<short> pathPreview;
        private string hoveredKey;
        private bool viewAllMonsters = true, showMonstersTooltip;
        private int repaintQueued;

        public sealed class ActorVisualState
        {
            public int Id, CellId, GFX, Orientation, ScaleX, ScaleY;
            public string Name, SpriteReason;
            /// <summary>Pied du sprite dans le repère de la carte et à l'écran.</summary>
            public PointF WorldPosition, ScreenPosition;
            /// <summary>Cadre de l'image à l'écran (clics, survol) et dans le repère de la carte.</summary>
            public RectangleF SpriteBounds, WorldBounds;
            public bool IsMoving, IsVisible, IsSelf, HasSprite;
            /// <summary>Image retournée horizontalement (orientations 3, 4 et 7 sans « * »).</summary>
            public bool IsMirrored;
            /// <summary>Survolé (teinte de sélection et surtête).</summary>
            public bool IsHovered;
            /// <summary>Animation affichée : <c>static</c>, <c>walk</c>, <c>run</c>, <c>scene</c>, <c>emote&lt;n&gt;</c>, <c>hit</c>…</summary>
            public string Animation = "static";
            /// <summary>
            /// Nom complet de l'animation affichée, lettre d'orientation comprise (<c>staticF</c>, <c>hitR</c>, <c>scene</c>) :
            /// condition <c>xtraClipTopAnimations</c> des clips du dessus (lot AN1).
            /// </summary>
            public string AnimationName = "staticS";
            /// <summary>Fantôme : acteur sorti du modèle (mort) dessiné d'après son instantané, ni cliquable ni survolable.</summary>
            public bool IsGhost;
            /// <summary>Image de la bande affichée (0 pour une pose fixe).</summary>
            public int Frame;
            /// <summary>-1 pour le sprite principal, sinon rang du membre lié (groupe, suiveur) ou de l'équipe (épées).</summary>
            public int MemberIndex = -1;
            public long ActorId;
            public ActorKind Kind;
            /// <summary>Acteur à qui reviennent le clic et le survol : le groupe pour un membre, le combat pour une épée.</summary>
            public Entites Entity;
            /// <summary>Profondeur du client : cellule × 100 + 30 (les objets de la cellule sont à cellule × 100).</summary>
            public float Depth;
            /// <summary>Icône d'émote à afficher au-dessus de la tête (pas de bande <c>emote&lt;n&gt;</c> exportée), 0 sinon.</summary>
            public int EmoteIcon;
            internal SpritePose Pose;
            internal Color Color;
            internal bool NoFlip;
        }

        /// <summary>Option <c>ViewAllMonsterInGroup</c> du client (vraie par défaut) : faux, seul le chef d'un groupe est dessiné.</summary>
        public bool ViewAllMonsterInGroup
        {
            get => viewAllMonsters;
            set { viewAllMonsters = value; Invalidate(); }
        }

        /// <summary>Option <c>ChatEffects</c> du client (vraie par défaut) : bulles du chat au-dessus des acteurs.</summary>
        public bool ChatEffects
        {
            get => bubbles.ChatEffects;
            set { bubbles.ChatEffects = value; if (!value) { foreach (SpeechBubble bubble in bubbles.Bubbles) bubbles.RemoveBubble(bubble.ActorId); } Invalidate(); }
        }

        /// <summary>Comme le raccourci <c>SHOWMONSTERSTOOLTIP</c> maintenu : surtête de tous les groupes de monstres.</summary>
        public bool ShowMonstersTooltip
        {
            get => showMonstersTooltip;
            set { showMonstersTooltip = value; Invalidate(); }
        }

        /// <summary>La souris entre sur un acteur (ou le quitte : null). Fil de l'interface.</summary>
        public event Action<Entites> ActorHovered;

        public delegate void ActorClickedHandler(Entites actor, short cellId, MouseButtons buttons, Keys modifiers);
        /// <summary>Clic sur le sprite d'un acteur (au pixel près) ; sinon <see cref="CellClicked"/> est levé.</summary>
        public event ActorClickedHandler ActorClicked;

        /// <summary>Acteur survolé (racine : le groupe d'un membre), null sinon.</summary>
        public Entites HoveredActor => hoveredKey == null ? null : GetActorVisualStates().FirstOrDefault(state => RootKey(state) == hoveredKey)?.Entity;

        /// <summary>Cellules du chemin prévisualisé (couleur <c>CELL_PATH_OVER_COLOR</c> #FF6600), vide sans aperçu.</summary>
        public IReadOnlyCollection<short> PathPreview => pathPreview ?? (IReadOnlyCollection<short>)new short[0];

        /// <summary>Remplace l'aperçu du chemin (null ou vide : aucun).</summary>
        public void SetPathPreview(IEnumerable<short> cells)
        {
            HashSet<short> next = cells == null ? null : new HashSet<short>(cells);
            if (next != null && next.Count == 0) next = null;
            if ((pathPreview == null && next == null) || (pathPreview != null && next != null && pathPreview.SetEquals(next))) return;
            pathPreview = next;
            Invalidate();
        }

        /// <summary>Redessin demandé depuis n'importe quel fil (images lues, acteurs modifiés) ; regroupé en un seul.</summary>
        public void RequestRepaint()
        {
            if (IsDisposed || Disposing) return;
            if (!InvokeRequired) { Invalidate(); return; }
            if (System.Threading.Interlocked.Exchange(ref repaintQueued, 1) == 1) return;
            try
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => { repaintQueued = 0; if (!IsDisposed) Invalidate(); }));
                else repaintQueued = 0;
            }
            catch (InvalidOperationException) { repaintQueued = 0; } // poignée détruite entre-temps
        }

        private void AssetsArrived(object sender, EventArgs e) => RequestRepaint();

        /// <summary>
        /// Attend la lecture des images des acteurs visibles (tests, captures) ; faux si le délai expire. Ne pas appeler
        /// depuis le fil de l'interface d'une fenêtre affichée.
        /// </summary>
        public bool WaitForActorSprites(int milliseconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                GetActorVisualStates();
                int left = (int)Math.Max(0, milliseconds - clock.ElapsedMilliseconds);
                bool done = sprites.WaitForPending(left) && overheadImages.WaitForPending(Math.Max(0, (int)(milliseconds - clock.ElapsedMilliseconds)));
                // Une pose lue peut en demander une autre (bande absente → pose fixe) : on recommence jusqu'à stabilité.
                GetActorVisualStates();
                if (sprites.PendingCount == 0 && overheadImages.PendingCount == 0) return done;
                if (clock.ElapsedMilliseconds > milliseconds) return false;
            }
        }

        // ---------------------------------------------------------------- bulles, smileys, émotes

        /// <summary>
        /// Bulle au-dessus de l'acteur <paramref name="actorId"/> (en attendant le chat, ou pour un message local) ; refusée
        /// comme dans le client si l'acteur est absent, invisible ou en mouvement, ou si l'option <see cref="ChatEffects"/> est
        /// désactivée. La bulle reste où était l'acteur au moment de l'ajout.
        /// </summary>
        public bool ShowBubble(long actorId, string text, BubbleKind kind = BubbleKind.Chat)
        {
            ActorVisualState state = MainState(actorId);
            if (state == null || !state.IsVisible || state.IsMoving) return false;
            if (bubbles.Show(actorId, text, kind, state.WorldPosition) == null) return false;
            StartTimer(); Invalidate();
            return true;
        }

        /// <summary>Bulle d'un message du chat (canal par défaut seulement, voir <see cref="BubbleLayer.BubbleText"/>).</summary>
        public bool ShowChatMessage(ChatMessage message)
        {
            string text = BubbleLayer.BubbleText(message, out BubbleKind kind);
            return text != null && ShowBubble(message.AuthorId.Value, text, kind);
        }

        /// <summary>Smiley <c>cS</c> au-dessus de la tête pendant 3 s.</summary>
        public void ShowSmiley(long actorId, int smiley)
        {
            bubbles.ShowSmiley(actorId, smiley);
            StartTimer(); Invalidate();
        }

        /// <summary>
        /// Émote <c>eUK</c> : bande <c>emote&lt;n&gt;</c> si elle existe, sinon icône de l'attitude au-dessus de la tête,
        /// gardée tant que l'acteur reste assis (1, 19, 20), 3 s sinon ; 0 revient à la pose fixe.
        /// </summary>
        public void ShowEmote(long actorId, int emote)
        {
            staleEntryEmotes.Add(actorId);
            bubbles.ShowEmote(actorId, emote, ChatService.IsSittingEmote(emote));
            StartTimer(); Invalidate();
        }

        /// <summary>Oublie les bulles, smileys et émotes de l'acteur (retiré de la carte).</summary>
        public void ForgetActor(long actorId)
        {
            bubbles.Remove(actorId);
            Invalidate();
        }

        /// <summary>Oublie toutes les bulles, smileys et émotes (acteurs retirés en bloc : changement de carte, <c>GA;2</c>).</summary>
        public void ClearActorOverlays()
        {
            bubbles.Clear(); staleEntryEmotes.Clear();
            Invalidate();
        }

        /// <summary>
        /// Profil de vitesse du client pour l'acteur <paramref name="id"/> du compte : celui du personnage (restrictions et
        /// monture) ou <see cref="MoveProfile.ForActor"/>. Lecture seule : utilisable depuis le fil réseau.
        /// </summary>
        public static MoveProfile MovementProfile(Tool_BotProtocol.Game.Accounts.Accounts account, long id)
        {
            Map map = account?.Game?.Map;
            bool fight = account?.Game?.Fight?.IsInFight == true;
            CharacterClass character = account?.Game?.character;
            if (character != null && character.id == id)
            {
                var self = map?.Self as PlayerActor;
                return MoveProfile.Player(self?.RestrictionsRaw, character.UseMount || self?.HasMount == true, fight);
            }
            return MoveProfile.ForActor(map?.GetActor(id), fight);
        }

        /// <summary>Bulle encore affichée pour l'acteur (tests, diagnostic).</summary>
        public SpeechBubble BubbleOf(long actorId) => bubbles.BubbleOf(actorId);

        /// <summary>Horloge des animations et des bulles (ms) : celle injectée au constructeur.</summary>
        public double Now => animationClock();

        private ActorVisualState MainState(long actorId) =>
            GetActorVisualStates().FirstOrDefault(state => state.ActorId == actorId && state.MemberIndex < 0 && !state.IsGhost && !(state.Entity is FightSwordsActor));

        // ---------------------------------------------------------------- états visuels

        private PointF WorldCenter(int cellId)
        {
            if (worldPolygons == null || cellId < 0 || cellId >= worldPolygons.Length) return PointF.Empty;
            PointF[] polygon = worldPolygons[cellId];
            return new PointF((polygon[0].X + polygon[2].X) / 2, (polygon[1].Y + polygon[3].Y) / 2);
        }

        private PointF ToScreen(PointF world) => new PointF(world.X * viewScale + origin.X, world.Y * viewScale + origin.Y);

        private RectangleF ToScreen(RectangleF world) =>
            new RectangleF(world.X * viewScale + origin.X, world.Y * viewScale + origin.Y, world.Width * viewScale, world.Height * viewScale);

        public ActorVisualState GetActorVisualState(int id) => GetActorVisualStates().FirstOrDefault(actor => actor.Id == id);

        /// <summary>États visuels dans l'ordre du dessin (du fond vers l'avant). Ne lit jamais le disque.</summary>
        public ActorVisualState[] GetActorVisualStates()
        {
            Map map = Account?.Game?.Map;
            // Copie locale : le fil réseau remet MapCells à null (Map.Clear) à chaque GDM pendant que la carte se dessine.
            Cell[] mapCells = map?.MapCells;
            if (mapCells == null || worldPolygons == null) return new ActorVisualState[0];
            var states = new List<ActorVisualState>();
            var live = new HashSet<int>();
            CharacterClass self = Account.Game.character;
            if (self?.Cell != null)
            {
                live.Add(self.id);
                MapActor selfActor = map.Self;
                ActorVisualState main = AddMain(states, self, self.id, ActorKind.Player, self.GFX > 0 ? self.GFX : self.Race_ID * 10 + self.Sex,
                    self.Orientation, selfActor?.NoFlip == true, self.GraphicsScaleX, self.GraphicsScaleY, self.Cell.CellID, true,
                    Color.FromArgb(72, 103, 156), self.Name ?? "Vous", PlayerEmote(self.id, selfActor as PlayerActor));
                if (main != null && selfActor != null) AddFollowers(states, main, selfActor, map, mapCells);
            }
            foreach (MapActor actor in map.AllActors.ToArray())
            {
                if (actor == null || actor.IsSelf) continue;
                int cell = actor.Cell?.CellID ?? actor.CellId;
                if (cell < 0 || cell >= worldPolygons.Length) continue;
                live.Add(actor.id);
                if (actor is MonsterGroupActor group) { AddGroup(states, group, cell, map, mapCells); continue; }
                ActorVisualState main = AddMain(states, actor, actor.Id, actor.Kind, actor.Gfx, actor.Orientation, actor.NoFlip, actor.ScaleX, actor.ScaleY,
                    cell, false, KindColor(actor.Kind), actor.DisplayName, PlayerEmote(actor.Id, actor as PlayerActor));
                if (main != null) AddFollowers(states, main, actor, map, mapCells);
            }
            foreach (FightSwordsActor swords in map.FightSwords.Values.ToArray()) AddSwords(states, swords);
            AddGhosts(states, live);
            foreach (int id in Anim.Keys.Where(id => !live.Contains(id)).ToArray()) CancelAnimation(id);
            ActorVisualState[] ordered = states.Select((state, index) => new { state, index }).OrderBy(item => item.state.Depth)
                .ThenBy(item => item.index).Select(item => item.state).ToArray();
            foreach (ActorVisualState state in ordered)
                state.IsHovered = state.MemberIndex < 0 && !state.IsGhost && hoveredKey != null && RootKey(state) == hoveredKey;
            return ordered;
        }

        /// <summary>
        /// Fantômes de <see cref="ActorAnimationQueue"/> : acteurs sortis du modèle, dessinés d'après leur instantané sur leur
        /// dernière cellule (animation sans boucle, dernière image tenue), tant qu'ils ne sont pas revenus sur la carte.
        /// </summary>
        private void AddGhosts(List<ActorVisualState> states, HashSet<int> live)
        {
            if (animationQueue.GhostCount == 0) return;
            double now = animationClock();
            foreach (ActorAnimationQueue.Ghost ghost in animationQueue.GhostsAt(now))
            {
                Tool_BotProtocol.Game.Combats.ActorSnapshot snapshot = ghost.Snapshot;
                if (live.Contains(unchecked((int)snapshot.Id)) || snapshot.CellId < 0 || snapshot.CellId >= worldPolygons.Length) continue;
                ActorVisualState state = NewState(null, snapshot.Id, snapshot.Kind, snapshot.Gfx, snapshot.Direction, snapshot.NoFlip,
                    snapshot.ScaleX, snapshot.ScaleY, snapshot.CellId, WorldCenter(snapshot.CellId), snapshot.IsSelf, KindColor(snapshot.Kind), snapshot.Name, -1);
                state.IsGhost = true;
                SpritePose requested = Resolve(state, ghost.Animation, ghost.Elapsed(now), false);
                if (!ghost.Shown.HasValue && requested.State == SpriteLoadState.Ready) ghost.Shown = now;
                states.Add(state);
            }
        }

        private static Color KindColor(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Npc: return Color.FromArgb(154, 106, 172);
                case ActorKind.MonsterGroup: case ActorKind.FightMonster: return Color.FromArgb(156, 63, 48);
                case ActorKind.Merchant: case ActorKind.Collector: return Color.FromArgb(150, 118, 52);
                default: return Color.FromArgb(85, 103, 143);
            }
        }

        private int PlayerEmote(long actorId, PlayerActor player)
        {
            int? shown = bubbles.EmoteOf(actorId);
            if (shown.HasValue) return shown.Value;
            if (player == null || staleEntryEmotes.Contains(actorId)) return 0;
            // Attitude déjà prise à l'arrivée sur la carte (champ émote de GM, « 1 » pour assis), jusqu'au premier déplacement.
            return int.TryParse(player.Emote, NumberStyles.Integer, CultureInfo.InvariantCulture, out int emote) && emote > 0 ? emote : 0;
        }

        private ActorVisualState AddMain(List<ActorVisualState> states, Entites entity, long actorId, ActorKind kind, int gfx, int orientation,
            bool noFlip, int scaleX, int scaleY, int cell, bool self, Color color, string name, int emote)
        {
            if (cell < 0 || cell >= worldPolygons.Length) return null;
            PointF position = Animate(unchecked((int)actorId), cell, self, ref orientation, out int shownCell, out bool moving, out double elapsed, out MoveMode? mode);
            if (moving && emote > 0) { bubbles.RemoveEmote(actorId); staleEntryEmotes.Add(actorId); emote = 0; }
            double now = animationClock();
            string animation;
            bool loop = true, overriding = false;
            if (moving)
            {
                // Comme moveSprite du client : le déplacement remplace l'animation ponctuelle en cours.
                animation = mode == MoveMode.Walk ? "walk" : "run";
                animationQueue.Cancel(actorId);
            }
            else if (animationQueue.Override(actorId, now, out string single, out double singleElapsed, out bool singleLoop))
            {
                // Animation ponctuelle (hit, anim<n>…) : temps écoulé depuis sa première image, pas l'horloge globale.
                animation = single; elapsed = singleElapsed; loop = singleLoop; overriding = true;
            }
            else
            {
                animation = emote > 0 ? "emote" + emote.ToString(CultureInfo.InvariantCulture) : "static";
                elapsed = now;
            }
            ActorVisualState state = NewState(entity, actorId, kind, gfx, orientation, noFlip, scaleX, scaleY, shownCell, position, self, color, name, -1);
            state.IsMoving = moving;
            SpritePose requested = Resolve(state, animation, elapsed, loop);
            if (overriding) animationQueue.Observe(actorId, animation, requested.State, requested.Sheet, now);
            if (emote > 0 && !moving && !(state.HasSprite && state.Animation.StartsWith("emote", StringComparison.Ordinal))) state.EmoteIcon = emote;
            states.Add(state);
            return state;
        }

        private ActorVisualState NewState(Entites entity, long actorId, ActorKind kind, int gfx, int orientation, bool noFlip, int scaleX, int scaleY,
            int cell, PointF position, bool self, Color color, string name, int member)
        {
            return new ActorVisualState
            {
                Id = unchecked((int)actorId), ActorId = actorId, Kind = kind, Entity = entity, CellId = cell, GFX = gfx,
                Orientation = ActorOrientation.Normalize(orientation), NoFlip = noFlip, Name = name,
                ScaleX = Math.Max(0, scaleX), ScaleY = Math.Max(0, scaleY), WorldPosition = position, ScreenPosition = ToScreen(position),
                IsSelf = self, IsVisible = scaleX > 0 && scaleY > 0, Color = color, MemberIndex = member, Depth = cell * 100f + 30f
            };
        }

        /// <summary>Position animée de l'acteur : le long de son chemin, sinon au centre de sa cellule.</summary>
        private PointF Animate(int id, int cell, bool self, ref int direction, out int shownCell, out bool moving, out double elapsed, out MoveMode? mode)
        {
            shownCell = cell; moving = false; elapsed = 0; mode = null;
            PointF position = WorldCenter(cell);
            if (!Anim.TryGetValue(id, out Animations movement) || !movement.TrySample(animationClock(), out Animations.Frame frame)) return position;
            bool relocated = cell != movement.StartCellId && cell != movement.EndCellId;
            bool acknowledged = frame.Complete && cell == movement.EndCellId;
            bool rejected = frame.Complete && self && !Account.IsMoving() && cell != movement.EndCellId;
            if (relocated || acknowledged || rejected) { CancelAnimation(id); return position; }
            shownCell = frame.CellId; direction = frame.Direction; moving = !frame.Complete; elapsed = frame.Elapsed; mode = movement.Mode;
            return frame.Position;
        }

        /// <summary>
        /// Pose de l'état : l'animation demandée si sa bande est prête, sinon la pose fixe (ou la scène pour les épées) ;
        /// silhouette (<see cref="ActorVisualState.HasSprite"/> faux) tant que l'image est en lecture ou si elle manque.
        /// <paramref name="loop"/> faux : passe unique, dernière image tenue. Rend la pose de l'animation demandée (prête,
        /// en lecture ou absente), pour <see cref="ActorAnimationQueue.Observe"/>.
        /// </summary>
        private SpritePose Resolve(ActorVisualState state, string animation, double elapsed, bool loop = true)
        {
            state.Animation = animation;
            if (!state.IsVisible)
            {
                state.SpriteReason = "Personnage masqué par le serveur (taille nulle).";
                return new SpritePose(null, false, SpriteLoadState.Missing, state.SpriteReason, animation);
            }
            SpritePose requested = null, pose = null;
            if (animation != "static" && animation != "scene")
            {
                requested = sprites.Resolve(state.GFX, state.Orientation, state.NoFlip, animation);
                if (requested.State == SpriteLoadState.Ready) pose = requested;
            }
            if (pose == null)
            {
                pose = sprites.Resolve(state.GFX, state.Orientation, state.NoFlip, animation == "scene" ? "scene" : "static");
                loop = true;
            }
            requested = requested ?? pose;
            state.Pose = pose;
            state.Animation = pose.Animation;
            state.AnimationName = pose.FullName;
            state.IsMirrored = pose.Mirrored;
            state.HasSprite = pose.State == SpriteLoadState.Ready && pose.Sheet != null;
            state.SpriteReason = pose.State == SpriteLoadState.Loading
                ? "Sprite " + state.GFX.ToString(CultureInfo.InvariantCulture) + " en cours de lecture." : pose.Reason;
            if (pose.State == SpriteLoadState.Missing) RecordMissing(state.GFX, pose.Reason);
            float sx = state.ScaleX / 100f, sy = state.ScaleY / 100f;
            if (state.HasSprite)
            {
                SpriteSheet sheet = pose.Sheet;
                // Cadence de la bande (colonne ips, 40 par défaut) ; image bloquée sur la dernière hors boucle.
                state.Frame = sheet.FrameAt(elapsed, loop);
                state.WorldBounds = sheet.Destination(state.WorldPosition, sx, sy, state.IsMirrored);
            }
            else state.WorldBounds = new RectangleF(state.WorldPosition.X - 9 * sx, state.WorldPosition.Y - 30 * sy, 18 * sx, 30 * sy);
            state.SpriteBounds = ToScreen(state.WorldBounds);
            return requested;
        }

        private void RecordMissing(int gfx, string reason)
        {
            if (missingSprites.ContainsKey(gfx)) return;
            missingSprites[gfx] = reason ?? "Sprite " + gfx.ToString(CultureInfo.InvariantCulture) + " absent.";
            DisplayStateChanged?.Invoke();
        }

        // ---------------------------------------------------------------- groupes, suiveurs, épées

        /// <summary>
        /// Groupe de monstres : le chef sur la cellule du groupe, puis (option <see cref="ViewAllMonsterInGroup"/>) chaque
        /// membre sur une cellule voisine d'un membre déjà placé, comme <c>createMonsterGroup</c> + <c>addLinkedSprite</c>
        /// du client : parent = chef, ou au hasard un membre précédent (2 fois sur 3 à partir du 2ᵉ), case voisine tirée
        /// parmi 8, orientation impaire tirée au hasard. Le hasard est fixé par l'identifiant du groupe (placement stable
        /// d'un dessin à l'autre). Les membres suivent le chef quand il marche.
        /// </summary>
        private void AddGroup(List<ActorVisualState> states, MonsterGroupActor group, int cell, Map map, Cell[] mapCells)
        {
            IReadOnlyList<MonsterGroupMember> members = group.Members ?? new MonsterGroupMember[0];
            MonsterGroupMember leader = members.Count > 0 ? members[0] : null;
            int leaderGfx = group.Gfx > 0 ? group.Gfx : MemberGfx(leader);
            ActorVisualState main = AddMain(states, group, group.Id, ActorKind.MonsterGroup, leaderGfx, group.Orientation, group.NoFlip,
                group.ScaleX, group.ScaleY, cell, false, KindColor(ActorKind.MonsterGroup), group.DisplayName, 0);
            if (main == null || !viewAllMonsters || members.Count < 2) return;
            var random = new Random(unchecked((int)(group.Id * 7919 + 17)));
            // Cellule logique du chef (destination du pas en cours quand il marche) : les membres s'y rattachent.
            int anchorCell = main.CellId;
            var cells = new List<int> { anchorCell };
            var directions = new List<int> { ActorOrientation.Normalize(group.Orientation) };
            PointF anchorWorld = WorldCenter(anchorCell);
            for (int index = 1; index < members.Count; index++)
            {
                MonsterGroupMember member = members[index];
                int direction = random.Next(4) * 2 + 1;
                int parent = 0;
                if (random.Next(3) != 0 && index != 1) parent = random.Next(index - 1) + 1;
                int childIndex = random.Next(8);
                int target = AroundCell(map.MapWidth, mapCells, cells[parent], directions[parent], childIndex);
                Cell data = target >= 0 && target < mapCells.Length ? mapCells[target] : null;
                if (data == null || !data.IsWalkable()) target = cells[parent];
                cells.Add(target); directions.Add(direction);
                PointF offset = WorldCenter(target);
                var position = new PointF(main.WorldPosition.X + offset.X - anchorWorld.X, main.WorldPosition.Y + offset.Y - anchorWorld.Y);
                int shown = main.IsMoving ? main.Orientation : direction;
                ActorVisualState state = NewState(group, group.Id, ActorKind.MonsterGroup, MemberGfx(member), shown, group.NoFlip,
                    member.ScaleX, member.ScaleY, target, position, false, main.Color, member.Name, index);
                state.IsMoving = main.IsMoving;
                Resolve(state, main.IsMoving ? main.Animation : "static", animationClock());
                states.Add(state);
            }
        }

        private static int MemberGfx(MonsterGroupMember member)
        {
            if (member == null) return 0;
            if (member.Gfx > 0) return member.Gfx;
            return Monstres.ReturnMonsters(member.TemplateId)?.GFX ?? LangData.Monster.Gfx(member.TemplateId) ?? 0;
        }

        /// <summary>
        /// <c>getArroundCellNum</c> du client : le rang <paramref name="childIndex"/> (modulo 8) choisit une des 8 cases
        /// voisines, tournée de l'orientation du parent ; la case doit exister, être active et à moins de 53 px en x.
        /// </summary>
        internal int AroundCell(int width, Cell[] mapCells, int cell, int parentDirection, int childIndex)
        {
            int[] offsets = { 1, width, width * 2 - 1, width - 1, -1, -width, -width * 2 + 1, -(width - 1) };
            int[] slots = { 2, 6, 4, 0, 3, 5, 1, 7 };
            int slot = (slots[((childIndex % 8) + 8) % 8] + ActorOrientation.Normalize(parentDirection)) % 8;
            int target = cell + offsets[slot];
            if (target < 0 || target >= worldPolygons.Length || mapCells == null || target >= mapCells.Length) return cell;
            bool active = artwork != null && artwork.Cells.Length == worldPolygons.Length ? artwork.Cells[target].Active : mapCells[target]?.IsActive == true;
            return active && Math.Abs(WorldCenter(target).X - WorldCenter(cell).X) <= BotMapArtwork.CellWidth ? target : cell;
        }

        /// <summary>
        /// Sprites liés d'un acteur qui n'est pas un groupe (familier qui suit un joueur) : disposition <c>circle</c>
        /// (rang k autour du parent), <c>line</c> (chaque sprite suit le précédent) ou par défaut rang 2 autour du parent.
        /// </summary>
        private void AddFollowers(List<ActorVisualState> states, ActorVisualState main, MapActor actor, Map map, Cell[] mapCells)
        {
            IReadOnlyList<GfxPart> parts = actor.LinkedSprites;
            if (parts == null || parts.Count == 0 || actor is MonsterGroupActor) return;
            int parentCell = main.CellId, parentDirection = main.Orientation;
            PointF anchorWorld = WorldCenter(main.CellId);
            for (int index = 0; index < parts.Count; index++)
            {
                GfxPart part = parts[index];
                if (part == null || part.Gfx <= 0) continue;
                int childIndex = actor.LinkedShape == "circle" ? index + 1 : 2;
                int target = AroundCell(map.MapWidth, mapCells, parentCell, parentDirection, childIndex);
                Cell data = target >= 0 && target < mapCells.Length ? mapCells[target] : null;
                if (data == null || !data.IsWalkable()) target = parentCell;
                PointF offset = WorldCenter(target);
                var position = new PointF(main.WorldPosition.X + offset.X - anchorWorld.X, main.WorldPosition.Y + offset.Y - anchorWorld.Y);
                ActorVisualState state = NewState(main.Entity, main.ActorId, main.Kind, part.Gfx, main.Orientation, false, part.ScaleX, part.ScaleY,
                    target, position, main.IsSelf, main.Color, main.Name, index + 1);
                state.IsMoving = main.IsMoving;
                Resolve(state, main.IsMoving ? main.Animation : "static", animationClock());
                states.Add(state);
                if (actor.LinkedShape == "line") { parentCell = target; anchorWorld = offset; }
            }
        }

        /// <summary>Une épée par équipe (<c>Gc+</c>), image de scène <c>sprites/&lt;n&gt;_scene.png</c> choisie comme <c>getTeamFileFromType</c>.</summary>
        private void AddSwords(List<ActorVisualState> states, FightSwordsActor swords)
        {
            for (int index = 0; index < swords.Teams.Count; index++)
            {
                FightTeamFlag team = swords.Teams[index];
                if (team == null || team.CellId < 0 || team.CellId >= worldPolygons.Length) continue;
                ActorVisualState state = NewState(swords, swords.Id, ActorKind.FightSwords, SwordGfx(team.TeamType, team.Alignment), 0, true, 100, 100,
                    team.CellId, WorldCenter(team.CellId), false, Color.FromArgb(120, 98, 60), swords.DisplayName, index);
                Resolve(state, "scene", 0);
                states.Add(state);
            }
        }

        /// <summary>Numéro du SWF d'épée : 1 Bonta, 2 Brâkmar, sinon 0 (joueurs), 3 (monstres) ou 4 (percepteur).</summary>
        public static int SwordGfx(int teamType, int alignment)
        {
            switch (teamType)
            {
                case 0: return alignment == 1 ? 1 : alignment == 2 ? 2 : 0;
                case 1: return alignment == 1 ? 1 : alignment == 2 ? 2 : 3;
                case 3: return 4;
                default: return 0;
            }
        }

        private static string RootKey(ActorVisualState state) =>
            (state.Entity is FightSwordsActor ? "fight:" : "actor:") + state.ActorId.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- survol et clic au pixel près

        /// <summary>
        /// Acteur sous le point <paramref name="point"/> (écran), du premier plan vers le fond : dans le cadre du sprite et
        /// sur un pixel visible de l'image (silhouette : son cadre). Null si aucun.
        /// </summary>
        public ActorVisualState HitTest(Point point)
        {
            ActorVisualState[] states = GetActorVisualStates();
            for (int index = states.Length - 1; index >= 0; index--)
            {
                ActorVisualState state = states[index];
                if (state.IsGhost || !state.IsVisible || !state.SpriteBounds.Contains(point)) continue;
                if (!state.HasSprite) return state;
                SpriteSheet sheet = state.Pose.Sheet;
                float sx = state.ScaleX / 100f * viewScale, sy = state.ScaleY / 100f * viewScale;
                if (sx <= 0 || sy <= 0) continue;
                int x = (int)Math.Floor((point.X - state.SpriteBounds.Left) / sx), y = (int)Math.Floor((point.Y - state.SpriteBounds.Top) / sy);
                if (state.IsMirrored) x = sheet.FrameWidth - 1 - x;
                if (sheet.IsOpaque(state.Frame, x, y)) return state;
            }
            return null;
        }

        private void UpdateHover(ActorVisualState hit)
        {
            string key = hit == null ? null : RootKey(hit);
            if (key == hoveredKey) return;
            hoveredKey = key;
            Invalidate();
            ActorHovered?.Invoke(hit?.Entity);
        }

        // ---------------------------------------------------------------- dessin

        private void DrawActor(Graphics graphics, ActorVisualState actor)
        {
            if (!actor.IsVisible) return;
            PointF foot = actor.WorldPosition;
            if (actor.HasSprite)
            {
                // Image posée en pixels du PNG dans un repère translaté et mis à l'échelle (retourné pour le miroir), case de
                // la bande (ligne par ligne pour une grille), bords répétés en miroir : pas de liseré pris sur l'image voisine.
                using (ImageAttributes attributes = actor.IsHovered ? OverheadLayer.SelectionTint() : new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    SpritePainter.Draw(graphics, actor.Pose.Sheet, actor.Frame, actor.WorldBounds, actor.IsMirrored, attributes);
                }
                return;
            }
            if (actor.Pose?.State == SpriteLoadState.Loading) return; // l'image arrive : rien plutôt qu'un repère éphémère
            float sx = actor.ScaleX / 100f, sy = actor.ScaleY / 100f;
            using (var shadow = new SolidBrush(Color.FromArgb(70, 35, 32, 23))) graphics.FillEllipse(shadow, foot.X - 12 * sx, foot.Y - 4 * sy, 24 * sx, 8 * sy);
            Color body = actor.IsHovered ? ControlPaint.Light(actor.Color) : actor.Color;
            using (var brush = new SolidBrush(body))
            {
                graphics.FillEllipse(brush, foot.X - 6 * sx, foot.Y - 28 * sy, 12 * sx, 12 * sy);
                graphics.FillPolygon(brush, new[] { new PointF(foot.X, foot.Y - 18 * sy), new PointF(foot.X - 9 * sx, foot.Y - 3 * sy), new PointF(foot.X + 9 * sx, foot.Y - 3 * sy) });
            }
            using (var pen = new Pen(Color.FromArgb(222, 153, 49), 2))
            {
                graphics.DrawLine(pen, foot.X - 4 * sx, foot.Y - 18 * sy, foot.X + 4 * sx, foot.Y - 10 * sy);
                graphics.DrawLine(pen, foot.X + 4 * sx, foot.Y - 18 * sy, foot.X - 4 * sx, foot.Y - 10 * sy);
            }
        }

        private void DrawPathPreview(Graphics graphics)
        {
            if (pathPreview == null || worldPolygons == null) return;
            using (var fill = new SolidBrush(Color.FromArgb(110, 0xFF, 0x66, 0x00)))
            using (var pen = new Pen(Color.FromArgb(200, 0xFF, 0x66, 0x00), 1 / Math.Max(.01f, viewScale)))
                foreach (short cell in pathPreview)
                {
                    if (cell < 0 || cell >= worldPolygons.Length) continue;
                    graphics.FillPolygon(fill, worldPolygons[cell]);
                    graphics.DrawPolygon(pen, worldPolygons[cell]);
                }
        }

        /// <summary>Surtêtes (survol, raccourci des groupes), smileys et icônes d'émote, puis bulles, dans le repère de l'écran.</summary>
        private void DrawOverheads(Graphics graphics, ActorVisualState[] actors)
        {
            bool inFight = Account?.Game?.Fight?.IsInFight == true;
            bool running = inFight && Account.Game.Fight.IsPlacement == false;
            Rectangle view = ClientRectangle;
            foreach (ActorVisualState actor in actors)
            {
                if (actor.MemberIndex >= 0 || !actor.IsVisible || actor.IsGhost) continue;
                bool showText = actor.IsHovered || (showMonstersTooltip && actor.Kind == ActorKind.MonsterGroup);
                int? smiley = bubbles.SmileyOf(actor.ActorId);
                int emote = actor.EmoteIcon;
                if (!showText && !smiley.HasValue && emote <= 0) continue;
                OverheadContent content = null;
                if (showText)
                {
                    Entites subject = actor.IsSelf && Account.Game.Map.Self is PlayerActor selfActor ? selfActor : actor.Entity;
                    content = OverheadLayer.Describe(subject, inFight, running, Account?.Game?.Map?.MapID ?? 0);
                }
                Size text = content == null ? Size.Empty : OverheadLayer.Measure(graphics, content);
                int icons = (smiley.HasValue ? 1 : 0) + (emote > 0 ? 1 : 0);
                var stack = new Size(Math.Max(text.Width, icons > 0 ? BubbleLayer.IconSize : 0), text.Height + icons * BubbleLayer.IconSize);
                float height = actor.HasSprite ? actor.Pose.Sheet.FrameHeight * actor.ScaleY / 100f : 30;
                Point corner = OverheadLayer.Place(actor.ScreenPosition, height, stack, viewScale, view);
                int bottom = corner.Y + stack.Height;
                if (content != null)
                {
                    var box = new Rectangle(corner.X + (stack.Width - text.Width) / 2, bottom - text.Height, text.Width, text.Height);
                    OverheadLayer.Draw(graphics, content, box, overheadImages);
                    bottom = box.Top;
                }
                int centerX = corner.X + stack.Width / 2;
                if (smiley.HasValue)
                {
                    DrawIcon(graphics, "Smileys/" + smiley.Value.ToString(CultureInfo.InvariantCulture) + ".png", new Rectangle(centerX - BubbleLayer.IconSize / 2, bottom - BubbleLayer.IconSize, BubbleLayer.IconSize, BubbleLayer.IconSize));
                    bottom -= BubbleLayer.IconSize;
                }
                if (emote > 0)
                    DrawIcon(graphics, "Emotes/" + emote.ToString(CultureInfo.InvariantCulture) + ".png", new Rectangle(centerX - BubbleLayer.IconSize / 2, bottom - BubbleLayer.IconSize, BubbleLayer.IconSize, BubbleLayer.IconSize));
            }
            foreach (SpeechBubble bubble in bubbles.Bubbles)
                BubbleLayer.Draw(graphics, bubble, ToScreen(bubble.WorldFoot), viewScale, view);
        }

        private void DrawIcon(Graphics graphics, string file, Rectangle box)
        {
            Bitmap image = overheadImages.File(file);
            if (image != null) { graphics.DrawImage(image, box); return; }
            // Image en lecture ou absente : pastille neutre à sa place.
            using (var brush = new SolidBrush(Color.FromArgb(150, 235, 227, 203))) graphics.FillEllipse(brush, box);
            using (var pen = new Pen(Color.FromArgb(180, 41, 38, 31))) graphics.DrawEllipse(pen, box);
        }
    }
}
