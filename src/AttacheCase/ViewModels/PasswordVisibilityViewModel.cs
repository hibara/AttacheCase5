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
// パスワード入力欄の内側（右端）に置く目玉アイコン（表示／伏せ字の切り替え）。
// アイコンは現在の状態を表す（表示中は開いた目、伏せ字中は閉じた目）。
// 画像の出し分けはビュー側で IsVisible により行う。
//
// 初期状態は設定「パスワードを隠さずに入力する」に従う。切り替えると
// Windows 版と同じくその状態を設定として覚えるが、コマンドライン起動時の
// 値は保存しない（その実行にだけ効く上書きのため）。
//----------------------------------------------------------------------

using System.Windows.Input;
using AttacheCase.Models;
using AttacheCase.Services;
using CommunityToolkit.Mvvm.Input;

namespace AttacheCase.ViewModels;

public class PasswordVisibilityViewModel : ViewModelBase
{
    private readonly RunContext _context;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    private bool _isVisible;

    public PasswordVisibilityViewModel(RunContext context)
    {
        _context = context;
        _isVisible = context.Settings.NotMaskPassword;

        ToggleCommand = new RelayCommand(Toggle);
    }

    public ICommand ToggleCommand { get; }

    // 目玉アイコンの SVG パス。SVG は中に色（濃いグレー/黒）を持っていてテーマでは
    // 変わらないため、ダークでは明色化した複製（Assets/Icons/dark/）を使う。
    // テーマ変更時はウィンドウごと作り直されるので、生成時の解決で足りる
    // （サイドバーアイコンの ThemedIconPath と同じ方針）。
    private static string ThemedIcon(string name)
    {
        var dark = Avalonia.Application.Current?.ActualThemeVariant
                   == Avalonia.Styling.ThemeVariant.Dark;
        return $"avares://AttacheCase/Assets/Icons/{(dark ? "dark/" : "")}{name}.svg";
    }

    /// <summary>開いた目（表示中）のアイコン。テーマに追従。</summary>
    public string EyeIconUri => ThemedIcon("eye");

    /// <summary>閉じた目（伏せ字中）のアイコン。テーマに追従。</summary>
    public string EyeBlindIconUri => ThemedIcon("eye-blind");

    /// <summary>パスワードを伏せ字にせず表示しているか</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (!SetProperty(ref _isVisible, value)) return;

            OnPropertyChanged(nameof(PasswordChar));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    /// <summary>入力欄の伏せ字。表示中は伏せ字なし（'\0'）にする</summary>
    public char PasswordChar => IsVisible ? '\0' : '●';

    public string ToolTip => _localize[IsVisible ? "Password_Hide" : "Password_Show"];

    private void Toggle()
    {
        IsVisible = !IsVisible;
        _context.RememberPasswordVisibility(IsVisible);
    }
}
