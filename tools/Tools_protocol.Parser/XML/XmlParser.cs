using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Tools_protocol.Kryone.Database;

namespace Tools_protocol.Parser.XML
{
    public static class XmlParser
    {
        public static void ParseSQLToXML(string path, string type, bool ForBot = false)
        {
            switch (type)
            {
                case "Maps":
                    if (ForBot)
                    {
                        foreach (MapsList M in MapsList.AllMaps)
                        {
                            using (XmlWriter writer = XmlWriter.Create($"{path}{M.ID}.xml"))
                            {
                                writer.WriteStartElement("RECORD");
                                writer.WriteElementString("ID", $"{M.ID}");
                                writer.WriteElementString("LARGEUR", $"{M.Width}");
                                writer.WriteElementString("LONGUEUR", $"{M.Heigth}");
                                writer.WriteElementString("X", $"{M.MapPos.Split(',')[0]}");
                                writer.WriteElementString("Y", $"{M.MapPos.Split(',')[1]}");
                                writer.WriteElementString("MAP_DATA", $"{M.MapData}");
                                writer.WriteElementString("BACK", $"{M.BackGround}");
                                writer.WriteEndElement();
                                writer.Flush();
                            }

                        }
                    }
                    MessageBox.Show("La génération des maps est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "Objets":
                    if (ForBot)
                    {
                        foreach (ItemTemplateList T in ItemTemplateList.ItemFullDico.Values)
                        {
                            using (XmlWriter writer = XmlWriter.Create($"{path}{T.Id}.xml"))
                            {
                                writer.WriteStartElement("RECORD");
                                writer.WriteElementString("ID", $"{T.Id}");
                                writer.WriteElementString("TYPE", $"{T.Type}");
                                writer.WriteElementString("NOM", $"{T.Name}");
                                writer.WriteElementString("NIVEAU", $"{T.Level}");
                                writer.WriteElementString("PODS", $"{T.Pod}");
                                writer.WriteElementString("ETHERE", $"0");
                                writer.WriteElementString("CONDITIONS", $"{T.Conditions}");
                                writer.WriteElementString("STATS", $"{T.StatsTemplate}");
                                writer.WriteEndElement();
                                writer.Flush();
                            }
                        }
                    }
                    MessageBox.Show("La génération des objets est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "Métiers":
                    if (ForBot)
                    {
                        foreach(JobsList J in JobsList.AllJobs)
                        {
                            using (XmlWriter writer = XmlWriter.Create($"{path}{J.ID}.xml"))
                            {
                                writer.WriteStartElement("RECORD");
                                writer.WriteElementString("ID", $"{J.ID}");
                                writer.WriteElementString("NOM", $"{J.Name}");
                                writer.WriteElementString("TOOLS", $"{J.Tools}");
                                writer.WriteElementString("CRAFTS", $"{J.Crafts}");
                                writer.WriteElementString("SKILLS", $"{J.Skills}");
                                writer.WriteElementString("AP", $"{J.AP}");
                                writer.WriteEndElement();
                                writer.Flush();
                            }
                        }
                    }
                    MessageBox.Show("La génération des métiers est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "PNJs":
                    if (ForBot)
                    {
                        foreach(NPCList NPC in NPCList.AllPnj)
                        {
                            if(NPCList.PNJIdName.ContainsKey(NPC.NPCId.ToString()))
                            {
                                using (XmlWriter writer = XmlWriter.Create($"{path}{NPC.NPCId}.xml"))
                                {
                                    writer.WriteStartElement("RECORD");
                                    writer.WriteElementString("ID", $"{NPC.NPCId}");
                                    writer.WriteElementString("NOM", $"{NPCList.PNJIdName[NPC.NPCId.ToString()]}");
                                    writer.WriteElementString("MAP", $"{NPC.MapId}");
                                    writer.WriteElementString("CELLULE", $"{NPC.CellId}");
                                    writer.WriteElementString("ORIENTATION", $"{NPC.Orientation}");
                                    writer.WriteElementString("GFX", $"{NPCTemplateList.TemplatesPNJ[NPC.NPCId].GFXID}");
                                    writer.WriteElementString("SEXE", $"{NPCTemplateList.TemplatesPNJ[NPC.NPCId].Sexe}");
                                    writer.WriteEndElement();
                                    writer.Flush();
                                }
                            }
                        }
                    }
                     MessageBox.Show("La génération des PNJs est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "Zaaps":
                    if (ForBot)
                    {
                        foreach(ZaapsList Z in ZaapsList.AllZaaps)
                        {
                            using (XmlWriter writer = XmlWriter.Create($"{path}{Z.Mapid}.xml"))
                            {
                                writer.WriteStartElement("RECORD");
                                writer.WriteElementString("MAP", $"{Z.Mapid}");
                                writer.WriteElementString("CELLULE", $"{Z.Cellid}");
                                writer.WriteEndElement();
                                writer.Flush();
                            }
                        }
                    }
                    MessageBox.Show("La génération des Zaaps est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "Monstres":
                    if (ForBot)
                    {
                        foreach(MonsterList M in MonsterList.AllMonster.Values)
                        {
                            using (XmlWriter writer = XmlWriter.Create($"{path}{M.Id}.xml"))
                            {
                                writer.WriteStartElement("RECORD");
                                writer.WriteElementString("ID", $"{M.Id}");
                                writer.WriteElementString("NAME", $"{M.Name}");
                                writer.WriteElementString("GFX", $"{M.GFXid}");
                                writer.WriteEndElement();
                                writer.Flush();
                            }
                        }
                    }
                    MessageBox.Show("La génération des monstres est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
    }
}
