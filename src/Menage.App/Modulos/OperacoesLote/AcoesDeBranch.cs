using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.ComDevelop;
using Menage.Core.Operacoes.CriarBranch;
using Menage.Core.Operacoes.Excluir;
using Menage.Core.Operacoes.Rebase;
using Menage.Core.Operacoes.Renomear;
using Menage.Core.Projetos;
using Menage.Core.Repos;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>Nova branch, Renomear, Excluir, Rebase e Com develop: pergunta o que falta e entrega ao coordenador.</summary>
public sealed class AcoesDeBranch(
    AcoesGit acoes, IProjetos projetos, CoordenadorDeOperacoes coordenador, IServicoDeJanelas janelas, IServicoDeDialogos dialogos,
    SugestoesDeBranches sugestoes, CriarBranchEmLote criar, RenomearBranchEmLote renomear, ExcluirEmLote excluir, RebaseEmLote rebase, ComDevelopEmLote comDevelop)
{
    bool Livre => projetos.Marcados.Count > 0 && !coordenador.EmAndamento;

    Task<object?> Perguntar(PedidoDeFormulario pedido) => janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, pedido);

    public Task CriarAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        var bases = await sugestoes.BasesAsync(projetos.Marcados);
        if (await Perguntar(new("Nova branch", "Cria a branch em cada projeto marcado, sem acompanhar a base.",
        [
            new CampoTexto("nome", "Nome da nova branch", Obrigatorio: true),
            new CampoEscolha("base", "Criar a partir de (vazio = commit atual; origin, depois local, depois tag)", bases),
            new CampoCaixa("publicar", "Publicar no origin (push -u)"),
        ], "Criar", Altura: 430)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(criar, new OpcoesDeCriarBranch(r.Texto("nome"), r.Texto("base"), r.Caixa("publicar")));
    });

    public Task RenomearAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Renomear branch", "Renomeia a branch em cada projeto marcado.",
        [
            new CampoTexto("atual", "Nome atual (vazio = a branch atual de cada projeto)"),
            new CampoTexto("novo", "Nome novo", Obrigatorio: true),
            new CampoCaixa("origin", "Renomear também no origin (publica o novo, apaga o antigo, ajusta o upstream)"),
        ], "Renomear", Altura: 430)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(renomear, new OpcoesDeRenomear(r.Texto("novo"), r.Texto("atual"), r.Caixa("origin")));
    });

    public Task ExcluirAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Excluir branches / tags", "Um nome por linha (ou separados por vírgula). Se não for branch, procura tag.",
        [
            new CampoTexto("nomes", "Nomes", VariasLinhas: true, Obrigatorio: true),
            new CampoCaixa("local", "Excluir neste computador (local)", true),
            new CampoCaixa("remoto", "Excluir também no origin (remoto)"),
            new CampoCaixa("backup", "Criar backup (backup/<nome>-<data>) antes de apagar", true),
        ], "Ver prévia", Altura: 500)) is not RespostaDeFormulario r) return;

        var opcoes = new OpcoesDeExclusao(ExcluirEmLote.SepararNomes(r.Texto("nomes")), r.Caixa("local"), r.Caixa("remoto"), r.Caixa("backup"));
        if (ExcluirEmLote.ExigeConfirmacaoExtra(opcoes))
        {
            var motivo = opcoes.Backup ? "Há branch sensível (main, master, develop ou release*) na lista." : "A exclusão será feita SEM backup.";
            if (!await dialogos.ConfirmarAsync(new Previa("Confirmação reforçada", motivo + " Excluir mesmo assim?", [.. opcoes.Nomes]))) return;
            opcoes = opcoes with { ConfirmouExtra = true };
        }

        await acoes.Rodar(excluir, opcoes);
    });

    public Task RebaseAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        var bases = await sugestoes.BasesAsync(projetos.Marcados);
        if (await Perguntar(new("Rebase", "Reescreve o histórico da branch atual de cada projeto sobre a base escolhida (alterações locais vão para o stash e voltam).",
        [
            new CampoEscolha("base", "Rebase sobre", bases, "develop"),
        ], "Rebase", Altura: 330)) is not RespostaDeFormulario r || r.Texto("base").Length == 0) return;
        await acoes.Rodar(rebase, new OpcoesDeRebase(r.Texto("base")));
    });

    public Task ComDevelopAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        var bases = await sugestoes.BasesAsync(projetos.Marcados);
        if (await Perguntar(new("Com develop", "Atualiza a base pelo origin sem sair da branch atual e faz merge dela na atual.",
        [
            new CampoEscolha("base", "Trazer a branch", bases, "develop"),
            new CampoCaixa("stash", "Guardar alterações locais em stash e devolver no fim", true),
        ], "Mesclar", Altura: 360)) is not RespostaDeFormulario r || r.Texto("base").Length == 0) return;
        await acoes.Rodar(comDevelop, new OpcoesDeComDevelop(r.Texto("base"), r.Caixa("stash")));
    });
}
