using System;
using System.Collections.Generic;
using Tools_protocol.Json;

namespace Tools_protocol.Emulators
{
    /// <summary>Fonctions d'Azur qu'un émulateur peut prendre en charge.</summary>
    [Flags]
    public enum EmulatorFeature
    {
        None = 0,
        /// <summary>Lecture de la liste des comptes.</summary>
        Accounts = 1 << 0,
        /// <summary>Modification, création, bannissement et suppression des comptes.</summary>
        AccountEditing = 1 << 1,
        /// <summary>Lecture et modification des personnages.</summary>
        Characters = 1 << 2,
        /// <summary>Exemplaires d'objets des personnages.</summary>
        Inventory = 1 << 3,
        /// <summary>Création de modèles d'objets, recettes et panoplies.</summary>
        ItemCreation = 1 << 4,
        /// <summary>Éditeurs de ressources statiques (sorts, monstres, PNJ, quêtes…).</summary>
        ResourceEditors = 1 << 5,
        /// <summary>Outil de recherche (objets, panoplies, sorts, monstres, butins).</summary>
        Search = 1 << 6,
        /// <summary>Données serveur liées aux cartes : déclencheurs, fins de combat, placements.</summary>
        MapServerData = 1 << 7,
        /// <summary>Export XML des ressources lisibles par le bot.</summary>
        BotResourceExport = 1 << 8,
    }

    /// <summary>Connexion SQL qui contient une table.</summary>
    public enum TableLocation
    {
        Auth,
        World,
    }

    /// <summary>
    /// Décrit un émulateur : les fonctions qu'Azur sait y utiliser, l'emplacement
    /// de chaque table logique et les données à charger au démarrage.
    /// Un nouvel émulateur s'ajoute avec une classe dérivée enregistrée dans
    /// <see cref="EmulatorRegistry"/>.
    /// </summary>
    public abstract class EmulatorProfile
    {
        private readonly Dictionary<string, TableLocation> tables = new Dictionary<string, TableLocation>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> physicalNames = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Identifiant écrit dans config.json (champ Emu).</summary>
        public abstract string Id { get; }

        /// <summary>Nom affiché dans la configuration.</summary>
        public virtual string DisplayName => Id;

        /// <summary>Courte description de la prise en charge, affichée dans la configuration.</summary>
        public abstract string Summary { get; }

        public abstract EmulatorFeature Features { get; }

        /// <summary>Vrai si l'émulateur utilise une seconde base (world/game) en plus de auth/login.</summary>
        public virtual bool UsesWorldDatabase => true;

        /// <summary>
        /// Famille des classes de données (AccountList, CharacterList…) qui lisent cet émulateur.
        /// Les émulateurs qui reprennent le modèle de Kryone renvoient "Kryone".
        /// </summary>
        public virtual string DataModel => Id;

        public bool Supports(EmulatorFeature feature) => feature == EmulatorFeature.None || (Features & feature) == feature;

        /// <summary>
        /// Déclare des tables logiques (clés des fichiers auth_tables.json / world_tables.json) dont le nom
        /// réel est lu dans ces fichiers de correspondance.
        /// </summary>
        protected void Map(TableLocation location, params string[] logicalNames)
        {
            foreach (string name in logicalNames)
                tables[name] = location;
        }

        /// <summary>
        /// Déclare une table logique avec son nom réel : le profil fait alors autorité et les fichiers
        /// de correspondance ne sont pas consultés pour cette table.
        /// Nommée différemment de <see cref="Map"/> pour que deux noms logiques ne soient jamais pris pour un nom réel.
        /// </summary>
        protected void MapTo(TableLocation location, string logicalName, string physicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName)) throw new ArgumentException("Le nom logique de la table est vide.", nameof(logicalName));
            if (string.IsNullOrWhiteSpace(physicalName)) throw new ArgumentException("Le nom réel de la table " + logicalName + " est vide.", nameof(physicalName));
            tables[logicalName] = location;
            physicalNames[logicalName] = physicalName;
        }

        public bool KnowsTable(string logicalName) => tables.ContainsKey(logicalName);

        /// <summary>Tables logiques connues du profil.</summary>
        public IEnumerable<string> Tables => tables.Keys;

        public TableLocation? Locate(string logicalName)
        {
            TableLocation location;
            return tables.TryGetValue(logicalName, out location) ? location : (TableLocation?)null;
        }

        /// <summary>
        /// Nom réel de la table, ou une chaîne vide si l'émulateur ne la connaît pas.
        /// Le nom déclaré par le profil est prioritaire ; sinon la correspondance JSON de l'utilisateur est lue.
        /// </summary>
        public string Table(string logicalName)
        {
            string physical;
            if (logicalName != null && physicalNames.TryGetValue(logicalName, out physical)) return physical;
            switch (Locate(logicalName))
            {
                case TableLocation.Auth: return JsonManager.SearchAuth(logicalName) ?? "";
                case TableLocation.World: return JsonManager.SearchWorld(logicalName) ?? "";
                default: return "";
            }
        }

        /// <summary>
        /// Nom réel d'une colonne de la table des exemplaires d'objets, à partir de son nom Kryone
        /// (guid, template, qua, pos, stats, puit).
        /// </summary>
        public virtual string ItemColumn(string logicalColumn)
        {
            if (string.IsNullOrWhiteSpace(logicalColumn)) throw new ArgumentException("Le nom logique de la colonne est vide.", nameof(logicalColumn));
            return logicalColumn;
        }

        /// <summary>Charge les caches nécessaires aux outils, une fois les connexions établies.</summary>
        public virtual void LoadCaches() { }

        public override string ToString() => DisplayName;
    }
}
