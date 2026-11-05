using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Modulos.GeradorBeta;
using Menage.Core;
using Menage.Core.Diario;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.Diario;

/// <summary>
/// Tela do Diário: só orquestra o <see cref="ServicoDoDiario"/> e expõe o estado. A IA nunca roda sozinha (só no botão, uma chamada por
/// worklog) e nada vai ao Jira sem a confirmação com a lista exata.
/// </summary>
public sealed partial class DiarioViewModel : ObservableObject, IDisposable
{
    readonly ServicoDoDiario _servico;
    readonly IServicoDeDialogos _dialogos;
    readonly IServicoDeJanelas _janelas;
    ResultadoDoDiario? _resultado;
    CancellationTokenSource? _cancelar;

    [ObservableProperty] DateTime _dia = DateTime.Today;
    [ObservableProperty] LinhaDoWorklog? _selecionada;
    [ObservableProperty] bool _ocupado;
    [ObservableProperty] string _mensagem = "";
    [ObservableProperty] bool _mensagemDeErro;
    [ObservableProperty] string _total = "";
    [ObservableProperty] string _motivo = "";
    [ObservableProperty] string _avisos = "";
    [ObservableProperty] bool _porCommit;

    public DiarioViewModel(ServicoDoDiario servico, IServicoDeDialogos dialogos, IServicoDeJanelas janelas)
    {
        (_servico, _dialogos, _janelas) = (servico, dialogos, janelas);
        _porCommit = servico.Configuracao.WorklogPorCommit;
    }

    public void Dispose() => _cancelar?.Dispose();

    public ObservableCollection<LinhaDoWorklog> Linhas { get; } = [];

    public ObservableCollection<CommitDaLinha> CommitsDaLinha { get; } = [];

    public string TextoDoDia => Dia.ToString("dddd, dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));

    public bool EhHoje => Dia.Date == DateTime.Today;

    public string ResumoDaIa => ClienteDeIaDescricao();

    /// <summary>Pedido ao code-behind: copiar este texto para a área de transferência.</summary>
    public event Action<string>? CopiarPedido;

    string ClienteDeIaDescricao()
    {
        var p = ClienteDeIa.Normalizar(_servico.Configuracao.Ia.Provedor);
        return p == "regras" ? "Resumo por regras (sem IA)" : $"IA: {p} (só quando você clicar em \"Resumir com IA\")";
    }

    void Avisar(string texto, bool erro = false) => (Mensagem, MensagemDeErro) = (texto, erro);

    partial void OnDiaChanged(DateTime value)
    {
        OnPropertyChanged(nameof(TextoDoDia));
        OnPropertyChanged(nameof(EhHoje));
    }

    partial void OnSelecionadaChanged(LinhaDoWorklog? value)
    {
        CommitsDaLinha.Clear();
        if (value is null || _resultado is null) return;
        var grupo = ResumoDoDiario.GrupoDoBloco(_resultado, value.Bloco);
        foreach (var c in grupo?.Commits ?? [])
            CommitsDaLinha.Add(new CommitDaLinha(c.Quando.ToString("HH:mm", CultureInfo.InvariantCulture), c.Projeto, c.Hash, c.Assunto, c.Branch, string.Join(", ", c.Arquivos.Take(4)) + (c.Arquivos.Count > 4 ? $" (+{c.Arquivos.Count - 4})" : "")));
    }

    partial void OnPorCommitChanged(bool value)
    {
        if (value == _servico.Configuracao.WorklogPorCommit) return;
        _servico.SalvarModoPorCommit(value);
        _ = CarregarAsync(); // trocar a caixa recarrega o dia (descarta ajustes ainda não lançados)
    }

    void AoMudarLinha(LinhaDoWorklog _) => AtualizarTotal();

    void AtualizarTotal()
    {
        var soma = Linhas.Sum(l => l.Minutos);
        var jornada = _servico.Configuracao.JornadaMin;
        Total = $"Total {ConfiguracaoDoDiario.FormatarMin(soma)} de {ConfiguracaoDoDiario.FormatarMin(jornada)}" + (soma == jornada ? "" : soma > jornada ? $"  (+{ConfiguracaoDoDiario.FormatarMin(soma - jornada)})" : $"  (faltam {ConfiguracaoDoDiario.FormatarMin(jornada - soma)})");
    }

    /// <summary>Lê os commits do dia e monta os worklogs (regras apenas: nenhuma chamada de rede).</summary>
    public async Task CarregarAsync()
    {
        _cancelar?.Cancel();
        _cancelar = new CancellationTokenSource();
        Ocupado = true;
        Avisar("Lendo os commits do dia...");
        try
        {
            var r = await _servico.CarregarDiaAsync(Dia, _cancelar.Token);
            if (!r.EhOk) { Linhas.Clear(); Avisar(r.Erro.ToString(), true); return; }
            Mostrar(r.Valor);
        }
        catch (OperationCanceledException)
        {
            // outra leitura começou: a mais nova vale
        }
        finally
        {
            Ocupado = false;
        }
    }

    void Mostrar(ResultadoDoDiario r)
    {
        _resultado = r;
        Linhas.Clear();
        foreach (var b in r.Blocos) Linhas.Add(new LinhaDoWorklog(b, AoMudarLinha));
        Motivo = r.Motivo;
        Avisos = string.Join("\n", r.Avisos);
        AtualizarTotal();
        Selecionada = Linhas.FirstOrDefault();
        Avisar($"{r.Commits.Count} commit(s) seu(s) em {r.Commits.Select(c => c.Projeto).Distinct().Count()} projeto(s).");
    }

    [RelayCommand]
    Task DiaAnteriorAsync() => TrocarDiaAsync(Dia.AddDays(-1));

    [RelayCommand]
    Task DiaSeguinteAsync() => TrocarDiaAsync(Dia.AddDays(1));

    [RelayCommand]
    Task HojeAsync() => TrocarDiaAsync(DateTime.Today);

    Task TrocarDiaAsync(DateTime dia)
    {
        Dia = dia.Date;
        return CarregarAsync();
    }

    [RelayCommand]
    Task RecarregarAsync() => CarregarAsync();

    [RelayCommand]
    async Task ResumoPorRegrasAsync()
    {
        if (_resultado is null || Selecionada is not { } linha) return;
        Ocupado = true;
        linha.Comentario = await _servico.ResumoPorRegrasAsync(_resultado, linha.Bloco);
        Ocupado = false;
        Avisar("Comentário refeito por regras (sem IA).");
    }

    [RelayCommand]
    async Task ResumirComIaAsync()
    {
        if (_resultado is null || Selecionada is not { } linha) return;
        Ocupado = true;
        Avisar("Pedindo o resumo à IA (uma chamada, só para este worklog)...");
        var r = await _servico.ResumirComIaAsync(_resultado, linha.Bloco);
        Ocupado = false;
        if (!r.EhOk) { Avisar($"IA indisponível: {r.Erro}. Ficou o resumo por regras.", true); return; }
        var anterior = _resultado.Blocos.Take(_resultado.Blocos.IndexOf(linha.Bloco)).Any(b => string.Equals(b.Issue, linha.Bloco.Issue, StringComparison.OrdinalIgnoreCase));
        linha.Comentario = anterior ? ResumoDoDiario.Continuacao(r.Valor.Texto) : r.Valor.Texto;
        Avisar("Resumo da IA aplicado." + (r.Valor.Aviso.Length > 0 ? " Atenção: " + r.Valor.Aviso + "." : ""));
    }

    [RelayCommand]
    void Copiar()
    {
        if (_resultado is null) return;
        CopiarPedido?.Invoke(ResumoDoDiario.Texto(_resultado));
        Avisar("Texto do dia copiado para a área de transferência.");
    }

    [RelayCommand]
    async Task AtribuirIssueAsync()
    {
        if (_resultado is null) return;
        var sem = _resultado.Commits.Where(c => c.Issue.Length == 0).ToList();
        if (sem.Count == 0) { Avisar("Todos os commits já têm issue."); return; }
        var id = await _janelas.AbrirDialogoAsync(JanelaDeTexto.Id, new PedidoDeTexto("Atribuir issue", $"Issue para os {sem.Count} commit(s) sem id (ex.: VENDAS-123)", "")) as string;
        var valida = ReconhecedorDeIssue.IssueDe(id, "");
        if (valida.Length == 0) { if (id is not null) Avisar("Id de issue inválido.", true); return; }
        foreach (var c in sem) c.Issue = valida;
        Mostrar(_servico.Redistribuir(_resultado.Dia, _resultado.Commits));
    }

    [RelayCommand]
    async Task ConferirNoJiraAsync()
    {
        if (_resultado is null) return;
        Ocupado = true;
        Avisar("Consultando o Jira (somente leitura)...");
        var r = await _servico.ConferirNoJiraAsync(_resultado);
        Ocupado = false;
        foreach (var l in Linhas) l.Atualizar();
        Avisar(r.EhOk ? "Issues conferidas no Jira." : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    async Task LancarAsync()
    {
        if (_resultado is null) return;
        var pendentes = ServicoDoDiario.Pendentes(_resultado);
        if (pendentes.Count == 0) { Avisar("Nada para lançar: marque ao menos um worklog ainda não lançado."); return; }
        if (ServicoDoDiario.Validar(pendentes) is { } problema) { Avisar(problema, true); return; }

        var confirmou = await _dialogos.ConfirmarAsync(new Previa(
            "Lançar no Jira",
            $"Enviar {pendentes.Count} worklog(s) para {_servico.Configuracao.JiraUrl}? Isto grava horas no Jira.",
            [.. pendentes.Select(p => p.Linha)]));
        if (!confirmou) return;

        Ocupado = true;
        var r = await _servico.LancarAsync(_resultado);
        Ocupado = false;
        foreach (var l in Linhas) l.Atualizar();
        if (r.EhOk) Avisar($"{r.Valor.Lancados} worklog(s) lançado(s).");
        else
        {
            Avisar("Lançamento interrompido.", true);
            await _dialogos.MostrarErroAsync(r.Erro);
        }
    }

    [RelayCommand]
    async Task ConfigurarAsync()
    {
        await _janelas.AbrirDialogoAsync(JanelaConfigDiario.Id);
        OnPropertyChanged(nameof(ResumoDaIa));
        if (_resultado is not null) await CarregarAsync();
    }
}
