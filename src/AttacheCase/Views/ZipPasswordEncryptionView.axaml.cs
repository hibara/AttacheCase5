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

using AttacheCase.ViewModels;
using Avalonia.Controls;

namespace AttacheCase.Views;

public partial class ZipPasswordEncryptionView : UserControl
{
  public ZipPasswordEncryptionView()
  {
    InitializeComponent();

    // ドロップ直後や、確認ステップに進んだ直後に入力欄へフォーカスする
    FocusHelper.TrackFocus(this, () => DataContext switch
    {
      ZipPasswordEncryptionViewModel { IsPasswordInputStep: true } => PasswordBox,
      ZipPasswordEncryptionViewModel { IsPasswordConfirmStep: true } => ConfirmPasswordBox,
      _ => null
    });
  }
}
