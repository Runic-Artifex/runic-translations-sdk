# Third-party notices

The Runic Translations Editor redistributes or incorporates the following
components. Versions are fixed by `Directory.Packages.props` and
`Frontend/package-lock.json` in the corresponding source revision.

| Component | Copyright / project | License |
|---|---|---|
| CsWebUi and CsWebUi.Native | Copyright (c) 2026 Viktor Jannicke; https://github.com/Runic-Artifex/cs-webui | MIT |
| WebUI native library | Copyright (c) 2020-2026 Hassan Draga and contributors; https://github.com/webui-dev/webui | MIT |
| Svelte | Copyright (c) 2016-2025 Svelte contributors; https://github.com/sveltejs/svelte | MIT |
| SvelteKit and adapter-static | Copyright (c) 2020 SvelteKit contributors; https://github.com/sveltejs/kit | MIT |
| Vite browser build tooling | Copyright (c) 2019-present VoidZero Inc. and Vite contributors; https://github.com/vitejs/vite | MIT |
| .NET runtime in self-contained distributions | .NET Foundation and contributors; https://github.com/dotnet/runtime | MIT |

Vite, TypeScript, and the remaining packages recorded in the frontend lockfile
are build and verification tools; they are not required on customer machines.
The generated browser application can contain small runtime portions of
transitive packages. Their copyright and license declarations remain available
in the locked source packages and are covered by the SPDX metadata in the
lockfile.

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

No WebUI or Runic logo asset is currently distributed by the editor. If one is
added, its separate logo license must be included before release. The optional
Windows NativeAOT/WebView2 static-link path is not used by these preview
artifacts; enabling it requires adding Microsoft's WebView2 license and notices.
