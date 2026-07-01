using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Goose.Monsterpatch.ShinyOddsPatcher
{
    public static class ShinyOddsPatcher
    {
        public const string PatcherName = "Monsterpatch Extra Options - Shiny Odds";
        public const string PatcherVersion = "v0.1.2";

        private const int VanillaOddsDenominator = 1000;
        private const int MinOddsDenominator = 1;
        private const int MaxOddsDenominator = 1000000;

        private const string PlayerPrefsOddsKey = "GooseExtraOptionsShinyOddsDenominator";
        private const string PlayerPrefsIndexKey = "GooseExtraOptionsShinyOddsIndex";

        private static readonly int[] MenuOddsChoices = new[] { 1000, 500, 250, 100, 50, 25 };

        private static int _oddsDenominator = VanillaOddsDenominator;
        private static int _oddsIndex = 0;
        private static bool _initialized;
        private static bool _patchOptionsMenu = true;
        private static bool _startSelectionOnShinyRow = false;

        public static void InitializeFeature()
        {
            LoadConfig();
            Console.WriteLine($"[{PatcherName}] Loaded. Default OddsDenominator={_oddsDenominator} ({FormatOdds(_oddsDenominator)}). PatchOptionsMenu={_patchOptionsMenu}, StartSelectionOnShinyRow={_startSelectionOnShinyRow}.");
        }

        public static void PatchFeature(AssemblyDefinition assembly)
        {
            if (!_initialized)
                LoadConfig();

            if (assembly == null || assembly.MainModule == null)
            {
                Console.WriteLine($"[{PatcherName}] Assembly was null; nothing patched.");
                return;
            }

            ModuleDefinition module = assembly.MainModule;

            TypeDefinition gameScript = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            TypeDefinition optionsMenuScript = module.Types.FirstOrDefault(t => t.Name == "OptionsMenuScript");

            int shinyFormulaReplacements = PatchShinyFormula(module, gameScript);
            int menuPatches = 0;

            if (_patchOptionsMenu)
            {
                menuPatches += PatchOptionsRefreshUI(module, optionsMenuScript);
                menuPatches += PatchFilterChoiceGuard(module, optionsMenuScript, "FilterLeft", -1);
                menuPatches += PatchFilterChoiceGuard(module, optionsMenuScript, "FilterRight", 1);
            }

            if (shinyFormulaReplacements == 0)
            {
                Console.WriteLine($"[{PatcherName}] WARNING: No shiny odds formula constants were patched. Expected 1000 near UnityEngine.Random.Range in GameScript.SetUniqueIDAndInitializeMon.");
            }
            else
            {
                Console.WriteLine($"[{PatcherName}] Patched shiny formula to read runtime PlayerPrefs key '{PlayerPrefsOddsKey}' with default {FormatOdds(_oddsDenominator)}. Replacements: {shinyFormulaReplacements}.");
            }

            if (_patchOptionsMenu)
                Console.WriteLine($"[{PatcherName}] Options menu patch result: {menuPatches} method patch(es). Clones the native Filter row into a separate Shiny Odds row under Filter and uses robust selectable navigation wiring and ancestor-aware Shiny row button handling.");
        }

        public static void FinishFeature()
        {
        }

        private static int PatchShinyFormula(ModuleDefinition module, TypeDefinition gameScript)
        {
            if (gameScript == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not find GameScript.");
                return 0;
            }

            MethodDefinition method = gameScript.Methods.FirstOrDefault(m => m.Name == "SetUniqueIDAndInitializeMon" && m.HasBody);
            if (method == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not find GameScript.SetUniqueIDAndInitializeMon.");
                return 0;
            }

            MethodReference playerPrefsGetInt = FindMethodReference(module, "UnityEngine.PlayerPrefs", "GetInt", 2);
            if (playerPrefsGetInt == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not find UnityEngine.PlayerPrefs.GetInt(string,int). Shiny formula not patched.");
                return 0;
            }

            int replacements = 0;
            IList<Instruction> instructions = method.Body.Instructions;
            ILProcessor il = method.Body.GetILProcessor();

            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction ins = instructions[i];
                if (!LoadsInt(ins, VanillaOddsDenominator))
                    continue;

                if (!HasRandomRangeAfter(instructions, i, 12))
                    continue;

                il.InsertBefore(ins, Instruction.Create(OpCodes.Ldstr, PlayerPrefsOddsKey));
                il.InsertBefore(ins, Instruction.Create(OpCodes.Ldc_I4, _oddsDenominator));
                ins.OpCode = OpCodes.Call;
                ins.Operand = module.ImportReference(playerPrefsGetInt);
                replacements++;
            }

            return replacements;
        }

        private static int PatchOptionsRefreshUI(ModuleDefinition module, TypeDefinition optionsMenuScript)
        {
            if (optionsMenuScript == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not find OptionsMenuScript.");
                return 0;
            }

            MethodDefinition refresh = optionsMenuScript.Methods.FirstOrDefault(m => m.Name == "RefreshUI" && m.HasBody);
            if (refresh == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not find OptionsMenuScript.RefreshUI.");
                return 0;
            }

            MethodReference ensureRow = ImportRuntimeMethod(module, "RuntimeEnsureShinyRow", module.TypeSystem.Void, module.TypeSystem.Object);
            if (ensureRow == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not create runtime method reference for RuntimeEnsureShinyRow.");
                return 0;
            }

            ILProcessor il = refresh.Body.GetILProcessor();
            List<Instruction> rets = refresh.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            int patched = 0;

            foreach (Instruction ret in rets)
            {
                // RuntimeEnsureShinyRow(this);
                il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, Instruction.Create(OpCodes.Call, ensureRow));
                patched++;
            }

            return patched;
        }

        private static int PatchFilterChoiceGuard(ModuleDefinition module, TypeDefinition optionsMenuScript, string methodName, int direction)
        {
            if (optionsMenuScript == null)
                return 0;

            MethodDefinition method = optionsMenuScript.Methods.FirstOrDefault(m => m.Name == methodName && m.HasBody);
            if (method == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not find OptionsMenuScript.{methodName}.");
                return 0;
            }

            MethodReference handler = ImportRuntimeMethod(module, "RuntimeHandleFilterChoice", module.TypeSystem.Boolean, module.TypeSystem.Object, module.TypeSystem.Int32);
            if (handler == null)
            {
                Console.WriteLine($"[{PatcherName}] Could not create runtime method reference for RuntimeHandleFilterChoice.");
                return 0;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction first = method.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;

            Instruction continueOriginal = Instruction.Create(OpCodes.Nop);

            // if (RuntimeHandleFilterChoice(this, direction)) return;
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, direction));
            il.InsertBefore(first, Instruction.Create(OpCodes.Call, handler));
            il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse_S, continueOriginal));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
            il.InsertBefore(first, continueOriginal);

            return 1;
        }

        private static MethodReference ImportRuntimeMethod(ModuleDefinition module, string methodName, TypeReference returnType, params TypeReference[] parameterTypes)
        {
            if (module == null)
                return null;

            AssemblyNameReference patcherAssembly = module.AssemblyReferences.FirstOrDefault(a => a.Name == "Monsterpatch_ExtraOptions_Patcher");
            if (patcherAssembly == null)
            {
                patcherAssembly = new AssemblyNameReference("Monsterpatch_ExtraOptions_Patcher", new Version(1, 0, 0, 0));
                module.AssemblyReferences.Add(patcherAssembly);
            }

            TypeReference runtimeType = new TypeReference("Goose.Monsterpatch.ShinyOddsPatcher", "ShinyOddsPatcher", module, patcherAssembly);
            MethodReference method = new MethodReference(methodName, returnType, runtimeType) { HasThis = false };

            foreach (TypeReference parameterType in parameterTypes)
                method.Parameters.Add(new ParameterDefinition(parameterType));

            return method;
        }

        // Runtime helper methods are called from the patched Assembly-CSharp.dll.
        // They intentionally use reflection so the patcher project does not need UnityEngine/UI/TMP compile references.
        public static void RuntimeEnsureShinyRow(object optionsMenuScript)
        {
            try
            {
                if (optionsMenuScript == null)
                    return;

                object filterText = GetField(optionsMenuScript, "filterText");
                if (filterText == null)
                    return;

                object filterTextTransform = GetProp(filterText, "transform");
                object filterRow = GetProp(filterTextTransform, "parent");
                if (filterRow == null)
                    return;

                object panelLeft = GetProp(filterRow, "parent");
                if (panelLeft == null)
                    return;

                object shinyRow = Call(panelLeft, "Find", "shinyOdds");
                if (IsUnityNull(shinyRow))
                {
                    object filterGameObject = GetProp(filterRow, "gameObject");
                    object clonedGameObject = InstantiateGameObject(filterGameObject, panelLeft);
                    if (IsUnityNull(clonedGameObject))
                        return;

                    SetProp(clonedGameObject, "name", "shinyOdds");
                    shinyRow = GetProp(clonedGameObject, "transform");

                    int filterIndex = ToInt(Call(filterRow, "GetSiblingIndex"), 5);
                    Call(shinyRow, "SetSiblingIndex", filterIndex + 1);
                }

                object shinyGameObject = GetProp(shinyRow, "gameObject");
                if (!IsUnityNull(shinyGameObject))
                    Call(shinyGameObject, "SetActive", true);

                object shinyTextTransform = Call(shinyRow, "Find", "txtFilter");
                object shinyText = GetComponentByTypeName(shinyTextTransform, "TMPro.TMP_Text");
                if (!IsUnityNull(shinyText))
                    SetProp(shinyText, "text", "Shiny Odds: 1/" + RuntimeGetCurrentOdds());

                WireShinyNavigation(filterRow, shinyRow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] RuntimeEnsureShinyRow failed: {ex.Message}");
            }
        }

        public static bool RuntimeHandleFilterChoice(object optionsMenuScript, int direction)
        {
            try
            {
                object selected = GetCurrentSelectedGameObject();
                if (IsUnityNull(selected))
                    return false;

                object selectedTransform = GetProp(selected, "transform");

                // v1.2.2: The selectable button may not be a direct child of the row.
                // Depending on the menu prefab, the actual selected object can be nested under
                // extra arrow/text/image objects. Walk upward until we find the cloned row.
                if (!IsTransformUnderNamedAncestor(selectedTransform, "shinyOdds"))
                    return false;

                RuntimeChangeOdds(direction);

                object refresh = optionsMenuScript?.GetType().GetMethod("RefreshUI", Type.EmptyTypes);
                if (refresh is System.Reflection.MethodInfo mi)
                    mi.Invoke(optionsMenuScript, null);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] RuntimeHandleFilterChoice failed: {ex.Message}");
                return false;
            }
        }

        private static bool IsTransformUnderNamedAncestor(object transform, string ancestorName)
        {
            try
            {
                object cur = transform;
                for (int depth = 0; depth < 16 && !IsUnityNull(cur); depth++)
                {
                    string curName = GetString(GetProp(cur, "name"));
                    if (string.Equals(curName, ancestorName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    cur = GetProp(cur, "parent");
                }
            }
            catch
            {
            }

            return false;
        }

        private static int RuntimeGetCurrentOdds()
        {
            int fallback = MenuOddsChoices[Math.Max(0, Math.Min(_oddsIndex, MenuOddsChoices.Length - 1))];
            int stored = PlayerPrefsGetInt(PlayerPrefsOddsKey, fallback);
            int idx = FindClosestChoiceIndex(stored);
            int official = MenuOddsChoices[Math.Max(0, Math.Min(idx, MenuOddsChoices.Length - 1))];
            if (stored != official)
            {
                PlayerPrefsSetInt(PlayerPrefsIndexKey, idx);
                PlayerPrefsSetInt(PlayerPrefsOddsKey, official);
                PlayerPrefsSave();
                WriteOddsToExtraOptionsConfig(official);
            }
            return official;
        }

        private static void RuntimeChangeOdds(int direction)
        {
            int currentIndex = PlayerPrefsGetInt(PlayerPrefsIndexKey, _oddsIndex);

            if (currentIndex < 0 || currentIndex >= MenuOddsChoices.Length)
                currentIndex = FindClosestChoiceIndex(RuntimeGetCurrentOdds());

            currentIndex += direction > 0 ? 1 : -1;
            if (currentIndex >= MenuOddsChoices.Length)
                currentIndex = 0;
            if (currentIndex < 0)
                currentIndex = MenuOddsChoices.Length - 1;

            int denominator = MenuOddsChoices[currentIndex];
            PlayerPrefsSetInt(PlayerPrefsIndexKey, currentIndex);
            PlayerPrefsSetInt(PlayerPrefsOddsKey, denominator);
            PlayerPrefsSave();
            WriteOddsToExtraOptionsConfig(denominator);
        }

        private static object InstantiateGameObject(object originalGameObject, object parentTransform)
        {
            if (IsUnityNull(originalGameObject) || IsUnityNull(parentTransform))
                return null;

            Type unityObjectType = FindType("UnityEngine.Object");
            if (unityObjectType == null)
                return null;

            System.Reflection.MethodInfo instantiate = unityObjectType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Instantiate" &&
                                     m.GetParameters().Length == 3 &&
                                     m.GetParameters()[1].ParameterType.Name == "Transform" &&
                                     m.GetParameters()[2].ParameterType == typeof(bool));

            if (instantiate == null)
                return null;

            return instantiate.Invoke(null, new[] { originalGameObject, parentTransform, (object)false });
        }

        private static void WireShinyNavigation(object filterRow, object shinyRow)
        {
            try
            {
                // v1.2.1: Do not depend on exact child names like bFilterDown/bFilterUp.
                // Clone layouts can vary, so grab every Selectable/Button under each row and wire them by index.
                List<object> filterSelectables = GetSelectablesInRow(filterRow);
                List<object> shinySelectables = GetSelectablesInRow(shinyRow);

                if (filterSelectables.Count == 0 || shinySelectables.Count == 0)
                {
                    Console.WriteLine($"[{PatcherName}] WireShinyNavigation: filterSelectables={filterSelectables.Count}, shinySelectables={shinySelectables.Count}. Row is visible but navigation could not be wired.");
                    return;
                }

                for (int i = 0; i < filterSelectables.Count; i++)
                {
                    object filterSelectable = filterSelectables[i];
                    object shinySelectable = shinySelectables[Math.Min(i, shinySelectables.Count - 1)];

                    // Preserve the original target below Filter, then insert Shiny Odds between Filter and that target.
                    object originalFilterDown = GetNavigationTarget(filterSelectable, "selectOnDown");

                    SetNavigationTarget(filterSelectable, "selectOnDown", shinySelectable);
                    SetNavigationTarget(shinySelectable, "selectOnUp", filterSelectable);

                    if (!IsUnityNull(originalFilterDown) && !ReferenceEquals(originalFilterDown, shinySelectable))
                    {
                        SetNavigationTarget(shinySelectable, "selectOnDown", originalFilterDown);

                        // If the row below had an explicit up link back to Filter, make it point to Shiny instead.
                        object originalUp = GetNavigationTarget(originalFilterDown, "selectOnUp");
                        if (IsUnityNull(originalUp) || ReferenceEquals(originalUp, filterSelectable))
                            SetNavigationTarget(originalFilterDown, "selectOnUp", shinySelectable);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] WireShinyNavigation failed: {ex.Message}");
            }
        }

        private static List<object> GetSelectablesInRow(object rowTransform)
        {
            var list = new List<object>();

            try
            {
                if (IsUnityNull(rowTransform))
                    return list;

                Type selectableType = FindType("UnityEngine.UI.Selectable");
                if (selectableType == null)
                    return list;

                object rowGameObject = GetProp(rowTransform, "gameObject");
                if (IsUnityNull(rowGameObject))
                    return list;

                object components = Call(rowGameObject, "GetComponentsInChildren", selectableType, true);
                if (components is Array arr)
                {
                    foreach (object component in arr)
                    {
                        if (!IsUnityNull(component))
                            list.Add(component);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] GetSelectablesInRow failed: {ex.Message}");
            }

            return list;
        }

        private static object GetNavigationTarget(object selectable, string propertyName)
        {
            if (IsUnityNull(selectable))
                return null;

            object navigation = GetProp(selectable, "navigation");
            if (navigation == null)
                return null;

            return GetProp(navigation, propertyName);
        }

        private static void SetNavigationTarget(object selectable, string propertyName, object targetSelectable)
        {
            if (IsUnityNull(selectable) || IsUnityNull(targetSelectable))
                return;

            object navigation = GetProp(selectable, "navigation");
            if (navigation == null)
                return;

            Type navType = navigation.GetType();
            System.Reflection.PropertyInfo modeProp = navType.GetProperty("mode");
            if (modeProp != null)
                SetProp(navigation, "mode", Enum.Parse(modeProp.PropertyType, "Explicit"));

            SetProp(navigation, propertyName, targetSelectable);
            SetProp(selectable, "navigation", navigation);
        }

        private static object GetCurrentSelectedGameObject()
        {
            Type eventSystemType = FindType("UnityEngine.EventSystems.EventSystem");
            if (eventSystemType == null)
                return null;

            object current = eventSystemType.GetProperty("current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null, null);
            if (IsUnityNull(current))
                return null;

            return GetProp(current, "currentSelectedGameObject");
        }

        private static int PlayerPrefsGetInt(string key, int fallback)
        {
            Type playerPrefs = FindType("UnityEngine.PlayerPrefs");
            if (playerPrefs == null)
                return fallback;

            object result = playerPrefs.GetMethod("GetInt", new[] { typeof(string), typeof(int) })?.Invoke(null, new object[] { key, fallback });
            return ToInt(result, fallback);
        }

        private static void PlayerPrefsSetInt(string key, int value)
        {
            Type playerPrefs = FindType("UnityEngine.PlayerPrefs");
            playerPrefs?.GetMethod("SetInt", new[] { typeof(string), typeof(int) })?.Invoke(null, new object[] { key, value });
        }

        private static void PlayerPrefsSave()
        {
            Type playerPrefs = FindType("UnityEngine.PlayerPrefs");
            playerPrefs?.GetMethod("Save", Type.EmptyTypes)?.Invoke(null, null);
        }

        private static object GetComponentByTypeName(object transformOrComponent, string typeFullName)
        {
            if (IsUnityNull(transformOrComponent))
                return null;

            Type componentType = FindType(typeFullName);
            if (componentType == null)
                return null;

            object gameObject = GetProp(transformOrComponent, "gameObject");
            if (IsUnityNull(gameObject))
                return null;

            return Call(gameObject, "GetComponent", componentType);
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static object GetField(object target, string name)
        {
            if (target == null)
                return null;

            return target.GetType().GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)?.GetValue(target);
        }

        private static object GetProp(object target, string name)
        {
            if (target == null)
                return null;

            return target.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)?.GetValue(target, null);
        }

        private static void SetProp(object target, string name, object value)
        {
            if (target == null)
                return;

            target.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)?.SetValue(target, value, null);
        }

        private static object Call(object target, string methodName, params object[] args)
        {
            if (target == null)
                return null;

            Type[] argTypes = args?.Select(a => a?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes;
            System.Reflection.MethodInfo method = target.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == methodName && ParametersCompatible(m.GetParameters(), args));

            return method?.Invoke(target, args);
        }

        private static bool ParametersCompatible(System.Reflection.ParameterInfo[] parameters, object[] args)
        {
            args = args ?? Array.Empty<object>();
            if (parameters.Length != args.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null)
                    continue;
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]) && !CanAssignPrimitive(parameters[i].ParameterType, args[i].GetType()))
                    return false;
            }

            return true;
        }

        private static bool CanAssignPrimitive(Type parameterType, Type argType)
        {
            if (parameterType == typeof(int) && argType == typeof(int)) return true;
            if (parameterType == typeof(bool) && argType == typeof(bool)) return true;
            if (parameterType == typeof(string) && argType == typeof(string)) return true;
            return false;
        }

        private static bool IsUnityNull(object obj)
        {
            if (obj == null)
                return true;

            try
            {
                Type unityObjectType = FindType("UnityEngine.Object");
                if (unityObjectType == null || !unityObjectType.IsInstanceOfType(obj))
                    return false;

                System.Reflection.MethodInfo opImplicit = unityObjectType.GetMethod("op_Implicit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { unityObjectType }, null);
                if (opImplicit == null)
                    return false;

                object result = opImplicit.Invoke(null, new[] { obj });
                return result is bool exists && !exists;
            }
            catch
            {
                return false;
            }
        }

        private static int ToInt(object value, int fallback)
        {
            if (value == null)
                return fallback;
            try { return Convert.ToInt32(value); }
            catch { return fallback; }
        }

        private static string GetString(object value)
        {
            return value?.ToString() ?? string.Empty;
        }

        private static void LoadConfig()
        {
            _initialized = true;

            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config");
            Directory.CreateDirectory(configDir);

            string extraOptionsConfig = Path.Combine(configDir, "goose.monsterpatch.extraoptions.cfg");
            string oldShinyConfig = Path.Combine(configDir, "goose.monsterpatch.shinyodds.patcher.cfg");
            string oldMenuConfig = Path.Combine(configDir, "goose.monsterpatch.shinyodds.menu.cfg");

            int configured = VanillaOddsDenominator;
            bool patchMenu = true;
            bool startSelectionOnShiny = false;

            if (File.Exists(extraOptionsConfig))
            {
                configured = ReadOddsFromConfig(extraOptionsConfig, configured, out bool? menuValue, out bool? startValue);
                if (menuValue.HasValue)
                    patchMenu = menuValue.Value;
                if (startValue.HasValue)
                    startSelectionOnShiny = startValue.Value;
            }
            else if (File.Exists(oldShinyConfig))
            {
                configured = ReadOddsFromConfig(oldShinyConfig, configured, out bool? menuValue, out bool? startValue);
                if (menuValue.HasValue)
                    patchMenu = menuValue.Value;
                if (startValue.HasValue)
                    startSelectionOnShiny = startValue.Value;
                WriteDefaultConfig(extraOptionsConfig, configured, patchMenu, startSelectionOnShiny);
            }
            else if (File.Exists(oldMenuConfig))
            {
                configured = ReadOddsFromConfig(oldMenuConfig, configured, out _, out _);
                WriteDefaultConfig(extraOptionsConfig, configured, patchMenu, startSelectionOnShiny);
            }
            else
            {
                WriteDefaultConfig(extraOptionsConfig, configured, patchMenu, startSelectionOnShiny);
            }

            _oddsDenominator = ClampOdds(configured);
            _oddsIndex = FindClosestChoiceIndex(_oddsDenominator);
            _oddsDenominator = MenuOddsChoices[_oddsIndex];
            // Keep the proven standalone patcher runtime behavior:
            // menu changes write PlayerPrefs, and the patched shiny formula reads PlayerPrefs.
            // Do not overwrite PlayerPrefs here; that lets the Options menu selection persist.
            _patchOptionsMenu = patchMenu;
            _startSelectionOnShinyRow = startSelectionOnShiny;
        }

        private static int ReadOddsFromConfig(string path, int fallback, out bool? patchMenu, out bool? startSelectionOnShiny)
        {
            patchMenu = null;
            startSelectionOnShiny = null;
            int value = fallback;

            try
            {
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq < 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("PatchOptionsMenu", StringComparison.OrdinalIgnoreCase) && bool.TryParse(val, out bool parsedBool))
                    {
                        patchMenu = parsedBool;
                        continue;
                    }

                    if (key.Equals("StartSelectionOnShinyRow", StringComparison.OrdinalIgnoreCase) && bool.TryParse(val, out bool parsedStartBool))
                    {
                        startSelectionOnShiny = parsedStartBool;
                        continue;
                    }

                    if (!key.Equals("OddsDenominator", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("ShinyChance", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("ShinyOddsDenominator", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (int.TryParse(val, out int parsed))
                        value = ClampOdds(parsed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] Failed reading config {path}: {ex.Message}");
            }

            return ClampOdds(value);
        }


        private static void WriteOddsToExtraOptionsConfig(int denominator)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config", "goose.monsterpatch.extraoptions.cfg");
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(path))
                {
                    WriteDefaultConfig(path, denominator, _patchOptionsMenu, _startSelectionOnShinyRow);
                    return;
                }

                List<string> lines = File.ReadAllLines(path).ToList();
                bool replaced = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("OddsDenominator", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("ShinyOddsDenominator", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "OddsDenominator = " + denominator;
                        replaced = true;
                    }
                }

                if (!replaced)
                {
                    lines.Add("");
                    lines.Add("[Shiny Odds]");
                    lines.Add("OddsDenominator = " + denominator);
                }

                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] Failed writing shiny odds to ExtraOptions config: {ex.Message}");
            }
        }

        private static void WriteDefaultConfig(string path, int odds, bool patchMenu, bool startSelectionOnShiny)
        {
            try
            {
                odds = ClampOdds(odds);
                File.WriteAllText(path,
$@"## Monsterpatch Extra Options
## This single preloader patcher replaces the separate Catch To Box, Shiny Odds, EXP Rates, and Options Layout patchers.
## Put Monsterpatch_ExtraOptions_Patcher.dll in BepInEx\patchers.
## Restart the game after replacing the patcher DLL.

[Shiny Odds]

## Denominator for shiny odds.
## Menu choices are: 1000, 500, 250, 100, 50, 25.
## 1000 = vanilla 1/1000.
## 25 = 1/25.
OddsDenominator = {odds}

[Options Menu]

## Clones the native Filter row into a separate Shiny Odds row under Filter.
## Keeps the normal Filter option intact and wires Filter -> Shiny Odds controller navigation.
## v1.2.2 uses robust selectable scanning and ancestor-aware Shiny row button detection.
PatchOptionsMenu = {patchMenu.ToString().ToLowerInvariant()}

## Legacy setting from earlier test builds. Ignored in v1.2.0 cloned-row mode.
StartSelectionOnShinyRow = false
");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] Failed writing default config {path}: {ex.Message}");
            }
        }

        private static int FindClosestChoiceIndex(int odds)
        {
            for (int i = 0; i < MenuOddsChoices.Length; i++)
            {
                if (MenuOddsChoices[i] == odds)
                    return i;
            }

            // Default to vanilla if the config value is outside the official menu choices.
            return 0;
        }

        private static int ClampOdds(int odds)
        {
            if (odds < MinOddsDenominator)
                return MinOddsDenominator;
            if (odds > MaxOddsDenominator)
                return MaxOddsDenominator;
            return odds;
        }

        private static string FormatOdds(int odds)
        {
            odds = ClampOdds(odds);
            return $"1/{odds}";
        }

        private static bool LoadsInt(Instruction ins, int value)
        {
            if (ins == null)
                return false;

            if (ins.OpCode == OpCodes.Ldc_I4 && ins.Operand is int i && i == value)
                return true;
            if (ins.OpCode == OpCodes.Ldc_I4_S && ins.Operand is sbyte sb && sb == value)
                return true;

            return value switch
            {
                -1 => ins.OpCode == OpCodes.Ldc_I4_M1,
                0 => ins.OpCode == OpCodes.Ldc_I4_0,
                1 => ins.OpCode == OpCodes.Ldc_I4_1,
                2 => ins.OpCode == OpCodes.Ldc_I4_2,
                3 => ins.OpCode == OpCodes.Ldc_I4_3,
                4 => ins.OpCode == OpCodes.Ldc_I4_4,
                5 => ins.OpCode == OpCodes.Ldc_I4_5,
                6 => ins.OpCode == OpCodes.Ldc_I4_6,
                7 => ins.OpCode == OpCodes.Ldc_I4_7,
                8 => ins.OpCode == OpCodes.Ldc_I4_8,
                _ => false
            };
        }

        private static bool HasRandomRangeAfter(IList<Instruction> instructions, int index, int maxDistance)
        {
            int end = Math.Min(instructions.Count, index + maxDistance + 1);
            for (int i = index + 1; i < end; i++)
            {
                Instruction ins = instructions[i];
                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                    continue;

                if (ins.Operand is MethodReference mr &&
                    mr.Name == "Range" &&
                    mr.DeclaringType != null &&
                    mr.DeclaringType.FullName == "UnityEngine.Random")
                {
                    return true;
                }
            }

            return false;
        }

        private static MethodReference FindMethodReference(ModuleDefinition module, string declaringTypeFullName, string methodName, int parameterCount)
        {
            if (module == null)
                return null;

            foreach (TypeDefinition type in module.Types)
            {
                MethodReference found = FindMethodReferenceInTypeRecursive(type, declaringTypeFullName, methodName, parameterCount);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static MethodReference FindMethodReferenceInTypeRecursive(TypeDefinition type, string declaringTypeFullName, string methodName, int parameterCount)
        {
            if (type == null)
                return null;

            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                MethodReference found = FindMethodReferenceInMethod(method, methodName, declaringTypeFullName, parameterCount);
                if (found != null)
                    return found;
            }

            foreach (TypeDefinition nested in type.NestedTypes)
            {
                MethodReference found = FindMethodReferenceInTypeRecursive(nested, declaringTypeFullName, methodName, parameterCount);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static MethodReference FindMethodReferenceInMethod(MethodDefinition method, string methodName, string declaringTypeFullName = null, int? parameterCount = null)
        {
            if (method == null || !method.HasBody)
                return null;

            foreach (Instruction ins in method.Body.Instructions)
            {
                if (!(ins.Operand is MethodReference mr))
                    continue;

                if (!mr.Name.Equals(methodName, StringComparison.Ordinal))
                    continue;

                if (declaringTypeFullName != null && (mr.DeclaringType == null || mr.DeclaringType.FullName != declaringTypeFullName))
                    continue;

                if (parameterCount.HasValue && mr.Parameters.Count != parameterCount.Value)
                    continue;

                return mr;
            }

            return null;
        }
    }
}
