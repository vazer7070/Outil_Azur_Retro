using MySql.Data.MySqlClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tools_protocol.Emulators;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Managers
{
    /// <summary>
    /// Marque les classes de données propres à un émulateur (par exemple la liste des comptes).
    /// Le choix de l'émulateur et la résolution des tables passent par <see cref="EmulatorRegistry"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class EmuManager : Attribute
    {
        public string Emulator { get; }
        public string Context { get; }

        public EmuManager(string emulator, string context)
        {
            Emulator = emulator;
            Context = context;
        }

        /// <summary>Identifiant de l'émulateur configuré ; une valeur inconnue sélectionne le profil vide.</summary>
        public static string EMUSELECTED
        {
            get { return EmulatorRegistry.Current.Id; }
            set { EmulatorRegistry.Select(value); }
        }

        public static List<Dictionary<string, object>> GetAllAccountPropertiesForEmulator(string emulatorName)
        {
            Type targetType = FindType(emulatorName, "AccountList")
                ?? throw new InvalidOperationException(string.IsNullOrWhiteSpace(emulatorName)
                    ? "Aucun émulateur n'est configuré : choisissez-en un dans la configuration."
                    : $"La lecture des comptes n'est pas disponible pour l'émulateur « {emulatorName} ».");

            object instance = Activator.CreateInstance(targetType);
            FieldInfo field = targetType.GetField("AllAccount", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            IDictionary dict = field?.GetValue(field.IsStatic ? null : instance) as IDictionary
                ?? throw new InvalidOperationException($"La liste des comptes de {targetType.FullName} est introuvable.");

            if (dict.Count == 0)
            {
                MethodInfo load = targetType.GetMethod("AllAccounts", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                load?.Invoke(load.IsStatic ? null : instance, null);
            }

            var results = new List<Dictionary<string, object>>();
            foreach (object account in dict.Values)
            {
                var row = new Dictionary<string, object>();
                foreach (PropertyInfo property in account.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.CanRead && IsSimpleType(property.PropertyType))
                        row[property.Name] = property.GetValue(account);
                }
                results.Add(row);
            }
            return results;
        }

        private static Type FindType(string emulator, string context)
        {
            // Un émulateur qui reprend le modèle de données d'un autre (StarLoco → Kryone) utilise ses classes.
            string model = EmulatorRegistry.Find(emulator)?.DataModel ?? emulator;
            return Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(type =>
            {
                var attribute = (EmuManager)GetCustomAttribute(type, typeof(EmuManager));
                return attribute != null &&
                       string.Equals(attribute.Emulator, model, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(attribute.Context, context, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
                   type == typeof(Guid) || type == typeof(DateTime);
        }

        /// <summary>Nom réel d'une table logique pour l'émulateur indiqué, ou chaîne vide.</summary>
        public static string ReturnTable(string table, string emu)
        {
            return (EmulatorRegistry.Find(emu) ?? EmulatorRegistry.None).Table(table);
        }

        private static bool CanCreateItems => EmulatorRegistry.Current.Supports(EmulatorFeature.ItemCreation);

        public static string RecupPanoRow(string panocol, string panoname)
        {
            if (!CanCreateItems) return "";
            string query = QueryBuilder.SelectFromQuery(new[] { panocol }, EmulatorRegistry.Current.Table("panoplies"), "name", panoname);
            try
            {
                using (var connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("panoplies")))
                using (var command = new MySqlCommand(query, connection))
                {
                    connection.Open();
                    string row = "";
                    using (MySqlDataReader reader = command.ExecuteReader())
                        while (reader.Read())
                            row = reader.GetString(panocol);
                    return row;
                }
            }
            catch (MySqlException) { return null; }
        }

        public static string ReturnInfoCol(string table)
        {
            return CanCreateItems && table == "pano" ? "name" : "";
        }

        /// <summary>Ajoute un modèle à la liste d'objets d'une panoplie (identifiants séparés par des virgules).</summary>
        public static string UpdateRowPano(string panorow, string IDtemplate)
        {
            if (!CanCreateItems) return "";
            if (string.IsNullOrEmpty(panorow)) return IDtemplate;
            return panorow + "," + IDtemplate;
        }

        public static string ReturnPanoCol()
        {
            return CanCreateItems ? "items" : "";
        }
    }
}
