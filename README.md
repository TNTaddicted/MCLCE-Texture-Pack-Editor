# Legacy Console Pack Editor

A small WinForms tool for browsing and editing `.arc` (`.swf`) and `.pck` packages used in legacy Minecraft console editions.

## ✅ What it does
- Open `.arc` archives and browse their contents
- Open and edit embedded `.pck` assets (extract / replace / delete)
- Open `.swf` files from `.arc` and edit them via an external editor (auto-sync back into the archive)
- Preview image assets

## 🧰 Requirements
- **Windows 10+**
- **.NET 8 runtime** (or .NET 8 SDK to build)
- **Java 8+** (only required if you want to edit SWF in the embedded SWF editor)

### Download a prebuilt JAR (Only when building yourselves)
1. Go to https://github.com/jindrapetrik/jpexs-decompiler/releases
2. Download the latest `ffdec.jar` and `ffdec.exe` (usually inside a zip)
3. Copy them to one of this location:
   - `bin\Release\net8.0-windows\`

The app will automatically locate it and use it when you choose **Edit SWF**.

### Build `ffdec.jar` yourself (advanced)
If you want to build from source, you need Apache Ant + JDK 8+.

```powershell
cd jpexs-decompiler
ant dist
```

After building, the jar will appear under `jpexs-decompiler\\dist\\ffdec.jar`.

## 📦 Using the installer (Recommended)
Head to the releases and download the prebuilt installer!

## 🏗️ Building from source
From a PowerShell prompt in this folder:

```powershell
dotnet build -c Release
```

After building, run the executable under:

```
bin\\Release\\net8.0-windows\\LegacyConsolePackEditor.exe
```
