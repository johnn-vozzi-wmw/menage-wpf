using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Tomcat;

/// <summary>Um ambiente Tomcat da WMW (pasta já montada; o Menage não baixa o Tomcat).</summary>
public sealed record AmbienteTomcat
{
    public const string ContextoVendas = "wmwvendas";
    public const string ContextoDados = "wmwdados";

    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    public string Nome { get; init; } = "";

    public string Pasta { get; init; } = "";

    public int Porta { get; init; } = 8082;

    public string Grupo { get; init; } = "";

    public string Repositorio { get; init; } = "";

    public string NomeVendas { get; init; } = ContextoVendas;

    public string NomeDados { get; init; } = ContextoDados;

    /// <summary>Vazio = a mais recente publicada (por data).</summary>
    public string VersaoVendas { get; init; } = "";

    public string VersaoDados { get; init; } = "";

    public string InstaladoVendas { get; init; } = "";

    public string InstaladoDados { get; init; } = "";

    public string UltimoPreparo { get; init; } = "";

    public int ManterBackups { get; init; } = 2;

    /// <summary>Processo que o Menage subiu (para reencontrar depois de reabrir): PID e hora de início.</summary>
    public int Pid { get; init; }

    public long PidInicioTicks { get; init; }

    public string Titulo => Nome.Length > 0 ? Nome : "(sem nome)";

    public string Url(string contexto) => $"http://localhost:{Porta}/{contexto}/";
}

public sealed record ConfiguracaoDoTomcat
{
    public const string NomeDoArquivo = "tomcat-express";

    public IReadOnlyList<AmbienteTomcat> Ambientes { get; init; } = [];

    public string Selecionado { get; init; } = "";

    public static ConfiguracaoDoTomcat Padrao() => new();

    public ConfiguracaoDoTomcat Com(AmbienteTomcat ambiente) =>
        this with { Ambientes = Ambientes.Any(a => a.Id == ambiente.Id) ? [.. Ambientes.Select(a => a.Id == ambiente.Id ? ambiente : a)] : [.. Ambientes, ambiente] };

    public ConfiguracaoDoTomcat Sem(string id) => this with { Ambientes = [.. Ambientes.Where(a => a.Id != id)] };
}

/// <summary>Conferências e leituras de uma pasta de Tomcat. Tudo devolve <see cref="Erro"/>; nada lança.</summary>
public static partial class PastaDoTomcat
{
    public const string JdkPadrao = @"C:\wmw\ferramentas\java\jdk17";

    static readonly string[] _arquivosObrigatorios = [@"bin\catalina.bat", @"conf\server.xml", @"lib\catalina.jar"];

    public static string PastaDeTrabalho(string pasta) => Path.Combine(pasta, "_express");

    public static Erro? Validar(string pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) return new Erro("Informe a pasta do Tomcat", "O ambiente ainda não tem pasta.", "Escolha a pasta que contém bin, conf, lib e webapps (ex.: tomcat-vendas-8082).");
        if (!Directory.Exists(pasta)) return new Erro("A pasta do Tomcat não existe", pasta, "Escolha a pasta de novo (ela pode ter sido movida ou apagada).");
        string[] faltas = [.. _arquivosObrigatorios.Where(r => !File.Exists(Path.Combine(pasta, r)))];
        return faltas.Length == 0 ? null
            : new Erro("Esta pasta não parece um Tomcat", $"Faltou: {string.Join(", ", faltas)}. Pasta: {pasta}", "Escolha a pasta que contém bin, conf, lib e webapps. O Menage não baixa o Tomcat: ele vem desta pasta.");
    }

    /// <summary>"Tomcat 9.0.82", lido de lib\catalina.jar.</summary>
    public static string Versao(string pasta)
    {
        try
        {
            using var zip = ZipFile.OpenRead(Path.Combine(pasta, "lib", "catalina.jar"));
            var entrada = zip.GetEntry("org/apache/catalina/util/ServerInfo.properties");
            if (entrada is null) return "";
            using var leitor = new StreamReader(entrada.Open(), Encoding.UTF8);
            var m = NumeroDoServidor().Match(leitor.ReadToEnd());
            return m.Success ? "Tomcat " + m.Groups[1].Value : "";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>JAVA_HOME que o setenv.bat define (o último <c>set JAVA_HOME=</c> ativo); sem ele, a variável do sistema.</summary>
    public static string? JavaHome(string pasta)
    {
        try
        {
            var arquivo = Path.Combine(pasta, "bin", "setenv.bat");
            if (File.Exists(arquivo))
            {
                string? ultimo = null;
                foreach (Match m in LinhaJavaHome().Matches(File.ReadAllText(arquivo, Encoding.Latin1))) ultimo = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(ultimo))
                    return ultimo.Replace("%CATALINA_HOME%", pasta, StringComparison.OrdinalIgnoreCase).Replace("%CATALINA_BASE%", pasta, StringComparison.OrdinalIgnoreCase).Replace('/', '\\');
            }
        }
        catch (IOException) { /* cai na variável do sistema */ }

        var env = Environment.GetEnvironmentVariable("JAVA_HOME");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim().Replace('/', '\\');
    }

    public static string VersaoJava(string javaHome)
    {
        try
        {
            var m = VersaoDoJava().Match(File.ReadAllText(Path.Combine(javaHome, "release")));
            return m.Success ? "Java " + m.Groups[1].Value : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    public static Erro? ValidarJava(string pasta)
    {
        var jh = JavaHome(pasta);
        if (jh is null)
            return new Erro("Nenhum Java configurado para este Tomcat", @"O bin\setenv.bat não define JAVA_HOME e a variável do Windows também não existe.", $"Edite o bin\\setenv.bat e coloque: set \"JAVA_HOME={JdkPadrao.Replace('\\', '/')}\"");
        return File.Exists(Path.Combine(jh, "bin", "java.exe")) ? null
            : new Erro("O Java configurado não existe", $@"JAVA_HOME = {jh} (não achei bin\java.exe nessa pasta)", $"Ajuste o bin\\setenv.bat para o JDK 17 ({JdkPadrao}) ou instale o JDK nessa pasta.");
    }

    /// <summary>Linhas do wmw_database_config.bat que o cmd não entende (o Tomcat sairia com código 255 sem explicar).</summary>
    public static IReadOnlyList<string> LinhasInvalidasDoBat(string pasta)
    {
        var arquivo = Path.Combine(pasta, "bin", "wmw_database_config.bat");
        if (!File.Exists(arquivo)) return [];
        var ruins = new List<string>();
        var linhas = ArquivoDeTexto.Ler(arquivo).Texto.Replace("\r", "").Split('\n');
        static string Curta(string l) => l.Length > 70 ? l[..70] + "..." : l;
        for (var i = 0; i < linhas.Length; i++)
        {
            var l = linhas[i].Trim();
            if (l.Length == 0 || l.StartsWith("::", StringComparison.Ordinal) || l.StartsWith('@') || ComecaCom(l, "set ", "rem ", "if ", "call ", "echo")) continue;
            ruins.Add($"linha {i + 1}: {Curta(l)}");
        }

        for (var i = 0; i < linhas.Length; i++)
        {
            var l = linhas[i].Trim();
            if (l.StartsWith("set \"", StringComparison.OrdinalIgnoreCase) && (l.Count(c => c == '"') != 2 || !l.EndsWith('"')))
                ruins.Add($"linha {i + 1}: aspas desbalanceadas em {Curta(l)}");
        }

        return ruins;
    }

    public static long EspacoLivre(string pasta)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(pasta))!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return -1; }
    }

    public static string Formatar(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };

    public static void ApagarArvore(string pasta)
    {
        if (!Directory.Exists(pasta)) return;
        foreach (var f in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(pasta, recursive: true);
    }

    /// <summary>Copia uma vez só (a original, de antes do Menage mexer).</summary>
    public static void CopiaUmaVez(string arquivo)
    {
        var bak = arquivo + ".menage.bak";
        if (File.Exists(arquivo) && !File.Exists(bak)) File.Copy(arquivo, bak);
    }

    static bool ComecaCom(string linha, params string[] prefixos) => prefixos.Any(p => linha.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"server\.number=(\S+)")]
    private static partial Regex NumeroDoServidor();

    [GeneratedRegex(@"(?im)^[ \t]*set[ \t]+""?JAVA_HOME=([^""\r\n]*)""?")]
    private static partial Regex LinhaJavaHome();

    [GeneratedRegex(@"JAVA_VERSION=""([^""]+)""")]
    private static partial Regex VersaoDoJava();
}

/// <summary>Texto de arquivo preservando codificação (UTF-8 ou Windows-1252), BOM e quebras; grava de forma atômica.</summary>
public sealed class ArquivoDeTexto
{
    ArquivoDeTexto(string caminho) => Caminho = caminho;

    public string Caminho { get; }

    public string Texto { get; set; } = "";

    Encoding Codificacao { get; set; } = new UTF8Encoding(false);

    bool Bom { get; set; }

    public static ArquivoDeTexto Ler(string caminho)
    {
        var bytes = File.ReadAllBytes(caminho);
        var arquivo = new ArquivoDeTexto(caminho);
        var inicio = 0;
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            arquivo.Bom = true;
            inicio = 3;
        }

        try
        {
            arquivo.Texto = new UTF8Encoding(false, true).GetString(bytes, inicio, bytes.Length - inicio);
        }
        catch (DecoderFallbackException)
        {
            arquivo.Codificacao = Encoding.Latin1;
            arquivo.Bom = false;
            arquivo.Texto = Encoding.Latin1.GetString(bytes);
        }

        return arquivo;
    }

    public void Gravar()
    {
        var temporario = Caminho + ".tmp";
        using (var fs = new FileStream(temporario, FileMode.Create, FileAccess.Write))
        {
            if (Bom) fs.Write([0xEF, 0xBB, 0xBF]);
            fs.Write(Codificacao.GetBytes(Texto));
        }

        File.Move(temporario, Caminho, overwrite: true);
    }
}
