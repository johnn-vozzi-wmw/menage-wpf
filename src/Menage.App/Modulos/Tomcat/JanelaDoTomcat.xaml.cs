using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Perfis;
using Menage.Core.Tomcat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Menage.App.Modulos.Tomcat;

/// <summary>Só liga eventos da tela ao <see cref="TomcatViewModel"/>. Fechar a janela não para os Tomcats.</summary>
public partial class JanelaDoTomcat : JanelaBase
{
    public const string Id = "tomcat.janela";

    readonly TomcatViewModel _vm;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _tiqueEmCurso;

    public JanelaDoTomcat(ContextoDeJanela contexto, TomcatViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        ((INotifyCollectionChanged)vm.Log).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && ListaDoLog.Items.Count > 0) ListaDoLog.ScrollIntoView(ListaDoLog.Items[^1]);
        };
        _relogio.Tick += async (_, _) =>
        {
            if (_tiqueEmCurso) return;
            _tiqueEmCurso = true;
            try { await _vm.TiqueAsync(); }
            finally { _tiqueEmCurso = false; }
        };
        Loaded += (_, _) =>
        {
            _vm.Iniciar();
            _relogio.Start();
        };
        Closing += AoFechar;
    }

    void AoFechar(object? sender, CancelEventArgs e)
    {
        _relogio.Stop();
        if (_vm.Preparando)
        {
            e.Cancel = true;
            _relogio.Start();
            _vm.Mensagem = "Há um preparo em andamento: cancele ou espere terminar antes de fechar.";
        }
    }

    void AoEscolherPasta(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Pasta do Tomcat (a que contém bin, conf, lib e webapps)" };
        if (dialogo.ShowDialog(this) == true) _vm.Pasta = dialogo.FolderName;
    }
}

/// <summary>Comando do Tomcat Express: só Desenvolvedor e Geral (o Tester não vê).</summary>
public sealed class TomcatModulo(IServicoDeJanelas janelas) : IModulo
{
    public string Nome => "Tomcat Express";

    public void Registrar(ICatalogoDeComandos catalogo) =>
        catalogo.Adicionar(new Comando("tomcat.abrir", "Tomcat Express: preparar, subir e parar ambientes", _ =>
        {
            janelas.Abrir(JanelaDoTomcat.Id);
            return Task.CompletedTask;
        }, "Ferramentas", "Server", Perfis: ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor, PerfilDeUso.Geral)));
}

public static class ComposicaoDoTomcat
{
    public static IServiceCollection AddTomcatApp(this IServiceCollection servicos)
    {
        servicos.AddTomcat();
        servicos.AddModulo<TomcatModulo>();
        servicos.AddSingleton<TomcatViewModel>();
        servicos.AddJanela<JanelaDoTomcat>(JanelaDoTomcat.Id);
        servicos.AddJanela<JanelaDoBancoTomcat>(JanelaDoBancoTomcat.Id);
        return servicos;
    }
}
