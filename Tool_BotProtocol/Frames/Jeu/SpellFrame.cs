using System;
using System.Collections.Concurrent;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    internal class SpellFrame : Frame
    {
        [MessageAttribution("SL")]
        public void ListSpells(TcpClient client, string message)
        {
            var account = client.account;
            if (account == null) return;
            var learned = new ConcurrentDictionary<short, Spell>();
            foreach (string entry in message.Substring(2).Split(';'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string[] fields = entry.Split('~');
                short id;
                byte level;
                if (fields.Length < 3 || !TryRead(fields[0], fields[1], out id, out level) ||
                    (fields[2].Length != 1 && fields[2] != "null" && fields[2].Length != 0))
                {
                    account.Logger.LogDanger("SORTS", "Un sort mal formé a été ignoré dans la liste reçue.");
                    continue;
                }
                learned[id] = Spell.ForCharacter(id, level, fields[2] == "null" ? null : fields[2]);
            }
            account.Game.character.Spells = learned;
            account.Game.character.SpellsRefreshEvent();
        }

        [MessageAttribution("SUK")]
        public void SpellUpgraded(TcpClient client, string message)
        {
            var account = client.account;
            if (account == null) return;
            string[] fields = message.Substring(3).Split('~');
            short id;
            byte level;
            if (fields.Length != 2 || !TryRead(fields[0], fields[1], out id, out level))
            {
                account.Logger.LogDanger("SORTS", "Le serveur a envoyé une mise à jour de sort invalide.");
                return;
            }
            account.Game.character.Spells.AddOrUpdate(id, key => Spell.ForCharacter(key, level),
                (key, previous) => previous.CopyForCharacter(level));
            account.Logger.LogInfo("SORTS", "Le serveur a confirmé le niveau " + level + " du sort #" + id + ".");
            account.Game.character.SpellsRefreshEvent();
        }

        [MessageAttribution("SUE")]
        public void SpellUpgradeRefused(TcpClient client, string message)
        {
            client.account?.Logger?.LogError("SORTS", "Amélioration du sort refusée : vérifiez les points de sort disponibles et le niveau requis.");
            client.account?.Game.character.SpellsRefreshEvent();
        }

        private static bool TryRead(string spell, string rank, out short id, out byte level)
        {
            level = 0;
            return short.TryParse(spell, out id) && id > 0 && byte.TryParse(rank, out level) && level >= 1 && level <= 6;
        }
    }
}
