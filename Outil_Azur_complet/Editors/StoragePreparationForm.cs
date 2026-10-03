using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MySql.Data.MySqlClient;

namespace Outil_Azur_complet.Editors
{
    internal sealed class StoragePreparationForm : AzurEditorWindow
    {
        private readonly ServerDataSnapshot snapshot;
        private readonly ListBox list=EditorUi.List();
        private readonly Dictionary<string,string> engines=new Dictionary<string,string>();
        private Control prepare;
        private bool busy;
        internal StoragePreparationForm(ServerDataSnapshot snapshot) : base("Activer l'enregistrement sécurisé","Les anciennes bases Kryone utilisent parfois MyISAM. Cet outil prépare les tables nécessaires aux enregistrements groupés.",new Size(1060,740))
        {
            this.snapshot=snapshot;
            var guide=EditorUi.Label("La conversion en InnoDB conserve les lignes et les colonnes. Elle modifie durablement le stockage des tables affichées.\nArrêtez votre serveur de jeu et conservez une sauvegarde de la base avant de préparer ces tables.");guide.Dock=DockStyle.Top;guide.Height=75;Body.Controls.Add(list);Body.Controls.Add(guide);
            prepare=ActionButton("Préparer ces tables",async()=>await Prepare(),true,205);prepare.Enabled=false;
            ActionButton("Fermer",Close);Shown+=async(s,e)=>await ReadStorageAsync();
        }
        private IEnumerable<Tuple<string,string>> Tables()
        {
            string schema=new MySqlConnectionStringBuilder(snapshot.ConnectionString).Database;
            yield return Tuple.Create(schema,snapshot.Table);
            string[] keys=snapshot.Kind==ServerResourceKind.ItemSets?new[]{"Template"}:snapshot.Kind==ServerResourceKind.ItemTemplates?new[]{"panoplies","crafts","drops"}:snapshot.Kind==ServerResourceKind.Crafts?new[]{"Template"}:snapshot.Kind==ServerResourceKind.Drops?new[]{"Template","monstres"}:snapshot.Kind==ServerResourceKind.Npcs?new[]{"npc_template"}:new string[0];
            foreach(string key in keys){string table;if(snapshot.RelatedTables.TryGetValue(key,out table) && !string.IsNullOrWhiteSpace(table))yield return Tuple.Create(schema,table);}
            if(snapshot.Kind==ServerResourceKind.ItemTemplates && !string.IsNullOrWhiteSpace(snapshot.WorldSchema) && !string.IsNullOrWhiteSpace(snapshot.WorldItemTable))yield return Tuple.Create(snapshot.WorldSchema,snapshot.WorldItemTable);
        }
        private async Task ReadStorageAsync()
        {
            busy=true;
            try
            {
                var loaded=await Task.Run(()=>{var result=new Dictionary<string,string>();using(var connection=new MySqlConnection(snapshot.ConnectionString)){connection.Open();foreach(var table in Tables().Distinct()){ServerSql.Identifier(table.Item1);ServerSql.Identifier(table.Item2);using(var command=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table",connection)){command.Parameters.AddWithValue("@schema",table.Item1);command.Parameters.AddWithValue("@table",table.Item2);result[table.Item1+"."+table.Item2]=Convert.ToString(command.ExecuteScalar());}}}return result;});
                engines.Clear();list.Items.Clear();foreach(var entry in loaded){engines[entry.Key]=entry.Value;list.Items.Add(entry.Key+" · "+(entry.Value=="InnoDB"?"Prête pour l'enregistrement":entry.Value=="MyISAM"?"À préparer (MyISAM)":"Stockage non pris en charge : "+entry.Value));}
                prepare.Enabled=engines.Values.Any(value=>value=="MyISAM") && engines.Values.All(value=>value=="MyISAM" || value=="InnoDB");SetStatus("Vérifiez les tables affichées avant de lancer leur préparation.");
            }catch(Exception error){ShowError(error);}finally{busy=false;}
        }
        private async Task Prepare()
        {
            busy=true;prepare.Enabled=false;
            try{await Task.Run(()=>{using(var connection=new MySqlConnection(snapshot.ConnectionString)){connection.Open();foreach(var entry in engines.Where(entry=>entry.Value=="MyISAM")){string[] target=entry.Key.Split('.');using(var command=new MySqlCommand("ALTER TABLE "+ServerSql.Identifier(target[0])+"."+ServerSql.Identifier(target[1])+" ENGINE=InnoDB",connection)){command.CommandTimeout=300;command.ExecuteNonQuery();}}}});await ReadStorageAsync();SetStatus("Tables préparées. Vous pouvez maintenant enregistrer dans l'éditeur.");}
            catch(Exception error){ShowError(error);await ReadStorageAsync();SetStatus("Préparation interrompue. L'état réel des tables est affiché ; les conversions terminées sont conservées.");}
            finally{busy=false;}
        }
        protected override void OnFormClosing(FormClosingEventArgs e){if(busy)e.Cancel=true;base.OnFormClosing(e);}
    }
}
