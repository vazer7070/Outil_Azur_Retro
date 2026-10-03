using System;
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
    /// StarLoco (serveurs Login / Game). Ses tables reprennent le modèle de Kryone, réparties sur
    /// deux bases : <c>login</c> (connexion auth : comptes, personnages, exemplaires d'objets, guildes,
    /// montures et copies « placement » des zones, maisons, enclos et coffres) et <c>game</c>
    /// (connexion world : ressources statiques et copies « propriété » de ces mêmes tables).
    /// Les noms réels sont portés par le profil ; les fichiers de correspondance JSON ne sont pas consultés.
    /// </summary>
    public sealed class StarLocoProfile : EmulatorProfile
    {
        public StarLocoProfile()
        {
            // Base login : données dynamiques lues par le serveur Login et par la connexion « statics » du Game.
            MapTo(TableLocation.Auth, "comptes", "accounts");
            MapTo(TableLocation.Auth, "perso", "players");
            MapTo(TableLocation.Auth, "items", "world.entity.objects");
            MapTo(TableLocation.Auth, "serveurs", "servers");
            MapTo(TableLocation.Auth, "banip", "banip");
            MapTo(TableLocation.Auth, "commandes", "administration.commands");
            MapTo(TableLocation.Auth, "groupes", "administration.groups");
            MapTo(TableLocation.Auth, "rss", "client_rss_news");
            MapTo(TableLocation.Auth, "areadata", "area_data");
            MapTo(TableLocation.Auth, "subarea_data", "subarea_data");
            MapTo(TableLocation.Auth, "maisons", "houses");
            MapTo(TableLocation.Auth, "coffre", "coffres");
            MapTo(TableLocation.Auth, "guildes", "world.entity.guilds");
            MapTo(TableLocation.Auth, "montures", "world.entity.mounts");
            MapTo(TableLocation.Auth, "familiers_joueurs", "world.entity.pets");
            MapTo(TableLocation.Auth, "quetes_joueurs", "world.entity.players.quests");
            // Base game : ressources statiques et données de jeu lues par la connexion « dynamics » du Game.
            MapTo(TableLocation.World, "animations", "animations");
            MapTo(TableLocation.World, "bandits", "bandits");
            MapTo(TableLocation.World, "banque", "banks");
            MapTo(TableLocation.World, "challenge", "challenge");
            MapTo(TableLocation.World, "crafts", "crafts");
            MapTo(TableLocation.World, "donjons", "donjons");
            MapTo(TableLocation.World, "drops", "drops");
            MapTo(TableLocation.World, "endfight", "endfight_action");
            MapTo(TableLocation.World, "experience", "experience");
            MapTo(TableLocation.World, "extra", "extra_monster");
            MapTo(TableLocation.World, "morphs", "full_morphs");
            MapTo(TableLocation.World, "gifts", "gifts");
            MapTo(TableLocation.World, "membres_guildes", "guild_members");
            MapTo(TableLocation.World, "hdvs", "hdvs");
            MapTo(TableLocation.World, "hdvs_objets", "hdvs_items");
            MapTo(TableLocation.World, "Iporte", "interactive_doors");
            MapTo(TableLocation.World, "interactions", "interactive_objects_data");
            MapTo(TableLocation.World, "Template", "item_template");
            MapTo(TableLocation.World, "panoplies", "itemsets");
            MapTo(TableLocation.World, "metiers", "jobs_data");
            MapTo(TableLocation.World, "cartes", "maps");
            MapTo(TableLocation.World, "groupe_monstre", "mobgroups_fix");
            MapTo(TableLocation.World, "monstres", "monsters");
            // La copie « propriété » des enclos (owner, guild, price…) est celle que l'éditeur d'enclos modifie.
            MapTo(TableLocation.World, "enclos", "mountpark_data");
            MapTo(TableLocation.World, "npc_questions", "npc_questions");
            MapTo(TableLocation.World, "npc_reponse", "npc_reponses_actions");
            MapTo(TableLocation.World, "npc_template", "npc_template");
            MapTo(TableLocation.World, "npcs", "npcs");
            MapTo(TableLocation.World, "objets_actions", "objectsactions");
            MapTo(TableLocation.World, "percepteurs", "percepteurs");
            MapTo(TableLocation.World, "familiers", "pets");
            MapTo(TableLocation.World, "prismes", "prismes");
            MapTo(TableLocation.World, "quete", "quest_data");
            MapTo(TableLocation.World, "quete_etape", "quest_etapes");
            MapTo(TableLocation.World, "quete_objectif", "quest_objectifs");
            MapTo(TableLocation.World, "runes", "runes");
            MapTo(TableLocation.World, "schema_fight", "schemafights");
            MapTo(TableLocation.World, "cellule", "scripted_cells");
            MapTo(TableLocation.World, "sort", "sorts");
            MapTo(TableLocation.World, "tuto", "tutoriel");
            MapTo(TableLocation.World, "zaapi", "zaapi");
            MapTo(TableLocation.World, "zaaps", "zaaps");
        }

        public override string Id => "StarLoco";
        public override string Summary => "Comptes, personnages, inventaires (login) ; objets, ressources, cartes et export vers le bot (game). Pas de titres ni de paroli.";
        public override string DataModel => "Kryone";

        public override EmulatorFeature Features =>
            EmulatorFeature.Accounts | EmulatorFeature.AccountEditing | EmulatorFeature.Characters |
            EmulatorFeature.Inventory | EmulatorFeature.ItemCreation | EmulatorFeature.ResourceEditors |
            EmulatorFeature.Search | EmulatorFeature.MapServerData | EmulatorFeature.BotResourceExport;

        /// <summary>Colonnes de login.world.entity.objects : id, template, quantity, position, stats, puit.</summary>
        public override string ItemColumn(string logicalColumn)
        {
            switch (base.ItemColumn(logicalColumn))
            {
                case "guid": return "id";
                case "qua": return "quantity";
                case "pos": return "position";
                default: return logicalColumn;
            }
        }

        public override void LoadCaches()
        {
            AccountList.AllAccounts();
            GroupesList.groupe();
            CharacterList.AllPerso();
        }
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
