using MySql.Data.MySqlClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;

namespace Tools_protocol.Managers
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public  class EmuManager : Attribute
    {
        public string Emulator { get; }
        public string Context { get; }

        public EmuManager(string emulator, string context)
        {
            Emulator = emulator;
            Context = context;
        }
        public static object GetEmulatorVariable(
    string emulator,
    string context,
    string variableName,
    object[] constructorParams,
    string methodToCallBeforeGettingVariable,
    Assembly[] assembliesToSearch)
        {
            if (assembliesToSearch == null)
                assembliesToSearch = AppDomain.CurrentDomain.GetAssemblies();

            Type targetType = null;

            foreach (Assembly assembly in assembliesToSearch)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (Type type in types)
                {
                    object[] attrs = type.GetCustomAttributes(typeof(EmuManager), false);
                    foreach (object attr in attrs)
                    {
                        EmuManager emuAttr = attr as EmuManager;
                        if (emuAttr != null &&
                            string.Equals(emuAttr.Emulator, emulator, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(emuAttr.Context, context, StringComparison.OrdinalIgnoreCase))
                        {
                            targetType = type;
                            break;
                        }
                    }

                    if (targetType != null)
                        break;
                }

                if (targetType != null)
                    break;
            }

            if (targetType == null)
            {
                MessageBox.Show("Aucune classe trouvée pour '" + emulator + "' / contexte '" + context + "'", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            object instance = CreateInstanceWithParameters(targetType, constructorParams);

            if (!string.IsNullOrEmpty(methodToCallBeforeGettingVariable))
            {
                MethodInfo method = targetType.GetMethod(methodToCallBeforeGettingVariable, BindingFlags.Public | BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(instance, null);
                }
                else
                {
                    MessageBox.Show("Méthode '" + methodToCallBeforeGettingVariable + "' introuvable.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            PropertyInfo prop = targetType.GetProperty(variableName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanRead)
            {
                return prop.GetValue(prop.GetGetMethod().IsStatic ? null : instance, null);
            }

            FieldInfo field = targetType.GetField(variableName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (field != null)
            {
                return field.GetValue(field.IsStatic ? null : instance);
            }

            MessageBox.Show("Variable '" + variableName + "' introuvable dans '" + targetType.Name + "'.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }



        private static bool ParametersMatch(ParameterInfo[] methodParams, object[] providedParams)
        {
            if (providedParams == null)
                return methodParams.Length == 0;

            if (methodParams.Length != providedParams.Length)
                return false;

            for (int i = 0; i < methodParams.Length; i++)
            {
                if (providedParams[i] == null)
                    continue;

                if (!methodParams[i].ParameterType.IsAssignableFrom(providedParams[i].GetType()))
                    return false;
            }

            return true;
        }

        private static object CreateInstanceWithParameters(Type type, object[] constructorParams)
        {
            if (constructorParams == null)
                constructorParams = new object[0];

            var constructors = type.GetConstructors();

            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                if (ParametersMatch(parameters, constructorParams))
                {
                    return ctor.Invoke(constructorParams);
                }
            }

            MessageBox.Show($"Aucun constructeur valide trouvé pour le type '{type.Name}'", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        public static List<Dictionary<string, object>> GetAllAccountPropertiesForEmulator(string emulatorName)
        {
            var results = new List<Dictionary<string, object>>();
            var asm = Assembly.GetExecutingAssembly();

            var targetType = asm.GetTypes()
                .FirstOrDefault(t =>
                {
                    var attr = (EmuManager)Attribute.GetCustomAttribute(t, typeof(EmuManager));
                    return attr != null && string.Equals(attr.Emulator, emulatorName, StringComparison.OrdinalIgnoreCase) && string.Equals(attr.Context, "AccountList", StringComparison.OrdinalIgnoreCase);
                });

            if (targetType == null)
                throw new Exception($"Classe avec [EmuManager(\"{emulatorName}\")] introuvable.");

            var instance = Activator.CreateInstance(targetType);


            var field = targetType.GetField("AllAccount", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object dictObj = field?.GetValue(field.IsStatic ? null : instance);

            if (dictObj == null)
                throw new Exception("Le dictionnaire 'AllAccount' est introuvable ou vide.");

            var dict = dictObj as IDictionary;
            if (dict == null)
                throw new Exception("'AllAccount' n'est pas un IDictionary.");

            if (dict.Count == 0)
            {
                var loadMethod = targetType.GetMethod("AllAccounts", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                loadMethod?.Invoke(loadMethod.IsStatic ? null : instance, null);
            }

            foreach (var val in dict.Values)
            {
                var valType = val.GetType();

                // Récupère toutes les propriétés publiques simples
                var props = valType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && IsSimpleType(p.PropertyType));

                var accountData = new Dictionary<string, object>();

                foreach (var prop in props)
                {
                    var value = prop.GetValue(val);
                    accountData[prop.Name] = value;
                }

                results.Add(accountData);
            }

            return results;
        }

        private static bool IsSimpleType(Type type)
        {
            return
                type.IsPrimitive ||
                type == typeof(string) ||
                type == typeof(decimal) ||
                type == typeof(Guid) ||
                type == typeof(DateTime);
        }


        public static object FindAndCallEmuHandler(string emulator, string context, string methodName = "init", object[] parameters = null)
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();

            var targetType = types.FirstOrDefault(t =>
            {
                var attr = (EmuManager)Attribute.GetCustomAttribute(t, typeof(EmuManager));
                return attr != null &&
                       attr.Emulator.Equals(emulator, StringComparison.OrdinalIgnoreCase) &&
                       attr.Context.Equals(context, StringComparison.OrdinalIgnoreCase);
            });

            if (targetType == null)
            {
                MessageBox.Show($"Aucune classe trouvée pour l’émulateur '{emulator}' avec le contexte '{context}'.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            var instance = Activator.CreateInstance(targetType);
            var method = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) &&
                    ParametersMatch(m.GetParameters(), parameters));

            if (method == null)
            {
                MessageBox.Show($"Méthode '{methodName}' introuvable dans '{targetType.Name}' avec les paramètres fournis.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            return method.Invoke(method.IsStatic ? null : instance, parameters);
        }
        private static bool VariableParametersMatch(ParameterInfo[] methodParams, object[] providedParams)
        {
            if (providedParams == null) return methodParams.Length == 0;
            if (methodParams.Length != providedParams.Length) return false;

            for (int i = 0; i < methodParams.Length; i++)
            {
                if (providedParams[i] == null)
                    continue;

                if (!methodParams[i].ParameterType.IsAssignableFrom(providedParams[i].GetType()))
                    return false;
            }

            return true;
        }

        public static MethodInfo FindEmuMethod(object target, string emulator, string context)
        {
            if (!Emu.Contains(emulator, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show("Émulateur non reconnu: " + emulator, "Émulateur introuvable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            return target.GetType().GetMethods()
                .FirstOrDefault(m =>
                {
                    var attr = (EmuManager)Attribute.GetCustomAttribute(m, typeof(EmuManager));
                    return attr != null &&
                           attr.Emulator.Equals(emulator, StringComparison.OrdinalIgnoreCase) &&
                           attr.Context.Equals(context, StringComparison.OrdinalIgnoreCase);
                });
        }


        public static string EMUSELECTED;

		 public static bool NOEMU = false;

		private static string[] Emu = new string[] { "Kryone", "Sunshine", "Codebreak" };


        public static void InitEmu(string emu)
		{
			if (!Emu.Contains(emu))
			{
				NOEMU = true;
			}
		}


		public static string RecupPanoRow(string panocol, string panoname)
        {
			string row = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
					string query = QueryBuilder.SelectFromQuery(new string[] { panocol }, JsonManager.SearchAuth("panoplies"), "name", panoname);
					using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
					{
						try
						{
							connection.Open();
							MySqlDataReader R = new MySqlCommand(query, connection).ExecuteReader();
							while (R.Read())
							{
								row = R.GetString(panocol);
							}
							R.Close();
							R.Dispose();
							connection.Close();
							connection.Dispose();
							return row;
						}
						catch (MySqlException) { return null; }
					}
            }
			return row;
        }
		public static string ReturnInfoCol(string table)
        {
			string colSelected = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
                    switch (table)
                    {
						case "pano":
							colSelected = "name";
							break;
                    }
					break;
            }
			return colSelected;
        }
		public static string UpdateRowPano(string panorow, string IDtemplate)
        {
			List<string> iteminpano = new List<string>();
			string newRow = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
                    if (!string.IsNullOrEmpty(panorow))
                    {
                        if (panorow.Contains(","))
                        {
							foreach (string h in panorow.Split(','))
							{
								iteminpano.Add(h);

							}
							iteminpano.Add(IDtemplate);
							newRow = string.Join(",", iteminpano);
						}
                        else
                        {
							iteminpano.Add(panorow);
							iteminpano.Add(IDtemplate);
							newRow = string.Join(",", iteminpano);
						}
                    }
                    else
                    {
						newRow = IDtemplate;
                    }
					return newRow;
            }
			return newRow;
        }
		public static string ReturnPanoCol()
        {
			string C = "";
            switch (EMUSELECTED)
            {
				case "Kryone":
					C = "items";
					return C;
            }
			return C;
        }
		public static void ExecuteQueryByEmu(string key, string query)
        {
            switch (EMUSELECTED)
            {
				case "Kryone":
                    switch (key)
                    {
						case "pano":
							DatabaseManager.UpdateQuery(query);
							break;
						case "craft":
							DatabaseManager.UpdateQuery(query);
							break;
						case "template":
							DatabaseManager.UpdateQuery(query);
							break;
						case "item":
							DatabaseManager2.UpdateQuery(query);
							break;
                    }
					break;
            }
        }
		public static string ReturnTable(string table, string emu)
		{
            switch (emu)
            {
				case "Kryone":
					switch (table)
					{
						case "comptes":
							return JsonManager.SearchAuth(table);
						case "perso":
							return JsonManager.SearchAuth(table);
						case "Template":
							return JsonManager.SearchAuth(table);
						case "crafts":
							return JsonManager.SearchAuth(table);
						case "panoplies":
							return JsonManager.SearchAuth(table);
						case "cellule":
							return JsonManager.SearchAuth(table);
						case "items":
							return JsonManager.SearchWorld(table);
						case "endfight":
							return JsonManager.SearchAuth(table);
						case "groupe_monstre":
							return JsonManager.SearchAuth(table);
					}
					break;
				case "Codebreak":
                    switch (table)
                    {
						case "comptes":
							return JsonManager.SearchAuth(table);
						case "perso":
							return JsonManager.SearchWorld(table);
					}
					break;
					case "Sunshine":
                    switch (table)
					{
                        case "comptes":
                            return JsonManager.SearchAuth(table);
                    }
					break;
            }
			return "";
		}
	}
}