<p align="center">
  <img src="docs/logo.png" alt="HydrogenXML logo" width="128" />
</p>

<h1 align="center">HydrogenXML</h1>

<p align="center">
  A free, lightweight XSLT workbench for Workday integration developers.<br />
  Load your XSL and XML once, keep editing, and see the result instantly.
</p>

---

## Download and install

Grab the latest build from the **[Releases page](https://github.com/dawood-myob/HydrogenXML/releases/latest)**:

| File | Use it when |
|---|---|
| `HydrogenXML-Setup-<version>.exe` | You want a normal installer (Start menu shortcut, optional desktop icon, uninstaller). Installs per-user, no admin rights needed. |
| `HydrogenXML-<version>-win-x64-portable.zip` | You want to unzip and run, e.g. from a USB drive or a locked-down machine. |

Both are self-contained: **no .NET, Saxon or Java install required.** Windows 10/11 (64-bit).

> The installer is not code-signed, so Windows SmartScreen may show *"Windows protected your PC"*. Click **More info → Run anyway**.

## Why HydrogenXML?

If you build Workday integrations (Studio, EIB, Core Connectors, RaaS output, Document Transformation), you spend your day in a loop: **edit XSL → load the XML → run the transformation → read the output → fix → repeat.** Doing that with general-purpose tools means reloading files, re-running validation and copying results around every single time.

**HydrogenXML is essential tooling for the Workday developer.** It lets you prepare your XSL and test it quickly: open the stylesheet and a sample XML once, press **F5**, and the transformed output appears next to your code. Errors come back with the file, line and column, and one click takes you to the problem.

It is also a **free alternative to [Oxygen XML Editor](https://www.oxygenxml.com/) for the bare-basic jobs**: editing, validating and transforming XML with XSLT 1.0, 2.0 and 3.0. It is not a full Oxygen replacement (there is no XSD/Schematron authoring, no step-through XSLT debugger and no PDF/FO tooling), but for day-to-day XSLT development and quick testing it covers what most Workday developers need, at no cost and with no license to manage.

## Features

**Transformations**
- Saxon-HE (via SaxonCS-HE) bundled with the app. Supports XSLT 1.0, 2.0 and 3.0, with nothing else to install.
- **Live preview:** transform on demand (**F5** / **Ctrl+Enter**) or enable **Auto** to re-run after a configurable delay whenever the XSL or XML changes.
- Transformation parameters, initial template / mode, and pretty-printed output.
- Handles large Workday exports: files above 20 MB open as a read-only preview while Saxon streams the full document from disk. A 90 MB+ input transforms in a few seconds and the UI stays responsive.

**Editing**
- Side-by-side layout: **Input XML | XSLT | Output**, with resizable and collapsible panes.
- Syntax highlighting, line numbers, code folding, auto-indent, auto-close tags and XML formatting.
- Find / Replace (match case, whole word, regex), undo / redo, bookmarks, zoom and split editing.
- Multiple XSL and XML tabs, drag-and-drop of files, and recent file lists.

**Validation and diagnostics**
- Live well-formedness checks for XSL and XML, plus XSL compile errors.
- One diagnostics panel for XSL compile errors, XML validation errors, Saxon runtime errors, XPath errors and `xsl:message` output. Each entry shows file, line, column and message, and **double-clicking jumps to the exact location**.

**Workday productivity**
- Sample data manager: keep named datasets of test XML and switch inputs in one click.
- Template library: starter stylesheets, Workday-flavoured templates and reusable snippets.
- XPath console: run an expression against the current XML and see the matching nodes.

**Workspace**
- Your open tabs, current XSL and XML, layout, theme, recent files and transformation settings are restored when you reopen the app. Unsaved edits are restored too.
- Dark and light themes.

## Getting started

1. Download or build the app (see below) and run `HydrogenXML.exe`.
2. Open your stylesheet (**Open** in the XSLT pane) and a source XML (**Open** in the Input pane, drag-and-drop, or pick one from the **Samples** tab).
3. Edit the XSL and press **F5**. Tick **Auto** to transform automatically as you type.
4. Fix errors from the **Diagnostics** panel, then **Save** the XSL back to its original file.

| Shortcut | Action |
|---|---|
| `F5` / `Ctrl+Enter` | Transform |
| `Ctrl+S` | Save |
| `Ctrl+O` / `Ctrl+Shift+O` | Open XSL / Open XML |
| `Ctrl+H` | Find / Replace |

The full list is under **Help → Keyboard shortcuts**.

## Building from source

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download) (SaxonCS-HE 13 targets .NET 10).

```powershell
dotnet build HydrogenXML.sln
dotnet run --project src\HydrogenXML
dotnet test tests\HydrogenXML.Tests
```

Create a portable, self-contained build that runs without installing .NET:

```powershell
dotnet publish src\HydrogenXML\HydrogenXML.csproj -c Release -r win-x64 --self-contained -o publish
```

Build the installer and portable zip (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php), `winget install JRSoftware.InnoSetup`):

```powershell
.\build-installer.ps1            # outputs to artifacts\
```

### Releasing

Push a version tag and GitHub Actions builds, tests and publishes the installer and zip to a new release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## Project layout

| Path | Purpose |
|---|---|
| `src/HydrogenXML` | WPF application (MVVM, AvalonEdit editors, themes) |
| `src/HydrogenXML.Core` | Transformation engine, XML tools, XPath, workspace, samples and templates |
| `tests/HydrogenXML.Tests` | xUnit tests for the core library |

User data (workspace, samples, backups) is stored in `%APPDATA%\HydrogenXML`.

## Known limitations

- Windows only (WPF).
- Panes are resizable and collapsible, but there is no free-floating docking.
- `xsl:strip-space` is not applied when an initial template is used (a Saxon API limitation).
- Very long single lines (over about 2 MB) can be slow in the editor.

## Built with

[SaxonCS-HE](https://www.saxonica.com/) (Mozilla Public License 2.0), [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (MIT) and .NET / WPF.

HydrogenXML is an independent project. It is not affiliated with or endorsed by Syncro Soft (Oxygen XML Editor), Saxonica or Workday.
