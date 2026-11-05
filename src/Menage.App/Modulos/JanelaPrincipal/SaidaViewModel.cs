using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Saida;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>
/// A Saída (console): linhas por tipo, recolher/abrir (abre sozinha em erro), divisória arrastável e a altura que cabe à
/// grade e à Saída. Eventos de qualquer thread voltam para a tela pelo contexto de sincronização capturado na criação.
/// </summary>
public sealed partial class SaidaViewModel : ObservableObject, IDisposable
{
    const int MaximoDeLinhas = 5000;

    readonly SaidaDeOperacoes _saida;
    readonly PreferenciasDaJanelaServico _preferencias;
    readonly SynchronizationContext? _tela = SynchronizationContext.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alturas), nameof(AlturaDaGrade), nameof(AlturaDaSaida))]
    bool _recolhida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alturas), nameof(AlturaDaGrade), nameof(AlturaDaSaida))]
    double _alturaPreferida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alturas), nameof(AlturaDaGrade), nameof(AlturaDaSaida))]
    double _alturaDisponivel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alturas), nameof(AlturaDaGrade), nameof(AlturaDaSaida))]
    int _linhasDaGrade;

    [ObservableProperty]
    LinhaDeSaida? _ultimaLinha;

    public SaidaViewModel(SaidaDeOperacoes saida, PreferenciasDaJanelaServico preferencias)
    {
        (_saida, _preferencias) = (saida, preferencias);
        _recolhida = preferencias.Valor.SaidaRecolhida;
        _alturaPreferida = preferencias.Valor.AlturaDaSaida;
        foreach (var linha in saida.Linhas) Linhas.Add(linha);
        UltimaLinha = Linhas.LastOrDefault();
        saida.Adicionada += AoAdicionar;
        saida.Limpa += AoLimpar;
    }

    public ObservableCollection<LinhaDeSaida> Linhas { get; } = [];

    public AlturasDaTela Alturas => CalculoDeAlturas.Calcular(AlturaDisponivel, LinhasDaGrade, AlturaPreferida, Recolhida);

    public double AlturaDaGrade => Alturas.Grade;

    public double AlturaDaSaida => Alturas.Saida;

    /// <summary>Texto da Saída para o botão Copiar; a tela entrega à área de transferência.</summary>
    public event Action<string>? Copiar;

    public event Action? Rolar;

    public void Dispose()
    {
        _saida.Adicionada -= AoAdicionar;
        _saida.Limpa -= AoLimpar;
    }

    [RelayCommand]
    public void Alternar()
    {
        Recolhida = !Recolhida;
        _preferencias.Alterar(p => p with { SaidaRecolhida = Recolhida });
    }

    [RelayCommand]
    void CopiarTudo() => Copiar?.Invoke(_saida.Texto());

    [RelayCommand]
    void Limpar() => _saida.Limpar();

    /// <summary>A divisória foi arrastada: positivo = para baixo (Saída menor). Arrastar com a Saída recolhida a abre.</summary>
    public void Arrastar(double deslocamento)
    {
        if (Recolhida) Alternar();
        AlturaPreferida = CalculoDeAlturas.AposArrastar(Alturas.Saida, deslocamento, AlturaDisponivel);
    }

    public void ConcluirArrasto() => _preferencias.Alterar(p => p with { AlturaDaSaida = AlturaPreferida });

    void AoAdicionar(LinhaDeSaida linha) => NaTela(() =>
    {
        Linhas.Add(linha);
        while (Linhas.Count > MaximoDeLinhas) Linhas.RemoveAt(0);
        UltimaLinha = linha;
        if (linha.Tipo == TipoDeLinha.Erro && Recolhida) Alternar();
        Rolar?.Invoke();
    });

    void AoLimpar() => NaTela(() =>
    {
        Linhas.Clear();
        UltimaLinha = null;
    });

    void NaTela(Action acao)
    {
        if (_tela is null || SynchronizationContext.Current == _tela) acao();
        else _tela.Post(_ => acao(), null);
    }
}
