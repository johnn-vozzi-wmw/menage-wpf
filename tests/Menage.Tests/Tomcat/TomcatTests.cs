using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Menage.Core.Http;
using Menage.Core.Nexus;
using Menage.Core.Tomcat;
using Menage.Tests.Nexus;

namespace Menage.Tests.Tomcat;

public sealed class PortasFalsas : IPortasDoSistema
{
    public HashSet<int> Ocupadas { get; } = [];

    public bool Escutando(int porta) => Ocupadas.Contains(porta);

    public bool Livre(int porta) => !Ocupadas.Contains(porta);

    public Task<string> OcupanteAsync(int porta, CancellationToken cancelar = default) => Task.FromResult(Ocupadas.Contains(porta) ? "java.exe (PID 1)" : "");
}

public sealed class LancadorFalso : ILancadorDeProcessos
{
    public sealed class Falso : IProcessoLongo
    {
        public int Id => 4242;

        public long InicioTicks => 1;

        public bool Vivo { get; set; } = true;

        public bool Encerrado { get; private set; }

        public Task EncerrarArvoreAsync()
        {
            Encerrado = true;
            Vivo = false;
            return Task.CompletedTask;
        }
    }

    public Falso Processo { get; } = new();

    public Action<string>? Linha { get; private set; }

    public Action<int>? Sair { get; private set; }

    public ComandoProcesso? Iniciado { get; private set; }

    public bool ReencontraPid { get; set; }

    public Resultado<IProcessoLongo> Iniciar(ComandoProcesso comando, Action<string> aoReceberLinha, Action<int> aoSair)
    {
        (Iniciado, Linha, Sair) = (comando, aoReceberLinha, aoSair);
        return Resultado.Ok<IProcessoLongo>(Processo);
    }

    public IProcessoLongo? Reencontrar(int pid, long inicioTicks) => ReencontraPid && pid > 0 ? Processo : null;
}

/// <summary>Tomcat de mentira em %TEMP%: só os arquivos que o motor confere. Nada de Tomcat nem Nexus reais.</summary>
public sealed class TomcatDeMentira : IDisposable
{
    public string Pasta { get; } = Path.Combine(Path.GetTempPath(), "tc" + Guid.NewGuid().ToString("N")[..6]);

    public TomcatDeMentira(int http = 8080, int parada = 8005, int ajp = 8009)
    {
        foreach (var d in new[] { "bin", "conf", "lib", "webapps" }) Directory.CreateDirectory(Path.Combine(Pasta, d));
        File.WriteAllText(Path.Combine(Pasta, "bin", "catalina.bat"), "@echo off");
        File.WriteAllText(Path.Combine(Pasta, "lib", "catalina.jar"), "x");
        File.WriteAllText(Path.Combine(Pasta, "conf", "server.xml"), $"""
            <Server port="{parada}" shutdown="SHUTDOWN">
              <!-- <Connector port="9999" protocol="HTTP/1.1"/> -->
              <Connector port="{http}" protocol="HTTP/1.1" connectionTimeout="20000"/>
              <Connector port="8443" protocol="HTTP/1.1" SSLEnabled="true"/>
              <Connector port="{ajp}" protocol="AJP/1.3"/>
            </Server>
            """);
        var jdk = Path.Combine(Pasta, "jdk", "bin");
        Directory.CreateDirectory(jdk);
        File.WriteAllText(Path.Combine(jdk, "java.exe"), "");
        File.WriteAllText(Path.Combine(Pasta, "bin", "setenv.bat"), $"set \"JAVA_HOME={Path.Combine(Pasta, "jdk")}\"\r\n");
    }

    public AmbienteTomcat Ambiente(int porta = 8082) => new() { Nome = "teste", Pasta = Pasta, Porta = porta };

    public string Webapps => Path.Combine(Pasta, "webapps");

    public static byte[] War(string conteudo, bool comWebXml = true)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
        {
            using var w = new StreamWriter(zip.CreateEntry(comWebXml ? "WEB-INF/web.xml" : "outro.txt").Open());
            w.Write(conteudo);
        }

        return memoria.ToArray();
    }

    public void Dispose()
    {
        if (!Directory.Exists(Pasta)) return;
        foreach (var f in Directory.EnumerateFiles(Pasta, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Pasta, true);
    }
}

public sealed class ServerXmlEPastaTests
{
    [Fact]
    public void Le_so_o_xml_ativo_e_ignora_ssl_e_comentarios()
    {
        using var t = new TomcatDeMentira();
        var p = ServerXml.Ler(File.ReadAllText(Path.Combine(t.Pasta, "conf", "server.xml")));
        (p.Http, p.Parada, p.Ajp, p.ConectoresHttpAtivos).Should().Be((8080, 8005, 8009, 1));
    }

    [Fact]
    public void Troca_so_os_numeros_e_preserva_o_resto()
    {
        using var t = new TomcatDeMentira();
        var xml = File.ReadAllText(Path.Combine(t.Pasta, "conf", "server.xml"));
        var novo = ServerXml.Aplicar(xml, ServerXml.Ler(xml), 8082, 8006, 8010);
        novo.Should().Contain("port=\"8082\"").And.Contain("<Server port=\"8006\"").And.Contain("port=\"8010\"").And.Contain("port=\"9999\"").And.Contain("connectionTimeout=\"20000\"");
    }

    [Fact]
    public void Valida_pasta_e_java()
    {
        using var t = new TomcatDeMentira();
        PastaDoTomcat.Validar(t.Pasta).Should().BeNull();
        PastaDoTomcat.Validar(Path.Combine(t.Pasta, "nao")).Should().NotBeNull();
        PastaDoTomcat.ValidarJava(t.Pasta).Should().BeNull();
        PastaDoTomcat.JavaHome(t.Pasta).Should().EndWith("jdk");
        File.Delete(Path.Combine(t.Pasta, "jdk", "bin", "java.exe"));
        PastaDoTomcat.ValidarJava(t.Pasta)!.Causa.Should().Contain("Java configurado não existe");
    }

    [Fact]
    public void Acha_linhas_invalidas_no_bat_do_banco()
    {
        using var t = new TomcatDeMentira();
        File.WriteAllText(Path.Combine(t.Pasta, "bin", "wmw_database_config.bat"), "Para oracle@echo off\r\nset \"db_host=x\"\r\nset \"db_user=\"a\"\r\n");
        var ruins = PastaDoTomcat.LinhasInvalidasDoBat(t.Pasta);
        ruins.Should().HaveCount(2);
        ruins[0].Should().StartWith("linha 1");
        ruins[1].Should().Contain("aspas");
    }
}

public sealed class InterpretadorDeLogTests
{
    [Fact]
    public void Traduz_porta_em_uso_e_ora()
    {
        var i = new InterpretadorDeLog();
        i.Linha("java.net.BindException: Address already in use: bind");
        i.ErroDetectado.Should().Contain("Porta em uso");
        i.Linha("ORA-01017: invalid username/password");
        i.ErroDetectado.Should().Contain("ORA-01017");
    }

    [Fact]
    public void Usa_a_causa_raiz_da_pilha()
    {
        var i = new InterpretadorDeLog();
        i.Linha("05-Nov-2026 10:00:00.000 SEVERE [main] algo falhou");
        i.Linha("Caused by: java.lang.IllegalArgumentException: The directory specified by base and internal path [C:/wmw/data] does not exist");
        i.Linha("05-Nov-2026 10:00:01.000 INFO [main] outro registro");
        i.ErroDetectado.Should().Contain(@"C:\wmw\data");
    }

    [Fact]
    public void App_que_falhou_nao_vira_implantada_e_servidor_sobe()
    {
        var i = new InterpretadorDeLog();
        i.Linha("Error deploying web application archive [C:\\t\\webapps\\wmwvendas.war]");
        i.Linha("Deployment of web application archive [C:\\t\\webapps\\wmwvendas.war] has finished in [10] ms");
        i.Apps["wmwvendas"].Should().Be("falhou");
        i.Linha("Server startup in [5000] milliseconds");
        i.ServidorSubiu.Should().BeTrue();
    }

    [Fact]
    public void Saida_antes_de_subir_explica()
    {
        var i = new InterpretadorDeLog();
        i.Saiu(255, parando: false);
        i.ErroDetectado.Should().Contain("255");
    }
}

public sealed class PreparadorETomcatTests : IDisposable
{
    const string Base = "http://nexus.teste/";
    readonly TomcatDeMentira _t = new();
    readonly PortasFalsas _portas = new();
    readonly byte[] _warVendas = TomcatDeMentira.War("vendas");
    readonly byte[] _warDados = TomcatDeMentira.War("dados");

    public void Dispose() => _t.Dispose();

    ClienteNexus Nexus(long? tamanhoVendas = null)
    {
        var http = new ClienteHttpFalso(url =>
        {
            var nome = url.Query.Contains("name=wmwvendas", StringComparison.Ordinal) ? "wmwvendas" : "wmwdados";
            var bytes = nome == "wmwvendas" ? _warVendas : _warDados;
            var tam = nome == "wmwvendas" ? tamanhoVendas ?? bytes.Length : bytes.Length;
            return new RespostaHttp(200, JsonNexus.Pagina(null, JsonNexus.Componente(nome, "9.060-1", Base, ($"{nome}-9.060-1.war", tam))));
        })
        {
            Arquivos = url => url.AbsolutePath.Contains("wmwvendas", StringComparison.Ordinal) ? _warVendas : _warDados,
        };
        return new ClienteNexus(http, new ConfiguracaoDoNexus { Url = Base, Usuario = "u", SenhaProtegida = "x" }, "s");
    }

    [Fact]
    public async Task Prepara_troca_os_war_guarda_backup_e_ajusta_portas()
    {
        File.WriteAllText(Path.Combine(_t.Webapps, "wmwvendas.war"), "antigo");
        Directory.CreateDirectory(Path.Combine(_t.Webapps, "wmwvendas"));
        var relator = new RelatorDePreparo();

        var r = await new PreparadorDoTomcat(_portas).PrepararAsync(_t.Ambiente(8082), Nexus(), "", new HashSet<int>(), SituacaoDoTomcat.Parado, relator);

        r.EhOk.Should().BeTrue(r.ToString());
        r.Valor.InstaladoVendas.Should().Be("9.060-1");
        File.ReadAllBytes(Path.Combine(_t.Webapps, "wmwvendas.war")).Should().Equal(_warVendas);
        File.ReadAllBytes(Path.Combine(_t.Webapps, "wmwdados.war")).Should().Equal(_warDados);
        Directory.Exists(Path.Combine(_t.Webapps, "wmwvendas")).Should().BeFalse();
        var backup = Directory.GetFiles(Path.Combine(_t.Pasta, "_express", "backup"), "wmwvendas.war", SearchOption.AllDirectories);
        File.ReadAllText(backup.Single()).Should().Be("antigo");
        ServerXml.Ler(File.ReadAllText(Path.Combine(_t.Pasta, "conf", "server.xml"))).Http.Should().Be(8082);
        File.Exists(Path.Combine(_t.Pasta, "conf", "server.xml.menage.bak")).Should().BeTrue();
        relator.Passos.Should().Contain(p => p.Contains("Pronto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_incompleto_nao_troca_nada()
    {
        File.WriteAllText(Path.Combine(_t.Webapps, "wmwvendas.war"), "antigo");
        var r = await new PreparadorDoTomcat(_portas).PrepararAsync(_t.Ambiente(), Nexus(tamanhoVendas: 999_999), "", new HashSet<int>(), SituacaoDoTomcat.Parado, new RelatorDePreparo());
        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Contain("incompleto");
        File.ReadAllText(Path.Combine(_t.Webapps, "wmwvendas.war")).Should().Be("antigo");
        Directory.Exists(Path.Combine(_t.Pasta, "_express", "baixando", "x")).Should().BeFalse();
    }

    [Fact]
    public async Task Erro_na_troca_devolve_tudo_ao_lugar()
    {
        File.WriteAllText(Path.Combine(_t.Webapps, "wmwvendas.war"), "vendas-antigo");
        var travado = Path.Combine(_t.Webapps, "wmwdados.war");
        File.WriteAllText(travado, "dados-antigo");
        using (new FileStream(travado, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = await new PreparadorDoTomcat(_portas).PrepararAsync(_t.Ambiente(), Nexus(), "", new HashSet<int>(), SituacaoDoTomcat.Parado, new RelatorDePreparo());
            r.EhOk.Should().BeFalse();
            r.Erro.Causa.Should().Contain("trocar os .war");
        }

        File.ReadAllText(Path.Combine(_t.Webapps, "wmwvendas.war")).Should().Be("vendas-antigo");
        File.ReadAllText(travado).Should().Be("dados-antigo");
    }

    [Fact]
    public async Task Recusa_ambiente_no_ar_e_porta_de_outro_ambiente()
    {
        var p = new PreparadorDoTomcat(_portas);
        var noAr = await p.PrepararAsync(_t.Ambiente(), Nexus(), "", new HashSet<int>(), SituacaoDoTomcat.NoAr, new RelatorDePreparo());
        noAr.Erro.Causa.Should().Contain("parado");
        var repetida = await p.PrepararAsync(_t.Ambiente(8082), Nexus(), "", new HashSet<int> { 8082 }, SituacaoDoTomcat.Parado, new RelatorDePreparo());
        repetida.Erro.Causa.Should().Contain("outro ambiente");
        var semNexus = await p.PrepararAsync(_t.Ambiente(), null, "", new HashSet<int>(), SituacaoDoTomcat.Parado, new RelatorDePreparo());
        semNexus.Erro.Causa.Should().Contain("Nexus");
    }

    [Fact]
    public void Mantem_a_porta_de_parada_que_nao_briga_e_troca_a_que_briga()
    {
        _portas.Ocupadas.Add(8005);
        var r = new PreparadorDoTomcat(_portas).SincronizarPortas(_t.Ambiente(8082), new HashSet<int>(), _ => { });
        r.EhOk.Should().BeTrue();
        var p = ServerXml.Ler(File.ReadAllText(Path.Combine(_t.Pasta, "conf", "server.xml")));
        (p.Http, p.Parada, p.Ajp).Should().Be((8082, 8006, 8009));
    }

    [Fact]
    public async Task Sobe_com_o_java_do_setenv_e_fica_no_ar_quando_as_apps_respondem()
    {
        File.WriteAllBytes(Path.Combine(_t.Webapps, "wmwvendas.war"), _warVendas);
        var lancador = new LancadorFalso();
        var http = new ClienteHttpFalso(_ => new RespostaHttp(200, ""));
        var exec = new ExecucaoDoTomcat(_t.Ambiente(8082), lancador, _portas, http);
        var mudou = new List<AmbienteTomcat>();
        exec.AmbienteMudou += mudou.Add;

        var r = await exec.SubirAsync(new HashSet<int>(), new PreparadorDoTomcat(_portas));

        r.EhOk.Should().BeTrue(r.ToString());
        lancador.Iniciado!.Ambiente!["JAVA_HOME"].Should().EndWith("jdk");
        lancador.Iniciado.Ambiente!["CATALINA_OPTS"].Should().BeNull();
        mudou.Single().Pid.Should().Be(4242);
        exec.Situacao().Should().Be(SituacaoDoTomcat.Subindo);
        lancador.Linha!("Server startup in [900] milliseconds");
        await exec.VerificarSaudeAsync();
        exec.Situacao().Should().Be(SituacaoDoTomcat.NoAr);
        exec.ResumoDasApps().Should().Be("wmwvendas: ok");
    }

    [Fact]
    public async Task Nao_sobe_sem_war_ou_com_porta_ocupada()
    {
        var exec = new ExecucaoDoTomcat(_t.Ambiente(8082), new LancadorFalso(), _portas, new ClienteHttpFalso(_ => new RespostaHttp(200, "")));
        (await exec.SubirAsync(new HashSet<int>(), new PreparadorDoTomcat(_portas))).Erro.Causa.Should().Contain("Não há aplicações");
        File.WriteAllBytes(Path.Combine(_t.Webapps, "wmwdados.war"), _warDados);
        _portas.Ocupadas.Add(8082);
        var r = await exec.SubirAsync(new HashSet<int>(), new PreparadorDoTomcat(_portas));
        r.EhOk.Should().BeFalse();
    }

    [Fact]
    public async Task Para_enviando_shutdown_e_mata_a_arvore_se_nao_sair()
    {
        var escuta = new TcpListener(IPAddress.Loopback, 0);
        escuta.Start();
        var porta = ((IPEndPoint)escuta.LocalEndpoint).Port;
        using var t = new TomcatDeMentira(parada: porta);
        var lancador = new LancadorFalso { ReencontraPid = true };
        var exec = new ExecucaoDoTomcat(t.Ambiente() with { Pid = 4242 }, lancador, _portas, new ClienteHttpFalso(_ => new RespostaHttp(200, "")));
        var recebido = Task.Run(async () =>
        {
            using var c = await escuta.AcceptTcpClientAsync();
            var buffer = new byte[16];
            var n = await c.GetStream().ReadAsync(buffer);
            return System.Text.Encoding.ASCII.GetString(buffer, 0, n);
        });

        var texto = await exec.PararAsync(TimeSpan.FromMilliseconds(600));

        (await recebido).Should().Be("SHUTDOWN");
        texto.Should().Contain("à força");
        lancador.Processo.Encerrado.Should().BeTrue();
        exec.Ambiente.Pid.Should().Be(0);
        escuta.Stop();
    }
}
