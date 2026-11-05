using System.IO.Compression;
using Menage.Core.Beta;
using Menage.Core.Entregas;
using Menage.TestKit;

namespace Menage.Tests.Beta;

/// <summary>Cenário em %TEMP% com caminho curto: projetos com pom.xml, JDK falso e Maven falso (nunca o Maven de verdade).</summary>
public sealed class CenarioDoGerador : IDisposable
{
    public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "gb" + Guid.NewGuid().ToString("N")[..6]);

    public MavenFalso Maven { get; }

    public CenarioDoGerador(int codigo = 0, string saida = "BUILD SUCCESS", string? erro = null, int atraso = 0)
    {
        Directory.CreateDirectory(Raiz);
        Maven = MavenFalso.Criar(codigo, saida, erro, atraso);
        File.WriteAllText(Path.Combine(Jdk, "bin", "java.exe"), "");
    }

    public string Jdk => Directory.CreateDirectory(Path.Combine(Raiz, "jdk", "bin")).Parent!.FullName;

    public string PastaDe(string projeto)
    {
        var pasta = Path.Combine(Raiz, "base", projeto);
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, "pom.xml"), "<project/>");
        return pasta;
    }

    public PlanoDeProjeto Plano(string nome, bool limpar = false, bool personalizado = false)
    {
        var projeto = new ProjetoDoGerador { Nome = nome, Grupo = "Web", Jdk = "17" };
        return new PlanoDeProjeto(projeto, PastaDe(nome), "main", Jdk, $"mvn -B clean install {nome}", limpar, personalizado);
    }

    public PedidoDeGeracao Pedido(params PlanoDeProjeto[] planos) =>
        new(planos, false, Path.Combine(Raiz, "logs"), Maven.Pasta, Jdk);

    public void Dispose()
    {
        Maven.Dispose();
        if (Directory.Exists(Raiz))
        {
            foreach (var f in Directory.EnumerateFiles(Raiz, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Raiz, recursive: true);
        }
    }
}

public sealed class MontadorDeComandoTests
{
    [Fact]
    public void Comando_usa_o_modo_e_os_parametros_comuns()
    {
        var config = new ConfiguracaoDoGerador { Parametros = "-Dx=1" };
        var web = config.Projetos.Single(p => p.Nome == "lavenderecore");

        MontadorDeComando.Completo(web, config, deploy: false).Should().Be("mvn -B clean antrun:run install -Dx=1");
        MontadorDeComando.Completo(web, config, deploy: true).Should().Be("mvn -B clean antrun:run deploy -Dx=1");
    }

    [Fact]
    public void Comando_personalizado_vence_o_padrao()
    {
        var config = new ConfiguracaoDoGerador { ComandosPersonalizados = new() { ["lavendereweb"] = "mvn -o install" } };
        var projeto = config.Projetos.Single(p => p.Nome == "lavendereweb");

        MontadorDeComando.Completo(projeto, config, deploy: true).Should().Be("mvn -o install");
        MontadorDeComando.Personalizado(projeto, config).Should().BeTrue();
    }

    [Fact]
    public void Jdk_inexistente_impede_a_geracao()
    {
        var config = new ConfiguracaoDoGerador { Jdks = new() { ["17"] = @"C:\nao\existe" } };

        MontadorDeComando.Validar(config.Projetos.Single(p => p.Nome == "lavendereweb"), config)!.Causa.Should().Contain("JDK 17");
    }
}

public sealed class LimpadorDeTargetTests : IDisposable
{
    readonly CenarioDoGerador _cenario = new();

    public void Dispose() => _cenario.Dispose();

    [Fact]
    public void Apaga_target_do_projeto_e_dos_modulos_e_so_eles()
    {
        var raiz = _cenario.PastaDe("proj");
        var modulo = Directory.CreateDirectory(Path.Combine(raiz, "modulo")).FullName;
        File.WriteAllText(Path.Combine(modulo, "pom.xml"), "<project/>");
        var alvos = new[] { Path.Combine(raiz, "target"), Path.Combine(modulo, "target") };
        var intocaveis = new[]
        {
            Path.Combine(raiz, "semPom", "target"),                 // target sem pom.xml ao lado
            Path.Combine(raiz, ".git", "target"),                   // .git
            Path.Combine(raiz, "node_modules", "x", "target"),      // node_modules
            Path.Combine(raiz, ".oculta", "target"),                // pasta oculta
        };
        foreach (var d in alvos.Concat(intocaveis)) Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(raiz, ".git", "pom.xml"), "<project/>");
        File.WriteAllText(Path.Combine(raiz, "node_modules", "x", "pom.xml"), "<project/>");
        File.WriteAllText(Path.Combine(raiz, ".oculta", "pom.xml"), "<project/>");
        var somenteLeitura = Path.Combine(alvos[0], "arquivo.class");
        File.WriteAllText(somenteLeitura, "x");
        File.SetAttributes(somenteLeitura, FileAttributes.ReadOnly);

        var r = LimpadorDeTarget.Limpar(raiz);

        r.Removidas.Should().Be(2);
        r.Avisos.Should().BeEmpty();
        alvos.Should().OnlyContain(d => !Directory.Exists(d));
        intocaveis.Should().OnlyContain(d => Directory.Exists(d));
    }

    [Fact]
    public void Arquivo_em_uso_vira_aviso_e_nao_levanta_excecao()
    {
        var raiz = _cenario.PastaDe("emuso");
        var target = Directory.CreateDirectory(Path.Combine(raiz, "target")).FullName;
        using var aberto = new FileStream(Path.Combine(target, "preso.jar"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var r = LimpadorDeTarget.Limpar(raiz);

        r.Removidas.Should().Be(0);
        r.Avisos.Should().ContainSingle().Which.Should().Contain("target");
        Directory.Exists(target).Should().BeTrue();
    }
}

public sealed class ExecutorDoGeradorTests : IDisposable
{
    readonly CenarioDoGerador _cenario = new();
    readonly List<StatusDoProjeto> _eventos = [];

    public void Dispose() => _cenario.Dispose();

    ExecutorDoGerador Executor()
    {
        var executor = new ExecutorDoGerador(new Menage.Core.Processos.ProcessoRunner());
        executor.StatusMudou += s => { lock (_eventos) _eventos.Add(s); };
        return executor;
    }

    [Fact]
    public async Task Roda_a_sequencia_na_ordem_com_log_por_projeto()
    {
        var pedido = _cenario.Pedido(_cenario.Plano("lavenderecore"), _cenario.Plano("lavenderedados"));

        var resumo = await Executor().ExecutarAsync(pedido);

        resumo.Oks.Should().Be(2);
        _cenario.Maven.Chamadas.Should().HaveCount(2);
        _cenario.Maven.Chamadas[0].Should().Contain("lavenderecore");
        _cenario.Maven.Chamadas[1].Should().Contain("lavenderedados");
        var log = File.ReadAllText(resumo.Projetos[0].Log!);
        log.Should().Contain("Projeto  : lavenderecore").And.Contain("BUILD SUCCESS").And.Contain("RESULTADO: SUCESSO");
        Path.GetFileName(resumo.Projetos[1].Log!).Should().Be("02-lavenderedados.log");
        _eventos.Where(e => e.Projeto == "lavenderecore").Select(e => e.Estado)
            .Should().ContainInOrder(FaseDaGeracao.NaFila, FaseDaGeracao.Executando, FaseDaGeracao.Ok);
    }

    [Fact]
    public async Task Falha_de_um_projeto_nao_para_os_seguintes()
    {
        using var falha = new CenarioDoGerador(codigo: 1, saida: "BUILD FAILURE");
        var pedido = falha.Pedido(falha.Plano("lavenderecore"), falha.Plano("lavenderedados"));

        var resumo = await new ExecutorDoGerador(new Menage.Core.Processos.ProcessoRunner()).ExecutarAsync(pedido);

        resumo.Falhas.Should().Be(2);
        resumo.Projetos.Should().OnlyContain(p => p.Estado == FaseDaGeracao.Erro);
        falha.Maven.Chamadas.Should().HaveCount(2);
        resumo.Projetos[0].Resumo.Should().Contain("sem testes");
    }

    [Fact]
    public async Task Parar_no_primeiro_erro_deixa_o_resto_sem_executar()
    {
        using var falha = new CenarioDoGerador(codigo: 1);
        var pedido = falha.Pedido(falha.Plano("lavenderecore"), falha.Plano("lavenderedados")) with { PararNoPrimeiroErro = true };

        var resumo = await new ExecutorDoGerador(new Menage.Core.Processos.ProcessoRunner()).ExecutarAsync(pedido);

        resumo.Projetos.Select(p => p.Estado).Should().Equal(FaseDaGeracao.Erro, FaseDaGeracao.NaoExecutado);
        falha.Maven.Chamadas.Should().HaveCount(1);
        resumo.Interrompida.Should().BeTrue();
    }

    [Fact]
    public async Task Parar_apos_o_atual_deixa_o_atual_terminar_e_nao_comeca_o_proximo()
    {
        using var lento = new CenarioDoGerador(atraso: 2);
        var parar = new CancellationTokenSource();
        var executor = new ExecutorDoGerador(new Menage.Core.Processos.ProcessoRunner());
        executor.StatusMudou += s => { if (s.Estado == FaseDaGeracao.Executando) parar.Cancel(); };

        var resumo = await executor.ExecutarAsync(lento.Pedido(lento.Plano("lavenderecore"), lento.Plano("lavenderedados")),
            new Menage.Core.Operacoes.SinaisDoLote(CancellationToken.None, parar.Token));

        resumo.Projetos.Select(p => p.Estado).Should().Equal(FaseDaGeracao.Ok, FaseDaGeracao.NaoExecutado);
    }

    [Fact]
    public async Task Abortar_mata_o_maven_em_curso_e_marca_interrompido()
    {
        using var lento = new CenarioDoGerador(atraso: 20);
        var abortar = new CancellationTokenSource();
        var executor = new ExecutorDoGerador(new Menage.Core.Processos.ProcessoRunner());
        executor.StatusMudou += s => { if (s.Estado == FaseDaGeracao.Executando) abortar.CancelAfter(600); };

        var resumo = await executor.ExecutarAsync(lento.Pedido(lento.Plano("lavenderecore")),
            new Menage.Core.Operacoes.SinaisDoLote(abortar.Token, CancellationToken.None));

        resumo.Projetos.Single().Estado.Should().Be(FaseDaGeracao.Interrompido);
        File.ReadAllText(resumo.Projetos.Single().Log!).Should().Contain("RESULTADO: INTERROMPIDO");
    }

    [Fact]
    public async Task Limpar_target_acontece_antes_do_maven_e_aparece_no_log()
    {
        var plano = _cenario.Plano("lavenderecore", limpar: true);
        var target = Directory.CreateDirectory(Path.Combine(plano.Pasta, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "velho.war"), "x");

        var resumo = await Executor().ExecutarAsync(_cenario.Pedido(plano));

        Directory.Exists(target).Should().BeFalse();
        File.ReadAllText(resumo.Projetos.Single().Log!).Should().Contain("removida: target").And.Contain("1 de 1 pasta(s) target removida(s)");
    }

    [Fact]
    public async Task Falha_do_surefire_no_lavenderepda_repete_uma_vez_com_a_opcao_da_jvm()
    {
        using var surefire = new CenarioDoGerador(codigo: 1, saida: "surefire The forked VM terminated");
        var pedido = surefire.Pedido(surefire.Plano("lavenderepda"));

        var resumo = await new ExecutorDoGerador(new Menage.Core.Processos.ProcessoRunner()).ExecutarAsync(pedido);

        surefire.Maven.Chamadas.Should().HaveCount(2);
        Path.GetFileName(resumo.Projetos.Single().Log!).Should().Be("01-lavenderepda-surefire.log");
        File.Exists(Path.Combine(pedido.PastaDeLogs, "01-lavenderepda.log")).Should().BeTrue();
        resumo.Projetos.Single().Estado.Should().Be(FaseDaGeracao.Erro);
    }

    [Fact]
    public void Script_define_o_ambiente_e_escapa_o_percentual()
    {
        var plano = _cenario.Plano("lavenderecore") with { Comando = "mvn -Dv=%X%" };
        var texto = ScriptDoProjeto.Montar(plano, _cenario.Pedido(plano), @"C:\logs\a.log", null);

        texto.Should().Contain("set \"JAVA_HOME=").And.Contain("call mvn -Dv=%%X%% >> \"C:\\logs\\a.log\" 2>&1").And.Contain("exit /b %errorlevel%");
    }
}

public sealed class EntregasTests : IDisposable
{
    readonly CenarioDoGerador _cenario = new();
    readonly ConfiguracaoDeEntregas _config;

    public EntregasTests() =>
        _config = ConfiguracaoDeEntregas.Padrao() with { Pasta = Path.Combine(_cenario.Raiz, "entregas") };

    public void Dispose() => _cenario.Dispose();

    static Dictionary<string, bool> Ok(params string[] nomes) => nomes.ToDictionary(n => n, _ => true);

    [Fact]
    public void Web_so_pergunta_quando_o_gatilho_termina_ok_com_core_e_dados()
    {
        PreparadorDeEntrega.PorGatilho(_config, "lavendereweb", Ok("lavenderecore", "lavenderedados", "lavendereweb")).Select(p => p.Nome)
            .Should().Equal("Web");
        PreparadorDeEntrega.PorGatilho(_config, "lavendereweb", Ok("lavenderedados", "lavendereweb")).Should().BeEmpty();
        PreparadorDeEntrega.PorGatilho(_config, "lavenderedados", Ok("lavenderecore", "lavenderedados", "lavendereweb")).Should().BeEmpty();
        var falhou = Ok("lavenderecore", "lavenderedados"); falhou["lavendereweb"] = false;
        PreparadorDeEntrega.PorGatilho(_config, "lavendereweb", falhou).Should().BeEmpty();
    }

    [Fact]
    public void Pda_pergunta_no_fim_e_exige_framework_tc_ok_se_participou()
    {
        PreparadorDeEntrega.NoFim(_config, Ok("lavenderepda")).Select(p => p.Nome).Should().Equal("PDA");
        var tcFalhou = Ok("lavenderepda"); tcFalhou["framework-tc"] = false;
        PreparadorDeEntrega.NoFim(_config, tcFalhou).Should().BeEmpty();
        PreparadorDeEntrega.NoFim(_config, Ok("lavenderecore", "lavenderedados", "lavendereweb")).Should().BeEmpty();
    }

    [Fact]
    public void Pda_gera_pasta_jar_simulador_e_zip_com_a_pasta_na_raiz()
    {
        var pda = _cenario.PastaDe("lavenderepda");
        Directory.CreateDirectory(Path.Combine(pda, "target"));
        File.WriteAllText(Path.Combine(pda, "target", "wmwvendasapp-1.2.jar"), "jar");
        File.WriteAllText(Path.Combine(pda, "target", "wmwvendasapp-1.2-sources.jar"), "nao");
        var simulador = Directory.CreateDirectory(Path.Combine(pda, "release", "resources", "app", "simulador")).FullName;
        File.WriteAllText(Path.Combine(simulador, "index.html"), "<html/>");

        var acoes = new AcoesFalsas();
        var r = new ServicoDeEntrega(acoes).Entregar(_config, _config.Pacotes.Single(p => p.Nome == "PDA"), n => n == "lavenderepda" ? pda : null);

        r.Problemas.Should().BeEmpty();
        Directory.GetFiles(r.Pasta).Select(Path.GetFileName).Should().BeEquivalentTo("wmwvendasapp-1.2.jar", "simulador.zip");
        r.Zip.Should().EndWith(".zip");
        using (var zip = ZipFile.OpenRead(r.Zip!))
            zip.Entries.Select(e => e.FullName).Should().Contain(e => e.EndsWith("/wmwvendasapp-1.2.jar", StringComparison.Ordinal))
                .And.OnlyContain(e => e.StartsWith(Path.GetFileName(r.Pasta) + "/", StringComparison.Ordinal));
        using (var interno = ZipFile.OpenRead(Path.Combine(r.Pasta, "simulador.zip")))
            interno.Entries.Single().FullName.Should().Be("simulador/index.html");
        acoes.Copiados.Should().Equal(r.Zip);
        acoes.Mostrados.Should().Equal(r.Zip);
    }

    [Fact]
    public void Item_ausente_vira_problema_e_sem_copia_nao_abre_o_explorer()
    {
        var acoes = new AcoesFalsas();
        var r = new ServicoDeEntrega(acoes).Entregar(_config, _config.Pacotes.Single(p => p.Nome == "Web"), _ => null);

        r.Problemas.Should().HaveCount(2);
        r.Zip.Should().BeNull();
        acoes.Copiados.Should().BeEmpty();
        acoes.Mostrados.Should().BeEmpty();
    }

    sealed class AcoesFalsas : IAcoesDeEntrega
    {
        public List<string> Copiados { get; } = [];

        public List<string> Mostrados { get; } = [];

        public bool CopiarParaAreaDeTransferencia(string arquivo) { Copiados.Add(arquivo); return true; }

        public void MostrarNoExplorer(string caminho) => Mostrados.Add(caminho);
    }
}

public sealed class RepositorioDoGeradorTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "gr" + Guid.NewGuid().ToString("N")[..6]);

    RepositorioDoGerador Novo() =>
        new(new Armazenamento<ConfiguracaoDoGerador>(new PastaDeDados(_pasta), "gerador-beta", padrao: ConfiguracaoDoGerador.Padrao));

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Favoritos_salvam_renomeiam_atualizam_e_excluem_e_sobrevivem_ao_reabrir()
    {
        var repo = Novo();
        repo.SalvarFavorito("Web", deploy: false, ["lavenderecore", "lavendereweb"]).EhOk.Should().BeTrue();
        repo.SalvarFavorito("web", deploy: false, ["x"]).EhOk.Should().BeFalse("o nome repetido é recusado");
        repo.RenomearFavorito("Web", "Web completa").EhOk.Should().BeTrue();
        repo.AtualizarFavorito("Web completa", deploy: true, ["lavenderecore"]).EhOk.Should().BeTrue();

        var reaberto = Novo().Achar("Web completa");
        reaberto!.Deploy.Should().BeTrue();
        reaberto.Projetos.Should().Equal("lavenderecore");

        repo.ExcluirFavorito("Web completa").EhOk.Should().BeTrue();
        Novo().Configuracao.Favoritos.Should().BeEmpty();
    }

    [Fact]
    public void Limpar_target_habilitado_sem_projeto_nao_salva_e_desabilitado_guarda_a_marcacao()
    {
        var repo = Novo();
        repo.DefinirLimparTarget(true, []).EhOk.Should().BeFalse();
        repo.Configuracao.LimparTarget.Ativo.Should().BeFalse("padrão desligado");

        repo.DefinirLimparTarget(true, ["lavendereweb"]).EhOk.Should().BeTrue();
        repo.Configuracao.LimparTarget.Vale("lavendereweb").Should().BeTrue();

        repo.DefinirLimparTarget(false, ["lavendereweb"]).EhOk.Should().BeTrue();
        var guardado = Novo().Configuracao.LimparTarget;
        guardado.Vale("lavendereweb").Should().BeFalse();
        guardado.Projetos.Should().Equal("lavendereweb");
    }
}
