using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.LinhaDoTempo;

/// <summary>
/// Lê as gerações do Gerador de Beta: uma pasta <c>yyyy-MM-dd_HH-mm-ss_modo</c> por geração, com um
/// <c>NN-projeto.log</c> por projeto (cabeçalho com Pasta/Branch e rodapé "RESULTADO: ..."). Quando o surefire do
/// lavenderepda repete, <c>NN-projeto-surefire.log</c> substitui o primeiro. Só o início e o fim de cada log são lidos.
/// Não há commit nos logs do Menage: a linha mostra branch e resultado, não o hash.
/// </summary>
public static partial class LeitorDeBetas
{
    const int BytesDoRodape = 4096;

    public static IReadOnlyList<EventoDeRelease> Ler(string pastaDeLogs, IReadOnlyList<string> pastasBase, int maximo = 150)
    {
        if (!Directory.Exists(pastaDeLogs)) return [];
        var eventos = new List<EventoDeRelease>();
        foreach (var pasta in Directory.GetDirectories(pastaDeLogs).OrderByDescending(p => p, StringComparer.Ordinal).Take(maximo))
            if (LerGeracao(pasta, pastasBase) is { } evento) eventos.Add(evento);
        return eventos;
    }

    static readonly string[] _pastasDeGrupo = ["web", "framework", "app", "pda"];

    /// <summary>Ambiente = a pasta base que contém o projeto; sem base conhecida, a pasta acima do projeto (ou acima do grupo web/framework/app/pda).</summary>
    public static string AmbienteDe(string pastaDoProjeto, IReadOnlyList<string> pastasBase)
    {
        if (pastaDoProjeto.Length == 0) return "";
        foreach (var b in pastasBase.OrderByDescending(b => b.Length))
        {
            var basePura = b.TrimEnd('\\', '/');
            if (basePura.Length > 0 && pastaDoProjeto.StartsWith(basePura + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(basePura);
        }

        var pai = Directory.GetParent(pastaDoProjeto);
        if (pai is not null && _pastasDeGrupo.Contains(pai.Name, StringComparer.OrdinalIgnoreCase)) pai = pai.Parent;
        return pai?.Name ?? "";
    }

    static EventoDeRelease? LerGeracao(string pasta, IReadOnlyList<string> pastasBase)
    {
        var partes = Path.GetFileName(pasta).Split('_');
        if (partes.Length < 3 || !DateTime.TryParseExact($"{partes[0]}_{partes[1]}", "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            return null;

        var logs = EscolherLogs(pasta).Select(Ler).Where(l => l is not null).Select(l => l!).ToList();
        if (logs.Count == 0) return null;

        var ambiente = AmbienteDe(logs.Select(l => l.Pasta).FirstOrDefault(p => p.Length > 0) ?? "", pastasBase);
        var branch = logs.Select(l => l.Branch).FirstOrDefault(b => b.Length > 0) ?? "?";
        var erros = logs.Count(l => l.Estado == EstadoDoEvento.Erro);
        var semResultado = logs.Count(l => l.Estado == EstadoDoEvento.SemResultado);
        var segundos = logs.Sum(l => l.Segundos);
        var estado = erros > 0 ? EstadoDoEvento.Erro : semResultado > 0 ? EstadoDoEvento.SemResultado : EstadoDoEvento.Ok;
        var total = logs.Count;

        return new EventoDeRelease(
            data, TipoDeEvento.Beta,
            $"Beta {partes[2]}  •  {branch}  •  {total} {(total == 1 ? "projeto" : "projetos")}",
            string.Join("   |   ", logs.Select(l => $"{l.Projeto} - {Rotulo(l.Estado)}{(l.Branch != branch && l.Branch.Length > 0 ? $" ({l.Branch})" : "")}"))
                + (segundos > 0 ? "   |   " + Duracao(segundos) : ""),
            ambiente, estado, [.. logs.Select(l => l.Projeto)]);
    }

    // Um log por posição (NN); o de repetição do surefire, se existe, é o que vale.
    static IEnumerable<string> EscolherLogs(string pasta) =>
        Directory.GetFiles(pasta, "*.log")
            .Where(f => NumeroDoLog().IsMatch(Path.GetFileName(f)))
            .GroupBy(f => Path.GetFileName(f)[..2])
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.OrderBy(f => f.EndsWith("-surefire.log", StringComparison.OrdinalIgnoreCase) ? 1 : 0).Last());

    static LogDeProjeto? Ler(string arquivo)
    {
        try
        {
            string projeto = "", pasta = "", branch = "";
            foreach (var linha in File.ReadLines(arquivo, Encoding.Latin1).Take(12))
            {
                if (Campo(linha, "Projeto") is { } p) projeto = p.Split("  (")[0].Trim();
                else if (Campo(linha, "Pasta") is { } d) pasta = d;
                else if (Campo(linha, "Branch") is { } b) branch = b.Replace("heads/", "").Replace("tags/", "");
            }

            if (projeto.Length == 0) return null;
            var (estado, segundos) = LerRodape(arquivo);
            return new LogDeProjeto(projeto, pasta, branch, estado, segundos);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static (EstadoDoEvento Estado, int Segundos) LerRodape(string arquivo)
    {
        using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fluxo.Seek(Math.Max(0, fluxo.Length - BytesDoRodape), SeekOrigin.Begin);
        using var leitor = new StreamReader(fluxo, Encoding.Latin1);
        var final = Rodape().Matches(leitor.ReadToEnd()).LastOrDefault();
        if (final is null) return (EstadoDoEvento.SemResultado, 0);
        var segundos = final.Groups["m"].Success ? int.Parse(final.Groups["m"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(final.Groups["s"].Value, CultureInfo.InvariantCulture) : 0;
        return (final.Groups["r"].Value == "SUCESSO" ? EstadoDoEvento.Ok : EstadoDoEvento.Erro, segundos);
    }

    static string? Campo(string linha, string nome) =>
        linha.StartsWith(nome, StringComparison.Ordinal) && linha.IndexOf(':') is var i and > 0 && linha[..i].Trim() == nome ? linha[(i + 1)..].Trim() : null;

    static string Rotulo(EstadoDoEvento estado) => estado switch { EstadoDoEvento.Ok => "ok", EstadoDoEvento.Erro => "erro", _ => "sem resultado" };

    static string Duracao(int s) => s >= 60 ? $"{s / 60}min {s % 60}s" : $"{s}s";

    sealed record LogDeProjeto(string Projeto, string Pasta, string Branch, EstadoDoEvento Estado, int Segundos);

    [GeneratedRegex(@"^\d\d-.+\.log$")]
    private static partial Regex NumeroDoLog();

    [GeneratedRegex(@"RESULTADO: (?<r>SUCESSO|ERRO|INTERROMPIDO)[^\r\n]*? em (?<m>\d+):(?<s>\d\d)")]
    private static partial Regex Rodape();
}
