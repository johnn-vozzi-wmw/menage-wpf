using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menage.App.Controles;
using Menage.App.Modulos.BetaAutomatica;
using Menage.App.Modulos.Melhorias;
using Menage.App.Tema;
using Menage.Core;
using Menage.Core.BetaAutomatica;
using Menage.Core.Melhorias;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Galeria;

/// <summary>Contexto de banco de mentira da galeria: três perfis fixos, nada é aplicado em lugar nenhum.</summary>
public sealed class BancoDeExemplo : IContextoDeBanco
{
    readonly List<PerfilDeBancoRef> _perfis = [new("p1", "Produção Lebon", "oracle"), new("p2", "Homologação", "postgres"), new("p3", "Local dev", "sqlserver")];

    public IReadOnlyList<PerfilDeBancoRef> Perfis => _perfis;

    public PerfilDeBancoRef? Achar(string id) => _perfis.FirstOrDefault(p => p.Id == id);

    public PerfilDeBancoRef? AtivoNoContexto() => _perfis[0];

    public Task<Resultado<Nada>> AplicarAsync(string perfilId, CancellationToken cancelar = default) => Task.FromResult(Resultado.Ok());
}

/// <summary>
/// Modo <c>--galeria-beta-melhorias &lt;pasta&gt;</c>: abre as telas da Beta automática e das Melhorias com dados de exemplo
/// (nenhum Jira, nenhum git e nenhum Maven são tocados) e captura nos temas escuro e claro.
/// </summary>
public sealed class RoteiroDaBetaEMelhorias(IServiceProvider servicos, IServicoDeTema tema)
{
    readonly List<string> _verificacoes = [];
    string _pasta = "";

    public async Task<int> ExecutarAsync(JanelaBetaAutomatica dono, string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(pasta);
        try
        {
            Preparar();
            foreach (var id in new[] { CatalogoDeTemas.IdEscuro, CatalogoDeTemas.IdClaro }) await CapturarTemaAsync(dono, id);
        }
        catch (Exception ex)
        {
            _verificacoes.Add($"FALHOU: exceção {ex}");
        }

        await File.WriteAllLinesAsync(Path.Combine(pasta, "verificacoes.txt"), _verificacoes);
        return _verificacoes.Exists(v => v.StartsWith("FALHOU", StringComparison.Ordinal)) ? 1 : 0;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    static ItemBeta Item(string chave, string titulo, EstadoDoItem estado, string branch, string motivo, params ProjetoDoItem[] projetos) => new()
    {
        Chave = chave, Titulo = titulo, Estado = estado, Branch = branch, Motivo = motivo, DetectadaEm = DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        TransicaoEm = DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), Projetos = [.. projetos],
    };

    static ItemBeta Com(ItemBeta item, Action<ItemBeta> ajustar)
    {
        ajustar(item);
        return item;
    }

    static ProjetoDoItem P(string nome, SituacaoDaBranch situacao, string resultado = "", bool explicito = true, bool complemento = false, string tag = "", string detalhe = "") =>
        new() { Nome = nome, Situacao = situacao, Resultado = resultado, Explicito = explicito, Complemento = complemento, Tag = tag, Detalhe = detalhe };

    void Preparar()
    {
        var beta = servicos.GetRequiredService<ServicoDeBetaAutomatica>();
        beta.Alterar(c =>
        {
            c.Ativo = true;
            c.UltimaConsulta = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            c.UltimoErro = "";
            c.Itens =
            [
                Item("VENDAS-45120", "Corrige o desconto do pedido em lote", EstadoDoItem.Pronta, "VENDAS-45120", "comentário seu com PROJETOS ALTERADOS/BRANCH",
                    P("lavenderecore", SituacaoDaBranch.Local), P("lavendereweb", SituacaoDaBranch.Remoto),
                    P("lavenderedados", SituacaoDaBranch.Tag, explicito: false, complemento: true, tag: "9.060", detalhe: "tag 9.060 (versão do pom.xml de lavenderecore)")),
                Item("VENDAS-45101", "Nova coluna de margem na grade de produtos", EstadoDoItem.Gerando, "VENDAS-45101", "subtask Coding atribuída a você",
                    P("lavenderecore", SituacaoDaBranch.Local, ResultadoGerado.Ok), P("lavenderedados", SituacaoDaBranch.Local, ResultadoGerado.Gerando), P("lavendereweb", SituacaoDaBranch.Local)),
                Item("VENDAS-45088", "Ajuste no cálculo de ICMS ST", EstadoDoItem.Concluida, "VENDAS-45088", "você era o responsável quando saiu de In Development para Waiting for Code Review",
                    P("lavenderepda", SituacaoDaBranch.Local, ResultadoGerado.Ok), P("framework-tc", SituacaoDaBranch.Local, ResultadoGerado.Ok)),
                Item("VENDAS-45077", "Sincronização de clientes falha com acentos", EstadoDoItem.Falhou, "VENDAS-45077", "comentário seu com PROJETOS ALTERADOS/BRANCH",
                    P("lavenderecore", SituacaoDaBranch.Local, ResultadoGerado.Ok), P("lavendereweb", SituacaoDaBranch.Local, ResultadoGerado.Falhou)),
                Item("VENDAS-45060", "Relatório de comissões", EstadoDoItem.Problema, "VENDAS-45060", "comentário seu com PROJETOS ALTERADOS/BRANCH",
                    P("lavendereweb", SituacaoDaBranch.Ausente, detalhe: "a branch não existe local nem em origin")),
                Item("VENDAS-45150", "Tela de pesquisa de preços", EstadoDoItem.Aguardando, "", "subtask Coding atribuída a você"),
                Com(Item("VENDAS-44900", "Teste de carga do sync", EstadoDoItem.Ignorada, "VENDAS-44900", "comentário seu"), i => { i.EstadoAntes = EstadoDoItem.Pronta; i.IgnoradaEm = "2026-11-03 09:10"; }),
                Com(Item("VENDAS-44800", "Revisão do layout do pedido", EstadoDoItem.Finalizada, "VENDAS-44800", "comentário seu"), i =>
                {
                    i.EstadoAntes = EstadoDoItem.Concluida; i.FinalizadaEm = "2026-11-02 17:40"; i.FinalizadaMotivo = "automático: foi para \"Waiting for Product Increment\"";
                }),
            ];
            c.Itens[1].Erro = "";
            c.Itens[3].Erro = "Falhou em lavendereweb.";
            c.Itens[4].Erro = "A branch VENDAS-45060 não existe em nenhum dos projetos conferidos.";
        });

        var console = servicos.GetRequiredService<ConsoleDaBeta>();
        console.LimparTela();
        console.Registrar(NivelDoConsole.Info, "Monitor", "", "", "consulta (dev) concluída em 812 ms: 4 atividade(s) no Jira");
        console.Registrar(NivelDoConsole.Info, "Beta automática", "VENDAS-45101", "", "Lote montado: lavenderecore (branch VENDAS-45101), lavenderedados (branch VENDAS-45101), lavendereweb (branch VENDAS-45101)");
        console.Registrar(NivelDoConsole.Comando, "Beta automática", "VENDAS-45101", "lavenderecore", "git fetch origin --prune   (em C:\\wmw\\beta-auto\\web\\lavenderecore)");
        console.Registrar(NivelDoConsole.Comando, "Beta automática", "VENDAS-45101", "lavenderecore", "git checkout --force -B beta-auto origin/VENDAS-45101");
        console.Registrar(NivelDoConsole.Info, "Beta automática", "VENDAS-45101", "lavenderecore", "ok (151 s)");
        console.Registrar(NivelDoConsole.Aviso, "Beta automática", "VENDAS-45120", "lavenderedados", "fora do lote: não achei a versão no pom.xml");
        console.Registrar(NivelDoConsole.Erro, "Beta automática", "VENDAS-45077", "lavendereweb", "falhou: ERRO (exit 1) - veja o log");

        var banco = servicos.GetRequiredService<IContextoDeBanco>();
        var repo = servicos.GetRequiredService<RepositorioDeMelhorias>();
        repo.Config.Lista.Clear();
        repo.Config.Lista.AddRange(
        [
            Melhoria("VENDAS-24574", "Novo fluxo de devolução", Melhoria_.Andamento, "urgente, cliente-lebon", ["lavenderepda", "lavendereweb"], ["p1", "p2"]),
            Melhoria("VENDAS-25012", "Importação de tabela de preços", Melhoria_.Andamento, "financeiro", ["lavenderedados", "lavenderecore"], ["p3"]),
            Melhoria("VENDAS-24100", "Relatório de metas por vendedor", Melhoria_.Pausada, "relatorio", ["lavendereweb"], []),
            Melhoria("VENDAS-23010", "Ajuste de arredondamento", Melhoria_.Concluida, "", ["lavenderepda"], ["p2"]),
        ]);
        repo.Config.Ativa = "VENDAS-24574";
        repo.Config.Anterior = new EstadoAnterior { Melhoria = "VENDAS-24574", Branches = [new() { Projeto = "lavenderepda", Branch = "develop" }] };
        repo.Config.Lista[0].UltimoUso = DateTime.Now.AddHours(-3).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        repo.Config.Lista[0].PerfilAtivoId = "p1";
        repo.Config.Lista[0].Notas = "Cliente pediu a devolução parcial por item.\nValidar com o financeiro antes da beta.";
        repo.Config.Filtros = [new FiltroSalvo { Nome = "Urgentes do Lebon", Consulta = "tag:urgente tag:cliente-lebon" }];
        repo.Config.VersaoConf.Ativo = true;
        repo.Config.Consulta = "";
        repo.Salvar();
        Conferir("dados de exemplo: banco falso com 3 perfis", banco.Perfis.Count == 3);
    }

    static class Melhoria_
    {
        public const string Andamento = Core.Melhorias.Melhoria.Andamento, Pausada = Core.Melhorias.Melhoria.Pausada, Concluida = Core.Melhorias.Melhoria.Concluida;
    }

    static Melhoria Melhoria(string id, string titulo, string status, string tags, string[] projetos, string[] perfis) => new()
    {
        Id = id, Titulo = titulo, Status = status, Tags = Core.Melhorias.Melhoria.InterpretarTags(tags), Perfis = [.. perfis],
        Branches = [.. projetos.Select(p => new BranchDaMelhoria { Projeto = p, Branch = id, Base = "develop" })],
    };

    async Task CapturarTemaAsync(JanelaBetaAutomatica dono, string id)
    {
        tema.Trocar(id, animar: false);
        await Esperar(400);

        var painel = dono;
        painel.SecaoIgnoradas.IsExpanded = true;
        painel.SecaoFinalizadas.IsExpanded = true;

        var vm = servicos.GetRequiredService<BetaAutomaticaViewModel>();
        vm.Atualizar();
        await Esperar(500);
        Conferir($"{id}: painel da beta com 6 cartões, 1 ignorada e 1 finalizada", vm.Cartoes.Count == 6 && vm.Ignoradas.Count == 1 && vm.Finalizadas.Count == 1);
        Conferir($"{id}: cartão pronto oferece Gerar beta", vm.Cartoes.First(c => c.Chave == "VENDAS-45120").PodeGerar);
        Conferir($"{id}: cartão gerando oferece Cancelar e não Gerar", vm.Cartoes.First(c => c.Chave == "VENDAS-45101") is { PodeCancelar: true, PodeGerar: false });
        Salvar(painel, $"beta-painel-{id}");
        await Esperar(300);

        await Dialogo<JanelaDeConfigBeta>(dono, $"beta-configurar-{id}", d => d.Receber(servicos.GetRequiredService<ServicoDeBetaAutomatica>()));
        await Dialogo<JanelaDeJql>(dono, $"beta-jql-{id}", d => d.Receber(servicos.GetRequiredService<ServicoDeBetaAutomatica>()));
        await Dialogo<JanelaConsoleBeta>(dono, $"beta-console-{id}", null);
        await Dialogo<JanelaDeDiagnostico>(dono, $"beta-diagnostico-{id}", d => d.Texto.Text = DiagnosticoDeExemplo);

        var melhorias = servicos.GetRequiredService<JanelaMelhorias>();
        await Mostrar(melhorias, dono);
        var mvm = servicos.GetRequiredService<MelhoriasViewModel>();
        mvm.Recarregar();
        await Esperar(800);
        Conferir($"{id}: lista de melhorias com 4 itens, a ativa primeiro", mvm.Lista.Count == 4 && mvm.Lista[0].Ativa);
        Conferir($"{id}: visões prontas e a salva aparecem", mvm.Visoes.Any(v => v.Nome == "Em andamento") && mvm.Visoes.Any(v => v.Salva));
        Conferir($"{id}: tags com contagem", mvm.Tags.Count >= 4);
        Salvar(melhorias, $"melhorias-visao-geral-{id}");
        melhorias.Abas.SelectedIndex = 1;
        await Esperar(300);
        Salvar(melhorias, $"melhorias-projetos-{id}");
        melhorias.Abas.SelectedIndex = 3;
        await Esperar(300);
        Conferir($"{id}: aba Banco mostra os 2 perfis da melhoria", mvm.Perfis.Count == 2);
        Salvar(melhorias, $"melhorias-banco-{id}");
        melhorias.Abas.SelectedIndex = 0;
        mvm.AplicarVisao("tag:urgente");
        await Esperar(400);
        Conferir($"{id}: busca tag:urgente filtra para 1", mvm.Lista.Count == 1);
        Salvar(melhorias, $"melhorias-busca-{id}");
        mvm.LimparBusca();
        melhorias.Close();
        await Esperar(300);

        await Dialogo<JanelaEditorDeMelhoria>(dono, $"melhorias-editor-{id}", d => d.Receber(new PedidoDeEdicaoDeMelhoria(servicos.GetRequiredService<RepositorioDeMelhorias>().Achar("VENDAS-24574"))));
    }

    const string DiagnosticoDeExemplo =
        "=== DIAGNÓSTICO DA BUSCA NO JIRA (simulação: nada é alterado) ===\r\nJira: https://jira2.wmw.com.br   token (JIRA_TOKEN): definido\r\nUsuário do token: voce\r\n"
        + "Papel: Desenvolvedor\r\n\r\n--- VENDAS-45120  \"Corrige o desconto do pedido em lote\"\r\n    DECISÃO: NOVA - comentário seu com PROJETOS ALTERADOS/BRANCH\r\n"
        + "      [ok] lavenderecore (citado) - branch local\r\n      [tag] lavenderedados (complemento da beta web) - tag 9.060\r\n"
        + "    verificação (sem alterar nada): a branch existe em 2 projeto(s) -> ficaria \"Pronta para gerar\" e alertaria\r\n\r\n=== RESUMO: 1 atividade(s) devolvida(s) pelo Jira ===";

    static async Task Mostrar(Window janela, Window dono)
    {
        janela.Owner = dono;
        janela.ShowActivated = false;
        janela.ShowInTaskbar = false;
        janela.Show();
        await Esperar(900);
    }

    async Task Dialogo<T>(Window dono, string nome, Action<T>? preparar) where T : JanelaBase
    {
        var janela = servicos.GetRequiredService<T>();
        preparar?.Invoke(janela);
        await Mostrar(janela, dono);
        Salvar(janela, nome);
        janela.Close();
        await Esperar(300);
    }

    void Salvar(Window janela, string nome)
    {
        var elemento = (FrameworkElement)VisualTreeHelper.GetChild(janela, 0);
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var imagem = new RenderTargetBitmap(
            (int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        imagem.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(Path.Combine(_pasta, nome + ".png"));
        png.Save(arquivo);
    }

    static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
