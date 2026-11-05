using System.Windows;
using Menage.App.Controles;
using Menage.Core.Operacoes.Stash;

namespace Menage.App.Modulos.JanelaPrincipal;

public sealed class LinhaDeStash(PendenciaDeStash pendencia)
{
    public PendenciaDeStash Pendencia { get; } = pendencia;

    public string Nome { get; set; } = "";

    public string Resumo => $"{Pendencia.Branch} · {Pendencia.Arquivos.Count} arquivo(s)";
}

public partial class JanelaDeStash : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    List<LinhaDeStash> _linhas = [];

    public JanelaDeStash(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeStash pedido) return;
        _linhas = [.. pedido.Pendencias.Select(p => new LinhaDeStash(p) { Nome = $"{p.Branch}-{DateTime.Now:yyyyMMdd}" })];
        Lista.ItemsSource = _linhas;
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        Resultado = new OpcoesDeStash([.. _linhas.Select(l => new ItemDeStash(l.Pendencia.Projeto, l.Nome))], NaoVersionados.IsChecked == true);
        Close();
    }
}
