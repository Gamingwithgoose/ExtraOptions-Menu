using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Goose.Monsterpatch.OptionsMenuLayoutPatcher
{
    public static class OptionsMenuLayoutPatcher
    {
        private const string PatcherName="Monsterpatch Extra Options - Layout";
        private const string PatcherVersion="v0.1.2";
        private const string ConfigFileName="goose.monsterpatch.extraoptions.cfg";
        private static bool _enabled=true;
        private static bool _autoCenter=true;
        private static bool _reflowVanillaRows=true;
        private static float _rowSpacing=16f;
        private static float _minRowSpacing=0f;
        private static float _maxRowSpacing=24f;
        private static float _verticalOffset=17f;
        private static bool _autoLiftExtraRows=false;
        private static float _extraRowLift=0f;
        private static bool _debugLogging=true;
        private static bool _initialized;
        private static bool _capturedBaseline;
        private static float _baselineCenterY;
        // TargetDLLs is owned by Monsterpatch_ExtraOptions_Patcher.
        public static void InitializeFeature(){LoadConfig(); Log($"{PatcherName} {PatcherVersion} loaded. Enabled={_enabled}, AutoCenter={_autoCenter}, RowSpacing={_rowSpacing}, VerticalOffset={_verticalOffset}, AutoLiftExtraRows={_autoLiftExtraRows}, ExtraRowLift={_extraRowLift}, ReflowVanillaRows={_reflowVanillaRows}.");}
        public static void PatchFeature(AssemblyDefinition assembly){ if(assembly==null){Log("Assembly was null.");return;} int result=PatchOptionsRefreshUI(assembly); Log($"OptionsMenuScript.RefreshUI layout patch result={result} insertion(s). Runtime schedules delayed reflow after custom rows are created."); }
        public static void FinishFeature(){}
        private static int PatchOptionsRefreshUI(AssemblyDefinition assembly)
        {
            TypeDefinition options=assembly.MainModule.Types.FirstOrDefault(t=>t.Name=="OptionsMenuScript"); MethodDefinition refresh=options?.Methods.FirstOrDefault(m=>m.Name=="RefreshUI" && m.HasBody); if(refresh==null){Log("OptionsMenuScript.RefreshUI not found.");return 0;}
            MethodReference runtime=ImportRuntimeMethod(assembly.MainModule,nameof(RuntimeScheduleReflowOptionsMenu)); ILProcessor il=refresh.Body.GetILProcessor(); var returns=refresh.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToList(); foreach(var ret in returns){il.InsertBefore(ret,Instruction.Create(OpCodes.Ldarg_0)); il.InsertBefore(ret,Instruction.Create(OpCodes.Call,runtime));} return returns.Count>0?1:0;
        }
        private static MethodReference ImportRuntimeMethod(ModuleDefinition module,string name)=>module.ImportReference(typeof(OptionsMenuLayoutPatcher).GetMethod(name,BindingFlags.Static|BindingFlags.Public));
        public static void RuntimeScheduleReflowOptionsMenu(object menu)
        {
            try
            {
                if(!_enabled||menu==null)return;
                MethodInfo startCoroutine=menu.GetType().GetMethod("StartCoroutine",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(IEnumerator)},null);
                if(startCoroutine!=null) startCoroutine.Invoke(menu,new object[]{DelayedReflowRoutine(menu)}); else RuntimeReflowOptionsMenu(menu);
            } catch(Exception ex){Console.WriteLine("[Monsterpatch Options Menu Layout Patcher] RuntimeScheduleReflowOptionsMenu failed: "+ex.Message);}
        }
        public static IEnumerator DelayedReflowRoutine(object menu){ yield return null; RuntimeReflowOptionsMenu(menu); yield return null; RuntimeReflowOptionsMenu(menu); yield return null; RuntimeReflowOptionsMenu(menu); }
        public static void RuntimeReflowOptionsMenu(object menu)
        {
            try
            {
                if(!_enabled||menu==null)return;
                object filterRow=GetFilterRow(menu);
                object root=filterRow!=null?GetParent(filterRow):null;
                if(root==null)return;

                List<object> vanillaRows=new List<object>();
                AddVisibleRow(vanillaRows,GetRowFromTextField(menu,"musicText"));
                AddVisibleRow(vanillaRows,GetRowFromTextField(menu,"sfxText"));
                AddVisibleRow(vanillaRows,GetRowFromTextField(menu,"fullscreenText"));
                AddVisibleRow(vanillaRows,GetRowFromTextField(menu,"resolutionText"));
                AddVisibleRow(vanillaRows,TransformFind(root,"vsync"));
                AddVisibleRow(vanillaRows,filterRow);
                AddVisibleRow(vanillaRows,GetRowFromTextField(menu,"dimText") ?? TransformFind(root,"dimFilter"));
                AddVisibleRow(vanillaRows,GetRowFromTextField(menu,"battleCamMoveText") ?? TransformFind(root,"battleCamMove"));
                vanillaRows=DistinctByReference(vanillaRows).Where(r=>r!=null && IsActiveSelf(GetGameObject(r))).ToList();

                List<object> rows=new List<object>();
                AddVisibleRow(rows,GetRowFromTextField(menu,"musicText"));
                AddVisibleRow(rows,GetRowFromTextField(menu,"sfxText"));
                AddVisibleRow(rows,GetRowFromTextField(menu,"fullscreenText"));
                AddVisibleRow(rows,GetRowFromTextField(menu,"resolutionText"));
                AddVisibleRow(rows,TransformFind(root,"vsync"));
                AddVisibleRow(rows,filterRow);
                AddVisibleRow(rows,TransformFind(root,"shinyOdds"));
                AddVisibleRow(rows,TransformFind(root,"expRate"));
                AddVisibleRow(rows,GetRowFromTextField(menu,"dimText") ?? TransformFind(root,"dimFilter"));
                AddVisibleRow(rows,GetRowFromTextField(menu,"battleCamMoveText") ?? TransformFind(root,"battleCamMove"));
                rows=DistinctByReference(rows).Where(r=>r!=null && IsActiveSelf(GetGameObject(r))).ToList();
                if(rows.Count==0)return;

                bool hasCustom=rows.Any(r=>GetUnityName(r)=="shinyOdds"||GetUnityName(r)=="expRate");
                if(!_reflowVanillaRows && !hasCustom)return;

                CaptureBaseline(vanillaRows.Count>0?vanillaRows:rows);
                float spacing=Math.Max(_minRowSpacing,Math.Min(_maxRowSpacing,_rowSpacing));
                float centerY=_autoCenter?_baselineCenterY:GetRowY(rows[0]);
                centerY+=_verticalOffset;
                float startY=centerY+spacing*(rows.Count-1)*0.5f;
                for(int i=0;i<rows.Count;i++)SetRowY(rows[i],startY-i*spacing);
                if(_debugLogging)Log($"Reflowing {rows.Count} row(s): spacing={spacing}, centerY={centerY}. Rows include latest Dim Light and Battle Cam Move menu items.");
            } catch(Exception ex){Console.WriteLine("[Monsterpatch Options Menu Layout Patcher] RuntimeReflowOptionsMenu failed: "+ex.Message);}
        }
        private static object GetRowFromTextField(object menu,string field){object text=GetField(menu,field); object t=GetTransform(text); return t!=null?GetParent(t):null;}
        private static void AddVisibleRow(List<object> rows,object row){if(row!=null && IsActiveSelf(GetGameObject(row)))rows.Add(row);}
        private static List<object> DistinctByReference(List<object> rows){var result=new List<object>(); foreach(var r in rows) if(r!=null && !result.Any(x=>ReferenceEquals(x,r))) result.Add(r); return result;}
        private static void CaptureBaseline(List<object> rows){if(_capturedBaseline)return; _capturedBaseline=true; _baselineCenterY=(rows.Max(GetRowY)+rows.Min(GetRowY))*0.5f; if(_debugLogging)Log("Captured baseline centerY="+_baselineCenterY.ToString("0.###",CultureInfo.InvariantCulture));}

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
            // The cloned row keeps the original left/right button OnClick events.
            // Full selectable navigation is intentionally left alone here to avoid UnityEngine.UI compile-time refs in a preloader patcher.
        }

        private static void LoadConfig()
        {
            if(_initialized)return; _initialized=true; string path=GetConfigPath();
            try{Directory.CreateDirectory(Path.GetDirectoryName(path)); if(!File.Exists(path))WriteDefaultConfig(path); foreach(string raw in File.ReadAllLines(path)){string line=raw.Trim(); if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||line.StartsWith("[")||!line.Contains("="))continue; string[] parts=line.Split(new[]{'='},2); string key=parts[0].Trim(); string value=parts[1].Trim(); if(key.Equals("Enabled",StringComparison.OrdinalIgnoreCase))bool.TryParse(value,out _enabled); else if(key.Equals("AutoCenter",StringComparison.OrdinalIgnoreCase))bool.TryParse(value,out _autoCenter); else if(key.Equals("ReflowVanillaRows",StringComparison.OrdinalIgnoreCase))bool.TryParse(value,out _reflowVanillaRows); else if(key.Equals("RowSpacing",StringComparison.OrdinalIgnoreCase))TryParseFloat(value,ref _rowSpacing); else if(key.Equals("MinRowSpacing",StringComparison.OrdinalIgnoreCase))TryParseFloat(value,ref _minRowSpacing); else if(key.Equals("MaxRowSpacing",StringComparison.OrdinalIgnoreCase))TryParseFloat(value,ref _maxRowSpacing); else if(key.Equals("VerticalOffset",StringComparison.OrdinalIgnoreCase))TryParseFloat(value,ref _verticalOffset); else if(key.Equals("AutoLiftExtraRows",StringComparison.OrdinalIgnoreCase))bool.TryParse(value,out _autoLiftExtraRows); else if(key.Equals("ExtraRowLift",StringComparison.OrdinalIgnoreCase))TryParseFloat(value,ref _extraRowLift); else if(key.Equals("DebugLogging",StringComparison.OrdinalIgnoreCase))bool.TryParse(value,out _debugLogging); }}catch(Exception ex){Console.WriteLine("[Monsterpatch Options Menu Layout Patcher] Failed to load config: "+ex.Message);}
        }
        private static bool TryParseFloat(string s,ref float value){if(float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out float f)){value=f;return true;}return false;}
        private static string GetConfigPath()=>Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BepInEx","config",ConfigFileName);
        private static void WriteDefaultConfig(string path)=>File.WriteAllText(path,$@"## Monsterpatch Extra Options
## New preloader-safe config. Defaults match v0.1.2 release layout for the latest Options menu.
[Layout]
Enabled = {_enabled}
AutoCenter = {_autoCenter}
ReflowVanillaRows = {_reflowVanillaRows}
RowSpacing = {_rowSpacing.ToString(CultureInfo.InvariantCulture)}
MinRowSpacing = {_minRowSpacing.ToString(CultureInfo.InvariantCulture)}
MaxRowSpacing = {_maxRowSpacing.ToString(CultureInfo.InvariantCulture)}
VerticalOffset = {_verticalOffset.ToString(CultureInfo.InvariantCulture)}
AutoLiftExtraRows = {_autoLiftExtraRows}
ExtraRowLift = {_extraRowLift.ToString(CultureInfo.InvariantCulture)}
[Debug]
DebugLogging = {_debugLogging}
");
        private static void Log(string msg)=>Console.WriteLine("[Monsterpatch Options Menu Layout Patcher] "+msg);
    }
}
