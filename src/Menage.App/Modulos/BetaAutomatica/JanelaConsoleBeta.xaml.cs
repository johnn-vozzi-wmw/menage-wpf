using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Menage.App.Controles;
using Menage.Core.BetaAutomatica;
using Microsoft.Win32;

namespace Menage.App.Modulos.BetaAutomatica;

public sealed record LinhaDoConsole(RegistroDoConsole Registro)
{
    public NivelDoConsole Nivel => Registro.Nivel;

    public string Linha => $"{Registro.Quando:HH:mm:ss}  {Registro.Nivel.ToString().ToUpperInvariant(),-7} {Registro.Origem,-15} {(Registro.Atividade.Length > 0 ? Registro.Atividade : "-"),-12} {(Registro.Projeto.Length > 0 ? Registro.Projeto : "-"),-18} {Registro.Texto.Replace('\n', ' ')}";
}

/// <summary>Console de diagnóstico: tudo o que a geração da beta faz, ao vivo, com filtros. O histórico fica em logs-beta-auto\console-beta.log.</summary>
public partial class JanelaConsoleBeta : JanelaBase
{
    public const string Id = "beta-auto.console";

    readonly ConsoleDaBeta _console;
    readonly ObservableCollection<LinhaDoConsole> _linhas = [];
    readonly ICollectionView _visao;
    readonly List<LinhaDoConsole> _pausadas = [];

    public JanelaConsoleBeta(ContextoDeJanela contexto, ConsoleDaBeta console) : base(contexto)
    {
        _console = console;
        InitializeComponent();
        _visao = CollectionViewSource.GetDefaultView(_linhas);
        _visao.Filter = Passa;
        Linhas.ItemsSource = _visao;
        foreach (var r in console.Registros) _linhas.Add(new LinhaDoConsole(r));
        console.Registrado += AoRegistrar;
        console.Limpo += AoLimpou;
        Closed += (_, _) => { console.Registrado -= AoRegistrar; console.Limpo -= AoLimpou; };
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => Busca.Focus()), Key.F, ModifierKeys.Control));
        Loaded += (_, _) => Rodar();
    }

    static string Marca(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    bool Passa(object o)
    {
        var r = ((LinhaDoConsole)o).Registro;
        var ok = Marca(Nivel) switch
        {
            "padrao" => r.Nivel != NivelDoConsole.Saida,
            "problemas" => r.Nivel is NivelDoConsole.Aviso or NivelDoConsole.Erro,
            "comandos" => r.Nivel == NivelDoConsole.Comando,
            _ => true,
        };
        if (!ok) return false;
        if (Marca(Origem) is { Length: > 0 } origem && r.Origem != origem) return false;
        if (Atividade.Text.Trim() is { Length: > 0 } atv && !r.Atividade.Contains(atv, StringComparison.OrdinalIgnoreCase)) return false;
        return Busca.Text.Trim() is not { Length: > 0 } busca || r.Texto.Contains(busca, StringComparison.OrdinalIgnoreCase) || r.Projeto.Contains(busca, StringComparison.OrdinalIgnoreCase);
    }

    void AoRegistrar(RegistroDoConsole r) => Dispatcher.BeginInvoke(() =>
    {
        var linha = new LinhaDoConsole(r);
        if (Pausar.IsChecked == true) { _pausadas.Add(linha); return; }
        _linhas.Add(linha);
        if (_linhas.Count > ConsoleDaBeta.LimiteNaMemoria) _linhas.RemoveAt(0);
        if (Acompanhar.IsChecked == true && Linhas.Items.Count > 0) Linhas.ScrollIntoView(Linhas.Items[^1]);
        Rodar();
    });

    void AoLimpou() => Dispatcher.BeginInvoke(() => { _linhas.Clear(); _pausadas.Clear(); Rodar(); });

    void Rodar() => Rodape.Text = $"{Linhas.Items.Count} linha(s) na tela de {_linhas.Count} na memória" + (_pausadas.Count > 0 ? $" · pausado: {_pausadas.Count} nova(s) esperando" : "");

    void AoFiltrar(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _visao.Refresh();
        Rodar();
    }

    void AoPausar(object sender, RoutedEventArgs e)
    {
        if (Pausar.IsChecked == true) return;
        foreach (var l in _pausadas) _linhas.Add(l);
        _pausadas.Clear();
        Rodar();
    }

    void AoLimpar(object sender, RoutedEventArgs e) => _console.LimparTela();

    string TextoNaTela() => string.Join(Environment.NewLine, Linhas.Items.Cast<LinhaDoConsole>().Select(l => l.Linha));

    void AoCopiar(object sender, RoutedEventArgs e)
    {
        if (Linhas.Items.Count > 0) Clipboard.SetText(TextoNaTela());
    }

    void AoSalvar(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { FileName = $"console-beta-{DateTime.Now:yyyyMMdd-HHmmss}.txt", Filter = "Texto|*.txt" };
        if (d.ShowDialog(this) == true) File.WriteAllText(d.FileName, TextoNaTela());
    }

    void AoAbrirLog(object sender, RoutedEventArgs e)
    {
        if (_console.Arquivo is { } a && File.Exists(a)) Process.Start(new ProcessStartInfo(a) { UseShellExecute = true });
        else Avisar("Console", "Ainda não há console-beta.log.");
    }
}
