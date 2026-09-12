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
// 更新があることを知らせる画面。
//
// 変更点は HTML ではなく構造化データから組み立てる。ここは更新経路のうち唯一
// 「外から来た内容」を描く場所なので、HTML レンダラを介在させない。
// 表示するのは見出しの種類（new / improvements / fixes）と項目の文字列だけで、
// 書式や色は配信側ではなくこちらが決める。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AttacheCase.Views;

/// <summary>利用者が選んだこと。</summary>
public enum UpdateChoice
{
    /// <summary>いま更新する。</summary>
    Install,

    /// <summary>あとで通知する。次の確認でまた知らせる。</summary>
    Later,

    /// <summary>この版は飛ばす。より新しい版が出れば知らせる。</summary>
    Skip,
}

public partial class UpdateAvailableWindow : Window
{
    /// <summary>
    /// 見出しの色。配信側の指定は受け取らない（外から見た目を操作させない）。
    /// </summary>
    private static readonly Dictionary<string, Color> SectionColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["new"] = Color.Parse("#F0A020"),
        ["improvements"] = Color.Parse("#3B82F6"),
        ["fixes"] = Color.Parse("#22A06B"),
    };

    private static readonly Color UnknownSectionColor = Color.Parse("#8E8E93");

    private UpdateChoice _choice = UpdateChoice.Later;

    public UpdateAvailableWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 画面を出して、利用者が選ぶまで待つ。
    /// </summary>
    public static async Task<UpdateChoice> ShowAsync(
        Window? owner, UpdateManifest manifest, Version currentVersion)
    {
        var window = new UpdateAvailableWindow();
        window.Populate(manifest, currentVersion);

        if (owner is null)
        {
            // 親が取れないときは単独で出す。出ないよりはよい
            window.Show();
            var completion = new TaskCompletionSource<UpdateChoice>();
            window.Closed += (_, _) => completion.TrySetResult(window._choice);
            return await completion.Task.ConfigureAwait(true);
        }

        await window.ShowDialog(owner).ConfigureAwait(true);
        return window._choice;
    }

    private void Populate(UpdateManifest manifest, Version currentVersion)
    {
        var localize = LocalizationService.Instance;
        var appName = localize["AppName"];

        Title = localize["Update_Title"];

        SetText("HeadlineText", localize.GetString("Update_Headline", appName));
        SetText("SubText", localize.GetString(
            "Update_SubText", appName, manifest.Version, currentVersion.ToString()));

        SetButton("SkipButton", localize["Update_SkipVersion"]);
        SetButton("LaterButton", localize["Update_RemindLater"]);
        SetButton("InstallButton", localize["Update_Install"]);

        var checkBox = this.FindControl<CheckBox>("CheckOnStartupBox");
        if (checkBox != null)
        {
            checkBox.Content = localize["Settings_CheckForUpdatesOnStartup"];
            checkBox.IsChecked = SettingsService.Instance.Current.CheckForUpdatesOnStartup;
            checkBox.IsCheckedChanged += OnCheckOnStartupChanged;
        }

        BuildNotes(manifest, localize);
    }

    /// <summary>
    /// 変更点を組み立てる。節が無い版もありうるので、その場合は一言だけ出す。
    /// </summary>
    private void BuildNotes(UpdateManifest manifest, LocalizationService localize)
    {
        var panel = this.FindControl<StackPanel>("NotesPanel");
        if (panel is null) return;

        panel.Children.Add(new TextBlock
        {
            Text = localize.GetString("Update_NotesHeading", manifest.Version),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        });

        var sections = manifest.NotesFor(
            LocalizationService.ResolveLanguage(SettingsService.Instance.Current.Language));
        if (sections.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localize["Update_NoNotes"],
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var section in sections)
        {
            if (section.Items.Count == 0) continue;

            panel.Children.Add(BuildSection(section, localize));
        }
    }

    private static Control BuildSection(ReleaseNoteSection section, LocalizationService localize)
    {
        var color = SectionColors.TryGetValue(section.Kind, out var known)
            ? known
            : UnknownSectionColor;

        var container = new StackPanel { Spacing = 6 };

        // 見出しの札
        container.Children.Add(new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new Avalonia.CornerRadius(10),
            Padding = new Avalonia.Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = LabelFor(section.Kind, localize),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
            },
        });

        var items = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(6, 2, 0, 0) };
        foreach (var item in section.Items)
        {
            // 横 StackPanel は子に無限幅を渡すので、中の TextBlock が折り返さない。
            // 幅の決まる Grid で受ける（窓を広げれば、そのぶん文も広がる）
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            };
            var bullet = new TextBlock
            {
                Text = "・",
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(bullet, 0);
            row.Children.Add(bullet);

            var text = new TextBlock
            {
                Text = item,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            items.Children.Add(row);
        }

        container.Children.Add(items);
        return container;
    }

    /// <summary>
    /// 見出しに出す語。訳語はこちらが持つ。知らない種類は、そのまま出す。
    /// </summary>
    internal static string LabelFor(string kind, LocalizationService localize) =>
        kind.ToLowerInvariant() switch
        {
            "new" => localize["Update_Section_New"],
            "improvements" => localize["Update_Section_Improvements"],
            "fixes" => localize["Update_Section_Fixes"],
            _ => kind,
        };

    private void SetText(string name, string text)
    {
        var block = this.FindControl<TextBlock>(name);
        if (block != null) block.Text = text;
    }

    private void SetButton(string name, string text)
    {
        var button = this.FindControl<Button>(name);
        if (button != null) button.Content = text;
    }

    /// <summary>
    /// 「起動時に確認する」の切り替えをその場で保存する。設定画面と同じ項目。
    /// </summary>
    private void OnCheckOnStartupChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box) return;

        var store = SettingsService.Instance;
        if (box.IsChecked == store.Current.CheckForUpdatesOnStartup) return;

        var settings = store.Current.Clone();
        settings.CheckForUpdatesOnStartup = box.IsChecked ?? true;
        store.Save(settings);
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e) => Finish(UpdateChoice.Install);

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Finish(UpdateChoice.Later);

    private void OnSkipClick(object? sender, RoutedEventArgs e) => Finish(UpdateChoice.Skip);

    private void Finish(UpdateChoice choice)
    {
        _choice = choice;
        Close();
    }
}
