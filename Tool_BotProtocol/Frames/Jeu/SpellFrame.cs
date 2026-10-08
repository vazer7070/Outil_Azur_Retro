using System;
using System.Collections.Concurrent;
using System.Globalization;
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

        /// <summary>
        /// <c>SLo+</c>/<c>SLo-</c> : option « voir tous les sorts » (<c>Spells.onChangeOption</c> du client). Sans ce préfixe
        /// plus long, le paquet tombait dans <c>SL</c> et vidait la liste des sorts (PR #15).
        /// </summary>
        [MessageAttribution("SLo")]
        public void SeeAllSpellsOption(TcpClient client, string message)
        {
            var account = client.account;
            if (account == null) return;
            string value = message.Length > 3 ? message.Substring(3) : string.Empty;
            account.Game.character.SpellBook.SetSeeAllSpells(value.StartsWith("+", StringComparison.Ordinal));
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
            var character = account.Game.character;
            byte? previous = character.Spells.TryGetValue(id, out Spell known) && known != null ? known.Level : (byte?)null;
            character.Spells.AddOrUpdate(id, key => Spell.ForCharacter(key, level),
                (key, old) => old.CopyForCharacter(level));
            account.Logger.LogInfo("SORTS", "Le serveur a confirmé le niveau " + level + " du sort #" + id + ".");
            character.SpellBook.OnUpgraded(id, level, previous);
            character.SpellsRefreshEvent();
        }

        [MessageAttribution("SUE")]
        public void SpellUpgradeRefused(TcpClient client, string message)
        {
            client.account?.Logger?.LogError("SORTS", "Amélioration du sort refusée : vérifiez les points de sort disponibles et le niveau requis.");
            client.account?.Game.character.SpellBook.OnUpgradeRefused();
            client.account?.Game.character.SpellsRefreshEvent();
        }

        /// <summary>
        /// <c>SB&lt;effet&gt;;&lt;sort&gt;;&lt;valeur&gt;</c> : bonus d'un objet de classe sur un sort (<c>SEND_SB_SPELL_BOOST</c> de
        /// StarLoco, <c>Spells.onSpellBoost</c> du client) ; une valeur 0 retire le bonus.
        /// </summary>
        [MessageAttribution("SB")]
        public void SpellModificator(TcpClient client, string message)
        {
            var account = client.account;
            if (account == null) return;
            string[] fields = message.Substring(2).Split(';');
            if (fields.Length < 3 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int effect)
                || !short.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out short spell)
                || !int.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                account.Logger.LogDanger("SORTS", "Bonus de sort illisible ignoré.");
                return;
            }
            account.Game.character.SpellBook.SetModificator(effect, spell, value);
        }

        /// <summary><c>SF+</c> ouvre la fenêtre d'oubli de sort, <c>SF-</c> la ferme (<c>Spells.onSpellForget</c> du client).</summary>
        [MessageAttribution("SF")]
        public void ForgetWindow(TcpClient client, string message)
        {
            var account = client.account;
            if (account == null) return;
            string sign = message.Length > 2 ? message.Substring(2) : string.Empty;
            if (sign == "+") account.Game.character.SpellBook.SetForgetWindow(true);
            else if (sign == "-") account.Game.character.SpellBook.SetForgetWindow(false);
            else account.Logger.LogDanger("SORTS", "Paquet d'oubli de sort inattendu ignoré.");
        }

        private static bool TryRead(string spell, string rank, out short id, out byte level)
        {
            level = 0;
            return short.TryParse(spell, out id) && id > 0 && byte.TryParse(rank, out level) && level >= 1 && level <= 6;
        }
    }
}
