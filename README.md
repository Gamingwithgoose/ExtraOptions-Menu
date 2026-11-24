# Monsterpatch Extra Options

Merged preloader patcher for Monsterpatch that combines the working Extra Options features into one DLL.

Release: **v0.1.3 (beta)**

Catch To Box has been removed; native storage handling is untouched. This version targets the beta. Unsupported old shiny denominators fall back to 1/200; supported settings are preserved. Source-level compatibility checked against the beta dump; in-game verification pending.

## What this patcher does

Monsterpatch Extra Options replaces the older separate option patchers with one preloader patcher:

- Shiny Odds option row
- EXP Rate option row
- Options menu layout/reflow for the latest game update

The latest game update added more vanilla Options menu rows, so this build reflows the menu around the new layout and includes the newer rows:

- Music
- SFX
- Fullscreen
- Resolution
- VSync
- Pixel Filter
- Shiny Odds
- EXP Rate
- Dim Light
- Battle Cam Move

## Important install note

Use only this merged patcher:

```text
BepInEx\patchers\Monsterpatch_ExtraOptions_Patcher.dll
```

Do not install the older separate option patchers at the same time, especially:

```text
Monsterpatch_ShinyOdds_Patcher.dll
Monsterpatch_ExpRates_Patcher.dll
Monsterpatch_CatchToBox_Patcher.dll
Monsterpatch_ZZZ_OptionsMenuLayout_Patcher.dll
```

Loading the old separate patchers together with this merged patcher can cause duplicate menu rows, conflicting shiny odds behavior, or layout issues.

## Features

### Catch To Box

Captured and hatched mons are moved to the Box when box space is available.

There is no longer a **Send To** Options menu row or Box/Town toggle. The Send To option was removed from the code. The behavior is Box-first when possible.

Config setting:

```ini
[Shiny Odds]
OddsDenominator = 200
```

Valid values:

```text
200, 150, 100, 50, 25
```

### EXP Rate

Adds an EXP Rate row to the Options menu.

Available choices:

```text
1x
1.5x
2x
2.5x
3x
```

Config settings:

```ini
[EXP Rate]
GlobalExpMultiplier = 1
```

The multiplier is applied immediately before the native EXP rounding call. At 1x the beta formula is unchanged.

### Options menu layout

The Options menu layout is updated for the latest game update. It reflows the vanilla rows and the added Shiny Odds / EXP Rate rows so the menu stays aligned with the new Dim Light and Battle Cam Move options.

Default v0.1.3 layout settings:

```ini
[Options Menu Layout]
Enabled = true
AutoCenter = true
ReflowVanillaRows = true
RowSpacing = 16
MinRowSpacing = 0
MaxRowSpacing = 24
VerticalOffset = 17
AutoLiftExtraRows = false
ExtraRowLift = 0
```

## Config file

The config is created here after the game starts once with the patcher installed:

```text
BepInEx\config\goose.monsterpatch.extraoptions.cfg
```

This source package also includes a copy of the default config at:

```text
goose.monsterpatch.extraoptions.cfg
config\goose.monsterpatch.extraoptions.cfg
```

If you already have an old config from a previous Extra Options build, delete the old config once and let this build regenerate it:

```text
BepInEx\config\goose.monsterpatch.extraoptions.cfg
```

## Default config

```ini
## Monsterpatch Extra Options
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
```

## Building from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), pull the repository, and double-click `build.bat` in the repository root. The builder targets .NET Framework 4.7.2, uses a local Mono.Cecil.dll when available, and otherwise restores Mono.Cecil 0.11.5 and the framework reference assemblies from NuGet. Internet access may be required for the first restore.

The finished DLL is written to:

```text
release\Monsterpatch_ExtraOptions_Patcher.dll
```

Copy only that DLL to the game's `BepInEx\patchers` folder. Build details are saved to `build.log`. For non-interactive builds, run `build.bat --no-pause`.

## Troubleshooting

### The Options menu still looks wrong

Delete the old config once so the v0.1.3 defaults regenerate:

```text
BepInEx\config\goose.monsterpatch.extraoptions.cfg
```

Then restart the game.

### Shiny odds do not seem to change

Make sure the old standalone shiny patcher is not installed:

```text
BepInEx\patchers\Monsterpatch_ShinyOdds_Patcher.dll
```

Only this merged patcher should be installed.

Also remember that shiny odds apply to newly generated mons. Existing party, box, or town mons will not change retroactively.

### Duplicate rows appear

Remove old separate option patchers from `BepInEx\patchers` and keep only:

```text
Monsterpatch_ExtraOptions_Patcher.dll
```
