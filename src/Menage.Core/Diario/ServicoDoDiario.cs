using System.Globalization;
using Menage.Core.Melhorias;

namespace Menage.Core.Diario;

public sealed class FabricaDeClientes(HttpClient http, IVariaveisDeAmbiente ambiente)
{
    public ClienteJira Jira(ConfiguracaoDoDiario cfg) => new(http, cfg, ambiente);

    public ClienteDeIa Ia(ConfiguracaoDoDiario cfg) => new(http, cfg.Ia, ambiente);
}

/// <summary>Uma linha da confirmação antes de enviar ao Jira.</summary>
public sealed record WorklogParaLancar(BlocoDeWorklog Bloco, string Linha);

public sealed record ResumoDoLancamento(int Lancados, int Total, IReadOnlyList<string> Linhas);

/// <summary>
/// O Diário de ponta a ponta: lê os commits do dia, distribui a jornada, resume (regras; IA só quando pedida) e lança no Jira
/// com confirmação. O que foi lançado fica em <c>diario.json</c>, então reabrir o dia não duplica.
/// </summary>
public sealed class ServicoDoDiario(
    IProjetos projetos, ColetorDoDiario coletor, LeitorDeDiffs leitor, RepositorioDoDiario repositorio, FabricaDeClientes clientes,
    RepositorioDeMelhorias? melhorias = null)
{
    /// <summary>Commit sem id na mensagem nem na branch: usa a melhoria cadastrada para aquela branch do projeto.</summary>
    string IssueDaMelhoria(string projeto, string branch) =>
        melhorias?.Config.Lista.FirstOrDefault(m => m.Do(projeto) is { } b && b.Branch.Equals(branch, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

    public ConfiguracaoDoDiario Configuracao => repositorio.Configuracao;

    public Resultado<Nada> SalvarModoPorCommit(bool ligado) => repositorio.Salvar(repositorio.Configuracao with { WorklogPorCommit = ligado });

    public Resultado<Nada> SalvarConfiguracao(ConfiguracaoDoDiario nova) => repositorio.Salvar(nova with { Lancamentos = repositorio.Configuracao.Lancamentos });

    public async Task<Resultado<ResultadoDoDiario>> CarregarDiaAsync(DateTime dia, CancellationToken cancelar = default)
    {
        if (projetos.Todos.Count == 0)
            return Resultado.Falha<ResultadoDoDiario>("Nenhum projeto configurado", null, "Adicione projetos na janela principal.");
        var coleta = await coletor.ColetarAsync(dia, Configuracao, projetos.Todos, melhorias is null ? null : IssueDaMelhoria, cancelar);
        var r = Redistribuir(dia, coleta.Commits);
        foreach (var a in coleta.Avisos) r.Avisos.Add(a);
        return Resultado.Ok(r);
    }

    /// <summary>Refaz a distribuição (ex.: depois de atribuir uma issue a um commit) e marca o que já foi lançado.</summary>
    public ResultadoDoDiario Redistribuir(DateTime dia, IReadOnlyList<CommitDoDia> commits)
    {
        var r = DistribuidorDeJornada.Distribuir(dia, commits, Configuracao);
        foreach (var b in r.Blocos)
            if (Configuracao.Lancado(b.Issue, dia, b.Inicio.ToString("HH:mm", CultureInfo.InvariantCulture)) is { } l)
            {
                (b.Lancado, b.Lancar, b.WorklogId) = (true, false, l.WorklogId);
                b.Aviso = "já lançado";
            }

        return r;
    }

    /// <summary>Lê o diff (só leitura local) dos commits do worklog; o resumo por regras passa a citar métodos e classes.</summary>
    public async Task<GrupoDeIssue?> PrepararGrupoAsync(ResultadoDoDiario r, BlocoDeWorklog bloco, CancellationToken cancelar = default)
    {
        var grupo = ResumoDoDiario.GrupoDoBloco(r, bloco);
        if (grupo is null) return null;
        var pastas = projetos.Todos.ToDictionary(p => p.Nome, p => p.Caminho, StringComparer.OrdinalIgnoreCase);
        await leitor.LerAsync(grupo.Commits, pastas, cancelar: cancelar);
        grupo.Resumo = ResumoDoDiario.PorRegras(grupo);
        return grupo;
    }

    /// <summary>Resumo por regras do worklog (volta ao texto sem IA).</summary>
    public async Task<string> ResumoPorRegrasAsync(ResultadoDoDiario r, BlocoDeWorklog bloco, CancellationToken cancelar = default) =>
        (await PrepararGrupoAsync(r, bloco, cancelar))?.Resumo ?? bloco.Comentario;

    /// <summary>Chama a IA UMA vez, só para este worklog. Nunca roda sozinha.</summary>
    public async Task<Resultado<ResumoDaIa>> ResumirComIaAsync(ResultadoDoDiario r, BlocoDeWorklog bloco, CancellationToken cancelar = default)
    {
        var cfg = Configuracao;
        var ia = clientes.Ia(cfg);
        if (!ia.Disponivel(out var motivo)) return Resultado.Falha<ResumoDaIa>(motivo, null, "Configure a IA em Configurar.");
        var grupo = await PrepararGrupoAsync(r, bloco, cancelar);
        if (grupo is null) return Resultado.Falha<ResumoDaIa>("Não há commits para resumir neste worklog");
        var resumo = await ia.ResumirAsync(grupo, cfg.EnviarDiff, cfg.MaxCharsDiff, cancelar);
        return resumo;
    }

    /// <summary>Busca o título de cada issue e avisa quando já há horas suas lançadas no dia. Precisa do token do Jira.</summary>
    public async Task<Resultado<Nada>> ConferirNoJiraAsync(ResultadoDoDiario r, CancellationToken cancelar = default)
    {
        var jira = clientes.Jira(Configuracao);
        foreach (var issue in r.Blocos.Select(b => b.Issue).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dados = await jira.IssueAsync(issue, cancelar);
            if (!dados.EhOk && dados.Erro.Explicacao is { } e && e.StartsWith("HTTP 404", StringComparison.Ordinal))
            {
                Marcar(r, issue, b => b.Aviso = "issue não encontrada no Jira");
                continue;
            }

            if (!dados.EhOk) return Resultado.Falha<Nada>(dados.Erro);
            var titulo = $"{dados.Valor.Titulo} [{dados.Valor.Status}]";
            var minutos = await jira.MinutosLancadosAsync(issue, r.Dia, cancelar);
            var aviso = minutos.EhOk && minutos.Valor > 0 ? $"você já tem {ConfiguracaoDoDiario.FormatarMin(minutos.Valor)} lançadas nesta issue hoje" : "";
            Marcar(r, issue, b => { b.TituloJira = titulo; if (!b.Lancado && aviso.Length > 0) b.Aviso = aviso; });
        }

        return Resultado.Ok();
    }

    static void Marcar(ResultadoDoDiario r, string issue, Action<BlocoDeWorklog> acao)
    {
        foreach (var b in r.Blocos.Where(b => string.Equals(b.Issue, issue, StringComparison.OrdinalIgnoreCase))) acao(b);
    }

    /// <summary>Os worklogs marcados que ainda não foram lançados, com a linha exata que o usuário confirma.</summary>
    public static IReadOnlyList<WorklogParaLancar> Pendentes(ResultadoDoDiario r) =>
        [.. r.Blocos.Where(b => b.Lancar && !b.Lancado).Select(b => new WorklogParaLancar(b,
            $"{b.Issue}  {b.Inicio:HH:mm}  {ConfiguracaoDoDiario.FormatarMin(b.Minutos)}  —  {Resumido(b.Comentario)}"))];

    static string Resumido(string comentario) => comentario.Length > 90 ? comentario[..90] + "..." : comentario;

    /// <summary>Primeiro problema que impede o envio (ou nulo): issue vazia, minutos zerados, comentário vazio.</summary>
    public static string? Validar(IEnumerable<WorklogParaLancar> pendentes)
    {
        foreach (var (b, _) in pendentes)
        {
            if (b.Issue.Length == 0 || b.Issue == GrupoDeIssue.SemIssue) return $"O worklog das {b.Inicio:HH:mm} está sem issue.";
            if (b.Minutos <= 0) return $"O worklog {b.Issue} das {b.Inicio:HH:mm} está com 0 minutos.";
            if (string.IsNullOrWhiteSpace(b.Comentario)) return $"O worklog {b.Issue} das {b.Inicio:HH:mm} está sem comentário.";
        }

        return null;
    }

    /// <summary>Envia os pendentes. Para no primeiro erro (o que já foi enviado fica registrado, então tentar de novo não duplica).</summary>
    public async Task<Resultado<ResumoDoLancamento>> LancarAsync(ResultadoDoDiario r, CancellationToken cancelar = default)
    {
        var pendentes = Pendentes(r);
        if (pendentes.Count == 0) return Resultado.Falha<ResumoDoLancamento>("Nada para lançar", null, "Marque ao menos um worklog ainda não lançado.");
        if (Validar(pendentes) is { } problema) return Resultado.Falha<ResumoDoLancamento>(problema);

        var jira = clientes.Jira(Configuracao);
        var linhas = new List<string>();
        foreach (var (b, _) in pendentes)
        {
            var id = await jira.LancarAsync(b.Issue, b.Inicio, b.Minutos, b.Comentario, cancelar);
            if (!id.EhOk)
                return Resultado.Falha<ResumoDoLancamento>($"Falhou em {b.Issue} das {b.Inicio:HH:mm}", $"{id.Erro} ({linhas.Count} de {pendentes.Count} já lançados.)");

            (b.Lancado, b.Lancar, b.WorklogId, b.Aviso) = (true, false, id.Valor, "lançado");
            var gravado = repositorio.RegistrarLancamento(new LancamentoDoDiario(
                b.Issue, r.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), b.Inicio.ToString("HH:mm", CultureInfo.InvariantCulture),
                b.Minutos, id.Valor, DateTime.Now.ToString("s", CultureInfo.InvariantCulture)));
            linhas.Add($"{b.Issue} {b.Inicio:HH:mm} ({ConfiguracaoDoDiario.FormatarMin(b.Minutos)}) → worklog {id.Valor}" + (gravado.EhOk ? "" : " (não foi possível registrar no diario.json)"));
        }

        return Resultado.Ok(new ResumoDoLancamento(linhas.Count, pendentes.Count, linhas));
    }

    /// <summary>Botão "Testar Jira": confirma o token e devolve o usuário.</summary>
    public async Task<Resultado<string>> TestarJiraAsync(CancellationToken cancelar = default) =>
        (await clientes.Jira(Configuracao).UsuarioAsync(cancelar)).Mapear(u => $"Conectado ao Jira como {u}.");

    /// <summary>Botão "Testar IA": uma chamada mínima, só quando clicado.</summary>
    public async Task<Resultado<string>> TestarIaAsync(CancellationToken cancelar = default) =>
        (await clientes.Ia(Configuracao).PerguntarAsync("Responda apenas: ok", "teste", cancelar)).Mapear(t => "A IA respondeu: " + ClienteDeIa.NormalizarTexto(t));
}
