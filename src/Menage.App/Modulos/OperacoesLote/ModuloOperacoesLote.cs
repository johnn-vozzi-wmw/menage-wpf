using Menage.App.Controles;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Operacoes;
using Menage.Core.Projetos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>Registra no catálogo os comandos das operações em lote (as pílulas, o menu e a paleta leem a mesma lista).</summary>
public sealed class ComandosDeLoteModulo(AcoesDeCommitEPush commitEPush, AcoesDeBranch branch, AcoesDeManutencao manutencao, CoordenadorDeOperacoes coordenador, IProjetos projetos) : IModulo
{
    public string Nome => "Operações em lote";

    public void Registrar(ICatalogoDeComandos catalogo)
    {
        bool PodeOperar() => !coordenador.EmAndamento && projetos.Marcados.Count > 0;

        void Git(string id, string texto, Func<Task> acao, string icone, string grupo = "Git") =>
            catalogo.Adicionar(new Comando(id, texto, _ => acao(), grupo, icone, Habilitado: PodeOperar));

        Git("git.commit", "Commit nos projetos marcados", commitEPush.CommitAsync, "Checkmark");
        Git("git.push", "Push dos projetos marcados", commitEPush.PushAsync, "ArrowUpload");
        Git("git.branch-criar", "Criar branch", branch.CriarAsync, "BranchFork", "Branch");
        Git("git.branch-renomear", "Renomear branch", branch.RenomearAsync, "Rename", "Branch");
        Git("git.branch-excluir", "Excluir branches ou tags", branch.ExcluirAsync, "Delete", "Branch");
        Git("git.rebase", "Rebase sobre uma branch", branch.RebaseAsync, "ArrowSplit", "Branch");
        Git("git.com-develop", "Trazer a develop para a branch atual", branch.ComDevelopAsync, "ArrowDownload", "Branch");
        Git("git.limpeza", "Limpeza de branches (mescladas e inativas)", manutencao.LimpezaAsync, "Broom", "Branch");
        Git("git.desfazer-commit", "Desfazer o último commit", manutencao.DesfazerAsync, "ArrowUndo", "Mais");
        Git("git.commit-pom", "Commit só do pom.xml", manutencao.CommitPomAsync, "DocumentCheckmark", "Mais");
        Git("git.buscar-commit", "Buscar commit nos projetos", manutencao.BuscarAsync, "Search", "Mais");
    }
}

public static class ModuloOperacoesLoteExtensions
{
    /// <summary>Uma linha em <c>ComposicaoDoApp</c>: registra as ações, o módulo de comandos e as janelas do módulo.</summary>
    public static IServiceCollection AddModuloOperacoesLote(this IServiceCollection servicos)
    {
        servicos.AddSingleton<AcoesDeCommitEPush>();
        servicos.AddSingleton<AcoesDeBranch>();
        servicos.AddSingleton<AcoesDeManutencao>();
        servicos.AddModulo<ComandosDeLoteModulo>();
        servicos.AddJanela<JanelaDeFormulario>(JanelaDeFormulario.Id);
        return servicos;
    }
}
