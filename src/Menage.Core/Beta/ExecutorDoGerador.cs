using System.Text;
using Menage.Core.Operacoes;
using Menage.Core.Processos;

namespace Menage.Core.Beta;

/// <summary>
/// Executa a geração: um projeto por vez, cada um num processo próprio (<c>cmd /c</c> de um script) com o log em arquivo.
/// Falha de um projeto não interrompe os seguintes (como no Git Manager) a não ser que o pedido diga
/// <c>PararNoPrimeiroErro</c>. "Parar após o atual" deixa o projeto terminar; "Abortar" mata o Maven em curso.
/// </summary>
public sealed class ExecutorDoGerador(IProcessoRunner runner)
{
    /// <summary>Disparado a cada mudança de estado de um projeto (possivelmente fora da thread da tela).</summary>
    public event Action<StatusDoProjeto>? StatusMudou;

    public async Task<ResumoDaGeracao> ExecutarAsync(PedidoDeGeracao pedido, SinaisDoLote sinais = default)
    {
        Directory.CreateDirectory(pedido.PastaDeLogs);
        var estados = pedido.Planos.ToDictionary(p => p.Nome, p => new StatusDoProjeto(p.Nome, FaseDaGeracao.NaFila));
        foreach (var s in estados.Values) StatusMudou?.Invoke(s);

        var interrompida = false;
        for (var i = 0; i < pedido.Planos.Count; i++)
        {
            if (sinais.PararApos.IsCancellationRequested || sinais.Abortar.IsCancellationRequested) { interrompida = true; break; }
            var final = await RodarComRepeticaoAsync(pedido, i, sinais.Abortar);
            estados[final.Projeto] = final;
            if (final.Estado == FaseDaGeracao.Interrompido) { interrompida = true; break; }
            if (final.Estado == FaseDaGeracao.Erro && pedido.PararNoPrimeiroErro) { interrompida = true; break; }
        }

        foreach (var plano in pedido.Planos.Where(p => !estados[p.Nome].Terminou))
        {
            estados[plano.Nome] = new StatusDoProjeto(plano.Nome, FaseDaGeracao.NaoExecutado, Resumo: "Não executado");
            StatusMudou?.Invoke(estados[plano.Nome]);
        }

        return new ResumoDaGeracao([.. pedido.Planos.Select(p => estados[p.Nome])], pedido.PastaDeLogs, interrompida);
    }

    async Task<StatusDoProjeto> RodarComRepeticaoAsync(PedidoDeGeracao pedido, int indice, CancellationToken abortar)
    {
        var plano = pedido.Planos[indice];
        var primeira = await RodarAsync(pedido, indice, plano, repeticao: false, abortar);
        if (primeira.Estado != FaseDaGeracao.Erro || !DeveRepetir(plano, primeira)) return primeira;

        StatusMudou?.Invoke(primeira with { Estado = FaseDaGeracao.Repetindo, Resumo = "Repetindo (correção do surefire)" });
        return await RodarAsync(pedido, indice, plano, repeticao: true, abortar);
    }

    // lavenderepda sem comando personalizado, com falha do surefire no log: repete UMA vez com a opção da JVM que contorna.
    static bool DeveRepetir(PlanoDeProjeto plano, StatusDoProjeto status) =>
        plano.Nome.Equals("lavenderepda", StringComparison.OrdinalIgnoreCase) && !plano.ComandoPersonalizado
        && status.Log is { } log && ResumidorDeLog.ProblemaDoSurefire(ResumidorDeLog.Ler(log));

    async Task<StatusDoProjeto> RodarAsync(PedidoDeGeracao pedido, int indice, PlanoDeProjeto plano, bool repeticao, CancellationToken abortar)
    {
        var prefixo = $"{indice + 1:00}-{plano.Nome}{(repeticao ? "-surefire" : "")}";
        var log = Path.Combine(pedido.PastaDeLogs, prefixo + ".log");
        var script = Path.Combine(pedido.PastaDeLogs, prefixo + ".cmd");
        var inicio = DateTime.Now;
        StatusMudou?.Invoke(new StatusDoProjeto(plano.Nome, FaseDaGeracao.Executando, Log: log, Inicio: inicio));

        EscreverCabecalho(log, plano, pedido, inicio, repeticao);
        var avisos = plano.LimparTarget ? LimparAntes(plano, log) : [];
        File.WriteAllText(script, ScriptDoProjeto.Montar(plano, pedido, log, repeticao ? MontadorDeComando.OpcoesDoSurefire : null), Encoding.Latin1);

        var saida = await runner.ExecutarAsync(new ComandoProcesso("cmd.exe", ["/d", "/c", script], plano.Pasta), abortar);
        var tempo = DateTime.Now - inicio;
        var (estado, codigo) = Classificar(saida);
        File.AppendAllText(log, Rodape(plano, estado, codigo, tempo, saida.FalhaAoIniciar), Encoding.Latin1);

        var resumo = estado switch
        {
            FaseDaGeracao.Interrompido => "Interrompido pelo usuário",
            _ when saida.FalhaAoIniciar is { } f => f,
            _ => ResumidorDeLog.Resumir(ResumidorDeLog.Ler(log), codigo),
        };
        if (avisos.Count > 0) resumo += $" | {avisos.Count} aviso(s) na limpeza do target";
        var final = new StatusDoProjeto(plano.Nome, estado, tempo, resumo, log, inicio);
        if (!(estado == FaseDaGeracao.Erro && !repeticao && DeveRepetir(plano, final))) StatusMudou?.Invoke(final);
        return final;
    }

    static (FaseDaGeracao Estado, int Codigo) Classificar(SaidaProcesso saida) =>
        saida.Cancelado ? (FaseDaGeracao.Interrompido, saida.CodigoDeSaida)
        : saida.Sucesso ? (FaseDaGeracao.Ok, 0)
        : (FaseDaGeracao.Erro, saida.CodigoDeSaida == 0 ? 1 : saida.CodigoDeSaida);

    static List<string> LimparAntes(PlanoDeProjeto plano, string log)
    {
        var linhas = new List<string> { "Limpeza  : removendo as pastas target antes do Maven" };
        var r = LimpadorDeTarget.Limpar(plano.Pasta);
        linhas.AddRange(r.RemovidasRelativas.Select(x => $"  removida: {x}"));
        linhas.AddRange(r.Avisos.Select(a => $"  [AVISO] {a}"));
        linhas.Add(r.Encontradas == 0 ? "  nenhuma pasta target encontrada" : $"  {r.Removidas} de {r.Encontradas} pasta(s) target removida(s)");
        linhas.Add(new string('=', 100));
        File.AppendAllLines(log, linhas, Encoding.Latin1);
        return r.Avisos.ToList();
    }

    static void EscreverCabecalho(string log, PlanoDeProjeto plano, PedidoDeGeracao pedido, DateTime inicio, bool repeticao)
    {
        var linha = new string('=', 100);
        File.WriteAllLines(log,
        [
            linha,
            $"Projeto  : {plano.Nome}{(repeticao ? "  (repetição: correção do surefire)" : "")}",
            $"Pasta    : {plano.Pasta}",
            $"Branch   : {plano.Branch}",
            $"JAVA_HOME: {plano.JavaHome}   JAVA8_HOME: {pedido.Java8Home}",
            $"Comando  : {plano.Comando}",
            $"Inicio   : {inicio:dd/MM/yyyy HH:mm:ss}",
            linha,
        ], Encoding.Latin1);
    }

    static string Rodape(PlanoDeProjeto plano, FaseDaGeracao estado, int codigo, TimeSpan tempo, string? falha)
    {
        var t = $"{(int)tempo.TotalMinutes:00}:{tempo.Seconds:00}";
        var linha = new string('=', 100);
        var resultado = estado switch
        {
            FaseDaGeracao.Ok => $"RESULTADO: SUCESSO - {plano.Nome} em {t}",
            FaseDaGeracao.Interrompido => $"RESULTADO: INTERROMPIDO - {plano.Nome} em {t}",
            _ => $"RESULTADO: ERRO (exit {codigo}) - {plano.Nome} em {t}{(falha is null ? "" : $" [{falha}]")}",
        };
        return $"{linha}{Environment.NewLine}{resultado}{Environment.NewLine}{linha}{Environment.NewLine}";
    }
}
