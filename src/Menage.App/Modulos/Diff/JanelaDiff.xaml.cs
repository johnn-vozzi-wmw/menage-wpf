using System.IO;
using System.Windows;
using System.Windows.Input;
using Menage.Core.Diff;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.Diff;

/// <summary>Arquivos alterados à esquerda, diff do escolhido à direita. Abre sozinha, sem depender da janela principal.</summary>
public partial class JanelaDiff : FluentWindow
{
    const double LarguraAberta = 330;
    const double LarguraRecolhida = 54;

    readonly ServicoDiff _servico;
    readonly string _repo;
    readonly string? _arquivoInicial;

    public JanelaDiff(ServicoDiff servico, string repo, string? arquivoInicial = null)
    {
        (_servico, _repo, _arquivoInicial) = (servico, repo, arquivoInicial?.Replace('\\', '/'));
        InitializeComponent();
        Title = Barra.Title = $"Alterações - {Path.GetFileName(repo.TrimEnd('\\', '/'))}   •   HEAD  ↔  pasta (o que ainda não foi commitado)";
        Animador = new AnimadorLista(HospedeiroLista, Lista, Conteudo, ColunaLista, LarguraRecolhida, LarguraAberta);
        Animador.Terminou += Lista.AoMudarEstado;
        Painel.Configurar(servico, repo);
        Lista.Escolheu += a => _ = Painel.MostrarAsync(a);
        Lista.PediuAtualizar += () => _ = RecarregarAsync();
        Lista.PediuRecolher += Animador.Alternar;
        Loaded += async (_, _) =>
        {
            Painel.Rolagem.LigarJanela(this);
            await RecarregarAsync(_arquivoInicial);
        };
        Closed += (_, _) => Painel.Rolagem.Dispose();
    }

    public AnimadorLista Animador { get; }

    public PainelDiff PainelDoDiff => Painel;

    public ListaArquivos ListaDeArquivos => Lista;

    /// <summary>Lê o git status de novo e mantém o arquivo escolhido, se ele ainda estiver alterado.</summary>
    public async Task RecarregarAsync(string? escolher = null)
    {
        var lista = await _servico.ListarAsync(_repo);
        if (!lista.EhOk)
        {
            Lista.Definir([]);
            Painel.SemDiff($"Não foi possível ler as alterações: {lista.Erro}");
            return;
        }
        if (lista.Valor.Count == 0) Painel.SemDiff("Nenhum arquivo alterado neste projeto.");
        Lista.Definir(lista.Valor, escolher);
    }

    void AoTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            _ = RecarregarAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Animador.Alternar();
            e.Handled = true;
        }
        else if (Painel.Tecla(e)) e.Handled = true;
    }
}
