using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using Outil_Azur_complet.maps;

namespace Tool_Editor.maps.data
{
    public class CellsData : ICell
    {
        private static int _sizeCell = 26;
        private static double _pourceTile = 1.0;
        private bool _disposed;
        private MapForm _parentForm;
        private Point[] _location;
        private bool _trigger;
        private string _triggerName;
        private bool _unWalk;
        private bool _io;
        private Image _gfx1;
        private Image _gfx2;
        private Image _gfx3;

        public static int SizeCell
        {
            get => _sizeCell;
            set
            {
                if (value > 0)
                    _sizeCell = value;
            }
        }

        public static double PourceTile
        {
            get => _pourceTile;
            set
            {
                if (value > 0)
                    _pourceTile = value;
            }
        }

        public int ID { get; set; }
        public Point[] Location
        {
            get => _location;
            set
            {
                _location = value;
                if (_location != null && _location.Length == 4)
                {
                    UpdateBounds();
                }
            }
        }

        public bool Trigger
        {
            get => _trigger;
            set
            {
                _trigger = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        public string TriggerName
        {
            get => _triggerName;
            set
            {
                _triggerName = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        public bool UnWalk
        {
            get => _unWalk;
            set
            {
                _unWalk = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        public bool IO
        {
            get => _io;
            set
            {
                _io = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        public Image GFX1
        {
            get => _gfx1;
            set
            {
                _gfx1 = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        public Image GFX2
        {
            get => _gfx2;
            set
            {
                _gfx2 = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        public Image GFX3
        {
            get => _gfx3;
            set
            {
                _gfx3 = value;
                if (_parentForm != null)
                {
                    _parentForm.Invalidate();
                }
            }
        }

        private Rectangle _bounds;

        public void New(MapForm parent)
        {
            _parentForm = parent;
        }

        public void JoinMap(MapForm parent)
        {
            _parentForm = parent;
        }

        private void UpdateBounds()
        {
            if (_location == null || _location.Length != 4) return;

            int minX = _location.Min(p => p.X);
            int minY = _location.Min(p => p.Y);
            int maxX = _location.Max(p => p.X);
            int maxY = _location.Max(p => p.Y);

            _bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        public void Draw_GFX1(Graphics g)
        {
            if (_disposed || g == null || _gfx1 == null || _location == null) return;

            try
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(_location);
                    g.SetClip(path);
                    
                    Rectangle drawRect = new Rectangle(
                        _location[3].X,  // Point D (gauche)
                        _location[0].Y,  // Point A (haut)
                        _location[1].X - _location[3].X,  // Largeur entre D et B
                        _location[2].Y - _location[0].Y   // Hauteur entre A et C
                    );
                    
                    g.DrawImage(_gfx1, drawRect);
                    g.ResetClip();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du dessin de GFX1: {ex.Message}");
            }
        }

        public void Draw_GFX2(Graphics g)
        {
            if (_disposed || g == null || _gfx2 == null || _location == null) return;

            try
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(_location);
                    g.SetClip(path);
                    
                    Rectangle drawRect = new Rectangle(
                        _location[3].X,  // Point D (gauche)
                        _location[0].Y,  // Point A (haut)
                        _location[1].X - _location[3].X,  // Largeur entre D et B
                        _location[2].Y - _location[0].Y   // Hauteur entre A et C
                    );
                    
                    g.DrawImage(_gfx2, drawRect);
                    g.ResetClip();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du dessin de GFX2: {ex.Message}");
            }
        }

        public void Draw_GFX3(Graphics g)
        {
            if (_disposed || g == null || _gfx3 == null || _location == null) return;

            try
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(_location);
                    g.SetClip(path);
                    
                    Rectangle drawRect = new Rectangle(
                        _location[3].X,  // Point D (gauche)
                        _location[0].Y,  // Point A (haut)
                        _location[1].X - _location[3].X,  // Largeur entre D et B
                        _location[2].Y - _location[0].Y   // Hauteur entre A et C
                    );
                    
                    g.DrawImage(_gfx3, drawRect);
                    g.ResetClip();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du dessin de GFX3: {ex.Message}");
            }
        }

        public void DrawIO(Graphics g)
        {
            if (_disposed || g == null || _location == null) return;

            try
            {
                using (var pen = new Pen(Color.Red, 2))
                {
                    g.DrawPolygon(pen, _location);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du dessin de l'IO: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _gfx1?.Dispose();
                _gfx2?.Dispose();
                _gfx3?.Dispose();
                _disposed = true;
            }
        }
    }
} 