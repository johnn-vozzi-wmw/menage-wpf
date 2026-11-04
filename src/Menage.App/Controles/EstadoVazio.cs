using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Menage.App.Controles;

/// <summary>Ícone, título e descrição para quando não há o que mostrar.</summary>
public sealed class EstadoVazio : Control
{
    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(
        nameof(Icone), typeof(SymbolRegular), typeof(EstadoVazio), new PropertyMetadata(SymbolRegular.Search24));

    public static readonly DependencyProperty TituloProperty =
        DependencyProperty.Register(nameof(Titulo), typeof(string), typeof(EstadoVazio), new PropertyMetadata("Nada por aqui"));

    public static readonly DependencyProperty DescricaoProperty =
        DependencyProperty.Register(nameof(Descricao), typeof(string), typeof(EstadoVazio));

    public SymbolRegular Icone
    {
        get => (SymbolRegular)GetValue(IconeProperty);
        set => SetValue(IconeProperty, value);
    }

    public string Titulo
    {
        get => (string)GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    public string? Descricao
    {
        get => (string?)GetValue(DescricaoProperty);
        set => SetValue(DescricaoProperty, value);
    }
}
