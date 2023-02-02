using System;
using System.Collections.Generic;
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
        public void GetGroup(TcpClient client, string message)
        {
           if(client.account.UseMasterCommands == true)
            {
                if (client.account.HasGroup == true)
                {
                    Task.Delay(1250);
                    client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else if (client.account.IsGroupLeader == false)
                {
                    string PlayerWhoInvite = message.Substring(3).Split('|')[0];
                    Accounts Leader = client.account.Groupe.leader;
                    string LeaderName = Leader.Game.character.Name;
                    if (PlayerWhoInvite.ToLower() == LeaderName.ToLower())
                    {

                        Task.Delay(550);
                        client.account.Connexion.SendPacket("PA");
                        client.account.Logger.LogInfo("GROUPE", $"Je suis maintenant dans le groupe de {LeaderName}");
                    }
                    else
                    {
                        client.SendPacket("PR");
                        client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                    }

                }
                else if (message.Substring(3).Split('|').Length == 1)
                {
                    Task.Delay(1250);
                    client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                }
            }
            else
            {
                if (client.account.Game.character.InGroupe == true)
                {
                    Task.Delay(1250);
                    client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else
                {
                    client.account.Connexion.SendPacket("PA");
                }
            }
        }
        [MessageAttribution("PCK")]
        public void AcceptGroupe(TcpClient client, string message) => client.account.Game.character.InGroupe = true;

        [MessageAttribution("PM")]
        public void InGroupParse(TcpClient client, string message)
        {
            string chief = "";
            string member = "";
            if (client.account.Game.character.InGroupe == true && client.account.Game.character.InEquip.Count > 0)
            {
                foreach(string l in message.Split('|'))
                {
                    string w = l.Split(';')[1];
                    member = w;
                    if (!client.account.Game.character.InEquip.ContainsKey(l))
                        client.account.Game.character.InEquip.TryAdd(l, false);
                }
                client.account.Logger.LogError("GROUPE", $"Ajout de {member} dans le groupe.");
            }
            else
            {
                foreach (string s in message.Split('|'))
                {
                    string j = s.Split(';')[1];
                    if (client.account.Game.character.InEquip.Count > 0)
                        client.account.Game.character.InEquip.TryAdd(j, false);
                    else
                    {
                        client.account.Game.character.InEquip.TryAdd(j, true);
                        chief = j;
                        client.account.Game.character.EquipLeader = chief;
                    }
                }
                client.account.Logger.LogError("GROUPE", $"Vous êtes dans le groupe de {chief}.");
            }
        }
        [MessageAttribution("PV")]
        public void EjectGroup(TcpClient client, string message)
        {
            client.account.Game.character.InEquip.Clear();
            client.account.Game.character.InGroupe = false;
            client.account.Logger.LogError("GROUPE", $"{client.account.Game.character.EquipLeader} vous a éjecté du groupe.");
            client.account.Game.character.EquipLeader = "";
        }
        [MessageAttribution("pong")]
        public void GetPingPong(TcpClient client, string message) => client.account.Logger.LogInfo("DOFUS", $"Ping: {client.GetPing()} ms");

        [MessageAttribution("Bp")]
        public void GetAllPing(TcpClient client, string message) => client.SendPacket($"Bp{client.GetPingAverage()}|{client.GetTotalPings()}|50");

        [MessageAttribution("Ow")]

        public void GetPods(TcpClient client, string message)
        {
            string[] pods = message.Substring(2).Split('|');
            short actual_pods = short.Parse(pods[0]);
            short Max_pods = short.Parse(pods[1]);
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

            foreach(string data in message.Substring(3).Split('|'))
            {
                Id_jobs = short.Parse(data.Split(';')[0]);
                job = perso.Jobs.Find(x => x.ID == Id_jobs);

                if (job == null)
                {
                    job = perso.Jobs.Find(x => x.ID == Id_jobs);
                    job = new Jobs(Id_jobs);
                    perso.Jobs.Add(job);
                }


                foreach (string skill in data.Split(';')[1].Split(','))
                {
                    separador_skill = skill.Split('~');
                    Id_skills = short.Parse(separador_skill[0]);
                    Min = byte.Parse(separador_skill[1]);
                    Max = byte.Parse(separador_skill[2]);
                    Time = float.Parse(separador_skill[4]);
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

            foreach (string jobs in separate_jobs_Exp)
            {
                var payload = jobs.Split(';');
                if (payload.Length < 4)
                    continue;
                Id = short.Parse(payload[0]);
                level = byte.Parse(payload[1]);
                baseExp = uint.Parse(payload[2]);
                actualExp = uint.Parse(payload[3]);

                if (level < 100 && payload.Length >= 4)
                    nextlevelExp = uint.Parse(jobs.Split(';')[4]);
                else
                    nextlevelExp = 0;
                perso.Jobs.Find(x => x.ID == Id).AcutalizeJob(level, baseExp, actualExp, nextlevelExp);
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

        [MessageAttribution("PCK")]
        public void AcceptGroup(TcpClient client, string message) => client.account.Game.character.InGroupe = true;

        [MessageAttribution("PV")]
        public void AbandonGroup(TcpClient client, string message) => client.account.Game.character.InGroupe = false;

        [MessageAttribution("ERK")]
        public void AskExchange(TcpClient client, string message)
        {
            client.account.Logger.LogInfo("DOFUS", "Quelqu'un demande un échange");
            client.SendPacket("EV", true);
        }

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
        public void HandleGuild(TcpClient client, string message)
        {
            if(client.account.Game.character.HasGuild == true)
            {
                Task.Delay(100);
                client.account.Logger.LogInfo("PERSO", "Invitation à la guilde refusée");
                client.SendPacket("gJE");
            }
        }


    }
}
