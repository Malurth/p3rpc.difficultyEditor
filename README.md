# P3R Custom Difficulty Editor

A [Reloaded II](https://github.com/Reloaded-Project/Reloaded-II) mod for **Persona 3 Reload** that lets you edit the game's per-difficulty combat multipliers right from the mod's config UI — no file editing, no external tools.

Every difficulty (Peaceful, Easy, Normal, Hard, Merciless) exposes the full set of multipliers the game actually uses, so you can fine-tune the balance or build your own custom difficulty.

## Features

- **Per-difficulty control** of all 10 multipliers:
  - Damage you **deal** / **take** — each with separate **Weakness** and **Critical** variants
  - **EXP** gained and item **sell value**
  - **Ailment** rates (landing on you / inflicted by you)
- **Presets** that fill in the values for you:
  - `Vanilla` — the game's stock values
  - `HarderEasy` — a less-cushy Easy (you deal 1.15×, take 0.85×, resist ailments at 0.85×)
  - `Halfsies` — every difficulty pulled halfway toward Normal
  - `Bursty` — gives every difficulty Merciless's weakness (1.36×) and critical (1.34×) damage
- **Live preset detection** — the dropdown shows whichever preset your current values match, or `Custom`.
- **Hide weakness/crit settings** toggle to keep the list tidy (on by default).
- Everything defaults to vanilla, so the mod changes nothing until you edit it.

## Requirements

- [Reloaded II](https://github.com/Reloaded-Project/Reloaded-II)
- [Unreal Essentials](https://github.com/AnimatedSwine37/UnrealEssentials) — fetched automatically as a dependency

## Installation

1. Set up Reloaded II for Persona 3 Reload (see the [Beginner's Guide](https://gamebanana.com/tuts/17156)).
2. Download this mod (from Releases or GameBanana) and enable it in Reloaded II. Dependencies are pulled in automatically.
3. Launch the game.

## Usage

1. In Reloaded II, select the mod and open **Configure**.
2. Choose a **Preset**, or edit individual difficulty values.
3. **Relaunch the game** — changes are applied at startup.

> Set the matching difficulty in-game for its values to take effect (e.g. Easy edits only affect Easy).

## How it works

The mod reads your config and patches a copy of the game's `DT_BtlDIfficultyParam` data table — the table holding every difficulty's multipliers — then serves it through Unreal Essentials. No game files on disk are modified.

## Building

Requires the **.NET 9 SDK** and a `RELOADEDIIMODS` environment variable pointing at your Reloaded II mods folder.

- `p3rpc.difficultyEditor/BuildLinked.ps1` — builds straight into your mods folder for testing.
- `Publish.ps1` — produces the release packages (GitHub / GameBanana / NuGet).

## Credits

Built on the Reloaded II mod template by Sewer56. Uses [Unreal Essentials](https://github.com/AnimatedSwine37/UnrealEssentials) by AnimatedSwine37 & Rirurin.
