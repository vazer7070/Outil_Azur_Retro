using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tool_Editor.maps.data;

namespace Outil_Azur_complet.maps
{
    internal static class MapSqlExport
    {
        internal static void Export(Map map,string path)
        {
            if(map==null || map.ID<=0 || map.Cells==null || map.Cells.Length!=Map.CellCount(map.Width,map.Height))throw new FormatException("La carte et ses dimensions sont invalides.");
            string data=string.Concat(map.Cells.Select(BuilderClass.GetCellData));map.SaveFightCell();
            var snapshot=ServerDataService.Load(ServerResourceKind.Maps);
            var matches=snapshot.Data.Rows.Cast<DataRow>().Where(row=>Convert.ToInt64(row["id"])==map.ID).ToArray();
            if(matches.Length>1)throw new InvalidOperationException("Plusieurs cartes portent cet identifiant. Corrigez la base avant l'export.");
            DataRow target=matches.SingleOrDefault() ?? ServerDataService.Add(snapshot);
            var values=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase){{"id",map.ID},{"date",map.DateMap},{"width",map.Width},{"heigth",map.Height},{"height",map.Height},{"places",map.fightPlaces},{"key",""},{"mapData",data},{"capabilities",map.Capabilities},{"mappos",map.X+","+map.Y+","+map.SubArea},{"numgroup",map.NbGroups},{"maxSize",map.GroupMaxSize}};
            foreach(var entry in values)if(target.Table.Columns.Contains(entry.Key))target[entry.Key]=entry.Value;
            ServerDataService.ExportSql(snapshot,path);
        }
    }
}
