# Third Party Notices

`jp.lilium.livestudio.virgo` is licensed under the Apache License 2.0 (see [LICENSE.md](LICENSE.md)).
This document lists third-party software bundled by this package and their respective license terms.

The C# sources under `Runtime/` and `Editor/` are original work of this package. The components listed
below are bundled inside the pre-built **VirgoMotion Fusion** helper application under
`Tools~/VirgoMotionFusion/`, which Studio launches as a child process. They are redistributed only as
integrated parts of that compiled application.

---

## MediaPipe

- Source: https://github.com/google-ai-edge/mediapipe
- License: Apache License 2.0
- Copyright: Copyright 2019 The MediaPipe Authors
- Bundled as:
  - `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Plugins/x86_64/mediapipe_c.dll`
  - The MediaPipe Solutions model bundles in `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/StreamingAssets/`:
    - `face_landmarker_v2_with_blendshapes.bytes`
    - `hand_landmarker.bytes`
    - `holistic_landmarker.bytes`
    - `pose_landmarker_lite.bytes`
    - `pose_landmarker_full.bytes`
    - `pose_landmarker_heavy.bytes`

```
Copyright 2019 The MediaPipe Authors.

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

## MediaPipe Unity Plugin

- Source: https://github.com/homuler/MediaPipeUnityPlugin
- Version: 0.16.1 (`com.github.homuler.mediapipe`)
- License: MIT
- Bundled as: `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Managed/Mediapipe.Runtime.dll`

This plugin bundles further third-party components of the MediaPipe build (RE2, Abseil, and others).
Their full notices are distributed with the plugin as `Third Party Notices.md` in the
`com.github.homuler.mediapipe` package.

```
MIT License

Copyright (c) 2021 homuler

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

## Protocol Buffers (Google.Protobuf)

- Source: https://github.com/protocolbuffers/protobuf
- License: 3-Clause BSD
- Bundled as: `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Managed/Google.Protobuf.dll`
  (redistributed as part of the MediaPipe Unity Plugin)

```
Copyright 2008 Google Inc.  All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

    * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
    * Neither the name of Google Inc. nor the names of its
contributors may be used to endorse or promote products derived from
this software without specific prior written permission.

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

Code generated by the Protocol Buffer compiler is owned by the owner
of the input file used when generating it.  This code is not
standalone and requires a support library to be linked with it.  This
support library is itself covered by the above license.
```

---

## UniVRM (UniGLTF / VRM / UniHumanoid / MToon / SpringBoneJobs)

- Source: https://github.com/vrm-c/UniVRM
- License: MIT
- Bundled as (all under `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Managed/`):
  `VRM.dll`, `UniGLTF.dll`, `UniGLTF.Utils.dll`, `UniGLTF.UniUnlit.dll`, `UniHumanoid.dll`,
  `MToon.dll`, `SpringBoneJobs.dll`

```
MIT License

Copyright (c) 2020 VRM Consortium
Copyright (c) 2018 Masataka SUMI for MToon

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

## Newtonsoft.Json

- Source: https://github.com/JamesNK/Newtonsoft.Json
- License: MIT
- Bundled as: `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Managed/Newtonsoft.Json.dll`

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

## Bouncy Castle

- Source: https://www.bouncycastle.org/csharp/
- License: MIT (the Bouncy Castle license is an adaptation of the MIT X11 license)
- Copyright: Copyright (c) 2000-2025 The Legion of the Bouncy Castle Inc.
- Bundled as: `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Managed/BouncyCastle.Crypto.dll`
  (used for license signature verification)

---

## Final IK (RootMotion)

- Source: Unity Asset Store — https://assetstore.unity.com/packages/tools/animation/final-ik-14290
- Copyright: Copyright (c) RootMotion (Pärtel Lang)
- License: **Proprietary.** Governed by the Unity Asset Store End User License Agreement
  (https://unity.com/legal/as-terms), not by an open-source license.
- Bundled as: `Tools~/VirgoMotionFusion/VirgoMotionFusion_Data/Managed/RootMotion.dll`

This component is redistributed **only in compiled form, as an integrated part of the VirgoMotion
Fusion application**, which the Unity Asset Store EULA permits. It is not provided in source form,
and it may not be extracted from this package or reused as a standalone asset. Anyone wishing to use
Final IK in their own project must obtain their own license from the Unity Asset Store.

---

## Unity Technologies packages

- License: Unity Companion License (https://unity.com/legal/licenses/unity-companion-license)
- Bundled as:
  - `Plugins/x86_64/H264Encoder.dll`, `Plugins/x86_64/NvEncPlugin.dll` — from `com.unity.live-capture`
  - `Plugins/x86_64/XRSimulationSubsystem.dll`, `Plugins/ARM64/XRSimulationSubsystem.dll` — from `com.unity.xr.arfoundation`

Unity Engine runtime components (`UnityPlayer.dll`, the `UnityEngine.*` assemblies, the Mono runtime
under `MonoBleedingEdge/`, and Burst-compiled output) are redistributed under the terms of the Unity
software license that applies to applications built with the Unity Editor.
