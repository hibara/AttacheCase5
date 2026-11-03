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
// PC がタブレットモード（スレートモード）で動作しているかの判定（Windows のみ）。
//
// 旧 5.0 の Form1.IsTabletMode と同じく GetSystemMetrics(SM_CONVERTIBLESLATEMODE) を見る
// （0 = スレート（タブレット）モード、それ以外 = ラップトップモード）。
// タブレットモードでは OS がウィンドウを全画面にするため、設定
// 「PC がタブレットモードで動作しているとき、最大化モードで起動しない」の判定に使う。
//----------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace AttacheCase.Platform.Windows;

internal static class WindowsTabletMode
{
    private const int SM_CONVERTIBLESLATEMODE = 0x2003;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>タブレットモードなら true。Windows 以外、または判定できなければ false</summary>
    public static bool IsTabletMode()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            return GetSystemMetrics(SM_CONVERTIBLESLATEMODE) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
