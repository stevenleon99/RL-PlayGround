# Unity Setup Guide

## 1. Install Unity

1. Download **Unity Hub**: https://unity.com/download
2. Hub → **Installs** → **Install Editor** → pick **2022.3 LTS** or newer.
3. Modules: select **Windows Build Support (IL2CPP)**.

## 2. Create / Open Project

- New: Hub → **Projects** → **New** → **3D (URP)**.
- Existing: Hub → **Add** → select folder (e.g. `drone-simulator`).

## 3. Troubleshooting

- **Compile errors after opening** → check the Console (`Ctrl+Shift+C`), ensure all script errors are resolved before entering Play mode.
- **Missing assets/scripts** → make sure the `Assets/Scripts` and `Assets/Editor` folders are committed and pulled.

## 4. Optional Git-friendly settings

`Edit` → `Project Settings` → `Editor`:
- **Asset Serialization**: *Force Text*
- **Version Control Mode**: *Visible Meta Files*

Then:
```bash
git init
```
