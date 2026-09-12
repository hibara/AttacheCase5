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

//----------------------------------------------------------------------
// 「高度な設定」のパスワードファイル関連のテスト。
//----------------------------------------------------------------------

using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class PasswordFileSettingTests
{
    private static RunContext Context(AppSettings settings) => new() { Settings = settings };

    [Fact]
    public void TheDefaultsAreAllOff()
    {
        var settings = new AppSettings();

        Assert.False(settings.AllowPasswordFileDragDrop);
        Assert.False(settings.CheckPasswordFileOnEncrypt);
        Assert.False(settings.CheckPasswordFileOnDecrypt);
        Assert.False(settings.NoErrorWhenPasswordFileMissing);
        Assert.False(settings.ExecuteWithPasswordFileWithoutConfirm);
    }

    [Fact]
    public async Task WhenTheCheckIsOff_NothingIsLoaded()
    {
        var directory = Directory.CreateTempSubdirectory("atc-pf").FullName;
        try
        {
            var file = Path.Combine(directory, "key.bin");
            File.WriteAllBytes(file, [1, 2, 3]);

            var settings = new AppSettings { EncryptPasswordFilePath = file };

            var material = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), forDecryption: false);

            Assert.False(material.IsAvailable);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TheConfiguredFileBecomesTheKeyMaterial()
    {
        var directory = Directory.CreateTempSubdirectory("atc-pf").FullName;
        try
        {
            var file = Path.Combine(directory, "key.bin");
            File.WriteAllBytes(file, [1, 2, 3, 4, 5]);

            var settings = new AppSettings
            {
                CheckPasswordFileOnEncrypt = true,
                EncryptPasswordFilePath = file,
            };

            var material = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), forDecryption: false);

            Assert.True(material.IsAvailable);
            Assert.Equal(file, material.Path);
            Assert.Equal(PasswordFileHash.ComputeSha256(file), material.Material);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TheEncryptAndDecryptPathsAreSeparate()
    {
        var directory = Directory.CreateTempSubdirectory("atc-pf").FullName;
        try
        {
            var encryptFile = Path.Combine(directory, "encrypt.bin");
            var decryptFile = Path.Combine(directory, "decrypt.bin");
            File.WriteAllBytes(encryptFile, [1]);
            File.WriteAllBytes(decryptFile, [2]);

            var settings = new AppSettings
            {
                CheckPasswordFileOnEncrypt = true,
                EncryptPasswordFilePath = encryptFile,
                CheckPasswordFileOnDecrypt = true,
                DecryptPasswordFilePath = decryptFile,
            };

            var forEncrypt = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), false);
            var forDecrypt = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), true);

            Assert.Equal(encryptFile, forEncrypt.Path);
            Assert.Equal(decryptFile, forDecrypt.Path);
            Assert.NotEqual(forEncrypt.Material, forDecrypt.Material);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AMissingFileIsToleratedWhenTheErrorIsTurnedOff()
    {
        var settings = new AppSettings
        {
            CheckPasswordFileOnDecrypt = true,
            DecryptPasswordFilePath = Path.Combine(Path.GetTempPath(), "no-such-key.bin"),
            NoErrorWhenPasswordFileMissing = true,
        };

        var material = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), forDecryption: true);

        Assert.False(material.IsAvailable);
    }

    [Fact]
    public void TheCommandLineOptionsMapToTheSettings()
    {
        var effective = CliParser.Parse(["/pf=1", "/nomsgp=1"]).ApplyTo(new AppSettings());

        Assert.True(effective.AllowPasswordFileDragDrop);
        Assert.True(effective.NoErrorWhenPasswordFileMissing);
        Assert.Empty(CliParser.Parse(["/pf=1", "/nomsgp=1"]).IgnoredOptions);
    }

    [Fact]
    public void TheSettingsAreSaved()
    {
        var directory = Directory.CreateTempSubdirectory("atc-pf-save").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            var settings = service.Current.Clone();
            settings.AllowPasswordFileDragDrop = true;
            settings.CheckPasswordFileOnEncrypt = true;
            settings.EncryptPasswordFilePath = "/tmp/key.bin";
            settings.ExecuteWithPasswordFileWithoutConfirm = true;
            service.Save(settings);

            var reopened = new SettingsService(path).Current;

            Assert.True(reopened.AllowPasswordFileDragDrop);
            Assert.True(reopened.CheckPasswordFileOnEncrypt);
            Assert.Equal("/tmp/key.bin", reopened.EncryptPasswordFilePath);
            Assert.True(reopened.ExecuteWithPasswordFileWithoutConfirm);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AFileFromTheSettingsDecryptsWhatTheSameFileEncrypted()
    {
        var directory = Directory.CreateTempSubdirectory("atc-pf-roundtrip").FullName;
        try
        {
            var keyFile = Path.Combine(directory, "key.bin");
            File.WriteAllBytes(keyFile, Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());

            var source = Path.Combine(directory, "source.txt");
            File.WriteAllText(source, "password file from settings");

            var settings = new AppSettings
            {
                CheckPasswordFileOnEncrypt = true,
                EncryptPasswordFilePath = keyFile,
                CheckPasswordFileOnDecrypt = true,
                DecryptPasswordFilePath = keyFile,
            };

            var forEncrypt = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), false);
            var encrypted = Path.Combine(directory, "source.atc");

            var encryptResult = await new EncryptionService().EncryptAsync(
                [source], encrypted, string.Empty,
                new EncryptionOptions { PasswordBinary = forEncrypt.Material });
            Assert.True(encryptResult.Success, encryptResult.ErrorMessage);

            var forDecrypt = await PasswordFileLoader.TryLoadConfiguredAsync(Context(settings), true);
            var output = Path.Combine(directory, "out");

            var decryptResult = await new DecryptionService().DecryptAsync(
                encrypted, output, string.Empty,
                new DecryptionOptions { PasswordBinary = forDecrypt.Material });

            Assert.True(decryptResult.Success, decryptResult.ErrorMessage);
            Assert.Equal("password file from settings", File.ReadAllText(Path.Combine(output, "source.txt")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
