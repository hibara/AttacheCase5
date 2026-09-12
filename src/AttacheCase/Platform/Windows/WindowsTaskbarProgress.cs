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
//----------------------------------------------------------------------
// Windows のタスクバーボタンに進捗を表示する（ITaskbarList3）。
// Windows 版 CustomControl\TaskbarProgress.cs に相当。
// macOS の Dock 進捗（MacDockTile）の Windows 版で、暗号化・復号中に
// タスクバーの緑のゲージとして見える。
//
// 失敗（COM 不可の環境・古い OS）はすべて握り潰す。進捗表示は飾りで、
// これが原因で処理を止めてはならない。
//----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AttacheCase.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsTaskbarProgress
{
    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8,
    }

    // ITaskbarList3 は ITaskbarList / ITaskbarList2 を継承する。
    // COM の vtable 順を保つため、使わないメソッドも順番どおりに宣言する
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig] int SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance
    {
    }

    private static ITaskbarList3? _taskbar;
    private static bool _initFailed;

    private static ITaskbarList3? GetTaskbar()
    {
        if (_taskbar != null) return _taskbar;
        if (_initFailed) return null;

        try
        {
            var instance = (ITaskbarList3)new TaskbarInstance();
            instance.HrInit();
            _taskbar = instance;
            return _taskbar;
        }
        catch (Exception)
        {
            _initFailed = true;
            return null;
        }
    }

    /// <summary>進捗（0〜100）をタスクバーボタンに表示する。</summary>
    public static void SetProgress(IntPtr hwnd, int percent)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var taskbar = GetTaskbar();
            if (taskbar == null) return;

            var clamped = Math.Clamp(percent, 0, 100);
            taskbar.SetProgressState(hwnd, TBPFLAG.TBPF_NORMAL);
            taskbar.SetProgressValue(hwnd, (ulong)clamped, 100);
        }
        catch (Exception)
        {
            // 進捗表示は飾り。失敗しても処理は続ける
        }
    }

    /// <summary>進捗表示を消す。</summary>
    public static void Clear(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            GetTaskbar()?.SetProgressState(hwnd, TBPFLAG.TBPF_NOPROGRESS);
        }
        catch (Exception)
        {
            // 同上
        }
    }
}
