# Unity Setup Guide

## 1. Install Unity

1. Download **Unity Hub**: https://unity.com/download
2. Hub → **Installs** → **Install Editor** → pick **2022.3 LTS** or newer.
3. Modules: select **Windows Build Support (IL2CPP)**.

## 2. Create / Open Project

- New: Hub → **Projects** → **New** → **3D (URP)**.
- Existing: Hub → **Add** → select folder (e.g. `drone-simulator`).

## 3. Install MCP for Unity (talks to Claude Code)

This extension lets Claude Code control Unity (assets, scenes, scripts, menu items) over Model Context Protocol.

### Prerequisites
- **Python 3.10+**: https://www.python.org/downloads/
- **uv** (Python toolchain manager) — in PowerShell:
  ```powershell
  winget install --id=astral-sh.uv -e
  ```
- **Claude Code** CLI installed and on PATH.

### Step A — Install the Unity package
1. Open your Unity project.
2. `Window` → `Package Manager` → `+` → **Add package from git URL...**
3. Paste:
   ```
   https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity
   ```
4. Click **Add**.

### Step B — Start the local HTTP server
1. `Window` → **MCP for Unity**.
2. Transport: **HTTP** (default), URL: `http://localhost:8080`.
3. Click **Start Local HTTP Server**. Keep the spawned terminal open.

### Step C — Register with Claude Code
In a terminal:
```bash
claude mcp add --scope user UnityMCP --transport http http://localhost:8080/mcp
```
Verify it's registered:
```bash
claude mcp list
```

### Step D — Use it
Restart Claude Code in your project folder. Ask things like:
- *"Create a 3D player controller"*
- *"Add a red cube to the scene and save"*
- *"Run all EditMode tests"*

Unity tools (`manage_scene`, `manage_gameobject`, `manage_asset`, `run_tests`, etc.) are now callable from Claude Code.

## 4. Troubleshooting

- **"uv Not Found"** in the Unity window → click the **[HELP]** link or set the `uv.exe` path manually.
- **Claude Code not connecting** → ensure the HTTP server shows "Session Active" and the URL ends with `/mcp`.
- **Multiple Unity instances** → ask Claude to list `unity_instances` and call `set_active_instance` with `Name@hash`.

## 5. Optional Git-friendly settings

`Edit` → `Project Settings` → `Editor`:
- **Asset Serialization**: *Force Text*
- **Version Control Mode**: *Visible Meta Files*

Then:
```bash
git init
```

---
Sources:
- [CoplayDev/unity-mcp (GitHub)](https://github.com/CoplayDev/unity-mcp)
- [Original: justinpbarnett/unity-mcp](https://github.com/justinpbarnett/unity-mcp)
- [uv install docs](https://docs.astral.sh/uv/getting-started/installation/)
