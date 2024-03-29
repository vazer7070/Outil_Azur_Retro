﻿using Outil_Azur_complet.Bot.Controls;

namespace Outil_Azur_complet.Bot.Interfaces
{
    partial class MapControl
    {
        /// <summary> 
        /// Variable nécessaire au concepteur.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Nettoyage des ressources utilisées.
        /// </summary>
        /// <param name="disposing">true si les ressources managées doivent être supprimées ; sinon, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Code généré par le Concepteur de composants

        /// <summary> 
        /// Méthode requise pour la prise en charge du concepteur - ne modifiez pas 
        /// le contenu de cette méthode avec l'éditeur de code.
        /// </summary>
        private void InitializeComponent()
        {
            this.UserMap = new Outil_Azur_complet.Bot.Controls.UserMapControl();
            this.iTalk_Label2 = new iTalk.iTalk_Label();
            this.iTalk_Label1 = new iTalk.iTalk_Label();
            this.SuspendLayout();
            // 
            // UserMap
            // 
            this.UserMap.BorderColorOver = System.Drawing.Color.Empty;
            this.UserMap.CellActive = System.Drawing.Color.Azure;
            this.UserMap.CellInactive = System.Drawing.Color.DarkGray;
            this.UserMap.CurrentCellHover = null;
            this.UserMap.H = 17;
            this.UserMap.Location = new System.Drawing.Point(3, 3);
            this.UserMap.MapQ = Outil_Azur_complet.Bot.Controls.MapQuality.HAUT;
            this.UserMap.Name = "UserMap";
            this.UserMap.ShowAnimations = true;
            this.UserMap.ShowCellId = false;
            this.UserMap.Size = new System.Drawing.Size(737, 429);
            this.UserMap.TabIndex = 2;
            this.UserMap.TraceOnOver = false;
            this.UserMap.W = 15;
            // 
            // iTalk_Label2
            // 
            this.iTalk_Label2.AutoSize = true;
            this.iTalk_Label2.BackColor = System.Drawing.Color.Transparent;
            this.iTalk_Label2.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.iTalk_Label2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.iTalk_Label2.Location = new System.Drawing.Point(88, 434);
            this.iTalk_Label2.Name = "iTalk_Label2";
            this.iTalk_Label2.Size = new System.Drawing.Size(12, 13);
            this.iTalk_Label2.TabIndex = 1;
            this.iTalk_Label2.Text = "*";
            // 
            // iTalk_Label1
            // 
            this.iTalk_Label1.AutoSize = true;
            this.iTalk_Label1.BackColor = System.Drawing.Color.Transparent;
            this.iTalk_Label1.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.iTalk_Label1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.iTalk_Label1.Location = new System.Drawing.Point(16, 431);
            this.iTalk_Label1.Name = "iTalk_Label1";
            this.iTalk_Label1.Size = new System.Drawing.Size(52, 13);
            this.iTalk_Label1.TabIndex = 0;
            this.iTalk_Label1.Text = "Position:";
            // 
            // MapControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.UserMap);
            this.Controls.Add(this.iTalk_Label2);
            this.Controls.Add(this.iTalk_Label1);
            this.Name = "MapControl";
            this.Size = new System.Drawing.Size(743, 453);
            this.Load += new System.EventHandler(this.MapControl_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private UserMapControl UserMap;
        private iTalk.iTalk_Label iTalk_Label1;
        private iTalk.iTalk_Label iTalk_Label2;
    }
}
