# Third-party notices

This repository is MIT-licensed (see [`LICENSE`](LICENSE)). Portions of it adapt
or derive from third-party software. This file preserves the copyright and
licence notices those works require, per ADR-0001.

## Scope and rules

- **Every third-party work whose code we read AND adapted is listed here**, with
  its full unmodified licence text. MIT's sole condition is that the copyright
  notice and permission notice travel with the work; this file is how they do.
- **Listing a work here does NOT make it a runtime dependency.** These are source
  adaptations compiled into our own assemblies. SBPR mods take no third-party mod
  loader as a runtime dependency — see ADR-0001.
- **Each adapted site must also carry an inline comment** naming the source work,
  so a reader of the code (not just this file) knows where it came from. Format:

  ```csharp
  // Adapted from Jotunn (JotunnLib Team, MIT) — <what was adapted, in one line>.
  // See THIRD-PARTY-NOTICES.md. Not a runtime dependency; source adaptation only.
  ```

- **Vanilla Valheim is not listed here.** Reading and adapting the decompiled game
  we are modding is normal engineering under ADR-0001, and no IronGate source,
  game binary, or asset is committed to this repository.
- **A work only earns an entry once we have actually adapted from it.** Reading a
  project to learn *where to look in vanilla* creates no obligation and no entry.

## Adapted works

### Jotunn (Jötunn, the Valheim Library)

- **Upstream:** https://github.com/Valheim-Modding/Jotunn
- **Licence:** MIT
- **Copyright:** Copyright (c) 2021 JotunnLib Team
- **Status:** listed pending first adaptation — see note below.
- **What we adapted:** _(none yet — update this line with each adapted area, e.g.
  "content registration lifecycle timing against ObjectDB/ZNetScene")_
- **Where:** _(none yet — list the files carrying the inline attribution comment)_

> **Note (2026-08-04):** this entry is registered ahead of use so the notice
> obligation cannot be forgotten at the moment of first adaptation. If no Jotunn
> code is ever adapted, delete this entry — an unused entry misrepresents the
> provenance of our work just as surely as a missing one does.

```text
MIT License

Copyright (c) 2021 JotunnLib Team

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
