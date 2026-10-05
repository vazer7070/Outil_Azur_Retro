using System;
using System.Globalization;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Métiers : compétences <c>JS</c> et expérience <c>JX</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot F6.
    /// </summary>
    internal class JobsFrame : Frame
    {
        [MessageAttribution("JS")]
        public void GetJobsSkills(TcpClient client, string message)
        {
            string[] separador_skill;
            CharacterClass perso = client.account.Game.character;
            Jobs job;
            JobSkills skilljobs = null;
            short Id_jobs, Id_skills;
            byte Min, Max;
            float Time;

            lock (perso.Jobs)
            foreach(string data in message.Substring(3).Split('|'))
            {
                string[] jobParts = data.Split(';');
                if (jobParts.Length < 2 || !short.TryParse(jobParts[0], out Id_jobs)) continue;
                job = perso.Jobs.Find(x => x.ID == Id_jobs);

                if (job == null)
                {
                    job = perso.Jobs.Find(x => x.ID == Id_jobs);
                    job = new Jobs(Id_jobs);
                    perso.Jobs.Add(job);
                }


                foreach (string skill in jobParts[1].Split(','))
                {
                    separador_skill = skill.Split('~');
                    if (separador_skill.Length < 5 || !short.TryParse(separador_skill[0], out Id_skills)
                        || !byte.TryParse(separador_skill[1], out Min) || !byte.TryParse(separador_skill[2], out Max)
                        || !float.TryParse(separador_skill[4], NumberStyles.Float, CultureInfo.InvariantCulture, out Time)) continue;
                    skilljobs = job.Skills.Find(x => x.Id == Id_skills);

                    if (skilljobs != null)
                        skilljobs.Actualise(Id_skills, Min, Max, Time);
                    else
                        job.Skills.Add(new JobSkills(Id_skills, Min, Max, Time));
                }
            }
            perso.JobsRefreshEvent();
        }
        [MessageAttribution("JX")]
        public void GetExpInJob(TcpClient client, string message)
        {
            string pre_cut = message.Substring(3);
            string[] separate_jobs_Exp = pre_cut.Split('|');
            CharacterClass perso = client.account.Game.character;
            uint actualExp, baseExp, nextlevelExp;
            short Id;
            byte level;

            lock (perso.Jobs)
            foreach (string jobs in separate_jobs_Exp)
            {
                var payload = jobs.Split(';');
                if (payload.Length < 4)
                    continue;
                if (!short.TryParse(payload[0], out Id) || !byte.TryParse(payload[1], out level)
                    || !uint.TryParse(payload[2], out baseExp) || !uint.TryParse(payload[3], out actualExp)) continue;

                if (level < 100 && payload.Length >= 5 && uint.TryParse(payload[4], out nextlevelExp)) { }
                else
                    nextlevelExp = 0;
                Jobs job = perso.Jobs.Find(x => x.ID == Id);
                if (job == null) { job = new Jobs(Id); perso.Jobs.Add(job); }
                job.AcutalizeJob(level, baseExp, actualExp, nextlevelExp);
            }
            perso.JobsRefreshEvent();
        }
    }
}
