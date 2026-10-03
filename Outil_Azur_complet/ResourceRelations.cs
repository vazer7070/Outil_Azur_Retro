using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using MySql.Data.MySqlClient;

namespace Outil_Azur_complet
{
    public static partial class ServerDataService
    {
        private static string Related(ServerDataSnapshot snapshot,string key)
        { string table; if(!snapshot.RelatedTables.TryGetValue(key,out table) || string.IsNullOrWhiteSpace(table))throw new InvalidOperationException("Configurez la table "+key+" avant de modifier les relations de cette ressource.");return ServerSql.Identifier(table); }
        private static bool Exists(MySqlConnection connection,MySqlTransaction transaction,string table,string column,object id)
        {using(var command=new MySqlCommand("SELECT 1 FROM "+table+" WHERE "+Column(column)+"=@id LIMIT 1 FOR UPDATE",connection,transaction)){command.Parameters.AddWithValue("@id",id);return command.ExecuteScalar()!=null;}}
        private static void CheckRelations(ServerDataSnapshot snapshot,DataRow row,MySqlConnection connection,MySqlTransaction transaction)
        {
            if((snapshot.Kind==ServerResourceKind.ItemTemplates || snapshot.Kind==ServerResourceKind.ItemSets) && row.RowState!=DataRowState.Added && row.RowState!=DataRowState.Deleted && !Equals(row["id"],row["id",DataRowVersion.Original]))throw new FormatException("L'identifiant d'une ressource existante ne peut pas changer. Créez une nouvelle fiche.");
            if(snapshot.Kind==ServerResourceKind.ItemSets)
            {
                string templates=Related(snapshot,"Template");ServerSql.RequireInnoDb(connection,snapshot.RelatedTables["Template"]);
                if(row.RowState!=DataRowState.Deleted)
                foreach(string id in Convert.ToString(row["items"]).Split(',').Where(id=>id!=""))
                using(var command=new MySqlCommand("SELECT `panoplie` FROM "+templates+" WHERE `id`=@id FOR UPDATE",connection,transaction))
                {command.Parameters.AddWithValue("@id",id);object owner=command.ExecuteScalar();if(owner==null)throw new FormatException("Le modèle d'objet "+id+" n'existe pas.");long set=Convert.ToInt64(owner);if(set>0 && set!=Convert.ToInt64(row["id"]))throw new InvalidOperationException("L'objet "+id+" appartient déjà à une autre panoplie. Retirez-le de cette panoplie avant de l'ajouter.");}
            }
            if(snapshot.Kind==ServerResourceKind.ItemTemplates && row.RowState!=DataRowState.Deleted && (row.RowState==DataRowState.Added || !Equals(row["panoplie"],row["panoplie",DataRowVersion.Original])))
            {
                long set=Convert.ToInt64(row["panoplie"]);if(set>0 && !Exists(connection,transaction,Related(snapshot,"panoplies"),"id",set))throw new FormatException("Cette panoplie n'existe pas.");
            }
            if(snapshot.Kind==ServerResourceKind.Crafts && row.RowState!=DataRowState.Deleted)
            {
                ServerSql.RequireInnoDb(connection,snapshot.RelatedTables["Template"]);
                string templates=Related(snapshot,"Template");
                if(!Exists(connection,transaction,templates,"id",row["id"]))throw new FormatException("L'objet fabriqué n'existe pas.");
                foreach(string ingredient in Convert.ToString(row["craft"]).Split(';').Where(value=>value!=""))if(!Exists(connection,transaction,templates,"id",ingredient.Split('*')[0]))throw new FormatException("Un ingrédient de la recette n'existe pas.");
            }
            if(snapshot.Kind==ServerResourceKind.Drops && row.RowState!=DataRowState.Deleted)
            {ServerSql.RequireInnoDb(connection,snapshot.RelatedTables["Template"],snapshot.RelatedTables["monstres"]);if(!Exists(connection,transaction,Related(snapshot,"Template"),"id",row["objectId"]) || !Exists(connection,transaction,Related(snapshot,"monstres"),"id",row["monsterId"]))throw new FormatException("Le monstre ou l'objet de ce butin n'existe pas.");}
            if(snapshot.Kind==ServerResourceKind.ItemTemplates && row.RowState==DataRowState.Deleted)
            {
                if(string.IsNullOrWhiteSpace(snapshot.WorldSchema) || string.IsNullOrWhiteSpace(snapshot.WorldItemTable))throw new InvalidOperationException("Connectez la base world sur le même serveur SQL pour vérifier les exemplaires avant de supprimer un modèle.");
                object id=row["id",DataRowVersion.Original];string items=ServerSql.Identifier(snapshot.WorldSchema)+"."+ServerSql.Identifier(snapshot.WorldItemTable);
                ServerSql.RequireInnoDbInSchema(connection,snapshot.WorldSchema,snapshot.WorldItemTable);
                if(Exists(connection,transaction,items,"template",id))throw new InvalidOperationException("Ce modèle possède encore des exemplaires. Migrez ou retirez ces objets avant de supprimer leur modèle.");
                string craft,drops;
                if(snapshot.RelatedTables.TryGetValue("crafts",out craft) && !string.IsNullOrWhiteSpace(craft))
                {
                    ServerSql.RequireInnoDb(connection,craft);
                    if(Exists(connection,transaction,ServerSql.Identifier(craft),"id",id))throw new InvalidOperationException("Retirez d'abord la recette de ce modèle.");
                    using(var command=new MySqlCommand("SELECT 1 FROM "+ServerSql.Identifier(craft)+" WHERE CONCAT(';',`craft`) LIKE @ingredient LIMIT 1 FOR UPDATE",connection,transaction)){command.Parameters.AddWithValue("@ingredient","%;"+id+"*%");if(command.ExecuteScalar()!=null)throw new InvalidOperationException("Ce modèle est utilisé comme ingrédient dans une recette.");}
                }
                if(snapshot.RelatedTables.TryGetValue("drops",out drops) && !string.IsNullOrWhiteSpace(drops)) { ServerSql.RequireInnoDb(connection,drops);if(Exists(connection,transaction,ServerSql.Identifier(drops),"objectId",id))throw new InvalidOperationException("Retirez d'abord les butins qui utilisent ce modèle."); }
            }
        }
        private static IEnumerable<string> RelationSql(ServerDataSnapshot snapshot,DataRow row)
        {
            if(snapshot.Kind==ServerResourceKind.ItemSets)
            {
                string templates=Related(snapshot,"Template"), id=Convert.ToInt64(row["id",row.RowState==DataRowState.Deleted?DataRowVersion.Original:DataRowVersion.Current]).ToString(CultureInfo.InvariantCulture);
                string[] ids=row.RowState==DataRowState.Deleted?new string[0]:Convert.ToString(row["items"]).Split(',').Where(value=>value!="").Select(value=>int.Parse(value).ToString(CultureInfo.InvariantCulture)).ToArray();
                yield return "UPDATE "+templates+" SET `panoplie`=-1 WHERE `panoplie`="+id+(ids.Length>0?" AND `id` NOT IN ("+string.Join(",",ids)+")":"");
                if(ids.Length>0)yield return "UPDATE "+templates+" SET `panoplie`="+id+" WHERE `id` IN ("+string.Join(",",ids)+")";
            }
            if(snapshot.Kind==ServerResourceKind.ItemTemplates && (row.RowState==DataRowState.Deleted || row.RowState==DataRowState.Added || !Equals(row["panoplie"],row["panoplie",DataRowVersion.Original])))
            {
                long id=Convert.ToInt64(row["id",row.RowState==DataRowState.Deleted?DataRowVersion.Original:DataRowVersion.Current]), old=row.RowState==DataRowState.Added?-1:Convert.ToInt64(row["panoplie",DataRowVersion.Original]), next=row.RowState==DataRowState.Deleted?-1:Convert.ToInt64(row["panoplie"]);
                if(old>0)yield return "UPDATE "+Related(snapshot,"panoplies")+" SET `items`=TRIM(BOTH ',' FROM REPLACE(CONCAT(',',REPLACE(`items`,' ',''),','),',"+id+",',',')) WHERE `id`="+old;
                if(next>0)yield return "UPDATE "+Related(snapshot,"panoplies")+" SET `items`=CONCAT_WS(',',NULLIF(TRIM(`items`),''),'"+id+"') WHERE `id`="+next+" AND FIND_IN_SET('"+id+"',REPLACE(`items`,' ',''))=0";
            }
        }
        private static void ApplyRelations(ServerDataSnapshot snapshot,DataRow row,MySqlConnection connection,MySqlTransaction transaction)
        {
            var sql=RelationSql(snapshot,row).ToArray();if(sql.Length==0)return;
            if(snapshot.Kind==ServerResourceKind.ItemTemplates)ServerSql.RequireInnoDb(connection,snapshot.RelatedTables["panoplies"]);
            foreach(string query in sql)using(var command=new MySqlCommand(query,connection,transaction))command.ExecuteNonQuery();
        }
    }
}
