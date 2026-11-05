using System.Windows;
using Menage.App.Controles;
using Menage.Core.Interacao;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Microsoft.Win32;

namespace Menage.App.Modulos.JanelaPrincipal;

public sealed record LinhaDeProjeto(string Nome, string Caminho, string Grupo);

/// <summary>Adiciona e remove repositórios no catálogo do Menage (nada de arquivos do Git Manager antigo).</summary>
public partial class JanelaDeProjetos : JanelaBase
{
    public const string Id = "projetos.gerenciar";

    readonly IConfiguracaoDeProjetos _configuracao;
    readonly IServicoDeDialogos _dialogos;

    public JanelaDeProjetos(ContextoDeJanela contexto, IConfiguracaoDeProjetos configuracao, IServicoDeDialogos dialogos) : base(contexto)
    {
        (_configuracao, _dialogos) = (configuracao, dialogos);
        InitializeComponent();
        Grupos.ItemsSource = (new List<string> { "Sem grupo" }).Concat(configuracao.Catalogo.Grupos.Select(g => g.Nome)).ToList();
        Grupos.SelectedIndex = 0;
        Atualizar();
    }

    void Atualizar() => Lista.ItemsSource = _configuracao.Projetos
        .Select(p => new LinhaDeProjeto(p.Nome, p.Caminho, p.Grupos.Count == 0 ? "Sem grupo" : string.Join(", ", p.Grupos.Select(_configuracao.Catalogo.NomeDoGrupo))))
        .ToList();

    async void AoAdicionar(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Escolha a pasta do repositório" };
        if (dialogo.ShowDialog(this) != true) return;
        if (!ConsultasGit.EhRepositorio(dialogo.FolderName))
        {
            await _dialogos.InformarAsync("Não é um repositório Git", "A pasta escolhida não tem uma pasta .git.");
            return;
        }

        var catalogo = _configuracao.Catalogo;
        var grupo = catalogo.Grupos.FirstOrDefault(g => g.Nome == (string?)Grupos.SelectedItem);
        var grupos = catalogo.Grupos.Select(g => g == grupo ? g with { Projetos = [.. g.Projetos, dialogo.FolderName] } : g).ToList();
        await Salvar(catalogo with { Grupos = grupos, Repositorios = [.. catalogo.Repositorios, dialogo.FolderName] });
    }

    async void AoRemover(object sender, RoutedEventArgs e)
    {
        if (Lista.SelectedItem is not LinhaDeProjeto linha) return;
        bool Diferente(string caminho) => !caminho.Equals(linha.Caminho, StringComparison.OrdinalIgnoreCase);
        var catalogo = _configuracao.Catalogo;
        await Salvar(catalogo with
        {
            Repositorios = [.. catalogo.Repositorios.Where(Diferente)],
            Grupos = [.. catalogo.Grupos.Select(g => g with { Projetos = [.. g.Projetos.Where(Diferente)] })],
            PastasDeBusca = [.. catalogo.PastasDeBusca.Where(Diferente)],
        });
    }

    async Task Salvar(CatalogoDeProjetos catalogo)
    {
        var r = _configuracao.Salvar(catalogo);
        if (!r.EhOk) await _dialogos.MostrarErroAsync(r.Erro);
        Atualizar();
    }
}
