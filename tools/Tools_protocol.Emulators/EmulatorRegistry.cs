using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools_protocol.Emulators
{
    /// <summary>Liste des émulateurs connus et émulateur actuellement configuré.</summary>
    public static class EmulatorRegistry
    {
        private static readonly List<EmulatorProfile> profiles = new List<EmulatorProfile>
        {
            new KryoneProfile(),
            new StarLocoProfile(),
            new SunshineProfile(),
            new CodebreakProfile(),
        };

        /// <summary>Profil utilisé quand aucun émulateur valide n'est configuré : aucun outil SQL.</summary>
        public static readonly EmulatorProfile None = new NoEmulatorProfile();

        public static IReadOnlyList<EmulatorProfile> All => profiles;

        public static EmulatorProfile Current { get; private set; } = None;

        public static bool HasEmulator => !ReferenceEquals(Current, None);

        public static EmulatorProfile Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return profiles.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Sélectionne l'émulateur ; renvoie faux s'il est inconnu (le profil vide est alors utilisé).</summary>
        public static bool Select(string id)
        {
            EmulatorProfile profile = Find(id);
            Current = profile ?? None;
            return profile != null;
        }

        /// <summary>Ajoute un émulateur (par exemple depuis un module externe).</summary>
        public static void Register(EmulatorProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (Find(profile.Id) != null) throw new InvalidOperationException($"L'émulateur {profile.Id} est déjà enregistré.");
            profiles.Add(profile);
        }

        private sealed class NoEmulatorProfile : EmulatorProfile
        {
            public override string Id => "";
            public override string DisplayName => "Aucun";
            public override string Summary => "Aucun émulateur reconnu : seuls les outils sans base de données sont disponibles.";
            public override EmulatorFeature Features => EmulatorFeature.None;
        }
    }
}
