using System.IO;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Conflito;
using Menage.Core.Diff;
using Menage.TestKit;

namespace Menage.Diff.Roteiro;

/// <summary>Resolve conflitos pela janela real, fora da tela, num repositório descartável. Sem mouse nem teclado de verdade.</summary>
public sealed class RoteiroConflito(string saida, bool escuro)
{
    readonly Dictionary<string, object?> _r = [];

    public async Task<object> RodarAsync()
    {
        using var repo = await Task.Run(() =>
        {
            var novo = new RepoDeTeste();
            novo.MontarConflito();
            return novo;
        });
        var dialogos = new DialogosDoRoteiro();
        var janela = new JanelaConflito.Fabrica(new ServicoConflito(GitDeTeste.Criar()), dialogos, dialogos).Criar(repo.Caminho);
        Tela.EsconderDoUsuario(janela);
        janela.Show();
        await Execucao.Esperar(1500);
        await Selecionar(janela, "Conflito.java");
        var p = janela.PainelDoConflito;
        Capturar(janela, "inicio");
        _r["inicial"] = new { p.Pendentes, estado = p.TextoDoEstado };
        await Escolhas(janela, p);
        await Fechamento(janela, dialogos);
        await Salvar(janela, p, repo, dialogos);
        await Binario(janela, dialogos, repo);
        _r["erros_mostrados"] = dialogos.Erros;
        await dialogos.AutorizarFechamento(janela);
        return _r;
    }

    async Task Escolhas(JanelaConflito janela, PainelConflito p)
    {
        p.UsarNoAtual(EscolhaConflito.Entrando);
        await Execucao.Esperar(700);
        _r["usar_entrando"] = new { p.Pendentes, estado = p.TextoDoEstado, temEntrando = Texto(p).Contains("entrando: linha 51", StringComparison.Ordinal) };
        Capturar(janela, "escolhido");
        p.Resultado.Editor.Undo();
        await Execucao.Esperar(700);
        _r["desfazer"] = new { p.Pendentes, voltou = !Texto(p).Contains("entrando: linha 51", StringComparison.Ordinal) };
        p.Resultado.Editor.Redo();
        await Execucao.Esperar(700);
        _r["refazer"] = new { p.Pendentes, refez = Texto(p).Contains("entrando: linha 51", StringComparison.Ordinal) };
        p.DesfazerEscolha(1);
        await Execucao.Esperar(700);
        _r["desfazer_escolha"] = new { p.Pendentes, marca = JuncaoConflito.Pendentes(p.LinhasDoResultado()).ContainsKey(1) };
        p.Ir(1);
        await Execucao.Esperar(500);
        _r["proximo"] = p.TextoDoEstado;
    }

    async Task Fechamento(JanelaConflito janela, DialogosDoRoteiro dialogos)
    {
        dialogos.RespostaConfirmar = false;
        janela.Close();
        await Execucao.Esperar(300);
        _r["fechar_sem_salvar"] = new { perguntou = dialogos.Confirmacoes > 0, continuaAberta = janela.IsVisible };
    }

    async Task Salvar(JanelaConflito janela, PainelConflito p, RepoDeTeste repo, DialogosDoRoteiro dialogos)
    {
        await janela.SalvarAsync(); // ainda pendente: tem que recusar
        _r["salvar_com_pendente"] = new { avisou = dialogos.Erros.Count, status = await Status(repo) };
        var escolhas = new[] { EscolhaConflito.AtualDepoisEntrando, EscolhaConflito.EntrandoDepoisAtual, EscolhaConflito.Atual };
        for (var i = 0; p.Pendentes > 0 && i < 10; i++)
        {
            p.Usar(JuncaoConflito.Pendentes(p.LinhasDoResultado()).Keys.Min(), escolhas[i % escolhas.Length]);
            await Execucao.Esperar(600);
        }
        Capturar(janela, "resolvido");
        await janela.SalvarAsync();
        await Execucao.Esperar(900);
        var bytes = File.ReadAllBytes(Path.Combine(repo.Caminho, "Conflito.java"));
        _r["salvo"] = new { temBom = Codificacoes.TemBom(bytes), fim = ArquivoTexto.De(bytes).FimDominante, status = await Status(repo) };
    }

    async Task Binario(JanelaConflito janela, DialogosDoRoteiro dialogos, RepoDeTeste repo)
    {
        dialogos.RespostaLado = LadoEscolhido.Entrando;
        await Selecionar(janela, "imagem.bin");
        await Execucao.Esperar(1000);
        _r["binario"] = new { perguntou = dialogos.Perguntas, bytes = File.ReadAllBytes(Path.Combine(repo.Caminho, "imagem.bin")), status = await Status(repo) };
        await Selecionar(janela, "Outro.java");
        await Execucao.Esperar(800);
        Capturar(janela, "outro");
    }

    static string Texto(PainelConflito p) => string.Join('\n', p.LinhasDoResultado());

    static Task<string> Status(RepoDeTeste repo) => Task.Run(() => repo.Repo.Rodar("status", "--porcelain"));

    static async Task Selecionar(JanelaConflito janela, string arquivo)
    {
        janela.ListaDeArquivos.Selecionar(arquivo);
        await Execucao.Esperar(1000);
    }

    void Capturar(JanelaConflito janela, string nome) => Tela.Salvar(Tela.Renderizar(janela), Path.Combine(saida, $"conflito-{(escuro ? "escuro" : "claro")}-{nome}.png"));
}

/// <summary>Responde sozinho às perguntas e registra o que foi perguntado.</summary>
public sealed class DialogosDoRoteiro : Core.Interacao.IServicoDeDialogos, IPerguntaDeLado
{
    public bool RespostaConfirmar { get; set; }

    public LadoEscolhido? RespostaLado { get; set; }

    public int Confirmacoes { get; private set; }

    public int Perguntas { get; private set; }

    public List<string> Erros { get; } = [];

    public async Task AutorizarFechamento(JanelaConflito janela)
    {
        RespostaConfirmar = true;
        janela.Close();
        await Execucao.Esperar(200);
    }

    public Task<bool> ConfirmarAsync(Core.Interacao.Previa previa)
    {
        Confirmacoes++;
        return Task.FromResult(RespostaConfirmar);
    }

    public Task InformarAsync(string titulo, string mensagem) => Task.CompletedTask;

    public Task MostrarErroAsync(Core.Erro erro)
    {
        Erros.Add(erro.ToString());
        return Task.CompletedTask;
    }

    public Task MostrarResumoAsync(Core.Operacoes.IResumoDoLote resumo) => Task.CompletedTask;

    public Task<LadoEscolhido?> PerguntarAsync(string titulo, string mensagem, (string Texto, LadoEscolhido Lado) primeira, (string Texto, LadoEscolhido Lado) segunda)
    {
        Perguntas++;
        return Task.FromResult(RespostaLado);
    }
}
