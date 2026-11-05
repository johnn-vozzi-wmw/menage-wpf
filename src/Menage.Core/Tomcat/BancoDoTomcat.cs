using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Menage.Core.Tomcat;

/// <summary>
/// Linhas <c>db_*</c> do <c>bin\wmw_database_config.bat</c> de um Tomcat WMW: padrão + WEB/APP/INT (campo em branco herda o padrão).
/// Grava só essas linhas (o resto do arquivo fica igual) e guarda uma cópia <c>.menage.bak</c> na primeira gravação.
/// A senha vai como está no arquivo (já criptografada) e nunca é escrita em log.
/// </summary>
public sealed partial class BancoDoTomcat
{
    public static readonly IReadOnlyList<string> Campos = ["driver", "host", "port", "user", "pass", "db"];

    /// <summary>"" = padrão (vale para quem não tem valor próprio).</summary>
    public static readonly IReadOnlyList<string> Grupos = ["", "web", "app", "int"];

    public static readonly IReadOnlyList<string> Drivers = ["postgres", "oracle", "sqlserver1", "sqlserver2"];

    readonly Dictionary<string, string> _valores = [];
    ArquivoDeTexto? _arquivo;

    public string Caminho => _arquivo?.Caminho ?? "";

    public static string Arquivo(string pasta) => Path.Combine(pasta, "bin", "wmw_database_config.bat");

    public static string Chave(string campo, string grupo)
    {
        var nome = campo == "db" ? "db" : "db_" + campo;
        return grupo.Length == 0 ? nome : nome + "_" + grupo;
    }

    public string Obter(string campo, string grupo) => _valores.GetValueOrDefault(Chave(campo, grupo), "");

    public void Definir(string campo, string grupo, string? valor) => _valores[Chave(campo, grupo)] = (valor ?? "").Trim();

    /// <summary>Valor que a conexão realmente usa: o próprio, ou o do padrão.</summary>
    public string Efetivo(string campo, string grupo) => Obter(campo, grupo) is { Length: > 0 } s ? s : Obter(campo, "");

    public static Resultado<BancoDoTomcat> Ler(string pasta)
    {
        var caminho = Arquivo(pasta);
        if (!File.Exists(caminho))
            return Resultado.Falha<BancoDoTomcat>("Não achei o arquivo do banco de dados", caminho, @"Este Tomcat não tem bin\wmw_database_config.bat. Use um Tomcat montado pela WMW.");
        try
        {
            var banco = new BancoDoTomcat { _arquivo = ArquivoDeTexto.Ler(caminho) };
            foreach (Match m in LinhaDb().Matches(banco._arquivo.Texto)) banco._valores[m.Groups["k"].Value] = m.Groups["v"].Value;
            return banco._valores.ContainsKey("db_driver") && banco._valores.ContainsKey("db_host") && banco._valores.ContainsKey("db_port")
                ? Resultado.Ok(banco)
                : Resultado.Falha<BancoDoTomcat>("O wmw_database_config.bat não está no formato esperado", "Não achei as linhas set \"db_driver=...\", \"db_host=...\" e \"db_port=...\".", "Confira o arquivo " + caminho);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<BancoDoTomcat>("Não consegui ler o wmw_database_config.bat", ex.Message, "Feche o programa que está com o arquivo aberto e tente de novo.");
        }
    }

    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();
        foreach (var g in Grupos)
        {
            var rotulo = Rotulo(g);
            var driver = Obter("driver", g);
            if (driver.Length > 0 && !Drivers.Contains(driver)) erros.Add($"{rotulo}: driver \"{driver}\" inválido (use {string.Join(", ", Drivers)}).");
            var porta = Obter("port", g);
            if (porta.Length > 0 && (!int.TryParse(porta, out var n) || n is < 1 or > 65535)) erros.Add($"{rotulo}: porta \"{porta}\" inválida.");
            foreach (var c in Campos)
                if (Obter(c, g).Any(ch => ch is '"' or '%' or '\n' or '\r')) erros.Add($"{rotulo}: o campo {c} não pode ter aspas, % nem quebra de linha.");
        }

        // tudo em branco = ambiente sem banco de propósito (vale); só o preenchimento pela metade é erro
        if (Grupos.Any(g => Campos.Any(c => Obter(c, g).Length > 0)))
            foreach (var g in Grupos.Skip(1))
            {
                var faltam = Campos.Where(c => Efetivo(c, g).Length == 0).ToList();
                if (faltam.Count > 0) erros.Add($"{g.ToUpperInvariant()}: falta {string.Join(", ", faltam)} (preencha o campo ou o Padrão).");
            }

        return erros;
    }

    /// <summary>Avisos que não impedem salvar (ex.: Oracle com service name diferente de "wmw").</summary>
    public IReadOnlyList<string> Avisos() =>
        [.. Grupos.Skip(1).Where(g => Efetivo("driver", g) == "oracle" && Efetivo("db", g) is { Length: > 0 } db && !db.Equals("wmw", StringComparison.OrdinalIgnoreCase))
            .Select(g => $"{g.ToUpperInvariant()}: Oracle com service name \"{Efetivo("db", g)}\" (o padrão da WMW é \"wmw\"). Confira se é isso mesmo.")];

    /// <summary>Grava só as linhas db_*. Falha antes de tocar no arquivo se algo estiver inválido.</summary>
    public Resultado<Nada> Gravar()
    {
        if (_arquivo is null) return Resultado.Falha<Nada>("Nenhum arquivo carregado");
        var erros = Validar();
        if (erros.Count > 0) return Resultado.Falha<Nada>("O banco tem campos inválidos", string.Join(" ", erros), "Corrija os campos e salve de novo.");
        try
        {
            PastaDoTomcat.CopiaUmaVez(_arquivo.Caminho);
            var presentes = new HashSet<string>();
            var texto = LinhaDb().Replace(_arquivo.Texto, m =>
            {
                var k = m.Groups["k"].Value;
                if (!_valores.TryGetValue(k, out var novo) || !EhNosso(k)) return m.Value;
                presentes.Add(k);
                return m.Groups["pre"].Value + novo + "\"";
            });
            var quebra = texto.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            foreach (var g in Grupos.Skip(1))
                foreach (var c in Campos)
                {
                    var (k, valor) = (Chave(c, g), Obter(c, g));
                    if (presentes.Contains(k) || valor.Length == 0) continue;
                    // linha que não existia (ex.: db_user_int): entra logo depois da linha padrão do mesmo campo
                    var baseK = Chave(c, "");
                    var mb = Regex.Match(texto, @"(?m)^[ \t]*set[ \t]+""" + Regex.Escape(baseK) + @"=[^""\r\n]*""[^\r\n]*(\r?\n)?");
                    if (!mb.Success) return Resultado.Falha<Nada>($"Não achei a linha {baseK} no wmw_database_config.bat", $"Preciso dela para inserir {k}.", "Confira o arquivo " + _arquivo.Caminho);
                    var fim = mb.Index + mb.Length;
                    var linha = mb.Value.EndsWith('\n') ? $"set \"{k}={valor}\"{quebra}" : $"{quebra}set \"{k}={valor}\"";
                    texto = texto[..fim] + linha + texto[fim..];
                }

            _arquivo.Texto = texto;
            _arquivo.Gravar();
            return Resultado.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<Nada>("Não consegui gravar o wmw_database_config.bat", ex.Message, "Confira se o arquivo não é somente leitura nem está aberto em outro programa.");
        }
    }

    /// <summary>Confere só host:porta de cada conexão (não testa usuário nem senha). O Tomcat não precisa estar de pé.</summary>
    public async Task<IReadOnlyList<string>> TestarAlcanceAsync(TimeSpan limite, CancellationToken cancelar = default)
    {
        var linhas = new List<string>();
        foreach (var g in Grupos.Skip(1))
        {
            var (host, porta) = (Efetivo("host", g), Efetivo("port", g));
            if (host.Length == 0 || !int.TryParse(porta, out var p)) { linhas.Add($"{g.ToUpperInvariant()}: sem host/porta."); continue; }
            try
            {
                using var tcp = new TcpClient();
                using var tempo = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
                tempo.CancelAfter(limite);
                await tcp.ConnectAsync(host, p, tempo.Token);
                linhas.Add($"{g.ToUpperInvariant()}: {host}:{p} responde.");
            }
            catch (OperationCanceledException) when (cancelar.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                linhas.Add($"{g.ToUpperInvariant()}: {host}:{p} NÃO responde ({(ex is SocketException s ? s.Message : "tempo esgotado")}).");
            }
        }

        return linhas;
    }

    static string Rotulo(string g) => g.Length == 0 ? "Padrão" : g.ToUpperInvariant();

    static bool EhNosso(string k) => Grupos.Any(g => Campos.Any(c => Chave(c, g) == k));

    [GeneratedRegex(@"(?m)^(?<pre>[ \t]*set[ \t]+""(?<k>db[A-Za-z0-9_]*)=)(?<v>[^""\r\n]*)""")]
    private static partial Regex LinhaDb();
}
