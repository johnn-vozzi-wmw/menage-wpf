using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Personalizar a barra de pílulas: tirar, mostrar, mover e restaurar (botão direito numa pílula). Fica em <c>preferencias</c>.</summary>
public partial class JanelaPrincipal
{
    readonly Dictionary<string, Pilula> _pilulas = [];

    static IReadOnlyList<string> NomesPadrao => [.. BarraDeAcoesViewModel.Definicoes.Select(d => d.Texto)];

    OrganizacaoDaBarra Organizacao => new OrganizacaoDaBarra(_preferencias.Valor.OrdemDaBarra ?? NomesPadrao, _preferencias.Valor.PilulasOcultas ?? []).Normalizar(NomesPadrao);

    void Guardar(OrganizacaoDaBarra nova)
    {
        _preferencias.Alterar(p => p with { OrdemDaBarra = nova.Ordem, PilulasOcultas = nova.Ocultas });
        MostrarPilulas();
    }

    void MostrarPilulas()
    {
        BarraDePilulas.Children.Clear();
        foreach (var nome in Organizacao.Visiveis)
            if (_pilulas.TryGetValue(nome, out var pilula)) BarraDePilulas.Children.Add(pilula);
    }

    void LigarMenuDaPilula(string nome, Pilula pilula) =>
        pilula.ContextMenuOpening += (_, e) =>
        {
            pilula.ContextMenu = MenuDaBarra(nome);
        };

    System.Windows.Controls.ContextMenu MenuDaBarra(string nome)
    {
        var org = Organizacao;
        var menu = new System.Windows.Controls.ContextMenu();
        var visiveis = org.Visiveis;
        menu.Items.Add(Entrada("Mover para a esquerda", "ArrowLeft", () => Guardar(org.Mover(nome, -1)), visiveis[0] == nome ? "Já é a primeira" : null));
        menu.Items.Add(Entrada("Mover para a direita", "ArrowRight", () => Guardar(org.Mover(nome, 1)), visiveis[^1] == nome ? "Já é a última" : null));
        menu.Items.Add(Entrada("Tirar da barra", "Dismiss", () => Guardar(org.Tirar(nome)),
            nome == OrganizacaoDaBarra.Obrigatoria ? "A pílula Mais fica sempre: é por ela que se chega às outras ações" : null));
        if (org.Ocultas.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var oculta in org.Ocultas) menu.Items.Add(Entrada($"Mostrar \"{oculta}\" na barra", "Add", () => Guardar(org.Mostrar(oculta)), null));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Entrada("Restaurar a barra padrão", "ArrowUndo", () => Guardar(OrganizacaoDaBarra.Padrao(NomesPadrao)), org.Ocultas.Count == 0 && org.Ordem.SequenceEqual(NomesPadrao) ? "A barra já está no padrão" : null));
        return menu;
    }
}
