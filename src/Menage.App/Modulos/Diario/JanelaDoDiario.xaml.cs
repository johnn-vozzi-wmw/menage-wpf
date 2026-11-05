using System.Windows;
using Menage.App.Controles;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Diario;
using Menage.Core.Interacao;
using Menage.Core.Perfis;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Diario;

/// <summary>Só liga a tela ao <see cref="DiarioViewModel"/>; nenhuma regra de negócio mora aqui.</summary>
public partial class JanelaDoDiario : JanelaBase
{
    public const string Id = "diario.janela";

    readonly DiarioViewModel _vm;
    bool _iniciado;

    public JanelaDoDiario(ContextoDeJanela contexto, DiarioViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.CopiarPedido += texto =>
        {
            try { Clipboard.SetText(texto); }
            catch (System.Runtime.InteropServices.ExternalException) { vm.Mensagem = "A área de transferência está em uso: tente de novo."; }
        };
        Closed += (_, _) => vm.Dispose();
        Loaded += async (_, _) =>
        {
            if (_iniciado) return;
            _iniciado = true;
            await _vm.CarregarAsync();
        };
    }
}

/// <summary>Configurar o Diário: grava ao salvar; Testar Jira e Testar IA gravam antes e só leem/perguntam quando clicados.</summary>
public partial class JanelaConfigDiario : JanelaBase, IJanelaComResultado
{
    public const string Id = "diario.configurar";

    readonly ConfigDiarioViewModel _vm;

    public JanelaConfigDiario(ContextoDeJanela contexto, ConfigDiarioViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    public object? Resultado { get; private set; }

    void AoSalvar(object sender, RoutedEventArgs e)
    {
        if (!_vm.Salvar()) return;
        Resultado = true;
        Close();
    }
}

/// <summary>Comandos do Diário (só perfil Desenvolvedor): a lateral, o Ctrl+J e a paleta Ctrl+K abrem a mesma janela.</summary>
public sealed class DiarioModulo(IServicoDeJanelas janelas) : IModulo
{
    public string Nome => "Diário de trabalho";

    public void Registrar(ICatalogoDeComandos catalogo) =>
        catalogo.Adicionar(new Comando("diario.abrir", "Diário de trabalho (worklogs do Jira)", _ =>
        {
            janelas.Abrir(Id);
            return Task.CompletedTask;
        }, "Diário", "Notebook", "Ctrl+J", ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor)));

    const string Id = JanelaDoDiario.Id;
}

public static class ComposicaoDoDiario
{
    public static IServiceCollection AddDiarioApp(this IServiceCollection servicos)
    {
        servicos.AddDiario();
        servicos.AddModulo<DiarioModulo>();
        servicos.AddTransient<DiarioViewModel>();
        servicos.AddTransient<ConfigDiarioViewModel>();
        servicos.AddJanela<JanelaDoDiario>(JanelaDoDiario.Id);
        servicos.AddJanela<JanelaConfigDiario>(JanelaConfigDiario.Id);
        return servicos;
    }
}
