using Menage.App.Controles;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Operacoes;
using Menage.Core.Projetos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>Registra no catálogo os comandos das operações em lote (as pílulas, o menu e a paleta leem a mesma lista).</summary>
public sealed class ComandosDeLoteModulo(AcoesDeCommitEPush commitEPush, CoordenadorDeOperacoes coordenador, IProjetos projetos) : IModulo
{
    public string Nome => "Operações em lote";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        bool PodeOperar() => !coordenador.EmAndamento && projetos.Marcados.Count > 0;

        void Git(string id, string texto, Func<Task> acao, string icone, string grupo = "Git") =>
            catalogo.Adicionar(new Comando(id, texto, _ => acao(), grupo, icone, Habilitado: PodeOperar));

        Git("git.commit", "Commit nos projetos marcados", commitEPush.CommitAsync, "Checkmark");
        Git("git.push", "Push dos projetos marcados", commitEPush.PushAsync, "ArrowUpload");
    }
}

public static class ModuloOperacoesLoteExtensions
{
    /// <summary>Uma linha em <c>ComposicaoDoApp</c>: registra as ações, o módulo de comandos e as janelas do módulo.</summary>
    public static IServiceCollection AddModuloOperacoesLote(this IServiceCollection servicos)
    {
        servicos.AddSingleton<AcoesDeCommitEPush>();
        servicos.AddModulo<ComandosDeLoteModulo>();
        servicos.AddJanela<JanelaDeFormulario>(JanelaDeFormulario.Id);
        return servicos;
    }
}
