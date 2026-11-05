using System.Globalization;
using System.Text.RegularExpressions;

namespace Menage.Core.Diario;

public static class ReconhecedorDeIssue
{
    /// <summary>
    /// Id da issue no texto (mensagem ou nome de branch). <paramref name="chaves"/>: "VENDAS,WMW"; vazio = qualquer CHAVE-123.
    /// </summary>
    public static string IssueDe(string? texto, string? chaves)
    {
        var ks = (chaves ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape).ToArray();
        var chave = ks.Length > 0 ? "(?:" + string.Join('|', ks) + ")" : "[A-Za-z][A-Za-z0-9]{1,9}";
        var m = Regex.Match(texto ?? "", "(?<![A-Za-z0-9])(" + chave + @"-\d+)(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
    }
}

public sealed record ColetaDoDia(IReadOnlyList<CommitDoDia> Commits, IReadOnlyList<string> Avisos);

/// <summary>Commits do dia (os seus) em todas as branches de todos os projetos. Só leitura.</summary>
public sealed class ColetorDoDiario(IGit git)
{
    /// <summary>
    /// Procura numa janela de 14 dias antes do dia e filtra pela data de <i>autoria</i>: o git filtra pela data do committer,
    /// que muda em rebase e cherry-pick, e o trabalho vale pela autoria.
    /// </summary>
    /// <param name="issueDaBranch">Opcional: (projeto, branch) -> id, quando nem a mensagem nem a branch citam a issue.</param>
    public async Task<ColetaDoDia> ColetarAsync(
        DateTime dia, ConfiguracaoDoDiario cfg, IEnumerable<Projeto> projetos, Func<string, string, string>? issueDaBranch = null, CancellationToken cancelar = default)
    {
        var lista = new List<CommitDoDia>();
        var avisos = new List<string>();
        var vistos = new HashSet<string>();
        var desde = dia.Date.AddDays(-14).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (var projeto in projetos)
        {
            var autor = await AutorDeAsync(projeto, cfg, cancelar);
            if (autor.Length == 0)
            {
                avisos.Add($"{projeto.Nome}: sem autor (configure o e-mail em Configurar), projeto ignorado.");
                continue;
            }

            var saida = await git.ExecutarBrutoAsync(
                Git.Em(projeto.Caminho).Comando("log").Opcao("--all").Opcao("--source").Opcao("--no-merges").Opcao("-i")
                    .Opcao("--author=" + autor).Opcao("--since=" + desde).Opcao("--format=@@%h%x1f%aI%x1f%s%x1f%S").Opcao("--numstat"),
                cancelar);
            if (!saida.Sucesso)
            {
                avisos.Add($"{projeto.Nome}: não foi possível ler o histórico ({saida.TextoErro.Trim()}).");
                continue;
            }

            Ler(saida.Linhas(), dia, cfg, projeto.Nome, issueDaBranch, vistos, lista);
        }

        return new ColetaDoDia([.. lista.OrderBy(c => c.Quando)], avisos);
    }

    async Task<string> AutorDeAsync(Projeto projeto, ConfiguracaoDoDiario cfg, CancellationToken cancelar)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Autor)) return cfg.Autor.Trim();
        var email = (await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("config").Argumento("user.email"), cancelar)).PrimeiraLinha().Trim();
        return email.Length > 0 ? email : Environment.GetEnvironmentVariable("GIT_AUTHOR_EMAIL") ?? "";
    }

    static void Ler(
        IReadOnlyList<string> linhas, DateTime dia, ConfiguracaoDoDiario cfg, string projeto,
        Func<string, string, string>? issueDaBranch, HashSet<string> vistos, List<CommitDoDia> destino)
    {
        CommitDoDia? atual = null;
        foreach (var linha in linhas)
        {
            if (linha.StartsWith("@@", StringComparison.Ordinal))
            {
                atual = Cabecalho(linha[2..].Split('\u001f'), dia, cfg, projeto, issueDaBranch, vistos);
                if (atual is not null) destino.Add(atual);
            }
            else if (atual is not null)
            {
                var t = linha.Split('\t');
                if (t.Length < 3) continue;
                _ = int.TryParse(t[0], CultureInfo.InvariantCulture, out var a); // "-" (binário) vira 0
                _ = int.TryParse(t[1], CultureInfo.InvariantCulture, out var d);
                atual.Linhas += a + d;
                atual.Arquivos.Add(t[2]);
            }
        }
    }

    static CommitDoDia? Cabecalho(
        string[] p, DateTime dia, ConfiguracaoDoDiario cfg, string projeto, Func<string, string, string>? issueDaBranch, HashSet<string> vistos)
    {
        if (p.Length < 4 || !DateTimeOffset.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var q)) return null;
        if (q.LocalDateTime.Date != dia.Date) return null;
        var branch = p[3].StartsWith("refs/heads/", StringComparison.Ordinal) ? p[3][11..]
            : p[3].StartsWith("refs/remotes/origin/", StringComparison.Ordinal) ? p[3][20..] : p[3];
        // cherry-pick mantém data e mensagem: conta uma vez
        if (!vistos.Add($"{projeto}|{q:o}|{p[2]}")) return null;
        var id = ReconhecedorDeIssue.IssueDe(p[2], cfg.ProjetosJira);
        if (id.Length == 0) id = ReconhecedorDeIssue.IssueDe(branch, cfg.ProjetosJira);
        if (id.Length == 0 && issueDaBranch is not null) id = issueDaBranch(projeto, branch);
        return new CommitDoDia { Projeto = projeto, Hash = p[0], Quando = q.LocalDateTime, Assunto = p[2], Branch = branch, Issue = id };
    }
}
