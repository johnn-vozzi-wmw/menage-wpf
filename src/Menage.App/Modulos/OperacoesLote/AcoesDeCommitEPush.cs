using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.Commit;
using Menage.Core.Operacoes.Push;
using Menage.Core.Projetos;
using Menage.Core.Saida;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>Commit e Push: pergunta o que falta (mensagem, opções) mostrando a prévia por projeto e entrega ao coordenador.</summary>
public sealed class AcoesDeCommitEPush(
    AcoesGit acoes, IProjetos projetos, CoordenadorDeOperacoes coordenador, IServicoDeJanelas janelas, SaidaDeOperacoes saida,
    CommitEmLote commit, PushEmLote push)
{
    public Task CommitAsync() => acoes.Proteger(async () =>
    {
        if (projetos.Marcados.Count == 0 || coordenador.EmAndamento) return;
        var previa = await commit.ListarAsync(projetos.Marcados);
        if (previa.All(p => p.Ignorado || p.SemAlteracoes))
        {
            saida.Escrever("Commit: nenhum projeto marcado tem alterações para commitar.", TipoDeLinha.Aviso);
            await janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, Aviso("Commit", "Nenhum projeto marcado tem alterações para commitar.", previa.Select(LinhaDoCommit)));
            return;
        }

        var ticket = SugestaoDeTicket.DasBranches(previa.Where(p => !p.Ignorado).Select(p => p.Branch));
        var pedido = new PedidoDeFormulario("Commit", "A mesma mensagem será usada em cada projeto marcado.",
        [
            new CampoInformacao("previa", "Projetos", [.. previa.Select(LinhaDoCommit)]),
            new CampoTexto("mensagem", "Mensagem do commit", ticket ?? "", VariasLinhas: true, Obrigatorio: true,
                Dica: "A primeira linha é o resumo; deixe uma linha em branco antes do corpo."),
            new CampoCaixa("novos", "Incluir arquivos novos (não versionados)"),
            new CampoCaixa("push", "Fazer push em seguida"),
        ], "Commit", Altura: 620);
        if (await janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, pedido) is not RespostaDeFormulario r) return;
        await acoes.Rodar(commit, new OpcoesDeCommit(r.Texto("mensagem"), r.Caixa("novos"), r.Caixa("push")));
    });

    public Task PushAsync() => acoes.Proteger(async () =>
    {
        if (projetos.Marcados.Count == 0 || coordenador.EmAndamento) return;
        saida.Escrever("Push: consultando o origin dos projetos marcados...", TipoDeLinha.Fraco);
        var previa = await push.ListarAsync(projetos.Marcados);
        var pedido = new PedidoDeFormulario("Push", "O que será enviado ao origin (já consultado com fetch).",
        [
            new CampoInformacao("previa", "Projetos", [.. previa.Select(LinhaDoPush)]),
            new CampoCaixa("publicar", "Publicar branches novas no origin", true),
            new CampoCaixa("forcar", "Forçar com --force-with-lease (pede confirmação; use após rebase ou amend)"),
        ], "Push");
        if (await janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, pedido) is not RespostaDeFormulario r) return;
        await acoes.Rodar(push, new OpcoesDePush(r.Caixa("forcar"), r.Caixa("publicar")));
    });

    static PedidoDeFormulario Aviso(string titulo, string texto, IEnumerable<string> linhas) =>
        new(titulo, texto, [new CampoInformacao("linhas", "Projetos", [.. linhas])], "Fechar", Altura: 420);

    static string LinhaDoCommit(PreviaDeCommit p) =>
        p.Ignorado ? $"{p.Projeto.Nome}: ignorado - {p.Situacao}"
        : p.SemAlteracoes ? $"{p.Projeto.Nome} ({p.Branch}): sem alterações"
        : $"{p.Projeto.Nome} ({p.Branch}): {p.Versionados} versionado(s), {p.NaoVersionados} novo(s)";

    static string LinhaDoPush(PreviaDePush p) =>
        p.Ignorado ? $"{p.Projeto.Nome}: ignorado - {p.Aviso}"
        : $"{p.Projeto.Nome} ({p.Branch}): ↑{p.Ahead} ↓{p.Behind}" + (p.Aviso.Length > 0 ? $" - {p.Aviso}" : "");
}
