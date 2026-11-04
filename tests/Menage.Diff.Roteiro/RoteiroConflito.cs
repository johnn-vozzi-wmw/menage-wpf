using System.IO;
using System.Windows;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Conflito;
using Menage.Core.Diff;
using Menage.TestKit;

namespace Menage.Diff.Roteiro;

/// <summary>Resolve um conflito pela janela real, com cliques e teclas de verdade, num repositório descartável.</summary>
public sealed class RoteiroConflito(string saida)
{
    const byte Alt = 0x12;
    const byte Control = 0x11;
    const byte Tecla1 = 0x31;
    const byte Tecla2 = 0x32;
    const byte Tecla3 = 0x33;
    const byte TeclaY = 0x59;
    const byte TeclaZ = 0x5A;

    readonly Dictionary<string, object?> _r = [];

    public async Task<object> RodarAsync()
    {
        using var repo = await Task.Run(() =>
        {
            var novo = new RepoDeTeste();
            novo.MontarConflito();
            return novo;
        });
        var git = GitDeTeste.Criar();
        var dialogos = new DialogosDoRoteiro();
        var janela = new JanelaConflito.Fabrica(new ServicoConflito(git), dialogos, dialogos).Criar(repo.Caminho);
        janela.Topmost = true;
        janela.Show();
        await Execucao.Esperar(1500);
        await Selecionar(janela, "Conflito.java");
        var p = janela.PainelDoConflito;
        await Capturar(janela, "claro");
        _r["inicial"] = new { p.Pendentes, estado = p.TextoDoEstado };
        await Escolhas(janela, p);
        await DuploClique(janela, p);
        await Fechamento(janela, dialogos);
        await Salvar(janela, p, repo);
        await Binario(janela, dialogos, repo);
        janela.Close();
        return _r;
    }

    async Task Escolhas(JanelaConflito janela, PainelConflito p)
    {
        await Clicar(janela, p.Resultado, 2);
        if (!Tela.EmPrimeiroPlano(janela))
        {
            _r["abortado"] = "janela de teste fora do primeiro plano";
            return;
        }
        Tela.Teclar(Tecla1, Alt);
        await Execucao.Esperar(700);
        _r["alt1"] = new { p.Pendentes, estado = p.TextoDoEstado, temEntrando = Texto(p).Contains("entrando: linha 51", StringComparison.Ordinal) };
        await Capturar(janela, "claro-escolhido");
        Tela.Teclar(TeclaZ, Control);
        await Execucao.Esperar(700);
        _r["ctrl_z"] = new { p.Pendentes, voltou = !Texto(p).Contains("entrando: linha 51", StringComparison.Ordinal) };
        Tela.Teclar(TeclaY, Control);
        await Execucao.Esperar(700);
        _r["ctrl_y"] = new { p.Pendentes, refez = Texto(p).Contains("entrando: linha 51", StringComparison.Ordinal) };
        p.DesfazerEscolha(1);
        await Execucao.Esperar(700);
        _r["desfazer_escolha"] = new { p.Pendentes, marca = JuncaoConflito.Pendentes(p.LinhasDoResultado()).ContainsKey(1) };
        Tela.Teclar(0x76); // F7 vai ao conflito seguinte, realçado
        await Execucao.Esperar(500);
        _r["f7"] = p.TextoDoEstado;
    }

    async Task DuploClique(JanelaConflito janela, PainelConflito p)
    {
        var alvo = p.Atual;
        var posicao = AnaliseConflito.Analisar(janela.PainelDoConflito.Arquivo!.Atual, janela.PainelDoConflito.Arquivo.Entrando, p.LinhasDoResultado()).Pendentes[0];
        var y = (posicao.AtualIni + 0.5) * alvo.AlturaLinha - alvo.Vista.VerticalOffset;
        var ponto = alvo.Vista.PointToScreen(new Point(100, y));
        var pendentes = p.Pendentes;
        if (y < 0 || y > alvo.Vista.ActualHeight || !Tela.PontoEstaNaJanela(janela, ponto))
        {
            _r["duplo_clique"] = "bloco fora da tela: não clicado";
            return;
        }
        Tela.MoverMouse(ponto);
        await Execucao.Esperar(80);
        for (var i = 0; i < 2; i++)
        {
            Tela.Apertar();
            Tela.Soltar();
            await Task.Delay(40);
        }
        await Execucao.Esperar(800);
        _r["duplo_clique"] = new { antes = pendentes, depois = p.Pendentes };
    }

    async Task Fechamento(JanelaConflito janela, DialogosDoRoteiro dialogos)
    {
        dialogos.RespostaConfirmar = false;
        janela.Close();
        await Execucao.Esperar(300);
        _r["fechar_sem_salvar"] = new { perguntou = dialogos.Confirmacoes > 0, continuaAberta = janela.IsVisible };
    }

    async Task Salvar(JanelaConflito janela, PainelConflito p, RepoDeTeste repo)
    {
        await janela.SalvarAsync(); // ainda pendente: tem que recusar
        _r["salvar_com_pendente"] = new { recusou = (await Status(repo)).Contains("UU Conflito.java", StringComparison.Ordinal) };
        var escolhas = new[] { EscolhaConflito.AtualDepoisEntrando, EscolhaConflito.EntrandoDepoisAtual, EscolhaConflito.Atual };
        var i = 0;
        while (p.Pendentes > 0 && i < 10)
        {
            var numero = JuncaoConflito.Pendentes(p.LinhasDoResultado()).Keys.Min();
            p.Usar(numero, escolhas[i++ % escolhas.Length]);
            await Execucao.Esperar(600);
        }
        await Capturar(janela, "claro-resolvido");
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
        await Execucao.Tema2(janela, escuro: true);
        await Selecionar(janela, "Outro.java");
        await Execucao.Esperar(1000);
        await Capturar(janela, "escuro");
        await Execucao.Tema2(janela, escuro: false);
    }

    static string Texto(PainelConflito p) => string.Join('\n', p.LinhasDoResultado());

    static Task<string> Status(RepoDeTeste repo) => Task.Run(() => repo.Repo.Rodar("status", "--porcelain"));

    static async Task Selecionar(JanelaConflito janela, string arquivo)
    {
        janela.ListaDeArquivos.Selecionar(arquivo);
        await Execucao.Esperar(1000);
    }

    static async Task Clicar(JanelaConflito janela, App.Modulos.Diff.Texto.PainelTexto painel, int linha)
    {
        var ponto = painel.Vista.PointToScreen(new Point(100, (linha + 0.5) * painel.AlturaLinha));
        if (!Tela.PontoEstaNaJanela(janela, ponto)) return;
        Tela.MoverMouse(ponto);
        await Execucao.Esperar(60);
        Tela.Apertar();
        Tela.Soltar();
        await Execucao.Esperar(200);
    }

    async Task Capturar(JanelaConflito janela, string nome)
    {
        await Execucao.Esperar(300);
        Tela.Salvar(Tela.Capturar(Tela.AreaDe(janela)), Path.Combine(saida, $"conflito-{nome}.png"));
    }
}

/// <summary>Responde sozinho às perguntas e registra o que foi perguntado.</summary>
public sealed class DialogosDoRoteiro : Core.Interacao.IServicoDeDialogos, IPerguntaDeLado
{
    public bool RespostaConfirmar { get; set; }

    public LadoEscolhido? RespostaLado { get; set; }

    public int Confirmacoes { get; private set; }

    public int Perguntas { get; private set; }

    public List<string> Erros { get; } = [];

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
