using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Menage.App.Animacoes;
using Menage.App.Tema;
using Menage.Core.Transicoes;

namespace Menage.App.Controles;

/// <summary>Paleta Ctrl+K: painel da própria janela (como no VS Code), lendo o catálogo de comandos.</summary>
public partial class PaletaDeComandos : UserControl
{
    public PaletaDeComandos()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Ligar(e.OldValue as PaletaDeComandosViewModel, e.NewValue as PaletaDeComandosViewModel);
    }

    // Painel deslizando 8 px para baixo ao abrir e voltando ao fechar; some (Collapsed) só quando a saída termina.
    static readonly EstadoVisual Fora = new(0, -8, 0.96);
    AnimadorDeElemento? _animador;
    bool _fechando;

    ServicoDeTransicao Transicao => HospedeDeTransicao.GetServico(this) ?? _padrao;

    static readonly ServicoDeTransicao _padrao = new();

    AnimadorDeElemento Animador => _animador ??= Transicao.CriarAnimador(this, vertical: true);

    public bool Aberta => Visibility == Visibility.Visible && !_fechando;

    PaletaDeComandosViewModel? Modelo => DataContext as PaletaDeComandosViewModel;

    /// <summary>Com a paleta já aberta, só devolve o foco à busca.</summary>
    public void Abrir()
    {
        if (!Aberta)
        {
            Modelo?.Reiniciar();
            _fechando = false;
            Visibility = Visibility.Visible;
            _ = ServicoDeTransicao.EntrarAsync(Animador, Fora);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Campo.Focus();
            Keyboard.Focus(Campo);
            Campo.SelectAll();
        });
    }

    public async void Fechar()
    {
        if (!Aberta) return;
        _fechando = true;
        // Reabrir no meio da saída cancela este pedido (devolve false) e o painel segue aberto.
        if (await ServicoDeTransicao.SairAsync(Animador, Fora) && _fechando)
        {
            Visibility = Visibility.Collapsed;
            _fechando = false;
        }
    }

    public bool Contem(DependencyObject? elemento) => elemento is Visual or System.Windows.Media.Media3D.Visual3D && IsAncestorOf(elemento);

    void Ligar(PaletaDeComandosViewModel? antigo, PaletaDeComandosViewModel? novo)
    {
        if (antigo is not null)
        {
            antigo.Concluido -= Fechar;
            antigo.PropertyChanged -= AoMudarModelo;
        }

        if (novo is null) return;
        novo.Concluido += Fechar;
        novo.PropertyChanged += AoMudarModelo;
    }

    void AoMudarModelo(object? origem, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaletaDeComandosViewModel.Itens))
        {
            var semItens = Modelo?.Itens.Count == 0;
            Vazio.Visibility = semItens ? Visibility.Visible : Visibility.Collapsed;
            Lista.Visibility = semItens ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (e.PropertyName == nameof(PaletaDeComandosViewModel.Selecionado) && Modelo?.Selecionado is { } item)
        {
            Lista.ScrollIntoView(item);
        }
    }

    void AoTeclar(object origem, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Modelo?.Mover(1); break;
            case Key.Up: Modelo?.Mover(-1); break;
            case Key.Enter: Modelo?.ExecutarCommand.Execute(null); break;
            default: return;
        }

        e.Handled = true;
    }

    void AoClicarItem(object origem, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject alvo && ItemsControl.ContainerFromElement(Lista, alvo) is not null)
            Modelo?.ExecutarCommand.Execute(null);
    }
}
