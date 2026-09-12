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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Forms;

namespace ExeToHex
{
	internal static class Program
	{
    private static int _exeOutSize;

    /// <summary>
    /// 自己実行形式ファイル（バイナリ）から、byte[] 文字列へ変換して、
    /// アタッシェケースプロジェクト構成ファイル内（ ExeOut48.cs ）に
    /// ソースコードとして書き込む外部ツール
    /// </summary>
    /// <param name="args">
    ///	0: Source code file ( ExeOut48.cs ) path
    ///	1: Exeout binary file ( Exeout.exe ) path (.NET Framework 4.8)
    /// </param>
    /// <returns></returns>
		static int Main(string[] args)
		{
			if (args.Length < 2)	// 引数の数が足りてない
			{
				MessageBox.Show("２つの引数が必要です！");
				return (1);
			}

			//-----------------------------------
			// args[0]: CS file that the binary data is written to
			var cSharpSourceFilePath = args[0];
			if (!File.Exists(cSharpSourceFilePath))
			{
				MessageBox.Show("C#ソースファイルの存在が見つかりません！　第一引数が不正です。");
				return (1);
			}
			else if (!string.Equals(Path.GetExtension(cSharpSourceFilePath), (".cs"), StringComparison.CurrentCultureIgnoreCase))
			{
				MessageBox.Show("C#ソースファイルの指定ではありません！　第一引数が不正です。");
				return (1);
			}

			//-----------------------------------
			// args[1]: Executable file (.NET Framework 4.8) that is written to binary data
			if (!File.Exists(args[1]))
			{
				MessageBox.Show("実行ファイルの存在が見つかりません！　第二引数が不正です。");
				return (1);
			}
			else if (!string.Equals(Path.GetExtension(args[1]), (".exe"), StringComparison.CurrentCultureIgnoreCase))
			{
				MessageBox.Show("実行ファイルの拡張子ではありません！　第二引数が不正です。");
				return (1);
			}

			//----------------------------------------------------------------------
			// ソースファイルにある、`#region` ～ `endregion` の間を一旦クリアする
			var srcOutList = new List<string>();
			var srcList = new List<string>(File.ReadAllLines(cSharpSourceFilePath, Encoding.UTF8));

			var fSkip = false;

			for (var i = 0; i < srcList.Count(); i++)
			{
				if (srcList[i].Contains("#region"))
				{
					fSkip = true;
					srcOutList.Add(srcList[i]);
					continue;
				}

				if (srcList[i].Contains("#endregion"))
				{
					fSkip = false;
					srcOutList.Add(srcList[i]);
					continue;
				}

				if (!fSkip)
				{
					srcOutList.Add(srcList[i]);
				}

			}

			//----------------------------------------------------------------------
			// ソースファイル（ExeOut48.cs）を加工して書き戻す
			//
			// #region ATC executable file size
			// #endregion
			//
			// #region ATC executable file bytes data
			// #endregion

			long exeFileSize = new FileInfo(args[1]).Length;
			string version = FileVersionInfo.GetVersionInfo(args[1]).ProductVersion;

			for (var i = 0; i < srcOutList.Count(); i++)
			{
				if (srcOutList[i].Contains("#region ATC executable file size"))
				{
					srcOutList.Insert(i + 1,
						"      " + exeFileSize + ";  // ver." + version);
				}
				else if (srcOutList[i].Contains("#region ATC executable file bytes data"))
				{
					srcOutList.Insert(i + 1, ReadExeBinaryFileToString(args[1]));
					var exeOutSizeString = exeFileSize.ToString("#,0") + " Bytes";
					Console.WriteLine("以下のファイルに、" + exeOutSizeString + " ( .NET Framework 4.8 ) を書き込みました。\n" + cSharpSourceFilePath);
				}
			}

			// ExeOut4.cs を読み込んで加工したデータをそのまま同じファイルに書き戻す
			File.WriteAllLines(cSharpSourceFilePath, srcOutList);

      return (0);

		}// end static int Main(string[] args);


		/// <summary>
		/// Reads the binary content of an executable file and converts it to a string representation.
		/// </summary>
		/// <param name="exeFilePath">The path to the executable file.</param>
		/// <returns>A string representation of the binary content of the given executable file.</returns>
		private static string ReadExeBinaryFileToString(string exeFilePath)
		{
			using (var ms = new MemoryStream())
			using (var fs = new FileStream(exeFilePath, FileMode.Open, FileAccess.Read))
			{
				_exeOutSize = (int)fs.Length;

				string result;
				using (var sw = new StreamWriter(ms))
				{
					ms.Position = 0;

					var br = new BinaryReader(fs);
					var data = new byte[_exeOutSize];

					var count = 0;

					for (var i = 0; i < _exeOutSize; i++)
					{
						data[i] = br.ReadByte();

						var dataString = i == _exeOutSize - 1 ? $"0x{data[i]:X2}" : $"0x{data[i]:X2}, ";

						if (count == 0)
						{
							sw.Write("      " + dataString);
						}
						else if (count > 15)
						{
							sw.WriteLine(dataString); // 改行
							count = -1;
						}
						else
						{
							sw.Write(dataString);
						}

						count++;

					}

					sw.WriteLine("");

					// Be sure to flush the StreamWriter to ensure all data has been written to the MemoryStream
					sw.Flush();

					// Set the position back to the start of the MemoryStream
					ms.Position = 0;

					// Read the MemoryStream out to a string
					using (var sr = new StreamReader(ms))
					{
						result = sr.ReadToEnd();
					}
				}
				return result;
			}

		}

	}

}
