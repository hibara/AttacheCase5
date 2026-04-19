//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace AttacheCase
{
  /// <summary>
  /// ライト／ダークモード対応のアイコン管理クラス。
  /// 白シルエットPNGをベースに、テーマと状態に応じた色を
  /// ColorMatrix でリアルタイムに適用してキャッシュします。
  /// </summary>
  public static class ThemeIconManager
  {
    // ─────────────────────────────────────────
    //  列挙体
    // ─────────────────────────────────────────

    public enum Theme
    {
      Light,
      Dark
    }

    public enum IconState
    {
      Normal,
      Hover,
      Active,      // 選択・押下中
      Disabled
    }

    // ─────────────────────────────────────────
    //  テーマ別・状態別カラー定義
    //  必要に応じて色を調整してください
    // ─────────────────────────────────────────

    private static readonly Dictionary<Theme, Dictionary<IconState, Color>> ThemeColors
        = new Dictionary<Theme, Dictionary<IconState, Color>>
    {
      {
        Theme.Light, new Dictionary<IconState, Color>
        {
          { IconState.Normal,   Color.FromArgb(255,  60,  60,  60) },  // ほぼ黒に近いグレー
          { IconState.Hover,    Color.FromArgb(255,   0, 120, 215) },  // Windows アクセントカラー（青）
          { IconState.Active,   Color.FromArgb(255,   0,  90, 170) },  // Active は少し暗め
          { IconState.Disabled, Color.FromArgb(128, 150, 150, 150) },  // 半透明グレー
        }
      },
      {
        Theme.Dark, new Dictionary<IconState, Color>
        {
          { IconState.Normal,   Color.FromArgb(255, 220, 220, 220) },  // 明るいグレー
          { IconState.Hover,    Color.FromArgb(255,  76, 194, 255) },  // 明るいアクセントブルー
          { IconState.Active,   Color.FromArgb(255, 120, 210, 255) },  // さらに明るめ
          { IconState.Disabled, Color.FromArgb(128,  90,  90,  90) },  // 暗い半透明グレー
        }
      }
    };

    // ─────────────────────────────────────────
    //  現在のテーマ（外部から切り替える）
    // ─────────────────────────────────────────

    private static Theme _currentTheme = Theme.Light;

    public static Theme CurrentTheme
    {
      get => _currentTheme;
      set
      {
        if (_currentTheme == value) return;
        _currentTheme = value;
        ClearCache();   // テーマ変更時はキャッシュをクリア
      }
    }

    // ─────────────────────────────────────────
    //  キャッシュ
    //  Key: (ベース画像のオブジェクト参照, テーマ, 状態)
    // ─────────────────────────────────────────

    private static readonly Dictionary<(int, Theme, IconState), Bitmap> _cache
        = new Dictionary<(int, Theme, IconState), Bitmap>();

    /// <summary>
    /// キャッシュを全クリアします（テーマ切り替え時などに呼び出す）。
    /// </summary>
    public static void ClearCache()
    {
      // Dispose しない。PictureBox.Image が参照中の可能性があるため、
      // 即座に Dispose すると描画時に ObjectDisposedException になる。
      // アイコンは小サイズなので GC に回収を委ねて問題ない。
      _cache.Clear();
    }

    // ─────────────────────────────────────────
    //  メイン API
    // ─────────────────────────────────────────

    /// <summary>
    /// ベース画像（白シルエットPNG）に対して現在のテーマ＋指定状態の色を
    /// 適用した Bitmap を返します。返却値はキャッシュされます。
    /// Dispose しないでください（キャッシュが所有しています）。
    /// </summary>
    /// <param name="baseIcon">白シルエットのベース Bitmap</param>
    /// <param name="state">アイコンの状態</param>
    /// <returns>着色済み Bitmap（キャッシュ）</returns>
    public static Bitmap Get(Bitmap baseIcon, IconState state)
    {
      var key = (RuntimeHelpers_GetHashCode(baseIcon), _currentTheme, state);

      if (_cache.TryGetValue(key, out var cached))
        return cached;

      var color = ThemeColors[_currentTheme][state];
      var result = ApplyColorMatrix(baseIcon, color);
      _cache[key] = result;
      return result;
    }

    /// <summary>
    /// 現在のテーマ＋指定状態の色を直接指定して Bitmap を生成します。
    /// こちらはキャッシュされません。呼び出し元で Dispose してください。
    /// </summary>
    public static Bitmap CreateColored(Bitmap baseIcon, Color color)
    {
      return ApplyColorMatrix(baseIcon, color);
    }

    // ─────────────────────────────────────────
    //  ColorMatrix 適用（内部処理）
    // ─────────────────────────────────────────

    /// <summary>
    /// 黒シルエット画像に ColorMatrix で指定色を乗算します。
    /// アルファチャンネルは元画像のものを使用します（不透明度はColorから取得）。
    /// </summary>
    private static Bitmap ApplyColorMatrix(Bitmap src, Color tint)
    {
      float r = tint.R / 255f;
      float g = tint.G / 255f;
      float b = tint.B / 255f;
      float a = tint.A / 255f;

      // 黒(0,0,0)ベース画像用。乗算では黒×何=0になるため、
      // 5行目（平行移動）にオフセット加算で tint 色を与える。
      // アルファは元画像のアルファ × tint のアルファ
      var matrix = new ColorMatrix(new float[][]
      {
        [0, 0, 0, 0, 0],
        [0, 0, 0, 0, 0],
        [0, 0, 0, 0, 0],
        [0, 0, 0, a, 0],  // アルファだけ元画像から引き継ぐ
        [r, g, b, 0, 1],  // ← ここでオフセット加算
      });

      var attr = new ImageAttributes();
      attr.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

      var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
      using (var g2 = Graphics.FromImage(dst))
      {
        g2.Clear(Color.Transparent);
        g2.DrawImage(
            src,
            new Rectangle(0, 0, src.Width, src.Height),
            0, 0, src.Width, src.Height,
            GraphicsUnit.Pixel,
            attr);
      }
      attr.Dispose();
      return dst;
    }

    // RuntimeHelpers.GetHashCode の代わり（参照同一性によるハッシュ）
    private static int RuntimeHelpers_GetHashCode(object obj)
    {
      return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
  }
}