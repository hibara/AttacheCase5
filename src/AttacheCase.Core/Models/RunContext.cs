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
// 1 回の暗号化・復号処理に与える実行条件。
//
// GUI 操作なら保存されている設定がそのまま入り、コマンドライン起動なら
// 設定の複製に引数を上書きしたものが入る。パスワードや /out のように
// 「その実行にだけ効くもの」は設定ではなくこちらに持たせる。
//----------------------------------------------------------------------

using AttacheCase.Services;

namespace AttacheCase.Models;

public sealed class RunContext
{
    public required AppSettings Settings { get; init; }

    /// <summary>設定の保存先。既定は共有インスタンス（テストで差し替えられるようにしてある）</summary>
    public SettingsService Store { get; init; } = SettingsService.Instance;

    /// <summary>コマンドライン引数から始まった処理か</summary>
    public bool IsCommandLineRun { get; init; }

    /// <summary>コマンドラインで渡されたパスワード（/p）</summary>
    public string? Password { get; init; }

    /// <summary>パスワードファイル（/pfile, /dpfile）から求めた鍵素材</summary>
    public byte[]? PasswordBinary { get; init; }

    /// <summary>出力先の指定（/out）。この実行にだけ効く</summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// 無人実行。ダイアログを出しても誰も閉じられないため、
    /// 確認や通知はダイアログではなく標準エラー出力に回す。
    /// </summary>
    public bool IsUnattended => IsCommandLineRun && Settings.ExitAfterProcess;

    /// <summary>パスワードが引数で与えられており、入力を待たずに始められるか</summary>
    public bool HasPassword => Password != null || PasswordBinary != null;

    /// <summary>GUI 操作用（保存されている設定をそのまま使う）</summary>
    public static RunContext ForGui()
    {
        var store = SettingsService.Instance;
        return new RunContext { Settings = store.Current, Store = store };
    }

    /// <summary>
    /// パスワード入力欄の目玉アイコンで切り替えた状態を、設定として覚える。
    /// コマンドライン起動時は複製を書き換えるだけで、settings.json には保存しない。
    /// </summary>
    public void RememberPasswordVisibility(bool visible)
    {
        Settings.NotMaskPassword = visible;

        if (IsCommandLineRun) return;

        Store.Save(Settings);
    }

    /// <summary>
    /// 暗号化ファイルの種類（ATC / EXE / ZIP）の選択を設定として覚える。
    /// パスワード入力画面のコンボボックスで選び、暗号化を実行したときに呼ぶ。
    /// 設定「前に使ったファイルタイプで暗号化する」（<see cref="AppSettings.UseLastEncryptionFileType"/>）が
    /// OFF なら何もしない（設定画面で選んだ種類を常に使う。コンボボックスの選択はその場限り）。
    /// コマンドライン起動時は複製を書き換えるだけで、settings.json には保存しない
    /// （引数 /entype が優先されるべきで、GUI の選択で上書きしないため）。
    /// </summary>
    public void RememberEncryptionFileType(int fileType)
    {
        if (!Settings.UseLastEncryptionFileType) return;

        Settings.EncryptionFileType = fileType;

        if (IsCommandLineRun) return;

        Store.Save(Settings);
    }
}
