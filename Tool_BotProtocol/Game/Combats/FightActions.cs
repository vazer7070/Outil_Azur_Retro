using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>
    /// Action <c>GA&lt;id&gt;;&lt;type&gt;;&lt;acteur&gt;;&lt;paramètres&gt;</c> reçue pendant un combat. Un acteur vide désigne
    /// le personnage du compte (<c>GameActions.onActions</c> du client 1.34).
    /// </summary>
    public sealed class FightActionContext
    {
        internal FightActionContext(Fights fight, TcpClient client, GameActionPacket packet, int actorId)
        {
            Fight = fight; Client = client; Packet = packet; ActorId = actorId;
            Arguments = (packet?.Parameters ?? string.Empty).Split(',');
        }

        public Fights Fight { get; }
        public TcpClient Client { get; }
        public Accounts.Accounts Account => Fight?.Owner;
        public GameActionPacket Packet { get; }
        public int ActionId => Packet.ActionId;
        public int ActorId { get; }
        public string Parameters => Packet.Parameters;
        /// <summary>Paramètres découpés sur <c>,</c> (la plupart des actions de combat : <c>&lt;cible&gt;,&lt;valeur&gt;[,&lt;tours&gt;]</c>).</summary>
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>Lit un paramètre entier ; faux s'il manque ou n'est pas un nombre.</summary>
        public bool TryArgument(int index, out int value)
        {
            value = 0;
            return index >= 0 && index < Arguments.Count
                && int.TryParse(Arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>Déclare un gestionnaire d'actions <c>GA</c> de combat, découvert automatiquement dans <c>Tool_BotProtocol</c>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class FightActionHandlerAttribute : Attribute
    {
        public FightActionHandlerAttribute(params int[] actionIds) { ActionIds = (actionIds ?? new int[0]).ToArray(); }
        public IReadOnlyList<int> ActionIds { get; }
    }

    /// <summary>
    /// Table « type d'action <c>GA</c> → gestionnaire » consultée pendant un combat, avant <see cref="GameActionRouter"/>
    /// (actions hors combat). Gestionnaires : méthodes statiques <c>[FightActionHandler(id, …)] static Task|void Nom(FightActionContext)</c>
    /// de <c>Tool_BotProtocol</c>, ou <see cref="Register"/> depuis une autre bibliothèque. Un doublon lève une exception.
    /// </summary>
    public static class FightActionTable
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, Func<FightActionContext, Task>> Handlers = new Dictionary<int, Func<FightActionContext, Task>>();
        private static bool discovered;

        public static void Register(int actionId, Func<FightActionContext, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (Sync)
            {
                EnsureDiscovered();
                if (Handlers.ContainsKey(actionId))
                    throw new InvalidOperationException("L'action de combat GA " + actionId + " a déjà un gestionnaire.");
                Handlers[actionId] = handler;
            }
        }

        public static bool Unregister(int actionId) { lock (Sync) { EnsureDiscovered(); return Handlers.Remove(actionId); } }

        public static bool IsRegistered(int actionId) { lock (Sync) { EnsureDiscovered(); return Handlers.ContainsKey(actionId); } }

        public static int[] RegisteredActions
        { get { lock (Sync) { EnsureDiscovered(); return Handlers.Keys.OrderBy(id => id).ToArray(); } } }

        /// <summary>Exécute le gestionnaire de l'action ; faux si aucun n'est enregistré. Le gestionnaire s'exécute hors de tout verrou.</summary>
        public static async Task<bool> DispatchAsync(FightActionContext context)
        {
            if (context?.Packet == null) return false;
            Func<FightActionContext, Task> handler;
            lock (Sync)
            {
                EnsureDiscovered();
                Handlers.TryGetValue(context.ActionId, out handler);
            }
            if (handler == null) return false;
            Task task = handler(context);
            if (task != null) await task.ConfigureAwait(false);
            return true;
        }

        private static void EnsureDiscovered()
        {
            if (discovered) return;
            var found = new Dictionary<int, Func<FightActionContext, Task>>();
            IEnumerable<MethodInfo> methods = typeof(FightActionTable).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(method => method.IsDefined(typeof(FightActionHandlerAttribute), false))
                .OrderBy(method => method.MetadataToken);
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (!method.IsStatic || parameters.Length != 1 || parameters[0].ParameterType != typeof(FightActionContext)
                    || (method.ReturnType != typeof(Task) && method.ReturnType != typeof(void)))
                    throw new InvalidOperationException(method.DeclaringType?.Name + "." + method.Name
                        + " : un gestionnaire GA de combat doit être statique, recevoir FightActionContext et renvoyer Task ou void.");
                Func<FightActionContext, Task> handler = method.ReturnType == typeof(Task)
                    ? (Func<FightActionContext, Task>)Delegate.CreateDelegate(typeof(Func<FightActionContext, Task>), method)
                    : Wrap((Action<FightActionContext>)Delegate.CreateDelegate(typeof(Action<FightActionContext>), method));
                foreach (FightActionHandlerAttribute attribute in method.GetCustomAttributes(typeof(FightActionHandlerAttribute), false))
                    foreach (int actionId in attribute.ActionIds)
                    {
                        if (found.ContainsKey(actionId))
                            throw new InvalidOperationException("L'action de combat GA " + actionId + " a deux gestionnaires.");
                        found[actionId] = handler;
                    }
            }
            foreach (var pair in found)
                if (Handlers.ContainsKey(pair.Key))
                    throw new InvalidOperationException("L'action de combat GA " + pair.Key + " a deux gestionnaires.");
            foreach (var pair in found) Handlers[pair.Key] = pair.Value;
            discovered = true;
        }

        private static Func<FightActionContext, Task> Wrap(Action<FightActionContext> action) => context =>
        {
            action(context);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Gestionnaires des actions <c>GA</c> de combat, d'après <c>GameActions.onActions</c> du client 1.34 (formats des paramètres)
    /// et <c>SpellEffect</c>/<c>Fight</c> de StarLoco (actions réellement émises). L'état est modifié sous le verrou ;
    /// la carte, le journal et les paquets réinjectés sont traités hors du verrou.
    /// </summary>
    public sealed partial class Fights
    {
        internal Accounts.Accounts Owner => account;
        private int SelfId => account.Game.character.id;

        private static void Invalid(FightActionContext context) =>
            context.Fight.Malformed("GA;" + context.ActionId.ToString(CultureInfo.InvariantCulture), context.Parameters);

        private static bool TryCell(FightActionContext context, int index, out short cell)
        {
            int value; cell = -1;
            if (!context.TryArgument(index, out value) || value < 0 || value > short.MaxValue) return false;
            cell = (short)value;
            return true;
        }

        private static string Amount(int value) => Math.Abs((long)value).ToString(CultureInfo.InvariantCulture);

        private static int Clamp(int value, int delta) => (int)Math.Max(0L, Math.Min(int.MaxValue, (long)value + delta));

        private static string SpellName(int spellId)
        {
            Spell spell = spellId >= short.MinValue && spellId <= short.MaxValue ? Spell.getSpell((short)spellId) : null;
            return spell != null && !string.IsNullOrEmpty(spell.Name) ? "« " + spell.Name + " »" : "le sort n° " + spellId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Retire le lien porteur/porté (sous le verrou).</summary>
        private void Unlink(int carrierId, int childId)
        {
            CombatFighter carrier, child;
            if (carrierId != 0 && fighters.TryGetValue(carrierId, out carrier) && carrier.CarryingId == childId) carrier.CarryingId = 0;
            if (fighters.TryGetValue(childId, out child) && child.CarriedById == carrierId) child.CarriedById = 0;
        }

        /// <summary>Place un combattant (et celui qu'il porte) sur une cellule puis met la carte à jour.</summary>
        private void PlaceFighter(int id, short cell)
        {
            int carried;
            lock (sync)
            {
                if (!InFight) return;
                CombatFighter fighter = GetFighter(id);
                fighter.CellId = cell;
                carried = fighter.CarryingId;
                if (carried != 0) GetFighter(carried).CellId = cell;
            }
            UpdateMapCell(id, cell);
            if (carried != 0) UpdateMapCell(carried, cell);
            MapActor actor = account.Game.Map.GetActor(id);
            if (actor != null) account.Game.Map.NotifyActorUpdated(actor);
        }

        /// <summary><c>4</c> : <c>&lt;cible&gt;,&lt;cellule&gt;</c> téléportation ; <c>5</c> : même format, glissement (poussée, attirance).</summary>
        [FightActionHandler(4, 5)]
        private static void OnRelocate(FightActionContext context)
        {
            int target; short cell;
            if (!context.TryArgument(0, out target) || !TryCell(context, 1, out cell)) { Invalid(context); return; }
            context.Fight.PlaceFighter(target, cell);
        }

        /// <summary><c>11</c> : <c>&lt;cible&gt;,&lt;orientation 0-7&gt;</c>.</summary>
        [FightActionHandler(11)]
        private static void OnDirection(FightActionContext context)
        {
            int target, direction;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out direction) || direction < 0 || direction > 7) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync) { if (!fight.InFight) return; fight.GetFighter(target).Orientation = direction; }
            MapActor actor = fight.account.Game.Map.GetActor(target);
            if (actor == null) return;
            actor.Orientation = direction;
            fight.account.Game.Map.NotifyActorUpdated(actor);
        }

        /// <summary><c>50</c> : l'acteur porte le combattant <c>&lt;cible&gt;</c>, qui rejoint sa cellule (StarLoco : <c>applyEffect_50</c>).</summary>
        [FightActionHandler(50)]
        private static void OnCarry(FightActionContext context)
        {
            int child; short cell;
            if (!context.TryArgument(0, out child) || child == context.ActorId) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter carrier = fight.GetFighter(context.ActorId), carried = fight.GetFighter(child);
                if (carried.CarriedById != 0) fight.Unlink(carried.CarriedById, child);
                if (carrier.CarryingId != 0) fight.Unlink(context.ActorId, carrier.CarryingId);
                carrier.CarryingId = child; carried.CarriedById = context.ActorId;
                carried.CellId = cell = carrier.CellId;
            }
            fight.UpdateMapCell(child, cell);
        }

        /// <summary><c>51</c> : l'acteur lance le combattant qu'il porte sur <c>&lt;cellule&gt;</c> (c'est le porté qui change de cellule).</summary>
        [FightActionHandler(51)]
        private static void OnThrow(FightActionContext context)
        {
            short cell; int child = 0;
            if (!TryCell(context, 0, out cell)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter carrier;
                if (fight.fighters.TryGetValue(context.ActorId, out carrier)) child = carrier.CarryingId;
                if (child != 0) { fight.Unlink(context.ActorId, child); fight.GetFighter(child).CellId = cell; }
            }
            if (child == 0) { fight.account.Logger?.LogDebug("COMBAT", "GA;51 sans combattant porté connu, ignoré."); return; }
            fight.UpdateMapCell(child, cell);
        }

        /// <summary><c>52</c> : <c>&lt;porté&gt;,&lt;cellule&gt;</c>, le combattant porté est déposé.</summary>
        [FightActionHandler(52)]
        private static void OnUncarry(FightActionContext context)
        {
            int child; short cell;
            if (!context.TryArgument(0, out child) || !TryCell(context, 1, out cell)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter carried = fight.GetFighter(child);
                fight.Unlink(carried.CarriedById, child);
                carried.CellId = cell;
            }
            fight.UpdateMapCell(child, cell);
        }

        /// <summary><c>100</c>, <c>108</c>, <c>110</c> : <c>&lt;cible&gt;,&lt;variation de PV&gt;[,&lt;élément&gt;]</c>.</summary>
        [FightActionHandler(100, 108, 110)]
        private static void OnLife(FightActionContext context)
        {
            int target, delta;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out delta)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter fighter = fight.GetFighter(target);
                if (fighter.Life >= 0) fighter.Life = Clamp(fighter.Life, delta);
            }
            string name = fight.NameOf(target);
            fight.Report(context.ActionId, context.ActorId, target, delta < 0 ? name + " perd " + Amount(delta) + " PV."
                : delta > 0 ? name + " gagne " + Amount(delta) + " PV." : name + " ne perd aucun PV.");
        }

        /// <summary>
        /// PA : <c>&lt;cible&gt;,&lt;variation&gt;[,&lt;tours&gt;]</c>. <c>102</c> = PA utilisés (StarLoco libère le sort par
        /// <c>GA;102;id;id,-0</c>), <c>101</c>/<c>168</c> retrait, <c>111</c>/<c>120</c> gain.
        /// </summary>
        [FightActionHandler(101, 102, 111, 120, 168)]
        private static void OnActionPoints(FightActionContext context)
        {
            int target, delta;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out delta)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter fighter = fight.GetFighter(target);
                if (fighter.ActionPoints >= 0) fighter.ActionPoints = Clamp(fighter.ActionPoints, delta);
                if (target == fight.SelfId)
                {
                    if (fight.pa >= 0) fight.pa = Clamp(fight.pa, delta);
                    if (context.ActionId == 102 && delta == 0 && fight.pendingKind == "sort") fight.pendingKind = null;
                }
            }
            if (context.ActionId != 102 && delta != 0)
                fight.Report(context.ActionId, context.ActorId, target, fight.NameOf(target) + (delta < 0 ? " perd " : " gagne ") + Amount(delta) + " PA.");
        }

        /// <summary>PM : même format. <c>129</c> = PM utilisés, <c>127</c>/<c>169</c> retrait, <c>78</c>/<c>128</c> gain.</summary>
        [FightActionHandler(78, 127, 128, 129, 169)]
        private static void OnMovementPoints(FightActionContext context)
        {
            int target, delta;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out delta)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter fighter = fight.GetFighter(target);
                if (fighter.MovementPoints >= 0) fighter.MovementPoints = Clamp(fighter.MovementPoints, delta);
                if (target == fight.SelfId && fight.pm >= 0) fight.pm = Clamp(fight.pm, delta);
            }
            if (context.ActionId != 129 && delta != 0)
                fight.Report(context.ActionId, context.ActorId, target, fight.NameOf(target) + (delta < 0 ? " perd " : " gagne ") + Amount(delta) + " PM.");
        }

        /// <summary>
        /// <c>103</c> : <c>&lt;mort&gt;</c>. Le combattant quitte sa cellule ; s'il portait quelqu'un, celui-ci reste sur la cellule
        /// (<c>uncarriedSprite</c>) ; ses effets et ceux qu'il avait lancés disparaissent (<c>removeEffectsByCasterID</c>).
        /// </summary>
        [FightActionHandler(103)]
        private static void OnDeath(FightActionContext context)
        {
            int target, child = 0; short childCell = -1;
            if (!context.TryArgument(0, out target)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                CombatFighter fighter = fight.GetFighter(target);
                if (fighter.CarryingId != 0)
                {
                    child = fighter.CarryingId; childCell = fighter.CellId;
                    fight.Unlink(target, child);
                    fight.GetFighter(child).CellId = childCell;
                }
                if (fighter.CarriedById != 0) fight.Unlink(fighter.CarriedById, target);
                fighter.IsDead = true; fighter.Life = 0; fighter.CellId = -1; fighter.IsInvisible = false;
                string caster = target.ToString(CultureInfo.InvariantCulture);
                fight.effects.RemoveAll(effect => effect.TargetId == target || effect.CasterId == caster);
                fight.states.Remove(target);
                if (target == fight.SelfId && !fight.spectator) { fight.pendingKind = null; fight.lastMessage = "Votre personnage est hors combat."; }
            }
            fight.UpdateMapCell(target, -1);
            if (child != 0) fight.UpdateMapCell(child, childCell);
            fight.Report(103, context.ActorId, target, fight.NameOf(target) + " est hors de combat.");
        }

        /// <summary><c>132</c> : <c>&lt;cible&gt;</c>, l'acteur retire tous les effets de la cible (<c>terminateAllEffects</c>).</summary>
        [FightActionHandler(132)]
        private static void OnDispel(FightActionContext context)
        {
            int target;
            if (!context.TryArgument(0, out target)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync) { if (!fight.InFight) return; fight.effects.RemoveAll(effect => effect.TargetId == target); }
            fight.Report(132, context.ActorId, target, fight.NameOf(context.ActorId) + " retire les effets de " + fight.NameOf(target) + ".");
        }

        /// <summary>Caractéristiques temporaires : <c>&lt;cible&gt;,&lt;valeur&gt;,&lt;tours&gt;</c> (-1 = sans fin, <c>CharacteristicsManager</c> du client).</summary>
        [FightActionHandler(112, 114, 115, 116, 117, 118, 119, 122, 123, 124, 125, 126, 138, 142, 145, 152, 153, 154, 155, 156, 157,
            160, 161, 162, 163, 182, 606, 607, 608, 609, 610, 611)]
        private static void OnCharacteristic(FightActionContext context)
        {
            int target, value, turns;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out value) || !context.TryArgument(2, out turns)) { Invalid(context); return; }
            Fights fight = context.Fight;
            var effect = new FightEffect(FightEffectSource.GameAction, target, context.ActionId, value, null, null, string.Empty, turns, 0,
                context.ActorId.ToString(CultureInfo.InvariantCulture));
            lock (fight.sync) { if (!fight.InFight) return; fight.AddEffect(effect); }
            fight.Report(context.ActionId, context.ActorId, target, fight.NameOf(target) + " : effet " + context.ActionId.ToString(CultureInfo.InvariantCulture)
                + " (" + value.ToString(CultureInfo.InvariantCulture) + ")" + (turns < 0 ? ", sans fin." : ", " + turns.ToString(CultureInfo.InvariantCulture) + " tour(s)."));
        }

        /// <summary><c>149</c> : <c>&lt;cible&gt;,&lt;apparence d'origine&gt;,&lt;nouvelle apparence&gt;,&lt;tours&gt;</c>.</summary>
        [FightActionHandler(149)]
        private static void OnAppearance(FightActionContext context)
        {
            int target, original, replacement, turns;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out original) || !context.TryArgument(2, out replacement)
                || !context.TryArgument(3, out turns)) { Invalid(context); return; }
            Fights fight = context.Fight;
            var effect = new FightEffect(FightEffectSource.GameAction, target, 149, original, replacement, null, string.Empty, turns, 0,
                context.ActorId.ToString(CultureInfo.InvariantCulture));
            lock (fight.sync) { if (!fight.InFight) return; fight.AddEffect(effect); }
            fight.Report(149, context.ActorId, target, fight.NameOf(target) + " change d’apparence.");
        }

        /// <summary><c>150</c> : <c>&lt;cible&gt;,&lt;tours&gt;</c> ; un nombre de tours positif rend invisible, 0 rend visible.</summary>
        [FightActionHandler(150)]
        private static void OnInvisibility(FightActionContext context)
        {
            int target, turns;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out turns)) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync) { if (!fight.InFight) return; fight.GetFighter(target).IsInvisible = turns > 0; }
            fight.Report(150, context.ActorId, target, fight.NameOf(target) + (turns > 0 ? " devient invisible." : " redevient visible."));
        }

        /// <summary>
        /// <c>950</c> : <c>&lt;cible&gt;,&lt;état&gt;,&lt;1 entre | 0 sort&gt;</c>. Comme le client, un combattant porté qui sort
        /// d'un état est déposé (StarLoco envoie <c>950 …,8,0</c> après <c>GA;51</c>).
        /// </summary>
        [FightActionHandler(950)]
        private static void OnState(FightActionContext context)
        {
            int target, state, active;
            if (!context.TryArgument(0, out target) || !context.TryArgument(1, out state) || !context.TryArgument(2, out active)) { Invalid(context); return; }
            bool enabled = active == 1;
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                HashSet<int> set;
                if (!fight.states.TryGetValue(target, out set)) fight.states[target] = set = new HashSet<int>();
                if (enabled) set.Add(state);
                else
                {
                    set.Remove(state);
                    if (set.Count == 0) fight.states.Remove(target);
                    CombatFighter fighter;
                    if (fight.fighters.TryGetValue(target, out fighter) && fighter.CarriedById != 0) fight.Unlink(fighter.CarriedById, target);
                }
            }
            fight.Report(950, context.ActorId, target, fight.NameOf(target) + (enabled ? " entre dans l’état " : " sort de l’état ")
                + state.ToString(CultureInfo.InvariantCulture) + ".");
        }

        /// <summary>
        /// <c>147</c> (retour au combat), <c>180</c>/<c>181</c> (invocation), <c>185</c>, <c>780</c> : les paramètres sont une entrée
        /// <c>GM</c> sans « GM| » (<c>getGmPacket('+', true).substring(3)</c>) ; elle est réinjectée telle quelle (<c>Game.onMovement</c>).
        /// </summary>
        [FightActionHandler(147, 180, 181, 185, 780)]
        private static async Task OnSpriteEntry(FightActionContext context)
        {
            string entry = context.Parameters;
            if (entry.Length < 2 || (entry[0] != '+' && entry[0] != '~' && entry[0] != '-')) { Invalid(context); return; }
            Fights fight = context.Fight;
            await MessagesReception.ReceptionAsync(context.Client, "GM|" + entry).ConfigureAwait(false);
            string[] fields = entry.Split(';');
            int id;
            if (fields.Length < 4 || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return;
            switch (context.ActionId)
            {
                case 147: fight.Report(147, context.ActorId, id, fight.NameOf(id) + " revient au combat."); break;
                case 180:
                case 181: fight.Report(context.ActionId, context.ActorId, id, fight.NameOf(context.ActorId) + " invoque " + fight.NameOf(id) + "."); break;
                case 780: fight.Report(780, context.ActorId, id, fight.NameOf(context.ActorId) + " ramène " + fight.NameOf(id) + " au combat."); break;
            }
        }

        /// <summary><c>200</c> : <c>&lt;cellule&gt;,&lt;image&gt;</c>, image d'un objet interactif (<c>setObject2Frame</c>).</summary>
        [FightActionHandler(200)]
        private static void OnObjectFrame(FightActionContext context)
        {
            short cell; int frame;
            if (!TryCell(context, 0, out cell) || !context.TryArgument(1, out frame)) { Invalid(context); return; }
            Map map = context.Fight.account.Game.Map;
            InteractiveObjectState previous;
            bool? interactive = map.ObjectStates.TryGetValue(cell, out previous) ? previous.Interactive : null;
            map.SetObjectState(new InteractiveObjectState(cell, frame, interactive));
        }

        /// <summary>
        /// Animations sans état à mémoriser : <c>165</c>, <c>208</c>, <c>228</c> (effets visuels) et <c>501</c>
        /// (<c>&lt;cellule&gt;,&lt;durée&gt;[,&lt;animation&gt;]</c>, animation d'outil).
        /// </summary>
        [FightActionHandler(165, 208, 228, 501)]
        private static void OnVisualOnly(FightActionContext context) { }

        /// <summary><c>300</c> : sort lancé (<c>&lt;sort&gt;,&lt;cellule&gt;,…</c>) ; <c>302</c> : échec critique (<c>&lt;sort&gt;</c>).</summary>
        [FightActionHandler(300, 302)]
        private static void OnSpellCast(FightActionContext context)
        {
            int spell;
            if (!context.TryArgument(0, out spell) || spell < short.MinValue || spell > short.MaxValue) { Invalid(context); return; }
            Fights fight = context.Fight;
            lock (fight.sync)
            {
                if (!fight.InFight) return;
                if (context.ActorId == fight.SelfId)
                {
                    short spellId = (short)spell;
                    fight.pendingConfirmed = true;
                    if (context.ActionId == 300)
                    {
                        fight.lastMessage = "Sort confirmé par le serveur."; fight.lastSpellTurn[spellId] = fight.turn;
                        int count; fight.castsThisTurn.TryGetValue(spellId, out count); fight.castsThisTurn[spellId] = count + 1;
                    }
                    else fight.lastMessage = "Échec critique confirmé par le serveur.";
                }
            }
            string actor = fight.NameOf(context.ActorId);
            fight.Report(context.ActionId, context.ActorId, 0, context.ActionId == 300 ? actor + " lance " + SpellName(spell) + "."
                : actor + " rate " + SpellName(spell) + " (échec critique).");
        }

        /// <summary>Actions affichées seulement dans le journal de combat du client (<c>INFO_FIGHT_CHAT</c>).</summary>
        [FightActionHandler(104, 105, 106, 107, 130, 140, 151, 164, 166, 301, 303, 304, 305, 306, 307, 308, 309)]
        private static void OnJournalOnly(FightActionContext context)
        {
            Fights fight = context.Fight;
            int target = 0, value;
            string actor = fight.NameOf(context.ActorId), text = null;
            switch (context.ActionId)
            {
                case 104: text = actor + " ne peut pas se déplacer."; break;
                case 105:
                case 164:
                    if (context.TryArgument(0, out target) && context.TryArgument(1, out value))
                        text = fight.NameOf(target) + " réduit les dommages de " + Amount(value) + (context.ActionId == 164 ? " %." : ".");
                    break;
                case 106:
                    if (context.TryArgument(0, out target) && context.TryArgument(1, out value))
                        text = fight.NameOf(target) + (value == 1 ? " renvoie le sort." : " ne renvoie pas le sort.");
                    break;
                case 107:
                    if (context.TryArgument(0, out target) && context.TryArgument(1, out value))
                        text = fight.NameOf(target) + " renvoie " + Amount(value) + " dommages.";
                    break;
                case 130:
                    if (context.TryArgument(0, out value)) text = actor + " vole " + Amount(value) + " kamas.";
                    break;
                case 140:
                    if (context.TryArgument(0, out target)) text = fight.NameOf(target) + " passe son prochain tour.";
                    break;
                case 151:
                    if (context.TryArgument(0, out value))
                        text = value == -1 ? actor + " est arrêté par un obstacle invisible."
                            : actor + " ne peut pas lancer " + SpellName(value) + " : obstacle invisible.";
                    break;
                case 166:
                    if (context.TryArgument(1, out value)) text = actor + " récupère " + Amount(value) + " PA.";
                    break;
                case 301: text = actor + " : coup critique."; break;
                case 303: text = actor + " attaque au corps à corps."; break;
                case 304: text = actor + " : coup critique au corps à corps."; break;
                case 305: text = actor + " : échec critique au corps à corps."; break;
                case 306:
                case 307:
                    if (context.TryArgument(0, out value) && context.TryArgument(5, out target))
                        text = actor + (context.ActionId == 306 ? " déclenche un piège (" : " subit un glyphe (") + SpellName(value) + ") de " + fight.NameOf(target) + ".";
                    break;
                case 308:
                case 309:
                    if (context.TryArgument(0, out target) && context.TryArgument(1, out value))
                        text = fight.NameOf(target) + " esquive la perte de " + Amount(value) + (context.ActionId == 308 ? " PA." : " PM.");
                    break;
            }
            if (text == null) { Invalid(context); return; }
            fight.Report(context.ActionId, context.ActorId, target, text);
        }

        /// <summary>
        /// <c>999</c> : les paramètres sont un paquet complet (<c>GTL|…</c>, <c>GDZ+…</c>, <c>GDC…</c>, <c>GIE…</c>), exécuté comme s'il
        /// venait du serveur (<c>aks.processCommand</c>). Un <c>GA;999</c> imbriqué est refusé pour borner la récursion.
        /// </summary>
        [FightActionHandler(999)]
        private static Task OnEmbeddedPacket(FightActionContext context)
        {
            string inner = context.Parameters;
            if (inner.Length < 2) { Invalid(context); return Task.CompletedTask; }
            if (inner.StartsWith("GA", StringComparison.Ordinal))
            {
                GameActionPacket nested = GameActionPacket.Parse(inner);
                if (nested != null && nested.ActionId == 999) { Invalid(context); return Task.CompletedTask; }
            }
            return MessagesReception.ReceptionAsync(context.Client, inner);
        }
    }
}
