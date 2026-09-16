using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

internal enum InputType
{
    Csproj,
    Plist
}

internal enum VersionKind
{
    Assembly,
    File,
    Version,
    Short,
    Bundle
}

internal static class Program
{
    private static readonly Regex PropertyReferenceRegex = new(
        @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
        RegexOptions.Compiled);

    // 入力ファイルの種類ごとに使えるバージョン種別オプション。
    // 渡されたファイルの拡張子に対応する表だけを受け付ける。
    private static readonly Dictionary<string, VersionKind> CsprojOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["--assembly"] = VersionKind.Assembly,
            ["-a"] = VersionKind.Assembly,

            ["--file"] = VersionKind.File,
            ["--fileversion"] = VersionKind.File,
            ["--file-version"] = VersionKind.File,
            ["-f"] = VersionKind.File,

            ["--version"] = VersionKind.Version,
            ["-v"] = VersionKind.Version
        };

    private static readonly Dictionary<string, VersionKind> PlistOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["--short"] = VersionKind.Short,
            ["--short-version"] = VersionKind.Short,
            ["-s"] = VersionKind.Short,

            ["--bundle"] = VersionKind.Bundle,
            ["--bundle-version"] = VersionKind.Bundle,
            ["-b"] = VersionKind.Bundle
        };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            if (args.Length == 1 &&
                args[0] is "--help" or "-h" or "/?")
            {
                PrintUsage();
                return 0;
            }

            string inputPath = args[0];

            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException(
                    $"ファイルが見つかりません: {inputPath}");
            }

            InputType inputType = DetectInputType(inputPath);

            VersionKind? requestedKind = null;
            bool integerFormat = false;

            for (int i = 1; i < args.Length; i++)
            {
                string option = args[i];

                if (TryGetVersionKind(
                        inputType,
                        option,
                        out VersionKind kind))
                {
                    SetVersionKind(ref requestedKind, kind);
                    continue;
                }

                switch (option.ToLowerInvariant())
                {
                    case "--int":
                    case "-i":
                        integerFormat = true;
                        break;

                    case "--help":
                    case "-h":
                        PrintUsage();
                        return 0;

                    default:
                        throw new ArgumentException(
                            BuildUnknownOptionMessage(
                                inputType,
                                option));
                }
            }

            VersionKind versionKind =
                requestedKind ?? GetDefaultVersionKind(inputType);

            string result = inputType switch
            {
                InputType.Csproj =>
                    ReadCsprojVersion(inputPath, versionKind),

                InputType.Plist =>
                    ReadPlistVersion(inputPath, versionKind),

                _ =>
                    throw new InvalidOperationException()
            };

            if (integerFormat)
            {
                result = ConvertToIntegerFormat(result);
            }

            // 標準出力には値だけを出す。
            Console.WriteLine(result);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GetVer: {ex.Message}");
            return 1;
        }
    }

    // 入力ファイルの拡張子から、扱う対象を決める。
    // .csproj なら MSBuild のバージョンプロパティ、.plist なら Info.plist のキーを読む。
    // Mac 版でも .csproj の Version から Info.plist が生成される構成
    // （Dotnet.Bundle など）があるため、実行 OS では判定しない。
    private static InputType DetectInputType(string path)
    {
        string extension =
            Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".csproj" => InputType.Csproj,
            ".plist" => InputType.Plist,
            _ => throw new ArgumentException(
                $"対応していないファイル形式です: {Path.GetFileName(path)}" +
                "（.csproj または Info.plist を指定してください）")
        };
    }

    private static string GetTargetDescription(
        InputType inputType)
    {
        return inputType switch
        {
            InputType.Csproj => ".csproj",
            InputType.Plist => "Info.plist",
            _ => throw new InvalidOperationException()
        };
    }

    private static bool TryGetVersionKind(
        InputType inputType,
        string option,
        out VersionKind kind)
    {
        Dictionary<string, VersionKind> options =
            GetVersionKindOptions(inputType);

        return options.TryGetValue(option, out kind);
    }

    private static Dictionary<string, VersionKind> GetVersionKindOptions(
        InputType inputType)
    {
        return inputType switch
        {
            InputType.Csproj => CsprojOptions,
            InputType.Plist => PlistOptions,
            _ => throw new InvalidOperationException()
        };
    }

    private static string BuildUnknownOptionMessage(
        InputType inputType,
        string option)
    {
        // 他方のファイル形式用のオプションなら、その旨を伝える。
        InputType otherType = inputType == InputType.Csproj
            ? InputType.Plist
            : InputType.Csproj;

        if (GetVersionKindOptions(otherType).ContainsKey(option))
        {
            return
                $"{option} は {GetTargetDescription(otherType)} 用のオプションです" +
                $"（{GetTargetDescription(inputType)} では使用できません）。";
        }

        return $"不明なオプションです: {option}";
    }

    private static VersionKind GetDefaultVersionKind(
        InputType inputType)
    {
        return inputType switch
        {
            InputType.Csproj =>
                VersionKind.Assembly,

            InputType.Plist =>
                VersionKind.Short,

            _ =>
                throw new InvalidOperationException()
        };
    }

    private static void SetVersionKind(
        ref VersionKind? currentKind,
        VersionKind newKind)
    {
        if (currentKind.HasValue)
        {
            throw new ArgumentException(
                "バージョン種別は1つだけ指定してください。");
        }

        currentKind = newKind;
    }

    private static string ReadCsprojVersion(
        string projectPath,
        VersionKind versionKind)
    {
        XDocument document = XDocument.Load(projectPath);

        var properties =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (XElement propertyGroup in document
                     .Descendants()
                     .Where(x =>
                         x.Name.LocalName == "PropertyGroup"))
        {
            foreach (XElement property
                     in propertyGroup.Elements())
            {
                string value = property.Value.Trim();

                if (value.Length > 0)
                {
                    properties[property.Name.LocalName] =
                        value;
                }
            }
        }

        string version =
            ReadVersion(properties);

        properties["Version"] = version;

        string assemblyVersion;

        if (properties.TryGetValue(
                "AssemblyVersion",
                out string? rawAssemblyVersion))
        {
            assemblyVersion =
                ResolveProperties(
                    rawAssemblyVersion,
                    properties);
        }
        else
        {
            assemblyVersion =
                NormalizeFourPartVersion(
                    RemoveVersionSuffix(version));
        }

        properties["AssemblyVersion"] =
            assemblyVersion;

        string fileVersion;

        if (properties.TryGetValue(
                "FileVersion",
                out string? rawFileVersion))
        {
            fileVersion =
                ResolveProperties(
                    rawFileVersion,
                    properties);
        }
        else
        {
            fileVersion =
                NormalizeFourPartVersion(
                    RemoveVersionSuffix(version));
        }

        return versionKind switch
        {
            VersionKind.Assembly =>
                assemblyVersion,

            VersionKind.File =>
                fileVersion,

            VersionKind.Version =>
                version,

            _ =>
                throw new InvalidOperationException()
        };
    }

    private static string ReadVersion(
        Dictionary<string, string> properties)
    {
        if (properties.TryGetValue(
                "Version",
                out string? rawVersion))
        {
            return ResolveProperties(
                rawVersion,
                properties);
        }

        string versionPrefix = "1.0.0";

        if (properties.TryGetValue(
                "VersionPrefix",
                out string? rawVersionPrefix))
        {
            versionPrefix =
                ResolveProperties(
                    rawVersionPrefix,
                    properties);
        }

        string? versionSuffix = null;

        if (properties.TryGetValue(
                "VersionSuffix",
                out string? rawVersionSuffix))
        {
            versionSuffix =
                ResolveProperties(
                    rawVersionSuffix,
                    properties);
        }

        if (!string.IsNullOrWhiteSpace(versionSuffix))
        {
            return $"{versionPrefix}-{versionSuffix}";
        }

        return versionPrefix;
    }

    private static string ReadPlistVersion(
        string plistPath,
        VersionKind versionKind)
    {
        string keyName = versionKind switch
        {
            VersionKind.Short =>
                "CFBundleShortVersionString",

            VersionKind.Bundle =>
                "CFBundleVersion",

            _ =>
                throw new InvalidOperationException()
        };

        XDocument document;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        };

        using (XmlReader reader =
               XmlReader.Create(plistPath, settings))
        {
            document = XDocument.Load(reader);
        }

        XElement? dict = document
            .Descendants()
            .FirstOrDefault(x =>
                x.Name.LocalName == "dict");

        if (dict is null)
        {
            throw new InvalidOperationException(
                "Info.plist に <dict> がありません。");
        }

        List<XElement> elements =
            dict.Elements().ToList();

        for (int i = 0; i < elements.Count - 1; i++)
        {
            XElement element = elements[i];

            if (element.Name.LocalName != "key")
            {
                continue;
            }

            if (element.Value.Trim() != keyName)
            {
                continue;
            }

            XElement valueElement =
                elements[i + 1];

            string value =
                valueElement.Value.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{keyName} の値が空です。");
            }

            return value;
        }

        throw new InvalidOperationException(
            $"Info.plist に {keyName} が見つかりません。");
    }

    private static string ResolveProperties(
        string value,
        Dictionary<string, string> properties)
    {
        string current = value;

        for (int i = 0; i < 20; i++)
        {
            string next =
                PropertyReferenceRegex.Replace(
                    current,
                    match =>
                    {
                        string propertyName =
                            match.Groups[1].Value;

                        if (properties.TryGetValue(
                                propertyName,
                                out string? propertyValue))
                        {
                            return propertyValue;
                        }

                        return match.Value;
                    });

            if (next == current)
            {
                break;
            }

            current = next;
        }

        if (PropertyReferenceRegex.IsMatch(current))
        {
            throw new InvalidOperationException(
                $"MSBuildプロパティを解決できませんでした: {current}");
        }

        return current;
    }

    private static string RemoveVersionSuffix(
        string version)
    {
        int hyphenIndex =
            version.IndexOf('-');

        int plusIndex =
            version.IndexOf('+');

        int cutIndex = -1;

        if (hyphenIndex >= 0 &&
            plusIndex >= 0)
        {
            cutIndex =
                Math.Min(
                    hyphenIndex,
                    plusIndex);
        }
        else if (hyphenIndex >= 0)
        {
            cutIndex = hyphenIndex;
        }
        else if (plusIndex >= 0)
        {
            cutIndex = plusIndex;
        }

        return cutIndex >= 0
            ? version[..cutIndex]
            : version;
    }

    private static string NormalizeFourPartVersion(
        string version)
    {
        string[] parts =
            version.Split('.');

        if (parts.Length > 4)
        {
            throw new InvalidOperationException(
                $"バージョン形式が不正です: {version}");
        }

        if (parts.Any(part =>
                !int.TryParse(part, out _)))
        {
            throw new InvalidOperationException(
                $"バージョン形式が不正です: {version}");
        }

        var normalized =
            new List<string>(parts);

        while (normalized.Count < 4)
        {
            normalized.Add("0");
        }

        return string.Join(".", normalized);
    }

    private static string ConvertToIntegerFormat(
        string version)
    {
        if (!Regex.IsMatch(
                version,
                @"^\d+(?:\.\d+)*$"))
        {
            throw new InvalidOperationException(
                $"--int は数値とドットだけのバージョンに使用できます: {version}");
        }

        return version.Replace(
            ".",
            string.Empty);
    }

    private static void PrintUsage()
    {
        const string usage =
            """
            GetVer - project / Info.plist version reader

            渡したファイルの拡張子（.csproj / .plist）に合わせて、扱う対象とオプションが決まります。
            実行 OS には依存しません。

            Usage:
              GetVer <file> [options]

            .csproj:
              --assembly, -a    AssemblyVersion（既定）
              --file,     -f    FileVersion
              --version,  -v    Version

            Info.plist:
              --short,  -s      CFBundleShortVersionString（既定）
              --bundle, -b      CFBundleVersion

            Common:
              --int, -i         ドットを除去して出力

            Examples:
              GetVer App.csproj
              GetVer App.csproj --file
              GetVer App.csproj --version
              GetVer App.csproj --assembly --int
              GetVer Info.plist
              GetVer Info.plist --bundle
              GetVer Info.plist --short --int

            ※ Mac 版のバージョンを .csproj の Version から生成している構成（Dotnet.Bundle など）では
              GetVer App.csproj --version を使ってください。
            """;

        Console.Error.WriteLine(usage);
    }
}
