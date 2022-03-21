using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    class Animations : IDisposable
    {
        public int Entites_Id { get; private set; }
        public List<UserMapCell> Path { get; private set; }
        public PointF Actual_point { get; private set; }

        public AnimationType AnimationType { get; private set; }
        private int FrameIndex;
        private int TimePerFrame;
        private Timer timer;
        private List<PointF> Frames;

        public event Action<Animations> Finalize;

        public Animations(int E_ID, IEnumerable<UserMapCell> path, int duration, AnimationType AT)
        {
            Entites_Id = E_ID;
            Path = new List<UserMapCell>(path);
            AnimationType = AT;
            timer = new Timer(MakeAnimation, null, Timeout.Infinite, Timeout.Infinite);

            InitFrames();
            TimePerFrame = duration / Frames.Count;
            FrameIndex = 0;
        }

        private void InitFrames()
        {
            Frames = new List<PointF>();
            for (int i = 0; i < Path.Count - 1; i++)
                Frames.AddRange(Getpoints(Path[i].Centre, Path[i + 1].Centre, 3));
        }
        public void Init()
        {
            Actual_point = Frames[FrameIndex];
            timer.Change(TimePerFrame, TimePerFrame);
        }
        private PointF[]Getpoints(PointF p1, PointF p2, int P)
        {
            PointF[] points = new PointF[P];
            float Y_D = p2.Y - p1.Y, X_D = p2.X - p1.X;
            double slope = (double)(p2.Y - p1.Y) / (p2.X - p1.X);
            double x, y;

            P--;

            for(double i = 0; i < P; i++)
            {
                y = slope == 0 ? 0: Y_D * (i / P);
                x = slope == 0 ? X_D * (i / P) : y / slope;
                points[(int)i] = new PointF((float)Math.Round(x) + p1.X, (float)Math.Round(y) + p1.Y);
            }
            points[P] = p2;
            return points;
        }
        private void MakeAnimation(object state)
        {
            FrameIndex++;
            Actual_point = Frames[FrameIndex];
            if(FrameIndex == Frames.Count - 1)
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                Finalize?.Invoke(this);
            }
        }
        public void Dispose()
        {
            Path.Clear();
            timer.Dispose();

            Path = null;
            timer = null;
        }
    }
}
