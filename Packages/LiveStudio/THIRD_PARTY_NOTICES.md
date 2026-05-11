# Third Party Notices

`jp.lilium.livestudio` is licensed under the Apache License 2.0 (see [LICENSE](LICENSE)).

This package is a C# Unity package. It bundles no binaries (`Plugins/` / `Tools~/` are absent). Optional integrations with external packages (UniVRM, uOSC, Klak.Spout, glTFast) are gated behind `versionDefines` in the asmdef and only compile when the consumer installs those packages; they are therefore not redistributed by this package and are not listed here.

## Vendored third-party source

### VRCFaceTracking — `UnifiedExpressions` enum

- File: `Runtime/Expression/UnifiedExpressions.cs`
- Source: https://github.com/benaclejames/VRCFaceTracking
- License: Apache License 2.0
- Copyright: (c) benaclejames and VRCFaceTracking contributors

The `UnifiedExpressions` enum in `Runtime/Expression/UnifiedExpressions.cs` is a port of the same enum from the VRCFaceTracking project, including the descriptive comments that document each shape. The original VRCFaceTracking project is distributed under the Apache License 2.0, the same license used by this package; per Section 4(c) of that license, attribution is provided in this notice.

```
Copyright (c) benaclejames and VRCFaceTracking contributors.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

---

*Last updated: 2026-05-11*
