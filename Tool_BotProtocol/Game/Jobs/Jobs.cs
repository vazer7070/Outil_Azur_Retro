using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Jobs
{
    public class Jobs
    {
        public int ID { get; private set; }
         public int Level { get; set; }
        public string name { get; private set; }
        public uint BaseXP { get; private set; }
        public uint ActualXP { get; private set; }
        public uint NextXP { get; private set; }
        public List<JobSkills> Skills { get; private set; }
        public static ConcurrentDictionary<int, Jobs> AllJobs = new ConcurrentDictionary<int, Jobs>();

        static string Jobspath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotJobs");
        public static Task LoadAllJobsAsync()
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, Jobs>();
                foreach (string file in Directory.EnumerateFiles(Jobspath, "*.xml"))
                {
                    XElement xml = XElement.Load(file);
                    var job = new Jobs { ID = int.Parse(xml.Element("ID").Value), name = xml.Element("NOM").Value };
                    loaded[job.ID] = job;
                }
                AllJobs = loaded;
            });
        }

        public Jobs(int id = 0)
        {
            if(id != 0)
            {
                ID = id;
                name = AllJobs.TryGetValue(id, out Jobs template) ? template.name : "Métier " + id;
                Skills = new List<JobSkills>();
            }
        }
        public double GetXpPercentage => NextXP <= BaseXP ? (Level >= 100 ? 100 : 0)
            : Math.Round(Math.Max(0, Math.Min(100, ((double)ActualXP - BaseXP) / (NextXP - BaseXP) * 100)), 2);

        public void AcutalizeJob(int level, uint basexp, uint actualxp, uint nextxp)
        {
            Level = level;
            BaseXP = basexp;
            ActualXP = actualxp;
            NextXP = nextxp;
        }


    }

}
