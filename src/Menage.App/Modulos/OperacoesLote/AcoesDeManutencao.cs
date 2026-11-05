using System.Globalization;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.BuscarCommit;
using Menage.Core.Operacoes.CommitPom;
using Menage.Core.Operacoes.DesfazerCommit;
using Menage.Core.Operacoes.Limpeza;
using Menage.Core.Interacao;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Menage.Core.Saida;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>Limpeza de branches, Desfazer commit, Commit pom e Buscar commit.</summary>
public sealed class AcoesDeManutencao(
    AcoesGit acoes, IProjetos projetos, CoordenadorDeOperacoes coordenador, IServicoDeJanelas janelas, IServicoDeDialogos dialogos, SaidaDeOperacoes saida,
    SugestoesDeBranches sugestoes, LimpezaEmLote limpeza, DesfazerCommitEmLote desfazer, CommitPomEmLote pom, BuscarCommitEmLote buscar)
{
    bool Livre => projetos.Marcados.Count > 0 && !coordenador.EmAndamento;

    Task<object?> Perguntar(PedidoDeFormulario pedido) => janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, pedido);

    public Task LimpezaAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        var bases = await sugestoes.BasesAsync(projetos.Marcados);
        if (await Perguntar(new("Limpeza de branches", "Acha branches já mescladas na base ou sem commits há muito tempo. Nada é apagado antes da prévia.",
        [
            new CampoEscolha("base", "Base (vazio = branch padrão do origin)", bases),
            new CampoCaixa("mescladas", "Branches já mescladas na base", true),
            new CampoCaixa("inativas", "Branches inativas (sem commits há N dias)"),
            new CampoTexto("dias", "Dias de inatividade", "90"),
            new CampoCaixa("local", "Neste computador (local)", true),
            new CampoCaixa("remoto", "No origin (remoto; faz fetch --prune)"),
        ], "Ver prévia", Altura: 560)) is not RespostaDeFormulario r) return;

        var dias = int.TryParse(r.Texto("dias"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 90;
        var opcoes = new OpcoesDeLimpeza(r.Texto("base"), r.Caixa("mescladas"), r.Caixa("inativas"), dias, r.Caixa("local"), r.Caixa("remoto"));
        saida.Escrever("Limpeza: procurando branches nos projetos marcados...", TipoDeLinha.Fraco);
        var previa = await limpeza.ListarAsync(projetos.Marcados, opcoes);
        foreach (var aviso in previa.Avisos) saida.Escrever(aviso, TipoDeLinha.Aviso);
        if (previa.Itens.Count == 0)
        {
            await Perguntar(new("Limpeza de branches", "Nenhuma branch encontrada com esses critérios.", [new CampoInformacao("avisos", "Avisos", previa.Avisos)], "Fechar", Altura: 340));
            return;
        }

        await ExecutarLimpezaAsync(opcoes, previa);
    });

    public Task DesfazerAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        var previa = await desfazer.ListarAsync(projetos.Marcados);
        var enviados = previa.Where(p => p.Erro.Length == 0 && p.Enviado).ToList();
        var linhas = previa.Select(p => p.Erro.Length > 0 ? $"{p.Projeto.Nome}: ignorado - {p.Erro}"
            : $"{p.Projeto.Nome} ({p.Branch}): {p.Curto} \"{p.Assunto}\" - {p.Autor}, {p.Data}" + (p.Aviso.Length > 0 ? $" [{p.Aviso}]" : "")).ToList();
        if (previa.All(p => p.Erro.Length > 0))
        {
            await Perguntar(new("Desfazer último commit", "Nenhum projeto marcado pode ter o commit desfeito.", [new CampoInformacao("l", "Projetos", linhas)], "Fechar", Altura: 400));
            return;
        }

        var campos = new List<CampoDeFormulario> { new CampoInformacao("l", "Último commit de cada projeto", linhas) };
        if (enviados.Count > 0)
            campos.Add(new CampoLista("enviados", "Commits que JÁ estão no origin (desmarcados não são desfeitos; o próximo push exigirá force)",
                [.. enviados.Select(p => new ItemMarcavel(p.Projeto.Caminho, $"{p.Projeto.Nome}: {p.Curto}", false))]));
        campos.Add(new CampoCaixa("mixed", "Tirar as alterações da staging (reset --mixed); senão ficam na staging (--soft)"));
        if (await Perguntar(new("Desfazer último commit", "reset HEAD~1: o código fica no seu computador; o commit sai do histórico da branch.", campos, "Desfazer", Altura: 580)) is not RespostaDeFormulario r) return;

        var esperados = previa.Where(p => p.Erro.Length == 0).ToDictionary(p => p.Projeto.Caminho, p => p.Sha);
        await acoes.Rodar(desfazer, new OpcoesDeDesfazer(r.Caixa("mixed"), r.Lista("enviados").ToHashSet(StringComparer.OrdinalIgnoreCase), esperados));
    });

    public Task CommitPomAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        var previa = await pom.ListarAsync(projetos.Marcados);
        if (previa.Count == 0)
        {
            saida.Escrever("Commit pom: nenhum projeto marcado tem pom.xml alterado.", TipoDeLinha.Aviso);
            await Perguntar(new("Commit pom.xml", "Nenhum projeto marcado tem pom.xml alterado.", [], "Fechar", Altura: 260));
            return;
        }

        var linhas = previa.SelectMany(p => p.Arquivos.Select(a => $"{p.Projeto.Nome} ({p.Branch}): {a}")).ToList();
        if (await Perguntar(new("Commit pom.xml", "Só o(s) pom.xml entram no commit; o resto fica como está.",
        [
            new CampoInformacao("l", "Arquivos", linhas),
            new CampoTexto("mensagem", "Mensagem", "pom.xml", Obrigatorio: true),
            new CampoCaixa("push", "Fazer push em seguida"),
        ], "Commit", Altura: 470)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(pom, new OpcoesDeCommitPom(r.Texto("mensagem"), r.Caixa("push")));
    });

    public Task BuscarAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Buscar commit", "Procura por hash (7 caracteres ou mais) ou por trecho da mensagem, em todas as branches dos projetos marcados.",
        [
            new CampoTexto("termo", "Hash ou trecho da mensagem (ex.: VENDAS-22222)", Obrigatorio: true),
            new CampoCaixa("fetch", "Atualizar com o origin antes (fetch)"),
        ], "Buscar", Altura: 360)) is not RespostaDeFormulario r) return;

        var resultado = await coordenador.ExecutarAsync(buscar, new OpcoesDeBuscaDeCommit(r.Texto("termo"), r.Caixa("fetch")));
        if (resultado is null || !resultado.EhOk) return;

        var linhas = resultado.Valor.Itens.Where(i => i.Resultado.Dados is not null).SelectMany(i => i.Resultado.Dados!.Achados.Select(a =>
            $"{i.Projeto.Nome}: {a.Commit.Curto} {a.Commit.Data} {a.Commit.Autor} - {a.Commit.Assunto}  [{string.Join(", ", a.Branches)}]")).ToList();
        foreach (var linha in linhas) saida.Escrever(linha, TipoDeLinha.Normal);
        await Perguntar(new("Buscar commit", linhas.Count == 0 ? "Nenhum commit encontrado." : $"{linhas.Count} commit(s) encontrado(s) (também na Saída).",
            [new CampoInformacao("achados", "Commits", linhas)], "Fechar", Altura: 480));
    });

    async Task ExecutarLimpezaAsync(OpcoesDeLimpeza opcoes, PreviaDeLimpeza previa)
    {
        var campos = new List<CampoDeFormulario>();
        if (previa.Avisos.Count > 0) campos.Add(new CampoInformacao("avisos", "Avisos", previa.Avisos));
        campos.Add(new CampoLista("itens", "Branches encontradas (mescladas vêm marcadas; as só inativas, não)",
            [.. previa.Itens.Select(i => new ItemMarcavel(i.Chave, $"{i.Projeto.Nome}: {i.Alvo.Nome} - {i.Motivo} ({i.Autor}, {i.Ultimo:dd/MM/yyyy})", i.MarcarPadrao))]));
        campos.Add(new CampoCaixa("backup", "Fazer backup das que não estão mescladas (backup/<nome>-<data>)", true));
        if (await Perguntar(new("Limpeza de branches", "Marque o que deve ser excluído.", campos, "Excluir marcadas", Largura: 760, Altura: 640)) is not RespostaDeFormulario r) return;

        var selecionadas = r.Lista("itens");
        var final = opcoes with { Selecionadas = selecionadas, Backup = r.Caixa("backup") };
        if (!final.Backup)
        {
            if (!await dialogos.ConfirmarAsync(new Previa("Excluir sem backup", "As branches não mescladas serão apagadas SEM backup. Continuar?", [$"{selecionadas.Count} branch(es)"]))) return;
            final = final with { ConfirmouExtra = true };
        }

        await acoes.Rodar(limpeza, final);
    }
}
