using System.Globalization;
using System.IO.Compression;
using Menage.Core.Nexus;

namespace Menage.Core.Tomcat;

public enum SituacaoDoTomcat { Parado, Subindo, NoAr, Parando, PortaOcupada }

/// <summary>Andamento do preparo, lido pela tela (a tela consulta; o motor só escreve).</summary>
public sealed class RelatorDePreparo
{
    readonly object _trava = new();
    readonly List<string> _passos = [];
    readonly long[] _feito = new long[2];

    public int EtapaNumero { get; private set; }

    public int EtapaTotal { get; } = 7;

    public string EtapaAtual { get; private set; } = "";

    public string[] Nomes { get; } = new string[2];

    public string[] Versoes { get; } = new string[2];

    public long[] Totais { get; } = new long[2];

    public bool PodeCancelar { get; set; } = true;

    public IReadOnlyList<string> Passos
    {
        get
        {
            lock (_trava) return [.. _passos];
        }
    }

    public long Feito(int indice) => Interlocked.Read(ref _feito[indice]);

    public void AdicionarBytes(int indice, long n) => Interlocked.Add(ref _feito[indice], n);

    public event Action<string>? NovaLinha;

    public void Log(string texto)
    {
        var linha = $"{DateTime.Now:HH:mm:ss}  {texto}";
        lock (_trava) _passos.Add(linha);
        NovaLinha?.Invoke(linha);
    }

    public void Etapa(int numero, string texto)
    {
        EtapaNumero = numero;
        EtapaAtual = texto;
        Log(texto);
    }
}

public sealed record WarResolvido(ComponenteNexus Componente, ArquivoNexus War, string Aviso);

/// <summary>Acha o .war certo de uma aplicação no Nexus (versão vazia = a publicada mais recentemente).</summary>
public static class ResolvedorDeWar
{
    static async Task<Resultado<List<ComponenteNexus>>> BuscarAsync(ClienteNexus cliente, string repo, string nome, string grupo, string? versao, int maxPaginas, CancellationToken cancelar)
    {
        var achados = new List<ComponenteNexus>();
        string? token = null;
        for (var i = 0; i < maxPaginas; i++)
        {
            var pagina = await cliente.BuscarAsync(repo, nome, grupo, versao, token, cancelar);
            if (!pagina.EhOk)
                return Resultado.Falha<List<ComponenteNexus>>(new Erro($"Não consegui consultar o Nexus para \"{nome}\"", pagina.Erro.ToString(), "Confira rede/VPN e o servidor, usuário e senha do Nexus (Servidor)."));
            achados.AddRange(pagina.Valor.Componentes);
            token = pagina.Valor.ProximoToken;
            if (token is null) break;
        }

        return Resultado.Ok(achados.Where(c => string.Equals(c.Nome, nome, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    static IOrderedEnumerable<ComponenteNexus> MaisRecentes(IEnumerable<ComponenteNexus> lista) =>
        lista.OrderByDescending(c => c.Atualizado ?? DateTime.MinValue).ThenByDescending(c => VersaoNatural.Chave(c.Versao), StringComparer.Ordinal);

    /// <summary>Versões com .war publicadas, mais recentes primeiro (para o combo da tela).</summary>
    public static async Task<Resultado<IReadOnlyList<string>>> VersoesAsync(ClienteNexus cliente, string repo, string nome, string grupo, CancellationToken cancelar = default)
    {
        var lista = await BuscarAsync(cliente, repo, nome, grupo, null, 6, cancelar);
        return lista.Mapear(l => (IReadOnlyList<string>)[.. MaisRecentes(l.Where(c => c.Arquivos.Any(a => a.Extensao == "war"))).Select(c => c.Versao).Distinct().Take(40)]);
    }

    public static async Task<Resultado<WarResolvido>> ResolverAsync(ClienteNexus cliente, string repo, string nome, string grupo, string? versao, CancellationToken cancelar = default)
    {
        versao = (versao ?? "").Trim();
        var onde = repo.Length > 0 ? $" no repositório \"{repo}\"" : "";
        var busca = await BuscarAsync(cliente, repo, nome, grupo, versao.Length > 0 ? versao : null, 15, cancelar);
        if (!busca.EhOk) return Resultado.Falha<WarResolvido>(busca.Erro);
        var lista = busca.Valor;

        if (lista.Count == 0)
        {
            if (versao.Length == 0)
                return Resultado.Falha<WarResolvido>($"Não encontrei \"{nome}\" no Nexus", $"A busca por name={nome}{onde} não retornou nenhum componente.", "Confira o nome do componente e o repositório (ele precisa existir e você precisa ter permissão de leitura).");
            var parecidas = await BuscarAsync(cliente, repo, nome, grupo, versao + "*", 3, cancelar);
            var sugestao = parecidas.EhOk && parecidas.Valor.Count > 0
                ? " Versões parecidas: " + string.Join(", ", parecidas.Valor.Select(c => c.Versao).Distinct().OrderByDescending(v => v, VersaoNatural.Comparador).Take(8)) + "."
                : "";
            return Resultado.Falha<WarResolvido>($"A versão {versao} de \"{nome}\" não existe no Nexus", $"Nenhum componente {nome} com a versão exata \"{versao}\"{onde}.{sugestao}", "Escolha uma versão da lista (Buscar versões) ou deixe em branco para usar a mais recente.");
        }

        var comWar = lista.Where(c => c.Arquivos.Any(a => a.Extensao == "war")).ToList();
        if (comWar.Count == 0)
        {
            var c0 = MaisRecentes(lista).First();
            var tipos = string.Join(", ", c0.Arquivos.Select(a => a.Extensao).Distinct());
            return Resultado.Falha<WarResolvido>($"\"{nome}\" {c0.Versao} existe no Nexus, mas não tem arquivo .war", $"Arquivos publicados: {(tipos.Length > 0 ? tipos : "nenhum")}.", "O .war só é publicado quando o build da aplicação termina. Escolha outra versão ou peça para publicarem o war.");
        }

        var escolhido = MaisRecentes(comWar).First();
        var war = escolhido.Arquivos.Where(a => a.Extensao == "war").OrderBy(a => a.Caminho.Contains("sources", StringComparison.Ordinal) ? 1 : 0).ThenByDescending(a => a.Tamanho).First();
        var aviso = comWar.Select(c => c.Grupo).Distinct().Count() > 1 && grupo.Length == 0
            ? $"Há \"{nome}\" em mais de um grupo; usei {escolhido.Grupo}. Informe o grupo para fixar." : "";
        return Resultado.Ok(new WarResolvido(escolhido, war, aviso));
    }
}

/// <summary>
/// Prepara um ambiente: valida, acha os dois .war no Nexus, baixa juntos, confere (tamanho e WEB-INF/web.xml), ajusta as portas,
/// guarda o backup e troca. Erro na troca devolve tudo ao lugar; cancelar antes da troca não altera nada.
/// </summary>
public sealed class PreparadorDoTomcat(IPortasDoSistema portas)
{
    static readonly string[] _contextos = [AmbienteTomcat.ContextoVendas, AmbienteTomcat.ContextoDados];

    public async Task<Resultado<AmbienteTomcat>> PrepararAsync(AmbienteTomcat ambiente, ClienteNexus? cliente, string repositorioPadrao,
        ISet<int> reservadas, SituacaoDoTomcat situacao, RelatorDePreparo relator, CancellationToken cancelar = default)
    {
        string? staging = null;
        try
        {
            var resultado = await ExecutarAsync(ambiente, cliente, repositorioPadrao, reservadas, situacao, relator, s => staging = s, cancelar);
            if (resultado.EhOk) relator.Log("Pronto: o ambiente está preparado para subir.");
            else relator.Log("ERRO: " + resultado.Erro);
            return resultado;
        }
        catch (OperationCanceledException)
        {
            relator.Log("Cancelado. Nada foi trocado no Tomcat.");
            return Resultado.Falha<AmbienteTomcat>(Erro.Cancelado);
        }
        finally
        {
            try { if (staging is not null) PastaDoTomcat.ApagarArvore(staging); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a pasta temporária fica; o próximo preparo usa outra */ }
        }
    }

    async Task<Resultado<AmbienteTomcat>> ExecutarAsync(AmbienteTomcat amb, ClienteNexus? cliente, string repositorioPadrao, ISet<int> reservadas,
        SituacaoDoTomcat situacao, RelatorDePreparo r, Action<string> registrarStaging, CancellationToken cancelar)
    {
        r.Etapa(1, "Validando o ambiente");
        if (PastaDoTomcat.Validar(amb.Pasta) is { } invalida) return Resultado.Falha<AmbienteTomcat>(invalida);
        if (amb.Porta is < 1024 or > 65535) return Resultado.Falha<AmbienteTomcat>($"Porta inválida: {amb.Porta}", "Use uma porta entre 1024 e 65535.", "Corrija a porta do ambiente.");
        if (reservadas.Contains(amb.Porta)) return Resultado.Falha<AmbienteTomcat>($"A porta {amb.Porta} já é de outro ambiente", "Outro ambiente cadastrado usa esta porta (HTTP, parada ou AJP).", "Escolha outra porta para este ambiente.");
        if (situacao is SituacaoDoTomcat.Subindo or SituacaoDoTomcat.NoAr or SituacaoDoTomcat.Parando)
            return Resultado.Falha<AmbienteTomcat>("O ambiente precisa estar parado para trocar os .war", $"Situação atual: {Nome(situacao)}.", "Clique em Parar, espere o Tomcat encerrar e prepare de novo.");
        if (situacao == SituacaoDoTomcat.PortaOcupada)
        {
            var quem = await portas.OcupanteAsync(amb.Porta, cancelar);
            r.Log($"AVISO: a porta {amb.Porta} está ocupada{(quem.Length > 0 ? " por " + quem : " por outro programa")}. Dá para preparar, mas será preciso liberá-la antes de subir.");
        }

        if (cliente is null) return Resultado.Falha<AmbienteTomcat>("O Nexus ainda não está configurado", "Faltam URL, usuário ou senha do Nexus.", "Abra o Nexus (Servidor) e preencha.");
        Directory.CreateDirectory(Path.Combine(amb.Pasta, "webapps"));
        if (PastaDoTomcat.ValidarJava(amb.Pasta) is { } semJava) r.Log($"AVISO: {semJava.Causa} - {semJava.Explicacao}");
        cancelar.ThrowIfCancellationRequested();

        r.Etapa(2, "Procurando os arquivos no Nexus");
        var repo = amb.Repositorio.Length > 0 ? amb.Repositorio : "";
        string[] nomes = [amb.NomeVendas, amb.NomeDados];
        string[] versoes = [amb.VersaoVendas, amb.VersaoDados];
        var res = new WarResolvido[2];
        for (var i = 0; i < 2; i++)
        {
            cancelar.ThrowIfCancellationRequested();
            var achado = await ResolvedorDeWar.ResolverAsync(cliente, repo, nomes[i], amb.Grupo, versoes[i], cancelar);
            if (!achado.EhOk) return Resultado.Falha<AmbienteTomcat>(achado.Erro);
            res[i] = achado.Valor;
            r.Nomes[i] = _contextos[i] + ".war";
            r.Versoes[i] = res[i].Componente.Versao;
            r.Totais[i] = res[i].War.Tamanho;
            r.Log($"{nomes[i]} {res[i].Componente.Versao} ({PastaDoTomcat.Formatar(res[i].War.Tamanho)}){(versoes[i].Trim().Length == 0 ? " - a mais recente publicada" : "")}");
            if (res[i].Aviso.Length > 0) r.Log("AVISO: " + res[i].Aviso);
        }

        var precisa = res[0].War.Tamanho + res[1].War.Tamanho + 300L * 1024 * 1024;
        var livre = PastaDoTomcat.EspacoLivre(amb.Pasta);
        if (livre >= 0 && livre < precisa)
            return Resultado.Falha<AmbienteTomcat>("Espaço em disco insuficiente", $"Preciso de cerca de {PastaDoTomcat.Formatar(precisa)} livres e há {PastaDoTomcat.Formatar(livre)} na unidade da pasta do Tomcat.", $@"Libere espaço (os backups antigos ficam em {PastaDoTomcat.PastaDeTrabalho(amb.Pasta)}\backup) e tente de novo.");

        r.Etapa(3, $"Baixando {_contextos[0]}.war e {_contextos[1]}.war");
        var staging = Path.Combine(PastaDoTomcat.PastaDeTrabalho(amb.Pasta), "baixando", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        registrarStaging(staging);
        Directory.CreateDirectory(staging);
        var baixados = await BaixarAsync(cliente, res, staging, r, cancelar);
        if (!baixados.EhOk) return Resultado.Falha<AmbienteTomcat>(baixados.Erro);

        r.Etapa(4, "Conferindo os arquivos baixados");
        for (var i = 0; i < 2; i++)
            if (Conferir(baixados.Valor[i], res[i].War.Tamanho, _contextos[i] + ".war") is { } defeito) return Resultado.Falha<AmbienteTomcat>(defeito);
        cancelar.ThrowIfCancellationRequested();

        r.Etapa(5, "Ajustando as portas do Tomcat");
        var ajuste = SincronizarPortas(amb, reservadas, r.Log);
        if (!ajuste.EhOk) return Resultado.Falha<AmbienteTomcat>(ajuste.Erro);

        r.PodeCancelar = false;
        r.Etapa(6, "Guardando o backup e trocando os .war");
        var troca = Trocar(amb, baixados.Valor, r);
        if (!troca.EhOk) return Resultado.Falha<AmbienteTomcat>(troca.Erro);

        r.Etapa(7, "Registrando");
        return Resultado.Ok(amb with
        {
            InstaladoVendas = res[0].Componente.Versao,
            InstaladoDados = res[1].Componente.Versao,
            UltimoPreparo = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR")),
        });
    }

    static async Task<Resultado<string[]>> BaixarAsync(ClienteNexus cliente, WarResolvido[] res, string staging, RelatorDePreparo r, CancellationToken cancelar)
    {
        using var abortar = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
        var tarefas = Enumerable.Range(0, 2).Select(async i =>
        {
            var baixa = await cliente.BaixarAsync(res[i].War, staging, false, n => r.AdicionarBytes(i, n), abortar.Token);
            if (!baixa.EhOk) await abortar.CancelAsync();
            return baixa;
        }).ToArray();
        var resultados = await Task.WhenAll(tarefas);
        cancelar.ThrowIfCancellationRequested();
        for (var i = 0; i < 2; i++)
        {
            if (resultados[i].EhOk || resultados[i].Erro == Erro.Cancelado) continue;
            var e = resultados[i].Erro;
            return Resultado.Falha<string[]>($"Falha ao baixar {_contextos[i]}.war do Nexus", e.ToString(), "Confira a rede/VPN e prepare de novo. Nada foi trocado no Tomcat.");
        }

        var destinos = resultados.Select(x => x.Valor).ToArray();
        for (var i = 0; i < 2; i++) r.Log($"Baixado {_contextos[i]}.war ({PastaDoTomcat.Formatar(new FileInfo(destinos[i]).Length)})");
        return Resultado.Ok(destinos);
    }

    static Erro? Conferir(string arquivo, long esperado, string rotulo)
    {
        var real = new FileInfo(arquivo).Length;
        if (esperado > 0 && real != esperado)
            return new Erro($"O download de {rotulo} veio incompleto", $"Esperado {PastaDoTomcat.Formatar(esperado)} ({esperado} bytes) e recebido {PastaDoTomcat.Formatar(real)} ({real} bytes).", "Prepare de novo. Nada foi trocado no Tomcat.");
        try
        {
            using var zip = ZipFile.OpenRead(arquivo);
            return zip.Entries.Any(e => string.Equals(e.FullName, "WEB-INF/web.xml", StringComparison.OrdinalIgnoreCase)) ? null
                : new Erro($"{rotulo} baixado não parece um .war válido", "O arquivo é um zip, mas não tem WEB-INF/web.xml.", "Confira se o componente do Nexus é mesmo a aplicação web. Nada foi trocado no Tomcat.");
        }
        catch (InvalidDataException)
        {
            return new Erro($"{rotulo} baixado está corrompido", "Não consegui abrir o arquivo como zip.", "Prepare de novo. Nada foi trocado no Tomcat.");
        }
    }

    /// <summary>HTTP = a porta do ambiente; parada e AJP só mudam se brigarem com outro ambiente ou outro programa.</summary>
    public Resultado<Nada> SincronizarPortas(AmbienteTomcat amb, ISet<int> reservadas, Action<string> log)
    {
        var caminho = Path.Combine(amb.Pasta, "conf", "server.xml");
        var arquivo = ArquivoDeTexto.Ler(caminho);
        var p = ServerXml.Ler(arquivo.Texto);
        if (p.HttpPos < 0)
            return Resultado.Falha<Nada>("Não achei o conector HTTP ativo no server.xml", "Todos os <Connector> HTTP estão comentados ou não têm port.", $"Descomente/crie um <Connector port=\"...\" protocol=\"HTTP/1.1\"...> em {caminho}");
        if (!int.TryParse(p.HttpTexto, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return Resultado.Falha<Nada>($"A porta HTTP do server.xml é uma variável ({p.HttpTexto})", "Não consigo trocar o número.", $"Troque por um número em {caminho} e tente de novo.");
        try
        {
            var evitar = new HashSet<int>(reservadas) { amb.Porta };
            var parada = EscolhaDePortas.Escolher(portas, p.Parada, evitar);
            evitar.Add(parada);
            var ajp = p.AjpPos >= 0 ? EscolhaDePortas.Escolher(portas, p.Ajp, evitar) : 0;
            if (p.ConectoresHttpAtivos > 1) log($"AVISO: há {p.ConectoresHttpAtivos} conectores HTTP ativos no server.xml; ajustei só o primeiro.");
            if (p.Http == amb.Porta && parada == p.Parada && (p.AjpPos < 0 || ajp == p.Ajp))
            {
                log($"Portas já corretas: HTTP {amb.Porta}, parada {p.Parada}{(p.AjpPos >= 0 ? $", AJP {p.Ajp}" : "")}.");
                return Resultado.Ok();
            }

            PastaDoTomcat.CopiaUmaVez(caminho);
            arquivo.Texto = ServerXml.Aplicar(arquivo.Texto, p, amb.Porta, parada, ajp);
            arquivo.Gravar();
            log($"server.xml: HTTP {p.Http} -> {amb.Porta}, parada {p.Parada} -> {parada}{(p.AjpPos >= 0 ? $", AJP {p.Ajp} -> {ajp}" : "")} (original guardado em server.xml.menage.bak).");
            return Resultado.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Resultado.Falha<Nada>("Não achei uma porta livre", ex.Message, "Libere portas no Windows e tente de novo.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<Nada>("Não consegui gravar o server.xml", ex.Message, "Feche o que estiver usando o arquivo e tente de novo.");
        }
    }

    static Resultado<Nada> Trocar(AmbienteTomcat amb, string[] novos, RelatorDePreparo r)
    {
        var webapps = Path.Combine(amb.Pasta, "webapps");
        var carimbo = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backup = Path.Combine(PastaDoTomcat.PastaDeTrabalho(amb.Pasta), "backup", carimbo, "webapps");
        var movidos = new List<(string De, string Para)>();
        var colocados = new List<string>();
        try
        {
            Directory.CreateDirectory(backup);
            foreach (var c in _contextos)
            {
                Mover(Path.Combine(webapps, c + ".war"), Path.Combine(backup, c + ".war"), movidos);
                Mover(Path.Combine(webapps, c), Path.Combine(backup, c), movidos);
            }

            foreach (var c in _contextos) PastaDoTomcat.ApagarArvore(Path.Combine(amb.Pasta, "work", "Catalina", "localhost", c));
            for (var i = 0; i < 2; i++)
            {
                var destino = Path.Combine(webapps, _contextos[i] + ".war");
                File.Move(novos[i], destino);
                colocados.Add(destino);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var c in colocados) TentarApagar(c);
            for (var i = movidos.Count - 1; i >= 0; i--)
            {
                try { Desfazer(movidos[i]); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { r.Log($"AVISO: não consegui devolver {movidos[i].De} (está em {movidos[i].Para})."); }
            }

            try
            {
                if (Directory.Exists(backup) && Directory.GetFileSystemEntries(backup).Length == 0)
                {
                    Directory.Delete(backup);
                    Directory.Delete(Path.GetDirectoryName(backup)!);
                }
            }
            catch (IOException) { /* sobra uma pasta vazia, sem importância */ }

            return Resultado.Falha<Nada>("Não consegui trocar os .war no Tomcat", $"{ex.Message} O que já tinha sido movido foi devolvido ao lugar.", "Feche o que estiver usando a pasta webapps (Explorer, editor, antivírus, o próprio Tomcat) e prepare de novo.");
        }

        r.Log("Backup em " + Path.GetDirectoryName(backup));
        LimparBackups(amb, r);
        return Resultado.Ok();
    }

    static void Mover(string de, string para, List<(string De, string Para)> movidos)
    {
        if (File.Exists(de)) File.Move(de, para);
        else if (Directory.Exists(de)) Directory.Move(de, para);
        else return;
        movidos.Add((de, para));
    }

    static void Desfazer((string De, string Para) m)
    {
        if (File.Exists(m.Para)) File.Move(m.Para, m.De);
        else if (Directory.Exists(m.Para)) Directory.Move(m.Para, m.De);
    }

    static void TentarApagar(string arquivo)
    {
        try { File.Delete(arquivo); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* o rollback segue com o resto */ }
    }

    static void LimparBackups(AmbienteTomcat amb, RelatorDePreparo r)
    {
        try
        {
            var raiz = Path.Combine(PastaDoTomcat.PastaDeTrabalho(amb.Pasta), "backup");
            if (!Directory.Exists(raiz)) return;
            var manter = Math.Max(1, amb.ManterBackups);
            foreach (var velho in Directory.GetDirectories(raiz).OrderByDescending(Path.GetFileName).Skip(manter))
            {
                PastaDoTomcat.ApagarArvore(velho);
                r.Log($"Backup antigo removido: {Path.GetFileName(velho)} (mantendo os {manter} mais novos).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            r.Log("AVISO: não consegui limpar backups antigos: " + ex.Message);
        }
    }

    public static string Nome(SituacaoDoTomcat s) => s switch
    {
        SituacaoDoTomcat.Subindo => "subindo",
        SituacaoDoTomcat.NoAr => "no ar",
        SituacaoDoTomcat.Parando => "parando",
        SituacaoDoTomcat.PortaOcupada => "porta ocupada por outro programa",
        _ => "parado",
    };
}
