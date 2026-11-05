using System.Threading.Channels;
using Menage.Core.Beta;

namespace Menage.Core.BetaAutomatica;

/// <summary>
/// Geração: uma atividade por vez, na ordem em que entraram na fila. Cada uma prepara os clones isolados dos projetos que têm a branch
/// (ou a tag) e roda o Maven <b>install</b> (nunca deploy) pelo <see cref="ExecutorDoGerador"/>, na ordem de dependência.
/// Falha de preparo ou de Maven aparece na linha do projeto e a fila segue.
/// </summary>
public sealed partial class ServicoDeBetaAutomatica
{
    readonly PreparadorDeClone _clones;
    readonly PreparadorDeGeracao _preparador;
    readonly IProcessoRunner _runner;
    readonly Channel<string> _fila = Channel.CreateUnbounded<string>();
    readonly List<string> _naFila = [];
    Task? _trabalhador;
    string _gerando = "";
    CancellationTokenSource? _geracaoAtual;

    /// <summary>Quando há projeto fora da ordem padrão: a tela pergunta a ordem (devolve nulo se o usuário desistir).</summary>
    public Func<IReadOnlyList<string>, Task<IReadOnlyList<string>?>>? PerguntarOrdem { get; set; }

    /// <summary>Entrega a atividade à fila. Só as Prontas, Falhadas ou Concluídas podem ser geradas.</summary>
    public void Gerar(string chave)
    {
        lock (_trava)
        {
            if (Config.Achar(chave) is not { Estado: EstadoDoItem.Pronta or EstadoDoItem.Falhou or EstadoDoItem.Concluida } i) return;
            i.Estado = EstadoDoItem.NaFila;
            i.Erro = "";
            foreach (var p in i.Projetos) p.Resultado = "";
            _naFila.Add(i.Chave);
            Con(NivelDoConsole.Info, i.Chave, "", $"Gerar: atividade na fila (branch {i.Branch})");
        }

        Salvar();
        Notificar();
        _fila.Writer.TryWrite(chave);
        _trabalhador ??= Task.Run(TrabalharAsync);
    }

    async Task TrabalharAsync()
    {
        await foreach (var chave in _fila.Reader.ReadAllAsync())
        {
            ItemBeta? item;
            lock (_trava)
            {
                _naFila.Remove(chave);
                item = Config.Achar(chave);
                if (item is not { Estado: EstadoDoItem.NaFila }) continue;
                _gerando = chave;
                _geracaoAtual = new CancellationTokenSource();
            }

            var ok = false;
            try { ok = await GerarItemAsync(item, _geracaoAtual!.Token); }
            catch (Exception ex)
            {
                lock (_trava) { item.Estado = EstadoDoItem.Falhou; item.Erro = ex.Message; }
                Con(NivelDoConsole.Erro, item.Chave, "", "Geração falhou: " + ex.Message);
            }
            finally
            {
                lock (_trava) { _gerando = ""; _geracaoAtual?.Dispose(); _geracaoAtual = null; }
            }

            lock (_trava) item.GeradaEm = Agora();
            RegistrarLog((ok ? "CONCLUIDA " : "FALHOU ") + item.Chave + (ok ? "" : " - " + item.Erro));
            Salvar();
            Notificar();
            GeracaoConcluida?.Invoke(item, ok);
        }
    }

    async Task<bool> GerarItemAsync(ItemBeta item, CancellationToken cancelar)
    {
        var gerador = _gerador.Configuracao;
        await CompletarWebAsync(item, cancelar);
        var nomes = item.Projetos.Where(x => x.Gera && gerador.Projetos.Any(g => g.Nome.Equals(x.Nome, StringComparison.OrdinalIgnoreCase))).Select(x => x.Nome).ToList();
        if (nomes.Count == 0)
        {
            Con(NivelDoConsole.Erro, item.Chave, "", $"Nenhum projeto com a branch {item.Branch} para gerar.");
            lock (_trava) { item.Estado = EstadoDoItem.Falhou; item.Erro = $"Nenhum projeto com a branch {item.Branch} para gerar."; }
            return false;
        }

        var ordem = await DefinirOrdemAsync(nomes);
        if (ordem is null)
        {
            lock (_trava) { item.Estado = EstadoDoItem.Pronta; item.Erro = "Ordem dos projetos não definida; gere de novo."; }
            return false;
        }

        Con(NivelDoConsole.Info, item.Chave, "", "Lote montado: " + string.Join(", ", item.Projetos.Where(x => x.Gera).Select(x => x.Nome + (x.Complemento ? $" (tag {x.Tag})" : $" (branch {item.Branch})"))));
        foreach (var sem in item.Projetos.Where(x => x.Complemento && !x.Gera))
            Con(NivelDoConsole.Aviso, item.Chave, sem.Nome, "fora do lote: " + (sem.Detalhe.Length > 0 ? sem.Detalhe : "sem tag da versão"));

        var logs = Path.Combine(_pasta.Caminho, "logs-beta-auto", $"{item.Chave}_{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(Config.PastaTrabalho);
        lock (_trava) { item.Estado = EstadoDoItem.Gerando; item.Erro = ""; item.PastaLogs = logs; foreach (var p in item.Projetos) p.Resultado = ""; }
        RegistrarLog($"GERANDO {item.Chave}: {string.Join(" > ", ordem)}");
        Con(NivelDoConsole.Info, item.Chave, "", "Geração iniciada, ordem: " + string.Join(" > ", ordem));
        Salvar();
        Notificar();

        var prontos = await PrepararClonesAsync(item, ordem, gerador, logs, cancelar);
        if (prontos.Count == 0 || cancelar.IsCancellationRequested) return Concluir(item, prontos, null, cancelar.IsCancellationRequested);

        var pedido = await _preparador.MontarAsync(prontos, deploy: false, Config.PastaTrabalho, cancelar);
        if (!pedido.EhOk)
        {
            lock (_trava) { item.Estado = EstadoDoItem.Falhou; item.Erro = pedido.Erro.ToString(); }
            Con(NivelDoConsole.Erro, item.Chave, "", pedido.Erro.ToString());
            return false;
        }

        var executor = new ExecutorDoGerador(_runner);
        executor.StatusMudou += s => AoStatusDoProjeto(item, s);
        var resumo = await executor.ExecutarAsync(pedido.Valor with { PastaDeLogs = logs }, new SinaisDoLote(cancelar, CancellationToken.None));
        return Concluir(item, prontos, resumo, cancelar.IsCancellationRequested);
    }

    async Task<IReadOnlyList<string>?> DefinirOrdemAsync(List<string> nomes)
    {
        var ordem = OrdemDeProjetos.Ordenar(nomes, Config.OrdemProjetos, out var semOrdem);
        if (semOrdem.Count == 0 || PerguntarOrdem is null) return ordem;
        var escolhida = await PerguntarOrdem(ordem);
        if (escolhida is null) return null;
        lock (_trava) Config.OrdemProjetos = OrdemDeProjetos.Incorporar(Config.OrdemProjetos, escolhida);
        Salvar();
        return escolhida;
    }

    async Task<List<string>> PrepararClonesAsync(ItemBeta item, IReadOnlyList<string> ordem, ConfiguracaoDoGerador gerador, string logs, CancellationToken cancelar)
    {
        var prontos = new List<string>();
        foreach (var nome in ordem)
        {
            if (cancelar.IsCancellationRequested) break;
            var projeto = gerador.Projetos.First(g => g.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
            var no = item.Projetos.First(x => x.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
            var porTag = !no.TemBranch && no.Complemento && no.Tag.Length > 0;
            AoResultado(item, nome, ResultadoGerado.Gerando);
            await using var log = new StreamWriter(Path.Combine(logs, $"preparo-{nome}.log"), false, new System.Text.UTF8Encoding(true)) { AutoFlush = true };
            var r = await _clones.PrepararAsync(projeto, PastaLocal(nome), Config.PastaTrabalho, porTag ? no.Tag : item.Branch, porTag, item.Chave, log, cancelar);
            if (r.EhOk) prontos.Add(nome);
            else AoResultado(item, nome, ResultadoGerado.Falhou);
        }

        return prontos;
    }

    void AoStatusDoProjeto(ItemBeta item, StatusDoProjeto s)
    {
        switch (s.Estado)
        {
            case FaseDaGeracao.Executando or FaseDaGeracao.Repetindo: AoResultado(item, s.Projeto, ResultadoGerado.Gerando); break;
            case FaseDaGeracao.Ok: AoResultado(item, s.Projeto, ResultadoGerado.Ok); break;
            case FaseDaGeracao.Erro or FaseDaGeracao.Interrompido: AoResultado(item, s.Projeto, ResultadoGerado.Falhou); break;
        }

        if (s.Estado == FaseDaGeracao.Erro) Con(NivelDoConsole.Erro, item.Chave, s.Projeto, $"falhou: {s.Resumo}");
        else if (s.Estado == FaseDaGeracao.Ok) Con(NivelDoConsole.Info, item.Chave, s.Projeto, $"ok ({(int)s.Tempo.TotalSeconds} s)");
    }

    void AoResultado(ItemBeta item, string projeto, string resultado)
    {
        lock (_trava)
            if (item.Projetos.FirstOrDefault(x => x.Nome.Equals(projeto, StringComparison.OrdinalIgnoreCase)) is { } p) p.Resultado = resultado;
        Salvar();
        Notificar();
    }

    bool Concluir(ItemBeta item, IReadOnlyList<string> prontos, ResumoDaGeracao? resumo, bool cancelado)
    {
        lock (_trava)
        {
            foreach (var p in item.Projetos.Where(x => x.Gera && x.Resultado is "" or ResultadoGerado.Gerando)) p.Resultado = ResultadoGerado.Pulado;
            var falhou = item.Projetos.FirstOrDefault(p => p.Resultado == ResultadoGerado.Falhou);
            var ok = !cancelado && resumo is not null && falhou is null && prontos.Count > 0 && (resumo.Falhas + resumo.NaoExecutados) == 0;
            item.Estado = ok ? EstadoDoItem.Concluida : EstadoDoItem.Falhou;
            item.Erro = ok ? "" : cancelado ? "Cancelado pelo usuário." : falhou is not null ? $"Falhou em {falhou.Nome}." : "Nenhum projeto foi preparado para gerar.";
            return ok;
        }
    }

    /// <summary>"Cancelar" no cartão: só aquela atividade. Se ninguém a está gerando (presa), volta para Pronta.</summary>
    public void CancelarGeracao(string chave)
    {
        Con(NivelDoConsole.Aviso, chave, "", "Cancelar solicitado pelo usuário.");
        var rodando = false;
        lock (_trava)
        {
            if (_gerando == chave) { rodando = true; _geracaoAtual?.Cancel(); }
        }

        if (!rodando) VoltarParaPronta(chave, "Cancelado pelo usuário.");
    }

    /// <summary>"Cancelar todas": esvazia a fila e para a que estiver rodando.</summary>
    public void CancelarTodas()
    {
        Con(NivelDoConsole.Aviso, "", "", "Cancelar todas solicitado pelo usuário.");
        List<string> presas;
        lock (_trava)
        {
            _geracaoAtual?.Cancel();
            presas = [.. Config.Itens.Where(i => i.Estado is EstadoDoItem.NaFila or EstadoDoItem.Gerando && i.Chave != _gerando).Select(i => i.Chave)];
        }

        foreach (var c in presas) VoltarParaPronta(c, "Cancelado pelo usuário.");
    }

    public bool VoltarParaPronta(string chave, string? motivo)
    {
        lock (_trava)
        {
            if (Config.Achar(chave) is not { Estado: EstadoDoItem.NaFila or EstadoDoItem.Gerando } i) return false;
            i.Estado = EstadoDoItem.Pronta;
            i.Erro = motivo ?? "";
            foreach (var p in i.Projetos.Where(p => p.Resultado == ResultadoGerado.Gerando)) p.Resultado = "";
            RegistrarLog($"VOLTOU PARA PRONTA {chave}" + (string.IsNullOrEmpty(motivo) ? "" : " - " + motivo));
        }

        Salvar();
        Notificar();
        return true;
    }

    /// <summary>Ao abrir: nada está gerando ainda, então "Na fila"/"Gerando" que ficou salvo (programa fechado no meio) volta para Pronta.</summary>
    public int DestravarPresas()
    {
        List<string> presas;
        lock (_trava) presas = [.. Config.Itens.Where(i => i.Estado is EstadoDoItem.NaFila or EstadoDoItem.Gerando).Select(i => i.Chave)];
        foreach (var c in presas) VoltarParaPronta(c, "A geração anterior foi interrompida (o programa fechou no meio). Gere de novo.");
        return presas.Count;
    }
}
