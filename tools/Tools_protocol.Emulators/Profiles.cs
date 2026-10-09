using System;
using System.Collections.Generic;
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

        public override IReadOnlyList<InteractiveSkillRule> InteractiveSkillRules => StarLocoSkillRules;

        /// <summary>
        /// Reprise de <c>GameCase.canDoAction</c> du serveur Game : <c>Player.startActionOnCell</c> refuse tout
        /// <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c> dont la compétence ne correspond pas au gfx de l'objet de la cellule.
        /// Les compétences de récolte exigent en plus une ressource pleine (<c>JobConstant.IOBJECT_STATE_FULL</c>).
        /// Les portes de maison couvrent toute la plage 6700-6776, enclos compris, comme dans le serveur.
        /// </summary>
        private static readonly InteractiveSkillRule[] StarLocoSkillRules = BuildStarLocoSkillRules();

        private static InteractiveSkillRule[] BuildStarLocoSkillRules()
        {
            var rules = new List<InteractiveSkillRule>();
            void Add(InteractiveKind kind, int gfx, params int[] skills) { foreach (int skill in skills) rules.Add(new InteractiveSkillRule(skill, gfx, kind)); }
            void Range(InteractiveKind kind, int first, int last, params int[] skills) { foreach (int skill in skills) rules.Add(new InteractiveSkillRule(skill, first, last, kind)); }
            // Récolte {compétence, gfx} : faucher, couper, puiser, miner, cueillir, pêcher. Le tas de patates (42)
            // est vérifié plein par GameCase.startAction plutôt que par canDoAction.
            int[] harvest =
            {
                45, 7511, 53, 7515, 57, 7517, 46, 7512, 50, 7513, 68, 7513, 159, 7550, 52, 7516, 58, 7518, 69, 7514, 54, 7514,
                6, 7500, 39, 7501, 40, 7502, 10, 7503, 141, 7542, 139, 7541, 37, 7504, 154, 7553, 33, 7505, 41, 7506,
                34, 7507, 174, 7557, 38, 7508, 35, 7509, 155, 7554, 158, 7552, 102, 7519, 42, 7510,
                24, 7520, 25, 7522, 26, 7523, 28, 7525, 56, 7524, 162, 7556, 55, 7521, 29, 7526, 31, 7528, 30, 7527, 161, 7555,
                71, 7533, 72, 7534, 73, 7535, 74, 7536, 160, 7551,
                128, 7530, 124, 7529, 136, 7544, 140, 7543, 125, 7532, 129, 7531, 126, 7537, 130, 7538, 127, 7539, 131, 7540,
            };
            for (int i = 0; i < harvest.Length; i += 2) Add(InteractiveKind.Harvest, harvest[i + 1], harvest[i]);
            // Ateliers des métiers.
            Add(InteractiveKind.Workshop, 7028, 151);
            Add(InteractiveKind.Workshop, 7007, 122, 47);
            Add(InteractiveKind.Workshop, 7003, 101);
            Add(InteractiveKind.Workshop, 7005, 48);
            Add(InteractiveKind.Workshop, 7002, 32);
            Add(InteractiveKind.Workshop, 7006, 22);
            Add(InteractiveKind.Workshop, 7019, 23);
            Add(InteractiveKind.Workshop, 7024, 133);
            Add(InteractiveKind.Workshop, 7001, 109, 27);
            Add(InteractiveKind.Workshop, 7022, 135);
            Add(InteractiveKind.Workshop, 7023, 134);
            Add(InteractiveKind.Workshop, 7025, 132);
            Add(InteractiveKind.Workshop, 7020, 1, 113, 115, 116, 117, 118, 119, 120);
            Add(InteractiveKind.Workshop, 7012, 19, 143, 145, 144, 142, 146, 67, 21, 65, 66, 20, 18);
            Add(InteractiveKind.Workshop, 7036, 167, 165, 166);
            Add(InteractiveKind.Workshop, 7037, 164, 163);
            Add(InteractiveKind.Workshop, 7038, 168, 169);
            Add(InteractiveKind.Workshop, 7039, 171, 182);
            Add(InteractiveKind.Workshop, 7027, 156);
            Add(InteractiveKind.Workshop, 7011, 13, 14);
            Add(InteractiveKind.Workshop, 7015, 123, 64);
            Add(InteractiveKind.Workshop, 7013, 17, 16, 147, 148, 149, 15);
            Add(InteractiveKind.Workshop, 7014, 63);
            Add(InteractiveKind.Workshop, 7016, 63);
            Range(InteractiveKind.Workshop, 7008, 7010, 11, 12);
            Add(InteractiveKind.Workshop, 7021, 121, 181);
            Add(InteractiveKind.Workshop, 7018, 110);
            // Zaaps (44 sauvegarder, 114 utiliser), zaapis, maisons, coffres, enclos.
            foreach (int gfx in new[] { 7000, 7026, 7029, 4287 }) Add(InteractiveKind.Zaap, gfx, 44, 114);
            foreach (int gfx in new[] { 7030, 7031 }) Add(InteractiveKind.Zaapi, gfx, 157);
            Range(InteractiveKind.House, 6700, 6776, 81, 84, 97, 98, 108);
            foreach (int gfx in new[] { 7350, 7351, 7353 }) Add(InteractiveKind.Chest, gfx, 104, 105);
            foreach (int gfx in new[] { 6763, 6766, 6767, 6772 }) Add(InteractiveKind.MountPark, gfx, 175, 176, 177, 178);
            // Autres : fontaine de jouvence, levier, statues vers Incarnam, livre des artisans, 153.
            Add(InteractiveKind.Other, 7004, 62);
            Add(InteractiveKind.Other, 7045, 179);
            Add(InteractiveKind.Other, 1845, 183);
            Range(InteractiveKind.Other, 1853, 1862, 183);
            Add(InteractiveKind.Other, 2319, 183);
            Add(InteractiveKind.Other, 7035, 170);
            Add(InteractiveKind.Other, 7352, 153);
            return rules.ToArray();
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
