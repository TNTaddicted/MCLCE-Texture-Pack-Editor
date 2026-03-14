# Legacy Console Pack Editor

A small WinForms tool for browsing and editing `.arc` (`.swf`) and `.pck` packages used in legacy Minecraft console editions.

![Example Screenshot](https://raw.githubusercontent.com/TNTaddicted/MCLCE-Texture-Pack-Editor/refs/heads/main/ExampleScreenshot.png)

## What it does
- Open `.arc` archives and browse their contents
- Open and edit embedded `.pck` assets (extract / replace / delete)
- Open `.swf` files from `.arc` and edit them via an external editor (auto-sync back into the archive)
- Preview and edit image assets

## 📦 Using the installer (Recommended)
Head to the [releases](https://github.com/TNTaddicted/MCLCE-Texture-Pack-Editor/releases) and download the prebuilt installer!

## Requirements
- **Windows 10+**
- **.NET 8 runtime** (or .NET 8 SDK to build)
- **Java 8+** (only required if you want to edit SWF in the embedded SWF editor)

The app will automatically locate it and use it when you choose **Edit SWF**.

## Building from source
From a PowerShell prompt in this folder:

```powershell
dotnet build -c Release
```

## Download the prebuilt JAR (Only when building manually)
1. Go to https://github.com/jindrapetrik/jpexs-decompiler/releases
2. Download the latest `ffdec.jar` and `ffdec.exe` (usually inside a zip)
3. Copy them to one of this location:
   - `bin\Release\net8.0-windows\`

After building and adding the needed files, run the executable under:

```
bin\\Release\\net8.0-windows\\LegacyConsolePackEditor.exe
```
