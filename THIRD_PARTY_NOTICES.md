# Third-Party Notices / 第三者ライセンス表示

アタッシェケース5（AttacheCase 5 for Windows / macOS、クロスプラットフォーム統合版）

本ソフトウェアは第三者のソフトウェアを同梱しています。第三者が著作権を有する部分には、
それぞれのライセンスが適用されます。AttacheCase Source Available License および
ライセンサーが定める Commercial / Integration / OEM License は、これらの第三者ライセンスを
変更または上書きするものではありません。過去に GNU General Public License version 3 その他の
ライセンスで公開されたバージョンについても同様です。

この一覧は、**実際に配布物（macOS: AttacheCase.app、Windows: インストーラ / ZIP）へ
入っているもの**を書いています。確認方法は末尾の「一覧の作り方」を参照してください。
OS を限定した記述が無い行は両 OS 共通です。

---

## 1. 配布物に含まれるもの

### マネージドアセンブリ

単一ファイルの実行形式（`Contents/MacOS/AttacheCase`）に埋め込まれています。

| コンポーネント | バージョン | ライセンス | 著作権表示 |
|---|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia)<br>（Desktop / Native / Skia / X11 / Win32 / FreeDesktop / Themes.Simple / Remote.Protocol / Fonts.Inter） | 11.3.10 | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| [MicroCom.Runtime](https://github.com/kekekeks/MicroCom) | 0.11.0 | MIT | Copyright 2021 © Nikita Tsukanov |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 2.88.9 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.<br>Copyright (c) 2017-2018 Microsoft Corporation |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | 8.3.1.1 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.<br>Copyright (c) 2017-2018 Microsoft Corporation |
| [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) | 2.6.2 | MIT | Copyright © Legion of the Bouncy Castle Inc. 2000-2025 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.0 | MIT | (c) .NET Foundation and Contributors. All rights reserved. |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | 1.4.2 | MIT | Copyright © 2000-2022 SharpZipLib Contributors |
| [Svg.Controls.Skia.Avalonia](https://github.com/wieslawsoltes/Svg.Skia) / Svg.Skia / Svg.Model / ShimSkiaSharp | 11.3.6.2 / 3.2.1 | MIT | Copyright © Wiesław Šoltés 2025 |
| [Svg.Custom](https://github.com/wieslawsoltes/Svg.Skia) | 3.2.1 | **Ms-PL** | Copyright © Wiesław Šoltés 2025<br>（[SVG.NET](https://github.com/svg-net/SVG) 由来） |
| [ExCSS](https://github.com/TylerBrinks/ExCSS) | 4.3.1 | MIT | Copyright (c) 2024 Tyler Brinks |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | 0.21.3 | MIT | Tom Deseyn |
| [.NET ランタイム](https://github.com/dotnet/runtime) | 10 | MIT | Copyright (c) .NET Foundation and Contributors |
| [DotNetZip (Zlib)](https://github.com/DinoChiesa/DotNetZip) | — | **Ms-PL** | Copyright (c) 2006-2011 Dino Chiesa<br>`src/AttacheCase.Core/Formats/Zlib/*.cs`（原文: `Formats/Zlib/LICENSE.Ms-PL.txt`）。旧形式（v105/v130）復号用のソース移植 |
| [Costura.Fody](https://github.com/Fody/Costura) / [Fody](https://github.com/Fody/Fody) | 6.0.0 / 6.9.3 | MIT | Copyright (c) 2012 Simon Cropp and contributors<br>**自己実行形式（SFX）スタブ Exeout.exe にローダーが埋め込まれます**（Windows 版のみ同梱） |
| [System.Resources.Extensions](https://github.com/dotnet/runtime) | 9.0.0 | MIT | Copyright (c) .NET Foundation and Contributors（SFX スタブに埋め込み） |
| [zxcvbn-core](https://github.com/trichards57/zxcvbn-cs) | 7.0.92 | MIT | Copyright (c) 2017 Tony Richards ほか（[Dropbox zxcvbn](https://github.com/dropbox/zxcvbn) の C# 移植）。パスワード強度メーター |
| [ShadUI](https://github.com/accntech/shad-ui) | 0.1.6 | MIT | Copyright (c) 2025 Accountech Business Management Services（shadcn/ui 風の Avalonia UI テーマ） |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | 9.0.0 | MIT | Copyright (c) .NET Foundation and Contributors（Windows の DPAPI。記憶パスワード・Post-PPAP 秘密鍵の保護。Windows 版のみ実行時に使用） |
| [System.Text.Encoding.CodePages](https://github.com/dotnet/runtime) | 9.0.0 | MIT | Copyright (c) .NET Foundation and Contributors（旧形式 v105 / v130 のファイル名 CP932 復号） |
| Microsoft.Bcl.HashCode / System.Buffers / System.Collections.Immutable / System.Formats.Nrbf / System.Memory / System.Numerics.Vectors / System.Reflection.Metadata / System.Runtime.CompilerServices.Unsafe / System.ValueTuple | — | MIT | Copyright (c) .NET Foundation and Contributors（**Windows 版のみ**。net48 の AtcSetup.exe に添える .NET Framework 向け互換アセンブリ。`{app}\AtcSetup\` に展開） |
| [jzlib](https://github.com/ymnk/jzlib) | — | BSD-style | Copyright (c) 2000,2001,2002,2003 ymnk, JCraft, Inc.（Zlib の一部。原文: `Formats/Zlib/LICENSE.jzlib.txt`） |
| [zlib](https://zlib.net/) | — | zlib License | Copyright (C) 1995-2004 Jean-loup Gailly and Mark Adler（Zlib の一部。原文: `Formats/Zlib/License.zlib.txt`） |

- **Svg.Custom だけライセンスが違います（Ms-PL）。** MIT とひとまとめにしないこと
- **.NET ランタイムを同梱しています。** self-contained で発行しているため、
  実行形式の中に BCL 一式（200 以上のアセンブリ）が入ります
- Avalonia.Win32 / X11 / FreeDesktop と Tmds.DBus.Protocol は、macOS では実行時に
  使いませんが、**配布物には入ります**

### ネイティブライブラリ

macOS では `Contents/MacOS/` に置かれる dylib、Windows では単一ファイルの実行形式に埋め込まれ
実行時に展開される DLL です。中に別のライセンスの成果物が入っています。

| ファイル | 中身 | ライセンス | 著作権表示 |
|---|---|---|---|
| `libSkiaSharp.dylib` / `libSkiaSharp.dll` | [Skia](https://skia.org/) | BSD 3-Clause | Copyright (c) 2011 Google Inc. All rights reserved. |
| `libHarfBuzzSharp.dylib` / `libHarfBuzzSharp.dll` | [HarfBuzz](https://github.com/harfbuzz/harfbuzz) | Old MIT | 下記全文のとおり（複数の権利者） |
| `libAvaloniaNative.dylib`（macOS のみ） | Avalonia | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| `av_libglesv2.dll`（Windows のみ、Avalonia.Angle.Windows.Natives） | [ANGLE](https://chromium.googlesource.com/angle/angle) | BSD 3-Clause | Copyright The ANGLE Project Authors. All rights reserved. |

### Windows インストーラ（Windows のみ）

| コンポーネント | ライセンス | 備考 |
|---|---|---|
| [Inno Setup](https://jrsoftware.org/isinfo.php) 6 | Inno Setup License（Jordan Russell / Martijn Laan） | インストーラ `atcs*.exe` の実行部。ZIP 配布には含まれない |
| Inno Download Plugin（`idp.dll`） | **要確認**（配布前に原文のライセンスを添えること） | インストーラが .NET ランタイム等を取得するときに使用 |

### フォント

| コンポーネント | ライセンス | 著作権表示 |
|---|---|---|
| [Inter](https://github.com/rsms/inter)（Avalonia.Fonts.Inter に埋め込み） | SIL Open Font License 1.1 | Copyright (c) 2016 The Inter Project Authors |

`Program.cs` の `WithInterFont()` で実際に使っています。**フォントを配る以上、
OFL の全文を添える必要があります**（下記 3 節）。

### システムフレームワーク

| コンポーネント | 備考 |
|---|---|
| Apple Security.framework | Keychain の読み書きに P/Invoke で使用。macOS 同梱のため再配布はしていない |
| Windows: crypt32（DPAPI）/ shell32 / user32 / advapi32（レジストリ）/ wintrust（署名検証）/ ITaskbarList3 | Windows 版が P/Invoke で使用。Windows 同梱のため再配布はしていない |

---

## 2. 配布物に含まれないもの

同梱していないので表示の義務はありませんが、取り違えないよう記録します。

| コンポーネント | 理由 |
|---|---|
| Avalonia.Diagnostics | Debug ビルドのみ。Release の発行物には入らない |
| Avalonia.BuildServices | ビルド時のみ動く。成果物には入らない |
| SkiaSharp / HarfBuzzSharp の Linux・WebAssembly 向け NativeAssets | どちらの版にも入らない（macOS 版は osx、Windows 版は win-x64 のみ） |
| Microsoft.NETFramework.ReferenceAssemblies | net48 プロジェクト（Exeout / AtcSetup）のビルド時のみ。成果物には入らない |
| xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk / coverlet.collector | テストプロジェクト専用 |

---

## 3. ライセンス全文

### 3.1 MIT License

次のコンポーネントに適用されます。著作権表示は 1 節の表のとおりです。

Avalonia（および各構成パッケージ）／MicroCom.Runtime／SkiaSharp／HarfBuzzSharp／
BouncyCastle.Cryptography／CommunityToolkit.Mvvm／SharpZipLib／
Svg.Controls.Skia.Avalonia／Svg.Skia／Svg.Model／ShimSkiaSharp／ExCSS／
Tmds.DBus.Protocol／ShadUI／.NET ランタイム

```
MIT License

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

### 3.2 Microsoft Public License (Ms-PL) — Svg.Custom

```
Microsoft Public License (Ms-PL)

This license governs use of the accompanying software. If you use the software, you accept this license. If you do not accept the license, do not use the software.

1. Definitions

The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same meaning here as under U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the software.

A "contributor" is any person that distributes its contribution under this license.

"Licensed patents" are a contributor's patent claims that read directly on its contribution.

2. Grant of Rights

(A) Copyright Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free copyright license to reproduce its contribution, prepare derivative works of its contribution, and distribute its contribution or any derivative works that you create.

(B) Patent Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free license under its licensed patents to make, have made, use, sell, offer for sale, import, and/or otherwise dispose of its contribution in the software or derivative works of the contribution in the software.

3. Conditions and Limitations

(A) No Trademark License- This license does not grant you rights to use any contributors' name, logo, or trademarks.

(B) If you bring a patent claim against any contributor over patents that you claim are infringed by the software, your patent license from such contributor to the software ends automatically.

(C) If you distribute any portion of the software, you must retain all copyright, patent, trademark, and attribution notices that are present in the software.

(D) If you distribute any portion of the software in source code form, you may do so only under this license by including a complete copy of this license with your distribution. If you distribute any portion of the software in compiled or object code form, you may only do so under a license that complies with this license.

(E) The software is licensed "as-is." You bear the risk of using it. The contributors give no express warranties, guarantees or conditions. You may have additional consumer rights under your local laws which this license cannot change. To the extent permitted under your local laws, the contributors exclude the implied warranties of merchantability, fitness for a particular purpose and non-infringement.
```

### 3.3 SIL Open Font License 1.1 — Inter

```
Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
http://scripts.sil.org/OFL

-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION AND CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```

### 3.4 BSD 3-Clause License — Skia

```
Copyright (c) 2011 Google Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in
    the documentation and/or other materials provided with the
    distribution.

  * Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived
    from this software without specific prior written permission.

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
```

### 3.5 Old MIT License — HarfBuzz

```
HarfBuzz is licensed under the so-called "Old MIT" license.  Details follow.
For parts of HarfBuzz that are licensed under different licenses see individual
files names COPYING in subdirectories where applicable.

Copyright © 2010-2022  Google, Inc.
Copyright © 2015-2020  Ebrahim Byagowi
Copyright © 2019,2020  Facebook, Inc.
Copyright © 2012,2015  Mozilla Foundation
Copyright © 2011  Codethink Limited
Copyright © 2008,2010  Nokia Corporation and/or its subsidiary(-ies)
Copyright © 2009  Keith Stribley
Copyright © 2011  Martin Hosken and SIL International
Copyright © 2007  Chris Wilson
Copyright © 2005,2006,2020,2021,2022,2023  Behdad Esfahbod
Copyright © 2004,2007,2008,2009,2010,2013,2021,2022,2023  Red Hat, Inc.
Copyright © 1998-2005  David Turner and Werner Lemberg
Copyright © 2016  Igalia S.L.
Copyright © 2022  Matthias Clasen
Copyright © 2018,2021  Khaled Hosny
Copyright © 2018,2019,2020  Adobe, Inc
Copyright © 2013-2015  Alexei Podtelezhnikov

For full copyright notices consult the individual files in the package.


Permission is hereby granted, without written agreement and without
license or royalty fees, to use, copy, modify, and distribute this
software and its documentation for any purpose, provided that the
above copyright notice and the following two paragraphs appear in
all copies of this software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
```

---

## 4. 同梱素材

アイコン・画像素材（`Assets/Icons/`、`Assets/dialog/`、`Assets/main-icon.*`、
`Assets/atc-icon.icns`、`dmg/`）はライセンサーが作成したものです。第三者素材は含まれていません。

---

## 5. 一覧の作り方

**心当たりで書かない。成果物を実測する。** 依存を足したり外したりしたときは、
必ずこの手順でこの一覧を作り直してください。

```bash
# 配布物に実際に入るパッケージ（発行時の deps.json が正）
python3 -c "import json;d=json.load(open('bin/Release/net10.0/osx-arm64/AttacheCase.deps.json'));\
print('\n'.join(sorted(k for k,v in d['libraries'].items() if v['type']=='package')))"

# ライセンスと著作権表示は nuspec が一次資料
sed -n 's:.*<license type="expression">\(.*\)</license>.*:\1:p;s:.*<copyright>\(.*\)</copyright>.*:\1:p' \
    ~/.nuget/packages/<パッケージ>/<版>/*.nuspec
```

`dotnet list package --include-transitive` は**参照の一覧であって成果物の一覧ではありません**。
これだけを見ると、実体の無いものを載せてしまいます（実際に Newtonsoft.Json と
Avalonia.Controls.ColorPicker を載せていました）。

---

## English

This software bundles third-party software. Each third-party component remains
under its own license; the AttacheCase Source Available License and any
Commercial, Integration or OEM License granted by the licensor do not modify or
override those licenses.

Section 1 lists what is actually contained in the distributed application, with
versions, licenses and copyright notices. Section 2 lists components that are
**not** shipped. Section 3 reproduces the full text of every license that applies:
MIT, Ms-PL (Svg.Custom), SIL Open Font License 1.1 (Inter), BSD 3-Clause (Skia)
and the "Old MIT" license (HarfBuzz).
