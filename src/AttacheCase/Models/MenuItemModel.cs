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

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AttacheCase.Models;

/// <summary>
/// メニュー項目の種類
/// </summary>
public enum MenuItemType
{
  /// <summary>
  /// 暗号化(ATC)
  /// </summary>
  Encrypt,

  /// <summary>
  /// 復号する(ATC)
  /// </summary>
  Decrypt,

  /// <summary>
  /// Post-PPAP（鍵サーバ経由の公開鍵暗号）
  /// </summary>
  PostPpap,

  /// <summary>
  /// RSA 公開鍵暗号（XML 鍵ファイル方式・AC4 後方互換）
  /// </summary>
  PublicKey,

  /// <summary>
  /// パスワード ZIP
  /// </summary>
  PasswordZip
}

/// <summary>
/// サイドメニューの項目
/// </summary>
public class MenuItemModel : INotifyPropertyChanged
{
  private bool _isSelected;

  public MenuItemType Type { get; set; }
  public string Title { get; set; } = string.Empty;
  public string IconPath { get; set; } = string.Empty;

  public bool IsSelected
  {
    get => _isSelected;
    set
    {
      if (_isSelected != value)
      {
        _isSelected = value;
        OnPropertyChanged();
      }
    }
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}