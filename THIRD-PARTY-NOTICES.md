# Third-party notices

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

## Container image

The image carries the components below beside Carina itself.

| Component | License | Obtained from | License text in the image |
| --- | --- | --- | --- |
| FFmpeg 6.1.6, built with `--enable-gpl` and libx264 | GPL-2.0-or-later | <https://ffmpeg.org/releases/ffmpeg-6.1.6.tar.xz> | `/usr/share/doc/carina/ffmpeg/` |
| libaribcaption v1.1.2 | MIT | <https://github.com/xqq/libaribcaption> | `/usr/share/doc/carina/libaribcaption/` |
| x264 | GPL-2.0-or-later | Ubuntu package `libx264-164` (source package `x264`) | `/usr/share/doc/libx264-164/copyright` |
| Noto CJK fonts | OFL-1.1 | Ubuntu package `fonts-noto-cjk` | `/usr/share/doc/fonts-noto-cjk/copyright` |
| Other Ubuntu packages | as each states | Ubuntu archive | `/usr/share/doc/<package>/copyright` |

The corresponding source of the FFmpeg binaries is in the image at
`/usr/share/doc/carina/ffmpeg/source/`: the release archive as downloaded, the
patch applied to it, and the configure options.
