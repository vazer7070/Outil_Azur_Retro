using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;
using Tool_Editor.maps.data;

namespace Outil_Azur_complet.Bot.Controls
{
    public partial class UserMapControl
    {
        private readonly string spriteDirectory;

        public sealed class ActorVisualState
        {
            public int Id, CellId, GFX, Orientation, ScaleX, ScaleY;
            public string Name, SpriteReason;
            public PointF WorldPosition, ScreenPosition;
            public RectangleF SpriteBounds;
            public bool IsMoving, IsVisible, IsSelf, HasSprite;
            internal Bitmap Image;
            internal Point Anchor;
            internal Color Color;
        }

        private PointF WorldCenter(int cellId)
        {
            if (worldPolygons == null || cellId < 0 || cellId >= worldPolygons.Length) return PointF.Empty;
            PointF[] polygon = worldPolygons[cellId];
            return new PointF((polygon[0].X + polygon[2].X) / 2, (polygon[1].Y + polygon[3].Y) / 2);
        }

        public ActorVisualState GetActorVisualState(int id) => GetActorVisualStates().FirstOrDefault(actor => actor.Id == id);

        public ActorVisualState[] GetActorVisualStates()
        {
            if (Account?.Game?.Map?.MapCells == null || worldPolygons == null) return new ActorVisualState[0];
            var actors = new List<ActorVisualState>();
            CharacterClass self = Account.Game.character;
            if (self?.Cell != null)
                AddActor(actors, self, self.GFX > 0 ? self.GFX : self.Race_ID * 10 + self.Sex,
                    self.Orientation, self.GraphicsScaleX, self.GraphicsScaleY, Color.FromArgb(72, 103, 156), true, PointF.Empty);
            Entites[] entities = Account.Game.Map.Entites?.Values.ToArray() ?? new Entites[0];
            foreach (Entites entity in entities)
            {
                if (entity?.Cell == null) continue;
                if (entity is PNJ npc)
                    AddActor(actors, npc, npc.GFX, npc.Orientation, npc.GraphicsScaleX, npc.GraphicsScaleY,
                        Color.FromArgb(154, 106, 172), false, PointF.Empty);
                else if (entity is Personnages player)
                    AddActor(actors, player, player.GFX > 0 ? player.GFX : player.Race_ID * 10 + player.Sexe,
                        player.Orientation, player.GraphicsScaleX, player.GraphicsScaleY, Color.FromArgb(85, 103, 143), false, PointF.Empty);
                else if (entity is Monstres group)
                {
                    Monstres[] members = group.MobsInGroupe?.Take(6).ToArray() ?? new Monstres[0];
                    if (members.Length == 0) members = new[] { group };
                    for (int member = members.Length - 1; member >= 0; member--)
                    {
                        Monstres monster = members[member];
                        var offset = member == 0 ? PointF.Empty : new PointF((member % 2 == 0 ? -1 : 1) * (12 + member / 2 * 5), -6 - member / 2 * 6);
                        int gfx = monster.GFX > 0 ? monster.GFX : Monstres.ReturnMonsters(monster.TemplateID)?.GFX ?? 0;
                        AddActor(actors, group, gfx, group.Orientation, monster.GraphicsScaleX, monster.GraphicsScaleY,
                            Color.FromArgb(156, 63, 48), false, offset, monster.Name);
                    }
                }
            }
            var active = new HashSet<int>(actors.Select(actor => actor.Id));
            foreach (int id in Anim.Keys.Where(id => !active.Contains(id)).ToArray()) CancelAnimation(id);
            return actors.ToArray();
        }

        private void AddActor(List<ActorVisualState> actors, Entites entity, int gfx, int orientation, int scaleX, int scaleY,
            Color color, bool self, PointF groupOffset, string name = null)
        {
            int cell = entity.Cell.CellID;
            if (cell < 0 || cell >= worldPolygons.Length) return;
            PointF position = WorldCenter(cell);
            bool moving = false;
            if (Anim.TryGetValue(entity.id, out Animations movement) && movement.TrySample(animationClock(), out Animations.Frame frame))
            {
                bool relocated = cell != movement.StartCellId && cell != movement.EndCellId;
                bool acknowledged = frame.Complete && cell == movement.EndCellId;
                bool rejected = frame.Complete && self && !Account.IsMoving() && cell != movement.EndCellId;
                if (relocated || acknowledged || rejected) CancelAnimation(entity.id);
                else
                {
                    position = frame.Position; cell = frame.CellId; orientation = frame.Direction; moving = !frame.Complete;
                }
            }
            position = new PointF(position.X + groupOffset.X, position.Y + groupOffset.Y);
            var actor = new ActorVisualState
            {
                Id = entity.id, CellId = cell, GFX = gfx, Orientation = orientation, Name = name ?? entity.Name,
                ScaleX = Math.Max(0, scaleX), ScaleY = Math.Max(0, scaleY), WorldPosition = position,
                ScreenPosition = new PointF(position.X * viewScale + origin.X, position.Y * viewScale + origin.Y),
                IsMoving = moving, IsSelf = self, IsVisible = scaleX > 0 && scaleY > 0, Color = color
            };
            if (actor.IsVisible) ResolveActorPicture(actor);
            else actor.SpriteReason = "Personnage masqué par le serveur (taille nulle).";
            actors.Add(actor);
        }

        private void ResolveActorPicture(ActorVisualState actor)
        {
            string key = actor.GFX + "/" + actor.Orientation;
            if (!spriteImages.TryGetValue(key, out Bitmap image))
            {
                image = LoadActorPicture(actor.GFX, actor.Orientation, out string reason);
                spriteImages[key] = image; spriteReasons[key] = reason;
                if (image != null) spriteAnchors[key] = TilesData.Anchor(new TilesData(actor.GFX, "", "", TilesData.TileType.objet), image);
                else if (missingSprites.Add(key)) DisplayStateChanged?.Invoke();
            }
            actor.Image = image; actor.HasSprite = image != null; actor.SpriteReason = spriteReasons[key];
            if (image == null) return;
            actor.Anchor = spriteAnchors[key];
            float scaleX = actor.ScaleX / 100f, scaleY = actor.ScaleY / 100f;
            actor.SpriteBounds = new RectangleF(actor.ScreenPosition.X - actor.Anchor.X * scaleX * viewScale,
                actor.ScreenPosition.Y - actor.Anchor.Y * scaleY * viewScale,
                image.Width * scaleX * viewScale, image.Height * scaleY * viewScale);
        }

        private Bitmap LoadActorPicture(int gfx, int direction, out string reason)
        {
            reason = null;
            if (gfx <= 0) { reason = "L'identifiant graphique de cet acteur n'a pas été transmis."; return null; }
            string preferred = direction == 2 ? "F" : direction == 3 || direction == 4 ? "L" : direction >= 5 ? "B" : "R";
            string[] alternatives = new[] { preferred, preferred == "L" ? "R" : preferred == "R" ? "L" : "", "F", "B", "S", "" }.Distinct().ToArray();
            foreach (string suffix in alternatives)
            {
                string path = Path.Combine(spriteDirectory, gfx + suffix + ".png");
                if (!File.Exists(path)) continue;
                try
                {
                    Bitmap image;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = Image.FromStream(stream)) image = new Bitmap(source);
                    if ((preferred == "L" && suffix == "R") || (preferred == "R" && suffix == "L")) image.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    if (suffix != preferred) reason = "Orientation " + direction + " : variante " + preferred + " absente, visuel " + (suffix.Length == 0 ? "principal" : suffix) + " utilisé.";
                    return image;
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                    || error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException)
                { reason = "Le PNG du sprite " + gfx + suffix + " est illisible."; }
            }
            if (reason == null) reason = "Le sprite " + gfx + " est absent du dossier des personnages/monstres.";
            return null;
        }

        private void DrawActor(Graphics graphics, ActorVisualState actor)
        {
            if (!actor.IsVisible) return;
            PointF center = actor.WorldPosition;
            using (var shadow = new SolidBrush(Color.FromArgb(70, 35, 32, 23))) graphics.FillEllipse(shadow, center.X - 12, center.Y - 4, 24, 8);
            if (actor.IsSelf)
                using (var pen = new Pen(Color.FromArgb(216, 190, 76), 2)) graphics.DrawEllipse(pen, center.X - 14, center.Y - 6, 28, 12);
            if (actor.HasSprite)
            {
                float sx = actor.ScaleX / 100f, sy = actor.ScaleY / 100f;
                graphics.DrawImage(actor.Image, new RectangleF(center.X - actor.Anchor.X * sx, center.Y - actor.Anchor.Y * sy,
                    actor.Image.Width * sx, actor.Image.Height * sy));
            }
            else
            {
                using (var body = new SolidBrush(actor.Color))
                {
                    graphics.FillEllipse(body, center.X - 6, center.Y - 28, 12, 12);
                    graphics.FillPolygon(body, new[] { new PointF(center.X, center.Y - 18), new PointF(center.X - 9, center.Y - 3), new PointF(center.X + 9, center.Y - 3) });
                }
                using (var pen = new Pen(Color.FromArgb(222, 153, 49), 2))
                {
                    graphics.DrawLine(pen, center.X - 4, center.Y - 18, center.X + 4, center.Y - 10);
                    graphics.DrawLine(pen, center.X + 4, center.Y - 18, center.X - 4, center.Y - 10);
                }
            }
        }

        private void SetActorOrientation(int id, int direction)
        {
            if (Account?.Game?.character?.id == id) Account.Game.character.Orientation = direction;
            else
            {
                var map = Account?.Game?.Map;
                if (map?.Entites == null || !map.Entites.TryGetValue(id, out Entites actor)) return;
                if (actor is PNJ npc) npc.Orientation = direction;
                else if (actor is Personnages player) player.Orientation = direction;
                else if (actor is Monstres monster) monster.Orientation = direction;
            }
        }
    }
}
