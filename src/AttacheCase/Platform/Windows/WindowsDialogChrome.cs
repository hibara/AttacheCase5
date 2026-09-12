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
// ダイアログのタイトルバーを「閉じる（×）だけ」にする（Windows のみ）。
//
// 所有モーダル（ShowDialog）はタスクバーに出ないため、最小化されると
// 復帰手段がなく行方不明になる。旧 WinForms なら FormBorderStyle=FixedDialog
// 相当だが、Avalonia 11 に対応プロパティが無いので、Win32 のウィンドウ
// スタイルから WS_MINIMIZEBOX / WS_MAXIMIZEBOX を直接落とす。
//----------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace AttacheCase.Platform.Windows;

internal static class WindowsDialogChrome
{
    private const int GWL_STYLE = -16;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    // 64bit Windows のみ（本アプリは win-x64 / win-arm64 配布）。
    // 32bit では GetWindowLongPtrW が user32 に無い点に注意
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// タイトルバーのボタンを閉じる（×）だけにする。ウィンドウ作成後
    /// （Opened 以降）に呼ぶこと。Windows 以外では何もしない。
    /// </summary>
    public static void MakeCloseOnly(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;

        var hWnd = handle.Handle;
        var style = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
        style &= ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLongPtr(hWnd, GWL_STYLE, new IntPtr(style));

        // スタイル変更を非クライアント領域（タイトルバー）へ反映させる
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }
}
