using Tools_protocol.Kryone.Database;

namespace Tools_protocol.Emulators
{
    /// <summary>Kryone V2 (bases kauth / kworld) : cible de référence de tous les outils.</summary>
    public sealed class KryoneProfile : EmulatorProfile
    {
        public KryoneProfile()
        {
            Map(TableLocation.Auth,
                "comptes", "animations", "areadata", "bandits", "banip", "banque", "challenge", "coffre",
                "commandes", "crafts", "donjons", "drops", "endfight", "extra", "morphs", "groupes", "hdvs",
                "maisons", "Iporte", "interactions", "Template", "panoplies", "metiers", "cartes",
                "groupe_monstre", "enclos", "monstres", "npc_questions", "npc_reponse", "npc_template", "npcs",
                "objets_actions", "paroli", "familiers", "perso", "prismes", "quete", "quete_etape",
                "quete_objectif", "rss", "runes", "schema_fight", "cellule", "serveurs", "sort",
                "subarea_data", "titres", "tuto", "zaapi", "zaaps");
            // Les exemplaires d'objets sont dans kworld ; les personnages restent dans kauth.players.
            Map(TableLocation.World, "items", "gifts");
        }

        public override string Id => "Kryone";
        public override string DisplayName => "Kryone V2";
        public override string Summary => "Tous les outils : comptes, personnages, inventaires, objets, ressources, cartes et export vers le bot.";

        public override EmulatorFeature Features =>
            EmulatorFeature.Accounts | EmulatorFeature.AccountEditing | EmulatorFeature.Characters |
            EmulatorFeature.Inventory | EmulatorFeature.ItemCreation | EmulatorFeature.ResourceEditors |
            EmulatorFeature.Search | EmulatorFeature.MapServerData | EmulatorFeature.BotResourceExport;

        public override void LoadCaches()
        {
            AccountList.AllAccounts();
            GroupesList.groupe();
            CharacterList.AllPerso();
        }
    }

    /// <summary>
    /// StarLoco (serveurs Login / Game). Le bot sait déjà s'y connecter ; les tables
    /// SQL de StarLoco ne correspondent pas à celles de Kryone et seront déclarées
    /// ici une fois le schéma login.sql / game.sql relevé.
    /// </summary>
    public sealed class StarLocoProfile : EmulatorProfile
    {
        public override string Id => "StarLoco";
        public override string Summary => "Bot, cartes, objets du client et gestionnaire. Les éditeurs SQL attendent la correspondance des tables StarLoco.";
        public override EmulatorFeature Features => EmulatorFeature.None;
    }

    /// <summary>Sunshine : lecture des comptes uniquement.</summary>
    public sealed class SunshineProfile : EmulatorProfile
    {
        public SunshineProfile()
        {
            Map(TableLocation.Auth, "comptes");
        }

        public override string Id => "Sunshine";
        public override string Summary => "Consultation des comptes. Les autres outils SQL ne sont pas adaptés à son schéma.";
        public override EmulatorFeature Features => EmulatorFeature.Accounts;
        public override bool UsesWorldDatabase => false;
    }

    /// <summary>Codebreak : correspondances de tables connues, outils SQL non encore adaptés.</summary>
    public sealed class CodebreakProfile : EmulatorProfile
    {
        public CodebreakProfile()
        {
            Map(TableLocation.Auth, "comptes");
            Map(TableLocation.World, "perso");
        }

        public override string Id => "Codebreak";
        public override string Summary => "Connexion aux bases uniquement. Les outils SQL ne sont pas encore adaptés à son schéma.";
        public override EmulatorFeature Features => EmulatorFeature.None;
    }
}
