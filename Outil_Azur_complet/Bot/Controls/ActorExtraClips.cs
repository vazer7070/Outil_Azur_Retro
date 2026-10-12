using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Clip annexe permanent accroché à un acteur (lot AN8), comme <c>addExtraClip</c> du client 1.34 : aura au-dessus
    /// (<c>auras/&lt;n&gt;.swf</c>), cercle d'équipe au-dessous (<c>circle.swf</c> teinté), sac du marchand au-dessus
    /// (<c>extra/&lt;offlineType&gt;.swf</c>), clip du dessous d'un PNJ (<c>extra/&lt;extraClip&gt;.swf</c>). À la
    /// différence d'<see cref="ActorAttachedEffect"/>, pas de limite de 20 s et pas de fin quand l'acteur n'est pas dessiné :
    /// c'est <see cref="ActorExtraClips"/> qui le retire quand le modèle change. La bande est posée au pied du sprite (origine
    /// du clip chargé par <c>loadMovie</c>), à l'échelle du sprite ; le clip du dessus suit <c>xtraClipTopAnimations</c>
    /// (<see cref="ActorAttachedEffect.TopClipVisible"/>). <see cref="Rgb"/> reproduit <c>new Color(clip).setRGB(couleur)</c> :
    /// composantes remplacées, alpha gardé, calculé une fois quand la bande est prête (aucun fichier lu sur ce fil).
    /// </summary>
    public sealed class ExtraClipEffect : IMapEffect, IDisposable
    {
        private readonly ActorSprites.FixedStrip strip;
        private readonly double requested;
        private ImageAttributes attributes;
        private SpriteSheet sheet, tinted;
        private Bitmap tintedImage;
        private MapView lastView;
        private int frame, depth;

        public ExtraClipEffect(long actorId, ActorSprites.FixedStrip strip, bool above, double now, int? rgb = null, bool topClipRule = true)
        {
            this.strip = strip ?? throw new ArgumentNullException(nameof(strip));
            ActorId = actorId; Above = above; requested = now; Rgb = rgb.HasValue ? rgb.Value & 0xFFFFFF : (int?)null; TopClipRule = topClipRule;
        }

        /// <summary>
        /// Attente maximale de la bande (ms de l'horloge de la vue) avant d'abandonner le clip : plus longue que celle des
        /// effets ponctuels (<see cref="StripEffect.MaxLoadWait"/>), car le clip est permanent et sa bande, lue sur le pool
        /// derrière les sprites des acteurs, peut arriver tard sur une carte chargée.
        /// </summary>
        public const double MaxLoadWait = 5000;

        public long ActorId { get; }
        /// <summary>Clip du dessus (<c>_mcXtraTop</c>, devant le sprite) ou du dessous (<c>_mcXtraBack</c>, derrière).</summary>
        public bool Above { get; }
        /// <summary>Couleur <c>setRGB</c> (0xRRGGBB), ou null pour la bande telle quelle.</summary>
        public int? Rgb { get; }
        public bool TopClipRule { get; }
        public string Family => strip.Family;
        public int ClipId => strip.Id;
        public EffectLayer Layer => EffectLayer.Depth;
        public int Order => Above ? 4 : 2;
        public int Depth => lastView != null && lastView.TryGetActorAnchor(ActorId, out ActorAnchor anchor) ? (int)anchor.Depth : depth;
        /// <summary>Première image (bande prête), ou null.</summary>
        public double? ShownAt { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>Bande absente ou pas prête à temps : rien ne sera affiché.</summary>
        public bool Skipped { get; private set; }
        /// <summary>Dessiné au dernier passage (acteur visible, condition du clip du dessus remplie).</summary>
        public bool Visible { get; private set; }
        public int Frame => frame;

        public bool Update(double now, MapView view)
        {
            if (Finished) return false;
            lastView = view;
            if (view != null && view.TryGetActorAnchor(ActorId, out ActorAnchor anchor)) depth = (int)anchor.Depth;
            if (!ShownAt.HasValue)
            {
                SpriteLoadState state = strip.State;
                if (state == SpriteLoadState.Ready) ShownAt = now;
                else if (state == SpriteLoadState.Missing || now - requested > MaxLoadWait) { Skipped = true; return Finish(); }
                else return true;
            }
            SpriteSheet source = strip.Sheet;
            if (source == null) return Finish();
            if (!ReferenceEquals(source, sheet)) { sheet = source; ReleaseTint(); }
            if (Rgb.HasValue && tinted == null) tinted = Tint(sheet, Rgb.Value, out tintedImage);
            frame = sheet.FrameAt(now - ShownAt.Value, sheet.End != SpriteEnd.Stop);
            return true;
        }

        public void Draw(Graphics graphics, MapView view)
        {
            Visible = false;
            if (Finished || sheet == null || view == null || !view.TryGetActorAnchor(ActorId, out ActorAnchor anchor) || !anchor.IsVisible || anchor.IsGhost) return;
            if (Above && TopClipRule && !ActorAttachedEffect.TopClipVisible(anchor.Kind, anchor.Animation)) return;
            if (attributes == null) attributes = SpritePainter.EdgeSafe();
            SpriteSheet shown = tinted ?? sheet;
            float sx = anchor.ScaleX / 100f, sy = anchor.ScaleY / 100f;
            SpritePainter.Draw(graphics, shown, frame, shown.Destination(anchor.WorldFoot, sx, sy, false), false, attributes);
            Visible = true;
        }

        /// <summary>Copie de la bande avec les composantes RVB remplacées par <paramref name="rgb"/>, alpha gardé (<c>Color.setRGB</c>).</summary>
        public static SpriteSheet Tint(SpriteSheet source, int rgb, out Bitmap image)
        {
            image = null;
            if (source?.Image == null) return null;
            int color = rgb & 0xFFFFFF;
            Bitmap copy = null;
            try
            {
                copy = new Bitmap(source.Image.Width, source.Image.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(copy))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImage(source.Image, 0, 0, copy.Width, copy.Height);
                }
                BitmapData data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[copy.Width];
                    for (int y = 0; y < copy.Height; y++)
                    {
                        IntPtr line = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(line, row, 0, row.Length);
                        for (int x = 0; x < row.Length; x++) row[x] = (int)((uint)row[x] & 0xFF000000u) | color;
                        Marshal.Copy(row, 0, line, row.Length);
                    }
                }
                finally { copy.UnlockBits(data); }
                image = copy;
                return new SpriteSheet(copy, source.Frames, source.FrameWidth, source.FrameHeight, source.XMin, source.YMin, source.File, false, null,
                    source.FramesPerSecond, source.End, source.NextAnimation);
            }
            catch (Exception error) when (error is ExternalException || error is ArgumentException || error is OutOfMemoryException)
            {
                copy?.Dispose();
                return null;
            }
        }

        private void ReleaseTint()
        {
            tinted = null;
            tintedImage?.Dispose();
            tintedImage = null;
        }

        private bool Finish()
        {
            Finished = true;
            sheet = null;
            ReleaseTint();
            return false;
        }

        public void Dispose()
        {
            Finished = true;
            sheet = null;
            ReleaseTint();
            strip.Dispose();
            attributes?.Dispose();
            attributes = null;
        }
    }

    /// <summary>
    /// Suivi d'une émote jouée en bande (<c>eUK&lt;id&gt;|&lt;n&gt;[|&lt;durée&gt;]</c>, lot AN8). Effet sans dessin, sur le fil de
    /// l'interface : la bande se lit sur le pool, donc l'icône d'émote du lot AN1 n'est effacée (<paramref name="onShown"/>)
    /// qu'à sa première image dessinée ; si la bande manque ou n'est pas lue à temps, la file abandonne l'animation et
    /// l'icône reste. Avec une durée, <c>setSpriteTimerAnim</c> du client remet <c>defaultAnimation</c> (<c>static</c>) au
    /// terme : l'animation ponctuelle est annulée à <see cref="End"/> si c'est encore cette émote (une autre émote ou un
    /// déplacement l'ont déjà remplacée sinon).
    /// </summary>
    public sealed class EmoteTimer : IMapEffect
    {
        private readonly ActorAnimationQueue queue;
        private readonly Action onShown;
        private bool shown;

        public EmoteTimer(ActorAnimationQueue queue, long actorId, string animation, double? end, Action onShown = null)
        {
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.onShown = onShown;
            ActorId = actorId; Animation = animation; End = end;
        }

        public long ActorId { get; }
        public string Animation { get; }
        /// <summary>Heure de l'horloge de la vue à laquelle l'émote se termine ; null sans durée (bande tenue).</summary>
        public double? End { get; }
        public bool Finished { get; private set; }
        public EffectLayer Layer => EffectLayer.Screen;
        public int Depth => 0;
        public int Order => 4;

        public bool Update(double now, MapView view)
        {
            if (Finished) return false;
            if (!queue.IsPlaying(ActorId, Animation)) { Finished = true; return false; }
            if (!shown && queue.IsShown(ActorId, Animation)) { shown = true; onShown?.Invoke(); }
            if (!End.HasValue || now < End.Value) return true;
            Finished = true;
            queue.Cancel(ActorId);
            return false;
        }

        public void Draw(Graphics graphics, MapView view) { }
    }

    /// <summary>
    /// Clips annexes voulus par le modèle, comme <c>onSpriteMovement</c> du client 1.34, et leur mise à jour dans
    /// <see cref="MapEffectSet"/> (lot AN8) : un marchand porte <c>extra/&lt;offlineType&gt;</c> au-dessus ; un PNJ porte
    /// <c>extra/&lt;extraClip&gt;</c> au-dessous quand le champ existe ; en combat lancé (<c>Game.isRunning</c>, après <c>GS</c>),
    /// chaque acteur porte le cercle d'équipe au-dessous, teinté <c>TEAMS_COLOR[équipe]</c> ; sinon un joueur dont le <c>GM</c>
    /// donne une aura la porte au-dessus si l'option <c>Aura</c> est vraie. <see cref="Reconcile"/> ajoute les clips manquants et
    /// retire ceux que le modèle ne veut plus ; une bande absente (<c>auras/3</c> : scène vide) n'est pas redemandée.
    /// Fil de l'interface.
    /// </summary>
    public sealed class ActorExtraClips
    {
        public const string AuraFamily = "auras";
        public const string CircleFamily = "cercle";
        public const string ExtraFamily = "extra";
        public const string SceneAnimation = "scene";
        /// <summary><c>Effets/cercle/1_scene.png</c> : <c>circle.swf</c> exporté sous le numéro 1.</summary>
        public const int CircleClip = 1;
        /// <summary><c>TEAMS_COLOR</c> du client : rouge pour l'équipe 0, bleu pour l'équipe 1.</summary>
        public static readonly int[] TeamColors = { 0xFF0000, 0x0000FF };

        private readonly Dictionary<string, ExtraClipEffect> clips = new Dictionary<string, ExtraClipEffect>(StringComparer.Ordinal);
        private readonly HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Un clip voulu pour un acteur.</summary>
        public struct Wanted
        {
            public Wanted(long actorId, string family, int id, bool above, int? rgb = null)
            {
                ActorId = actorId; Family = family; Id = id; Above = above; Rgb = rgb;
            }
            public long ActorId { get; }
            public string Family { get; }
            public int Id { get; }
            public bool Above { get; }
            public int? Rgb { get; }
            public string Key => ActorId.ToString(CultureInfo.InvariantCulture) + "|" + Family + "/" + Id.ToString(CultureInfo.InvariantCulture)
                + (Above ? "|dessus" : "|dessous") + (Rgb.HasValue ? "|" + Rgb.Value.ToString("X6", CultureInfo.InvariantCulture) : string.Empty);
            public override string ToString() => Key;
        }

        /// <summary>Clips en place (tests, diagnostic).</summary>
        public IReadOnlyCollection<ExtraClipEffect> Active => clips.Values.ToArray();
        public int Count => clips.Count;

        /// <summary>Couleur du cercle d'une équipe (<c>TEAMS_COLOR</c>), null hors de 0 et 1 (le client ne teinte pas).</summary>
        public static int? TeamColor(int? team) => team.HasValue && team.Value >= 0 && team.Value < TeamColors.Length ? TeamColors[team.Value] : (int?)null;

        /// <summary>
        /// Clips voulus pour <paramref name="actor"/> : <paramref name="fightRunning"/> = combat lancé (<c>isRunning</c>),
        /// <paramref name="auraOption"/> = option <c>Aura</c>, <paramref name="teamOf"/> = équipe connue du combat quand l'acteur
        /// n'en annonce pas.
        /// </summary>
        public static IEnumerable<Wanted> WantedFor(MapActor actor, bool fightRunning, bool auraOption, Func<long, int?> teamOf)
        {
            if (actor == null) yield break;
            switch (actor)
            {
                case MerchantActor merchant:
                    if (merchant.OfflineType >= 0) yield return new Wanted(actor.Id, ExtraFamily, merchant.OfflineType, true);
                    yield break;
                case NpcActor npc:
                    if (npc.ExtraClip >= 0) yield return new Wanted(actor.Id, ExtraFamily, npc.ExtraClip, false);
                    yield break;
            }
            if (fightRunning)
            {
                int? team = (actor as PlayerActor)?.Team ?? (actor as FightMonsterActor)?.Team ?? (actor as CollectorActor)?.Team;
                if (!team.HasValue || team.Value < 0) team = teamOf?.Invoke(actor.Id);
                yield return new Wanted(actor.Id, CircleFamily, CircleClip, false, TeamColor(team));
                yield break;
            }
            if (actor is PlayerActor player && auraOption && player.Aura > 0)
                yield return new Wanted(actor.Id, AuraFamily, player.Aura, true);
        }

        /// <summary>
        /// Met les effets au niveau de <paramref name="wanted"/> : ajoute ce qui manque (bande demandée au pool par
        /// <see cref="ActorSprites.ResolveFixed"/>, effet ajouté par <paramref name="tryAdd"/>, qui le refuse carte cachée) et
        /// retire ce que le modèle ne veut plus. Un clip sauté (bande absente) est noté et n'est plus redemandé.
        /// </summary>
        public void Reconcile(IEnumerable<Wanted> wanted, MapEffectSet effects, ActorSprites sprites, Func<IMapEffect, bool> tryAdd, double now)
        {
            if (effects == null || sprites == null || tryAdd == null) return;
            var keep = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<IMapEffect> active = effects.Active;
            foreach (Wanted clip in wanted ?? Enumerable.Empty<Wanted>())
            {
                string key = clip.Key;
                if (!keep.Add(key) || missing.Contains(key)) continue;
                if (clips.TryGetValue(key, out ExtraClipEffect existing))
                {
                    if (!existing.Finished && active.Contains(existing)) continue;
                    clips.Remove(key);
                    if (existing.Skipped) { missing.Add(key); continue; }
                }
                var effect = new ExtraClipEffect(clip.ActorId, sprites.ResolveFixed(clip.Family, clip.Id, SceneAnimation), clip.Above, now, clip.Rgb);
                if (!tryAdd(effect)) { Forget(effects); return; }
                clips[key] = effect;
            }
            foreach (KeyValuePair<string, ExtraClipEffect> pair in clips.Where(pair => !keep.Contains(pair.Key)).ToArray())
            {
                clips.Remove(pair.Key);
                effects.Remove(pair.Value);
            }
        }

        /// <summary>Retire tous les clips (changement de carte, combat, carte cachée) et oublie les bandes absentes.</summary>
        public void Forget(MapEffectSet effects)
        {
            foreach (ExtraClipEffect effect in clips.Values.ToArray()) effects?.Remove(effect);
            clips.Clear();
            missing.Clear();
        }
    }
}
