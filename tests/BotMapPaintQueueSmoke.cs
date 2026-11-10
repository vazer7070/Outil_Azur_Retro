using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;

// File de dessin partagée du fil d'interface : une carte animée dont le dessin dure plus que sa minuterie (33 ms) ne doit
// pas priver de dessin les autres contrôles. Sous Mono, sans l'acquittement de UserMapControl.RequestFrame, la carte gardait
// la tête de la file dès qu'une exposition non cliente l'attendait (ici : un redimensionnement pendant l'animation) et son
// voisin n'était plus jamais dessiné ; le scénario réel x7 échouait ainsi sur une machine lente. Le dessin de la carte est
// ralenti par un gestionnaire Paint de 60 ms, l'animation est une suite d'étapes minutées du séquenceur, et un fil poste des
// appels d'interface toutes les 5 ms comme le font les sessions du bot (sous Mono, la minuterie d'une fenêtre n'est servie
// entre deux dessins que si la file X n'est pas vide). Tout est piloté par une minuterie de la fenêtre :
// Application.DoEvents ne rend pas la main tant qu'une vue se redessine en boucle. Hors Mono, le test est sans objet
// (Windows sert WM_PAINT avant WM_TIMER) et réussit sans rien ouvrir.
internal static class BotMapPaintQueueSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try
        {
            if (Type.GetType("Mono.Runtime") == null) { Console.WriteLine("OK: not applicable outside Mono (Windows serves WM_PAINT before WM_TIMER)"); return; }
            string failure = null, summary = null;
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = Point.Empty, ClientSize = new Size(700, 420), Text = "paint-queue" })
            using (var view = new UserMapControl { Location = Point.Empty, Size = new Size(500, 400) })
            using (var neighbour = new Panel { Location = new Point(510, 0), Size = new Size(180, 400), BackColor = Color.Beige })
            using (var driver = new System.Windows.Forms.Timer { Interval = 50 })
            {
                int viewPaints = 0, neighbourPaints = 0, state = 0, mark = 0, seen = 0;
                Stopwatch total = Stopwatch.StartNew(), wait = new Stopwatch();
                view.Paint += (s, e) => { viewPaints++; Thread.Sleep(60); };
                neighbour.Paint += (s, e) => neighbourPaints++;
                form.Controls.Add(view); form.Controls.Add(neighbour);
                Action<string> stop = message => { failure = message; driver.Stop(); form.Close(); };
                driver.Tick += (s, e) =>
                {
                    if (total.ElapsedMilliseconds > 40000) { stop("Étape " + state + " jamais atteinte (carte " + viewPaints + " dessins, voisin " + neighbourPaints + ")"); return; }
                    switch (state)
                    {
                        case 0: // premier dessin des deux contrôles, puis 12 s d'animation
                            if (viewPaints == 0 || neighbourPaints == 0) return;
                            for (int i = 0; i < 24; i++) view.EnqueueVisual(1, VisualStep.Timed("attente" + i, null, 500));
                            mark = viewPaints; state = 1; return;
                        case 1: // la carte se redessine en boucle : exposition non cliente par un redimensionnement
                            if (viewPaints < mark + 3) return;
                            view.Width -= 4; mark = viewPaints; state = 2; return;
                        case 2: // quelques images plus tard, le voisin demande son dessin
                            if (viewPaints < mark + 3) return;
                            seen = neighbourPaints; mark = viewPaints; neighbour.Invalidate(); wait.Start(); state = 3; return;
                        case 3:
                            if (neighbourPaints > seen)
                            {
                                if (view.Sequencer.IsIdle) { stop("Animation finie avant le dessin du voisin : rien n'est prouvé"); return; }
                                summary = "voisin dessiné en " + wait.ElapsedMilliseconds + " ms, " + (viewPaints - mark) + " image(s) de la carte entre-temps";
                                stop(null);
                            }
                            else if (wait.ElapsedMilliseconds > 4000)
                                stop("Voisin jamais redessiné pendant l'animation de la carte : " + (viewPaints - mark) + " images de la carte en " + wait.ElapsedMilliseconds + " ms");
                            return;
                    }
                };
                form.Shown += (s, e) => driver.Start();
                Action nothing = () => { };
                using (new System.Threading.Timer(_ => { try { if (form.IsHandleCreated && !form.IsDisposed) form.BeginInvoke(nothing); } catch (Exception) { } }, null, 0, 5))
                    Application.Run(form);
            }
            if (failure != null) throw new Exception(failure);
            Console.WriteLine("OK: animated map slower than its 33 ms timer leaves the shared paint queue to its neighbour (" + summary + ")");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}
