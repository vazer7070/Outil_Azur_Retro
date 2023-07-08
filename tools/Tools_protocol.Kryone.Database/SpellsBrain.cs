using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tools_protocol.Kryone.Database
{
    public class SpellsBrain
    {
       public string Zone { get; set; }
      public string PA { get; set; }
        public string PO { get; set; }
        public string CC { get; set; }
        public string EC { get; set; }
        public string NBL { get; set; }
        public string INTERVAL { get; set; }
        public List<string> EFFECT;
        public List<string> EFFECT_CRIT; 
        public string PMODIF { get; set; }
        public string LINE { get; set; }
        public string LINE_SEE { get; set; }
        public string EMPTY_CELL{ get; set;}
        public string EC_TURN { get; set; }
        public string LEVEL { get; set; }
        List<string> h = new List<string>();
        List<string> v = new List<string>();
        public SpellsBrain()
        {

        }
        public SpellsBrain(string zone, string pA, string pO, string cC, string eC, string nBL, string iNTERVAL, List<string> eFFECT, List<string> eFFECT_CRIT, string pMODIF, string lINE, string lINE_SEE, string eMPTY_CELL, string eC_TURN, string lEVEL)
        {
            Zone = zone;
            PA = pA;
            PO = pO;
            CC = cC;
            EC = eC;
            NBL = nBL;
            INTERVAL = iNTERVAL;
            EFFECT = eFFECT = new List<string>();
            EFFECT_CRIT = eFFECT_CRIT = new List<string>();
            PMODIF = pMODIF;
            LINE = lINE;
            LINE_SEE = lINE_SEE;
            EMPTY_CELL = eMPTY_CELL;
            EC_TURN = eC_TURN;
            LEVEL = lEVEL;
        }
         void ClearAll()
        {
            Zone= null;
            PA= null;
            PO= null;
            CC= null;
            EC= null;
            NBL= null;
            INTERVAL = null;
            EFFECT = null;
            EFFECT_CRIT = null;
            PMODIF = null;
            LINE = null;
            LINE_SEE = null;
            EMPTY_CELL = null;
            EC_TURN = null;
            LEVEL = null;
        }
        public  Task TradAndUnderstandSpells() => Task.Factory.StartNew(() =>
        {
            foreach (int id in SpellsList.AllSpells.Keys)
            {
                SpellsList.ParseLevel(id, true);

                foreach (string lvl in SpellsList.EffectLvl1)
                {
                    
                    if (lvl.Contains("?"))
                    {
                        PA = lvl.Split('?')[1];
                        PO = lvl.Split('?')[2];
                        CC = $"1/{lvl.Split('?')[3]}";
                        if (lvl.Split('?')[4].Equals("0"))
                            EC = "0";
                        else
                            EC = $"1/{lvl.Split('?')[4]}";
                        LINE = lvl.Split('?')[5];
                        LINE_SEE = lvl.Split('?')[6];
                        EMPTY_CELL = lvl.Split('?')[7];
                        PMODIF = lvl.Split('?')[8];
                        EC_TURN = lvl.Split('?')[14];
                        NBL = lvl.Split('?')[9];
                        INTERVAL = lvl.Split('?')[10];
                        Zone = lvl.Split('?')[12];
                        LEVEL = lvl.Split('?')[13];
                    }
                    if (lvl.Contains("~"))
                    {
                        h.Add(lvl.Split('@')[0]); //normal
                        v.Add(lvl.Split('~')[1].Split('§')[0]); //critique

                    }
                    else if (lvl.Contains('@') && !lvl.Contains('~'))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                        v.Add(lvl.Split('§')[0]);
                }
                SpellsList.ParsedSPells.Add($"{id}|{SpellsList.Return_spells(id)}|1", new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL, h, v, PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
                h.Clear();
                v.Clear();
                ClearAll();

                foreach (string lvl in SpellsList.EffectLvl2)
                {
                  

                    if (lvl.Contains("?"))
                    {
                        PA = lvl.Split('?')[1];
                        PO = lvl.Split('?')[2];
                        CC = $"1/{lvl.Split('?')[3]}";
                        if (lvl.Split('?')[4].Equals("0"))
                            EC = "0";
                        else
                            EC = $"1/{lvl.Split('?')[4]}";
                        LINE = lvl.Split('?')[5];
                        LINE_SEE = lvl.Split('?')[6];
                        EMPTY_CELL = lvl.Split('?')[7];
                        PMODIF = lvl.Split('?')[8];
                        EC_TURN = lvl.Split('?')[14];
                        NBL = lvl.Split('?')[9];
                        INTERVAL = lvl.Split('?')[10];
                        Zone = lvl.Split('?')[12];
                        LEVEL = lvl.Split('?')[13];
                    }
                    if (lvl.Contains("~"))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add(lvl.Split('~')[1].Split('§')[0]);

                    }
                    else if (lvl.Contains('@') && !lvl.Contains('~'))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                        v.Add(lvl.Split('§')[0]);
                }
                SpellsList.ParsedSPells.Add($"{id}|{SpellsList.Return_spells(id)}|2", new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL, h, v, PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
                h.Clear();
                v.Clear();
                ClearAll();

                foreach (string lvl in SpellsList.EffectLvl3)
                {
                 

                    if (lvl.Contains("?"))
                    {
                        PA = lvl.Split('?')[1];
                        PO = lvl.Split('?')[2];
                        CC = $"1/{lvl.Split('?')[3]}";
                        if (lvl.Split('?')[4].Equals("0"))
                            EC = "0";
                        else
                            EC = $"1/{lvl.Split('?')[4]}";
                        LINE = lvl.Split('?')[5];
                        LINE_SEE = lvl.Split('?')[6];
                        EMPTY_CELL = lvl.Split('?')[7];
                        PMODIF = lvl.Split('?')[8];
                        EC_TURN = lvl.Split('?')[14];
                        NBL = lvl.Split('?')[9];
                        INTERVAL = lvl.Split('?')[10];
                        Zone = lvl.Split('?')[12];
                        LEVEL = lvl.Split('?')[13];
                    }
                    if (lvl.Contains("~"))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add(lvl.Split('~')[1].Split('§')[0]);

                    }
                    else if (lvl.Contains('@') && !lvl.Contains('~'))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                        v.Add(lvl.Split('§')[0]);
                }
                SpellsList.ParsedSPells.Add($"{id}|{SpellsList.Return_spells(id)}|3", new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL, h, v, PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
                h.Clear();
                v.Clear();
                ClearAll();

                foreach (string lvl in SpellsList.EffectLvl4)
                {
                 

                    if (lvl.Contains("?"))
                    {
                        PA = lvl.Split('?')[1];
                        PO = lvl.Split('?')[2];
                        CC = $"1/{lvl.Split('?')[3]}";
                        if (lvl.Split('?')[4].Equals("0"))
                            EC = "0";
                        else
                            EC = $"1/{lvl.Split('?')[4]}";
                        LINE = lvl.Split('?')[5];
                        LINE_SEE = lvl.Split('?')[6];
                        EMPTY_CELL = lvl.Split('?')[7];
                        PMODIF = lvl.Split('?')[8];
                        EC_TURN = lvl.Split('?')[14];
                        NBL = lvl.Split('?')[9];
                        INTERVAL = lvl.Split('?')[10];
                        Zone = lvl.Split('?')[12];
                        LEVEL = lvl.Split('?')[13];
                    }
                    if (lvl.Contains("~"))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add(lvl.Split('~')[1].Split('§')[0]);

                    }
                    else if (lvl.Contains('@') && !lvl.Contains('~'))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                        v.Add(lvl.Split('§')[0]);
                }
                SpellsList.ParsedSPells.Add($"{id}|{SpellsList.Return_spells(id)}|4", new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL, h, v, PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
                h.Clear();
                v.Clear();
                ClearAll();

                foreach (string lvl in SpellsList.EffectLvl5)
                {
               

                    if (lvl.Contains("?"))
                    {
                        PA = lvl.Split('?')[1];
                        PO = lvl.Split('?')[2];
                        CC = $"1/{lvl.Split('?')[3]}";
                        if (lvl.Split('?')[4].Equals("0"))
                            EC = "0";
                        else
                            EC = $"1/{lvl.Split('?')[4]}";
                        LINE = lvl.Split('?')[5];
                        LINE_SEE = lvl.Split('?')[6];
                        EMPTY_CELL = lvl.Split('?')[7];
                        PMODIF = lvl.Split('?')[8];
                        EC_TURN = lvl.Split('?')[14];
                        NBL = lvl.Split('?')[9];
                        INTERVAL = lvl.Split('?')[10];
                        Zone = lvl.Split('?')[12];
                        LEVEL = lvl.Split('?')[13];
                    }
                    if (lvl.Contains("~"))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add(lvl.Split('~')[1].Split('§')[0]);

                    }
                    else if (lvl.Contains('@') && !lvl.Contains('~'))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                        v.Add(lvl.Split('§')[0]);
                }
                SpellsList.ParsedSPells.Add($"{id}|{SpellsList.Return_spells(id)}|5", new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL, h, v, PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
                h.Clear();
                v.Clear();
                ClearAll();

                foreach (string lvl in SpellsList.EffectLvl6)
                {
                    

                    if (lvl.Contains("?"))
                    {
                        PA = lvl.Split('?')[1];
                        PO = lvl.Split('?')[2];
                        CC = $"1/{lvl.Split('?')[3]}";
                        if (lvl.Split('?')[4].Equals("0"))
                            EC = "0";
                        else
                            EC = $"1/{lvl.Split('?')[4]}";
                        LINE = lvl.Split('?')[5];
                        LINE_SEE = lvl.Split('?')[6];
                        EMPTY_CELL = lvl.Split('?')[7];
                        PMODIF = lvl.Split('?')[8];
                        EC_TURN = lvl.Split('?')[14];
                        NBL = lvl.Split('?')[9];
                        INTERVAL = lvl.Split('?')[10];
                        Zone = lvl.Split('?')[12];
                        LEVEL = lvl.Split('?')[13];
                    }
                    if (lvl.Contains("~"))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add(lvl.Split('~')[1].Split('§')[0]);

                    }
                    else if (lvl.Contains('@') && !lvl.Contains('~'))
                    {
                        h.Add(lvl.Split('@')[0]);
                        v.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                        v.Add(lvl.Split('§')[0]);
                }
                SpellsList.ParsedSPells.Add($"{id}|{SpellsList.Return_spells(id)}|6", new SpellsBrain(Zone, PA, PO, CC, EC, NBL, INTERVAL, h, v, PMODIF, LINE, LINE_SEE, EMPTY_CELL, EC_TURN, LEVEL));
                h.Clear();
                v.Clear();
                ClearAll();
            }
        }, TaskCreationOptions.LongRunning);
    }

}
