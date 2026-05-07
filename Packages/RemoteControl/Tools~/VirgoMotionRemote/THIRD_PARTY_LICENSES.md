# Third-Party Licenses

This document lists third-party libraries used in VirgoMotionRemote.

---

## Frontend (JavaScript/TypeScript)

### React

- **Package:** react, react-dom
- **Version:** ^18.2.0
- **License:** MIT License
- **Copyright:** (c) Meta Platforms, Inc. and affiliates
- **Website:** https://react.dev/

### Zustand

- **Package:** zustand
- **Version:** ^5.0.8
- **License:** MIT License
- **Copyright:** (c) 2019 Paul Henschel
- **Website:** https://github.com/pmndrs/zustand

### Immer

- **Package:** immer
- **Version:** ^10.2.0
- **License:** MIT License
- **Copyright:** (c) 2017 Michel Weststrate
- **Website:** https://immerjs.github.io/immer/

### i18next

- **Packages:** i18next, react-i18next
- **Version:** ^23.7.6, ^13.5.0
- **License:** MIT License
- **Copyright:** (c) 2022 i18next
- **Website:** https://www.i18next.com/

### Font Awesome

- **Packages:** @fortawesome/fontawesome-svg-core, @fortawesome/free-solid-svg-icons, @fortawesome/react-fontawesome
- **Version:** ^7.0.1, ^7.0.1, ^3.0.2
- **License:** Font Awesome Free License (Icons: CC BY 4.0, Fonts: SIL OFL 1.1, Code: MIT License)
- **Website:** https://fontawesome.com/

```
Font Awesome Free License

Font Awesome Free is free, open source, and GPL friendly. You can use it for
commercial projects, open source projects, or really almost whatever you want.
Full Font Awesome Free license: https://fontawesome.com/license/free

Icons: CC BY 4.0 License (https://creativecommons.org/licenses/by/4.0/)
Fonts: SIL OFL 1.1 License (https://scripts.sil.org/OFL)
Code: MIT License (https://opensource.org/licenses/MIT)
```

### Base UI

- **Package:** @base-ui-components/react
- **Version:** ^1.0.0-beta.3
- **License:** MIT License
- **Copyright:** (c) MUI
- **Website:** https://base-ui.com/

### QRCode

- **Package:** qrcode
- **Version:** ^1.5.3
- **License:** MIT License
- **Website:** https://github.com/soldair/node-qrcode

### Tauri API & Plugins

- **Packages:** @tauri-apps/api, @tauri-apps/plugin-http
- **Version:** ^2.9.0, ^2.5.4
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://tauri.app/

---

## Frontend Development Dependencies

### Vite

- **Package:** vite
- **Version:** ^6.0.11
- **License:** MIT License
- **Website:** https://vite.dev/

### TypeScript

- **Package:** typescript
- **Version:** ^5.9.2
- **License:** Apache License 2.0
- **Copyright:** (c) Microsoft Corporation
- **Website:** https://www.typescriptlang.org/

### Tailwind CSS

- **Package:** tailwindcss
- **Version:** ^3.4.0
- **License:** MIT License
- **Website:** https://tailwindcss.com/

### ESLint

- **Package:** eslint
- **Version:** ^9.38.0
- **License:** MIT License
- **Website:** https://eslint.org/

### Prettier

- **Package:** prettier
- **Version:** ^3.6.2
- **License:** MIT License
- **Website:** https://prettier.io/

### Jest

- **Package:** jest
- **Version:** ^29.7.0
- **License:** MIT License
- **Copyright:** (c) Meta Platforms, Inc. and affiliates
- **Website:** https://jestjs.io/

### PostCSS & Autoprefixer

- **Packages:** postcss, autoprefixer
- **Version:** ^8.5.6, ^10.4.21
- **License:** MIT License

---

## Backend (Rust)

### Tauri

- **Crate:** tauri, tauri-build, tauri-plugin-dialog, tauri-plugin-opener, tauri-plugin-http
- **Version:** 2.x
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://tauri.app/

```
MIT License

Copyright (c) 2017 - Present Tauri Programme within The Commons Conservancy

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

### Serde

- **Crate:** serde, serde_json
- **Version:** 1.x
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://serde.rs/

### if-addrs

- **Crate:** if-addrs
- **Version:** 0.13
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://github.com/messense/if-addrs

### base64

- **Crate:** base64
- **Version:** 0.22
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://github.com/marshallpierce/rust-base64

### dirs

- **Crate:** dirs
- **Version:** 5.x
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://github.com/dirs-dev/dirs-rs

### open

- **Crate:** open
- **Version:** 5.x
- **License:** MIT License
- **Website:** https://github.com/Byron/open-rs

### built

- **Crate:** built
- **Version:** 0.7
- **License:** MIT License (dual-licensed: MIT OR Apache-2.0, this project chooses MIT)
- **Website:** https://github.com/lukasluber/built

---

## License Summary

This project chooses MIT License for all dual-licensed (MIT OR Apache-2.0) dependencies.

| License | Packages |
|---------|----------|
| MIT | React, Zustand, Immer, i18next, Base UI, QRCode, Vite, Tailwind CSS, ESLint, Prettier, Jest, PostCSS, Tauri, Serde, if-addrs, base64, dirs, open, built |
| Apache-2.0 | TypeScript |
| Font Awesome Free | Font Awesome (Icons: CC BY 4.0, Fonts: SIL OFL 1.1, Code: MIT) |

---

*Last updated: 2026-01-26*
