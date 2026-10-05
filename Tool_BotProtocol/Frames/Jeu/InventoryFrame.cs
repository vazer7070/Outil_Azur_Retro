using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Inventaire : <c>OAK</c>, <c>OAE</c>, <c>OR</c>, <c>OQ</c>, <c>OC</c>, <c>OM</c>, <c>OS</c>, <c>OT</c>, <c>OK</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot F13b.
    /// </summary>
    internal class InventoryFrame : Frame
    {
        /// <summary>
        /// OAK : ajout d'objets selon <c>Items.onAdd</c> du client 1.34. Enregistrements séparés par « * »,
        /// chacun préfixé par son type : « O » = fiches d'objets séparées par « ; », « G » = ignoré par le client.
        /// StarLoco envoie <c>OAKO&lt;fiche&gt;;</c>, c'est-à-dire un seul enregistrement « O ».
        /// </summary>
        [MessageAttribution("OAK")]
        public void GetObjects(TcpClient client, string message)
        {
            foreach (string record in message.Substring(3).Split('*'))
            {
                if (string.IsNullOrEmpty(record)) continue;
                switch (record[0])
                {
                    case 'O': client.account.Game.character.Inventory.Add_Items(record.Substring(1)); break;
                    case 'G': break;
                    default: client.account.Logger.LogError("INVENTAIRE", "Type d'ajout d'objet inconnu : " + record[0]); break;
                }
            }
        }

        /// <summary>OAE : refus d'ajout ou d'équipement, mêmes codes que le client (A déjà équipé, L niveau, F inventaire plein).</summary>
        [MessageAttribution("OAE")]
        public void GetObjectsError(TcpClient client, string message)
        {
            string code = message.Length > 3 ? message.Substring(3, 1) : string.Empty;
            string reason;
            switch (code)
            {
                case "A": reason = "Cet objet est déjà équipé."; break;
                case "L": reason = "Votre niveau est trop bas pour cet objet."; break;
                case "F": reason = "Votre inventaire est plein."; break;
                default: reason = "Le serveur a refusé l'opération sur l'objet (" + message + ")."; break;
            }
            client.account.Logger.LogError("INVENTAIRE", reason);
            client.account.Game.character.Inventory.NotifyRefused(reason);
        }

        [MessageAttribution("OR")]
        public void EliminateObject(TcpClient client, string message)
        {
            if (uint.TryParse(message.Substring(2), out uint inventoryId))
                client.account.Game.character.Inventory.SuppItem(inventoryId, 0, false);
        }

        [MessageAttribution("OQ")]
        public void ModifyQuantityItems(TcpClient client, string message) => client.account.Game.character.Inventory.Modify_Items(message.Substring(2));

        /// <summary>OC : fiches mises à jour. Le client ignore le troisième caractère (StarLoco envoie <c>OC|…</c> ou <c>OCO…</c>), puis sépare par « * » et « ; ».</summary>
        [MessageAttribution("OC")]
        public void ChangeObjects(TcpClient client, string message)
        {
            if (message.Length <= 3) return;
            foreach (string group in message.Substring(3).Split('*'))
                foreach (string record in group.Split(';'))
                    if (!string.IsNullOrWhiteSpace(record) && !client.account.Game.character.Inventory.Update_Item(record))
                        client.account.Logger.LogError("INVENTAIRE", "Fiche d'objet modifiée illisible : " + record);
        }

        /// <summary>OM&lt;id&gt;|&lt;emplacement&gt; : objet déplacé ; un emplacement vide ou non numérique signifie le sac.</summary>
        [MessageAttribution("OM")]
        public void MoveObject(TcpClient client, string message)
        {
            string[] parts = message.Substring(2).Split('|');
            if (!uint.TryParse(parts[0], out uint inventoryId)) return;
            string position = parts.Length > 1 ? parts[1] : string.Empty;
            if (client.account.Game.character.Inventory.Move_Item(inventoryId, position))
                client.account.Logger.LogInfo("INVENTAIRE", "Objet " + inventoryId + (string.IsNullOrEmpty(position) ? " rangé dans le sac." : " équipé à l'emplacement " + position + "."));
        }

        /// <summary>OS+&lt;panoplie&gt;|&lt;objets&gt;|&lt;bonus&gt; ou OS-&lt;panoplie&gt; : panoplie portée.</summary>
        [MessageAttribution("OS")]
        public void ItemSet(TcpClient client, string message)
        {
            string payload = message.Substring(2);
            if (client.account.Game.character.Inventory.Apply_ItemSet(payload))
                client.account.Logger.LogInfo("INVENTAIRE", (payload[0] == '+' ? "Panoplie portée : " : "Panoplie retirée : ") + payload.Substring(1).Split('|')[0]);
        }

        /// <summary>OT&lt;métier&gt; : outil de métier équipé ; OT seul signifie aucun outil.</summary>
        [MessageAttribution("OT")]
        public void Tool(TcpClient client, string message)
        {
            CharacterClass character = client.account.Game.character;
            character.CurrentJobTool = int.TryParse(message.Substring(2), out int job) ? job : (int?)null;
            client.account.Logger.LogInfo("INVENTAIRE", character.CurrentJobTool.HasValue ? "Outil du métier " + character.CurrentJobTool + " équipé." : "Aucun outil de métier équipé.");
        }

        /// <summary>
        /// OK : condition d'utilisation à confirmer (<c>OKU&lt;objet&gt;|&lt;cible&gt;|&lt;cellule&gt;|&lt;modèle&gt;</c> ou
        /// <c>OKG…|&lt;kamas&gt;</c>). Le client affiche une confirmation ; le bot journalise sans répondre.
        /// </summary>
        [MessageAttribution("OK")]
        public void ItemUseCondition(TcpClient client, string message)
        {
            string[] parts = message.Length > 3 ? message.Substring(3).Split('|') : new string[0];
            string objectId = parts.Length > 0 ? parts[0] : "?";
            string last = parts.Length > 3 ? parts[3] : "?";
            char kind = message.Length > 2 ? message[2] : ' ';
            string text = kind == 'G'
                ? "Utiliser l'objet " + objectId + " coûte " + last + " kamas : confirmation attendue, aucune réponse automatique."
                : "Le serveur demande confirmation pour utiliser l'objet " + objectId + " (modèle " + last + ") : aucune réponse automatique.";
            client.account.Logger.LogDanger("INVENTAIRE", text);
            client.account.Game.character.Inventory.NotifyRefused(text);
        }
    }
}
