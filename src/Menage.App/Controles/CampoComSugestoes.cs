using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Menage.Core.Busca;

namespace Menage.App.Controles;

/// <summary>
/// Campo de texto com sugestões instantâneas: a cada letra consulta o <see cref="IndiceDeSugestoes"/> (já normalizado)
/// e mostra até 50 itens numa lista virtualizada, com o trecho digitado em destaque.
/// Setas navegam, Enter/Tab aceitam o item marcado, Esc fecha, clique escolhe. O texto continua livre
/// (serve também para digitar algo que não está na lista). Substitui o ComboBox editável.
/// </summary>
public sealed class CampoComSugestoes : Grid
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CampoComSugestoes),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((CampoComSugestoes)d).AoMudarTextoExterno((string?)e.NewValue ?? string.Empty))
        { DefaultUpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(CampoComSugestoes),
        new PropertyMetadata(null, (d, e) => ((CampoComSugestoes)d).AoMudarItens(e.OldValue as IEnumerable, e.NewValue as IEnumerable)));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(CampoComSugestoes), new PropertyMetadata(string.Empty,
            (d, e) => ((CampoComSugestoes)d)._caixa.PlaceholderText = (string?)e.NewValue ?? string.Empty));

    readonly Wpf.Ui.Controls.TextBox _caixa = new() { ClearButtonEnabled = false };
    readonly ListBox _lista = new() { Focusable = false, MaxHeight = 260, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly Popup _popup = new() { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
    readonly Border _cartao = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 8) };
    IndiceDeSugestoes? _indice;
    bool _atualizando;

    public CampoComSugestoes()
    {
        _cartao.SetResourceReference(Border.BackgroundProperty, "Tema.Painel");
        _cartao.SetResourceReference(Border.BorderBrushProperty, "Tema.Borda");
        _lista.SetResourceReference(Control.ForegroundProperty, "Tema.Texto");
        _lista.ItemTemplate = Modelo();
        _lista.ItemContainerStyle = EstiloDoItem();
        VirtualizingPanel.SetIsVirtualizing(_lista, true);
        VirtualizingPanel.SetVirtualizationMode(_lista, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(_lista, ScrollBarVisibility.Disabled);
        _cartao.Child = _lista;
        _popup.Child = _cartao;
        _popup.PlacementTarget = _caixa;
        Children.Add(_caixa);
        Children.Add(_popup);

        _caixa.TextChanged += (_, _) => AoDigitar();
        _caixa.PreviewKeyDown += AoTeclar;
        _caixa.GotKeyboardFocus += (_, _) => { if (_caixa.Text.Length == 0) Mostrar(); };
        _caixa.LostKeyboardFocus += (_, _) => _popup.IsOpen = false;
        _caixa.PreviewMouseLeftButtonDown += (_, _) => { if (!_popup.IsOpen && _caixa.IsKeyboardFocusWithin) Mostrar(); };
        _lista.PreviewMouseLeftButtonUp += AoClicarNaLista;
        SizeChanged += (_, _) => _cartao.Width = ActualWidth + 8;
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <summary>Foco no campo de texto (a janela chama ao abrir).</summary>
    public new bool Focus() => _caixa.Focus();

    void AoMudarTextoExterno(string valor)
    {
        if (_caixa.Text == valor) return;
        _atualizando = true;
        _caixa.Text = valor;
        _atualizando = false;
    }

    void AoMudarItens(IEnumerable? antigos, IEnumerable? novos)
    {
        if (antigos is INotifyCollectionChanged a) a.CollectionChanged -= AoMudarColecao;
        if (novos is INotifyCollectionChanged n) n.CollectionChanged += AoMudarColecao;
        _indice = null;
    }

    void AoMudarColecao(object? s, NotifyCollectionChangedEventArgs e) => _indice = null;

    IndiceDeSugestoes Indice => _indice ??= new IndiceDeSugestoes(ItemsSource?.Cast<object>().Select(o => o?.ToString() ?? "") ?? []);

    void AoDigitar()
    {
        if (_atualizando) return;
        SetCurrentValue(TextProperty, _caixa.Text);
        if (_caixa.IsKeyboardFocusWithin) Mostrar();
    }

    void Mostrar()
    {
        var resultado = Indice.Buscar(_caixa.Text);
        _lista.ItemsSource = resultado;
        _popup.IsOpen = resultado.Count > 0 && !(resultado.Count == 1 && resultado[0].Texto == _caixa.Text);
    }

    void AoTeclar(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up when !_popup.IsOpen: Mostrar(); e.Handled = true; break;
            case Key.Down or Key.Up when _lista.Items.Count > 0:
                var passo = e.Key == Key.Down ? 1 : -1;
                var novo = Math.Clamp(_lista.SelectedIndex + passo, 0, _lista.Items.Count - 1);
                _lista.SelectedIndex = novo;
                _lista.ScrollIntoView(_lista.SelectedItem);
                e.Handled = true;
                break;
            case Key.Escape when _popup.IsOpen: _popup.IsOpen = false; e.Handled = true; break;
            case Key.Enter or Key.Tab when _popup.IsOpen && _lista.SelectedItem is Sugestao s:
                Escolher(s);
                e.Handled = e.Key == Key.Enter;
                break;
        }
    }

    void AoClicarNaLista(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(_lista, o) is ListBoxItem { DataContext: Sugestao s })
        {
            Escolher(s);
            _caixa.Focus();
        }
    }

    void Escolher(Sugestao s)
    {
        _popup.IsOpen = false;
        _atualizando = true;
        _caixa.Text = s.Texto;
        _caixa.CaretIndex = s.Texto.Length;
        _atualizando = false;
        SetCurrentValue(TextProperty, s.Texto);
    }

    static DataTemplate Modelo()
    {
        var texto = new FrameworkElementFactory(typeof(TextoDestacado));
        texto.SetValue(MarginProperty, new Thickness(8, 5, 8, 5));
        return new DataTemplate { VisualTree = texto };
    }

    static Style EstiloDoItem()
    {
        var estilo = new Style(typeof(ListBoxItem));
        estilo.Setters.Add(new Setter(FocusableProperty, false));
        estilo.Setters.Add(new Setter(Control.TemplateProperty, ModeloDoItem()));
        return estilo;
    }

    static ControlTemplate ModeloDoItem()
    {
        var fundo = new FrameworkElementFactory(typeof(Border), "Fundo");
        fundo.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        fundo.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        fundo.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var modelo = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = fundo };
        var marcado = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        marcado.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Tema.GradeSel"), "Fundo"));
        var sobre = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        sobre.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Tema.GradeHover"), "Fundo"));
        modelo.Triggers.Add(sobre);
        modelo.Triggers.Add(marcado);
        return modelo;
    }
}
