namespace Outil_Azur_complet.Bot
{
    partial class GameClientFullform
    {
        /// <summary>
        /// Conteneur des composants du formulaire. La disposition est construite dans <c>GameClientFullform.cs</c> ;
        /// le fichier .resx ne sert plus qu'aux images de repli du bandeau et du bouton d'envoi.
        /// </summary>
        private readonly System.ComponentModel.IContainer components = new System.ComponentModel.Container();

        /// <summary>Libère les ressources : l'interface est d'abord détachée de la session.</summary>
        /// <param name="disposing">true pour libérer les ressources managées.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseUi();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
