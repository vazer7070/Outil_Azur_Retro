﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Tools_protocol.Kryone.Database;

namespace Tools_protocol.Parser.XML
{
    public static class XmlParser
    {
        public static List<XElement> NormEffects = new List<XElement>();
        public static List<XElement> CritEffects = new List<XElement>();
        public static List<XElement> NormEffects2 = new List<XElement>();
        public static List<XElement> CritEffects2 = new List<XElement>();
        public static List<XElement> NormEffects3 = new List<XElement>();
        public static List<XElement> CritEffects3 = new List<XElement>();
        public static List<XElement> NormEffects4 = new List<XElement>();
        public static List<XElement> CritEffects4 = new List<XElement>();
        public static List<XElement> NormEffects5 = new List<XElement>();
        public static List<XElement> CritEffects5 = new List<XElement>();
        public static List<XElement> NormEffects6 = new List<XElement>();
        public static List<XElement> CritEffects6 = new List<XElement>();

        public static Task ParseSQLToXML(string path, string type, bool ForBot = false) => Task.Factory.StartNew(() =>
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
                case "Sorts":
                    if (ForBot)
                    {

                            foreach (SpellsList spells in SpellsList.AllSpells.Values)
                            {
                                if (SpellsList.EffectLvl1 != null)
                                    SpellsList.EffectLvl1.Clear();
                                if (SpellsList.EffectLvl2 != null)
                                    SpellsList.EffectLvl2.Clear();
                                if (SpellsList.EffectLvl3 != null)
                                    SpellsList.EffectLvl3.Clear();
                                if (SpellsList.EffectLvl4 != null)
                                    SpellsList.EffectLvl4.Clear();
                                if (SpellsList.EffectLvl5 != null)
                                    SpellsList.EffectLvl5.Clear();
                                if (SpellsList.EffectLvl6 != null)
                                    SpellsList.EffectLvl6.Clear();

                                string pa = "";
                                string pomin = "";
                                string pomax = "";
                                string line = "";
                                string seeline = "";
                                string empty = "";
                                string modif = "";
                                string turn = "";
                                string obj = "";
                                string interval = "";
                                string zone = "";
                                List<string> L = new List<string>();
                                List<string> LC = new List<string>();
                                SpellsBrain SB = new SpellsBrain();

                                SpellsList.ParseLevel(spells.Id, true);

                            if (!File.Exists($"{path}{spells.Id}.xml"))
                                using (XmlWriter writer = XmlWriter.Create($"{path}{spells.Id}.xml"))
                                {
                                    writer.WriteStartElement("SORTS");
                                    XElement sort =
                                        new XElement("SORT", new XAttribute("ID", $"{spells.Id}"));
                                    sort.WriteTo(writer);
                                    writer.WriteElementString("NOM", spells.Nom);


                                    foreach (string h in SpellsList.ParsedSPells.Keys)
                                    {
                                        SB = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "1").Value;
                                        pa = SB.PA;
                                        pomin = SB.PO.Split('à')[0].Trim();
                                        pomax = SB.PO.Split('à')[1].Trim();
                                        line = SB.LINE;
                                        seeline = SB.LINE_SEE;
                                        empty = SB.EMPTY_CELL;
                                        modif = SB.PMODIF;
                                        turn = SB.EC_TURN;
                                        obj = SB.NBL.Trim();
                                        interval = SB.INTERVAL.Trim();
                                        zone = SB.Zone;
                                        L = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "1").Value.EFFECT;
                                        foreach (string n_e in L)
                                            NormEffects.Add(new XElement("EFFETS", new XAttribute("TYPE", n_e.Split('|')[0]), new XAttribute("COOLDOWN", n_e.Split('|')[1]), new XAttribute("BUT", n_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", n_e.Split('|')[3])));
                                        L.Clear();
                                        LC = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "1").Value.EFFECT_CRIT;
                                        foreach (string c_e in LC)
                                            CritEffects.Add(new XElement("EFFETS", new XAttribute("TYPE", c_e.Split('|')[0]), new XAttribute("COOLDOWN", c_e.Split('|')[1]), new XAttribute("BUT", c_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", c_e.Split('|')[3].Trim().Split('?')[0])));
                                        LC.Clear();
                                        SB = null;

                                        SB = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "2").Value;
                                        pa = SB.PA;
                                        pomin = SB.PO.Split('à')[0].Trim();
                                        pomax = SB.PO.Split('à')[1].Trim();
                                        line = SB.LINE;
                                        seeline = SB.LINE_SEE;
                                        empty = SB.EMPTY_CELL;
                                        modif = SB.PMODIF;
                                        turn = SB.EC_TURN;
                                        obj = SB.NBL.Trim();
                                        interval = SB.INTERVAL.Trim();
                                        zone = SB.Zone;
                                        L = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "2").Value.EFFECT;
                                        foreach (string n_e in L)
                                            NormEffects2.Add(new XElement("EFFETS", new XAttribute("TYPE", n_e.Split('|')[0]), new XAttribute("COOLDOWN", n_e.Split('|')[1]), new XAttribute("BUT", n_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", n_e.Split('|')[3])));
                                        L.Clear();
                                        LC = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "2").Value.EFFECT_CRIT;
                                        foreach (string c_e in LC)
                                            CritEffects2.Add(new XElement("EFFETS", new XAttribute("TYPE", c_e.Split('|')[0]), new XAttribute("COOLDOWN", c_e.Split('|')[1]), new XAttribute("BUT", c_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", c_e.Split('|')[3].Trim().Split('?')[0])));
                                        LC.Clear();
                                        SB = null;

                                        SB = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "3").Value;
                                        pa = SB.PA;
                                        pomin = SB.PO.Split('à')[0].Trim();
                                        pomax = SB.PO.Split('à')[1].Trim();
                                        line = SB.LINE;
                                        seeline = SB.LINE_SEE;
                                        empty = SB.EMPTY_CELL;
                                        modif = SB.PMODIF;
                                        turn = SB.EC_TURN;
                                        obj = SB.NBL.Trim();
                                        interval = SB.INTERVAL.Trim();
                                        zone = SB.Zone;
                                        L = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "3").Value.EFFECT;
                                        foreach (string n_e in L)
                                            NormEffects3.Add(new XElement("EFFETS", new XAttribute("TYPE", n_e.Split('|')[0]), new XAttribute("COOLDOWN", n_e.Split('|')[1]), new XAttribute("BUT", n_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", n_e.Split('|')[3])));
                                        L.Clear();
                                        LC = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "3").Value.EFFECT_CRIT;
                                        foreach (string c_e in LC)
                                            CritEffects3.Add(new XElement("EFFETS", new XAttribute("TYPE", c_e.Split('|')[0]), new XAttribute("COOLDOWN", c_e.Split('|')[1]), new XAttribute("BUT", c_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", c_e.Split('|')[3].Trim().Split('?')[0])));
                                        LC.Clear();
                                        SB = null;

                                        SB = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "4").Value;
                                        pa = SB.PA;
                                        pomin = SB.PO.Split('à')[0].Trim();
                                        pomax = SB.PO.Split('à')[1].Trim();
                                        line = SB.LINE;
                                        seeline = SB.LINE_SEE;
                                        empty = SB.EMPTY_CELL;
                                        modif = SB.PMODIF;
                                        turn = SB.EC_TURN;
                                        obj = SB.NBL.Trim();
                                        interval = SB.INTERVAL.Trim();
                                        zone = SB.Zone;
                                        L = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "4").Value.EFFECT;
                                        foreach (string n_e in L)
                                            NormEffects4.Add(new XElement("EFFETS", new XAttribute("TYPE", n_e.Split('|')[0]), new XAttribute("COOLDOWN", n_e.Split('|')[1]), new XAttribute("BUT", n_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", n_e.Split('|')[3])));
                                        L.Clear();
                                        LC = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "4").Value.EFFECT_CRIT;
                                        foreach (string c_e in LC)
                                            CritEffects4.Add(new XElement("EFFETS", new XAttribute("TYPE", c_e.Split('|')[0]), new XAttribute("COOLDOWN", c_e.Split('|')[1]), new XAttribute("BUT", c_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", c_e.Split('|')[3].Trim().Split('?')[0])));
                                        LC.Clear();
                                        SB = null;

                                        SB = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "5").Value;
                                        pa = SB.PA;
                                        pomin = SB.PO.Split('à')[0].Trim();
                                        pomax = SB.PO.Split('à')[1].Trim();
                                        line = SB.LINE;
                                        seeline = SB.LINE_SEE;
                                        empty = SB.EMPTY_CELL;
                                        modif = SB.PMODIF;
                                        turn = SB.EC_TURN;
                                        obj = SB.NBL.Trim();
                                        interval = SB.INTERVAL.Trim();
                                        zone = SB.Zone;
                                        L = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "5").Value.EFFECT;
                                        foreach (string n_e in L)
                                            NormEffects5.Add(new XElement("EFFETS", new XAttribute("TYPE", n_e.Split('|')[0]), new XAttribute("COOLDOWN", n_e.Split('|')[1]), new XAttribute("BUT", n_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", n_e.Split('|')[3])));
                                        L.Clear();
                                        LC = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "5").Value.EFFECT_CRIT;
                                        foreach (string c_e in LC)
                                            CritEffects5.Add(new XElement("EFFETS", new XAttribute("TYPE", c_e.Split('|')[0]), new XAttribute("COOLDOWN", c_e.Split('|')[1]), new XAttribute("BUT", c_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", c_e.Split('|')[3].Trim().Split('?')[0])));
                                        LC.Clear();
                                        SB = null;

                                        SB = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "6").Value;
                                        pa = SB.PA;
                                        pomin = SB.PO.Split('à')[0].Trim();
                                        pomax = SB.PO.Split('à')[1].Trim();
                                        line = SB.LINE;
                                        seeline = SB.LINE_SEE;
                                        empty = SB.EMPTY_CELL;
                                        modif = SB.PMODIF;
                                        turn = SB.EC_TURN;
                                        obj = SB.NBL.Trim();
                                        interval = SB.INTERVAL.Trim();
                                        zone = SB.Zone;
                                        L = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "6").Value.EFFECT;
                                        foreach (string n_e in L)
                                            NormEffects6.Add(new XElement("EFFETS", new XAttribute("TYPE", n_e.Split('|')[0]), new XAttribute("COOLDOWN", n_e.Split('|')[1]), new XAttribute("BUT", n_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", n_e.Split('|')[3])));
                                        L.Clear();
                                        LC = SpellsList.ParsedSPells.FirstOrDefault(x => x.Key.Split('|')[0] == spells.Id.ToString() && x.Key.Split('|')[2] == "6").Value.EFFECT_CRIT;
                                        foreach (string c_e in LC)
                                            CritEffects6.Add(new XElement("EFFETS", new XAttribute("TYPE", c_e.Split('|')[0]), new XAttribute("COOLDOWN", c_e.Split('|')[1]), new XAttribute("BUT", c_e.Split('|')[2]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", c_e.Split('|')[3].Trim().Split('?')[0])));
                                        LC.Clear();
                                        SB = null;
                                    }

                                    XElement level1 = new XElement("NIVEAU", new XAttribute("NIVEAU", "1"), new XAttribute("PA", pa), new XAttribute("MIN_RANGE", pomin), new XAttribute("MAX_RANGE", pomax), new XAttribute("LIGNE", line), new XAttribute("LIGNE_DE_VUE", seeline), new XAttribute("NEED_EMPTY_CELL", empty), new XAttribute("MODIF", modif), new XAttribute("PER_TURN", turn), new XAttribute("PER_OBJECTIVE", obj), new XAttribute("INTERVAL", interval));
                                    level1.WriteTo(writer);
                                    foreach (XElement t in NormEffects)
                                        t.WriteTo(writer);
                                    //  foreach (XElement x in CritEffects)
                                    //     x.WriteTo(writer);
                                    NormEffects.Clear();
                                    CritEffects.Clear();
                                    level1 = null;

                                    XElement level2 = new XElement("NIVEAU", new XAttribute("NIVEAU", "2"), new XAttribute("PA", pa), new XAttribute("MIN_RANGE", pomin), new XAttribute("MAX_RANGE", pomax), new XAttribute("LIGNE", line), new XAttribute("LIGNE_DE_VUE", seeline), new XAttribute("NEED_EMPTY_CELL", empty), new XAttribute("MODIF", modif), new XAttribute("PER_TURN", turn), new XAttribute("PER_OBJECTIVE", obj), new XAttribute("INTERVAL", interval));
                                    level2.WriteTo(writer);
                                    foreach (XElement t in NormEffects2)
                                        t.WriteTo(writer);
                                    //  foreach (XElement x in CritEffects2)
                                    //      x.WriteTo(writer);
                                    NormEffects2.Clear();
                                    CritEffects2.Clear();
                                    level2 = null;

                                    XElement level3 = new XElement("NIVEAU", new XAttribute("NIVEAU", "3"), new XAttribute("PA", pa), new XAttribute("MIN_RANGE", pomin), new XAttribute("MAX_RANGE", pomax), new XAttribute("LIGNE", line), new XAttribute("LIGNE_DE_VUE", seeline), new XAttribute("NEED_EMPTY_CELL", empty), new XAttribute("MODIF", modif), new XAttribute("PER_TURN", turn), new XAttribute("PER_OBJECTIVE", obj), new XAttribute("INTERVAL", interval));
                                    level3.WriteTo(writer);
                                    foreach (XElement t in NormEffects3)
                                        t.WriteTo(writer);
                                    //  foreach (XElement x in CritEffects3)
                                    //      x.WriteTo(writer);
                                    NormEffects3.Clear();
                                    CritEffects3.Clear();
                                    level3 = null;

                                    XElement level4 = new XElement("NIVEAU", new XAttribute("NIVEAU", "4"), new XAttribute("PA", pa), new XAttribute("MIN_RANGE", pomin), new XAttribute("MAX_RANGE", pomax), new XAttribute("LIGNE", line), new XAttribute("LIGNE_DE_VUE", seeline), new XAttribute("NEED_EMPTY_CELL", empty), new XAttribute("MODIF", modif), new XAttribute("PER_TURN", turn), new XAttribute("PER_OBJECTIVE", obj), new XAttribute("INTERVAL", interval));
                                    level4.WriteTo(writer);
                                    foreach (XElement t in NormEffects4)
                                        t.WriteTo(writer);
                                    // foreach (XElement x in CritEffects4)
                                    //   x.WriteTo(writer);
                                    NormEffects4.Clear();
                                    CritEffects4.Clear();
                                    level4 = null;

                                    XElement level5 = new XElement("NIVEAU", new XAttribute("NIVEAU", "5"), new XAttribute("PA", pa), new XAttribute("MIN_RANGE", pomin), new XAttribute("MAX_RANGE", pomax), new XAttribute("LIGNE", line), new XAttribute("LIGNE_DE_VUE", seeline), new XAttribute("NEED_EMPTY_CELL", empty), new XAttribute("MODIF", modif), new XAttribute("PER_TURN", turn), new XAttribute("PER_OBJECTIVE", obj), new XAttribute("INTERVAL", interval));
                                    level5.WriteTo(writer);
                                    foreach (XElement t in NormEffects5)
                                        t.WriteTo(writer);
                                    foreach (XElement x in CritEffects5)
                                        x.WriteTo(writer);
                                    NormEffects5.Clear();
                                    CritEffects5.Clear();
                                    level5 = null;

                                    XElement level6 = new XElement("NIVEAU", new XAttribute("NIVEAU", "6"), new XAttribute("PA", pa), new XAttribute("MIN_RANGE", pomin), new XAttribute("MAX_RANGE", pomax), new XAttribute("LIGNE", line), new XAttribute("LIGNE_DE_VUE", seeline), new XAttribute("NEED_EMPTY_CELL", empty), new XAttribute("MODIF", modif), new XAttribute("PER_TURN", turn), new XAttribute("PER_OBJECTIVE", obj), new XAttribute("INTERVAL", interval));
                                    level6.WriteTo(writer);
                                    foreach (XElement t in NormEffects6)
                                        t.WriteTo(writer);
                                    foreach (XElement x in CritEffects6)
                                        x.WriteTo(writer);
                                    NormEffects6.Clear();
                                    CritEffects6.Clear();
                                    level6 = null;

                                    writer.WriteEndElement();
                                    writer.Flush();
                                }

                                
                            }
                        
                    }
                    MessageBox.Show("La génération des sorts est terminée", "Convertion complète", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }, TaskCreationOptions.LongRunning);
    }
}
