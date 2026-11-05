using System.Windows;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.Checkout;
using Menage.Core.Operacoes.DefinirOrigin;
using Menage.Core.Operacoes.Diagnostico;
using Menage.Core.Operacoes.Status;
using Menage.Core.Operacoes.Ferramentas;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Menage.Core.Saida;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>Diagnóstico (divergência, arquivos alterados) e Ferramentas (tag em massa, branch de todos, não enviados, Git Doctor, vários remotos, copiar).</summary>
public sealed class AcoesDeFerramentas(
    AcoesGit acoes, IProjetos projetos, CoordenadorDeOperacoes coordenador, IServicoDeJanelas janelas, SaidaDeOperacoes saida, SugestoesDeBranches sugestoes,
    DivergenciaEmLote divergencia, ArquivosAlteradosEmLote arquivos, CommitsNaoEnviadosEmLote naoEnviados, TagEmMassaEmLote tag, CheckoutEmLote checkout,
    GitDoctorEmLote doctor, PushMultiRemotoEmLote pushMulti, CopiaDosMarcados copia, DefinirOriginEmLote definirOrigin, StatusEmLote status)
{
    static readonly string[] NomesDasFerramentas =
    [
        "Criar tag em massa", "Mudar a branch de TODOS os projetos", "Commits não enviados", "Git Doctor (descartar alterações)",
        "Push em vários remotos", "Copiar dos marcados", "Definir o origin da branch (sem upstream)",
    ];

    bool Livre => projetos.Marcados.Count > 0 && !coordenador.EmAndamento;

    Task<object?> Perguntar(PedidoDeFormulario pedido) => janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, pedido);

    public Task DiagnosticoAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Diagnóstico", "Só leitura: nada é alterado nos projetos.",
        [
            new CampoCaixa("diverg", "Divergência com o origin (↑ à frente, ↓ atrás e os commits de cada lado)", true),
            new CampoCaixa("fetch", "Atualizar com o origin antes (fetch)", true),
            new CampoCaixa("arqs", "Arquivos alterados", true),
            new CampoTexto("filtro", "Filtrar arquivos por trecho do caminho (opcional)"),
        ], "Diagnosticar", Altura: 440)) is not RespostaDeFormulario r) return;

        var linhas = new List<string>();
        if (r.Caixa("diverg")) linhas.AddRange(await ConsultarAsync(divergencia, new OpcoesDeDivergencia(r.Caixa("fetch"))));
        if (r.Caixa("arqs")) linhas.AddRange(await ConsultarAsync(arquivos, new OpcoesDeArquivosAlterados(r.Texto("filtro"))));
        await MostrarAsync("Diagnóstico", linhas);
    });

    public Task FerramentasAsync() => acoes.Proteger(async () =>
    {
        if (coordenador.EmAndamento) return;
        if (await Perguntar(new("Ferramentas", "Escolha a ferramenta; ela pergunta o que precisa em seguida.",
            [new CampoOpcoes("ferramenta", "Ferramenta", NomesDasFerramentas)], "Abrir", Altura: 300)) is not RespostaDeFormulario r) return;

        var ferramenta = Array.IndexOf(NomesDasFerramentas, r.Texto("ferramenta"));
        var tarefa = ferramenta switch
        {
            0 => TagAsync(),
            1 => MudarBranchDeTodosAsync(),
            2 => NaoEnviadosAsync(),
            3 => GitDoctorAsync(),
            4 => PushMultiAsync(),
            5 => CopiarAsync(),
            6 => DefinirOriginAsync(),
            _ => Task.CompletedTask,
        };
        await tarefa;
    });

    public Task StatusAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        await acoes.Rodar(status, new OpcoesDeStatus());
    });

    public Task DefinirOriginAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Definir o origin da branch", "Vincula a branch atual de cada projeto marcado ao origin (a que já tem origin é ignorada).",
            [new CampoCaixa("publicar", "Publicar no origin as branches que ainda não existem lá (git push -u origin)", true)], "Ver prévia", Altura: 320)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(definirOrigin, new OpcoesDeDefinirOrigin(r.Caixa("publicar")));
    });

    public Task TagAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Criar tag em massa", "Cria a mesma tag em cada projeto marcado. Uma tag que já existe nunca é movida.",
        [
            new CampoTexto("nome", "Nome da tag", Obrigatorio: true),
            new CampoTexto("mensagem", "Mensagem (opcional; com mensagem a tag é anotada)"),
            new CampoTexto("de", "A partir de (branch, tag ou commit; vazio = commit atual)"),
            new CampoCaixa("enviar", "Enviar ao origin", true),
        ], "Criar tag", Altura: 460)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(tag, new OpcoesDeTag(r.Texto("nome"), r.Texto("mensagem"), r.Texto("de"), r.Caixa("enviar")));
    });

    public Task MudarBranchDeTodosAsync() => acoes.Proteger(async () =>
    {
        if (coordenador.EmAndamento || projetos.Todos.Count == 0) return;
        var bases = await sugestoes.BasesAsync(projetos.Todos);
        if (await Perguntar(new("Mudar a branch de TODOS", $"Checkout em todos os {projetos.Todos.Count} projetos da lista, marcados ou não.",
        [
            new CampoEscolha("branch", "Branch ou tag", bases),
            new CampoCaixa("stash", "Guardar pendências ao sair da branch e restaurar ao voltar (auto-stash)", true),
        ], "Mudar de branch", Altura: 400)) is not RespostaDeFormulario r || r.Texto("branch").Length == 0) return;
        await acoes.Rodar(checkout, new OpcoesDeCheckout(r.Texto("branch"), r.Caixa("stash"), TodosOsProjetos: true));
    });

    public Task NaoEnviadosAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        await MostrarAsync("Commits não enviados", await ConsultarAsync(naoEnviados, new OpcoesDeNaoEnviados()));
    });

    public Task GitDoctorAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Git Doctor", "Descarta alterações locais dos projetos marcados. Por padrão, tudo é guardado antes num stash.",
        [
            new CampoCaixa("reset", "Descartar alterações em arquivos versionados (reset --hard)", true),
            new CampoCaixa("clean", "Apagar arquivos novos não versionados (clean -fd)"),
            new CampoCaixa("stash", "Guardar tudo num stash de segurança antes (recomendado)", true),
        ], "Continuar", Altura: 400)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(doctor, new OpcoesDoGitDoctor(r.Caixa("reset"), r.Caixa("clean"), r.Caixa("stash")));
    });

    public Task PushMultiAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        if (await Perguntar(new("Push em vários remotos", "Envia a branch atual de cada projeto para cada remote informado.",
        [
            new CampoTexto("remotes", "Remotes (separados por vírgula ou espaço)", "origin", Obrigatorio: true),
            new CampoCaixa("tags", "Enviar também as tags"),
            new CampoCaixa("forcar", "Forçar com --force-with-lease (pede confirmação)"),
        ], "Enviar", Altura: 420)) is not RespostaDeFormulario r) return;
        await acoes.Rodar(pushMulti, new OpcoesDePushMulti(Excluir.Separar(r.Texto("remotes")), r.Caixa("tags"), r.Caixa("forcar")));
    });

    public Task CopiarAsync() => acoes.Proteger(async () =>
    {
        if (!Livre) return;
        string[] tipos = ["Branches atuais", "Hashes do HEAD", "Links para abrir PR", "URLs dos repositórios"];
        if (await Perguntar(new("Copiar dos marcados", "Copia para a área de transferência, uma linha por projeto.",
            [new CampoOpcoes("tipo", "O que copiar", tipos)], "Copiar", Altura: 300)) is not RespostaDeFormulario r) return;

        var linhas = await copia.ColetarAsync((TipoDeCopia)Math.Max(0, Array.IndexOf(tipos, r.Texto("tipo"))), projetos.Marcados);
        var texto = string.Join(Environment.NewLine, linhas);
        var copiou = Application.Current.Dispatcher.Invoke(() => TentarCopiar(texto));
        saida.Escrever(copiou ? $"Copiado para a área de transferência ({linhas.Count} linha(s))." : "Não foi possível acessar a área de transferência.", copiou ? TipoDeLinha.Ok : TipoDeLinha.Aviso);
        await MostrarAsync("Copiar dos marcados", linhas);
    });

    static bool TentarCopiar(string texto)
    {
        try
        {
            Clipboard.SetText(texto);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // Roda uma operação só de leitura pelo coordenador e devolve as linhas "projeto: mensagem" com o detalhe de cada uma.
    async Task<IReadOnlyList<string>> ConsultarAsync<TOpcoes>(OperacaoEmLote<TOpcoes, ResultadoDeConsulta> operacao, TOpcoes opcoes)
    {
        var resultado = await coordenador.ExecutarAsync(operacao, opcoes);
        if (resultado is null || !resultado.EhOk) return [];

        var linhas = new List<string> { $"== {operacao.Nome} ==" };
        foreach (var item in resultado.Valor.Itens)
        {
            linhas.Add($"{item.Projeto.Nome}: {item.Resultado.Mensagem}");
            linhas.AddRange((item.Resultado.Dados?.Linhas ?? []).Select(l => "    " + l));
        }

        return linhas;
    }

    async Task MostrarAsync(string titulo, IReadOnlyList<string> linhas)
    {
        foreach (var linha in linhas) saida.Escrever(linha, TipoDeLinha.Normal);
        await Perguntar(new(titulo, linhas.Count == 0 ? "Nada a mostrar." : "Também está na Saída.", [new CampoInformacao("l", "Resultado", linhas)], "Fechar", Largura: 760, Altura: 560));
    }

    static class Excluir
    {
        public static IReadOnlyList<string> Separar(string texto) =>
            [.. texto.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];
    }
}
