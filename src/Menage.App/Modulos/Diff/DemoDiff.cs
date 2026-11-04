using System.IO;
using System.Windows;
using Menage.App.Modulos.Diff.Conflito;
using Menage.Core;
using Menage.Core.Diff;
using Menage.Core.Interacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace Menage.App.Modulos.Diff;

/// <summary>Janelas de demonstração <c>--diff repo [arquivo]</c> e <c>--conflito repo</c>, sem a janela principal (que ainda não existe).</summary>
public static class DemoDiff
{
    /// <summary>Janela pedida pelos argumentos; nulo quando eles não são do módulo. <c>--escuro</c> abre no tema escuro.</summary>
    public static Window? Criar(IReadOnlyList<string> args)
    {
        var indice = args.ToList().FindIndex(a => a is "--diff" or "--conflito");
        if (indice < 0 || indice + 1 >= args.Count) return null;
        var repo = Path.GetFullPath(args[indice + 1]);
        InstalarAparencia(args.Contains("--escuro"));
        var servicos = Servicos();
        if (args[indice] == "--conflito") return servicos.GetRequiredService<JanelaConflito.Fabrica>().Criar(repo);
        var arquivo = indice + 2 < args.Count && !args[indice + 2].StartsWith("--", StringComparison.Ordinal) ? args[indice + 2] : null;
        return new JanelaDiff(servicos.GetRequiredService<ServicoDiff>(), repo, arquivo);
    }

    /// <summary>Serviços do módulo sobre o Core.</summary>
    public static IServiceCollection AddDiff(this IServiceCollection servicos) => servicos
        .AddSingleton<ServicoDiff>()
        .AddSingleton<ServicoConflito>()
        .AddSingleton<IServicoDeDialogos, DialogosDiff>()
        .AddSingleton<IPerguntaDeLado>(sp => (DialogosDiff)sp.GetRequiredService<IServicoDeDialogos>())
        .AddSingleton<JanelaConflito.Fabrica>();

    /// <summary>
    /// Enquanto a tela principal não existe, o módulo garante sozinho os dicionários do WPF UI. Se o App já os
    /// tiver (design system), nada é adicionado.
    /// </summary>
    public static void InstalarAparencia(bool escuro)
    {
        var recursos = Application.Current.Resources;
        if (!recursos.MergedDictionaries.OfType<ThemesDictionary>().Any())
        {
            recursos.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            recursos.MergedDictionaries.Add(new ControlsDictionary());
        }
        ApplicationThemeManager.Apply(escuro ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, false);
    }

    static ServiceProvider Servicos() => new ServiceCollection()
        .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
        .AddMenageCore()
        .AddDiff()
        .BuildServiceProvider();
}
