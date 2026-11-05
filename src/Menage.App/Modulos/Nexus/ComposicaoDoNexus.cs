using Menage.App.Controles;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Nexus;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Nexus;

/// <summary>Comando "Nexus" (todos os perfis, inclusive Tester): pílula/menu e paleta Ctrl+K abrem a mesma janela.</summary>
public sealed class NexusModulo(IServicoDeJanelas janelas) : IModulo
{
    public string Nome => "Nexus";

    public void Registrar(ICatalogoDeComandos catalogo) =>
        catalogo.Adicionar(new Comando("nexus.abrir", "Nexus: buscar e baixar", _ =>
        {
            janelas.Abrir(JanelaDoNexus.Id);
            return Task.CompletedTask;
        }, "Ferramentas", "CloudArrowDown"));
}

public static class ComposicaoDoNexus
{
    public static IServiceCollection AddNexusApp(this IServiceCollection servicos)
    {
        servicos.AddNexus();
        servicos.AddModulo<NexusModulo>();
        servicos.AddSingleton<NexusViewModel>();
        servicos.AddJanela<JanelaDoNexus>(JanelaDoNexus.Id);
        return servicos;
    }
}
