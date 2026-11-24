using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Goose.Monsterpatch.ExpRatesPatcher
{
    public static class ExpRatesPatcher
    {
        private const string PatcherName = "Monsterpatch Extra Options - EXP Rate";
        private const string PatcherVersion = "v0.1.3";
        private const string ConfigFileName = "goose.monsterpatch.extraoptions.cfg";
        private const string RowName = "expRate";
        private static readonly float[] MenuMultiplierChoices = { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f };
        private static float _globalExpMultiplier = 1.0f;
        private static bool _patchOptionsMenu = true;
        private static bool _debugLogging = true;
        private static bool _initialized;
        // TargetDLLs is owned by Monsterpatch_ExtraOptions_Patcher.
        public static void InitializeFeature() { LoadConfig(); Log($"{PatcherName} {PatcherVersion} loaded. GlobalExpMultiplier={_globalExpMultiplier} (default/new config 1x), PatchOptionsMenu={_patchOptionsMenu}"); }
        public static void PatchFeature(AssemblyDefinition assembly)
        {
            if (assembly == null) { Log("Assembly was null."); return; }
            int formula = PatchExpFormula(assembly); int ui=0;
            if (_patchOptionsMenu) { ui += PatchOptionsRefreshUI(assembly); ui += PatchFilterChoiceGuard(assembly,"FilterLeft",-1); ui += PatchFilterChoiceGuard(assembly,"FilterRight",1); }
            Log($"Patched GameScript.GetExpGain replacements={formula}. EXP menu default/current={RuntimeGetCurrentGlobalExpMultiplier():0.###}x. Options menu insertion(s)={ui}.");
        }
        public static void FinishFeature() { }
        private static int PatchExpFormula(AssemblyDefinition assembly)
        {
            var gameScript = assembly.MainModule.Types.FirstOrDefault(t => t.Name == "GameScript");
            var method = gameScript?.Methods.FirstOrDefault(m => m.Name == "GetExpGain" && m.IsStatic && m.HasBody && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "Mon[]" && m.Parameters[1].ParameterType.FullName == "System.Boolean" && m.ReturnType.FullName == "System.Int32");
            if (method == null) { Log("WARNING: beta GetExpGain signature not found."); return 0; }
            var targets = method.Body.Instructions.Where(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is MethodReference mr && mr.DeclaringType.FullName == "UnityEngine.Mathf" &&
                mr.Name == "RoundToInt" && mr.Parameters.Count == 1 &&
                mr.Parameters[0].ParameterType.FullName == "System.Single").ToList();
            if (targets.Count != 1) { Log("WARNING: expected exactly one EXP rounding call; found " + targets.Count + ". EXP unchanged."); return 0; }
            var target = targets[0];
            var round = (MethodReference)target.Operand;
            var runtime = ImportRuntimeMethod(assembly.MainModule, nameof(RuntimeApplyExpMultiplier));
            // Preserve the original instruction as a branch target.
            var il = method.Body.GetILProcessor();
            target.OpCode = OpCodes.Call;
            target.Operand = runtime;
            il.InsertAfter(target, Instruction.Create(OpCodes.Call, round));
            return 1;
        }
        public static float RuntimeApplyExpMultiplier(float exp) => exp * RuntimeGetCurrentGlobalExpMultiplier();
        private static int PatchOptionsRefreshUI(AssemblyDefinition assembly)
        {
            TypeDefinition options = assembly.MainModule.Types.FirstOrDefault(t=>t.Name=="OptionsMenuScript");
            MethodDefinition refresh = options?.Methods.FirstOrDefault(m=>m.Name=="RefreshUI" && m.HasBody);
            if (refresh==null) { Log("OptionsMenuScript.RefreshUI not found."); return 0; }
            MethodReference runtime = ImportRuntimeMethod(assembly.MainModule, nameof(RuntimeEnsureExpRateRow));
            ILProcessor il = refresh.Body.GetILProcessor(); var returns=refresh.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToList();
            foreach(var ret in returns) { il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0)); il.InsertBefore(ret, Instruction.Create(OpCodes.Call, runtime)); }
            return returns.Count>0?1:0;
        }
        private static int PatchFilterChoiceGuard(AssemblyDefinition assembly,string methodName,int delta)
        {
            TypeDefinition options=assembly.MainModule.Types.FirstOrDefault(t=>t.Name=="OptionsMenuScript");
            MethodDefinition method=options?.Methods.FirstOrDefault(m=>m.Name==methodName && m.HasBody);
            if(method==null) { Log($"OptionsMenuScript.{methodName} not found."); return 0; }
            MethodReference runtime=ImportRuntimeMethod(assembly.MainModule,nameof(RuntimeHandleFilterChoice));
            ILProcessor il=method.Body.GetILProcessor(); Instruction first=method.Body.Instructions.First();
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0)); il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, delta)); il.InsertBefore(first, Instruction.Create(OpCodes.Call, runtime)); il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first)); il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
            return 1;
        }
        private static MethodReference ImportRuntimeMethod(ModuleDefinition module,string name) => module.ImportReference(typeof(ExpRatesPatcher).GetMethod(name,BindingFlags.Static|BindingFlags.Public));
        public static float RuntimeGetCurrentGlobalExpMultiplier() => ClampMultiplierToChoice(_globalExpMultiplier);
        public static void RuntimeEnsureExpRateRow(object menu)
        {
            try
            {
                object filterRow=GetFilterRow(menu); object root=filterRow!=null?GetParent(filterRow):null; if(filterRow==null||root==null)return;
                object row=TransformFind(root,RowName); if(row==null) { object clone=InstantiateUnder(GetGameObject(filterRow),root); if(clone==null) { if(_debugLogging) Console.WriteLine("[Monsterpatch EXP Rates Patcher] Failed to clone Filter row."); return; } SetUnityName(clone,RowName); row=GetTransform(clone); }
                object shiny=TransformFind(root,"shinyOdds"); object anchor=shiny??filterRow; SetSiblingIndex(row,GetSiblingIndex(anchor)+1); SetActive(GetGameObject(row),true); SetRowText(row,"EXP Rate: "+FormatMultiplier(RuntimeGetCurrentGlobalExpMultiplier())); BasicWireNavigation(anchor,row);
            } catch(Exception ex) { Console.WriteLine("[Monsterpatch EXP Rates Patcher] RuntimeEnsureExpRateRow failed: "+ex.Message); }
        }
        public static bool RuntimeHandleFilterChoice(object menu,int delta)
        {
            try
            {
                object selected=GetCurrentSelectedGameObject(); if(!IsTransformUnderNamedAncestor(GetTransform(selected),RowName)) return false;
                int idx=FindClosestMultiplierIndex(RuntimeGetCurrentGlobalExpMultiplier())+delta; if(idx<0)idx=MenuMultiplierChoices.Length-1; if(idx>=MenuMultiplierChoices.Length)idx=0; _globalExpMultiplier=MenuMultiplierChoices[idx]; WriteMultiplierToConfig(_globalExpMultiplier); RuntimeEnsureExpRateRow(menu); return true;
            } catch(Exception ex) { Console.WriteLine("[Monsterpatch EXP Rates Patcher] RuntimeHandleFilterChoice failed: "+ex.Message); return false; }
        }
        private static bool Approximately(float a,float b)=>Math.Abs(a-b)<0.0001f;
        private static int FindClosestMultiplierIndex(float value) { int best=0; float diff=float.MaxValue; for(int i=0;i<MenuMultiplierChoices.Length;i++) { float d=Math.Abs(MenuMultiplierChoices[i]-value); if(d<diff) {diff=d; best=i;} } return best; }
        private static float ClampMultiplierToChoice(float value)=>MenuMultiplierChoices[FindClosestMultiplierIndex(value)];
        private static string FormatMultiplier(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture)+"x";

        // IMPORTANT: preloader-safe reflection helpers.
        // This patcher intentionally does not reference UnityEngine or Assembly-CSharp at compile time.
        // Referencing those assemblies directly from a preloader patcher can load them too early and prevent patching.
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

        private static bool SetProp(object obj, string name, object value)
        {
            if (obj == null) return false;
            Type type = obj as Type ?? obj.GetType();
            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null) return false;
            prop.SetValue(obj is Type ? null : obj, value, null);
            return true;
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
            object transform = GetProp(value, "transform");
            return transform;
        }

        private static object GetGameObject(object value)
        {
            if (value == null) return null;
            if (value.GetType().Name == "GameObject") return value;
            object go = GetProp(value, "gameObject");
            return go;
        }

        private static string GetUnityName(object value)
        {
            object name = GetProp(value, "name");
            return name as string ?? string.Empty;
        }

        private static void SetUnityName(object value, string name)
        {
            SetProp(value, "name", name);
        }

        private static object TransformFind(object transform, string childName)
        {
            return InvokeMethod(transform, "Find", childName);
        }

        private static object GetParent(object transform)
        {
            return GetProp(transform, "parent");
        }

        private static int GetSiblingIndex(object transform)
        {
            object result = InvokeNoArg(transform, "GetSiblingIndex");
            return result is int i ? i : 0;
        }

        private static void SetSiblingIndex(object transform, int index)
        {
            InvokeMethod(transform, "SetSiblingIndex", index);
        }

        private static void SetActive(object gameObject, bool active)
        {
            InvokeMethod(gameObject, "SetActive", active);
        }

        private static bool IsActiveSelf(object gameObject)
        {
            object active = GetProp(gameObject, "activeSelf");
            return !(active is bool b) || b;
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

        private static object[] GetComponentsInChildrenByTypeName(object obj, string typeName, bool includeInactive)
        {
            if (obj == null) return new object[0];
            Type type = FindType(typeName);
            if (type == null) return new object[0];
            object result = InvokeMethod(obj, "GetComponentsInChildren", type, includeInactive);
            if (result is Array arr)
            {
                object[] output = new object[arr.Length];
                for (int i = 0; i < arr.Length; i++) output[i] = arr.GetValue(i);
                return output;
            }
            object go = GetGameObject(obj);
            if (go != null && !ReferenceEquals(go, obj))
                return GetComponentsInChildrenByTypeName(go, typeName, includeInactive);
            return new object[0];
        }

        private static object InstantiateUnder(object originalGameObject, object parentTransform)
        {
            if (originalGameObject == null || parentTransform == null) return null;
            Type unityObject = FindType("UnityEngine.Object");
            if (unityObject == null) return null;
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            foreach (MethodInfo method in unityObject.GetMethods(flags))
            {
                if (method.Name != "Instantiate") continue;
                if (method.IsGenericMethod) continue;
                ParameterInfo[] p = method.GetParameters();
                if (p.Length != 2) continue;
                try
                {
                    return method.Invoke(null, new object[] { originalGameObject, parentTransform });
                }
                catch { }
            }
            return null;
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

        private static object GetCurrentSelectedGameObject()
        {
            Type eventSystemType = FindType("UnityEngine.EventSystems.EventSystem");
            if (eventSystemType == null) return null;
            object current = GetProp(eventSystemType, "current");
            return GetProp(current, "currentSelectedGameObject");
        }

        private static bool IsTransformUnderNamedAncestor(object transform, string name)
        {
            object t = transform;
            while (t != null)
            {
                if (string.Equals(GetUnityName(t), name, StringComparison.OrdinalIgnoreCase)) return true;
                t = GetParent(t);
            }
            return false;
        }

        private static object GetFilterRow(object menu)
        {
            object filterText = GetField(menu, "filterText");
            object filterTextTransform = GetTransform(filterText);
            return filterTextTransform != null ? GetParent(filterTextTransform) : null;
        }

        private static void SetRowText(object row, string text)
        {
            if (row == null) return;
            object textTransform = TransformFind(row, "txtFilter");
            object textComp = GetComponentByTypeName(textTransform, "TMPro.TMP_Text") ?? GetComponentByTypeName(textTransform, "UnityEngine.UI.Text");
            if (textComp == null)
            {
                foreach (object c in GetComponentsInChildrenByTypeName(row, "TMPro.TMP_Text", true)) { textComp = c; break; }
            }
            if (textComp == null)
            {
                foreach (object c in GetComponentsInChildrenByTypeName(row, "UnityEngine.UI.Text", true)) { textComp = c; break; }
            }
            SetProp(textComp, "text", text);
        }

        private static float GetVectorComponent(object vector, string name)
        {
            if (vector == null) return 0f;
            object value = GetProp(vector, name);
            if (value is float f) return f;
            FieldInfo field = vector.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.GetValue(vector) is float ff) return ff;
            return 0f;
        }

        private static object MakeVector(Type type, float x, float y, float z)
        {
            if (type == null) return null;
            ConstructorInfo c2 = type.GetConstructor(new[] { typeof(float), typeof(float) });
            if (c2 != null) return c2.Invoke(new object[] { x, y });
            ConstructorInfo c3 = type.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            if (c3 != null) return c3.Invoke(new object[] { x, y, z });
            return null;
        }

        private static float GetRowY(object row)
        {
            object anchored = GetProp(row, "anchoredPosition");
            if (anchored != null) return GetVectorComponent(anchored, "y");
            object local = GetProp(row, "localPosition");
            return GetVectorComponent(local, "y");
        }

        private static void SetRowY(object row, float y)
        {
            object anchored = GetProp(row, "anchoredPosition");
            if (anchored != null)
            {
                float x = GetVectorComponent(anchored, "x");
                object nv = MakeVector(anchored.GetType(), x, y, 0f);
                if (nv != null && SetProp(row, "anchoredPosition", nv)) return;
            }
            object local = GetProp(row, "localPosition");
            if (local != null)
            {
                float x = GetVectorComponent(local, "x");
                float z = GetVectorComponent(local, "z");
                object nv = MakeVector(local.GetType(), x, y, z);
                if (nv != null) SetProp(row, "localPosition", nv);
            }
        }


        private static void BasicWireNavigation(object previousRow, object row)
        {
            try
            {
                List<object> previousSelectables = GetSelectablesInRow(previousRow);
                List<object> rowSelectables = GetSelectablesInRow(row);

                if (previousSelectables.Count == 0 || rowSelectables.Count == 0)
                {
                    if (_debugLogging)
                    {
                        Console.WriteLine($"[{PatcherName}] BasicWireNavigation: previousSelectables={previousSelectables.Count}, rowSelectables={rowSelectables.Count}. Row is visible but controller/keyboard navigation could not be wired.");
                    }
                    return;
                }

                for (int i = 0; i < previousSelectables.Count; i++)
                {
                    object previousSelectable = previousSelectables[i];
                    object newSelectable = rowSelectables[Math.Min(i, rowSelectables.Count - 1)];

                    object oldDown = GetNavigationTarget(previousSelectable, "selectOnDown");

                    SetNavigationTarget(previousSelectable, "selectOnDown", newSelectable);
                    SetNavigationTarget(newSelectable, "selectOnUp", previousSelectable);

                    // Preserve the old downward chain. This inserts the new row between the previous row
                    // and whatever row/button vanilla navigation used to go to.
                    if (oldDown != null && !ReferenceEquals(oldDown, newSelectable))
                    {
                        SetNavigationTarget(newSelectable, "selectOnDown", oldDown);

                        object oldDownUp = GetNavigationTarget(oldDown, "selectOnUp");
                        if (oldDownUp != null && ReferenceEquals(oldDownUp, previousSelectable))
                        {
                            SetNavigationTarget(oldDown, "selectOnUp", newSelectable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] BasicWireNavigation failed: " + ex.Message);
            }
        }

        private static List<object> GetSelectablesInRow(object row)
        {
            List<object> results = new List<object>();
            try
            {
                if (row == null) return results;

                foreach (object selectable in GetComponentsInChildrenByTypeName(row, "UnityEngine.UI.Selectable", true))
                {
                    if (selectable == null) continue;
                    object go = GetGameObject(selectable);
                    if (go != null && !IsActiveSelf(go)) continue;
                    results.Add(selectable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] GetSelectablesInRow failed: " + ex.Message);
            }
            return results;
        }

        private static object GetNavigationTarget(object selectable, string propertyName)
        {
            try
            {
                if (selectable == null) return null;
                object nav = GetProp(selectable, "navigation");
                if (nav == null) return null;
                return GetProp(nav, propertyName);
            }
            catch { return null; }
        }

        private static void SetNavigationTarget(object selectable, string propertyName, object target)
        {
            try
            {
                if (selectable == null || target == null) return;

                object nav = GetProp(selectable, "navigation");
                if (nav == null) return;

                PropertyInfo modeProp = nav.GetType().GetProperty("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (modeProp != null)
                {
                    object explicitMode = Enum.Parse(modeProp.PropertyType, "Explicit");
                    SetProp(nav, "mode", explicitMode);
                }

                SetProp(nav, propertyName, target);
                SetProp(selectable, "navigation", nav);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PatcherName}] SetNavigationTarget failed: " + ex.Message);
            }
        }

        private static void LoadConfig()
        {
            if(_initialized)return; _initialized=true; string path=GetConfigPath();
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); if(!File.Exists(path)) WriteDefaultConfig(path); foreach(string raw in File.ReadAllLines(path)) { string line=raw.Trim(); if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||line.StartsWith("[")||!line.Contains("="))continue; string[] parts=line.Split(new[]{'='},2); string key=parts[0].Trim(); string value=parts[1].Trim(); if(key.Equals("GlobalExpMultiplier",StringComparison.OrdinalIgnoreCase)||key.Equals("DefaultGlobalExpMultiplier",StringComparison.OrdinalIgnoreCase)) TryParseFloat(value,ref _globalExpMultiplier); else if(key.Equals("PatchOptionsMenu",StringComparison.OrdinalIgnoreCase)) bool.TryParse(value,out _patchOptionsMenu); else if(key.Equals("DebugLogging",StringComparison.OrdinalIgnoreCase)) bool.TryParse(value,out _debugLogging); } } catch(Exception ex) { Console.WriteLine("[Monsterpatch EXP Rates Patcher] Failed to load config: "+ex.Message); } _globalExpMultiplier=ClampMultiplierToChoice(_globalExpMultiplier);
        }
        private static bool TryParseFloat(string s, ref float value) { if(float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out float f)) { value=f; return true; } return false; }
        private static string GetConfigPath()=>Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BepInEx","config",ConfigFileName);
        private static void WriteDefaultConfig(string path)=>File.WriteAllText(path,$@"## Monsterpatch Extra Options
## New preloader-safe config. Default EXP rate is 1x.
[EXP]
GlobalExpMultiplier = {_globalExpMultiplier.ToString(CultureInfo.InvariantCulture)}
[Options Menu]
PatchOptionsMenu = {_patchOptionsMenu}
[Debug]
DebugLogging = {_debugLogging}
");
        private static void WriteMultiplierToConfig(float value) { try { string path=GetConfigPath(); if(!File.Exists(path)) WriteDefaultConfig(path); var lines=File.ReadAllLines(path).ToList(); bool rep=false; for(int i=0;i<lines.Count;i++) if(lines[i].TrimStart().StartsWith("GlobalExpMultiplier",StringComparison.OrdinalIgnoreCase)) { lines[i]="GlobalExpMultiplier = "+value.ToString(CultureInfo.InvariantCulture); rep=true; } if(!rep) lines.Add("GlobalExpMultiplier = "+value.ToString(CultureInfo.InvariantCulture)); File.WriteAllLines(path,lines.ToArray()); } catch { } }
        private static void Log(string msg)=>Console.WriteLine("[Monsterpatch EXP Rates Patcher] "+msg);
    }
}
