using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Menage.App.Controles;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.PullRequests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Menage.App.Modulos.PullRequests;

/// <summary>Só liga eventos da tela ao <see cref="PullRequestsViewModel"/>; nenhuma regra de negócio mora aqui.</summary>
public partial class JanelaDosPullRequests : JanelaBase
{
    public const string Id = "prs.janela";

    readonly PullRequestsViewModel _vm;

    public JanelaDosPullRequests(ContextoDeJanela contexto, PullRequestsViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        ((INotifyCollectionChanged)vm.Log).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && ListaDoLog.Items.Count > 0) ListaDoLog.ScrollIntoView(ListaDoLog.Items[^1]);
        };
        Loaded += async (_, _) => await _vm.IniciarAsync();
    }

    async void AoTrocarPasta(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Pasta do repositório que recebe os pull requests" };
        if (dialogo.ShowDialog(this) == true) await _vm.TrocarPastaAsync(dialogo.FolderName);
    }

    void AoClicarNaGrade(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (e.OriginalSource is DependencyObject origem && Acima<DataGridCell>(origem) is not null && Acima<DataGridRow>(origem)?.Item is LinhaDePr linha)
            linha.Marcado = !linha.Marcado;
    }

    void AoTeclarNaGrade(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        var linhas = Grade.SelectedItems.OfType<LinhaDePr>().ToList();
        var marcar = linhas.Any(l => !l.Marcado);
        foreach (var l in linhas) l.Marcado = marcar;
        e.Handled = true;
    }

    static T? Acima<T>(DependencyObject origem) where T : DependencyObject
    {
        for (var atual = origem; atual is not null; atual = VisualTreeHelper.GetParent(atual))
            if (atual is T alvo) return alvo;
        return null;
    }
}

/// <summary>"Pull requests": só aparece para o dono do repositório (e-mail global do git). Conveniência; o GitHub autoriza o push.</summary>
public sealed class PullRequestsModulo : IModulo
{
    readonly IServicoDeJanelas _janelas;
    volatile bool _permitido;

    public PullRequestsModulo(IServicoDeJanelas janelas, ServicoDePullRequests servico)
    {
        _janelas = janelas;
        _ = Task.Run(async () => _permitido = await servico.PermitidoAsync());
    }

    public string Nome => "Pull requests";

    public void Registrar(ICatalogoDeComandos catalogo) =>
        catalogo.Adicionar(new Comando("prs.abrir", "Pull requests do Menage (só o dono do repositório)", _ =>
        {
            _janelas.Abrir(JanelaDosPullRequests.Id);
            return Task.CompletedTask;
        }, "Ferramentas", "BranchFork", Habilitado: () => _permitido));
}

public static class ComposicaoDosPullRequests
{
    public static IServiceCollection AddPullRequestsApp(this IServiceCollection servicos)
    {
        servicos.AddPullRequests();
        servicos.AddModulo<PullRequestsModulo>();
        servicos.AddSingleton<PullRequestsViewModel>();
        servicos.AddJanela<JanelaDosPullRequests>(JanelaDosPullRequests.Id);
        return servicos;
    }
}
