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
// Dock アイコンへの進捗表示。
//
// NSDockTile のバッジ（アイコン右上の赤い丸）に「42%」のように表示する。
// 本物のプログレスバーを描くには Dock タイルへ独自ビューを差し込む必要があり、
// 割に合わないため、標準の API で確実に出せるバッジで代用している。
//
// 必ず UI スレッドから呼ぶこと（AppKit の制約）。
//----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace AttacheCase.Services;

public static class MacDockTile
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";

    private static bool _unavailable;
    private static string? _currentBadge;

    /// <summary>
    /// バッジの文字列を設定する。null または空文字で消す。
    /// </summary>
    public static void SetBadge(string? text)
    {
        if (_unavailable || !OperatingSystem.IsMacOS()) return;
        if (_currentBadge == text) return;

        _currentBadge = text;

        try
        {
            var app = SendMessage(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) return;

            var tile = SendMessage(app, sel_registerName("dockTile"));
            if (tile == IntPtr.Zero) return;

            var label = string.IsNullOrEmpty(text)
                ? IntPtr.Zero
                : SendMessageUtf8(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text);

            SendMessageVoidPtr(tile, sel_registerName("setBadgeLabel:"), label);
            SendMessageVoid(tile, sel_registerName("display"));
        }
        catch (Exception)
        {
            // Dock が使えない環境（バンドル外実行など）では黙ってあきらめる
            _unavailable = true;
        }
    }

    /// <summary>進捗をパーセント表示にする</summary>
    public static void SetProgress(int percent) => SetBadge(Math.Clamp(percent, 0, 100) + "%");

    /// <summary>バッジを消す</summary>
    public static void Clear() => SetBadge(null);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageUtf8(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoid(IntPtr receiver, IntPtr selector);
}
