using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Menage.Core.Entregas;

public sealed record ResultadoDaEntrega(string Pasta, string? Zip, IReadOnlyList<string> Copiados, IReadOnlyList<string> Problemas)
{
    /// <summary>O que vai para a área de transferência e para o Explorer: o .zip, ou a pasta se o zip está desligado.</summary>
    public string Principal => Zip ?? Pasta;
}

/// <summary>
/// Monta a pasta de um pacote (copia os arquivos, compacta pastas pedidas) e o .zip final com a pasta na raiz.
/// Não envia nada a lugar nenhum.
/// </summary>
public static partial class PreparadorDeEntrega
{
    static readonly string[] Sufixos = ["-sources", "-javadoc", "-binaries", "-tests", "-test-sources"];

    /// <summary>Pacotes cujos obrigatórios terminaram ok (e os condicionais, se participaram da geração).</summary>
    public static IReadOnlyList<PacoteDeEntrega> Completos(ConfiguracaoDeEntregas config, IReadOnlyDictionary<string, bool> resultados) =>
    [
        .. config.Pacotes.Where(p => p.Obrigatorios.Count > 0 && p.Itens.Count > 0
            && p.Obrigatorios.All(n => resultados.TryGetValue(n, out var ok) && ok)
            && p.SeSelecionados.All(n => !resultados.TryGetValue(n, out var ok) || ok)),
    ];

    /// <summary>Pacotes a oferecer agora: o projeto que acabou de terminar ok é o gatilho e o pacote está completo.</summary>
    public static IReadOnlyList<PacoteDeEntrega> PorGatilho(
        ConfiguracaoDeEntregas config, string projetoQueTerminou, IReadOnlyDictionary<string, bool> resultados) =>
        resultados.TryGetValue(projetoQueTerminou, out var ok) && ok
            ? [.. Completos(config, resultados).Where(p => p.Gatilho.Equals(projetoQueTerminou, StringComparison.OrdinalIgnoreCase))]
            : [];

    /// <summary>Pacotes sem gatilho, a oferecer no fim da geração (os com gatilho já foram oferecidos).</summary>
    public static IReadOnlyList<PacoteDeEntrega> NoFim(ConfiguracaoDeEntregas config, IReadOnlyDictionary<string, bool> resultados) =>
        [.. Completos(config, resultados).Where(p => p.Gatilho.Length == 0)];

    /// <summary>Arquivo mais recente que combina com a origem (aceita * e ?), sem -sources/-javadoc/-binaries/-tests.</summary>
    public static string? AcharArquivo(string pastaDoProjeto, string origem)
    {
        var caminho = Path.Combine(pastaDoProjeto, origem);
        if (origem.IndexOfAny(['*', '?']) < 0) return File.Exists(caminho) ? caminho : null;
        var dir = Path.GetDirectoryName(caminho);
        if (dir is null || !Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, Path.GetFileName(caminho))
            .Where(f => !Sufixos.Any(s => Path.GetFileNameWithoutExtension(f).EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    /// <summary>O que será copiado, para mostrar antes de confirmar (✔ achou, ✖ não achou).</summary>
    public static IReadOnlyList<string> Previa(PacoteDeEntrega pacote, Func<string, string?> pastaDe) =>
    [
        .. pacote.Itens.Select(it =>
        {
            if (pastaDe(it.Projeto) is not { } pasta) return $"✖ {it.Projeto}: pasta do projeto não encontrada";
            if (it.Tipo == TipoDeItem.PastaEmZip)
                return Directory.Exists(Path.Combine(pasta, it.Origem))
                    ? $"✔ {it.Projeto}: {it.Origem}  →  {NomeDoZip(it)}"
                    : $"✖ {it.Projeto}: pasta {it.Origem} não encontrada";
            return AcharArquivo(pasta, it.Origem) is { } f
                ? $"✔ {it.Projeto}: {Path.GetFileName(f)}  ({Tamanho(new FileInfo(f).Length)})"
                : $"✖ {it.Projeto}: nenhum arquivo {it.Origem}";
        }),
    ];

    public static ResultadoDaEntrega Preparar(ConfiguracaoDeEntregas config, PacoteDeEntrega pacote, Func<string, string?> pastaDe, DateTime? agora = null)
    {
        var nome = NaoAlfanumerico().Replace(pacote.Nome.ToLowerInvariant(), "-");
        var pasta = Path.Combine(config.Pasta, $"{nome}_{agora ?? DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
        Directory.CreateDirectory(pasta);
        var copiados = new List<string>();
        var problemas = new List<string>();
        foreach (var item in pacote.Itens) CopiarItem(item, pasta, pastaDe, copiados, problemas);

        string? zip = null;
        if (config.Zip && copiados.Count > 0)
        {
            try
            {
                zip = pasta.TrimEnd('\\', '/') + ".zip";
                Compactar(pasta, zip);
                copiados.Add($"ZIP: {Path.GetFileName(zip)}  ({Tamanho(new FileInfo(zip).Length)})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                zip = null;
                problemas.Add($"não foi possível criar o .zip da entrega: {ex.Message}");
            }
        }

        return new ResultadoDaEntrega(pasta, zip, copiados, problemas);
    }

    static void CopiarItem(ItemDeEntrega item, string pasta, Func<string, string?> pastaDe, List<string> copiados, List<string> problemas)
    {
        try
        {
            if (pastaDe(item.Projeto) is not { } origemDoProjeto) { problemas.Add($"{item.Projeto}: pasta do projeto não encontrada"); return; }
            if (item.Tipo == TipoDeItem.PastaEmZip)
            {
                var dir = Path.Combine(origemDoProjeto, item.Origem);
                if (!Directory.Exists(dir)) { problemas.Add($"{item.Projeto}: pasta {item.Origem} não encontrada"); return; }
                var zip = Path.Combine(pasta, NomeDoZip(item));
                Compactar(dir, zip);
                copiados.Add($"{Path.GetFileName(zip)}  ({Tamanho(new FileInfo(zip).Length)})");
                return;
            }

            if (AcharArquivo(origemDoProjeto, item.Origem) is not { } arquivo) { problemas.Add($"{item.Projeto}: nenhum arquivo {item.Origem}"); return; }
            var destino = Path.Combine(pasta, item.Nome.Length > 0 ? item.Nome : Path.GetFileName(arquivo));
            File.Copy(arquivo, destino, overwrite: true);
            copiados.Add($"{Path.GetFileName(destino)}  ({Tamanho(new FileInfo(destino).Length)})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problemas.Add($"{item.Projeto}: {ex.Message}");
        }
    }

    static string NomeDoZip(ItemDeEntrega item)
    {
        var nome = item.Nome.Length > 0 ? item.Nome : Path.GetFileName(item.Origem.TrimEnd('\\', '/'));
        return nome.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? nome : nome + ".zip";
    }

    public static string Tamanho(long bytes) =>
        bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0} KB";

    /// <summary>Zip com a própria pasta na raiz (simulador/...) e "/" como separador, o padrão do formato.</summary>
    static void Compactar(string pasta, string zip)
    {
        var raiz = Path.GetFileName(pasta.TrimEnd('\\', '/'));
        var tamanho = pasta.TrimEnd('\\', '/').Length + 1;
        if (File.Exists(zip)) File.Delete(zip);
        using var fs = File.Create(zip);
        using var arquivo = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var f in Directory.GetFiles(pasta, "*", SearchOption.AllDirectories))
            arquivo.CreateEntryFromFile(f, raiz + "/" + f[tamanho..].Replace('\\', '/'), CompressionLevel.Optimal);
    }

    [GeneratedRegex("[^a-z0-9_-]+")]
    private static partial Regex NaoAlfanumerico();
}
