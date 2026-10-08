# WallDivide

A small Revit 2026 add-in that divides walls at grid lines while you draw them.

Draw a wall from grid 1 to grid 6 and every grid line in between cuts it into
separate wall pieces. Grids that run parallel to the wall are ignored.

> **Status: experimental test tool.** It compiles for both Revit 2026 runtimes but has
> only been tried on a small test model. Do not use it on production models without
> checking the result.

## How it works

- An `IUpdater` listens for **added walls** (drawn or copied).
- For each straight wall it intersects the wall's location line with every straight grid
  in plan. Grids are treated as infinite lines, so a grid with short extents still cuts.
  Cuts closer than 1 mm to a wall end are ignored.
- The original wall becomes the first piece and keeps its parameters; the other pieces
  are copies of it, so type, level, height and instance parameters carry over.
- Straight walls and straight grids only. Curved walls and arc grids are ignored.

The ribbon tab **WallDivide** has one button, **Divide**, that switches the behaviour
on and off (green dot = ON, red dot = OFF). It starts as ON every time Revit opens.

Every split is logged to `%LOCALAPPDATA%\WallDivide\log.txt`.

## Build and install

Requirements: .NET SDK 10 (it also builds the .NET 8 target) and Revit 2026.

Revit 2026.5 runs on .NET 10, while 2026.4 and older run on .NET 8, and an add-in built
for one does not load in the other. The project builds both from one source tree:

```
dotnet build src/WallDivide -c Release
```

The build copies the version matching the installed Revit, plus `WallDivide.addin`,
to `%APPDATA%\Autodesk\Revit\Addins\2026\`. Pass `-p:Deploy=false` to only build.
Restart Revit afterwards.

Each target compiles against its own `RevitAPI.dll` / `RevitAPIUI.dll`. Put copies of
the two DLLs of each line in:

```
C:\RevitRefs\2026.4\   (used for the .NET 8 build)
C:\RevitRefs\2026.5\   (used for the .NET 10 build)
```

The location can be changed with `-p:RevitRefsDir=...`. These are reference assemblies
only; nothing from them is copied into the output.

To uninstall, delete `WallDivide.addin` and the `WallDivide` folder from the add-ins folder.
