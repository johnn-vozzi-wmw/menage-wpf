using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.Beta;
using Menage.Core.Entregas;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Geração: confirmações, execução pelo <see cref="ExecutorDoGerador"/>, parar, gatilho e entregas.</summary>
public sealed partial class GeradorBetaViewModel
{
    readonly ExecutorDoGerador _executor;
    readonly ServicoDeEntrega _entrega;
    readonly Dictionary<string, bool> _resultados = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _oferecidos = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim _umaPerguntaPorVez = new(1, 1);
    CancellationTokenSource _pararApos = new();
    CancellationTokenSource _abortar = new();
    string? _pastaDaGeracao;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoGerar))]
    bool _executando;

    [ObservableProperty]
    bool _parando;

    [ObservableProperty]
    EntregaPronta? _entregaPronta;

    /// <summary>A tela segue a linha em execução quando "Acompanhar execução" está ligado.</summary>
    [ObservableProperty]
    LinhaDeBeta? _selecionadaNaTela;

    public string TextoGerar => Deploy ? "Gerar beta (deploy)" : "Gerar beta";

    public void Dispose()
    {
        _pararApos.Dispose();
        _abortar.Dispose();
        _umaPerguntaPorVez.Dispose();
    }

    public async Task GerarAsync()
    {
        if (Executando) return;
        var pedido = await _preparador.MontarAsync(Ordem, Deploy, BaseSelecionada.Caminho);
        if (!pedido.EhOk) { await _dialogos.MostrarErroAsync(pedido.Erro); return; }
        if (!await ConfirmarAsync(pedido.Valor)) return;

        _repositorio.LembrarEscolha(BaseSelecionada.Caminho, Deploy, Ordem);
        Preparar(pedido.Valor);
        _executor.StatusMudou += AoMudarStatus;
        ResumoDaGeracao resumo;
        try { resumo = await Task.Run(() => _executor.ExecutarAsync(pedido.Valor, new SinaisDoLote(_abortar.Token, _pararApos.Token))); }
        finally
        {
            _executor.StatusMudou -= AoMudarStatus;
            Executando = false;
            Parando = false;
        }

        await ConcluirAsync(resumo, pedido.Valor.Deploy);
    }

    async Task<bool> ConfirmarAsync(PedidoDeGeracao pedido)
    {
        var nomes = pedido.Planos.Select(p => $"{p.Nome}  ({p.Branch})").ToList();
        if (pedido.Deploy && !await _dialogos.ConfirmarAsync(new Previa("Confirmar deploy", "Os projetos abaixo serão publicados no Nexus.", nomes))) return false;
        if (Process.GetProcessesByName("eclipse").Length == 0) return true;
        return await _dialogos.ConfirmarAsync(new Previa("Eclipse aberto",
            "O Eclipse está aberto. Se o Build Automatically estiver ligado ele pode travar o clean (arquivos em uso na pasta target). Continuar mesmo assim?", []));
    }

    void Preparar(PedidoDeGeracao pedido)
    {
        _pastaDaGeracao = pedido.PastaDeLogs;
        _resultados.Clear();
        _oferecidos.Clear();
        _pararApos = new CancellationTokenSource();
        _abortar = new CancellationTokenSource();
        EntregaPronta = null;
        var nomes = pedido.Planos.Select(p => p.Nome).ToHashSet();
        foreach (var l in Linhas) l.Reiniciar(nomes.Contains(l.Nome));
        Executando = true;
        Mensagem = $"Fila iniciada ({(pedido.Deploy ? "DEPLOY" : "install")}): {string.Join(" > ", pedido.Planos.Select(p => p.Nome))}   | logs: {pedido.PastaDeLogs}";
    }

    void AoMudarStatus(StatusDoProjeto status) => Application.Current.Dispatcher.InvokeAsync(async () =>
    {
        var linha = Linhas.FirstOrDefault(l => l.Nome == status.Projeto);
        if (linha is null) return;
        linha.Aplicar(status);
        if (status.Estado == FaseDaGeracao.Executando && Log.Acompanhar) { SelecionadaNaTela = linha; Log.Mostrar(linha); }
        if (status.Estado is not (FaseDaGeracao.Ok or FaseDaGeracao.Erro or FaseDaGeracao.Interrompido)) return;
        _resultados[status.Projeto] = status.Estado == FaseDaGeracao.Ok;
        if (status.Estado == FaseDaGeracao.Ok)
            await OferecerAsync(PreparadorDeEntrega.PorGatilho(Configuracao.Entregas, status.Projeto, _resultados), aposBeta: true);
    });

    public void Tique()
    {
        foreach (var l in Linhas) l.AtualizarTempoCorrido();
        Log.Tique();
    }

    public void PararAposAtual()
    {
        if (!Executando) return;
        _pararApos.Cancel();
        Parando = true;
        Mensagem = "A fila vai parar depois do projeto atual.";
    }

    public void InterromperAgora()
    {
        if (!Executando) return;
        _pararApos.Cancel();
        _abortar.Cancel();
        Parando = true;
        Mensagem = "Interrompendo o projeto em andamento...";
    }

    async Task ConcluirAsync(ResumoDaGeracao resumo, bool deploy)
    {
        Mensagem = $"Fila encerrada: ok {resumo.Oks}, erro {resumo.Falhas}, não executados {resumo.NaoExecutados}   | logs: {resumo.PastaDeLogs}";
        var linhas = resumo.Projetos.Select(p => $"{p.Projeto}   {LinhaDeBeta.Formatar(p.Tempo)}  •  {p.Resumo}");
        await _dialogos.InformarAsync($"Beta finalizada ({(deploy ? "deploy" : "install")})",
            string.Join(Environment.NewLine, linhas) + $"{Environment.NewLine}{Environment.NewLine}Logs: {resumo.PastaDeLogs}");
        await OferecerAsync([.. PreparadorDeEntrega.NoFim(Configuracao.Entregas, _resultados).Where(p => !_oferecidos.Contains(p.Nome))], aposBeta: true);
    }

    // ---- entregas

    string? PastaDoProjeto(string nome) => Linhas.FirstOrDefault(l => l.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase))?.Pasta;

    /// <summary>Pergunta (com a prévia) e prepara cada pacote. Uma pergunta por vez, mesmo com a fila rodando.</summary>
    public async Task OferecerAsync(IReadOnlyList<PacoteDeEntrega> pacotes, bool aposBeta)
    {
        foreach (var pacote in pacotes)
        {
            await _umaPerguntaPorVez.WaitAsync();
            try
            {
                _oferecidos.Add(pacote.Nome);
                var config = Configuracao.Entregas;
                var subtitulo = aposBeta
                    ? $"{(pacote.Gatilho.Length > 0 ? pacote.Gatilho + " finalizado" : "Beta do " + pacote.Nome + " finalizada")}: {string.Join(", ", pacote.Obrigatorios)} ok."
                    : $"Preparar a entrega do pacote {pacote.Nome} agora.";
                var pedido = new PedidoDeEntrega(pacote.Nome, subtitulo, config.Pasta, config.Zip, config.CopiarZip, PreparadorDeEntrega.Previa(pacote, PastaDoProjeto));
                if (await _janelas.AbrirDialogoAsync(JanelaDeEntrega.Id, pedido) is not true) continue;
                Mensagem = $"Preparando a entrega do {pacote.Nome}: copiando e compactando os arquivos...";
                var resultado = await Task.Run(() => _entrega.Entregar(config, pacote, PastaDoProjeto));
                EntregaPronta = new EntregaPronta(pacote.Nome, resultado);
                Mensagem = resultado.Problemas.Count == 0 ? $"Entrega do {pacote.Nome} pronta." : $"Entrega do {pacote.Nome} com problemas (ver o painel).";
            }
            finally { _umaPerguntaPorVez.Release(); }
        }
    }

    public void CopiarEntregaDeNovo()
    {
        if (EntregaPronta?.Resultado.Zip is { } zip)
            Mensagem = Windows.CopiarParaAreaDeTransferencia(zip) ? "O .zip foi copiado de novo (Ctrl+V no Slack)." : "Não foi possível copiar o .zip.";
    }

    public void AbrirEntrega()
    {
        if (EntregaPronta is { } e) Windows.MostrarNoExplorer(e.Resultado.Principal);
    }

    public async Task AbrirEntregasAsync()
    {
        if (Executando) return;
        if (await _janelas.AbrirDialogoAsync(JanelaDeEntregas.Id, new PedidoDeEntregas(Configuracao.Entregas)) is not RespostaDeEntregas resposta) return;
        var r = _repositorio.Salvar(Configuracao with { Entregas = resposta.Config });
        if (!r.EhOk) { await _dialogos.MostrarErroAsync(r.Erro); return; }
        if (resposta.PrepararAgora is { } nome && Configuracao.Entregas.Pacotes.FirstOrDefault(p => p.Nome == nome) is { } pacote)
            await OferecerAsync([pacote], aposBeta: false);
    }

    // ---- log

    public void AbrirLog()
    {
        if (Log.Caminho is { } caminho && File.Exists(caminho)) AcoesDoWindows.AbrirNoEditor(caminho);
    }

    public void AbrirPastaDeLogs()
    {
        Directory.CreateDirectory(PastaDosLogs);
        Windows.MostrarNoExplorer(PastaDosLogs);
    }

    public async Task AbrirCmdAsync(LinhaDeBeta? linha)
    {
        if (linha?.Pasta is null) return;
        var pedido = await _preparador.MontarAsync([linha.Nome], deploy: false, BaseSelecionada.Caminho);
        if (pedido.EhOk) AcoesDoWindows.AbrirCmd(pedido.Valor.Planos[0], pedido.Valor.Java8Home, pedido.Valor.MavenBin);
        else await _dialogos.MostrarErroAsync(pedido.Erro);
    }
}
