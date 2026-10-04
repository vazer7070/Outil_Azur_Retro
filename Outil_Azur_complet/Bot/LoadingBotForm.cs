using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Session;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Spells;
namespace Outil_Azur_complet.Bot
{
    public partial class LoadingBotForm : Form
    {
        private Label status;
        private ProgressBar progress;
        private RichTextBox journal;
        private Control retry, proceed;
        private bool loading;
        private int failures;
        public LoadingBotForm()
        {
            var root=BotUi.Window(this,"Bot · Préparation","Chargement des ressources locales avant la connexion.");
            var card=BotUi.Card("Ressources du bot","L’absence d’une ressource est indiquée ci-dessous. La connexion reste disponible pour le diagnostic.");
            journal=BotUi.Journal();status=BotUi.Status("Préparation…");progress=new ProgressBar { Dock=DockStyle.Bottom,Height=18,Maximum=13 };
            retry=BotUi.Button("Retenter",async(s,e)=>await LoadDataAsync(),false,120);proceed=BotUi.Button("Ouvrir la connexion",(s,e)=>OpenLoginForm(),true,190);proceed.Enabled=false;
            BotUi.Body(card).Controls.Add(journal);BotUi.Body(card).Controls.Add(status);BotUi.Body(card).Controls.Add(progress);BotUi.Body(card).Controls.Add(BotUi.Actions(proceed,retry,BotUi.Button("Fermer",(s,e)=>Close(),false,100)));root.Controls.Add(card,0,1);
            Load+=async(s,e)=>await LoadDataAsync();
        }
        private async Task LoadDataAsync()
        {
            if(loading)return;loading=true;failures=0;retry.Enabled=proceed.Enabled=false;progress.Value=0;journal.Clear();
            try
            {
                foreach(var folder in new[] { "BotMaps","BotObjets","BotJobs","AccountSingle","BotZaaps","BotNPCs","BotMonsters","BotSorts","BotLang" })Directory.CreateDirectory(Path.Combine(".","ressources","Bot",folder));
                MessagesReception.Init();
                await LoadStep("Métiers",Jobs.LoadAllJobsAsync,()=>Jobs.AllJobs.Count);await LoadStep("Cartes",Map.LoadAllMapsAsync,()=>Map.AllBotMaps.Count);await LoadStep("Monstres",Monstres.LoadAllMonstersAsync,()=>Monstres.AllMonstersTemplate.Count);await LoadStep("Personnages non joueurs",PNJ.LoadAllNPCAsync,()=>PNJ.AllPNJ.Count);await LoadStep("Zaaps",Zaaps.LoadZaapsAsync,()=>Zaaps.Z.Count);await LoadStep("Objets",InventoryClass.LoadAllObjectsAsync,()=>InventoryObjects.FullInventory.Count);await LoadStep("Sorts",()=>Task.Run((Action)Spell.LoadAllSpells),()=>Spell.AllSpells.Count);
                await LoadStep("Objets interactifs",InteractivesParent.LoadAllInteractivesAsync,()=>InteractivesParent.Count);await LoadStep("Cellules déclencheurs",Triggers.LoadAllTriggersAsync,()=>Triggers.Count);await LoadStep("Zaapis",Zaaps.LoadZaapisAsync,()=>Zaaps.Zaapis.Count);await LoadStep("Panoplies",ItemSets.LoadAllItemSetsAsync,()=>ItemSets.Count);
                await LoadStep("Textes du client",LangData.LoadAsync,()=>LangData.EntryCount);
                ServerMessages.Resolver=(type,id,args)=> { string text=LangData.Text.Im(type,id,args); return string.IsNullOrEmpty(text)||(text.StartsWith("!")&&text.EndsWith("!"))?null:text; };
                if (IsDisposed || Disposing) return;
                foreach(var warning in Map.LoadWarnings) { failures++;BotUi.Append(journal,"Carte ignorée : "+warning); }
                foreach(var warning in InteractivesParent.LoadWarnings.Concat(Triggers.LoadWarnings).Concat(Zaaps.LoadWarnings).Concat(ItemSets.LoadWarnings).Take(20))BotUi.Append(journal,"Ressource du serveur : "+warning);
                foreach(var warning in LangData.LoadWarnings) { failures++;BotUi.Append(journal,"Textes du client : "+warning); }
                status.Text="Indexation des décors PNG…";
                await Outil_Azur_complet.Bot.Controls.BotMapArtwork.WarmupAsync();
                if (IsDisposed || Disposing) return;
                progress.Value=progress.Maximum;BotUi.Append(journal,"Décors PNG : index prêt. Les visuels manquants seront signalés sur chaque carte.");
                status.Text=failures==0?"Préparation terminée. Ouvrez la connexion pour choisir votre compte.":failures+" ressource(s) absente(s) ou invalide(s). Exportez les ressources depuis le parseur pour jouer ; le diagnostic réseau reste disponible.";
            }
            catch(Exception ex) { if(!IsDisposed&&!Disposing) { status.Text="La préparation n’est pas complète.";BotUi.Append(journal,ex.Message); } }
            finally { loading=false;if(!IsDisposed&&!Disposing)retry.Enabled=proceed.Enabled=true; }
        }
        private async Task LoadStep(string name,Func<Task> load,Func<int> count)
        {
            if(IsDisposed||Disposing)return;
            status.Text="Chargement : "+name+"…";
            try { await Task.Run(load);if(IsDisposed||Disposing)return;int loaded=count();BotUi.Append(journal,name+" : "+loaded+" élément(s) chargé(s)." );if(loaded==0)failures++; }
            catch(Exception ex) { if(IsDisposed||Disposing)return;failures++;BotUi.Append(journal,name+" : "+ex.Message); }
            if(!IsDisposed)progress.Value=Math.Min(progress.Maximum,progress.Value+1);
        }
        private void OpenLoginForm() { new LoginForm().Show();Close(); }
    }
}
