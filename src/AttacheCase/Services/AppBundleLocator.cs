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
// 自動更新で差し替える .app の位置を求め、差し替えてよい場所かを判定する。
//
// ここを誤ると、更新のつもりで無関係な場所を壊すことになる。判定は保守的に行い、
// 少しでも確信が持てない形なら差し替えない（手動更新に案内する）。
//----------------------------------------------------------------------

using System;
using System.IO;

namespace AttacheCase.Services;

/// <summary>差し替えの可否と、その理由。</summary>
public enum InstallLocationStatus
{
    /// <summary>差し替えられる。</summary>
    Ok,

    /// <summary>
    /// .app バンドルの中から実行されていない。開発中の dotnet run など。
    /// </summary>
    NotInAppBundle,

    /// <summary>
    /// App Translocation 配下。隔離属性の付いたアプリを Finder から起動すると、
    /// macOS が読み取り専用の場所へ複製して実行する。ここを差し替えても意味が無い。
    /// 利用者には「一度 /Applications へ移動してください」と案内する。
    /// </summary>
    Translocated,

    /// <summary>
    /// 読み取り専用の場所。DMG から直接起動した場合など。
    /// </summary>
    ReadOnlyLocation,

    /// <summary>
    /// 書き込む権限が無い。管理者でない利用者の /Applications など。
    /// 権限昇格は求めない（昇格を伴う自動更新は、それ自体が攻撃経路になる）。
    /// </summary>
    NotWritable,
}

/// <summary>差し替え対象の位置。</summary>
/// <param name="Status">差し替えてよいか。</param>
/// <param name="BundlePath">.app バンドルのパス。判定できなければ null。</param>
public sealed record InstallLocation(InstallLocationStatus Status, string? BundlePath)
{
    public bool CanReplace => Status == InstallLocationStatus.Ok && BundlePath is not null;
}

public static class AppBundleLocator
{
    /// <summary>
    /// 実行中のアプリについて、差し替え先を求める。
    /// </summary>
    public static InstallLocation Locate() =>
        Locate(Environment.ProcessPath, Directory.Exists, IsWritable);

    /// <summary>
    /// 差し替え先を求める。テストから探索と書き込み可否を差し替えられるようにしてある。
    /// </summary>
    /// <param name="executablePath">
    /// 実行ファイルのパス。.app/Contents/MacOS/&lt;name&gt; の形を期待する。
    /// </param>
    /// <param name="directoryExists">ディレクトリの存在確認。</param>
    /// <param name="isWritable">書き込めるかの確認。</param>
    internal static InstallLocation Locate(
        string? executablePath,
        Func<string, bool> directoryExists,
        Func<string, bool> isWritable)
    {
        var bundlePath = FindBundlePath(executablePath, directoryExists);
        if (bundlePath is null)
        {
            return new InstallLocation(InstallLocationStatus.NotInAppBundle, null);
        }

        if (IsTranslocated(bundlePath))
        {
            return new InstallLocation(InstallLocationStatus.Translocated, bundlePath);
        }

        if (IsReadOnlyLocation(bundlePath))
        {
            return new InstallLocation(InstallLocationStatus.ReadOnlyLocation, bundlePath);
        }

        // 差し替えは「バンドルを置き換える」操作なので、書き込む必要があるのは
        // バンドル自身ではなく、それを収めている親ディレクトリ
        var parent = Path.GetDirectoryName(bundlePath);
        if (string.IsNullOrEmpty(parent) || !isWritable(parent))
        {
            return new InstallLocation(InstallLocationStatus.NotWritable, bundlePath);
        }

        return new InstallLocation(InstallLocationStatus.Ok, bundlePath);
    }

    /// <summary>
    /// 実行ファイルのパスから .app バンドルを求める。
    /// <c>/Applications/AttacheCase.app/Contents/MacOS/AttacheCase</c> から
    /// <c>/Applications/AttacheCase.app</c> を得る。
    /// 期待する形でなければ null を返す。
    /// </summary>
    internal static string? FindBundlePath(string? executablePath, Func<string, bool> directoryExists)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        // .../<Name>.app/Contents/MacOS/<exe> の 3 階層をさかのぼる
        var macOsDir = Path.GetDirectoryName(executablePath);
        if (macOsDir is null || Path.GetFileName(macOsDir) != "MacOS") return null;

        var contentsDir = Path.GetDirectoryName(macOsDir);
        if (contentsDir is null || Path.GetFileName(contentsDir) != "Contents") return null;

        var bundleDir = Path.GetDirectoryName(contentsDir);
        if (bundleDir is null) return null;
        if (!Path.GetFileName(bundleDir).EndsWith(".app", StringComparison.Ordinal)) return null;
        if (!directoryExists(bundleDir)) return null;

        return bundleDir;
    }

    /// <summary>
    /// App Translocation 配下か。macOS は隔離属性の付いたアプリを、読み取り専用の
    /// /private/var/folders/.../AppTranslocation/&lt;UUID&gt;/d/ へ複製して実行する。
    /// </summary>
    internal static bool IsTranslocated(string path) =>
        path.Contains("/AppTranslocation/", StringComparison.Ordinal);

    /// <summary>
    /// 読み取り専用とみなす場所か。マウントしたディスクイメージから直接
    /// 起動している場合など。
    /// </summary>
    internal static bool IsReadOnlyLocation(string path) =>
        path.StartsWith("/Volumes/", StringComparison.Ordinal);

    /// <summary>
    /// そのディレクトリに書き込めるか。権限は実際に書いてみないと分からない
    /// （読めるが書けない、ACL で拒否される、といった場合がある）ので、
    /// 一時ファイルを作って確かめる。
    /// </summary>
    internal static bool IsWritable(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;

            var probe = Path.Combine(directory, $".attachecase-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
}
