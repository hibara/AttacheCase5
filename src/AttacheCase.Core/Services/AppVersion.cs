//----------------------------------------------------------------------
// SPDX-License-Identifier: LicenseRef-AttacheCase-SourceAvailable-1.0
// Copyright (c) 2016-2026 HiBARA Software, LLC
// Released under the AttacheCase Source Available License Version 1.0 (LICENSE.md).
// This is NOT an open source license. Business use requires a separate Commercial,
// Integration or OEM License (COMMERCIAL-LICENSE.md).
// Third-party components remain under their own licenses (THIRD_PARTY_NOTICES.md).
// AttacheCase Source Available License Version 1.0 に基づき公開しています。
// オープンソースライセンスではありません。業務・商用利用には別途
// Commercial / Integration / OEM License が必要です（詳細: LICENSE.md / COMMERCIAL-LICENSE.md）。
//
// 自分のバージョン。
//
// もとは「バージョン情報」画面・コマンドラインの --version・更新確認の 3 か所が
// それぞれ Assembly から取り、それぞれ違う既定値を持っていた（"5.0.0" と "4.3.0β"）。
// 版を上げるたびに直し漏れるので、ここに一本化する。
//
// 実体は csproj の AssemblyVersion。build_and_release.sh が同じ値を
// Info.plist の CFBundleShortVersionString へ反映する。
//----------------------------------------------------------------------

using System;
using System.Reflection;

namespace AttacheCase.Services;

public static class AppVersion
{
    /// <summary>
    /// 実行中のアプリのバージョン。リビジョンは持たない（例: 5.0.1）。
    /// </summary>
    public static Version Current
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null
                ? new Version(0, 0, 0)
                : new Version(version.Major, version.Minor, version.Build);
        }
    }

    /// <summary>
    /// 4 桁のフルバージョン（例: 5.0.0.3）。
    /// Windows の更新確認はこちらを使う。Windows は 4 桁目（Revision）で版を
    /// 進める運用のため、3 桁に丸めた <see cref="Current"/> で比較すると
    /// 5.0.0.3 と 5.0.0.4 が同じに見えて更新を検出できない（実際に踏んだ問題）。
    /// </summary>
    public static Version CurrentFull
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version ?? new Version(0, 0, 0, 0);
        }
    }

    /// <summary>
    /// 画面やコマンドラインに出す表記。
    /// Windows は 4 桁（例: "5.0.0.3"。Revision で版を進める運用のため）、
    /// それ以外は 3 桁（例: "5.0.1"）。
    /// </summary>
    public static string Display
        => OperatingSystem.IsWindows() ? CurrentFull.ToString() : Current.ToString();
}
