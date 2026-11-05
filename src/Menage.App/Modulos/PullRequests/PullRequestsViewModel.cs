using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Interacao;
using Menage.Core.PullRequests;

namespace Menage.App.Modulos.PullRequests;

public sealed partial class LinhaDePr(PullRequest pr) : ObservableObject
{
    [ObservableProperty]
    bool _marcado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Situacao), nameof(Numero), nameof(Titulo))]
    PullRequest _pr = pr;

    public int Numero => Pr.Numero;

    public string Titulo => Pr.Rascunho ? $"[rascunho] {Pr.Titulo}" : Pr.Titulo;

    public string Autor => Pr.Autor;

    public string Branch => Pr.Branch;

    public string Atualizado => Pr.Atualizado;

    public string Situacao => Pr.ProntoParaMesclar ? $"✔ {Pr.Validacao}" : Pr.Valido == false ? $"✖ {Pr.Validacao}" : Pr.Validacao.Length > 0 ? Pr.Validacao : "não validado";

    public string Tom => Pr.ProntoParaMesclar ? "Ok" : Pr.Valido == false ? "Erro" : "Neutro";
}

public sealed record LinhaDeLogDoPr(TipoDeLinha Tipo, string Texto);

/// <summary>Lista, valida e mescla pull requests (só o dono). Git, HTTP e regras estão em <c>Menage.Core.PullRequests</c>.</summary>
public sealed partial class PullRequestsViewModel(ServicoDePullRequests servico, IServicoDeDialogos dialogos) : ObservableObject, IDisposable
{
    [ObservableProperty]
    string _fontes = "";

    [ObservableProperty]
    string _origemDaLista = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AtualizarCommand), nameof(ValidarCommand), nameof(MesclarCommand))]
    bool _ocupado;

    [ObservableProperty]
    string _comandoDeValidacao = "";

    [ObservableProperty]
    bool _enviarAoGitHub;

    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    bool _mensagemComErro;

    CancellationTokenSource? _cancelar;

    /// <summary>Galeria: não usa git nem rede ao abrir.</summary>
    public bool ModoExemplo { get; set; }

    public ObservableCollection<LinhaDePr> Prs { get; } = [];

    public ObservableCollection<LinhaDeLogDoPr> Log { get; } = [];

    public IReadOnlyList<LinhaDePr> Marcados => [.. Prs.Where(p => p.Marcado)];

    public string ResumoValidar => Marcados.Count > 0 ? $"Validar marcados ({Marcados.Count})" : "Validar marcados";

    public string ResumoMesclar => Marcados.Count > 0 ? $"Mesclar na main ({Marcados.Count})" : "Mesclar na main";

    public async Task IniciarAsync()
    {
        var c = servico.Configuracao;
        (Fontes, ComandoDeValidacao) = (c.Fontes, c.ComandoDeValidacao);
        if (ModoExemplo) return;
        if (!ServicoDePullRequests.EhRepositorioGit(Fontes))
        {
            Dizer("Escolha a pasta do repositório (a que tem .git) em Trocar pasta.", erro: true);
            return;
        }

        await AtualizarAsync();
    }

    public void ColocarExemplo(IEnumerable<PullRequest> prs, IEnumerable<LinhaDeLogDoPr> log, string origem)
    {
        Prs.Clear();
        foreach (var p in prs) Adicionar(p);
        Log.Clear();
        foreach (var l in log) Log.Add(l);
        OrigemDaLista = origem;
        Dizer("Marque os PRs, valide e mescle. Nada é enviado ao GitHub sem a caixa marcada.", erro: false);
        AtualizarContagem();
    }

    void Adicionar(PullRequest pr)
    {
        var linha = new LinhaDePr(pr);
        linha.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LinhaDePr.Marcado)) AtualizarContagem(); };
        Prs.Add(linha);
    }

    void AtualizarContagem()
    {
        OnPropertyChanged(nameof(ResumoValidar));
        OnPropertyChanged(nameof(ResumoMesclar));
        ValidarCommand.NotifyCanExecuteChanged();
        MesclarCommand.NotifyCanExecuteChanged();
    }

    bool Livre() => !Ocupado;

    [RelayCommand(CanExecute = nameof(Livre))]
    async Task AtualizarAsync()
    {
        Ocupado = true;
        Dizer("Listando os pull requests...", erro: false);
        try
        {
            var token = Novo();
            var r = await servico.ListarAsync(Fontes, Para(), token);
            if (token.IsCancellationRequested)
            {
                Dizer("Listagem cancelada.", erro: false);
                return;
            }

            if (!r.EhOk)
            {
                Dizer(r.Erro.ToString(), erro: true);
                return;
            }

            Prs.Clear();
            foreach (var p in r.Valor.Prs) Adicionar(p);
            OrigemDaLista = "Fonte: " + r.Valor.Fonte;
            AtualizarContagem();
            Dizer(Prs.Count == 0 ? "Nenhum pull request aberto." : $"{Prs.Count} pull request(s) aberto(s).", erro: false);
        }
        finally
        {
            Ocupado = false;
        }
    }

    bool PodeAgir() => !Ocupado && Prs.Any(p => p.Marcado);

    [RelayCommand(CanExecute = nameof(PodeAgir))]
    async Task ValidarAsync()
    {
        Ocupado = true;
        Log.Clear();
        var token = Novo();
        try
        {
            foreach (var linha in Marcados)
            {
                token.ThrowIfCancellationRequested();
                Dizer($"Validando o PR #{linha.Numero}...", erro: false);
                var validado = await servico.ValidarAsync(Fontes, linha.Pr, Para(), token);
                token.ThrowIfCancellationRequested(); // o runner devolve falha ao cancelar: não grava "✖" num PR que só foi interrompido
                linha.Pr = validado;
            }

            var ok = Marcados.Count(l => l.Pr.ProntoParaMesclar);
            Dizer($"{ok} de {Marcados.Count} PR(s) validados.", erro: ok != Marcados.Count);
        }
        catch (OperationCanceledException)
        {
            Dizer("Validação cancelada. As cópias temporárias foram apagadas; os PRs que ficaram sem validar continuam como estavam.", erro: false);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeAgir))]
    async Task MesclarAsync()
    {
        var escolhidos = Marcados.Select(l => l.Pr).ToList();
        if (escolhidos.Any(p => !p.ProntoParaMesclar))
        {
            Dizer("Valide os PRs marcados antes de mesclar (um deles não passou ou mudou depois da validação).", erro: true);
            return;
        }

        var principal = servico.Configuracao.BranchPrincipal;
        var detalhes = escolhidos.Select(p => $"#{p.Numero} {p.Titulo}").ToList();
        detalhes.Add(EnviarAoGitHub ? $"A {principal} será ENVIADA ao GitHub (push)." : $"Só a {principal} local; nada é enviado ao GitHub.");
        if (!await dialogos.ConfirmarAsync(new Previa("Mesclar pull requests", $"Mesclar {escolhidos.Count} PR(s) na {principal}?", detalhes))) return;

        Ocupado = true;
        Log.Clear();
        try
        {
            var token = Novo();
            var r = await servico.MesclarAsync(Fontes, escolhidos, EnviarAoGitHub, Para(), token);
            if (token.IsCancellationRequested && !r.EhOk)
            {
                Dizer($"Mescla cancelada. A {servico.Configuracao.BranchPrincipal} local foi devolvida ao estado de antes; nada foi enviado.", erro: false);
                return;
            }

            Dizer(r.EhOk ? r.Valor.Mensagem : r.Erro.ToString(), erro: !r.EhOk);
            if (r.EhOk) await AtualizarAsync();
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    void AbrirNoGitHub()
    {
        var url = Prs.FirstOrDefault(p => p.Marcado)?.Pr.Url;
        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public async Task TrocarPastaAsync(string pasta)
    {
        if (!ServicoDePullRequests.EhRepositorioGit(pasta))
        {
            Dizer("Essa pasta não é um repositório git (não tem .git).", erro: true);
            return;
        }

        Fontes = pasta;
        servico.Salvar(servico.Configuracao with { Fontes = pasta });
        await AtualizarAsync();
    }

    [RelayCommand]
    void SalvarComando()
    {
        servico.Salvar(servico.Configuracao with { ComandoDeValidacao = ComandoDeValidacao.Trim() });
        Dizer("Comando de validação salvo.", erro: false);
    }

    /// <summary>Para a listagem, a validação ou a mescla em curso; o Core desfaz o que já tinha mexido (cópia temporária, refs, main).</summary>
    [RelayCommand]
    void Cancelar() => _cancelar?.Cancel();

    public void Dispose() => _cancelar?.Dispose();

    CancellationToken Novo()
    {
        _cancelar?.Dispose();
        _cancelar = new CancellationTokenSource();
        return _cancelar.Token;
    }

    Progress<LinhaDoPr> Para() => new Progress<LinhaDoPr>(l => Log.Add(new LinhaDeLogDoPr(l.Tipo, l.Texto)));

    void Dizer(string texto, bool erro) => (Mensagem, MensagemComErro) = (texto, erro);
}
