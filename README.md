[marketplace]: https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer
[vsixgallery]: http://vsixgallery.com/extension/KS.RustAnalyzer.3a91e56b-fb28-4d85-b572-ec964abf8e31/
[repo]: https://github.com/kitamstudios/rust-analyzer

# rust-analyzer - Rust language support for Visual Studio

[![CDP](https://github.com/kitamstudios/rust-analyzer/actions/workflows/cdp.yml/badge.svg)](https://github.com/kitamstudios/rust-analyzer/actions/workflows/cdp.yml) [![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg?label=license)](https://creativecommons.org/licenses/by-nc-sa/4.0/) [![Release](https://img.shields.io/github/release/kitamstudios/rust-analyzer.vs.svg?label=release)](https://github.com/kitamstudios/rust-analyzer.vs/releases) [![Visual Studio Marketplace Downloads](https://img.shields.io/visual-studio-marketplace/i/kitamstudios.RustAnalyzer)](https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer) [![Visual Studio Marketplace Rating](https://img.shields.io/visual-studio-marketplace/r/kitamstudios.RustAnalyzer)](https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer&ssr=false\#review-details) [![Discord](https://img.shields.io/discord/1060697970426773584)](https://discord.gg/JyK55EsACr)

Download this extension from the [Visual Studio Marketplace][marketplace] or get the [CI build][vsixgallery].

## Principles

- 100% UI and behavior parity with 'Open Folder' experience for a C# solution with multiple projects.
- Enhance with Rust community best practices e.g. fmt, clippy.
- Killer features e.g. ChatGPT integration.

## Features

- Workspace support.
- Build, Clean (errors in Error list with details in output window).
- Debug.
- Intellisense / Auto-complete / Goto definition / Code actions / Find references etc. all features from Rust language server.

### Upcoming

- Test integration - 1 (unit / integration, run & debug)
- Clippy / Fmt integration.
- Test integration - 2 (document / example)
- Folder enhancements (icons, context menus)
- Examples (run & debug).
- ChatGPT integration.
- Basic crates.io integration.
- Basic cargo management.

## Demo

<img src="http://i.imgur.com/qvqSHDp.gif" width="605" height="405" />

## How can I help?

If you enjoy using the extension, please give it a ★★★★★ rating on the [Visual Studio Marketplace][marketplace].

Should you encounter bugs or if you have feature requests, head on over to the [GitHub repo][repo] to open an issue if one doesn't already exist.

Pull requests are also very welcome, since I can't always get around to fixing all bugs myself.

## Common links

- [Open Folder extensibility](https://learn.microsoft.com/en-us/visualstudio/extensibility/open-folder?view=vs-2022) is pretty much the only documentation apart from the sample code folks have written (see [Acknowledgements](#Acknowledgements)).

## Acknowledgements

- [VS-RustAnalyzer](https://github.com/cchharris/VS-RustAnalyzer) for the inspiration and show how to write an 'Open Folder' extension.
- [nodejstools](https://github.com/microsoft/nodejstools/) for demonstrating good practices and utilities for writing extensions.
- [madskristensen](https://github.com/madskristensen) for being an immense store of extensions authoring tips, tricks & techniques.
- [develop-vsextension-with-github-actions](https://cezarypiatek.github.io/post/develop-vsextension-with-github-actions/) for the workflow scripts.
- [vsixcookbook](https://www.vsixcookbook.com/publish/checklist.html) for the guidance and best practices.
