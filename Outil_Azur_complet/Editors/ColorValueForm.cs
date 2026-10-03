using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Outil_Azur_complet.Editors
{
    internal sealed class ColorValueForm : AzurEditorWindow
    {
        private readonly iTalk.iTalk_TextBox_Small input=EditorUi.TextBox();
        private readonly Panel preview=new Panel { Height=54,BackColor=Color.White };
        internal string Result { get; private set; }
        private ColorValueForm(string data) : base("Choisir une couleur","Choisissez une couleur ou saisissez son code. La couleur par défaut est conservée avec la valeur -1.",new Size(790,500))
        {
            EditorUi.PrepareWindow(this,Text,new Size(790,500),new Size(700,450));
            int value;input.Text=int.TryParse(data,out value)&&value>=0?"#"+value.ToString("X6"):data;
            var sheet=EditorUi.Sheet();sheet.Controls.Add(BoundEditorLayout.FieldPanel(input,"Code de la couleur","Code #RRGGBB, valeur décimale, ou -1 pour la couleur par défaut."),0,0);
            EditorUi.AddField(sheet,preview);Body.Controls.Add(sheet);
            ActionButton("Palette…",ChooseColor,width:130);ActionButton("Couleur par défaut",()=>input.Text="-1",width:180);
            ActionButton("Appliquer",()=>{Result=Parse(input.Text).ToString(CultureInfo.InvariantCulture);DialogResult=DialogResult.OK;Close();},true,130);
            ActionButton("Annuler",()=>{DialogResult=DialogResult.Cancel;Close();},width:110);
            input.TextChanged+=(s,e)=>UpdatePreview();UpdatePreview();
        }
        private void UpdatePreview()
        {
            try { int value=Parse(input.Text);preview.BackColor=value<0?Color.FromArgb(235,238,242):Color.FromArgb(value>>16,(value>>8)&255,value&255);SetStatus(value<0?"Couleur définie par le client de jeu.":"Couleur choisie · #"+value.ToString("X6")); }
            catch(FormatException error) { Status.Text=error.Message;Status.ForeColor=Color.Firebrick; }
        }
        private void ChooseColor()
        {
            int value;try{value=Parse(input.Text);}catch(FormatException){value=0;}
            using(var picker=new ColorDialog { FullOpen=true,Color=value<0?Color.White:Color.FromArgb(value>>16,(value>>8)&255,value&255) })
                if(picker.ShowDialog(this)==DialogResult.OK)input.Text="#"+((picker.Color.R<<16)|(picker.Color.G<<8)|picker.Color.B).ToString("X6");
        }
        internal static int Parse(string data)
        {
            string text=(data??"").Trim();int value=0;
            bool valid=text.StartsWith("#",StringComparison.Ordinal)?text.Length==7&&int.TryParse(text.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value):int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out value);
            // Assign explicitly for the short-circuited hexadecimal branch.
            if(text.StartsWith("#",StringComparison.Ordinal)&&text.Length!=7)throw new FormatException("Saisissez un code #RRGGBB, une valeur de 0 à 16 777 215, ou -1.");
            if(!valid || value < -1 || value > 0xFFFFFF)throw new FormatException("Saisissez un code #RRGGBB, une valeur de 0 à 16 777 215, ou -1.");
            return value;
        }
        internal static string Edit(IWin32Window owner,string data)
        { using(var form=new ColorValueForm(data))return form.ShowDialog(owner)==DialogResult.OK?form.Result:null; }
    }
}
