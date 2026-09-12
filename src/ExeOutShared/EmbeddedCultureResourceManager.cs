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
// 単一 exe（SFX スタブ）のための、衛星アセンブリを使わないローカライズ。
//
// SFX は暗号化データの先頭に付く 1 個の exe で、ja-JP\Exeout.resources.dll の
// ような衛星アセンブリを並置できない。以前は Resource.Embedder で衛星を
// 埋め込んでいたが、dotnet CLI（SDK スタイル）でのビルドではモジュール
// 初期化子の注入が Costura と衝突し、Main 到達前にクラッシュすることが
// 分かった（2026-09-07 実測）。
//
// そこで日本語リソース（*.ja-JP.resources）をメインアセンブリに直接埋め込み
// （csproj で LogicalName + WithCulture=false を指定）、このクラスが
// 文化名付きのマニフェストリソースを直接読む。見つからない文化は
// 通常のフォールバック（ニュートラル=英語）に落ちる。
//
// リソースは dotnet CLI ビルドでは preserialized 形式になるため、読み取りは
// System.Resources.Extensions の DeserializingResourceReader を使う
//（Costura が exe に埋め込む）。
//----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Resources;

namespace Exeout
{
  /// <summary>
  /// メインアセンブリに埋め込んだ「文化名サフィックス付き .resources」を読む
  /// ComponentResourceManager。WinForms の InitializeComponent から使う。
  /// </summary>
  internal class EmbeddedCultureResourceManager : ComponentResourceManager
  {
    private readonly Dictionary<string, ResourceSet> _cache =
      new Dictionary<string, ResourceSet>(StringComparer.OrdinalIgnoreCase);

    // ResourceManager(Type) の BaseName は「型名だけ」（名前空間を含まない）。
    // マニフェストリソース名は名前空間込みなので、FullName を別に持つ
    private readonly string _baseFullName;

    public EmbeddedCultureResourceManager(Type type) : base(type)
    {
      _baseFullName = type.FullName;
    }

    protected override ResourceSet InternalGetResourceSet(
      CultureInfo culture, bool createIfNotExists, bool tryParents)
    {
      if (culture != null && !culture.Equals(CultureInfo.InvariantCulture))
      {
        lock (_cache)
        {
          if (_cache.TryGetValue(culture.Name, out var cached)) return cached;

          var resourceName = _baseFullName + "." + culture.Name + ".resources";
          var stream = MainAssembly.GetManifestResourceStream(resourceName);
          if (stream != null)
          {
            // dotnet CLI ビルドの .resources は preserialized 形式
            //（アイコン・サイズなどの非文字列を含む）
            var set = new ResourceSet(
              new System.Resources.Extensions.DeserializingResourceReader(stream));
            _cache[culture.Name] = set;
            return set;
          }
        }
      }

      return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
    }

    public override object GetObject(string name, CultureInfo culture)
    {
      // 文化指定なしなら現在の UI 文化（AppLanguage が ja-JP を設定する）
      var effective = culture ?? CultureInfo.CurrentUICulture;

      var set = InternalGetResourceSet(effective, true, true);
      var value = set != null ? set.GetObject(name, true) : null;
      if (value != null) return value;

      // 見つからなければ通常のフォールバック（ニュートラル=英語）
      return base.GetObject(name, culture);
    }

    public override string GetString(string name, CultureInfo culture)
    {
      return GetObject(name, culture) as string;
    }
  }

  /// <summary>
  /// <see cref="EmbeddedCultureResourceManager"/> の文字列リソース版
  /// （Properties.Resources の静的クラスから使う）。
  /// </summary>
  internal class EmbeddedCultureStringResourceManager : ResourceManager
  {
    private readonly Dictionary<string, ResourceSet> _cache =
      new Dictionary<string, ResourceSet>(StringComparer.OrdinalIgnoreCase);

    public EmbeddedCultureStringResourceManager(string baseName, System.Reflection.Assembly assembly)
      : base(baseName, assembly)
    {
    }

    protected override ResourceSet InternalGetResourceSet(
      CultureInfo culture, bool createIfNotExists, bool tryParents)
    {
      if (culture != null && !culture.Equals(CultureInfo.InvariantCulture))
      {
        lock (_cache)
        {
          if (_cache.TryGetValue(culture.Name, out var cached)) return cached;

          var resourceName = BaseName + "." + culture.Name + ".resources";
          var stream = MainAssembly.GetManifestResourceStream(resourceName);
          if (stream != null)
          {
            var set = new ResourceSet(
              new System.Resources.Extensions.DeserializingResourceReader(stream));
            _cache[culture.Name] = set;
            return set;
          }
        }
      }

      return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
    }

    public override object GetObject(string name, CultureInfo culture)
    {
      var effective = culture ?? CultureInfo.CurrentUICulture;

      var set = InternalGetResourceSet(effective, true, true);
      var value = set != null ? set.GetObject(name, true) : null;
      if (value != null) return value;

      return base.GetObject(name, culture);
    }

    public override string GetString(string name, CultureInfo culture)
    {
      return GetObject(name, culture) as string;
    }
  }
}
