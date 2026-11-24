using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mono.Cecil;

namespace Goose.Monsterpatch.ExtraOptions
{
    public static class ExtraOptionsPatcher
    {
        private const string PatcherName = "Monsterpatch Extra Options";
        private const string PatcherVersion = "v0.1.3";

        public static IEnumerable<string> TargetDLLs => new[] { "Assembly-CSharp.dll" };

        public static void Initialize()
        {
            ExtraOptionsConfig.EnsureDefaultConfig();
            Goose.Monsterpatch.ExpRatesPatcher.ExpRatesPatcher.InitializeFeature();
            Goose.Monsterpatch.ShinyOddsPatcher.ShinyOddsPatcher.InitializeFeature();
            Goose.Monsterpatch.OptionsMenuLayoutPatcher.OptionsMenuLayoutPatcher.InitializeFeature();
            Log($"{PatcherName} {PatcherVersion} loaded. One patcher controls EXP Rate, Shiny Odds, and latest Options layout.");
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            if (assembly == null)
            {
                Log("Assembly was null. Nothing patched.");
                return;
            }

            Goose.Monsterpatch.ExpRatesPatcher.ExpRatesPatcher.PatchFeature(assembly);
            Goose.Monsterpatch.ShinyOddsPatcher.ShinyOddsPatcher.PatchFeature(assembly);
            Goose.Monsterpatch.OptionsMenuLayoutPatcher.OptionsMenuLayoutPatcher.PatchFeature(assembly);
            Log("Patch complete. Added Extra Options rows and runtime handlers.");
        }

        public static void Finish() { }

        private static void Log(string message)
        {
            try { Console.WriteLine("[Monsterpatch Extra Options] " + message); } catch { }
        }
    }

    internal static class ExtraOptionsConfig
    {
        public const string ConfigFileName = "goose.monsterpatch.extraoptions.cfg";

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config", ConfigFileName);

        public static void EnsureDefaultConfig()
        {
            try
            {
                string path = ConfigPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(path)) WriteDefaultConfig(path);
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("[Monsterpatch Extra Options] Failed to ensure config: " + ex.Message); } catch { }
            }
        }

        public static void WriteDefaultConfig(string path)
        {
            File.WriteAllText(path, @"## Monsterpatch Extra Options
## Plugin GUID: goose.monsterpatch.extraoptions
## This single preloader patcher replaces the separate Shiny Odds, EXP Rates, and Options Layout patchers.
## Put Monsterpatch_ExtraOptions_Patcher.dll in BepInEx\patchers.

[General]

## Enables the custom Options menu rows for Shiny Odds and EXP Rate.
# Setting type: Boolean
# Default value: true
PatchOptionsMenu = true

[Shiny Odds]

## Shiny odds denominator. Valid values: 200, 150, 100, 50, 25
# Setting type: Int32
# Default value: 200
OddsDenominator = 200

[EXP Rate]

## Global EXP multiplier. Valid values: 1, 1.5, 2, 2.5, 3
# Setting type: Single
# Default value: 1
GlobalExpMultiplier = 1

[Options Menu Layout]

## Enables the extra options menu layout reflow. Updated for the latest Dim Light and Battle Cam Move rows.
# Setting type: Boolean
# Default value: true
Enabled = true

## Centers rows around the captured vanilla menu center.
# Setting type: Boolean
# Default value: true
AutoCenter = true

## If false, layout only reflows when custom rows exist.
# Setting type: Boolean
# Default value: true
ReflowVanillaRows = true

## Vertical spacing between option rows.
# Setting type: Single
# Default value: 18
RowSpacing = 16

## Minimum row spacing clamp.
# Setting type: Single
# Default value: 12
MinRowSpacing = 0

## Maximum row spacing clamp.
# Setting type: Single
# Default value: 24
MaxRowSpacing = 24

## Extra vertical offset applied after centering.
# Setting type: Single
# Default value: 0
VerticalOffset = 17

## Automatically lifts the menu when extra rows are present.
# Setting type: Boolean
# Default value: false
AutoLiftExtraRows = false

## Lift amount per extra row.
# Setting type: Single
# Default value: 0
ExtraRowLift = 0

[Debug]

## Enables console logging for Extra Options.
# Setting type: Boolean
# Default value: true
DebugLogging = true
");
        }
    }
}
