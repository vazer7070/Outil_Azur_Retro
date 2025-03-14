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

        static string Jobspath = @".\ressources\Bot\BotJobs";
        public static async Task LoadAllJobsAsync()
        {
            try
            {
                DirectoryInfo jobFolder = new DirectoryInfo(Jobspath);
                List<Task> tasks = new List<Task>();

                foreach (FileInfo file in jobFolder.GetFiles())
                {
                    if (file.Exists)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            XElement xmlmap = await Task.Run(() => XElement.Load(file.FullName));
                            Jobs J = new Jobs()
                            {
                                ID = int.Parse(xmlmap.Element("ID").Value),
                                name = xmlmap.Element("NOM").Value
                            };
                            AllJobs.GetOrAdd(J.ID, J);
                        }));
                    }
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }

        public Jobs(int id = 0)
        {
            if(id != 0)
            {
                ID = id;
                name = AllJobs[id].name;
                Skills = new List<JobSkills>();
            }
        }
        public double GetXpPercentage => ActualXP == 0 ?0 :Math.Round((double)(ActualXP - BaseXP) / (NextXP - BaseXP) * 100, 2);

        public void AcutalizeJob(int level, uint basexp, uint actualxp, uint nextxp)
        {
            Level = level;
            BaseXP = basexp;
            ActualXP = actualxp;
            NextXP = nextxp;
        }


    }

}
