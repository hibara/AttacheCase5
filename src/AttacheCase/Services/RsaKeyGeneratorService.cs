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
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace AttacheCase.Services;

public class RsaKeyGeneratorService : IRsaKeyGenerator
{
  public (string publicKeyPath, string privateKeyPath) GenerateKeyPair(string outputDirectory, string baseFileName)
  {
    string? guidString = null;
    if (string.IsNullOrEmpty(guidString))
    {
      // GUIDを生成する
      var guid = Guid.NewGuid();
      guidString = guid.ToString();
    }

    // 公開鍵・秘密鍵のファイルパス
    var publicKeyFilePath = Path.Combine(outputDirectory, baseFileName + ".atcpub");
    var privateFilePath = Path.Combine(outputDirectory, baseFileName + ".atcpvt");

    //-----------------------------------
    //RSACryptoServiceProviderオブジェクトの作成
    // 鍵長は 4096（旧 Windows 5.0 と同じ。v150 の宛先識別子 PkaAlgorithmId.Rsa4096）。
    // 2048 の鍵（AttacheCase 4 で作ったもの）も暗号化・復号には使える
    var rsa = new RSACryptoServiceProvider(4096);

    //公開鍵をXML形式で取得
    var publicKey = rsa.ToXmlString(false);
    //秘密鍵をXML形式で取得
    var privateKey = rsa.ToXmlString(true);

    //-----------------------------------
    // 公開鍵XMLファイルの編集
    var xml = XElement.Parse(publicKey);
    // 種別
    var xmlType = new XElement("type", "public");
    xml.AddFirst(xmlType);
    // GUID
    var xmlGuid = new XElement("id", guidString);
    xml.AddFirst(xmlGuid);
    // Token
    var xmlToken = new XElement("token", "AttacheCase");
    xml.AddFirst(xmlToken);
    // 公開鍵として保存する
    xml.Save(publicKeyFilePath);

    //-----------------------------------
    // 秘密鍵XMLファイルの編集
    xml = XElement.Parse(privateKey);
    // 種別
    xmlType = new XElement("type", "private");
    xml.AddFirst(xmlType);
    // GUID
    xml.AddFirst(xmlGuid);
    // Token
    xml.AddFirst(xmlToken);
    // 秘密鍵として保存する
    xml.Save(privateFilePath);

    rsa.Clear();

    return (publicKeyFilePath, privateFilePath);
  }
}