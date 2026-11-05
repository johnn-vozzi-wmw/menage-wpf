using System.Diagnostics;
using System.Globalization;

namespace Menage.Core.BetaAutomatica;

public enum Decisao { ForaDoStatus, ForaDoEscopo, Nova, Reentrada, Reavaliar, JaTratada, EmAndamento }

/// <summary>Decisão sobre uma atividade devolvida pelo Jira (a mesma no ciclo real e no diagnóstico, para nunca divergirem).</summary>
public sealed class AvaliacaoDeAtividade
{
    public Decisao Decisao { get; init; }

    public string Texto { get; init; } = "";

    public string TransicaoDe { get; init; } = "";

    public EscopoBeta? Escopo { get; init; }

    public DateTime? Transicao { get; init; }

    public bool SemAlerta { get; init; }
}

public sealed partial class ServicoDeBetaAutomatica
{
    public static string Min(DateTime? d) => d is null ? "-" : d.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    static DateTime? LerMin(string s)
    {
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
            ? new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0) : null;
    }

    static DateTime Truncar(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);

    EscopoBeta EscopoDe(IssueJira i, string eu) => ModoTester ? EscopoBeta.AvaliarTester(i, eu, Config) : EscopoBeta.Avaliar(i, eu, Config);

    public AvaliacaoDeAtividade Decidir(IssueJira i, ItemBeta? item, string eu, bool primeira, DateTime baseline)
    {
        if (!i.Status.Equals(Config.StatusAlvo, StringComparison.OrdinalIgnoreCase))
            return new() { Decisao = Decisao.ForaDoStatus, Texto = $"o status atual é \"{i.Status}\", não \"{Config.StatusAlvo}\"" };

        var m = i.UltimaPassagemPara(Config.StatusAlvo);
        var trans = m?.Quando;
        var de = m?.DeNome ?? "";
        if (item is not null) return DecidirComItem(i, item, eu, trans, de);

        // Toda atividade do escopo no status entra na lista; as que já estavam lá antes do monitor entram SEM alerta.
        var semAlerta = primeira || (trans is not null && trans.Value < baseline);
        var escopo = EscopoDe(i, eu);
        if (!escopo.Entra) return new() { Decisao = Decisao.ForaDoEscopo, Texto = escopo.Motivo, Escopo = escopo, Transicao = trans, TransicaoDe = de };
        return new()
        {
            Decisao = Decisao.Nova, Escopo = escopo, Transicao = trans, TransicaoDe = de, SemAlerta = semAlerta,
            Texto = escopo.Motivo + (semAlerta ? " (já estava neste status antes do monitor: entra sem alerta)" : ""),
        };
    }

    AvaliacaoDeAtividade DecidirComItem(IssueJira i, ItemBeta item, string eu, DateTime? trans, string de)
    {
        if (item.Estado is EstadoDoItem.Gerando or EstadoDoItem.NaFila)
            return new() { Decisao = Decisao.EmAndamento, Texto = "a geração está em andamento", Transicao = trans, TransicaoDe = de };

        var daItem = LerMin(item.TransicaoEm) ?? LerMin(item.DetectadaEm);
        var novaPassagem = trans is not null && (daItem is null || Truncar(trans.Value) > daItem.Value);
        if (novaPassagem)
        {
            var escopo = EscopoDe(i, eu);
            if (!escopo.Entra)
                return new() { Decisao = Decisao.ForaDoEscopo, Escopo = escopo, Transicao = trans, TransicaoDe = de, Texto = $"passou de novo para {Config.StatusAlvo} em {Min(trans)}, mas agora: {escopo.Motivo}" };
            return new()
            {
                Decisao = Decisao.Reentrada, Escopo = escopo, Transicao = trans, TransicaoDe = de,
                Texto = $"passou de novo para {Config.StatusAlvo} em {Min(trans)} (vinda de \"{de}\"); a anterior, de {Min(daItem)}, estava \"{ItemBeta.NomeDoEstado(item.Estado)}\": será reaberta como nova",
            };
        }

        if (item.Estado is EstadoDoItem.Aguardando or EstadoDoItem.Problema)
            return new() { Decisao = Decisao.Reavaliar, Escopo = EscopoDe(i, eu), Transicao = trans, TransicaoDe = de, Texto = $"estado \"{ItemBeta.NomeDoEstado(item.Estado)}\": confere de novo o comentário e a branch" };

        var desde = item.TransicaoEm.Length > 0 ? item.TransicaoEm : item.DetectadaEm;
        return new()
        {
            Decisao = Decisao.JaTratada, Transicao = trans, TransicaoDe = de,
            Texto = $"já está \"{ItemBeta.NomeDoEstado(item.Estado)}\" desde {desde} e não houve nova passagem para {Config.StatusAlvo} (para refazer: Reabrir/Gerar de novo, ou mova a atividade de novo)",
        };
    }

    /// <summary>Roda as consultas; uma que falha é ignorada e só derruba o ciclo se TODAS falharem.</summary>
    static async Task<List<IssueJira>> BuscarIssuesAsync(IClienteJira jira, IReadOnlyList<string> jqls, List<string>? relatorio, CancellationToken cancelar)
    {
        var issues = new List<IssueJira>();
        Exception? ultimo = null;
        var falhas = 0;
        for (var n = 0; n < jqls.Count; n++)
        {
            try
            {
                var r = await jira.BuscarCompletasAsync(jqls[n], 200, cancelar);
                relatorio?.Add($"JQL {n + 1}: {jqls[n]}\r\n   -> {r.Count} resultado(s)" + (r.Count > 0 ? ": " + string.Join(", ", r.Select(x => x.Chave)) : ""));
                issues.AddRange(r.Where(k => !issues.Any(x => x.Chave == k.Chave)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancelar.IsCancellationRequested)
            {
                falhas++;
                ultimo = ex;
                relatorio?.Add($"JQL {n + 1}: {jqls[n]}\r\n   -> ERRO: {ex.Message} (ignorada; segue com as outras)");
            }
        }

        if (falhas > 0 && falhas == jqls.Count) throw ultimo!;
        return issues;
    }

    /// <summary>Uma volta de consulta. <paramref name="importar"/> traz também o que já estava no status (hoje igual a uma consulta comum).</summary>
    public async Task<int> CicloAsync(bool importar, CancellationToken cancelar = default)
    {
        if (_ciclando) return 0;
        _ciclando = true;
        var novas = 0;
        var resumo = new List<string>();
        var papel = ModoTester ? "tester" : "dev";
        try
        {
            var jira = _fabrica.Criar(Config);
            if (!jira.TemToken)
            {
                var msg = $"A variável de ambiente {jira.NomeDaVariavel} (token do Jira) não está definida.";
                Falhou(msg);
                RegistrarLog("ERRO: " + msg);
                return 0;
            }

            var eu = await jira.UsuarioAsync(cancelar);
            var cron = Stopwatch.StartNew();
            _verificador.CacheDeFetch = true;
            RegistrarLog($"consulta iniciada ({papel})" + (importar ? " [importar]" : ""));
            var rel = new List<string>();
            var issues = await BuscarIssuesAsync(jira, GerenciadorDeJql.Consultas(Config, ModoTester), rel, cancelar);
            foreach (var r in rel) RegistrarLog(r.Replace("\r\n", " "));
            RegistrarLog($"busca no Jira concluída em {cron.ElapsedMilliseconds} ms");

            var primeira = Config.Baseline.Length == 0;
            var baseline = primeira ? DateTime.Now : LerMin(Config.Baseline) ?? DateTime.MinValue;
            foreach (var i in issues)
            {
                var (nova, linha) = await TratarAsync(i, eu, primeira, baseline, cancelar);
                novas += nova;
                resumo.Add(linha);
            }

            var finalizadas = await FinalizarPorStatusAsync(jira, issues, cancelar);
            if (finalizadas > 0) RegistrarLog($"{finalizadas} atividade(s) finalizada(s) automaticamente (foram para \"{Config.StatusFinal}\")");
            lock (_trava)
            {
                if (primeira) Config.Baseline = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                Config.UltimaConsulta = Agora();
                Config.UltimoErro = "";
            }

            RegistrarLog($"consulta ({papel}) concluída em {cron.ElapsedMilliseconds} ms: {issues.Count} atividade(s) no Jira" + (resumo.Count > 0 ? " -> " + string.Join("; ", resumo) : ""));
            Msg($"Última consulta {DateTime.Now:HH:mm:ss}" + (primeira ? " (referência inicial gravada: só o que mudar daqui para a frente gera alerta)" : "") + ".");
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Falhou("Beta automática: " + ex.Message);
            RegistrarLog("ERRO na consulta: " + ex.Message);
        }
        finally
        {
            _ciclando = false;
            _verificador.CacheDeFetch = false;
            Salvar();
            Notificar();
        }

        return novas;
    }

    async Task<(int Novas, string Linha)> TratarAsync(IssueJira i, string eu, bool primeira, DateTime baseline, CancellationToken cancelar)
    {
        var item = Achar(i.Chave);
        var a = Decidir(i, item, eu, primeira, baseline);
        RegistrarLog($"{i.Chave} -> {a.Decisao.ToString().ToUpperInvariant()} - {a.Texto}");
        var novas = 0;
        var conferir = false;
        var semAlerta = false;
        switch (a.Decisao)
        {
            case Decisao.Nova:
                lock (_trava)
                {
                    Config.Removidas.Remove($"{i.Chave}|{Min(a.Transicao)}");
                    item = new ItemBeta
                    {
                        Chave = i.Chave, Titulo = i.Titulo, Tipo = i.Tipo, Motivo = a.Texto, DetectadaEm = Agora(),
                        TransicaoEm = a.Transicao is null ? "" : Min(a.Transicao),
                    };
                    Config.Itens.Add(item);
                }

                novas++;
                AplicarComentario(item, a.Escopo?.Comentario);
                RegistrarLog($"NOVA {i.Chave} - {a.Texto}");
                conferir = true;
                semAlerta = a.SemAlerta;
                break;
            case Decisao.Reentrada:
                lock (_trava)
                {
                    item!.Estado = EstadoDoItem.Aguardando;
                    item.Erro = item.GeradaEm = item.PastaLogs = item.Branch = item.FinalizadaEm = item.FinalizadaMotivo = "";
                    item.Titulo = i.Titulo;
                    item.Tipo = i.Tipo;
                    item.Motivo = a.Escopo!.Motivo + " (nova passagem)";
                    item.DetectadaEm = Agora();
                    item.TransicaoEm = Min(a.Transicao);
                    item.TextoProjetos = [];
                    item.Projetos = [];
                }

                novas++;
                AplicarComentario(item, a.Escopo?.Comentario);
                RegistrarLog($"REENTRADA {i.Chave} - {a.Texto}");
                conferir = true;
                break;
            case Decisao.Reavaliar:
                AplicarComentario(item!, a.Escopo?.Comentario);
                conferir = true;
                break;
        }

        if (conferir && item is { Branch.Length: > 0 }) await VerificarAsync(item, !semAlerta, cancelar);
        return (novas, $"{i.Chave}:{a.Decisao}");
    }

    void AplicarComentario(ItemBeta item, ComentarioBeta? c)
    {
        lock (_trava)
        {
            if (c is not { Valido: true })
            {
                if (item.Branch.Length == 0) item.Estado = EstadoDoItem.Aguardando;
                return;
            }

            var mudou = item.Branch != c.Branch || !item.TextoProjetos.SequenceEqual(c.Projetos);
            item.Branch = c.Branch;
            item.TextoProjetos = c.Projetos;
            if (!mudou && item.Projetos.Count != 0) return;

            item.Projetos = LeitorDeComentario.Expandir(c.Projetos.Count > 0 ? c.Projetos : ["FRAMEWORK-XXXX", "LAVENDERE-XXXX"], ProjetosConhecidos());
            if (item.Estado is EstadoDoItem.Problema or EstadoDoItem.Aguardando) item.Estado = EstadoDoItem.Aguardando;
        }
    }

    string? PastaLocal(string nome) =>
        _projetos.Todos.FirstOrDefault(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase))?.Caminho;

    /// <summary>Confere a branch em cada projeto (sem checkout). Pronta se existir em algum; Problema se em nenhum.</summary>
    public async Task VerificarAsync(ItemBeta item, bool alertar = true, CancellationToken cancelar = default)
    {
        var eraPronta = item.Estado == EstadoDoItem.Pronta;
        lock (_trava) item.Projetos.RemoveAll(p => p.Complemento);
        foreach (var p in item.Projetos.ToList())
        {
            var pasta = PastaLocal(p.Nome);
            if (pasta is null) { p.Situacao = SituacaoDaBranch.SemRepo; p.Detalhe = "projeto não configurado neste computador"; continue; }
            var s = await _verificador.BranchAsync(pasta, item.Branch, cancelar);
            p.Situacao = s.Situacao;
            p.Detalhe = s.Detalhe;
        }

        await CompletarWebAsync(item, cancelar);
        foreach (var pv in item.Projetos)
            Con(pv.Gera ? NivelDoConsole.Info : NivelDoConsole.Aviso, item.Chave, pv.Nome,
                $"branch {item.Branch}: {pv.Situacao}" + (pv.Complemento && pv.Gera ? $" -> tag {pv.Tag}" : "") + (pv.Detalhe.Length > 0 ? $" ({pv.Detalhe})" : ""));

        lock (_trava)
        {
            var faltam = item.Projetos.Where(p => p.Explicito && !p.TemBranch).Select(p => p.Nome).ToList();
            if (item.Projetos.Any(p => p.TemBranch))
            {
                item.Estado = EstadoDoItem.Pronta;
                item.Erro = faltam.Count > 0 ? $"A branch {item.Branch} não foi achada em: {string.Join(", ", faltam)} (citado no comentário)." : "";
                var semTag = item.Projetos.Where(p => p.Complemento && !p.Gera).Select(p => p.Nome + (p.Detalhe.Length > 0 ? $" ({p.Detalhe})" : "")).ToList();
                if (semTag.Count > 0) item.Erro += (item.Erro.Length > 0 ? " " : "") + "Beta web incompleta: sem a tag da versão em " + string.Join(", ", semTag) + ".";
            }
            else
            {
                item.Estado = EstadoDoItem.Problema;
                item.Erro = $"A branch {item.Branch} não existe em nenhum dos projetos conferidos.";
            }
        }

        Con(item.Estado == EstadoDoItem.Problema ? NivelDoConsole.Erro : item.Erro.Length > 0 ? NivelDoConsole.Aviso : NivelDoConsole.Info,
            item.Chave, "", $"Verificação: {ItemBeta.NomeDoEstado(item.Estado)}" + (item.Erro.Length > 0 ? " - " + item.Erro : ""));
        Salvar();
        Notificar();
        if (alertar && !eraPronta && item.Estado == EstadoDoItem.Pronta) ItemPronto?.Invoke(item);
    }

    static readonly string[] FamiliaWeb = ["lavenderecore", "lavenderedados", "lavendereweb"];

    /// <summary>
    /// Beta web completa precisa de core, dados e web, mas o dev cria branch só nos que alterou. Os que ficaram sem branch (e não foram
    /// citados) entram pela TAG da versão do pom.xml do projeto web que TEM a branch (9.060-SNAPSHOT -> tag 9.060). Só leitura.
    /// </summary>
    public async Task CompletarWebAsync(ItemBeta item, CancellationToken cancelar = default)
    {
        lock (_trava) item.Projetos.RemoveAll(p => p.Complemento);
        var referencia = FamiliaWeb.Select(n => item.Projetos.FirstOrDefault(p => p.TemBranch && p.Nome.Equals(n, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(p => p is not null);
        if (referencia is null) return;

        var pastaRef = PastaLocal(referencia.Nome);
        var versao = pastaRef is null ? null : await _verificador.VersaoDaBranchAsync(pastaRef, item.Branch, cancelar);
        var tag = versao is null ? "" : System.Text.RegularExpressions.Regex.Replace(versao.Trim(), "(?i)-SNAPSHOT$", "");
        foreach (var nome in FamiliaWeb.Where(n => !n.Equals(referencia.Nome, StringComparison.OrdinalIgnoreCase)))
        {
            var existente = item.Projetos.FirstOrDefault(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
            if (existente is not null && (existente.TemBranch || existente.Explicito)) continue;
            var pasta = PastaLocal(nome);
            if (pasta is null) continue;

            var complemento = existente;
            if (complemento is null)
            {
                complemento = new ProjetoDoItem { Nome = nome };
                lock (_trava) item.Projetos.Add(complemento);
            }

            complemento.Complemento = true;
            complemento.Tag = "";
            if (tag.Length == 0)
            {
                complemento.Situacao = SituacaoDaBranch.Ausente;
                complemento.Detalhe = $"não achei a versão no pom.xml de {referencia.Nome} na branch {item.Branch}";
                continue;
            }

            var s = await _verificador.TagAsync(pasta, tag, cancelar);
            if (s.Existe)
            {
                complemento.Situacao = SituacaoDaBranch.Tag;
                complemento.Tag = tag;
                complemento.Detalhe = $"tag {tag} (versão do pom.xml de {referencia.Nome}), {s.Detalhe}";
            }
            else
            {
                complemento.Situacao = s.Situacao;
                complemento.Detalhe = s.Detalhe;
            }
        }
    }

    /// <summary>Atividades da lista que NÃO vieram na busca: pergunta ao Jira o status atual delas (50 por chamada).</summary>
    async Task<Dictionary<string, IssueJira>> ConsultarForaDaBuscaAsync(IClienteJira jira, IReadOnlyList<IssueJira> issues, CancellationToken cancelar)
    {
        var r = new Dictionary<string, IssueJira>(StringComparer.OrdinalIgnoreCase);
        List<string> chaves;
        lock (_trava)
            chaves = [.. Config.Itens.Where(x => x.Estado is not (EstadoDoItem.Finalizada or EstadoDoItem.Gerando or EstadoDoItem.NaFila) && !issues.Any(y => y.Chave.Equals(x.Chave, StringComparison.OrdinalIgnoreCase))).Select(x => x.Chave)];
        foreach (var lote in chaves.Chunk(50))
        {
            try { foreach (var j in await jira.BuscarCompletasAsync($"key in ({string.Join(",", lote)})", 50, cancelar)) r[j.Chave] = j; }
            catch (HttpRequestException ex) { RegistrarLog("aviso: não consegui consultar o status das atividades fora da busca: " + ex.Message); }
        }

        return r;
    }

    /// <summary>Nulo = não está no status final; senão o texto do motivo.</summary>
    public string? MotivoDeFinalizacao(IssueJira j)
    {
        if (!j.Status.Equals(Config.StatusFinal, StringComparison.OrdinalIgnoreCase)) return null;
        var m = j.UltimaPassagemPara(Config.StatusFinal);
        return $"automático: foi para \"{Config.StatusFinal}\"" + (m is null ? "" : $" (vinda de \"{m.DeNome}\" em {Min(m.Quando)})");
    }

    async Task<int> FinalizarPorStatusAsync(IClienteJira jira, IReadOnlyList<IssueJira> issues, CancellationToken cancelar)
    {
        var n = 0;
        foreach (var (chave, issue) in await ConsultarForaDaBuscaAsync(jira, issues, cancelar))
        {
            var motivo = MotivoDeFinalizacao(issue);
            var item = Achar(chave);
            if (motivo is null || item is null || item.Estado is EstadoDoItem.Finalizada or EstadoDoItem.Gerando or EstadoDoItem.NaFila) continue;
            MarcarFinalizada(item, motivo);
            n++;
        }

        return n;
    }

    void MarcarFinalizada(ItemBeta item, string motivo)
    {
        lock (_trava)
        {
            item.EstadoAntes = item.Estado;
            item.Estado = EstadoDoItem.Finalizada;
            item.FinalizadaEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            item.FinalizadaMotivo = motivo;
        }

        RegistrarLog($"FINALIZADA {item.Chave} - {motivo}");
    }
}
