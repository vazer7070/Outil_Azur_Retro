using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Groupes
{
    public class Groupe : IDisposable
    {
        private Regroupement Regroupement;
        private Dictionary<Accounts.Accounts, ManualResetEvent> IsMember;
        public Accounts.Accounts leader { get; private set; }
        public ObservableCollection<Accounts.Accounts> Membres { get; private set; }
        private bool disposed;

        public Groupe (Accounts.Accounts Leader)
        {
            leader = Leader;
            Regroupement = new Regroupement(this);
            IsMember = new Dictionary<Accounts.Accounts,ManualResetEvent>();
            Membres = new ObservableCollection<Accounts.Accounts>();

            leader.Groupe = this;
        }
        public void AddMembers(Accounts.Accounts member)
        {
            if (Membres.Count >= 7)
                return;
            member.Groupe = this;
            Membres.Add(member);
            IsMember.Add(member, new ManualResetEvent(false));
        }
        public void KickMember(Accounts.Accounts M) => Membres.Remove(M);

        public void ConnectAllAccounts() // à utiliser pour le multi-compte et/ou mûles
        {
            leader.Connect();
            foreach(Accounts.Accounts member in Membres)
                member.Connect();
        }
        public void DisconnectGroupe()
        {
            foreach(Accounts.Accounts member in Membres)
                member?.Disconnect();
        }

        public void Dispose()
        {
            
        }
    }
}
