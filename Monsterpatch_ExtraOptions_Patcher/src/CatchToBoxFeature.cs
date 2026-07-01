using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Goose.Monsterpatch.CatchToBoxPatcher
{
    public static class CatchToBoxPatcher
    {
        private const string PatcherName = "Monsterpatch Extra Options - Catch To Box";
        private const string PatcherVersion = "v0.1.2";
        private const string ConfigFileName = "goose.monsterpatch.extraoptions.cfg";
        private static bool _onlyMoveWhenBoxHasSpace = true;
        private static bool _debugLogging = true;
        private static bool _initialized;

        // TargetDLLs is owned by Monsterpatch_ExtraOptions_Patcher.

        public static void InitializeFeature()
        {
            LoadConfig();
            Log($"{PatcherName} {PatcherVersion} loaded. Always sends caught/hatched mons to Box. OnlyMoveWhenBoxHasSpace={_onlyMoveWhenBoxHasSpace}");
        }

        public static void PatchFeature(AssemblyDefinition assembly)
        {
            if (assembly == null) { Log("Assembly was null. Nothing patched."); return; }
            int catchPatches = PatchCatchDestination(assembly);
            Log($"Patched catch-to-box logic={catchPatches}. No Options menu row is created.");
        }

        public static void FinishFeature() { }

        private static int PatchCatchDestination(AssemblyDefinition assembly)
        {
            TypeDefinition gameScript = assembly.MainModule.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScript == null) { Log("GameScript not found."); return 0; }
            MethodDefinition method = gameScript.Methods.FirstOrDefault(m => m.Name == "AddMonToTeamFromCatchOrHatch" && m.Parameters.Count == 1 && m.HasBody);
            if (method == null) { Log("GameScript.AddMonToTeamFromCatchOrHatch(Mon) not found."); return 0; }
            MethodReference runtime = ImportRuntimeMethod(assembly.MainModule, nameof(RuntimeAfterAddMonToTeamFromCatchOrHatch));
            ILProcessor il = method.Body.GetILProcessor();
            var returns = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            foreach (Instruction ret in returns)
            {
                il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_1));
                il.InsertBefore(ret, Instruction.Create(OpCodes.Call, runtime));
            }
            return returns.Count > 0 ? 1 : 0;
        }

        private static MethodReference ImportRuntimeMethod(ModuleDefinition module, string methodName)
        {
            MethodInfo mi = typeof(CatchToBoxPatcher).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            return module.ImportReference(mi);
        }

        public static void RuntimeAfterAddMonToTeamFromCatchOrHatch(object gameScript, object mon)
        {
            try
            {
                if (gameScript == null || mon == null) return;

                object boxManager = GetField(gameScript, "boxManager");
                if (boxManager == null) return;

                object townRoot = GetTownRoot(gameScript);
                if (townRoot == null)
                {
                    if (_debugLogging) Console.WriteLine("[Monsterpatch Catch To Box Patcher] Could not locate _PlayerTownMons root.");
                    return;
                }

                object townMonTransform = FindTownMonObject(townRoot, mon);
                if (townMonTransform == null) return;

                if (_onlyMoveWhenBoxHasSpace)
                {
                    object hasSpace = InvokeNoArg(boxManager, "HasSpaceInBox");
                    if (hasSpace is bool b && !b)
                    {
                        if (_debugLogging) Console.WriteLine("[Monsterpatch Catch To Box Patcher] Mon stayed in town because boxes are full.");
                        return;
                    }
                }

                InvokeMethod(boxManager, "AddMonToBox", mon);
                object go = GetGameObject(townMonTransform);
                if (go != null) DestroyUnityObject(go);
                InvokeNoArg(gameScript, "RefreshTownMonsArray");
                SetField(gameScript, "addedMonTo", "BOX");

                if (_debugLogging) Console.WriteLine("[Monsterpatch Catch To Box Patcher] Moved caught/hatched mon from town to box.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Monsterpatch Catch To Box Patcher] Runtime catch move failed: " + ex.Message);
            }
        }

        private static object GetTownRoot(object gameScript)
        {
            object value = GetField(gameScript, "_PlayerTownMons");
            return GetTransform(value);
        }

        private static object FindTownMonObject(object townRoot, object mon)
        {
            if (townRoot == null || mon == null) return null;
            object monUnique = GetField(mon, "uniqueID");
            int count = GetChildCount(townRoot);
            for (int i = 0; i < count; i++)
            {
                object child = GetChild(townRoot, i);
                object monObject = GetComponentByTypeName(child, "MonObject");
                object childMon = GetField(monObject, "mon");
                if (childMon == null) continue;
                if (ReferenceEquals(childMon, mon)) return child;
                object childUnique = GetField(childMon, "uniqueID");
                if (monUnique != null && childUnique != null && monUnique.Equals(childUnique)) return child;
            }
            return null;
        }

        // IMPORTANT: preloader-safe reflection helpers.
        private static object GetField(object obj, string name)
        {
            if (obj == null) return null;
            Type type = obj as Type ?? obj.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(obj is Type ? null : obj);
        }

        private static bool SetField(object obj, string name, object value)
        {
            if (obj == null) return false;
            Type type = obj as Type ?? obj.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return false;
            field.SetValue(obj is Type ? null : obj, value);
            return true;
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            Type type = obj as Type ?? obj.GetType();
            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return prop == null ? null : prop.GetValue(obj is Type ? null : obj, null);
        }

        private static object InvokeNoArg(object target, string methodName)
        {
            if (target == null) return null;
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return method == null ? null : method.Invoke(target, null);
        }

        private static object InvokeMethod(object target, string methodName, params object[] args)
        {
            if (target == null) return null;
            Type type = target as Type ?? target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.Name != methodName) continue;
                ParameterInfo[] p = method.GetParameters();
                if (p.Length != (args == null ? 0 : args.Length)) continue;
                try { return method.Invoke(target is Type ? null : target, args); }
                catch { }
            }
            return null;
        }

        private static object GetTransform(object value)
        {
            if (value == null) return null;
            if (value.GetType().Name == "Transform" || value.GetType().Name == "RectTransform") return value;
            return GetProp(value, "transform");
        }

        private static object GetGameObject(object value)
        {
            if (value == null) return null;
            if (value.GetType().Name == "GameObject") return value;
            return GetProp(value, "gameObject");
        }

        private static int GetChildCount(object transform)
        {
            object count = GetProp(transform, "childCount");
            return count is int i ? i : 0;
        }

        private static object GetChild(object transform, int index)
        {
            return InvokeMethod(transform, "GetChild", index);
        }

        private static Type FindType(string fullOrShortName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullOrShortName, false);
                if (t != null) return t;
                try
                {
                    foreach (Type candidate in asm.GetTypes())
                    {
                        if (candidate.FullName == fullOrShortName || candidate.Name == fullOrShortName)
                            return candidate;
                    }
                }
                catch { }
            }
            return null;
        }

        private static object GetComponentByTypeName(object obj, string typeName)
        {
            if (obj == null) return null;
            Type type = FindType(typeName);
            if (type == null) return null;
            object go = GetGameObject(obj);
            if (go != null)
            {
                object comp = InvokeMethod(go, "GetComponent", type);
                if (comp != null) return comp;
            }
            return InvokeMethod(obj, "GetComponent", type);
        }

        private static void DestroyUnityObject(object obj)
        {
            if (obj == null) return;
            Type unityObject = FindType("UnityEngine.Object");
            if (unityObject == null) return;
            foreach (MethodInfo method in unityObject.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (method.Name != "Destroy") continue;
                ParameterInfo[] p = method.GetParameters();
                if (p.Length != 1) continue;
                try { method.Invoke(null, new object[] { obj }); return; } catch { }
            }
        }

        private static void LoadConfig()
        {
            if (_initialized) return;
            _initialized = true;
            string path = GetConfigPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) WriteDefaultConfig(path);
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[") || !line.Contains("=")) continue;
                    string[] parts = line.Split(new[] { '=' }, 2);
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    if (key.Equals("OnlyMoveWhenBoxHasSpace", StringComparison.OrdinalIgnoreCase)) bool.TryParse(value, out _onlyMoveWhenBoxHasSpace);
                    else if (key.Equals("DebugLogging", StringComparison.OrdinalIgnoreCase)) bool.TryParse(value, out _debugLogging);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Monsterpatch Catch To Box Patcher] Failed to load config: " + ex.Message); }
        }

        private static string GetConfigPath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config", ConfigFileName);

        private static void WriteDefaultConfig(string path) => File.WriteAllText(path, $@"## Monsterpatch Extra Options
## Preloader-safe. Install in BepInEx\patchers, not plugins.
[Catch To Box]
OnlyMoveWhenBoxHasSpace = {_onlyMoveWhenBoxHasSpace}
[Debug]
DebugLogging = {_debugLogging}
");

        private static void Log(string msg) => Console.WriteLine("[Monsterpatch Catch To Box Patcher] " + msg);
    }
}
