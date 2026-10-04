using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Entrées de groupe des menus de joueurs, comme <c>PartyItem.addPartyMenuItems</c> du client 1.34. « Inviter dans le groupe »
    /// (<c>PI&lt;nom&gt;</c>) sur un autre joueur de la carte est déjà fourni par <c>PlayerMenuProvider</c> (lot des actions de la
    /// carte) : ce fournisseur ne le répète pas. Sur son propre personnage, quand un groupe ou une invitation envoyée existe :
    /// « Groupe… » (volet du tiroir, qui remplace le bandeau <c>Party</c> toujours visible du client).
    /// Le menu d'un membre (<see cref="MemberEntries"/>) est ouvert depuis le volet Groupe : localiser (<c>PW</c>), quitter
    /// (<c>PV</c>), suivre (<c>PF±&lt;id&gt;</c>), faire suivre par tout le groupe (<c>PG±&lt;id&gt;</c>) et exclure (<c>PV&lt;id&gt;</c>).
    /// </summary>
    [ActorMenuOrder(300)]
    public sealed class PartyMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => ActorClassifier.Of(a) == MenuActorKind.Player;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            PartyActions party = g?.Interactions?.Party;
            if (a == null || party == null || !ActorClassifier.IsSelf(a, g)) yield break;
            if (!party.Group.IsActive && party.OutgoingInvitee == null) yield break;
            yield return new MenuEntry(PartyTexts.Get("PARTY", "Groupe") + "…", ShowPanel)
            { Infobulle = "Affiche le volet Groupe : membres, suivi, localisation" };
        }

        /// <summary>
        /// Menu d'un membre du volet Groupe (<c>PartyItem.addPartyMenuItems</c>) : en-tête « Groupe », « Localiser les membres »,
        /// puis pour soi « Quitter le groupe » et, chef seulement, « Suivez-moi tous » / « Arrêtez tous de me suivre » ; pour un
        /// autre membre « Suivre » / « Ne plus suivre » et, chef seulement, « Suivez-le tous » / « Arrêtez tous de le suivre »
        /// et « Exclure du groupe ». Le client ne propose jamais l'arrêt du suivi collectif de soi-même (son état « suivi »
        /// ne vaut que pour les autres) ; le bot s'appuie sur la dernière demande <c>PG+</c> envoyée.
        /// </summary>
        public static IList<MenuEntry> MemberEntries(PartyActions party, PartyMember member, long selfId)
        {
            var entries = new List<MenuEntry>();
            if (party == null || member == null) return entries;
            Groupe group = party.Group;
            bool leader = group.IsLeader;
            long id = member.Id;
            entries.Add(MenuEntry.Statique(PartyTexts.Get("PARTY", "Groupe")));
            entries.Add(Entry(PartyTexts.Get("PARTY_WHERE", "Localiser le groupe"), "PW", party.LocateAsync));
            if (id == selfId)
            {
                entries.Add(Entry(PartyTexts.Get("LEAVE_PARTY", "Quitter"), "PV", party.LeaveAsync));
                if (leader)
                {
                    if (party.FollowAllTargetId == id)
                        entries.Add(Entry(PartyTexts.Get("PARTY_STOP_FOLLOW_ME_ALL", "Ne plus me suivre (tous)"), "PG-" + id, () => party.StopFollowAllAsync(id)));
                    else entries.Add(Entry(PartyTexts.Get("PARTY_FOLLOW_ME_ALL", "Me suivre (tous)"), "PG+" + id, () => party.FollowAllAsync(id)));
                }
                return entries;
            }
            if (group.FollowedId == id)
                entries.Add(Entry(PartyTexts.Get("STOP_FOLLOW", "Arrêter de suivre"), "PF-" + id, () => party.StopFollowingAsync(id)));
            else entries.Add(Entry(PartyTexts.Get("FOLLOW", "Suivre"), "PF+" + id, () => party.FollowAsync(id)));
            if (leader)
            {
                if (party.FollowAllTargetId == id)
                    entries.Add(Entry(PartyTexts.Get("PARTY_STOP_FOLLOW_HIM_ALL", "Ne plus le suivre (tous)"), "PG-" + id, () => party.StopFollowAllAsync(id)));
                else entries.Add(Entry(PartyTexts.Get("PARTY_FOLLOW_HIM_ALL", "Le suivre (tous)"), "PG+" + id, () => party.FollowAllAsync(id)));
                entries.Add(Entry(PartyTexts.Get("KICK_FROM_PARTY", "Exclure"), "PV" + id, () => party.KickAsync(id)));
            }
            return entries;
        }

        private static MenuEntry Entry(string text, string packet, Func<Task<InteractionResult>> action) =>
            new MenuEntry(text, context => Run(action)) { Infobulle = "Envoie " + packet };

        private static Task<string> ShowPanel(ActorMenuContext context)
        {
            PartyPanel panel = context?.Panels?.Get<PartyPanel>();
            if (panel == null) return Task.FromResult("Le volet Groupe n'est pas disponible.");
            context.Panels.Show(panel);
            return Task.FromResult<string>(null);
        }

        private static async Task<string> Run(Func<Task<InteractionResult>> action)
        {
            InteractionResult result = await action().ConfigureAwait(true);
            return result?.Message;
        }
    }
}
