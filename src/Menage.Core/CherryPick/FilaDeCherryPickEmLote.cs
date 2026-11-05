namespace Menage.Core.CherryPick;

/// <param name="PararSolicitado">"Parar após o atual": consultado entre um commit e outro.</param>
public sealed record OpcoesDaFila(Func<bool>? PararSolicitado = null);

public sealed record ResultadoDaFila(int Aplicados, int Pulados, bool Pausou);

/// <summary>
/// Roda a fila gravada em <see cref="ServicoDaFila"/>, um projeto por vez e um commit por vez, e grava a cada passo. Conflito ou erro
/// pausa a fila inteira deixando o git em cherry-pick em andamento. Nada é enviado ao origin.
/// </summary>
public sealed class FilaDeCherryPickEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, ServicoDaFila servico, PreparoDoProjeto preparo, AplicadorDaFila aplicador)
    : OperacaoEmLote<OpcoesDaFila, ResultadoDaFila>(projetos, dialogos, progresso)
{
    public override string Id => "git.cherry-pick-fila";

    public override string Nome => "Fila de cherry-pick";

    public override IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam => ConjuntosDePerfis.Somente(PerfilDeUso.Geral, PerfilDeUso.Desenvolvedor);

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDaFila opcoes) =>
        [.. servico.Fila.Itens.Where(i => !i.Resolvido).GroupBy(i => i.Caminho, StringComparer.OrdinalIgnoreCase).Select(g => g.First().ComoProjeto())];

    protected override Erro? Validar(OpcoesDaFila opcoes, IReadOnlyList<Projeto> alvo)
    {
        if (servico.Rodando) return new Erro("A fila já está rodando");
        if (alvo.Count == 0) return new Erro("A fila não tem commits pendentes", null, "Adicione commits pela janela Cherry-pick.");
        return string.IsNullOrWhiteSpace(servico.Fila.Destino) ? new Erro("Informe a branch destino", null, "Preencha o destino da fila.") : null;
    }

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDaFila opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar)
    {
        var fila = servico.Fila;
        if (fila.Estado is EstadoDaFila.Concluida or EstadoDaFila.Abortada) fila.Estado = EstadoDaFila.Montando;
        fila.Estado = EstadoDaFila.Rodando;
        servico.Salvar();
        return Task.FromResult<Previa?>(null);
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDaFila>> ExecutarEmProjeto(Projeto projeto, OpcoesDaFila opcoes, CancellationToken cancelar)
    {
        var fila = servico.Fila;
        var itens = fila.Itens.Where(i => !i.Resolvido && string.Equals(i.Caminho, projeto.Caminho, StringComparison.OrdinalIgnoreCase)).ToList();
        if (itens.Count == 0) return ResultadoDoProjeto<ResultadoDaFila>.Aviso("nada pendente", new(0, 0, false));

        int aplicados = 0, pulados = 0;
        foreach (var item in itens)
        {
            if (opcoes.PararSolicitado?.Invoke() == true || cancelar.IsCancellationRequested)
                return Parar(fila, aplicados, pulados);

            (item.Estado, item.Detalhe, item.Arquivos) = (EstadoDoItem.Aplicando, "", []);
            fila.Atual = fila.Itens.IndexOf(item);
            servico.Salvar();

            if (await preparo.PrepararAsync(fila, projeto, cancelar) is { } motivo) (item.Estado, item.Detalhe) = (EstadoDoItem.Erro, motivo);
            else await aplicador.AplicarAsync(item, cancelar);

            if (item.Estado is EstadoDoItem.Conflito or EstadoDoItem.Erro) return Pausar(fila, item, aplicados, pulados);
            if (item.Estado == EstadoDoItem.Aplicado) aplicados++;
            else pulados++;
            fila.Atual = fila.Itens.IndexOf(item) + 1;
            servico.Salvar();
        }

        return ResultadoDoProjeto<ResultadoDaFila>.Ok(new(aplicados, pulados, false), $"{aplicados} aplicado(s), {pulados} pulado(s)");
    }

    protected override Task<ConclusaoDoLote<ResultadoDaFila>> ConcluirAsync(
        OpcoesDaFila opcoes, IReadOnlyList<ItemDoLote<ResultadoDaFila>> itens, bool interrompido, CancellationToken cancelar)
    {
        var fila = servico.Fila;
        if (fila.Estado == EstadoDaFila.Rodando) fila.Estado = fila.Pendentes == 0 ? EstadoDaFila.Concluida : EstadoDaFila.Parada;
        servico.Salvar();
        var observacao = new ObservacaoDoLote("Os commits foram aplicados apenas localmente. Use o push quando quiser publicar.");
        return Task.FromResult(new ConclusaoDoLote<ResultadoDaFila> { Observacoes = [observacao] });
    }

    ResultadoDoProjeto<ResultadoDaFila> Pausar(FilaDeCherryPick fila, ItemDaFila item, int aplicados, int pulados)
    {
        fila.Estado = EstadoDaFila.Pausada;
        servico.Salvar();
        var dados = new ResultadoDaFila(aplicados, pulados, true);
        return (ResultadoDoProjeto<ResultadoDaFila>.Falha(new Erro($"{item.Curto} - {item.Detalhe} (fila pausada)"), dados)) with { InterromperLote = true };
    }

    ResultadoDoProjeto<ResultadoDaFila> Parar(FilaDeCherryPick fila, int aplicados, int pulados)
    {
        fila.Estado = EstadoDaFila.Parada;
        servico.Salvar();
        return ResultadoDoProjeto<ResultadoDaFila>.Aviso("fila parada pelo usuário", new(aplicados, pulados, false)) with { InterromperLote = true };
    }
}
