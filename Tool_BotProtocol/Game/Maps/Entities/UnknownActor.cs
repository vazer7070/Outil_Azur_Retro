using System.Collections.Generic;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Acteur d'un type que le bot ne décode pas (mutants <c>-7</c>/<c>-8</c>, type inconnu) : position, sprite et champs bruts
    /// sont conservés pour le rendu et le diagnostic, rien n'est interprété.
    /// </summary>
    public sealed class UnknownActor : MapActor
    {
        public override ActorKind Kind => ActorKind.Unknown;
        public IReadOnlyList<string> Fields { get; set; } = new string[0];
    }
}
