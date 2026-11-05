using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Tema;
using Wpf.Ui.Controls;

namespace Menage.App.Controles;

/// <summary>O que toda janela precisa, injetado de uma vez para as subclasses só repassarem.</summary>
public sealed record ContextoDeJanela(IServicoDeTema Tema, Func<PaletaDeComandosViewModel> CriarPaleta);

/// <summary>
/// Janela do app. Já traz: barra de título, entrada animada, Ctrl+K (paleta), Ctrl+T (par do tema), Esc
/// (fecha paleta, depois diálogo, depois a janela se <see cref="FechaComEsc"/>) e a dissolução da troca de tema.
/// </summary>
public class JanelaBase : FluentWindow
{
    public static readonly DependencyProperty MostrarMaximizarProperty =
        DependencyProperty.Register(nameof(MostrarMaximizar), typeof(bool), typeof(JanelaBase), new PropertyMetadata(true));

    public static readonly DependencyProperty MostrarMinimizarProperty =
        DependencyProperty.Register(nameof(MostrarMinimizar), typeof(bool), typeof(JanelaBase), new PropertyMetadata(true));

    readonly ContextoDeJanela _contexto;
    Task _filaDeDialogos = Task.CompletedTask;
    FrameworkElement? _raiz;
    System.Windows.Controls.Image? _transicao;
    ContentDialogHost? _dialogos;
    SnackbarPresenter? _avisos;
    ContentDialog? _dialogoAberto;

    public JanelaBase(ContextoDeJanela contexto)
    {
        _contexto = contexto;
        SetResourceReference(StyleProperty, "Janela.Base");
        InputBindings.Add(new KeyBinding(new RelayCommand(AbrirPaleta), Key.K, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => contexto.Tema.AlternarPar()), Key.T, ModifierKeys.Control));
        Loaded += (_, _) => AnimarEntrada();
        Deactivated += (_, _) => Paleta?.Fechar();
    }

    public bool MostrarMaximizar
    {
        get => (bool)GetValue(MostrarMaximizarProperty);
        set => SetValue(MostrarMaximizarProperty, value);
    }

    public bool MostrarMinimizar
    {
        get => (bool)GetValue(MostrarMinimizarProperty);
        set => SetValue(MostrarMinimizarProperty, value);
    }

    /// <summary>Janelas de diálogo: Esc fecha a própria janela quando não há paleta nem diálogo abertos.</summary>
    public bool FechaComEsc { get; set; }

    public PaletaDeComandos? Paleta { get; private set; }

    public ContentDialog? DialogoAberto => _dialogoAberto;

    public void AbrirPaleta() => Paleta?.Abrir();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _raiz = (FrameworkElement)GetTemplateChild("PART_Raiz");
        _transicao = (System.Windows.Controls.Image)GetTemplateChild("PART_Transicao");
        _dialogos = (ContentDialogHost)GetTemplateChild("PART_Dialogos");
        _avisos = (SnackbarPresenter)GetTemplateChild("PART_Avisos");
        Paleta = (PaletaDeComandos)GetTemplateChild("PART_Paleta");
        Paleta.DataContext = _contexto.CriarPaleta();
    }

    /// <summary>Mostra um diálogo nesta janela. Pedidos simultâneos entram em fila: o host só tem um lugar.</summary>
    public async Task<ContentDialogResult> MostrarDialogoAsync(Func<ContentDialogHost, ContentDialog> criar)
    {
        var anterior = _filaDeDialogos;
        var fim = new TaskCompletionSource();
        _filaDeDialogos = fim.Task;
        await anterior;
        try
        {
            _dialogoAberto = criar(_dialogos!);
            return await _dialogoAberto.ShowAsync();
        }
        finally
        {
            _dialogoAberto = null;
            fim.SetResult();
        }
    }

    public void Avisar(string titulo, string mensagem, ControlAppearance aparencia = ControlAppearance.Secondary) =>
        new Snackbar(_avisos!) { Title = titulo, Content = mensagem, Appearance = aparencia, Timeout = TimeSpan.FromSeconds(4) }.Show();

    /// <summary>Cobre a janela com a imagem de como ela está, antes de os recursos mudarem.</summary>
    public void CongelarParaTransicao()
    {
        if (_raiz is not { ActualWidth: > 0, ActualHeight: > 0 } || _transicao is null) return;
        var dpi = VisualTreeHelper.GetDpi(_raiz);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(_raiz.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(_raiz.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(_raiz);
        imagem.Freeze();
        _transicao.BeginAnimation(OpacityProperty, null);
        _transicao.Opacity = 1;
        _transicao.Source = imagem;
        _transicao.Visibility = Visibility.Visible;
    }

    /// <summary>Dissolve a imagem por tempo, revelando as cores novas por baixo.</summary>
    public void DissolverTransicao()
    {
        if (_transicao is not { Source: not null } camada) return;
        var dissolver = new DoubleAnimation(1, 0, Animacao.Lenta) { EasingFunction = Animacao.Suave };
        dissolver.Completed += (_, _) =>
        {
            camada.Visibility = Visibility.Collapsed;
            camada.Source = null;
        };
        camada.BeginAnimation(OpacityProperty, dissolver);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && TratarEsc()) e.Handled = true;
    }

    // Clique fora fecha a paleta e segue para onde foi dado (não há camada engolindo o clique).
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (Paleta is { Aberta: true } paleta && !paleta.Contem(e.OriginalSource as DependencyObject)) paleta.Fechar();
    }

    bool TratarEsc()
    {
        if (Paleta is { Aberta: true } paleta)
        {
            paleta.Fechar();
            return true;
        }

        if (_dialogoAberto is { } dialogo)
        {
            dialogo.Hide(ContentDialogResult.None);
            return true;
        }

        if (!FechaComEsc) return false;
        Close();
        return true;
    }

    void AnimarEntrada()
    {
        if (_raiz is null) return;
        var deslocamento = new TranslateTransform();
        _raiz.RenderTransform = deslocamento;
        deslocamento.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, Animacao.Lenta) { EasingFunction = Animacao.Entrada });
        _raiz.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Animacao.Lenta) { EasingFunction = Animacao.Entrada });
    }
}
