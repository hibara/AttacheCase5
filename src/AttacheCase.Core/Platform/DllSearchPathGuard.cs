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
// DLL プリロード攻撃（DLL ハイジャック）対策。Windows 専用、他 OS では何もしない。
//
// 過去に JPCERT/CC から何度も指摘を受けている脆弱性クラス。典型的な経路は
// 「.atc と同じフォルダーに細工した DLL を置いて、.atc をダブルクリック
// させる」（関連付け起動のカレントディレクトリがそのフォルダーになる）と、
// 「ZIP 版の exe をダウンロードフォルダーなど、細工した DLL が置かれうる
// フォルダーで実行させる」の 2 つ。
//
// .NET (Core) では DLL が読まれる経路が 2 系統あり、対策もそれぞれ要る:
//   A. ネイティブコードの LoadLibrary(名前だけ)・遅延ロード・システム DLL の
//      依存解決 …… Windows の検索順序（exe のフォルダーが最初）で探す。
//      → SetDllDirectory("") + SetDefaultDllDirectories(SYSTEM32 | USER_DIRS)
//        で exe のフォルダー・カレントディレクトリ・PATH を検索から外す。
//   B. .NET ランタイムの P/Invoke 解決（[DllImport]）…… ランタイムがホストの
//      探索フォルダー（単一ファイル発行なら exe のフォルダーと %TEMP% の展開先）
//      を自分で見て【フルパス】で LoadLibraryEx する。検索が起きないので A の
//      設定は効かない（DefaultDllImportSearchPaths(System32) を付けても、この
//      探索のほうが先に行われる。実測）。
//      → 全アセンブリに DllImportResolver を登録し、P/Invoke 対象は System32
//        だけから読む。同梱ネイティブ（Skia / ANGLE / HarfBuzz）は展開先の
//        フルパスで読み、その依存も System32 に限定する。
//   C. ランタイム本体（System.Private.CoreLib）の P/Invoke（bcrypt.dll、ntdll.dll、
//      kernel32.dll、advapi32.dll …）にはリゾルバーが呼ばれず、コードでは防げない。
//      bcrypt.dll は RandomNumberGenerator の土台なので、乗っ取られると鍵が壊れる。
//      → 単一ファイル発行で動いているとき、起動時に exe と同じフォルダーを調べ、
//        DLL が 1 つでもあれば警告して起動しない（FindForeignDlls）。
//        列挙は自前の FindFirstFileW で行い、検査自体が偽 ntdll.dll を踏まない
//        ようにしてある。kernel32 / advapi32 だけはこの検査より前にランタイムが
//        読むので防げない（exe の隣に書ける攻撃者は exe 自体も差し替えられる
//        ので、そこは配置場所で守る。インストーラ版は Program Files）。
//
// 呼び出し方（GUI / CLI の Program.Main の【最初】。他のライブラリを呼ぶ前、
// 設定やリソースを読む前）:
//   if (!DllSearchPathGuard.Install()) → 検索順序を限定できなかった。GUI は
//       ShowInstallFailureDialog、CLI は BuildInstallFailureMessage を標準エラーへ
//       出して AtcExitCode.GeneralError で終わる（黙って続けない = fail closed）
//   var dlls = DllSearchPathGuard.FindForeignDlls();   // 空なら続行
//   ... 空でなければ GUI は ShowRefusalDialog、CLI は BuildRefusalMessage を
//       標準エラーへ出して AtcExitCode.GeneralError で終わる
//
// 罠（RandomPasswordGenerator で実測、_docs/dll-hijack-hardening/README.md）:
//   - 文字列ハッシュ（Marvin）の初期化が bcrypt.dll を読む。string.GetHashCode /
//     StringComparer.OrdinalIgnoreCase のハッシュ / Uri などを起動時チェックより
//     前に使ってはいけない。このファイルの Install / FindForeignDlls はそれらを
//     避けて書いてある（Dictionary<string,…> は非ランダム化ハッシュなので可）。
//   - リゾルバーの中で使う LoadLibraryExW を、リゾルバー登録より前に一度呼んで
//     束縛しておかないと、初回束縛でリゾルバーが再入して無限再帰になる。
//   - SetProcessMitigationPolicy(PreferSystem32Images) は実行中のプロセスからは
//     効かない（プロセス生成時に親が付けるもの）。使わない。
//   - 単一ファイル発行の判定は Assembly.GetEntryAssembly().Location が空か
//     どうか。開発時のビルドやテストホストでは空にならないので、起動時
//     チェックは自動的に無効になる（出力フォルダーに DLL が並ぶため）。
//     ※ invariants の「Location 禁止」は「パスを得る目的で使うな」の意味で、
//       ここは「空であること」を判定に使う唯一の例外。
//   - SetDllDirectory(null) は「既定に戻す」= カレントディレクトリを含める、
//     という逆の意味になる。必ず空文字を渡す。
//
// 旧 Windows 版 5.0 は SetDllDirectory("") + SetDefaultDllDirectories(SYSTEM32)
// だけだった（.NET Framework は名前検索なのでそれで足りる）。SFX スタブ
// （ExeOutShared）と AtcSetup は net48 のままなので従来どおり。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AttacheCase.Platform;

/// <summary>
/// Windows での DLL ハイジャック対策。P/Invoke の解決先と DLL の検索順序を
/// System32 に限定し、単一ファイル発行では exe の隣に DLL があれば起動を断る。
/// プロセスの最初（Main の 1 行目）で <see cref="Install"/> を呼び、続けて
/// <see cref="FindForeignDlls"/> の結果で起動可否を決める。
/// </summary>
public static class DllSearchPathGuard
{
    /// <summary>警告ダイアログのタイトル（リソースを介さず固定文字列にする）</summary>
    private const string ProductName = "AttacheCase";

    /// <summary>
    /// 起動を断ったことを表す固有の識別子。拒否メッセージ（ダイアログ・標準エラー）の末尾に必ず入れ、
    /// 検証ハーネス（dogfooding/DllHijackCheck）がこの文字列で「確かに拒否した」と判定できるようにする。
    /// 「DLL」などの一般語や終了コードだけだと、無関係な起動エラーを拒否と誤認しうるため。
    /// スクリプト側の既定 -RefusalMarker とこの値を一致させること。
    /// </summary>
    public const string RefusalMarker = "AtcDllGuardRefusal";

    private static readonly object Gate = new();
    private static string[] _bundledNativeDirectories = [];
    private static bool _resolverRegistered;

    /// <summary>
    /// CoreLib が P/Invoke で（リゾルバーを通さず）読む暗号系システム DLL。<see cref="Install"/> で
    /// System32 の本物を先に読み込んでプロセスに固定し、起動時チェック後に同名 DLL を置かれても
    /// 拾われないようにする。bcrypt = RandomNumberGenerator / AES-GCM(CNG)、ncrypt = CNG 鍵、
    /// crypt32 / advapi32 = DPAPI（ProtectedData）。
    /// </summary>
    private static readonly string[] CoreLibSecurityDlls =
        ["bcrypt.dll", "ncrypt.dll", "crypt32.dll", "advapi32.dll"];

    /// <summary>
    /// <see cref="Install"/> が【成功して】完了しているか（テスト・診断用）。
    /// 検索順序の限定に失敗したときは true にならない（呼び直せば再試行する）。
    /// </summary>
    public static bool IsInstalled { get; private set; }

    /// <summary>SetDefaultDllDirectories の設定に成功したか（テスト・診断用）</summary>
    public static bool DefaultDllDirectoriesRestricted { get; private set; }

    /// <summary>
    /// 単一ファイル発行（PublishSingleFile）として動いているか。
    /// バンドルされたアセンブリは Location が空になる。
    /// </summary>
    public static bool IsSingleFileBundle
    {
        get
        {
#pragma warning disable IL3000 // 単一ファイルでは Location が空になることを利用して判定している
            return string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
#pragma warning restore IL3000
        }
    }

    /// <summary>
    /// 対策 A・B を有効にする。Main の先頭、他のライブラリを呼ぶ前に呼ぶ。
    /// 複数回呼んでも安全。Windows 以外では何もせず true を返す。
    /// Windows で検索順序の設定（SetDllDirectory / SetDefaultDllDirectories）の
    /// どちらかが失敗したときは false を返す。**呼び出し側は起動を中止すること**
    /// （黙って続けると検索順序が既定のまま＝対策前と同じ状態で動くことになる。
    /// 両 API は Windows 8 以降に必ずあり、対応 OS は Windows 10 以降なので、
    /// 失敗は事実上起きない）。
    /// </summary>
    public static bool Install()
    {
        if (!OperatingSystem.IsWindows()) return true;
        return InstallOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool InstallOnWindows()
    {
        lock (Gate)
        {
            if (IsInstalled) return true;

            // リゾルバーの登録は 1 回だけ（失敗後の呼び直しで AssemblyLoad の購読が重複しないように）
            if (!_resolverRegistered)
            {
                _bundledNativeDirectories = GetBundledNativeDirectories();

                // リゾルバーが使う LoadLibraryExW（kernel32）を先に束縛しておく。
                // リゾルバーの中から初めて呼ぶと、束縛のためにリゾルバーが再入して
                // 無限再帰になる
                NativeMethods.LoadLibraryExW("kernel32.dll", IntPtr.Zero, NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);

                // CoreLib（System.Private.CoreLib）の P/Invoke はリゾルバーを通らず、
                // SetDefaultDllDirectories でも縛れない。とくに RandomNumberGenerator は
                // [LibraryImport("bcrypt.dll")]（System32 未指定）で bcrypt を「遅延」で読むため、
                // 起動時チェックを通ったあとに exe の隣へ bcrypt.dll を置かれ、その後はじめて乱数を
                // 引くと偽物を拾いうる（鍵・ノイズ生成が乗っ取られる）。そこで暗号系のシステム DLL を
                // ここで System32 の本物として先に読み込み、プロセスに固定する。以後は名前一致で
                // この既読モジュールが再利用されるので、あとから置かれた同名 DLL は読まれない。
                // （FreeLibrary しないので固定されたまま。DPAPI 経路の crypt32/ncrypt/advapi32 も同様に固定）
                // 固定に失敗した DLL は System32 に固定されない = 穴が残るので、fail closed で起動を断る。
                // _resolverRegistered をまだ立てないので、呼び直せば最初からやり直す（Windows 10+ の System32 に
                // これらは必ずあるので、失敗は事実上起きない）
                foreach (var systemDll in CoreLibSecurityDlls)
                {
                    var handle = NativeMethods.LoadLibraryExW(systemDll, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (handle == IntPtr.Zero)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Failed to preload {systemDll} from System32: {Marshal.GetLastWin32Error()}");
                        DefaultDllDirectoriesRestricted = false;
                        IsInstalled = false;
                        return false;
                    }
                }

                // 自アセンブリも含めて全アセンブリに登録する（自アセンブリの P/Invoke も、
                // 属性だけでは exe のフォルダーを先に探されるため）。以後に読み込まれる
                // アセンブリにも AssemblyLoad で登録する
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) => RegisterResolver(e.LoadedAssembly);
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    RegisterResolver(assembly);
                }

                _resolverRegistered = true;
            }

            // 2 本の API は独立に呼ぶ（片方の失敗でもう片方を飛ばすと対策が外れる）。
            // GetLastWin32Error は各呼び出しの直後に取る（次の P/Invoke で上書きされる）
            var isDllDirectorySet = NativeMethods.SetDllDirectoryW("");
            var dllDirectoryError = Marshal.GetLastWin32Error();
            var isDefaultDirectoriesSet = NativeMethods.SetDefaultDllDirectories(
                NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32 | NativeMethods.LOAD_LIBRARY_SEARCH_USER_DIRS);
            var defaultDirectoriesError = Marshal.GetLastWin32Error();

            if (!isDllDirectorySet)
            {
                System.Diagnostics.Debug.WriteLine("SetDllDirectory(\"\") failed: " + dllDirectoryError);
            }

            if (!isDefaultDirectoriesSet)
            {
                System.Diagnostics.Debug.WriteLine("SetDefaultDllDirectories failed: " + defaultDirectoriesError);
            }

            // 両方成功したときだけ「完了」。失敗なら呼び出し側が起動を中止する
            DefaultDllDirectoriesRestricted = isDllDirectorySet && isDefaultDirectoriesSet;
            IsInstalled = DefaultDllDirectoriesRestricted;
            return IsInstalled;
        }
    }

    /// <summary>「安全と断定できない」ことを表す番目印。フォルダーを列挙できなかったときに
    /// この 1 件を返し、呼び出し側の Count &gt; 0 判定で起動を断らせる（fail closed）。</summary>
    internal const string EnumerationFailedMarker = "(DLL の一覧を取得できませんでした)";

    /// <summary>
    /// 対策 C。単一ファイル発行で動いているとき、exe と同じフォルダー（直下のみ）に
    /// ある .dll のファイル名を返す。空なら起動してよい。開発時のビルド（出力
    /// フォルダーに DLL が並ぶ）や Windows 以外では常に空。列挙自体に失敗したときは
    /// <see cref="EnumerationFailedMarker"/> を 1 件返し、呼び出し側に起動を断らせる（fail closed）。
    /// <para>
    /// 列挙には CoreLib の Directory.EnumerateFiles（内部で ntdll.dll を P/Invoke
    /// する）ではなく自前の FindFirstFileW を使い、この検査自体が偽 DLL を
    /// 読み込まないようにしている。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> FindForeignDlls()
    {
        if (!OperatingSystem.IsWindows() || !IsSingleFileBundle)
        {
            return [];
        }

        // 列挙に失敗したら「DLL が無い」ではなく「安全と断定できない」として扱い、起動を断る。
        // FindFirstFileW はアクセス拒否などでも INVALID_HANDLE_VALUE を返すので、成功と区別する
        if (!TryFindDlls(AppContext.BaseDirectory, out var dlls))
        {
            return [EnumerationFailedMarker];
        }

        return dlls;
    }

    /// <summary>
    /// 起動を断るときの利用者向けメッセージ。表示言語の設定はまだ読めない段階
    /// （設定ファイルの読み込みは JSON と文字列ハッシュを使う）なので、OS の
    /// UI 言語が日本語なら日本語、それ以外は英語にする。
    /// </summary>
    public static string BuildRefusalMessage(IReadOnlyList<string> dlls)
    {
        var body = string.Format(
            CultureInfo.InvariantCulture,
            IsJapaneseUi() ? MessageJa : MessageEn,
            AppContext.BaseDirectory,
            string.Join(Environment.NewLine, dlls));
        return body + Environment.NewLine + Environment.NewLine + RefusalMarker;
    }

    /// <summary>
    /// <see cref="Install"/> が false を返したとき（検索順序を限定できなかった）に
    /// 起動を断る利用者向けメッセージ。
    /// </summary>
    public static string BuildInstallFailureMessage()
    {
        var body = IsJapaneseUi() ? InstallFailureJa : InstallFailureEn;
        return body + Environment.NewLine + Environment.NewLine + RefusalMarker;
    }

    /// <summary>
    /// GUI 用: 起動を断る警告ダイアログを出す（Avalonia を初期化する前なので
    /// Win32 の MessageBox を直接使う）。Windows 以外では何もしない。
    /// </summary>
    public static void ShowRefusalDialog(IReadOnlyList<string> dlls)
    {
        if (!OperatingSystem.IsWindows()) return;
        var message = BuildRefusalMessage(dlls);
        EchoToStandardError(message);
        ShowDialogOnWindows(message);
    }

    /// <summary>
    /// GUI 用: 検索順序を限定できなかったときの警告ダイアログ。Windows 以外では何もしない。
    /// </summary>
    public static void ShowInstallFailureDialog()
    {
        if (!OperatingSystem.IsWindows()) return;
        var message = BuildInstallFailureMessage();
        EchoToStandardError(message);
        ShowDialogOnWindows(message);
    }

    // GUI（ウィンドウ）アプリでも、標準エラーがつながっているとき（スクリプト・CI からの起動）は
    // 拒否理由を書き出しておく。通常のダブルクリック起動ではコンソールが無く、書き込みは無害に捨てられる
    private static void EchoToStandardError(string message)
    {
        try { Console.Error.WriteLine(message); }
        catch { /* コンソールが無い・閉じている場合は無視 */ }
    }

    [SupportedOSPlatform("windows")]
    private static void ShowDialogOnWindows(string message)
    {
        NativeMethods.MessageBoxW(
            IntPtr.Zero,
            message,
            ProductName,
            NativeMethods.MB_OK | NativeMethods.MB_ICONERROR | NativeMethods.MB_SETFOREGROUND);
    }

    // 起動時チェックの前に文字列ハッシュを使わないよう、辞書やリソースマネージャーを
    // 介さず定数で持つ（Strings.resx へは移さない）
    private const string MessageJa =
        "プログラムと同じフォルダーに DLL ファイルがあるため、起動を中止しました。\n\n" +
        "フォルダー: {0}\n\n{1}\n\n" +
        "アタッシェケースは実行ファイルの隣に DLL ファイルを必要としません。想定外の DLL は、" +
        "Windows のシステムファイルの代わりに読み込まれるおそれがあります（DLL ハイジャック）。\n" +
        "AttacheCase.exe と AttacheCase.Cli.exe を DLL のない専用フォルダーに移してから、もう一度起動してください。";

    private const string MessageEn =
        "DLL files were found in the same folder as the program, so it did not start.\n\n" +
        "Folder: {0}\n\n{1}\n\n" +
        "AttacheCase needs no DLL files next to its executables. An unexpected DLL there could be loaded " +
        "in place of a Windows system file (DLL hijacking).\n" +
        "Move AttacheCase.exe and AttacheCase.Cli.exe to their own folder that contains no DLL files, then start again.";

    private const string InstallFailureJa =
        "DLL の読み込み先を Windows のシステムフォルダーに限定する保護（DLL ハイジャック対策）を" +
        "有効にできなかったため、起動を中止しました。\n\n" +
        "アタッシェケースは Windows 10 以降で動作します。もう一度起動しても同じ場合は、開発元へお知らせください。";

    private const string InstallFailureEn =
        "AttacheCase could not enable the protection that restricts DLL loading to the Windows system folder " +
        "(DLL hijacking protection), so it did not start.\n\n" +
        "AttacheCase requires Windows 10 or later. If this happens again, please report it to the developer.";

    private static bool IsJapaneseUi() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// フォルダー直下（サブフォルダーは含まない）の .dll ファイル名を列挙する。
    /// 列挙に成功したときだけ true を返し、<paramref name="dlls"/> に結果を入れる。
    /// FindFirstFileW / FindNextFileW がアクセス拒否などで失敗したときは false
    /// （呼び出し側は「安全と断定できない」として扱う）。フォルダーが実在して空
    /// （ERROR_FILE_NOT_FOUND）のときは true + 空。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static bool TryFindDlls(string directory, out IReadOnlyList<string> dlls)
    {
        const int ErrorFileNotFound = 2;   // ERROR_FILE_NOT_FOUND（一致するものが無い）
        const int ErrorNoMoreFiles = 18;   // ERROR_NO_MORE_FILES（列挙が正常に終わった）

        var found = new List<string>();
        dlls = found;

        var handle = NativeMethods.FindFirstFileW(Path.Combine(directory, "*"), out var data);
        if (handle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            // 「一致するものが無い」だけは成功（空）扱い。それ以外（アクセス拒否・パス無し等）は失敗
            return Marshal.GetLastWin32Error() == ErrorFileNotFound;
        }

        try
        {
            do
            {
                if ((data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) == 0
                    && data.cFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(data.cFileName);
                }
            }
            while (NativeMethods.FindNextFileW(handle, out data));

            // FindNextFileW が false で抜けたとき、正常終了（NO_MORE_FILES）でなければ列挙は途中で失敗している
            if (Marshal.GetLastWin32Error() != ErrorNoMoreFiles)
            {
                return false;
            }
        }
        finally
        {
            NativeMethods.FindClose(handle);
        }

        // 比較（ハッシュではない）なので OrdinalIgnoreCase でよい
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>フォルダー直下の .dll ファイル名を返す（列挙失敗時は空）。診断・テスト用。</summary>
    [SupportedOSPlatform("windows")]
    internal static IReadOnlyList<string> FindDlls(string directory)
        => TryFindDlls(directory, out var dlls) ? dlls : [];

    [SupportedOSPlatform("windows")]
    private static void RegisterResolver(Assembly assembly)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // そのライブラリ自身がリゾルバーを登録済み。ライブラリの判断に任せる
        }
        catch (ArgumentException)
        {
            // 動的アセンブリなど、リゾルバーを登録できないもの
        }
    }

    /// <summary>
    /// P/Invoke の DLL 解決。System32 にある DLL はそこから、アプリ同梱のネイティブ
    /// DLL は展開先から読み込む。パス（フォルダー区切り・ルート）付きで指定された名前は
    /// 呼び出し元の意図どおり <see cref="IntPtr.Zero"/> を返してランタイム既定の解決に委ねる。
    /// パス無しの名前がどちらにも無いときは、既定解決へ戻さず
    /// <see cref="DllNotFoundException"/> を投げて拒否する（既定解決は単一ファイル発行で
    /// exe のフォルダーも探すため、そこへ落とさない）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // パス付きで指定されたものは呼び出し元の意図どおりにする
        if (libraryName.AsSpan().IndexOfAny('\\', '/') >= 0 || Path.IsPathRooted(libraryName))
        {
            return IntPtr.Zero;
        }

        // 1) System32 のみ（拡張子がなければローダーが .dll を補う）
        var handle = NativeMethods.LoadLibraryExW(libraryName, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        // 2) アプリ同梱のネイティブ DLL をフルパスで読み込む。その依存 DLL（libSkiaSharp の
        //    FONTSUB.dll、av_libglesv2 の dxgi.dll など）は System32 だけから解決する。
        //    同梱 DLL 同士の依存が必要になったときのために「その DLL のフォルダー + System32」
        //    でもう一度だけ試す
        var fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? libraryName : libraryName + ".dll";
        foreach (var directory in _bundledNativeDirectories)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            handle = NativeMethods.LoadLibraryExW(path, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (handle == IntPtr.Zero)
            {
                handle = NativeMethods.LoadLibraryExW(
                    path,
                    IntPtr.Zero,
                    NativeMethods.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);
            }

            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        // ここまで来た = パス指定の無い名前が System32 にも同梱先にも無い。
        // Zero を返すとランタイム既定の解決に落ちるが、.NET 10 の単一ファイル発行では
        // 既定解決が実行ファイルのフォルダー（AssemblyDirectory）も探すため、起動時チェックを
        // 通ったあとに exe の隣へ置かれた DLL を拾いうる（SetDefaultDllDirectories はこの
        // ランタイム経路を縛れない）。既定解決へは戻さず、見つからなかったものとして拒否する
        throw new DllNotFoundException(
            $"Native library '{libraryName}' was not found in System32 or the bundled native directory.");
    }

    /// <summary>
    /// ランタイムがネイティブ DLL を探すフォルダー。単一ファイル発行では %TEMP% の展開先
    /// （と exe のフォルダー）、通常ビルドでは出力フォルダー（runtimes/win-x64/native など）が
    /// ホストから渡される。AppContext.GetData の Dictionary は非ランダム化ハッシュなので
    /// bcrypt.dll を読まない。
    /// </summary>
    private static string[] GetBundledNativeDirectories()
    {
        var directories = new List<string>();

        // 単一ファイル発行では、ホストが NATIVE_DLL_SEARCH_DIRECTORIES に exe のフォルダーと
        // %TEMP% の展開先の両方を渡す。exe のフォルダーを残すと、起動時チェック（FindForeignDlls）を
        // 通ったあとに exe の隣へ DLL を置かれた場合、後続の P/Invoke が Resolve() の「同梱先をフルパスで
        // 読む」経路で拾ってしまい、起動時チェックを迂回される。そこで単一ファイル発行では exe の
        // フォルダーだけを除外する（同梱ネイティブは展開先から読むので影響しない）
        var baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));

        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string configured)
        {
            foreach (var directory in configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Path.IsPathRooted(directory))
                {
                    continue;
                }

                var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
                if (IsSingleFileBundle && string.Equals(normalized, baseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                directories.Add(normalized);
            }
        }

        // 展開先が渡されない環境（通常ビルド）だけ、出力フォルダーを既定にする。
        // 単一ファイル発行では exe のフォルダーを既定に足さない（上と同じ迂回を避ける）
        if (directories.Count == 0 && !IsSingleFileBundle)
        {
            directories.Add(baseDirectory);
        }

        return [.. directories];
    }

    // kernel32 / user32 は「既知の DLL」なので、この P/Invoke 自体がハイジャックされる
    // ことはない。念のため検索先も System32 に固定しておく
    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        public const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
        public const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
        public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        public const uint MB_OK = 0x00000000;
        public const uint MB_ICONERROR = 0x00000010;
        public const uint MB_SETFOREGROUND = 0x00010000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectoryW(string lpPathName);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(IntPtr hFindFile);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
    }
}
