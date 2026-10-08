using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Couche d'un effet de la carte : <see cref="Ground"/> après les cellules, <see cref="Depth"/> intercalée avec le décor
    /// et les acteurs (<see cref="BotMapArtwork.DepthLayer"/> : <c>Order</c> 2 derrière l'acteur de la cellule, 4 devant),
    /// <see cref="Screen"/> après les surtêtes, dans le repère de l'écran (chiffres, bulles « Échec critique »).
    /// </summary>
    public enum EffectLayer { Ground, Depth, Screen }

    /// <summary>
    /// Effet dessiné sur la carte (lot AN1), possédé par <see cref="MapEffectSet"/> : <see cref="Update"/> à chaque tick de
    /// la minuterie commune (faux : terminé, l'effet est retiré et libéré s'il est <see cref="IDisposable"/>), puis
    /// <see cref="Draw"/> dans le repère de la carte (couches Ground et Depth) ou de l'écran (Screen). Fil de l'interface.
    /// </summary>
    public interface IMapEffect
    {
        EffectLayer Layer { get; }
        /// <summary>Profondeur du client (cellule × 100 + 30 pour l'acteur de la cellule).</summary>
        int Depth { get; }
        /// <summary>Rang à profondeur égale : 3 pour les acteurs, 2 derrière, 4 devant.</summary>
        int Order { get; }
        bool Update(double now, MapView view);
        void Draw(Graphics graphics, MapView view);
    }

    /// <summary>Position et pose d'un acteur dessiné (ou d'un fantôme), pour accrocher un effet.</summary>
    public struct ActorAnchor
    {
        public long ActorId;
        public int CellId;
        /// <summary>Pied du sprite dans le repère de la carte.</summary>
        public PointF WorldFoot;
        /// <summary>Cadre de l'image dans le repère de la carte.</summary>
        public RectangleF WorldBounds;
        public float Depth;
        /// <summary>Animation de base (<c>static</c>, <c>walk</c>, <c>hit</c>…).</summary>
        public string BaseAnimation;
        /// <summary>Nom complet de l'animation affichée (<c>staticF</c>, <c>hitR</c>…).</summary>
        public string Animation;
        public ActorKind Kind;
        public int ScaleX, ScaleY;
        public bool IsMirrored, IsVisible, IsMoving, IsGhost, HasSprite;
    }

    /// <summary>Accès d'un effet à la vue : horloge, échelle, repères, sprites, cellules et acteurs dessinés.</summary>
    public sealed class MapView
    {
        private readonly UserMapControl owner;

        internal MapView(UserMapControl owner) { this.owner = owner; }

        public double Now => owner.Now;
        /// <summary>Échelle de la carte vers l'écran.</summary>
        public float Scale => owner.ViewScale;
        /// <summary>Position à l'écran de l'origine de la carte.</summary>
        public PointF Origin => owner.ViewOrigin;
        public ActorSprites Sprites => owner.Sprites;

        public PointF ToScreen(PointF world) => new PointF(world.X * Scale + Origin.X, world.Y * Scale + Origin.Y);

        public RectangleF ToScreen(RectangleF world) =>
            new RectangleF(world.X * Scale + Origin.X, world.Y * Scale + Origin.Y, world.Width * Scale, world.Height * Scale);

        /// <summary>Centre de la cellule dans le repère de la carte ; faux si la cellule n'existe pas.</summary>
        public bool TryGetCellCenter(int cellId, out PointF world) => owner.TryGetCellCenter(cellId, out world);

        /// <summary>Acteur (ou fantôme) tel qu'il a été dessiné au dernier passage ; faux s'il n'est pas sur la carte.</summary>
        public bool TryGetActorAnchor(long actorId, out ActorAnchor anchor) => owner.TryGetActorAnchor(actorId, out anchor);
    }

    /// <summary>Lecture d'une bande d'effet : une passe, dernière image tenue, ou en boucle.</summary>
    public enum StripPlay { Once, Hold, Loop }

    /// <summary>Dessin d'une image de bande (repère translaté et mis à l'échelle, retourné pour le miroir).</summary>
    internal static class SpritePainter
    {
        public static void Draw(Graphics graphics, SpriteSheet sheet, int frame, RectangleF target, bool mirrored, ImageAttributes attributes)
        {
            if (sheet?.Image == null || target.Width <= 0 || target.Height <= 0) return;
            Rectangle source = sheet.Source(frame);
            GraphicsState saved = graphics.Save();
            try
            {
                // libgdiplus ignore la transformation de la vue avec DrawImage(PointF[]…), pas avec un rectangle.
                graphics.TranslateTransform(mirrored ? target.Right : target.Left, target.Top);
                graphics.ScaleTransform((mirrored ? -1 : 1) * target.Width / source.Width, target.Height / source.Height);
                graphics.DrawImage(sheet.Image, new Rectangle(0, 0, source.Width, source.Height), source.X, source.Y, source.Width, source.Height,
                    GraphicsUnit.Pixel, attributes);
            }
            finally { graphics.Restore(saved); }
        }

        /// <summary>Attributs réutilisables : bords répétés en miroir, sans liseré pris sur l'image voisine de la bande.</summary>
        public static ImageAttributes EdgeSafe()
        {
            var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            return attributes;
        }
    }

    /// <summary>
    /// Bande d'effet posée à une position de la carte (<see cref="ActorSprites.ResolveFixed"/>), sans orientation. Tant que
    /// la bande est en lecture, l'effet attend au plus 300 ms, puis il est sauté (<see cref="Skipped"/>). Lecture selon
    /// <see cref="StripPlay"/> (par défaut d'après la colonne <c>fin</c> : <c>arret</c> tient la dernière image,
    /// <c>boucle</c> boucle, sinon une passe), jamais plus de 20 s (<c>VISUAL_EFFECT_MAX_TIMER</c> du client).
    /// </summary>
    public sealed class StripEffect : IMapEffect, IDisposable
    {
        public const double MaxLoadWait = 300;
        public const double MaxDisplay = 20000;

        private readonly ActorSprites.FixedStrip strip;
        private readonly StripPlay? play;
        private readonly double? duration;
        private readonly double requested;
        private ImageAttributes attributes;
        private SpriteSheet sheet;
        private int frame;

        /// <param name="strip">Bande tenue par l'effet (libérée à sa fin).</param>
        /// <param name="world">Ancre de la bande dans le repère de la carte (repère de l'écran pour la couche Screen).</param>
        /// <param name="durationMs">Durée de <see cref="StripPlay.Hold"/> et <see cref="StripPlay.Loop"/> (au plus 20 s).</param>
        public StripEffect(ActorSprites.FixedStrip strip, PointF world, double now, StripPlay? play = null, double? durationMs = null,
            EffectLayer layer = EffectLayer.Depth, int depth = 0, int order = 4, float scale = 1f)
        {
            this.strip = strip ?? throw new ArgumentNullException(nameof(strip));
            World = world; requested = now; this.play = play; duration = durationMs; Layer = layer; Depth = depth; Order = order;
            Scale = scale > 0 ? scale : 1f;
        }

        public EffectLayer Layer { get; }
        public int Depth { get; set; }
        public int Order { get; }
        public float Scale { get; }
        /// <summary>Ancre de la bande ; modifiable (projectile).</summary>
        public PointF World { get; set; }
        /// <summary>Première image affichée, ou <c>null</c>.</summary>
        public double? ShownAt { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>Bande absente ou pas prête à temps : l'effet n'a rien affiché.</summary>
        public bool Skipped { get; private set; }
        public int Frame => frame;

        public bool Update(double now, MapView view)
        {
            if (Finished) return false;
            if (!ShownAt.HasValue)
            {
                SpriteLoadState state = strip.State;
                if (state == SpriteLoadState.Ready) ShownAt = now;
                else if (state == SpriteLoadState.Missing || now - requested > MaxLoadWait) { Skipped = true; return Finish(); }
                else return true;
            }
            sheet = strip.Sheet;
            if (sheet == null) return Finish();
            double elapsed = now - ShownAt.Value;
            StripPlay mode = play ?? (sheet.End == SpriteEnd.Stop ? StripPlay.Hold : sheet.End == SpriteEnd.Loop ? StripPlay.Loop : StripPlay.Once);
            double limit = mode == StripPlay.Once ? Math.Min(sheet.DurationMs, MaxDisplay) : Math.Min(duration ?? MaxDisplay, MaxDisplay);
            if (elapsed >= limit) return Finish();
            frame = sheet.FrameAt(elapsed, mode == StripPlay.Loop);
            return true;
        }

        public void Draw(Graphics graphics, MapView view)
        {
            if (Finished || !ShownAt.HasValue || sheet == null) return;
            if (attributes == null) attributes = SpritePainter.EdgeSafe();
            RectangleF target = sheet.Destination(World, Scale, Scale, false);
            if (Layer == EffectLayer.Screen && view != null)
                target = new RectangleF(World.X + sheet.XMin * Scale * view.Scale, World.Y + sheet.YMin * Scale * view.Scale,
                    sheet.FrameWidth * Scale * view.Scale, sheet.FrameHeight * Scale * view.Scale);
            SpritePainter.Draw(graphics, sheet, frame, target, false, attributes);
        }

        private bool Finish()
        {
            Finished = true;
            sheet = null;
            return false;
        }

        public void Dispose()
        {
            Finished = true;
            sheet = null;
            strip.Dispose();
            attributes?.Dispose();
            attributes = null;
        }
    }

    /// <summary>
    /// Bande accrochée à un acteur, dessous (<c>Order</c> 2 : cercle d'équipe) ou dessus (<c>Order</c> 4 : aura, coup
    /// critique, sac du marchand), qui le suit. Le clip du dessus suit <c>xtraClipTopAnimations</c> du client : sur un
    /// personnage seulement en <c>staticF</c>, sur un marchand en <c>staticL</c>, <c>staticF</c> et <c>staticR</c>, toujours
    /// ailleurs (<see cref="TopClipVisible"/>). L'effet se termine quand l'acteur quitte la carte ou à la fin de sa durée.
    /// </summary>
    public class ActorAttachedEffect : IMapEffect, IDisposable
    {
        private readonly ActorSprites.FixedStrip strip;
        private readonly double requested;
        private readonly double? duration;
        private ImageAttributes attributes;
        private SpriteSheet sheet;
        private MapView lastView;
        private int frame, depth;

        public ActorAttachedEffect(long actorId, ActorSprites.FixedStrip strip, bool above, double now, double? durationMs = null,
            bool topClipRule = true, PointF offset = default(PointF))
        {
            this.strip = strip ?? throw new ArgumentNullException(nameof(strip));
            ActorId = actorId; Above = above; requested = now; duration = durationMs; TopClipRule = topClipRule; Offset = offset;
        }

        public long ActorId { get; }
        public bool Above { get; }
        public bool TopClipRule { get; }
        public PointF Offset { get; }
        public EffectLayer Layer => EffectLayer.Depth;
        public int Order => Above ? 4 : 2;
        public int Depth => lastView != null && lastView.TryGetActorAnchor(ActorId, out ActorAnchor anchor) ? (int)anchor.Depth : depth;
        public double? ShownAt { get; private set; }
        public bool Finished { get; private set; }
        public bool Skipped { get; private set; }
        /// <summary>Visible au dernier dessin (condition du clip du dessus remplie).</summary>
        public bool Visible { get; private set; }

        /// <summary>
        /// Condition <c>xtraClipTopAnimations</c> du client pour le clip du dessus : classe Character (joueurs) en
        /// <c>staticF</c> seulement, classe Merchant en <c>staticL</c>, <c>staticF</c>, <c>staticR</c> ; les autres n'en ont pas.
        /// </summary>
        public static bool TopClipVisible(ActorKind kind, string fullAnimation)
        {
            switch (kind)
            {
                case ActorKind.Player: return fullAnimation == "staticF";
                case ActorKind.Merchant: return fullAnimation == "staticL" || fullAnimation == "staticF" || fullAnimation == "staticR";
                default: return true;
            }
        }

        public bool Update(double now, MapView view)
        {
            if (Finished) return false;
            lastView = view;
            if (view == null || !view.TryGetActorAnchor(ActorId, out ActorAnchor anchor)) return Finish();
            depth = (int)anchor.Depth;
            if (!ShownAt.HasValue)
            {
                SpriteLoadState state = strip.State;
                if (state == SpriteLoadState.Ready) ShownAt = now;
                else if (state == SpriteLoadState.Missing || now - requested > StripEffect.MaxLoadWait) { Skipped = true; return Finish(); }
                else return true;
            }
            sheet = strip.Sheet;
            if (sheet == null) return Finish();
            double elapsed = now - ShownAt.Value;
            if (elapsed >= Math.Min(duration ?? StripEffect.MaxDisplay, StripEffect.MaxDisplay)) return Finish();
            frame = sheet.FrameAt(elapsed, sheet.End != SpriteEnd.Stop);
            return true;
        }

        public void Draw(Graphics graphics, MapView view)
        {
            Visible = false;
            if (Finished || sheet == null || view == null || !view.TryGetActorAnchor(ActorId, out ActorAnchor anchor) || !anchor.IsVisible) return;
            if (Above && TopClipRule && !TopClipVisible(anchor.Kind, anchor.Animation)) return;
            if (attributes == null) attributes = SpritePainter.EdgeSafe();
            float sx = anchor.ScaleX / 100f, sy = anchor.ScaleY / 100f;
            var foot = new PointF(anchor.WorldFoot.X + Offset.X * sx, anchor.WorldFoot.Y + Offset.Y * sy);
            SpritePainter.Draw(graphics, sheet, frame, sheet.Destination(foot, sx, sy, false), false, attributes);
            Visible = true;
        }

        private bool Finish()
        {
            Finished = true;
            sheet = null;
            return false;
        }

        public void Dispose()
        {
            Finished = true;
            sheet = null;
            strip.Dispose();
            attributes?.Dispose();
            attributes = null;
        }
    }

    /// <summary>
    /// Effets en cours sur la carte : <see cref="Add"/>, <see cref="Update"/> (retire et libère les effets terminés),
    /// dessin par couche, <see cref="Clear"/> au changement de carte ou de combat. Un effet qui lève une exception est retiré
    /// (<see cref="EffectFailed"/>) sans interrompre le dessin. Fil de l'interface.
    /// </summary>
    public sealed class MapEffectSet : IDisposable
    {
        private readonly List<IMapEffect> effects = new List<IMapEffect>();

        public event Action<IMapEffect, Exception> EffectFailed;

        public int Count => effects.Count;
        public IReadOnlyList<IMapEffect> Active => effects.ToArray();

        public void Add(IMapEffect effect)
        {
            if (effect != null && !effects.Contains(effect)) effects.Add(effect);
        }

        /// <summary>Fait avancer chaque effet ; vrai s'il en reste.</summary>
        public bool Update(double now, MapView view)
        {
            foreach (IMapEffect effect in effects.ToArray())
            {
                bool alive;
                try { alive = effect.Update(now, view); }
                catch (Exception error) when (!(error is OutOfMemoryException)) { Fail(effect, error); alive = false; }
                if (!alive) Remove(effect);
            }
            return effects.Count > 0;
        }

        /// <summary>Dessine les effets d'une couche plate (<see cref="EffectLayer.Ground"/> ou <see cref="EffectLayer.Screen"/>).</summary>
        public void Draw(Graphics graphics, MapView view, EffectLayer layer)
        {
            foreach (IMapEffect effect in effects.ToArray())
                if (effect.Layer == layer) SafeDraw(effect, graphics, view);
        }

        /// <summary>Couches de profondeur des effets <see cref="EffectLayer.Depth"/>, à intercaler avec le décor et les acteurs.</summary>
        public IEnumerable<BotMapArtwork.DepthLayer> DepthLayers(MapView view)
        {
            return effects.Where(effect => effect.Layer == EffectLayer.Depth).ToArray().Select(effect => new BotMapArtwork.DepthLayer
            {
                Depth = SafeDepth(effect), Order = effect.Order, Draw = graphics => SafeDraw(effect, graphics, view)
            });
        }

        public void Remove(IMapEffect effect)
        {
            if (!effects.Remove(effect)) return;
            try { (effect as IDisposable)?.Dispose(); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Fail(effect, error); }
        }

        public void Clear()
        {
            foreach (IMapEffect effect in effects.ToArray()) Remove(effect);
        }

        public void Dispose() => Clear();

        private int SafeDepth(IMapEffect effect)
        {
            try { return effect.Depth; }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Fail(effect, error); return 0; }
        }

        private void SafeDraw(IMapEffect effect, Graphics graphics, MapView view)
        {
            if (!effects.Contains(effect)) return;
            GraphicsState saved = graphics.Save();
            try { effect.Draw(graphics, view); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Fail(effect, error);
                Remove(effect);
            }
            finally { graphics.Restore(saved); }
        }

        private void Fail(IMapEffect effect, Exception error)
        {
            try { EffectFailed?.Invoke(effect, error); } catch (Exception) { /* diagnostic seulement */ }
        }
    }
}
