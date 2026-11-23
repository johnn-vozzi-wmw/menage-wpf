using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Menage.App;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Tema;
using Menage.Core.Armazenamento;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace Menage.Smoke.Fechar;

/// <summary>
/// Abre a janela de checkout (modeless e como diálogo) fora da tela, pede o fechamento por Close() e espera <c>Closed</c>.
/// Com <c>--descrever</c> só lista o que o TitleBar do WPF UI tem para fechar.
/// </summary>
public static class Program
{
    const BindingFlags Todos = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [STAThread]
    public static int Main(string[] args)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "mfs" + Environment.TickCount);
        var codigo = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new XamlFlair.XamlFlairResources());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Menage;component/Tema/DesignSystem.xaml", UriKind.Relative) });
        app.Startup += async (_, _) =>
        {
            try { codigo = await RodarAsync(pasta, args.Contains("--descrever")); }
            catch (Exception e) { Console.WriteLine("FALHA: " + e); }
            app.Shutdown();
        };
        app.Run();
        return codigo;
    }

    static async Task<int> RodarAsync(string pasta, bool descrever)
    {
        var sp = ComposicaoDoApp.Montar(ModoDeInicio.Ler([]), new PastaDeDados(pasta));
        sp.GetRequiredService<CarregadorDeTemasPrivados>().Carregar();
        sp.GetRequiredService<IServicoDeTema>().Iniciar();

        if (Environment.GetCommandLineArgs().Contains("--reduzir"))
            sp.GetRequiredService<Menage.App.Animacoes.ServicoDeTransicao>().ReduzirMovimento = true;
        if (Environment.GetCommandLineArgs().Contains("--principal"))
        {
            var p = sp.GetRequiredService<Menage.App.Modulos.JanelaPrincipal.JanelaPrincipal>();
            p.ShowActivated = false;
            p.ShowInTaskbar = false;
            return await Fechar("principal", p, () => p.Show(), false);
        }

        var j = Oculta(sp);
        var falhas = await Fechar("modeless", j, () => j.Show(), descrever);
        var d = Oculta(sp);
        falhas += await Fechar("dialogo", d, () => _ = Application.Current.Dispatcher.InvokeAsync(() => d.ShowDialog()), descrever);
        return falhas;
    }

    static JanelaDeCheckout Oculta(IServiceProvider sp)
    {
        var j = sp.GetRequiredService<JanelaDeCheckout>();
        j.ShowActivated = false;
        j.ShowInTaskbar = false;
        return j;
    }

    static async Task<int> Fechar(string nome, Window w, Action abrir, bool descrever)
    {
        var fechou = new TaskCompletionSource();
        w.Closed += (_, _) => fechou.TrySetResult();
        var pronta = new TaskCompletionSource();
        w.ContentRendered += (_, _) => pronta.TrySetResult();
        abrir();
        await Task.WhenAny(pronta.Task, Task.Delay(5000));
        await Task.Delay(500); // deixa a animação de entrada terminar
        if (descrever) Console.WriteLine(Descrever(w));
        var b = Descendentes(w).OfType<TitleBarButton>().First(x => x.ButtonType == TitleBarButtonType.Close);
        var ponto = b.TranslatePoint(new Point(b.ActualWidth / 2, b.ActualHeight / 2), w);
        var acerto = VisualTreeHelper.HitTest(w, ponto)?.VisualHit;
        Console.WriteLine($"HitTest no Fechar ({ponto}): {acerto?.GetType().Name} (e o botão? {acerto is not null && (acerto == b || ((acerto as FrameworkElement)?.TemplatedParent == b) || IsDescendant(b, acerto))})");
        w.Close();
        Console.WriteLine($"ClientAreaAnimation={SystemParameters.ClientAreaAnimation} Fechando={(w as Menage.App.Controles.JanelaBase)?.Fechando}");
        var ok = await Task.WhenAny(fechou.Task, Task.Delay(3000)) == fechou.Task;
        Console.WriteLine($"{nome}: {(ok ? "FECHOU" : "NAO FECHOU")}");
        return ok ? 0 : 1;
    }

    static bool IsDescendant(DependencyObject pai, DependencyObject filho) { for (var o = filho; o is not null; o = VisualTreeHelper.GetParent(o)) if (o == pai) return true; return false; }

    static string Descrever(Window w)
    {
        var barra = Descendentes(w).OfType<TitleBar>().First();
        var metodos = typeof(TitleBar).GetMethods(Todos).Where(m => m.Name.Contains("Clos") || m.Name.Contains("Click")).Select(m => m.Name);
        return "TitleBar métodos: " + string.Join(",", metodos) + " | _currentWindow=" + typeof(TitleBar).GetField("_currentWindow", Todos)?.GetValue(barra);
    }

    static IEnumerable<DependencyObject> Descendentes(DependencyObject o)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var f = VisualTreeHelper.GetChild(o, i);
            yield return f;
            foreach (var n in Descendentes(f)) yield return n;
        }
    }
}
