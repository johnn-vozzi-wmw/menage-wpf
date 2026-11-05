using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Menage.Core.BetaAutomatica;

public sealed partial class ServicoDeBetaAutomatica
{
    /// <summary>Roda uma JQL (já expandida) só para ver o resultado: nada é alterado. Devolve as atividades ou o erro.</summary>
    public async Task<Resultado<IReadOnlyList<IssueJira>>> TestarJqlAsync(string jqlExpandida, CancellationToken cancelar = default)
    {
        try
        {
            var jira = _fabrica.Criar(Config);
            return jira.TemToken
                ? Resultado.Ok(await jira.BuscarCompletasAsync(jqlExpandida, 100, cancelar))
                : Resultado.Falha<IReadOnlyList<IssueJira>>($"A variável de ambiente {jira.NomeDaVariavel} (token do Jira) não está definida.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Resultado.Falha<IReadOnlyList<IssueJira>>(ex.Message);
        }
    }

    /// <summary>
    /// SIMULA a busca (mesmas consultas e regras do ciclo) e relata o que ela encontraria e por quê. Não altera nada: não grava itens,
    /// referência nem "vistos", não alerta e não faz fetch (confere a branch com ls-remote).
    /// </summary>
    public async Task<string> DiagnosticarAsync(Action<string>? aoLinha = null, CancellationToken cancelar = default)
    {
        var sb = new StringBuilder();
        var cron = Stopwatch.StartNew();
        void L(string t)
        {
            lock (sb) sb.Append(t).Append("\r\n");
            aoLinha?.Invoke(t);
        }

        RegistrarLog("[SIMULAÇÃO] iniciada");
        L($"=== DIAGNÓSTICO DA BUSCA NO JIRA (simulação: nada é alterado) - {DateTime.Now:dd/MM/yyyy HH:mm:ss} ===");
        try
        {
            var jira = _fabrica.Criar(Config);
            L($"Jira: {jira.Endereco}   token ({jira.NomeDaVariavel}): {(jira.TemToken ? "definido" : "NÃO definido")}");
            if (!jira.TemToken)
            {
                L($"\r\nSem token não há como consultar. Defina a variável de ambiente {jira.NomeDaVariavel} e reabra o programa.");
                return sb.ToString();
            }

            var eu = await jira.UsuarioAsync(cancelar);
            L("Usuário do token: " + eu);
            L("Papel: " + (ModoTester
                ? $"Tester (escopo: {(Config.TesterEscopo.Equals("todas", StringComparison.OrdinalIgnoreCase) ? "qualquer atividade" : "somente atribuídas a mim")}; precisa vir de \"{Config.StatusCodeReview}\")"
                : $"Desenvolvedor (minhas subtasks Coding, comentários meus com PROJETOS ALTERADOS/BRANCH, ou passagem minha de \"{Config.StatusDev}\" para \"{Config.StatusRevisao}\")"));
            L($"Status que dispara a beta: \"{Config.StatusAlvo}\"   Projetos do Jira: {(Config.ChavesJira.Length > 0 ? Config.ChavesJira : "(todos)")}   Monitor: {(Config.Ativo ? "ativo" : "pausado")}{(Rodando ? "" : " (não iniciado nesta sessão)")}");
            var primeira = Config.Baseline.Length == 0;
            var baseline = primeira ? DateTime.Now : LerMin(Config.Baseline) ?? DateTime.MinValue;
            L($"Referência (baseline): {(primeira ? "ainda não gravada (a 1ª consulta real só grava)" : Config.Baseline)}   Última consulta real: {(Config.UltimaConsulta.Length > 0 ? Config.UltimaConsulta : "nunca")}{(Config.UltimoErro.Length > 0 ? "   Último erro: " + Config.UltimoErro : "")}");
            L("");

            var relatorio = new List<string>();
            _verificador.CacheDeFetch = true;
            var issues = await BuscarIssuesAsync(jira, GerenciadorDeJql.Consultas(Config, ModoTester), relatorio, cancelar);
            L($"(busca no Jira em {cron.ElapsedMilliseconds} ms)");
            foreach (var r in relatorio) L(r);
            L("");

            var contagem = new Dictionary<Decisao, int>();
            foreach (var i in issues) await DiagnosticarAtividadeAsync(i, eu, primeira, baseline, contagem, L, cancelar);
            await ListarForaDaBuscaAsync(jira, issues, L, cancelar);
            L($"=== RESUMO: {issues.Count} atividade(s) devolvida(s) pelo Jira, simulação em {cron.ElapsedMilliseconds} ms" +
              (contagem.Count > 0 ? ": " + string.Join("; ", contagem.Select(k => $"{k.Value} {k.Key.ToString().ToUpperInvariant()}")) : "") + " ===");
            L("NOVA/REENTRADA = entram na fila e alertam; REAVALIAR = já na fila, confere comentário/branch; JATRATADA = já na fila sem nova passagem; FORADOESCOPO / FORADOSTATUS = não entram (o motivo está em cada atividade).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            L($"\r\nERRO durante a simulação: {ex.Message}");
        }
        finally
        {
            _verificador.CacheDeFetch = false;
        }

        RegistrarLog($"[SIMULAÇÃO] concluída em {cron.ElapsedMilliseconds} ms");
        return sb.ToString();
    }

    async Task DiagnosticarAtividadeAsync(
        IssueJira i, string eu, bool primeira, DateTime baseline, Dictionary<Decisao, int> contagem, Action<string> L, CancellationToken cancelar)
    {
        var item = Achar(i.Chave);
        var a = Decidir(i, item, eu, primeira, baseline);
        contagem[a.Decisao] = contagem.GetValueOrDefault(a.Decisao) + 1;
        L($"--- {i.Chave}  \"{i.Titulo}\"");
        L($"    tipo: {i.Tipo}{(i.EhSubtask ? " (subtask)" : "")}   status: {i.Status}   responsável: {(i.Responsavel.Length > 0 ? i.Responsavel : "(ninguém)")}");
        L($"    última passagem para \"{Config.StatusAlvo}\": {(a.Transicao is not null ? $"{Min(a.Transicao)} (vinda de \"{a.TransicaoDe}\")" : "nenhuma encontrada no histórico")}");
        L("    no programa: " + (item is null ? "não existe na lista" : $"\"{ItemBeta.NomeDoEstado(item.Estado)}\" (passagem registrada: {(item.TransicaoEm.Length > 0 ? item.TransicaoEm : "-")}, branch: {(item.Branch.Length > 0 ? item.Branch : "-")})"));
        L($"    DECISÃO: {a.Decisao.ToString().ToUpperInvariant()} - {a.Texto}");
        if (a.Escopo is { } escopo)
        {
            L($"    escopo: {(escopo.Entra ? "ENTRA" : "FORA")} - {escopo.Motivo}");
            if (escopo.Comentario is { Valido: true } c) await DiagnosticarBranchAsync(i, escopo, c, L, cancelar);
            else if (escopo.Entra) L($"    comentário: nenhum com BRANCH{(ModoTester ? " (de qualquer autor)" : " seu")} -> ficaria \"Aguardando branch\" (reavaliado a cada consulta)");
        }

        L("");
    }

    async Task DiagnosticarBranchAsync(IssueJira i, EscopoBeta escopo, ComentarioBeta c, Action<string> L, CancellationToken cancelar)
    {
        L($"    comentário lido: BRANCH {c.Branch}{(c.PelaChave ? " (nome da atividade: nada no comentário indicou a branch)" : "")} | PROJETOS: {(c.Projetos.Count > 0 ? string.Join(", ", c.Projetos) : "(não informados: confere todos)")}");
        if (!escopo.Entra) return;

        var projs = LeitorDeComentario.Expandir(c.Projetos.Count > 0 ? c.Projetos : ["FRAMEWORK-XXXX", "LAVENDERE-XXXX"], ProjetosConhecidos());
        var com = 0;
        foreach (var p in projs)
        {
            var pasta = PastaLocal(p.Nome);
            var s = pasta is null ? new SituacaoVerificada(SituacaoDaBranch.SemRepo, "projeto não configurado neste computador") : await _verificador.BranchSemEfeitoAsync(pasta, c.Branch, cancelar);
            p.Situacao = s.Situacao;
            p.Detalhe = s.Detalhe;
            if (s.Existe) com++;
            if (p.Explicito || s.Existe) L($"      {(s.Existe ? "[ok]" : "[--]")} {p.Nome}{(p.Explicito ? " (citado)" : " (curinga)")} - {s.Detalhe}");
        }

        if (com > 0)
        {
            var temp = new ItemBeta { Chave = i.Chave, Branch = c.Branch, Projetos = projs };
            await CompletarWebAsync(temp, cancelar);
            foreach (var cp in temp.Projetos.Where(x => x.Complemento))
                L($"      {(cp.Gera ? "[tag]" : "[--]")} {cp.Nome} (complemento da beta web) - {(cp.Detalhe.Length > 0 ? cp.Detalhe : "sem tag")}");
        }

        L($"    verificação (sem alterar nada): a branch existe em {com} projeto(s) -> ficaria {(com > 0 ? "\"Pronta para gerar\" e alertaria" : "\"Precisa de atenção\"")}");
    }

    async Task ListarForaDaBuscaAsync(IClienteJira jira, IReadOnlyList<IssueJira> issues, Action<string> L, CancellationToken cancelar)
    {
        List<ItemBeta> fora;
        lock (_trava) fora = [.. Config.Itens.Where(x => !issues.Any(y => y.Chave == x.Chave))];
        if (fora.Count == 0) return;

        var status = await ConsultarForaDaBuscaAsync(jira, issues, cancelar);
        L("--- Atividades que estão na lista do programa mas NÃO vieram nesta busca (já saíram do status, ou fora do filtro/prazo de 14 dias):");
        foreach (var x in fora)
        {
            status.TryGetValue(x.Chave, out var sj);
            var fin = sj is null ? null : MotivoDeFinalizacao(sj);
            L($"    {x.Chave} - \"{ItemBeta.NomeDoEstado(x.Estado)}\" (passagem registrada: {(x.TransicaoEm.Length > 0 ? x.TransicaoEm : "-")})" +
              (sj is not null ? $"   status atual no Jira: \"{sj.Status}\"" : "") + (fin is not null ? $"   -> FINALIZARIA ({fin})" : ""));
        }

        L("");
    }
}
