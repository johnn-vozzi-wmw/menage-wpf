using System.Windows;
using System.Windows.Input;
using Menage.App.Controles;
using Menage.App.Modulos.Diff.Conflito;

namespace Menage.App.Modulos.CherryPick;

/// <summary>Fila de cherry-pick. Só liga controles ao ViewModel; as regras moram no Core.</summary>
public partial class JanelaFila : JanelaBase
{
    readonly FilaViewModel _modelo;
    readonly JanelaConflito.Fabrica _conflitos;

    public JanelaFila(ContextoDeJanela contexto, FilaViewModel modelo, JanelaConflito.Fabrica conflitos) : base(contexto)
    {
        (_modelo, _conflitos) = (modelo, conflitos);
        DataContext = modelo;
        InitializeComponent();
        _modelo.PediuResolverAqui += AoPedirResolver;
        Closed += (_, _) => _modelo.Dispose();
    }

    public FilaViewModel Modelo => _modelo;

    async void AoIniciar(object sender, RoutedEventArgs e) => await _modelo.IniciarAsync();

    void AoParar(object sender, RoutedEventArgs e) => _modelo.PararApos();

    void AoLimpar(object sender, RoutedEventArgs e) => _modelo.Limpar();

    void AoResolverAqui(object sender, RoutedEventArgs e) => _modelo.ResolverAqui();

    async void AoJaResolvi(object sender, RoutedEventArgs e) => await _modelo.JaResolviAsync();

    async void AoPular(object sender, RoutedEventArgs e) => await _modelo.PularAsync();

    async void AoTentarDeNovo(object sender, RoutedEventArgs e) => await _modelo.TentarDeNovoAsync();

    async void AoAbortar(object sender, RoutedEventArgs e) => await _modelo.AbortarAsync();

    void AoSubir(object sender, RoutedEventArgs e) => _modelo.Mover(Passo(sender), -1);

    void AoDescer(object sender, RoutedEventArgs e) => _modelo.Mover(Passo(sender), 1);

    void AoRemover(object sender, RoutedEventArgs e) => _modelo.Remover(Passo(sender));

    void AoTeclaNaLista(object sender, KeyEventArgs e)
    {
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla == Key.Delete) _modelo.Remover(_modelo.Selecionado);
        else if (tecla == Key.Up && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) _modelo.Mover(_modelo.Selecionado, -1);
        else if (tecla == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) _modelo.Mover(_modelo.Selecionado, 1);
        else return;
        e.Handled = true;
    }

    // O editor de conflito do módulo Diff já faz o git add de cada arquivo salvo.
    void AoPedirResolver(string repo)
    {
        var janela = _conflitos.Criar(repo);
        janela.Owner = this;
        janela.ShowDialog();
    }

    static PassoDaFila? Passo(object sender) => (sender as FrameworkElement)?.DataContext as PassoDaFila;
}
