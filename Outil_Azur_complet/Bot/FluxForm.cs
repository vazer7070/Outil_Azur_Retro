using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Logger;
namespace Outil_Azur_complet.Bot
{
    public partial class FluxForm : Form
    {
        private readonly Accounts account;
        private TcpClient attached;
        public List<string> DebugMessages = new List<string>();
        private ListView packets;
        private RichTextBox details;
        private Label status;
        private TextBox filter;
        private bool paused;
        public FluxForm(Accounts value)
        {
            account=value;BuildLayout();_ = Handle;Attach();account.AccountStateEvent+=Attach;account.AccountDisconnectEvent+=Attach;FormClosed+=OnSessionClosed;
        }
        private void BuildLayout()
        {
            var root=BotUi.Window(this,"Bot · Journal des paquets","Diagnostic de la session du compte "+account.accountConfig.Account+". Les identifiants et tickets sont masqués.");
            var card=BotUi.Card("Échanges réseau","Le journal conserve les 300 derniers messages. Les filtres portent sur les préfixes, par exemple GDM ou GA.");
            var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3 };body.RowStyles.Add(new RowStyle(SizeType.Absolute,42));body.RowStyles.Add(new RowStyle(SizeType.Percent,65));body.RowStyles.Add(new RowStyle(SizeType.Percent,35));
            filter=BotUi.Input("FiltrePaquets");filter.TextChanged+=(s,e)=>RefreshRows();body.Controls.Add(filter,0,0);
            packets=BotUi.List("Date et sens","Paquet");packets.Columns[0].Width=160;packets.Columns[1].Width=750;packets.SelectedIndexChanged+=(s,e)=>ShowDetails();body.Controls.Add(packets,0,1);
            details=BotUi.Journal();body.Controls.Add(details,0,2);status=BotUi.Status("Journal actif.");
            var pause=BotUi.Button("Suspendre",(s,e)=> { paused=!paused;((Control)s).Text=paused?"Reprendre":"Suspendre";status.Text=paused?"Journal suspendu. La connexion reste active.":"Journal actif."; },false,130);
            BotUi.Body(card).Controls.Add(body);BotUi.Body(card).Controls.Add(status);BotUi.Body(card).Controls.Add(BotUi.Actions(pause,BotUi.Button("Effacer",(s,e)=> { DebugMessages.Clear();RefreshRows();details.Clear(); },false,100),BotUi.Button("Exporter",(s,e)=>Export(),false,110),BotUi.Button("Fermer",(s,e)=>Close(),false,100)));root.Controls.Add(card,0,1);
        }
        private void Attach()
        {
            BotUi.OnUi(this,()=> {
                if(ReferenceEquals(attached,account.Connexion))return;Detach();attached=account.Connexion;
                if(attached!=null) { attached.packetReceivedEvent+=PacketRecu;attached.packetSendEvent+=PacketSent;attached.socketInformationEvent+=GetSocketInfo; }
            });
        }
        private void Detach() { if(attached==null)return;attached.packetReceivedEvent-=PacketRecu;attached.packetSendEvent-=PacketSent;attached.socketInformationEvent-=GetSocketInfo;attached=null; }
        public void PacketRecu(string packet) => WritePacket(packet,false);
        public void PacketSent(string packet) => WritePacket(packet,true);
        private void GetSocketInfo(string value) { BotUi.OnUi(this,()=>status.Text=BotPacketRedactor.Redact(value,account)); }
        private void WritePacket(string packet,bool sent)
        {
            string safe=BotPacketRedactor.Redact(packet,account);string line=DateTime.Now.ToString("HH:mm:ss")+" "+(sent?"→ Envoi":"← Réception")+"\t"+safe;
            BotUi.OnUi(this,()=> { if(paused)return;if(DebugMessages.Count>=300)DebugMessages.RemoveAt(0);DebugMessages.Add(line);RefreshRows(); });
        }
        private void RefreshRows()
        {
            packets.BeginUpdate();packets.Items.Clear();foreach(var line in DebugMessages) { int tab=line.IndexOf('\t');string message=tab<0?line:line.Substring(tab+1);if(filter.Text.Length>0&&message.IndexOf(filter.Text,StringComparison.OrdinalIgnoreCase)<0)continue;var row=packets.Items.Add(tab<0?"":line.Substring(0,tab));row.SubItems.Add(message);row.Tag=message; }packets.EndUpdate();if(packets.Items.Count>0)packets.EnsureVisible(packets.Items.Count-1);
        }
        private void ShowDetails()
        {
            if(packets.SelectedItems.Count==0)return;string message=(string)packets.SelectedItems[0].Tag;details.Clear();var frame=MessagesReception.messagesDatas.OrderByDescending(x=>x.MessageName.Length).FirstOrDefault(x=>message.StartsWith(x.MessageName,StringComparison.Ordinal));
            if(frame!=null)BotUi.Append(details,"Préfixe reconnu : "+frame.MessageName);BotUi.Append(details,message);
        }
        private void Export()
        {
            using(var dialog=new SaveFileDialog { Filter="Journal texte (*.txt)|*.txt",FileName="Azur-bot-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt" })
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;try { File.WriteAllLines(dialog.FileName,DebugMessages,Encoding.UTF8);status.Text="Journal exporté."; }catch(Exception ex) { status.Text=ex.Message; }
            }
        }
        private void OnSessionClosed(object sender,FormClosedEventArgs args) { Detach();account.AccountStateEvent-=Attach;account.AccountDisconnectEvent-=Attach; }
    }
}
