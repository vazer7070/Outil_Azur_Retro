using System;
using System.Globalization;

namespace Tool_Editor.items
{
    public sealed class ItemClientDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Graphic { get; set; }
        public int Type { get; set; }
        public int Level { get; set; }
        public int Weight { get; set; }
        public int Price { get; set; }
        public string Condition { get; set; }
        public bool ForgeMagic { get; set; }
        public bool Usable { get; set; }
        public bool TwoHands { get; set; }
        public int ActionPoints { get; set; }
        public int MinimumRange { get; set; }
        public int MaximumRange { get; set; }
        public int CriticalRate { get; set; }
        public int FailureRate { get; set; }
        public int CriticalBonus { get; set; }

        public void Validate()
        {
            if (Id <= 0 || Type <= 0 || Level <= 0 || string.IsNullOrWhiteSpace(Name))
                throw new ArgumentException("L'identifiant, le type, le niveau et le nom de l'objet sont obligatoires.");
            foreach (int value in new[] { Graphic, Weight, Price, ActionPoints, MinimumRange, MaximumRange, CriticalRate, FailureRate, CriticalBonus })
                if (value < 0) throw new ArgumentException("Les valeurs numériques de l'objet doivent être positives ou nulles.");
            if (MinimumRange > MaximumRange)
                throw new ArgumentException("La portée minimale ne peut pas dépasser la portée maximale.");
        }

        public static ItemClientDefinition FromCreation(int id, string name, string description, string gfx,
            string type, string level, bool fm, string pods, string weapon, string condition,
            string price, bool usable, bool twoHands)
        {
            string[] parts = (weapon ?? "").Split(';');
            if (parts.Length < 6) throw new ArgumentException("Les informations d'arme sont incomplètes.");
            var item = new ItemClientDefinition {
                Id = id, Name = name, Description = description, Graphic = Number(gfx), Type = Number(type),
                Level = Number(level), Weight = Number(pods), Price = Number(price), ForgeMagic = fm,
                Condition = condition, Usable = usable, TwoHands = twoHands, ActionPoints = Number(parts[0]),
                MinimumRange = Number(parts[1]), MaximumRange = Number(parts[2]), CriticalRate = Number(parts[3]),
                FailureRate = Number(parts[4]), CriticalBonus = Number(parts[5])
            };
            item.Validate(); return item;
        }

        private static int Number(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 0)
                throw new FormatException("Une valeur numérique de l'objet client est invalide.");
            return number;
        }
    }
}
