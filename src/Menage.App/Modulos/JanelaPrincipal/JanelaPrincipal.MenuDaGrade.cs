using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core.Operacoes.Ferramentas;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Botão direito da grade, teclado da grade e da busca de projetos (as mesmas decisões do Git Manager).</summary>
public partial class JanelaPrincipal
{
    // Busca: Esc limpa; Seta para baixo ou Enter levam o foco para a grade (primeira linha se nada estava selecionado).
    void AoTeclaNaBusca(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when CampoDeBusca.Text.Length > 0:
                _modelo.Grade.LimparBusca();
                e.Handled = true;
                break;
            case Key.Down or Key.Enter:
                FocarGrade();
                e.Handled = true;
                break;
        }
    }

    void FocarGrade()
    {
        if (Grade.Items.Count == 0) return;
        if (Grade.SelectedItem is null) Grade.SelectedIndex = 0;
        Grade.ScrollIntoView(Grade.SelectedItem);
        if (Grade.ItemContainerGenerator.ContainerFromItem(Grade.SelectedItem) is DataGridRow linha) linha.Focus();
        else Grade.Focus();
    }

    // Espaço marca ou desmarca a linha selecionada (só quando o foco está na linha, para não brigar com a caixa da célula).
    void AoTeclaNaGrade(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is not (DataGridRow or DataGridCell) || Grade.SelectedItem is not ItemDeProjeto item) return;
        item.Marcado = !item.Marcado;
        e.Handled = true;
    }

    // Botão direito seleciona a linha sob o cursor sem mexer nas marcações.
    void AoCliqueDireitoNaGrade(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origem && Ancestral<DataGridRow>(origem) is { } linha)
        {
            linha.IsSelected = true;
            linha.Focus();
        }
    }

    void AoAbrirMenuDaGrade(object sender, ContextMenuEventArgs e)
    {
        if (Grade.SelectedItem is not ItemDeProjeto item)
        {
            e.Handled = true;
            return;
        }

        Grade.ContextMenu = CriarMenuDoProjeto(item);
    }

    /// <summary>O mesmo menu do botão direito, para a galeria.</summary>
    internal System.Windows.Controls.ContextMenu MenuDeExemplo(ItemDeProjeto item) => CriarMenuDoProjeto(item);

    System.Windows.Controls.ContextMenu CriarMenuDoProjeto(ItemDeProjeto item)
    {
        var projeto = item.Projeto;
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var (id, nome) in AcoesDoProjeto.SoNeste)
            menu.Items.Add(Entrada($"{nome} só neste projeto", "ArrowRight", () => _ = _doProjeto.RodarSoNesteAsync(projeto, id), _doProjeto.MotivoDoComando(id)));
        menu.Items.Add(Entrada("Marcar só este projeto", "CheckboxChecked", () => _doProjeto.MarcarSo(projeto), _doProjeto.MotivoParaMarcar));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entrada($"Ver diff das alterações ({_doProjeto.NomeDaFerramentaDeDiff})", "DocumentSplitHint", () => _ = _doProjeto.VerDiffAsync(projeto)));
        menu.Items.Add(Entrada("Abrir pasta no Explorer", "FolderOpen", () => _ = _doProjeto.AbrirPastaAsync(projeto)));
        menu.Items.Add(Entrada("Copiar caminho", "Copy", () => _doProjeto.CopiarCaminho(projeto)));
        menu.Items.Add(Entrada("Abrir terminal nesta pasta", "WindowConsole", () => _ = _doProjeto.AbrirTerminalAsync(projeto)));
        menu.Items.Add(Entrada("Definir o origin desta branch...", "Link", () => _ = _doProjeto.DefinirOriginAsync(projeto), _doProjeto.MotivoDoComando("ferramentas.definir-origin")));
        if (_doProjeto.TortoiseDisponivel) menu.Items.Add(Entrada("Abrir log no TortoiseGit", "History", () => _ = _doProjeto.AbrirLogNoTortoiseAsync(projeto)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entrada("Copiar nome da branch", "Copy", () => _ = _doProjeto.CopiarAsync(projeto, TipoDeCopia.Branches)));
        menu.Items.Add(Entrada("Copiar hash do commit atual", "Copy", () => _ = _doProjeto.CopiarAsync(projeto, TipoDeCopia.Hashes)));
        menu.Items.Add(Entrada("Copiar link para abrir o PR da branch", "Copy", () => _ = _doProjeto.CopiarAsync(projeto, TipoDeCopia.LinksDePr)));
        menu.Items.Add(Entrada("Copiar URL do repositório", "Copy", () => _ = _doProjeto.CopiarAsync(projeto, TipoDeCopia.UrlsDosRepositorios)));
        return menu;
    }

    static Wpf.Ui.Controls.MenuItem Entrada(string texto, string icone, Action acao, string? motivoSeApagado)
    {
        var entrada = new Wpf.Ui.Controls.MenuItem
        {
            Header = texto,
            Icon = new SymbolIcon(FabricaDeComandos.Icone(icone)),
            IsEnabled = motivoSeApagado is null,
            ToolTip = motivoSeApagado,
            Command = new RelayCommand(acao),
        };
        ToolTipService.SetShowOnDisabled(entrada, true);
        return entrada;
    }
}
