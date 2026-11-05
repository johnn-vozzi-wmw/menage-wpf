using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.CherryPick;

namespace Menage.App.Modulos.CherryPick;

/// <summary>Um passo da linha do tempo: o item da fila e como ele se mostra.</summary>
public sealed class PassoDaFila(ItemDaFila item) : ObservableObject
{
    public ItemDaFila Item { get; } = item;

    public EstadoDoItem Estado => Item.Estado;

    public string Titulo => $"{Item.Curto}  {Item.Assunto}";

    public string Projeto => Item.Projeto;

    public string Situacao => Rotulo(Item.Estado) + (Item.Detalhe.Length > 0 && Item.Estado != EstadoDoItem.Pendente ? " - " + Item.Detalhe : "");

    public string Origem => Item.Origem.Length > 0 ? $"{Item.Autor} · {Item.Data} · {Item.Origem}" : $"{Item.Autor} · {Item.Data}";

    public bool Pendente => Item.Estado == EstadoDoItem.Pendente;

    public void Atualizar() => OnPropertyChanged(string.Empty);

    static string Rotulo(EstadoDoItem estado) => estado switch
    {
        EstadoDoItem.Pendente => "Pendente",
        EstadoDoItem.Aplicando => "Aplicando",
        EstadoDoItem.Aplicado => "Aplicado",
        EstadoDoItem.Pulado => "Pulado",
        EstadoDoItem.Conflito => "Conflito",
        EstadoDoItem.Erro => "Erro",
        _ => "Desfeito",
    };
}

/// <summary>
/// Janela da fila: monta, roda e acompanha a fila. Cada decisão depois de uma pausa vai para o <see cref="ControleDaFila"/>;
/// aqui só há o encadeamento tela, motor e aviso.
/// </summary>
public sealed partial class FilaViewModel : ObservableObject, IDisposable
{
    readonly ServicoDaFila _servico;
    readonly ControleDaFila _controle;
    readonly FilaDeCherryPickEmLote _operacao;
    readonly CoordenadorDeOperacoes _coordenador;
    readonly IServicoDeDialogos _dialogos;
    readonly SugestoesDeBranches _sugestoes;
    bool _carregando;

    [ObservableProperty]
    string _destino = "";

    [ObservableProperty]
    bool _backup = true;

    [ObservableProperty]
    bool _atualizarDestino = true;

    [ObservableProperty]
    int _total;

    [ObservableProperty]
    int _aplicados;

    [ObservableProperty]
    int _pulados;

    [ObservableProperty]
    int _pendentes;

    [ObservableProperty]
    double _progresso;

    [ObservableProperty]
    string _textoDoEstado = "";

    [ObservableProperty]
    PassoDaFila? _pausadoEm;

    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    bool _voltarAoAbortar = true;

    [ObservableProperty]
    bool _rodando;

    [ObservableProperty]
    PassoDaFila? _selecionado;

    public FilaViewModel(
        ServicoDaFila servico, ControleDaFila controle, FilaDeCherryPickEmLote operacao, CoordenadorDeOperacoes coordenador,
        IServicoDeDialogos dialogos, SugestoesDeBranches sugestoes)
    {
        (_servico, _controle, _operacao, _coordenador, _dialogos, _sugestoes) = (servico, controle, operacao, coordenador, dialogos, sugestoes);
        _servico.Mudou += AoMudar;
        _coordenador.Alterado += AoMudar;
        Sincronizar();
        _ = CarregarSugestoesAsync();
    }

    public ObservableCollection<PassoDaFila> Passos { get; } = [];

    public ObservableCollection<string> Sugestoes { get; } = [];

    /// <summary>Pede à janela o editor de conflito do módulo Diff para esta pasta.</summary>
    public event Action<string>? PediuResolverAqui;

    public bool Pausada => PausadoEm is not null;

    public bool PodeEditar => !Rodando && !Pausada;

    public bool PodeIniciar => !Rodando && !Pausada && Pendentes > 0 && Destino.Trim().Length > 0;

    public bool PodeLimpar => !Rodando && !Pausada && Total > 0;

    public bool EhErro => PausadoEm?.Estado == EstadoDoItem.Erro;

    public string ListaDeArquivos => PausadoEm is null ? "" : string.Join("\n", PausadoEm.Item.Arquivos);

    public void Dispose()
    {
        _servico.Mudou -= AoMudar;
        _coordenador.Alterado -= AoMudar;
    }

    public async Task IniciarAsync()
    {
        if (!PodeIniciar) return;
        Mensagem = "";
        var resultado = await _coordenador.ExecutarAsync(_operacao, new OpcoesDaFila(() => _coordenador.PararSolicitado));
        if (resultado is null) Mensagem = "Há outra operação em andamento.";
        else if (!resultado.EhOk && resultado.Erro != Erro.Cancelado) await _dialogos.MostrarErroAsync(resultado.Erro);
        Sincronizar();
    }

    public void PararApos() => _coordenador.PararApos();

    public void ResolverAqui()
    {
        if (PausadoEm is { } passo) PediuResolverAqui?.Invoke(passo.Item.Caminho);
    }

    public async Task JaResolviAsync()
    {
        var (resultado, texto) = await _controle.RetomarAsync();
        if (resultado == Retomada.Marcadores && await _dialogos.ConfirmarAsync(new Previa("Marcadores de conflito", texto + " Continuar mesmo assim?", [])))
            (resultado, texto) = await _controle.RetomarAsync(ignorarMarcadores: true);

        Mensagem = texto;
        if (resultado == Retomada.Pronto) await IniciarAsync();
    }

    public async Task PularAsync()
    {
        await _controle.PularAsync();
        await IniciarAsync();
    }

    public async Task TentarDeNovoAsync()
    {
        await _controle.TentarDeNovoAsync();
        await IniciarAsync();
    }

    public async Task AbortarAsync()
    {
        var detalhe = VoltarAoAbortar ? "Cada projeto volta ao HEAD de antes da fila (só se ainda estiver na branch destino)." : "Os commits já aplicados ficam como estão.";
        if (!await _dialogos.ConfirmarAsync(new Previa("Abortar a fila", "Encerra o cherry-pick em andamento e para a fila.", [detalhe]))) return;
        var relatorio = await _controle.AbortarAsync(VoltarAoAbortar);
        await _dialogos.InformarAsync("Fila abortada", string.Join("\n", relatorio));
        Sincronizar();
    }

    public void Remover(PassoDaFila? passo)
    {
        if (passo is not null) _servico.Remover(passo.Item);
    }

    public void Mover(PassoDaFila? passo, int direcao)
    {
        if (passo is not null && _servico.Mover(passo.Item, direcao)) Selecionado = passo;
    }

    public void Limpar() => _servico.Limpar();

    partial void OnDestinoChanged(string value) => Configurar();

    partial void OnBackupChanged(bool value) => Configurar();

    partial void OnAtualizarDestinoChanged(bool value) => Configurar();

    void Configurar()
    {
        if (!_carregando) _servico.Configurar(Destino, Backup, AtualizarDestino);
        OnPropertyChanged(nameof(PodeIniciar));
    }

    void AoMudar() => System.Windows.Application.Current.Dispatcher.InvokeAsync(Sincronizar, DispatcherPriority.Background);

    void Sincronizar()
    {
        var fila = _servico.Fila;
        _carregando = true;
        (Destino, Backup, AtualizarDestino) = (fila.Destino, fila.Backup, fila.AtualizarDestino);
        _carregando = false;
        SincronizarPassos(fila);
        (Total, Aplicados, Pulados, Pendentes) = (fila.Itens.Count, fila.Aplicados, fila.Pulados, fila.Pendentes);
        Progresso = Total == 0 ? 0 : 100.0 * (Aplicados + Pulados) / Total;
        Rodando = fila.Estado == EstadoDaFila.Rodando || _coordenador.EmAndamento;
        PausadoEm = fila.Estado == EstadoDaFila.Pausada ? Passos.FirstOrDefault(p => p.Estado is EstadoDoItem.Conflito or EstadoDoItem.Erro) : null;
        TextoDoEstado = Descrever(fila);
        foreach (var nome in new[] { nameof(Pausada), nameof(PodeEditar), nameof(PodeIniciar), nameof(PodeLimpar), nameof(EhErro), nameof(ListaDeArquivos) })
            OnPropertyChanged(nome);
    }

    // Mesma lista: só atualiza os passos (a animação do estado dispara); lista diferente: refaz.
    void SincronizarPassos(FilaDeCherryPick fila)
    {
        var igual = Passos.Count == fila.Itens.Count && Passos.Zip(fila.Itens).All(par => ReferenceEquals(par.First.Item, par.Second));
        if (igual)
        {
            foreach (var passo in Passos) passo.Atualizar();
            return;
        }

        var antes = Selecionado?.Item;
        Passos.Clear();
        foreach (var item in fila.Itens) Passos.Add(new PassoDaFila(item));
        Selecionado = Passos.FirstOrDefault(p => ReferenceEquals(p.Item, antes));
    }

    static string Descrever(FilaDeCherryPick fila) => fila.Estado switch
    {
        EstadoDaFila.Montando => fila.Itens.Count == 0 ? "Fila vazia" : "Pronta para rodar",
        EstadoDaFila.Rodando => "Rodando",
        EstadoDaFila.Pausada => "Pausada: conflito ou erro",
        EstadoDaFila.Parada => "Parada: continue quando quiser",
        EstadoDaFila.Concluida => "Concluída (nada foi enviado ao origin)",
        _ => "Abortada",
    };

    async Task CarregarSugestoesAsync()
    {
        var projetos = _servico.Fila.Itens.GroupBy(i => i.Caminho).Select(g => g.First().ComoProjeto()).ToList();
        if (projetos.Count == 0) return;
        var nomes = await _sugestoes.BasesAsync(projetos);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var n in nomes) Sugestoes.Add(n);
        });
    }
}
