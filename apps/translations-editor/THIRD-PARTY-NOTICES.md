# Third-party notices

The Runic Translations Editor redistributes or incorporates the following
components. Versions are fixed by `Directory.Packages.props` and
`Frontend/bun.lock` in the corresponding source revision.

| Component | Copyright / project | License |
|---|---|---|
| Runic SDK: Runic Desktop host, Views, Platform providers (`Runic.Application.Views.Desktop`, `Runic.Desktop`, `Runic.Desktop.Gtk4`, `Runic.Platform.*`) | Copyright (c) Runic Artifex; https://github.com/Runic-Artifex/runic-sdk | MIT |
| Runic Desktop native server (`librax_server.so`, `rax_server.dll`, `librax_server.dylib`, from `Runic.Desktop.NativeServer`) and the Rust crates it statically links, among them axum, hyper, tokio and tower, and the Rust standard library | Runic Artifex and each crate's authors; the crates, versions and repositories are listed in the package's `THIRD-PARTY-NOTICES.md` | MIT; the crates MIT, MIT OR Apache-2.0, Apache-2.0 (sync_wrapper), MIT AND BSD-3-Clause (matchit), Unicode-3.0 (unicode-ident) |
| GirCore GObject bindings for GTK 4 and WebKitGTK 6 (`GirCore.Gtk-4.0`, `GirCore.WebKit-6.0` and their dependencies; Linux only) | Copyright (c) Marcel Tiede and contributors; https://github.com/gircore/gir.core | MIT |
| Tmds.DBus.Protocol | Copyright (c) Tom Deseyn; https://github.com/tmds/Tmds.DBus | MIT |
| WebView2 loader (`WebView2Loader.dll`, from `Microsoft.Web.WebView2`; Windows only) | Copyright (C) Microsoft Corporation; https://aka.ms/webview2 | BSD-3-Clause (below) |
| Svelte | Copyright (c) 2016-2025 Svelte contributors; https://github.com/sveltejs/svelte | MIT |
| SvelteKit and adapter-static | Copyright (c) 2020 SvelteKit contributors; https://github.com/sveltejs/kit | MIT |
| Vite browser build tooling | Copyright (c) 2019-present VoidZero Inc. and Vite contributors; https://github.com/vitejs/vite | MIT |
| .NET runtime in self-contained distributions | .NET Foundation and contributors; https://github.com/dotnet/runtime | MIT |
| Schibsted Grotesk (variable font, via `@fontsource-variable/schibsted-grotesk`) | Copyright 2023 The Schibsted-Grotesk Project Authors; https://github.com/schibsted/schibsted-grotesk | SIL Open Font License 1.1 |
| Spline Sans Mono (variable font, via `@fontsource-variable/spline-sans-mono`) | Copyright 2022 The Spline Sans Mono Project Authors; https://github.com/SorkinType/SplineSansMono | SIL Open Font License 1.1 |

Vite, TypeScript, and the remaining packages recorded in the frontend lockfile
are build and verification tools; they are not required on customer machines.
The generated browser application can contain small runtime portions of
transitive packages. Their copyright and license declarations remain available
in the locked source packages and are covered by the SPDX metadata in the
lockfile.

The two fonts are bundled unmodified in the browser application. The SIL Open
Font License 1.1 text is in each font package's `LICENSE` file in the locked
source packages (https://openfontlicense.org).

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

The canonical Runic Translations Editor logo is original Runic Artifex artwork
distributed under this project's MIT license.

On Linux the editor loads the system's GTK 4 (LGPL-2.1-or-later) and WebKitGTK 6
(LGPL-2.1-or-later and BSD-2-Clause) at run time; it does not redistribute them.
On Windows it uses the installed Microsoft Edge WebView2 Runtime, and on macOS the
system's WKWebView.

## WebView2 loader license

Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
