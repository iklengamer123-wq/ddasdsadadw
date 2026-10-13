# SoundSpace

Aim-based rhythm game, built in Godot 4.7 (.NET / C#), modelled after
[github.com/rhythia/client](https://github.com/rhythia/client).

You move your mouse over a 3x3 grid and hit hollow square notes as they fly in.

---

## How to run

1. Open **Godot 4.7.x (.NET / mono)**.
2. **Import** -> select `project.godot` -> Open.
3. Press **F5**.

Requirements (same as the original Rhythia client):

| Tool | Version |
| --- | --- |
| Godot Engine (.NET build) | **4.7** |
| .NET SDK | **10.0** |

---

## Controls

| Input | Action |
| --- | --- |
| Mouse movement | Aim (relative input, cursor is captured) |
| `Esc` | Pause / resume (also unlocks the mouse) |
| `Esc` (on results) | Back to menu |

---

## Menu

* **Maps** – list of imported maps with cover thumbnails.
* **Right-click a map** – opens a small menu with **Favorite** (moves the map to the
  top of the list and marks it with a ★ in gold) and **Delete Map** (removes the map).
* **Import Map** – pick a `.sspm` (or `.phxm`) file.
* **Settings** – the gameplay and appearance settings (Music Volume applies to the menu preview and to gameplay).
* **Start From** – start the map from any moment (the menu preview seeks there so you can hear it).
* **Speed** – 0.75x, 0.8x, 0.87x, 1.0x, 1.15x, 1.25x, 1.35x.
* Selecting a map plays its song.
* **PLAY** – start the selected map.

## Gameplay

* A square frame is centered on the screen; hollow square notes fly inside it.
* Notes must be hit by moving the mouse cursor onto them at the right time.
* Right of the grid (vertically centred): the **accuracy** counter on top and the
  **misses** counter below it — they move with the frame, because they live in the
  playfield and follow the camera parallax.
* Top of the screen: **song name** and a **running timer** `current / total` (no parallax).

## Settings (taken from the Rhythia client)

| Setting | Default | Range | Step |
| --- | --- | --- | --- |
| Approach Rate (AR) | 30 | 0.5 .. 100 | 0.5 |
| Spawn Distance (AD) | 15 | 0.5 .. 100 | 0.5 |
| Parallax | 0.25 | 0 .. 1 | 0.05 |
| Mouse Sensitivity | 0.5 | 0.01 .. 2.5 | 0.01 |
| Music Volume | 50 | 0 .. 100 | 1 |
| Note Style | Square | Square / Thin | – |
| Note Size | 0.8 | 0.3 .. 1.5 | 0.05 |
| Cursor Size | 1.0 | 0.4 .. 3.0 | 0.05 |
| Colorset | Default | files in `%APPDATA%\SoundSpace\colorsets` | – |

`Approach Time = Spawn Distance / Approach Rate` (same as Rhythia).

---

## Where user data is stored

Everything is written to `%APPDATA%\SoundSpace` (`project.godot` uses a custom user dir):

```
%APPDATA%\SoundSpace\
├── settings.json              # gameplay + appearance settings
├── colorsets\                 # note coloursets, one .txt per colorset
│   ├── Default.txt
│   ├── Killing Freeze.txt
│   └── Rainbow Freeze.txt
└── maps\
    └── <Artist> - <Title> [Diff]\
        ├── metadata.json      # title / artist / mappers / difficulty / length / favorite ...
        ├── notes.bin          # hit objects (int ms, float x, float y)
        ├── cover.png          # cover art extracted from the map
        └── audio.mp3 | .ogg   # song extracted from the map
```

A colorset file contains one RGB colour per line, e.g. `255,128,0` (values 0..255);
`#RRGGBB` is accepted too, and empty lines / lines starting with `//` are ignored.
Drop your own `.txt` files into `colorsets\` and they appear in the Colorset setting.

## Supported map formats

* `.sspm` – Sound Space Plus (version 1 and version 2). **Required format.**
* `.phxm` – Phira map archive (zip). Bonus, the native Rhythia format.

Both are parsed with the exact same rules as the Rhythia client
(`scripts/map/MapParser.cs`): note coordinates are converted to the playfield
coordinate system `X ∈ [-1, 1]`, `Y ∈ [-1, 1]` (Y up), sorted by timestamp.
