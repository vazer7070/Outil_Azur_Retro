using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Projectiles des sorts (lot AN5) : types 20, 21, 30, 31, 40, 41, 50 et 51 de GA300, d'après
    /// <c>VisualEffectHandler.onLoadInit</c> du client 1.34 (cadence <c>DOUBLEFRAMERATE</c>, 40 images/s). Le clip est posé au
    /// lanceur (pied du sprite, <c>mc._x</c>, <c>mc._y</c>) et vise le centre de la cellule ; <c>dx</c>, <c>dy</c> vont de l'un
    /// à l'autre.
    /// <list type="bullet">
    /// <item>20 et 21 : l'enfant <c>rotate</c> de la scène tourné de <c>atan2(dy, dx)</c> (bande <c>&lt;gfx&gt;_rotate.png</c>,
    /// tournée autour du centre de son cadre) et <c>shoot</c> posé à (dx, dy) ; non bloquant.</item>
    /// <item>30 et 31 : parabole de <c>move</c> (<see cref="ProjectileArc"/>), clip 10 px au-dessus du lanceur, retourné si
    /// <c>dx ≤ 0</c> ; à l'arrivée <c>shoot</c> à la destination et fin de l'étape (<c>end()</c>).</item>
    /// <item>40 et 41 : traînée de <c>duplicate</c> (<see cref="ProjectileTrail"/>) ; 41 pose <c>shoot</c> à la fin.</item>
    /// <item>50 et 51 : le clip du client se débrouille seul par script ; le bot pose la scène, ou à défaut <c>shoot</c>, au
    /// centre de la cellule visée et la joue une fois (approximation du plan des animations).</item>
    /// </list>
    /// Le nombre d'appels d'<c>onEnterFrame</c> à l'instant <c>now</c> vaut <c>floor((now − début) · 40 / 1000)</c>.
    /// </summary>
    public static class SpellProjectiles
    {
        /// <summary>Cadence du client (<c>DOUBLEFRAMERATE</c>) : un appel d'<c>onEnterFrame</c> toutes les 25 ms.</summary>
        public const double FramesPerSecond = 40;
        /// <summary>Durée d'une image (ms).</summary>
        public const double FrameMs = 1000 / FramesPerSecond;
        /// <summary>Appels suivis au plus : 20 s (<c>VISUAL_EFFECT_MAX_TIMER</c>), après quoi le clip est retiré.</summary>
        public const int MaxCalls = (int)(StripEffect.MaxDisplay / FrameMs);
        /// <summary>Profondeur d'attache de <c>shoot</c> pour les types 20, 21 et 41 (<c>attachMovie(…, 10)</c>).</summary>
        public const int ShootDepth = 10;

        public const string RotateAnimation = "rotate", ShootAnimation = "shoot", MoveAnimation = "move", DuplicateAnimation = "duplicate";

        private static readonly string[] None = new string[0];
        private static readonly string[] Turned = { SpellEffects.SceneAnimation, RotateAnimation, ShootAnimation };
        private static readonly string[] Thrown = { SpellEffects.SceneAnimation, MoveAnimation, ShootAnimation };
        private static readonly string[] Trail = { SpellEffects.SceneAnimation, DuplicateAnimation };
        private static readonly string[] TrailAndShoot = { SpellEffects.SceneAnimation, DuplicateAnimation, ShootAnimation };
        private static readonly string[] Scripted = { SpellEffects.SceneAnimation, ShootAnimation };

        /// <summary>Type de projectile (20, 21, 30, 31, 40, 41, 50, 51).</summary>
        public static bool IsProjectile(int type)
        {
            switch (type)
            {
                case 20: case 21: case 30: case 31: case 40: case 41: case 50: case 51: return true;
                default: return false;
            }
        }

        /// <summary>Bandes d'<c>Effets/sorts</c> qu'un type de projectile emploie (vide pour les autres types).</summary>
        public static IReadOnlyList<string> Animations(int type)
        {
            switch (type)
            {
                case 20: case 21: return Turned;
                case 30: case 31: return Thrown;
                case 40: return Trail;
                case 41: return TrailAndShoot;
                case 50: case 51: return Scripted;
                default: return None;
            }
        }

        /// <summary>Appels d'<c>onEnterFrame</c> faits <paramref name="elapsedMs"/> ms après la pose du clip (0 à <see cref="MaxCalls"/>).</summary>
        public static int Calls(double elapsedMs)
        {
            if (!(elapsedMs > 0)) return 0;
            double calls = Math.Floor(elapsedMs * FramesPerSecond / 1000);
            return calls >= MaxCalls ? MaxCalls : (int)calls;
        }

        /// <summary><c>angle</c> du clip : <c>atan2(dy, dx) · 180 / π</c>, en degrés (sens des aiguilles d'une montre à l'écran).</summary>
        public static double AngleDegrees(double dx, double dy) => Math.Atan2(dy, dx) * 180 / Math.PI;
    }

    /// <summary>
    /// Parabole des types 30 et 31 (et 33, que GA300 n'emploie pas), formules exactes du client, calculées une fois appel par
    /// appel (<c>t</c> cumulé comme dans le client) :
    /// <c>a = (atan2(dy, |dx|) + π/2) · f − π/2</c> (f = 0,5 ; 0,9 pour 31 et 33), <c>X = |dx|</c>, <c>Y = dy</c> ;
    /// si <c>dx ≤ 0</c>, clip retourné horizontalement (et verticalement avec <c>Y = −dy</c> si <c>dx = 0</c> et <c>dy &lt; 0</c>) ;
    /// <c>vx = √|g/2 · X² / |Y − tan(a) · X||</c>, <c>vy = tan(a) · vx</c>, <c>g = 9,81</c> ; à chaque appel
    /// <c>x = t · vx</c>, <c>y = g/2 · t² + vy · t</c>, rotation <c>atan((vy + g · t) / vx)</c>, puis <c>t += 0,675 / 2</c> ;
    /// arrivée quand <c>(|y| ≥ |Y| et x ≥ X) ou x &gt; X</c> (comparaisons du client, NaN compris).
    /// </summary>
    public sealed class ProjectileArc
    {
        public const double Gravity = 9.81;
        /// <summary><c>speed</c> : 0,675, divisé par 2 avec <c>DOUBLEFRAMERATE</c>.</summary>
        public const double Speed = 0.675 / 2;
        /// <summary>Le clip est posé 10 px au-dessus du lanceur.</summary>
        public const float Lift = 10f;

        private readonly double[] xs, ys, rotations;

        public ProjectileArc(double dx, double dy, int type)
        {
            Factor = type == 31 || type == 33 ? 0.9 : 0.5;
            double quarter = Math.PI / 2;
            StartAngle = (Math.Atan2(dy, Math.Abs(dx)) + quarter) * Factor - quarter;
            double xDest = Math.Abs(dx), yDest = dy;
            if (!(dx > 0))
            {
                if (dx == 0 && dy < 0) { MirrorY = true; yDest = -yDest; }
                MirrorX = true;
            }
            DestX = xDest; DestY = yDest;
            double halfg = Gravity / 2;
            Vx = Math.Sqrt(Math.Abs(halfg * Math.Pow(xDest, 2) / Math.Abs(yDest - Math.Tan(StartAngle) * xDest)));
            Vy = Math.Tan(StartAngle) * Vx;

            var x = new List<double>(); var y = new List<double>(); var r = new List<double>();
            double t = 0;
            for (int call = 1; call <= SpellProjectiles.MaxCalls; call++)
            {
                double vyi = Vy + Gravity * t;
                double px = t * Vx, py = halfg * Math.Pow(t, 2) + Vy * t;
                t += Speed;
                double rotation = Math.Atan(vyi / Vx) * 180 / Math.PI;
                if ((!(Math.Abs(py) < Math.Abs(yDest)) && !(px < xDest)) || px > xDest)
                {
                    ArrivalCall = call;
                    ArrivalRotation = rotation;
                    break;
                }
                x.Add(px); y.Add(py); r.Add(rotation);
            }
            xs = x.ToArray(); ys = y.ToArray(); rotations = r.ToArray();
        }

        /// <summary>Facteur f de l'angle de départ (0,5 ou 0,9).</summary>
        public double Factor { get; }
        /// <summary>Angle de départ <c>a</c> (radians, <c>startangle</c> du client).</summary>
        public double StartAngle { get; }
        /// <summary>Destination dans le repère du clip (après retournement) : <c>X = |dx|</c>, <c>Y</c>.</summary>
        public double DestX { get; }
        public double DestY { get; }
        public double Vx { get; }
        public double Vy { get; }
        /// <summary><c>_xscale</c> négatif (dx ≤ 0).</summary>
        public bool MirrorX { get; }
        /// <summary><c>_yscale</c> négatif (dx = 0 et dy &lt; 0).</summary>
        public bool MirrorY { get; }
        /// <summary>Appel (1 pour le premier) où <c>shoot</c> remplace <c>move</c> et où l'étape est libérée ; 0 si l'arrivée ne vient pas en 20 s.</summary>
        public int ArrivalCall { get; }
        /// <summary>Rotation de <c>shoot</c> à l'arrivée (degrés ; NaN si le lanceur vise sa propre position).</summary>
        public double ArrivalRotation { get; } = double.NaN;

        /// <summary>
        /// Position et rotation de <c>move</c> (repère du clip, avant retournement) après <paramref name="calls"/> appels, tant que
        /// l'arrivée n'est pas atteinte : (0, 0) sans rotation avant le premier appel. Faux une fois <c>move</c> remplacé.
        /// </summary>
        public bool TryMove(int calls, out double x, out double y, out double rotation)
        {
            x = y = rotation = 0;
            if (calls <= 0) return true; // attaché par onLoadInit, avant le premier appel
            if (ArrivalCall != 0 && calls >= ArrivalCall) return false;
            int index = Math.Min(calls, xs.Length) - 1;
            if (index < 0) return false;
            x = xs[index]; y = ys[index]; rotation = rotations[index];
            return true;
        }
    }

    /// <summary>
    /// Traînée des types 40 et 41, formules exactes du client : <c>D = √(dx² + dy²)</c>, <c>interval = D / floor(D / 10)</c> ;
    /// à chaque appel <c>dist += interval</c>, et un appel sur deux (le premier, le troisième…) : si <c>dist &gt; D</c>, fin
    /// (<c>end()</c>, puis <c>shoot</c> à (dx, dy) pour 41), sinon un <c>duplicate</c> de plus à <c>dist</c> sur le segment.
    /// D = 100 donne 5 <c>duplicate</c> (10, 30, 50, 70, 90) et la fin au 11<sup>e</sup> appel ; D &lt; 10, la fin au premier
    /// appel ; D = 0 (même position), des distances NaN dans le client : rien n'est posé et la fin ne vient pas.
    /// </summary>
    public sealed class ProjectileTrail
    {
        /// <summary><c>20 / 2</c> (<c>DOUBLEFRAMERATE</c>).</summary>
        public const double Spacing = 10;

        private readonly PointF[] offsets;

        public ProjectileTrail(double dx, double dy)
        {
            Angle = Math.Atan2(dy, dx);
            Distance = Math.Sqrt(Math.Pow(0 - dx, 2) + Math.Pow(0 - dy, 2));
            Interval = Distance / Math.Floor(Distance / Spacing);
            var list = new List<PointF>();
            double dist = 0;
            bool skip = false;
            for (int call = 1; call <= SpellProjectiles.MaxCalls && !double.IsNaN(Interval); call++)
            {
                dist += Interval;
                if (!skip)
                {
                    if (dist > Distance) { EndCall = call; break; }
                    list.Add(new PointF((float)(dist * Math.Cos(Angle)), (float)(dist * Math.Sin(Angle))));
                }
                skip = !skip;
            }
            offsets = list.ToArray();
        }

        public double Distance { get; }
        public double Interval { get; }
        /// <summary>Direction du segment (radians).</summary>
        public double Angle { get; }
        /// <summary>Appel de la fin (1 pour le premier) ; 0 si elle ne vient pas.</summary>
        public int EndCall { get; }
        /// <summary>Nombre de <c>duplicate</c> posés avant la fin.</summary>
        public int Count => offsets.Length;

        /// <summary>Position du <c>duplicate</c> <paramref name="index"/> (0 pour le premier, profondeur <c>index + 1</c>) dans le repère du clip.</summary>
        public PointF Offset(int index) => offsets[index];

        /// <summary>Appel qui pose le <c>duplicate</c> <paramref name="index"/> : 1, 3, 5…</summary>
        public static int AttachCall(int index) => 2 * index + 1;

        /// <summary><c>duplicate</c> posés après <paramref name="calls"/> appels.</summary>
        public int AttachedAfter(int calls) => calls <= 0 ? 0 : Math.Min(Count, (calls + 1) / 2);
    }

    /// <summary>
    /// Clip d'un projectile sur la carte (couche Depth, profondeur de <see cref="SpellEffects.Depth"/>), bandes lues par
    /// <see cref="ActorSprites.ResolveFixed"/> (<c>Effets/sorts/&lt;gfx&gt;_&lt;anim&gt;.png</c>). Les bandes absentes sont
    /// ignorées ; celles qui ne sont pas prêtes 300 ms après la pose aussi, et le clip part alors sans elles. La géométrie
    /// avance même sans aucune bande : l'étape des types 30, 31, 40 et 41 est libérée à l'arrivée, comme par <c>end()</c>.
    /// <para>
    /// Durée de vie : chaque bande attachée part de sa première image à son appel d'attache ; <c>arret</c> tient sa dernière
    /// image, <c>move</c> boucle jusqu'à l'arrivée, les autres jouent une passe. Le clip se termine à la fin de la première
    /// bande <c>static</c> de la scène, de <c>rotate</c> ou de <c>shoot</c> (dans ces SWF, leur dernière image retire le clip),
    /// sinon quand tout est joué ; jamais plus de 20 s. Types 50 et 51 : une passe de la scène (ou de <c>shoot</c>), comme les
    /// scènes du lot AN4. Fil de l'interface, aucune lecture de fichier.
    /// </para>
    /// </summary>
    public sealed class ProjectileEffect : IMapEffect, IDisposable
    {
        private enum Kind { Scene, Rotate, Shoot, Move, Duplicate }

        private readonly int type;
        private readonly double dx, dy, requested;
        private readonly PointF origin;
        private readonly float scaleX = 1f, scaleY = 1f;
        private readonly ActorSprites.FixedStrip[] strips;
        private readonly Kind[] kinds;
        private SpriteSheet scene, rotate, shoot, move, duplicate;
        private ImageAttributes attributes;
        private double clipEnd, shootStart = double.NaN, releaseAt = double.PositiveInfinity, elapsed;
        private int calls;
        private bool disposed;

        /// <param name="caster">Pied du lanceur (<c>r12</c>), repère de la carte.</param>
        /// <param name="target">Centre de la cellule visée (<c>r13</c>).</param>
        public ProjectileEffect(ActorSprites sprites, int gfx, int type, PointF caster, PointF target, double now, int depth, int order)
        {
            if (sprites == null) throw new ArgumentNullException(nameof(sprites));
            if (!SpellProjectiles.IsProjectile(type)) throw new ArgumentOutOfRangeException(nameof(type));
            this.type = type; requested = now; Depth = depth; Order = order;
            dx = target.X - caster.X; dy = target.Y - caster.Y;
            Angle = SpellProjectiles.AngleDegrees(dx, dy);
            switch (type)
            {
                case 30: case 31:
                    Arc = new ProjectileArc(dx, dy, type);
                    origin = new PointF(caster.X, caster.Y - ProjectileArc.Lift);
                    if (Arc.MirrorX) scaleX = -1f;
                    if (Arc.MirrorY) scaleY = -1f;
                    break;
                case 40: case 41:
                    Trail = new ProjectileTrail(dx, dy);
                    origin = caster;
                    break;
                case 50: case 51:
                    origin = target;
                    break;
                default:
                    origin = caster;
                    break;
            }
            IReadOnlyList<string> animations = SpellProjectiles.Animations(type);
            strips = new ActorSprites.FixedStrip[animations.Count];
            kinds = new Kind[animations.Count];
            for (int i = 0; i < animations.Count; i++)
            {
                strips[i] = sprites.ResolveFixed(SpellEffects.SpellFamily, gfx, animations[i]);
                kinds[i] = KindOf(animations[i]);
            }
            TryStart(now);
        }

        public EffectLayer Layer => EffectLayer.Depth;
        public int Depth { get; }
        public int Order { get; }
        public int Type => type;
        /// <summary>Point où le clip est posé (lanceur, 10 px au-dessus pour 30 et 31, cellule visée pour 50 et 51).</summary>
        public PointF Origin => origin;
        /// <summary><c>angle</c> du clip (degrés), rotation de <c>rotate</c> pour 20 et 21.</summary>
        public double Angle { get; }
        /// <summary>Parabole (30, 31) ou null.</summary>
        public ProjectileArc Arc { get; }
        /// <summary>Traînée (40, 41) ou null.</summary>
        public ProjectileTrail Trail { get; }
        /// <summary>Instant où le clip est posé (bandes prêtes ou 300 ms écoulées), ou null.</summary>
        public double? StartedAt { get; private set; }
        /// <summary>Appels d'<c>onEnterFrame</c> faits à la dernière mise à jour.</summary>
        public int CallCount => calls;
        /// <summary>Fin du clip après sa pose (ms), connue une fois posé.</summary>
        public double Lifetime => clipEnd;
        public bool Finished { get; private set; }
        /// <summary>Aucune bande à dessiner (toutes absentes ou en retard).</summary>
        public bool Skipped { get; private set; }

        /// <summary>
        /// L'étape bloquante du lanceur peut continuer : arrivée de la parabole (30, 31), fin de la traînée (40, 41), fin de la
        /// passe (51) ; toujours vrai pour les types non bloquants et pour un clip terminé ou retiré.
        /// </summary>
        public bool IsReleased(double now)
        {
            if (!SpellEffects.IsBlocking(type)) return true;
            // Une fois posé, seule la géométrie compte, même si le clip n'a rien à dessiner ou a été retiré entre-temps
            // (le séquenceur coupe de toute façon à 1 000 ms) ; retiré avant d'être posé, il ne libérera rien : on libère.
            if (!StartedAt.HasValue) return disposed;
            double since = now - StartedAt.Value;
            if (Arc != null) return Arc.ArrivalCall > 0 && SpellProjectiles.Calls(since) >= Arc.ArrivalCall;
            if (Trail != null) return Trail.EndCall > 0 && SpellProjectiles.Calls(since) >= Trail.EndCall;
            return since >= releaseAt;
        }

        public bool Update(double now, MapView view)
        {
            if (Finished) return false;
            if (!StartedAt.HasValue && !TryStart(now)) return true;
            elapsed = now - StartedAt.Value;
            calls = SpellProjectiles.Calls(elapsed);
            if (elapsed >= clipEnd) return Finish();
            return true;
        }

        public void Draw(Graphics graphics, MapView view)
        {
            if (Finished || !StartedAt.HasValue || disposed) return;
            if (attributes == null) attributes = SpritePainter.EdgeSafe();
            switch (type)
            {
                case 20: case 21: DrawTurned(graphics); break;
                case 30: case 31: DrawThrown(graphics); break;
                case 40: case 41: DrawTrail(graphics); break;
                default: DrawAtCell(graphics); break;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Finished = true;
            scene = rotate = shoot = move = duplicate = null;
            foreach (ActorSprites.FixedStrip strip in strips) strip.Dispose();
            attributes?.Dispose();
            attributes = null;
        }

        // ---------------------------------------------------------------- pose et durée de vie

        private static Kind KindOf(string animation)
        {
            switch (animation)
            {
                case SpellProjectiles.RotateAnimation: return Kind.Rotate;
                case SpellProjectiles.ShootAnimation: return Kind.Shoot;
                case SpellProjectiles.MoveAnimation: return Kind.Move;
                case SpellProjectiles.DuplicateAnimation: return Kind.Duplicate;
                default: return Kind.Scene;
            }
        }

        /// <summary>Pose le clip quand chaque bande est prête ou absente, ou 300 ms après la demande ; fixe alors sa durée de vie.</summary>
        private bool TryStart(double now)
        {
            if (StartedAt.HasValue) return true;
            bool late = now - requested > StripEffect.MaxLoadWait;
            foreach (ActorSprites.FixedStrip strip in strips)
                if (strip.State == SpriteLoadState.Loading && !late) return false;
            StartedAt = now;
            for (int i = 0; i < strips.Length; i++)
            {
                SpriteSheet sheet = strips[i].State == SpriteLoadState.Ready ? strips[i].Sheet : null;
                switch (kinds[i])
                {
                    case Kind.Scene: scene = sheet; break;
                    case Kind.Rotate: rotate = sheet; break;
                    case Kind.Shoot: shoot = sheet; break;
                    case Kind.Move: move = sheet; break;
                    default: duplicate = sheet; break;
                }
            }
            Skipped = scene == null && rotate == null && shoot == null && move == null && duplicate == null;
            Plan();
            return true;
        }

        /// <summary>Fin du clip et, pour 51, de l'étape, d'après les bandes présentes (ms après la pose).</summary>
        private void Plan()
        {
            double geometry = 0;
            if (Arc != null)
            {
                geometry = Arc.ArrivalCall > 0 ? Arc.ArrivalCall * SpellProjectiles.FrameMs : StripEffect.MaxDisplay;
                if (Arc.ArrivalCall > 0) shootStart = geometry;
            }
            else if (Trail != null)
            {
                geometry = Trail.EndCall > 0 ? Trail.EndCall * SpellProjectiles.FrameMs : StripEffect.MaxDisplay;
                if (Trail.EndCall > 0 && type == 41) shootStart = geometry;
            }
            else if (type == 20 || type == 21) shootStart = 0;

            if (type == 50 || type == 51)
            {
                SpriteSheet shown = scene ?? shoot;
                clipEnd = shown == null ? 0 : Math.Min(shown.DurationMs, StripEffect.MaxDisplay);
                releaseAt = clipEnd;
                return;
            }
            if (Skipped) { clipEnd = 0; return; } // rien à dessiner ; l'étape suit quand même la géométrie (IsReleased)

            double removing = double.PositiveInfinity, played = geometry;
            bool holding = false;
            Action<SpriteSheet, double, bool> account = (sheet, start, removes) =>
            {
                if (sheet == null || double.IsNaN(start)) return;
                if (removes && sheet.End == SpriteEnd.Static) removing = Math.Min(removing, start + sheet.DurationMs);
                if (sheet.End == SpriteEnd.Stop) holding = true;
                else played = Math.Max(played, start + sheet.DurationMs);
            };
            account(scene, 0, true);
            account(rotate, 0, true);
            account(shoot, shootStart, true);
            if (duplicate != null && Trail != null)
                for (int i = 0; i < Trail.Count; i++) account(duplicate, ProjectileTrail.AttachCall(i) * SpellProjectiles.FrameMs, false);
            double end = !double.IsPositiveInfinity(removing) ? removing : holding ? StripEffect.MaxDisplay : played;
            clipEnd = Math.Min(end, StripEffect.MaxDisplay);
        }

        private bool Finish()
        {
            Finished = true;
            return false;
        }

        // ---------------------------------------------------------------- dessin

        /// <summary>Image d'une bande attachée à <paramref name="start"/> ms : une passe, la dernière image tenue pour <c>arret</c>.</summary>
        private bool Visible(SpriteSheet sheet, double start, out int frame)
        {
            frame = 0;
            double local = elapsed - start;
            if (local < 0) return false;
            if (sheet.End != SpriteEnd.Stop && local >= sheet.DurationMs) return false;
            frame = sheet.FrameAt(local, false);
            return true;
        }

        /// <summary>20, 21 : rotate (profondeur 1 de la scène) tourné autour du centre de son cadre, la scène, puis shoot à (dx, dy).</summary>
        private void DrawTurned(Graphics graphics)
        {
            if (rotate != null && Visible(rotate, 0, out int frame))
            {
                // Cadre de rotate centré sur l'origine de l'instance dans la scène (exporter_sorts.py).
                float pivotX = rotate.XMin + rotate.FrameWidth / 2f, pivotY = rotate.YMin + rotate.FrameHeight / 2f;
                Paint(graphics, rotate, frame, pivotX, pivotY, (float)Angle, true);
            }
            PaintScene(graphics);
            PaintShoot(graphics, (float)dx, (float)dy, 0f);
        }

        /// <summary>30, 31 : la scène, move sur la parabole jusqu'à l'arrivée, puis shoot à la destination (même profondeur 2).</summary>
        private void DrawThrown(Graphics graphics)
        {
            PaintScene(graphics);
            if (move != null && Arc.TryMove(calls, out double x, out double y, out double rotation))
                Paint(graphics, move, move.FrameAt(elapsed, move.End != SpriteEnd.Stop), (float)x, (float)y, Finite(rotation), false);
            PaintShoot(graphics, (float)Arc.DestX, (float)Arc.DestY, Finite(Arc.ArrivalRotation));
        }

        /// <summary>
        /// 40, 41 : la scène, puis dans l'ordre des profondeurs d'attache le duplicate n (profondeur n) et shoot (profondeur 10,
        /// où il remplace le duplicate 10 du client : attachMovie sur une profondeur occupée).
        /// </summary>
        private void DrawTrail(Graphics graphics)
        {
            PaintScene(graphics);
            int attached = duplicate != null ? Trail.AttachedAfter(calls) : 0;
            bool shot = shoot != null && !double.IsNaN(shootStart) && elapsed >= shootStart;
            for (int i = 0; i < attached; i++)
            {
                if (shot && i + 1 == SpellProjectiles.ShootDepth) { PaintShoot(graphics, (float)dx, (float)dy, 0f); continue; }
                if (!Visible(duplicate, ProjectileTrail.AttachCall(i) * SpellProjectiles.FrameMs, out int frame)) continue;
                PointF at = Trail.Offset(i);
                Paint(graphics, duplicate, frame, at.X, at.Y, 0f, false);
            }
            if (attached < SpellProjectiles.ShootDepth) PaintShoot(graphics, (float)dx, (float)dy, 0f);
        }

        /// <summary>50, 51 : la scène, ou à défaut shoot, au centre de la cellule visée, une passe.</summary>
        private void DrawAtCell(Graphics graphics)
        {
            SpriteSheet shown = scene ?? shoot;
            if (shown != null && elapsed < shown.DurationMs) Paint(graphics, shown, shown.FrameAt(elapsed, false), 0, 0, 0f, false);
        }

        private void PaintScene(Graphics graphics)
        {
            if (scene != null && Visible(scene, 0, out int frame)) Paint(graphics, scene, frame, 0, 0, 0f, false);
        }

        private void PaintShoot(Graphics graphics, float x, float y, float rotation)
        {
            if (shoot != null && !double.IsNaN(shootStart) && Visible(shoot, shootStart, out int frame)) Paint(graphics, shoot, frame, x, y, rotation, false);
        }

        private static float Finite(double degrees) => double.IsNaN(degrees) || double.IsInfinity(degrees) ? 0f : (float)degrees;

        /// <summary>
        /// Dessine une image dans le repère du clip : origine, retournement du clip, position de l'enfant, rotation de l'enfant.
        /// <paramref name="centered"/> : le cadre est centré sur la position (rotate), sinon l'ancre de la bande y est posée.
        /// </summary>
        private void Paint(Graphics graphics, SpriteSheet sheet, int frame, float x, float y, float rotation, bool centered)
        {
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.TranslateTransform(origin.X, origin.Y);
                if (scaleX != 1f || scaleY != 1f) graphics.ScaleTransform(scaleX, scaleY);
                graphics.TranslateTransform(x, y);
                if (rotation != 0f) graphics.RotateTransform(rotation);
                RectangleF target = centered
                    ? new RectangleF(-sheet.FrameWidth / 2f, -sheet.FrameHeight / 2f, sheet.FrameWidth, sheet.FrameHeight)
                    : new RectangleF(sheet.XMin, sheet.YMin, sheet.FrameWidth, sheet.FrameHeight);
                SpritePainter.Draw(graphics, sheet, frame, target, false, attributes);
            }
            finally { graphics.Restore(saved); }
        }
    }
}
