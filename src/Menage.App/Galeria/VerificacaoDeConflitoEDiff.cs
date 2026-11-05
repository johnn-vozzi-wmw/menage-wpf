using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Conflito;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.Core;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.ComDevelop;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Galeria;

/// <summary>
/// Verificação de ponta a ponta, em segundo plano: um merge real com conflito (Com develop) decidido pelo diálogo de verdade
/// (botões acionados por automação, sem mouse nem teclado, janelas fora da tela), mais o editor de conflito e a janela de diff
/// abrindo sobre o repositório. Tudo em repositórios descartáveis em %TEMP%.
/// </summary>
public sealed class VerificacaoDeConflitoEDiff(IServiceProvider servicos)
{
    public async Task<IReadOnlyList<string>> ExecutarAsync(Window dono)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "mgv" + Guid.NewGuid().ToString("N")[..5]);
        var resultado = new List<string>();
        void Conferir(string nome, bool ok) => resultado.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");
        try
        {
            var repo = Preparar(raiz);
            var projeto = new Projeto("exemplo", repo, []);

            // 1) conflito, o usuário escolhe "Pular": o merge é desfeito e a pasta fica limpa
            var pular = await RodarComDevelopAsync(projeto, dono, "BotaoPular", resolver: false);
            Conferir($"merge em conflito + botão Pular: erro no projeto e merge abortado ({pular.Situacao}: {pular.Mensagem})",
                pular.Situacao == Situacao.Erro && Git(repo, "status", "--porcelain").Length == 0);

            // 2) conflito, o editor de conflito abre sobre o repositório, o arquivo é resolvido e o usuário escolhe "Já resolvi"
            var seguir = await RodarComDevelopAsync(projeto, dono, "BotaoContinuar", resolver: true);
            Conferir($"merge em conflito + resolver + Já resolvi: merge concluído ({seguir.Situacao}: {seguir.Mensagem})",
                seguir.Situacao == Situacao.Ok && File.ReadAllText(Path.Combine(repo, "c.txt")).Contains("resolvido", StringComparison.Ordinal));
            Conferir("depois do merge não sobra operação em andamento", await servicos.GetRequiredService<ConsultasGit>().OperacaoEmAndamentoAsync(repo) == "");

            // 3) janela de diff sobre alterações reais
            File.WriteAllText(Path.Combine(repo, "c.txt"), "alterado de novo\n");
            File.WriteAllText(Path.Combine(repo, "novo.txt"), "arquivo novo\n");
            var diff = servicos.GetRequiredService<JanelaDiff.Fabrica>().Criar(repo);
            Esconder(diff, dono);
            diff.Show();
            await Esperar(1500);
            Conferir($"janela de diff abre e lista as alterações ({diff.ListaDeArquivos.Total} arquivo(s))", diff.ListaDeArquivos.Total >= 2);
            diff.Close();
        }
        catch (Exception ex)
        {
            Conferir($"conflito/diff de ponta a ponta: exceção {ex}", false);
        }
        finally
        {
            Apagar(raiz);
        }

        return resultado;
    }

    async Task<ResultadoDoProjeto<ResultadoDeComDevelop>> RodarComDevelopAsync(Projeto projeto, Window dono, string botao, bool resolver)
    {
        // novo conflito: develop e feature mexem na mesma linha
        var repo = projeto.Caminho;
        var sufixo = Guid.NewGuid().ToString("N")[..4];
        Git(repo, "checkout", "-q", "develop");
        Commitar(repo, "c.txt", "da develop " + sufixo + "\n", "develop " + sufixo);
        Git(repo, "push", "-q", "origin", "develop");
        Git(repo, "checkout", "-q", "feature");
        Commitar(repo, "c.txt", "da feature " + sufixo + "\n", "feature " + sufixo);

        var projetos = new ProjetosEmMemoria();
        projetos.Adicionar(projeto);
        projetos.Marcar(projeto.Caminho, true);
        var operacao = new ComDevelopEmLote(projetos, new DialogosQueConfirmam(), new ProgressoNulo(), servicos.GetRequiredService<IGit>(),
            servicos.GetRequiredService<ConsultasGit>(), servicos.GetRequiredService<GuardaDePendencias>(), new ResolvedorDeTeste(servicos, dono, botao, repo, resolver));
        var resumo = await operacao.ExecutarAsync(new OpcoesDeComDevelop());
        return resumo.Valor.Itens.Single().Resultado;
    }

    sealed class ResolvedorDeTeste(IServiceProvider servicos, Window dono, string botao, string repo, bool resolver) : IResolvedorDeConflitos
    {
        public async Task<DecisaoDeConflito> DecidirAsync(ConflitoDoGit conflito, CancellationToken cancelar = default)
        {
            var janela = servicos.GetRequiredService<JanelaDeDecisaoDeConflito>();
            janela.Receber(conflito);
            Esconder(janela, dono);
            janela.Show();
            await Esperar(500);
            if (resolver)
            {
                var editor = servicos.GetRequiredService<JanelaConflito.Fabrica>().Criar(repo);
                Esconder(editor, dono);
                editor.Show();
                await Esperar(1500);
                editor.Close();
                File.WriteAllText(Path.Combine(repo, "c.txt"), "resolvido\n");
                Git(repo, "add", "c.txt");
            }

            var fechada = new TaskCompletionSource();
            janela.Closed += (_, _) => fechada.TrySetResult();
            var alvo = (Button)janela.FindName(botao);
            ((IInvokeProvider)new ButtonAutomationPeer(alvo).GetPattern(PatternInterface.Invoke)!).Invoke();
            await fechada.Task;
            return (DecisaoDeConflito)janela.Resultado!;
        }
    }

    sealed class DialogosQueConfirmam : IServicoDeDialogos
    {
        public Task<bool> ConfirmarAsync(Previa previa) => Task.FromResult(true);

        public Task InformarAsync(string titulo, string mensagem) => Task.CompletedTask;

        public Task MostrarErroAsync(Erro erro) => Task.CompletedTask;

        public Task MostrarResumoAsync(IResumoDoLote resumo) => Task.CompletedTask;
    }

    static void Esconder(Window janela, Window dono)
    {
        janela.Owner = dono;
        (janela.ShowActivated, janela.ShowInTaskbar, janela.WindowStartupLocation, janela.Left, janela.Top) = (false, false, WindowStartupLocation.Manual, -12000, 0);
    }

    static string Preparar(string raiz)
    {
        Directory.CreateDirectory(raiz);
        var origin = Path.Combine(raiz, "o");
        var repo = Path.Combine(raiz, "r");
        Git(raiz, "init", "-q", "--bare", "-b", "main", origin);
        Git(raiz, "clone", "-q", origin, repo);
        Git(repo, "config", "user.email", "t@t");
        Git(repo, "config", "user.name", "t");
        Git(repo, "config", "commit.gpgsign", "false");
        Commitar(repo, "c.txt", "base\n", "base");
        Git(repo, "branch", "-M", "main");
        Git(repo, "push", "-q", "-u", "origin", "main");
        foreach (var b in new[] { "develop", "feature" })
        {
            Git(repo, "checkout", "-q", "-b", b);
            Git(repo, "push", "-q", "-u", "origin", b);
        }

        return repo;
    }

    static void Commitar(string repo, string arquivo, string conteudo, string mensagem)
    {
        File.WriteAllText(Path.Combine(repo, arquivo), conteudo);
        Git(repo, "add", arquivo);
        Git(repo, "commit", "-q", "-m", mensagem);
    }

    static string Git(string pasta, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = pasta, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_EDITOR"] = "true";
        using var p = Process.Start(info)!;
        var saida = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return saida.Trim();
    }

    static void Apagar(string raiz)
    {
        try
        {
            foreach (var f in new DirectoryInfo(raiz).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) f.Attributes = FileAttributes.Normal;
            Directory.Delete(raiz, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // pasta temporária: sobra de lixo em %TEMP% não afeta nada
        }
    }

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
