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
// macOS Keychain Services への P/Invoke ラッパー。
//   Generic Password 項目 (kSecClassGenericPassword) を service + account の
//   キーで保存/取得/削除する。
//
// 参照: Security.framework / Keychain Services Reference (Apple)
//----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace AttacheCase.Services;

public sealed class KeychainService : IKeychainService
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string Security =
        "/System/Library/Frameworks/Security.framework/Security";

    // CFStringEncoding kCFStringEncodingUTF8
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    // OSStatus values
    private const int errSecSuccess = 0;
    private const int errSecItemNotFound = -25300;
    private const int errSecDuplicateItem = -25299;

    public bool TryGet(string service, string account, out byte[]? data)
    {
        EnsureMacOS();
        data = null;

        var cfService = CFStringCreate(service);
        var cfAccount = CFStringCreate(account);
        try
        {
            using var query = CFMutableDictionary.Create();
            query.Add(s_kSecClass, s_kSecClassGenericPassword);
            query.Add(s_kSecAttrService, cfService);
            query.Add(s_kSecAttrAccount, cfAccount);
            query.Add(s_kSecReturnData, s_kCFBooleanTrue);
            query.Add(s_kSecMatchLimit, s_kSecMatchLimitOne);

            var status = SecItemCopyMatching(query.Handle, out var resultRef);
            if (status == errSecItemNotFound) return false;
            ThrowIfError(status, "SecItemCopyMatching");

            try
            {
                data = CFDataToBytes(resultRef);
                return true;
            }
            finally
            {
                if (resultRef != IntPtr.Zero) CFRelease(resultRef);
            }
        }
        finally
        {
            CFRelease(cfService);
            CFRelease(cfAccount);
        }
    }

    public void Set(string service, string account, byte[] data)
    {
        EnsureMacOS();
        if (data == null) throw new ArgumentNullException(nameof(data));

        var cfService = CFStringCreate(service);
        var cfAccount = CFStringCreate(account);
        var cfData = CFDataCreate(IntPtr.Zero, data, data.LongLength);
        if (cfData == IntPtr.Zero) throw new InvalidOperationException("CFDataCreate failed.");

        try
        {
            // まず追加を試みる
            using (var addQuery = CFMutableDictionary.Create())
            {
                addQuery.Add(s_kSecClass, s_kSecClassGenericPassword);
                addQuery.Add(s_kSecAttrService, cfService);
                addQuery.Add(s_kSecAttrAccount, cfAccount);
                addQuery.Add(s_kSecValueData, cfData);
                // kSecAttrAccessible は付けない。
                // この属性が意味を持つのはデータ保護 Keychain だけで、そちらを使うには
                // keychain-access-groups の entitlement とコード署名が要る（無いと -34018）。
                // ここは従来の file-based Keychain（login.keychain）が対象なので、
                // 指定しても効かない属性を残さない。保護はキーチェーンのロックに依る

                var addStatus = SecItemAdd(addQuery.Handle, out var unused);
                if (unused != IntPtr.Zero) CFRelease(unused);

                if (addStatus == errSecSuccess) return;
                if (addStatus != errSecDuplicateItem)
                {
                    ThrowIfError(addStatus, "SecItemAdd");
                    return;
                }
            }

            // 既存項目あり → 更新
            using var query = CFMutableDictionary.Create();
            query.Add(s_kSecClass, s_kSecClassGenericPassword);
            query.Add(s_kSecAttrService, cfService);
            query.Add(s_kSecAttrAccount, cfAccount);

            using var update = CFMutableDictionary.Create();
            update.Add(s_kSecValueData, cfData);

            var updateStatus = SecItemUpdate(query.Handle, update.Handle);
            ThrowIfError(updateStatus, "SecItemUpdate");
        }
        finally
        {
            CFRelease(cfService);
            CFRelease(cfAccount);
            CFRelease(cfData);
        }
    }

    public bool Delete(string service, string account)
    {
        EnsureMacOS();

        var cfService = CFStringCreate(service);
        var cfAccount = CFStringCreate(account);
        try
        {
            using var query = CFMutableDictionary.Create();
            query.Add(s_kSecClass, s_kSecClassGenericPassword);
            query.Add(s_kSecAttrService, cfService);
            query.Add(s_kSecAttrAccount, cfAccount);

            var status = SecItemDelete(query.Handle);
            if (status == errSecItemNotFound) return false;
            ThrowIfError(status, "SecItemDelete");
            return true;
        }
        finally
        {
            CFRelease(cfService);
            CFRelease(cfAccount);
        }
    }

    // =================================================================
    // ヘルパー
    // =================================================================

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "KeychainService は macOS でのみ動作します。");
    }

    private static void ThrowIfError(int status, string op)
    {
        if (status == errSecSuccess) return;
        throw new InvalidOperationException(
            $"{op} failed with OSStatus={status}.");
    }

    private static IntPtr CFStringCreate(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var cf = CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length,
            kCFStringEncodingUTF8, false);
        if (cf == IntPtr.Zero)
            throw new InvalidOperationException("CFStringCreateWithBytes failed.");
        return cf;
    }

    private static byte[] CFDataToBytes(IntPtr cfData)
    {
        var length = (int)CFDataGetLength(cfData);
        if (length == 0) return Array.Empty<byte>();
        var ptr = CFDataGetBytePtr(cfData);
        var buf = new byte[length];
        Marshal.Copy(ptr, buf, 0, length);
        return buf;
    }

    // =================================================================
    // P/Invoke: Core Foundation
    // =================================================================

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithBytes(
        IntPtr alloc, byte[] bytes, long numBytes,
        uint encoding, [MarshalAs(UnmanagedType.I1)] bool isExternal);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr alloc, byte[] bytes, long length);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr theData);

    [DllImport(CoreFoundation)]
    private static extern long CFDataGetLength(IntPtr theData);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreateMutable(
        IntPtr alloc, long capacity,
        IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern void CFDictionarySetValue(
        IntPtr theDict, IntPtr key, IntPtr value);

    // =================================================================
    // P/Invoke: Security
    // =================================================================

    [DllImport(Security)]
    private static extern int SecItemAdd(IntPtr attributes, out IntPtr result);

    [DllImport(Security)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(Security)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(Security)]
    private static extern int SecItemDelete(IntPtr query);

    // =================================================================
    // dlopen/dlsym によるシンボル解決（CFStringRef *const グローバル）
    // =================================================================

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlsym")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    // CFDictionary 用標準コールバック（構造体そのもの）
    private static readonly IntPtr s_kCFTypeDictionaryKeyCallBacks;
    private static readonly IntPtr s_kCFTypeDictionaryValueCallBacks;

    // Security.framework の CFStringRef 定数
    private static readonly IntPtr s_kSecClass;
    private static readonly IntPtr s_kSecClassGenericPassword;
    private static readonly IntPtr s_kSecAttrService;
    private static readonly IntPtr s_kSecAttrAccount;
    private static readonly IntPtr s_kSecValueData;
    private static readonly IntPtr s_kSecReturnData;
    private static readonly IntPtr s_kSecMatchLimit;
    private static readonly IntPtr s_kSecMatchLimitOne;
    private static readonly IntPtr s_kCFBooleanTrue;

    /// <summary>
    /// 静的コンストラクタ。dlopen → 全シンボル解決を確実な順序で行う。
    /// （静的フィールド初期化子だと宣言順依存になり、ハンドル未初期化のまま
    ///  シンボル解決が走って IntPtr.Zero になる事故が起きやすい）
    /// </summary>
    static KeychainService()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var securityHandle = dlopen(Security, 0);
        var cfHandle = dlopen(CoreFoundation, 0);

        // CFType ポインタ変数（dlsym は変数アドレスを返すので1段読む）
        s_kSecClass = ReadIndirect(securityHandle, "kSecClass");
        s_kSecClassGenericPassword = ReadIndirect(securityHandle, "kSecClassGenericPassword");
        s_kSecAttrService = ReadIndirect(securityHandle, "kSecAttrService");
        s_kSecAttrAccount = ReadIndirect(securityHandle, "kSecAttrAccount");
        s_kSecValueData = ReadIndirect(securityHandle, "kSecValueData");
        s_kSecReturnData = ReadIndirect(securityHandle, "kSecReturnData");
        s_kSecMatchLimit = ReadIndirect(securityHandle, "kSecMatchLimit");
        s_kSecMatchLimitOne = ReadIndirect(securityHandle, "kSecMatchLimitOne");
        s_kCFBooleanTrue = ReadIndirect(cfHandle, "kCFBooleanTrue");

        // 構造体そのもの（アドレス直渡し）
        s_kCFTypeDictionaryKeyCallBacks = dlsym(cfHandle, "kCFTypeDictionaryKeyCallBacks");
        s_kCFTypeDictionaryValueCallBacks = dlsym(cfHandle, "kCFTypeDictionaryValueCallBacks");
    }

    private static IntPtr ReadIndirect(IntPtr handle, string name)
    {
        var addr = dlsym(handle, name);
        return addr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(addr);
    }

    // =================================================================
    // CFDictionary 構築用ヘルパー（IDisposable で確実に CFRelease）
    // =================================================================

    private sealed class CFMutableDictionary : IDisposable
    {
        public IntPtr Handle { get; private set; }

        private CFMutableDictionary(IntPtr handle) => Handle = handle;

        public static CFMutableDictionary Create()
        {
            var h = CFDictionaryCreateMutable(
                IntPtr.Zero, 0,
                s_kCFTypeDictionaryKeyCallBacks,
                s_kCFTypeDictionaryValueCallBacks);
            if (h == IntPtr.Zero)
                throw new InvalidOperationException("CFDictionaryCreateMutable failed.");
            return new CFMutableDictionary(h);
        }

        public void Add(IntPtr key, IntPtr value)
        {
            if (key == IntPtr.Zero || value == IntPtr.Zero)
                throw new InvalidOperationException("Cannot add NULL key/value to CFDictionary.");
            CFDictionarySetValue(Handle, key, value);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                CFRelease(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}
