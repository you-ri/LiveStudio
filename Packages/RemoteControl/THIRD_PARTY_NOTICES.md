# Third Party Notices

`jp.lilium.remotecontrol` is licensed under the Apache License 2.0 (see [LICENSE.md](LICENSE.md)).
This document lists third-party software referenced or bundled by this package and their respective license terms.

---

## Runtime dependencies (resolved via Unity Package Manager)

### Newtonsoft.Json

- Source: https://github.com/JamesNK/Newtonsoft.Json
- License: MIT
- Usage: Declared in `package.json` as `com.unity.nuget.newtonsoft-json`. Resolved by consumers through Unity Package Manager; **not bundled** in this package.

```
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

## Build-time references (not redistributed)

### Roslyn (Microsoft.CodeAnalysis.CSharp / Microsoft.CodeAnalysis.Analyzers)

- Source: https://github.com/dotnet/roslyn
- License: MIT
- Usage: Build-time only reference for the Source Generator project at `SourceGenerator~/Lilium.RemoteControl.SourceGenerator/`. The csproj uses `PrivateAssets="all"` and `SuppressDependenciesWhenPacking`, so no Roslyn binaries are redistributed in the shipped `Plugins/Lilium.RemoteControl.SourceGenerator.dll`.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Bundled tools

### VirgoMotionRemote (companion application)

- Location: `Tools~/VirgoMotionRemote/VirgoMotionRemote.exe`
- Origin: Built from the [VirgoMotionRemote](https://github.com/you-ri/VirgoMotionRemote) project (Tauri 2 / Rust + React).
- License of the application itself: Apache License 2.0, Copyright (c) You-Ri, 2026.
- Usage: A self-contained desktop application shipped inside this Unity package as a launchable companion tool. Because it is distributed as a pre-built binary, the runtime artifacts of all of its transitive third-party dependencies are redistributed together with this package.

The canonical, versioned list of third-party libraries linked into the bundled `VirgoMotionRemote.exe` — including their copyright notices and full license texts — is shipped alongside the binary at [`Tools~/VirgoMotionRemote/THIRD_PARTY_LICENSES.md`](Tools~/VirgoMotionRemote/THIRD_PARTY_LICENSES.md). Consumers redistributing this package must keep that file together with the executable.

Summary of license families used by the bundled binary:

| License | Representative libraries |
|---|---|
| MIT | React, React-DOM, Zustand, Immer, i18next, react-i18next, Base UI, QRCode, Vite, Tailwind CSS, ESLint, Prettier, Jest, PostCSS, Autoprefixer, Tauri, Serde, if-addrs, base64, dirs, open, built |
| Apache-2.0 | TypeScript |
| Font Awesome Free (Icons: CC BY 4.0 / Fonts: SIL OFL 1.1 / Code: MIT) | Font Awesome |

For dependencies that are dual-licensed under `MIT OR Apache-2.0`, this distribution uses them under the terms compatible with both licenses; downstream consumers may rely on either.

---

*Last updated: 2026-05-11*
