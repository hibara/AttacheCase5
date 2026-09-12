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

using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace AttacheCase.Services;

/// <summary>
/// ローカライズサービス - アプリケーション全体で文字列リソースを管理
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance =
        new(() => new LocalizationService());

    public static LocalizationService Instance => _instance.Value;

    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationService()
    {
        _resourceManager = new ResourceManager(
            "AttacheCase.Resources.Strings",
            typeof(LocalizationService).Assembly);

        // システム言語が日本語かどうかで初期言語を決定
        var systemCulture = CultureInfo.CurrentUICulture;
        _currentCulture = systemCulture.TwoLetterISOLanguageName == "ja"
            ? new CultureInfo("ja")
            : new CultureInfo("en");
    }

    /// <summary>
    /// 現在のカルチャ
    /// </summary>
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Name != value.Name)
            {
                _currentCulture = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
        }
    }

    /// <summary>
    /// 日本語かどうか
    /// </summary>
    public bool IsJapanese => _currentCulture.TwoLetterISOLanguageName == "ja";

    /// <summary>
    /// 指定されたキーのローカライズ文字列を取得
    /// </summary>
    public string GetString(string key)
    {
        try
        {
            return _resourceManager.GetString(key, _currentCulture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// フォーマット付きローカライズ文字列を取得
    /// </summary>
    public string GetString(string key, params object[] args)
    {
        try
        {
            var format = _resourceManager.GetString(key, _currentCulture) ?? key;
            return string.Format(format, args);
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// インデクサーでアクセス可能
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// 言語を切り替え
    /// </summary>
    public void SetLanguage(string cultureCode)
    {
        CurrentCulture = new CultureInfo(cultureCode);
    }

    /// <summary>
    /// 設定「表示言語」を解決する。空文字ならシステムの言語に従う。
    /// </summary>
    public static string ResolveLanguage(string? setting)
    {
        if (setting is "ja" or "en") return setting;

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
    }

    /// <summary>
    /// 設定「表示言語」を適用する。切り替わったら true。
    /// </summary>
    public bool ApplyLanguageSetting(string? setting)
    {
        var language = ResolveLanguage(setting);
        if (language == CurrentCulture.TwoLetterISOLanguageName) return false;

        SetLanguage(language);
        return true;
    }
}
