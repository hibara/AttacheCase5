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
// ライセンスの表示。
//
// 本体の利用条件と、同梱している第三者ソフトウェアの表示を 1 つの窓にまとめる。
// MIT などは「著作権表示と許諾表示を配布物に含めること」を条件にしているので、
// 利用者がアプリの中だけで読めなければならない（リポジトリは非公開）。
//
// 本文は選択して複製できるようにする。読めるだけで持ち出せないと、
// 監査や社内申請のときに困る。
//----------------------------------------------------------------------

using System;
using AttacheCase.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AttacheCase.Views;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // 所有モーダルは最小化するとタスクバーに出ず行方不明になるため、
        // タイトルバーを閉じる（×）だけにする（Windows のみ）
        Opened += (_, _) => Platform.Windows.WindowsDialogChrome.MakeCloseOnly(this);

        var localize = LocalizationService.Instance;
        Title = localize["License_Title"];

        SetTab("SourceAvailableTab", "SourceAvailableText",
            localize["License_Tab_SourceAvailable"], LicenseDocument.SourceAvailable);
        SetTab("CommercialTab", "CommercialText",
            localize["License_Tab_Commercial"], LicenseDocument.Commercial);
        SetTab("ThirdPartyTab", "ThirdPartyText",
            localize["License_Tab_ThirdParty"], LicenseDocument.ThirdParty);

        var copyButton = this.FindControl<Button>("CopyButton");
        if (copyButton != null) copyButton.Content = localize["License_Copy"];

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null) closeButton.Content = localize["About_Close"];
    }

    private void SetTab(string tabName, string textName, string header, LicenseDocument document)
    {
        var tab = this.FindControl<TabItem>(tabName);
        if (tab != null) tab.Header = header;

        var text = this.FindControl<SelectableTextBlock>(textName);
        if (text == null) return;

        try
        {
            text.Text = LicenseDocuments.LoadText(document);
        }
        catch (Exception ex)
        {
            // 読めなくても窓は出す。何が起きたかは見えるようにしておく
            text.Text = ex.Message;
        }
    }

    /// <summary>いま見ている文書を、元の Markdown のまま複製する。</summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        if (clipboard == null) return;

        var tabs = this.FindControl<TabControl>("DocumentTabs");
        var document = tabs?.SelectedIndex switch
        {
            1 => LicenseDocument.Commercial,
            2 => LicenseDocument.ThirdParty,
            _ => LicenseDocument.SourceAvailable,
        };

        try
        {
            // 整形前の .md を渡す。社内申請や監査に貼るなら、元の形のほうが扱いやすい
            await clipboard.SetTextAsync(LicenseDocuments.LoadMarkdown(document));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"License copy failed: {ex.Message}");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
