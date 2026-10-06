# Third-party notices

Carina is licensed under AGPL-3.0-only (`LICENSE`). This file names what the
container image carries beside Carina. The image keeps this file, `LICENSE` and
the license texts named below under `/usr/share/doc/carina/`.

Building the image fails when the NuGet and Ubuntu tables below differ from what
the image carries.

## libaribcaption

`src/Carina.Broadcast/Text/AribSymbols.cs` carries the mapping from the ARIB
additional-symbol rows to Unicode, transcribed from `b24_gaiji_table.hpp` of
libaribcaption (<https://github.com/xqq/libaribcaption>).

```
Copyright (C) 2021 magicxqq <xqq@xqq.im>. All rights reserved.

Permission to use, copy, modify, and distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## Built in the image from source

| Component | License | Obtained from | License text in the image |
| --- | --- | --- | --- |
| FFmpeg 6.1.6, built with `--enable-gpl` and libx264 | GPL-2.0-or-later | <https://ffmpeg.org/releases/ffmpeg-6.1.6.tar.xz> | `/usr/share/doc/carina/ffmpeg/` |
| libaribcaption v1.1.2 | MIT | <https://github.com/xqq/libaribcaption> | `/usr/share/doc/carina/libaribcaption/` |

The corresponding source of the FFmpeg binaries is in the image at
`/usr/share/doc/carina/ffmpeg/source/`: the release archive as downloaded, the
patch applied to it, and the configure options.

## .NET

The .NET runtime and ASP.NET Core are MIT licensed. `app` and `migrate` run on
the shared framework of the base image `mcr.microsoft.com/dotnet/aspnet`, whose
license and notices are `/usr/share/dotnet/LICENSE.txt` and
`/usr/share/dotnet/ThirdPartyNotices.txt`. The driver is compiled by native AOT,
which links the runtime packs into its binary; their license and notices are
under `/usr/share/doc/carina/dotnet/<pack>/<version>/`.

## NuGet packages

The license and notices of each package are under
`/usr/share/doc/carina/nuget/<package>/<version>/`, and the versions each program
carries are in `/usr/share/doc/carina/nuget/<role>.tsv`.

| Package | License | Carried by |
| --- | --- | --- |
| `EFCore.NamingConventions` | Apache-2.0 | app, migrate |
| `Humanizer.Core` | MIT | migrate |
| `Konscious.Security.Cryptography.Argon2` | MIT | app, migrate |
| `Konscious.Security.Cryptography.Blake2` | MIT | app, migrate |
| `Microsoft.AspNetCore.Cryptography.Internal` | MIT | migrate |
| `Microsoft.AspNetCore.DataProtection` | MIT | migrate |
| `Microsoft.AspNetCore.DataProtection.Abstractions` | MIT | migrate |
| `Microsoft.AspNetCore.OpenApi` | MIT | app |
| `Microsoft.Build.Framework` | MIT | migrate |
| `Microsoft.CodeAnalysis.CSharp` | MIT | migrate |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | MIT | migrate |
| `Microsoft.CodeAnalysis.Common` | MIT | migrate |
| `Microsoft.CodeAnalysis.Workspaces.Common` | MIT | migrate |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | MIT | migrate |
| `Microsoft.EntityFrameworkCore` | MIT | app, migrate |
| `Microsoft.EntityFrameworkCore.Abstractions` | MIT | app, migrate |
| `Microsoft.EntityFrameworkCore.Design` | MIT | migrate |
| `Microsoft.EntityFrameworkCore.Relational` | MIT | app, migrate |
| `Microsoft.Extensions.Caching.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.Caching.Memory` | MIT | migrate |
| `Microsoft.Extensions.Configuration` | MIT | migrate |
| `Microsoft.Extensions.Configuration.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.Configuration.Binder` | MIT | migrate |
| `Microsoft.Extensions.DependencyInjection` | MIT | migrate |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.DependencyModel` | MIT | migrate |
| `Microsoft.Extensions.Diagnostics` | MIT | migrate |
| `Microsoft.Extensions.Diagnostics.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.FileProviders.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.Hosting.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.Http` | MIT | migrate |
| `Microsoft.Extensions.Logging` | MIT | migrate |
| `Microsoft.Extensions.Logging.Abstractions` | MIT | migrate |
| `Microsoft.Extensions.Options` | MIT | migrate |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | MIT | migrate |
| `Microsoft.Extensions.Options.DataAnnotations` | MIT | migrate |
| `Microsoft.Extensions.Primitives` | MIT | migrate |
| `Microsoft.OpenApi` | MIT | app |
| `Microsoft.VisualStudio.SolutionPersistence` | MIT | migrate |
| `Mono.TextTemplating` | MIT | migrate |
| `MySqlConnector` | MIT | app, migrate |
| `Newtonsoft.Json` | MIT | migrate |
| `Npgsql` | PostgreSQL | app, migrate |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL | app, migrate |
| `System.CodeDom` | MIT | migrate |
| `System.Composition` | MIT | migrate |
| `System.Composition.AttributedModel` | MIT | migrate |
| `System.Composition.Convention` | MIT | migrate |
| `System.Composition.Hosting` | MIT | migrate |
| `System.Composition.Runtime` | MIT | migrate |
| `System.Composition.TypedParts` | MIT | migrate |
| `System.Security.Cryptography.Pkcs` | MIT | migrate |
| `System.Security.Cryptography.Xml` | MIT | migrate |

## Ubuntu packages added to the base image

The license of each package is `/usr/share/doc/<package>/copyright`.

| Package | Source package | License |
| --- | --- | --- |
| `fontconfig` | `fontconfig` | HPND-sell-variant |
| `fontconfig-config` | `fontconfig` | HPND-sell-variant |
| `fonts-dejavu-core` | `fonts-dejavu` | Bitstream-Vera |
| `fonts-dejavu-mono` | `fonts-dejavu` | Bitstream-Vera |
| `fonts-noto-cjk` | `fonts-noto-cjk` | OFL-1.1 |
| `intel-media-va-driver` | `intel-media-driver` | MIT |
| `libbrotli1` | `brotli` | MIT |
| `libdrm-common` | `libdrm` | MIT |
| `libdrm2` | `libdrm` | MIT |
| `libexpat1` | `expat` | MIT |
| `libfontconfig1` | `fontconfig` | HPND-sell-variant |
| `libfreetype6` | `freetype` | FTL |
| `libigdgmm12` | `intel-gmmlib` | MIT |
| `libpcsclite1` | `pcsc-lite` | BSD-3-Clause |
| `libpng16-16t64` | `libpng1.6` | libpng-2.0 |
| `libva-drm2` | `libva` | MIT |
| `libva2` | `libva` | MIT |
| `libx264-164` | `x264` | GPL-2.0-or-later |

The corresponding source of each package under a GPL license is in the image at
`/usr/share/doc/carina/<source package>/source/`, as the Ubuntu source package of
the installed version: x264 at `/usr/share/doc/carina/x264/source/`.

FreeType is offered under the FreeType License or GPL-2.0-or-later, and the image
uses it under the FreeType License. Portions of this software are copyright ©
2023 The FreeType Project (www.freetype.org). All rights reserved.

## Packages of the base image

The base image `mcr.microsoft.com/dotnet/aspnet` is Ubuntu 24.04. The license of
each of its packages is `/usr/share/doc/<package>/copyright`, and Ubuntu publishes
the source of every version at
`https://launchpad.net/ubuntu/+source/<source package>/<version>`, under the name
and version that `dpkg-query -W -f '${source:Package} ${source:Version}\n'`
reports in the image.
