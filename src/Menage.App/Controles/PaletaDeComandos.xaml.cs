using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Menage.App.Tema;

namespace Menage.App.Controles;

/// <summary>Paleta Ctrl+K: painel da própria janela (como no VS Code), lendo o catálogo de comandos.</summary>
public partial class PaletaDeComandos : UserControl
{
    public PaletaDeComandos()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Ligar(e.OldValue as PaletaDeComandosViewModel, e.NewValue as PaletaDeComandosViewModel);
    }

    public bool Aberta => Visibility == Visibility.Visible;

    PaletaDeComandosViewModel? Modelo => DataContext as PaletaDeComandosViewModel;

    /// <summary>Com a paleta já aberta, só devolve o foco à busca.</summary>
    public void Abrir()
    {
        if (!Aberta)
        {
            Modelo?.Reiniciar();
            Visibility = Visibility.Visible;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Animacao.Rapida) { EasingFunction = Animacao.Entrada });
            Deslocamento.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-8, 0, Animacao.Media) { EasingFunction = Animacao.Entrada });
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Campo.Focus();
            Keyboard.Focus(Campo);
            Campo.SelectAll();
        });
    }

    public void Fechar()
    {
        if (Aberta) Visibility = Visibility.Collapsed;
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
