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
