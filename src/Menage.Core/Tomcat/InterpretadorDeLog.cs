using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Tomcat;

/// <summary>
/// Lê o log do Tomcat linha a linha e transforma erros conhecidos em texto claro (porta em uso, Java incompatível, memória,
/// falha ao implantar, pasta exigida pelo context.xml, causa raiz e erros ORA-). Sem processo nem tela: só texto.
/// </summary>
public sealed partial class InterpretadorDeLog
{
    readonly Dictionary<string, string> _apps = [];
    string _causaRaiz = "";
    bool _causaFixada;
    bool _erroBanco;

    public string ErroDetectado { get; private set; } = "";

    public bool ServidorSubiu { get; private set; }

    /// <summary>Contexto -> aguardando | implantado | ok | falhou | HTTP nnn.</summary>
    public IReadOnlyDictionary<string, string> Apps
    {
        get
        {
            lock (_apps) return new Dictionary<string, string>(_apps);
        }
    }

    public void Reiniciar()
    {
        ErroDetectado = "";
        ServidorSubiu = false;
        _causaRaiz = "";
        _causaFixada = false;
        _erroBanco = false;
        lock (_apps) _apps.Clear();
    }

    /// <summary>Estado de uma app vindo da verificação por HTTP.</summary>
    public void DefinirApp(string contexto, string estado)
    {
        lock (_apps) _apps[contexto] = estado;
    }

    public void MarcarSubiu() => ServidorSubiu = true;

    /// <summary>O processo terminou: fecha a pilha pendente e explica se saiu antes de subir.</summary>
    public void Saiu(int codigo, bool parando)
    {
        FixarCausa();
        if (!parando && !ServidorSubiu && ErroDetectado.Length == 0)
            ErroDetectado = $"O Tomcat encerrou sozinho (código {codigo}) antes de terminar de subir. Veja o log abaixo.";
    }

    public void Linha(string l)
    {
        CausaDaPilha(l);
        if (l.Contains("Server startup in", StringComparison.Ordinal)) ServidorSubiu = true;
        var d = Implantou().Match(l);
        if (d.Success) Marcar(d.Groups[1].Value, "implantado");
        var f = FalhaAoImplantar().Match(l);
        if (f.Success)
        {
            Marcar(f.Groups[1].Value, "falhou");
            Erro($"Falha ao implantar {f.Groups[1].Value}. Veja o primeiro SEVERE/Exception no log abaixo.");
        }

        var c = ContextoFalhou().Match(l);
        if (c.Success)
        {
            Marcar(c.Groups[1].Value, "falhou");
            Erro($"{c.Groups[1].Value} não conseguiu iniciar (startup failed). Veja o log abaixo.");
        }

        if (l.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) || l.Contains("já está em uso", StringComparison.OrdinalIgnoreCase) || l.Contains("BindException", StringComparison.Ordinal))
            Erro("Porta em uso: outro programa ocupa uma das portas deste Tomcat (HTTP, parada ou AJP).");
        var ora = CodigoOra().Match(l);
        if (ora.Success && !_erroBanco)
        {
            _erroBanco = true;
            ErroDetectado = TraduzirOra(ora.Groups[1].Value);
        }

        if (l.Contains("UnsupportedClassVersionError", StringComparison.Ordinal))
            Erro(@"Java incompatível: o .war foi compilado para uma versão de Java mais nova que a do Tomcat. Use o JDK 17 no bin\setenv.bat.");
        if (l.Contains("OutOfMemoryError", StringComparison.Ordinal))
            Erro(@"Memória insuficiente (OutOfMemoryError). Aumente o -Xmx no bin\setenv.bat ou feche outros programas.");
        if (l.Contains("JAVA_HOME", StringComparison.Ordinal) && (l.Contains("not defined correctly", StringComparison.OrdinalIgnoreCase) || l.Contains("cannot be found", StringComparison.OrdinalIgnoreCase) || l.Contains("não está definida", StringComparison.OrdinalIgnoreCase)))
            Erro(@"O JAVA_HOME não está correto. Ajuste o bin\setenv.bat.");
        if (ErroDetectado.Length == 0 && Grave().IsMatch(l))
            Erro("Erro grave no Tomcat: " + (l.Length > 200 ? l[..200] + "..." : l).Trim());
    }

    public static string TraduzirOra(string codigo) => codigo switch
    {
        "12514" => "O banco recusou: o service name não está registrado no listener (ORA-12514). O servidor responde, mas o nome do banco não existe nele. Pare, corrija e suba de novo.",
        "12505" => "O banco recusou: o SID informado não está registrado no listener (ORA-12505). Confira o nome do banco.",
        "01017" => "Usuário ou senha do banco recusados (ORA-01017). Confira usuário e senha (a senha vai criptografada).",
        "12541" => "Não há listener Oracle nesse host/porta (ORA-12541). Confira host e porta do banco.",
        "12170" or "12543" or "12545" => $"Não consegui chegar ao servidor do banco (ORA-{codigo}): rede/VPN ou host errado. Confira host e porta.",
        _ => $"Erro do Oracle (ORA-{codigo}). Veja a linha no log abaixo e confira a configuração do banco.",
    };

    public static string TraduzirCausa(string causa)
    {
        var d = PastaFaltando().Match(causa);
        if (d.Success) return $@"Falta uma pasta que o context.xml do Tomcat usa: {d.Groups[1].Value.Replace('/', '\\')}. Crie a pasta ou copie o data\ do Tomcat de origem.";
        var cnf = ClasseFaltando().Match(causa);
        if (cnf.Success) return $@"Classe não encontrada: {cnf.Groups[1].Value}. Confira se o .war e as libs do Tomcat (lib\) são da mesma versão.";
        if (causa.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || causa.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase))
            return $"Acesso negado a um arquivo/pasta do Tomcat: {causa}. Feche o que estiver usando a pasta ou rode com permissão.";
        return "Erro grave no Tomcat: " + (causa.Length > 260 ? causa[..260] + "..." : causa);
    }

    /// <summary>O Tomcat escreve UTF-8 e o driver Oracle escreve ANSI na mesma saída: decide linha a linha.</summary>
    public static string Decodificar(string latin1)
    {
        var bytes = Encoding.Latin1.GetBytes(latin1);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return latin1; }
    }

    void Erro(string texto)
    {
        if (ErroDetectado.Length == 0 || ErroDetectado.StartsWith("Erro grave", StringComparison.Ordinal)) ErroDetectado = texto;
    }

    // "A child container failed during start" não diz nada: a causa de verdade é o último "Caused by:" da pilha desse registro.
    void CausaDaPilha(string l)
    {
        if (_causaFixada) return;
        if (InicioDeRegistro().IsMatch(l))
        {
            FixarCausa();
            return;
        }

        var cb = CausadoPor().Match(l);
        if (cb.Success) _causaRaiz = cb.Groups[1].Value.Trim();
    }

    void FixarCausa()
    {
        if (_causaRaiz.Length > 0 && !_causaFixada && ErroDetectado.StartsWith("Erro grave", StringComparison.Ordinal))
        {
            ErroDetectado = TraduzirCausa(_causaRaiz);
            _causaFixada = true;
        }

        _causaRaiz = "";
    }

    void Marcar(string contexto, string valor)
    {
        lock (_apps)
        {
            // falhou não vira "implantado" (o Tomcat loga "finished" mesmo assim)
            if (_apps.TryGetValue(contexto, out var atual) && (atual == "ok" || (atual == "falhou" && valor == "implantado"))) return;
            _apps[contexto] = valor;
        }
    }

    [GeneratedRegex(@"Deployment of web application (?:archive|directory) \[[^\]]*[\\/](wmwvendas|wmwdados)(?:\.war)?\] has finished")]
    private static partial Regex Implantou();

    [GeneratedRegex(@"Error deploying web application (?:archive|directory) \[[^\]]*[\\/](wmwvendas|wmwdados)(?:\.war)?\]")]
    private static partial Regex FalhaAoImplantar();

    [GeneratedRegex(@"Context \[/(wmwvendas|wmwdados)\] startup failed")]
    private static partial Regex ContextoFalhou();

    [GeneratedRegex(@"ORA-(\d{5})")]
    private static partial Regex CodigoOra();

    [GeneratedRegex(@"\bSEVERE\b|\bSEVERO\b|\bGRAVE\b")]
    private static partial Regex Grave();

    [GeneratedRegex(@"^\d{2}-\p{L}{3}-\d{4} \d")]
    private static partial Regex InicioDeRegistro();

    [GeneratedRegex(@"^\s*Caused by: (.+)$")]
    private static partial Regex CausadoPor();

    [GeneratedRegex(@"The directory specified by base and internal path \[(.+?)\](?:\\\[\])? does not exist")]
    private static partial Regex PastaFaltando();

    [GeneratedRegex(@"(?:ClassNotFoundException|NoClassDefFoundError): (\S+)")]
    private static partial Regex ClasseFaltando();
}
