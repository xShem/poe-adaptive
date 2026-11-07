# Attribution and source

PoeAdaptive 0.2.0 is derived from Gineticus/PoeRedux, commit
`bfaa635128f78b2c4ef3927ad5fd4795a7d546d1` (MIT).
Original copyright and license are retained in `LICENSE`.
Original project: https://github.com/Gineticus/PoeRedux

New contributions in this distribution are licensed under AGPL-3.0-or-later.
The complete application uses the AGPL library below; redistribution must
also comply with its license. See `LICENSE-AGPL-3.0.txt`.

Dependencies:

| Component | Version | License | Source |
| --- | --- | --- | --- |
| LibGGPK3.LibBundle3 / LibGGPK3 / LibBundledGGPK3, aianlinb | 2.7.5 | AGPL-3.0-or-later | https://github.com/aianlinb/LibGGPK3/tree/f2e1a39b3f99b4c1f35a6dd22754d43cc58d208c |
| aianlinb.SystemExtensions | 1.8.0 | MIT | https://github.com/aianlinb/SystemExtensions/tree/1f5e58edd5da4810d60cd7dfacbb62c8aaab0f4f |
| .NET SDK, Microsoft | 10.0.403 | MIT and third party notices | https://github.com/dotnet/sdk |
| .NET and Windows Desktop Runtime, Microsoft | 10.0.14 | MIT and third party notices | https://github.com/dotnet/runtime and https://github.com/dotnet/wpf |

SystemExtensions copyright: Copyright (c) 2024-2025 aianlinb.
MIT permission and disclaimer text in `LICENSE` also describes the terms for
SystemExtensions, with its own copyright notice above.

The downloadable distribution includes application source and the pinned
LibGGPK3 dependency source. Restore the remaining packages with NuGet as
described in README. The Oodle native DLL is not included. Use a compatible
local library from your own game/tool installation.

All NuGet direct dependencies are pinned exactly and packages.lock.json files retain resolved transitive content hashes. Corresponding AGPL library source is included under third-party/LibGGPK3; SystemExtensions source is included under third-party/SystemExtensions. Runtime license/notice files are copied when present in the installed SDK/runtime packs.
