namespace Tool_BotProtocol.Game.Maps
{
    /// <summary>
    /// Droits de la carte lus par le client 1.34 dans la variable <c>capabilities</c> du SWF de carte
    /// (<c>Map.bCanChallenge</c>, <c>Map.bCanAttack</c>) : un bit posé interdit l'action. Les ressources
    /// <c>BotMaps</c> n'exportent pas encore ce champ : sans valeur connue, la carte est traitée comme le client
    /// traite une carte sans restriction et le serveur reste juge (StarLoco : <c>maps.forbidden</c>).
    /// </summary>
    public partial class Map
    {
        /// <summary>Bit 0 : défis interdits sur la carte (<c>bCanChallenge</c> faux).</summary>
        public const int CapabilityNoChallenge = 1;
        /// <summary>Bit 1 : agressions interdites sur la carte (<c>bCanAttack</c> faux).</summary>
        public const int CapabilityNoAttack = 2;

        private readonly object capabilitiesSync = new object();
        private int capabilitiesMapId = int.MinValue;
        private int? capabilities;

        /// <summary>
        /// Masque <c>capabilities</c> de la carte courante, ou <c>null</c> s'il est inconnu. La valeur ne vaut que pour la
        /// carte où elle a été posée : après un changement de carte, elle redevient inconnue.
        /// </summary>
        public int? Capabilities
        {
            get { lock (capabilitiesSync) return capabilitiesMapId == MapID ? capabilities : null; }
            set { lock (capabilitiesSync) { capabilities = value; capabilitiesMapId = MapID; } }
        }

        /// <summary>Le client propose « Défier » actif (<c>Map.bCanChallenge</c>) ; vrai si le masque est inconnu.</summary>
        public bool CanChallenge => ((Capabilities ?? 0) & CapabilityNoChallenge) == 0;
        /// <summary>Le client propose « Agresser » actif (<c>Map.bCanAttack</c>) ; vrai si le masque est inconnu.</summary>
        public bool CanAttack => ((Capabilities ?? 0) & CapabilityNoAttack) == 0;
    }
}
