using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    class CharacterFrame : Frame
    {
        [MessageAttribution("As")]
        public void ActualiseStats(TcpClient client, string message) => client.account.Game.character.RefreshCaracs(message);

        [MessageAttribution("PIK")]
        public Task GetGroup(TcpClient client, string message) => Task.Run(async () =>
        {
            if (client.account.UseMasterCommands == true)
            {
                if (client.account.HasGroup == true)
                {
                    await Task.Delay(1250);
                    await client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else if (client.account.IsGroupLeader == false)
                {
                    string PlayerWhoInvite = message.Substring(3).Split('|')[0];
                    Accounts Leader = client.account.Groupe.leader;
                    string LeaderName = Leader?.Game?.character?.Name;
                    if (string.IsNullOrEmpty(LeaderName)) { await client.SendPacket("PR"); return; }
                    if (PlayerWhoInvite.ToLower() == LeaderName.ToLower())
                    {

                        await Task.Delay(550);
                        await client.account.Connexion.SendPacket("PA");
                        client.account.Logger.LogInfo("GROUPE", $"Je suis maintenant dans le groupe de {LeaderName}");
                    }
                    else
                    {
                        await client.SendPacket("PR");
                        client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                    }

                }
                else if (message.Substring(3).Split('|').Length == 1)
                {
                    await Task.Delay(1250);
                    await client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                }
            }
            else
            {
                if (client.account.Game.character.InGroupe == true)
                {
                    await Task.Delay(1250);
                    await client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else
                {
                    await client.account.Connexion.SendPacket("PA");
                }
            }
        });
        [MessageAttribution("PCK")]
        public void AcceptGroupe(TcpClient client, string message) => client.account.Game.character.InGroupe = true;

        [MessageAttribution("PM")]
        public void InGroupParse(TcpClient client, string message)
        {
            CharacterClass character = client.account.Game.character;
            foreach (string entry in message.Substring(2).TrimStart('|').Split('|'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry[0] == '-')
                {
                    if (int.TryParse(entry.Substring(1), out int removedId)
                        && character.GroupMembers.TryRemove(removedId, out string removedName))
                        character.InEquip.TryRemove(removedName, out bool ignored);
                    continue;
                }
                string[] parts = entry.TrimStart('+', '~').Split(';');
                if (parts.Length < 2 || !int.TryParse(parts[0], out int memberId)) continue;
                character.GroupMembers[memberId] = parts[1];
                character.InEquip[parts[1]] = false;
                character.InGroupe = true;
            }
        }
        [MessageAttribution("PV")]
        public void EjectGroup(TcpClient client, string message)
        {
            client.account.Game.character.InEquip.Clear();
            client.account.Game.character.GroupMembers.Clear();
            client.account.Game.character.InGroupe = false;
            client.account.Logger.LogError("GROUPE", $"{client.account.Game.character.EquipLeader} vous a éjecté du groupe.");
            client.account.Game.character.EquipLeader = "";
        }
        [MessageAttribution("pong")]
        public void GetPingPong(TcpClient client, string message) => client.account.Logger.LogInfo("DOFUS", $"Ping: {client.GetPingAverage()} ms");

        [MessageAttribution("Bp")]
        public Task GetAllPing(TcpClient client, string message) => Task.Run(async () =>  await client.SendPacket($"Bp{client.GetPingAverage()}|{client.GetTotalPings()}|50"));

        [MessageAttribution("Ow")]

        public void GetPods(TcpClient client, string message)
        {
            string[] pods = message.Substring(2).Split('|');
            if (pods.Length < 2 || !int.TryParse(pods[0], out int actual_pods)
                || !int.TryParse(pods[1], out int Max_pods) || actual_pods < 0 || Max_pods < 0) return;
            CharacterClass perso = client.account.Game.character;

            perso.Inventory.Actual_pods = actual_pods;
            perso.Inventory.Pods_Max = Max_pods;
            client.account.Game.character.PodsRefreshEvent();
        }

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

        [MessageAttribution("Re")]
        public void GetInfoMonture(TcpClient client, string message) => client.account.CanUseMount = true;

        [MessageAttribution("OAKO")]
        public void GetObjects(TcpClient client, string message) => client.account.Game.character.Inventory.Add_Items(message.Substring(4));

        [MessageAttribution("OR")]
        public void EliminateObject(TcpClient client, string message) => client.account.Game.character.Inventory.SuppItem(uint.Parse(message.Substring(2)), 1, false);

        [MessageAttribution("OQ")]
        public void ModifyQuantityItems(TcpClient client, string message) => client.account.Game.character.Inventory.Modify_Items(message.Substring(2));

        [MessageAttribution("ECK")]
        public void GoInStorage(TcpClient client, string message) => client.account.AccountStates = AccountStates.STORAGE;

        [MessageAttribution("ERK")]
        public Task AskExchange(TcpClient client, string message) => Task.Run(async () =>
        {
            client.account.Logger.LogInfo("DOFUS", "Quelqu'un demande un échange");
            await client.SendPacket("EV", true);
        });

        [MessageAttribution("ILS")]
        public void GetRegenTime(TcpClient client, string message)
        {
            string cut = message.Substring(3);
            int time = int.Parse(cut);
            Accounts A = client.account;
            CharacterClass perso = A.Game.character;

            if(perso.stats.VitalityActual < perso.stats.MaxVitality)
            {
                perso.Regen_Timer.Change(Timeout.Infinite, Timeout.Infinite);
                perso.Regen_Timer.Change(time, time);
                perso.DisplayRegen();
                A.Logger.LogInfo("DOFUS", $"Votre personnage récupère 1 pdv chaque {time / 1000} secondes");
            }
        }

        [MessageAttribution("ILF")]
        public void GetLifeRegen(TcpClient client, string message)
        {
            string cut = message.Substring(3);
            int life = int.Parse(cut);
            Accounts A = client.account;
            CharacterClass perso = A.Game.character;

            perso.stats.VitalityActual += life;
            A.Logger.LogInfo("DOFUS", $"Vous avez récupéré {life} points de vie");
        }

        [MessageAttribution("eUK")]
        public void GetEmote(TcpClient client, string message)
        {
            string[] sep = message.Substring(3).Split('|');
            int id = int.Parse(sep[0]);
            int emote_id = int.Parse(sep[1]);
            Accounts A = client.account;

            if (A.Game.character.id != id)
                return;

            if (emote_id == 1 && A.AccountStates != AccountStates.REGENERATION)
                A.AccountStates = AccountStates.REGENERATION;
            else if (emote_id == 0 && A.AccountStates == AccountStates.REGENERATION)
                A.AccountStates = AccountStates.CONNECTED_INACTIVE;
        }

        [MessageAttribution("gJR")]
        public Task HandleGuild(TcpClient client, string message) => Task.Run(async () =>
        {
            if (client.account.Game.character.HasGuild == true)
            {
                await Task.Delay(100);
                client.account.Logger.LogInfo("PERSO", "Invitation à la guilde refusée");
                await client.SendPacket("gJE");
            }
        });


    }
}
