using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Modulos.GeradorBeta;
using Menage.Core;
using Menage.Core.Interacao;
using Menage.Core.Nexus;
using Menage.Core.Tomcat;

namespace Menage.App.Modulos.Nexus;

/// <summary>Uma linha da grade: um componente do Nexus e a marcação para baixar.</summary>
public sealed partial class LinhaDeComponente(ComponenteNexus componente) : ObservableObject
{
    [ObservableProperty]
    bool _marcado;

    public ComponenteNexus Componente { get; } = componente;

    public string Nome => Componente.Nome;

    public string Grupo => Componente.Grupo;

    public string Versao => Componente.Versao;

    public string Repositorio => Componente.Repositorio;

    public string Tipos => string.Join(", ", Componente.Arquivos.Select(a => a.Extensao).Distinct());

    public string Tamanho => PastaDoTomcat.Formatar(Componente.Arquivos.Sum(a => a.Tamanho));

    public DateTime? Atualizado => Componente.Atualizado;

    public string AtualizadoTexto => Componente.Atualizado?.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR")) ?? "";
}

/// <summary>Busca e download do Nexus. Regras no Core (<c>Menage.Core.Nexus</c>); aqui só estado de tela e comandos.</summary>
public sealed partial class NexusViewModel(RepositorioDoNexus repositorio, IServicoDeJanelas janelas, IServicoDeDialogos dialogos) : ObservableObject, IDisposable
{
    SessaoDeBuscaNexus? _sessao;
    CancellationTokenSource? _cancelar;

    [ObservableProperty]
    string _names = "";

    [ObservableProperty]
    string _grupo = "";

    [ObservableProperty]
    string _versao = "";

    [ObservableProperty]
    string _repositorioEscolhido = "";

    [ObservableProperty]
    string _destino = "";

    [ObservableProperty]
    bool _manterEstrutura;

    [ObservableProperty]
    bool _subpasta = true;

    [ObservableProperty]
    bool _jar = true;

    [ObservableProperty]
    bool _war = true;

    [ObservableProperty]
    bool _pom;

    [ObservableProperty]
    string _status = "Informe nome, grupo ou versão e clique em Buscar.";

    [ObservableProperty]
    bool _statusComErro;

    [ObservableProperty]
    string _resumoMarcados = "Marque os componentes (clique ou Espaço) e use Baixar marcados.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuscarCommand), nameof(BaixarCommand), nameof(CarregarMaisCommand))]
    bool _ocupado;

    [ObservableProperty]
    double _progresso = -1;

    [ObservableProperty]
    bool _temMais;

    [ObservableProperty]
    bool _servidorAberto;

    [ObservableProperty]
    string _url = "";

    [ObservableProperty]
    string _usuario = "";

    /// <summary>Senha digitada na caixa (não é propriedade de binding): só vive até Salvar.</summary>
    public string SenhaDigitada { get; set; } = "";

    public bool TemSenhaSalva => repositorio.Configuracao.SenhaProtegida.Length > 0;

    /// <summary>Galeria: não fala com o Nexus ao abrir.</summary>
    public bool ModoExemplo { get; set; }

    public ObservableCollection<LinhaDeComponente> Itens { get; } = [];

    public ObservableCollection<string> Repositorios { get; } = [];

    public ObservableCollection<FavoritoNexus> Favoritos { get; } = [];

    public string Resumo => repositorio.Configuracao.Completa ? $"{repositorio.Configuracao.Url}  •  {repositorio.Configuracao.Usuario}" : "Servidor não configurado";

    public async Task IniciarAsync()
    {
        var c = repositorio.Configuracao;
        (Url, Usuario, Destino, ManterEstrutura, Subpasta) = (c.Url, c.Usuario, c.Destino, false, c.SubpastaAuto);
        var tipos = c.ExtensoesLista;
        (Jar, War, Pom) = (tipos.Contains("jar"), tipos.Contains("war"), tipos.Contains("pom"));
        RecarregarFavoritos();
        if (ModoExemplo) return;
        if (!c.Completa)
        {
            ServidorAberto = true;
            Dizer("Configure o servidor do Nexus (endereço, usuário e senha) para começar.", erro: false);
            return;
        }

        await CarregarRepositoriosAsync();
    }

    public void ColocarExemplo(IEnumerable<ComponenteNexus> componentes, IEnumerable<string> repositorios, IEnumerable<FavoritoNexus> favoritos)
    {
        Itens.Clear();
        foreach (var c in componentes) Adicionar(c);
        Repositorios.Clear();
        foreach (var r in repositorios) Repositorios.Add(r);
        Favoritos.Clear();
        foreach (var f in favoritos) Favoritos.Add(f);
        AtualizarMarcados();
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    async Task BuscarAsync()
    {
        var cliente = repositorio.Cliente();
        if (!cliente.EhOk)
        {
            ServidorAberto = true;
            Dizer(cliente.Erro.Causa, erro: true);
            return;
        }

        _sessao = new SessaoDeBuscaNexus(cliente.Valor);
        Itens.Clear();
        AtualizarMarcados();
        await ExecutarBuscaAsync(s => s.IniciarAsync(new ConsultaNexus(Names, Grupo, Versao, RepositorioEscolhido)));
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    Task CarregarMaisAsync() => _sessao is null ? Task.CompletedTask : ExecutarBuscaAsync(s => s.CarregarMaisAsync());

    async Task ExecutarBuscaAsync(Func<SessaoDeBuscaNexus, Task<Resultado<int>>> busca)
    {
        Ocupado = true;
        Dizer("Buscando no Nexus...", erro: false);
        try
        {
            var r = await busca(_sessao!);
            if (!r.EhOk)
            {
                Dizer(r.Erro.ToString(), erro: true);
                return;
            }

            var existentes = Itens.Select(i => i.Componente.Chave).ToHashSet();
            foreach (var c in _sessao!.Componentes.Where(c => !existentes.Contains(c.Chave)).OrderByDescending(c => c.Atualizado ?? DateTime.MinValue)) Adicionar(c);
            TemMais = _sessao.TemMais;
            Dizer(Itens.Count == 0 ? "Nenhum componente encontrado para esses filtros." : $"{Itens.Count} componente(s){(TemMais ? " carregados - há mais resultados (Carregar mais)." : ".")}", erro: false);
        }
        finally
        {
            Ocupado = false;
        }
    }

    void Adicionar(ComponenteNexus c)
    {
        var linha = new LinhaDeComponente(c);
        linha.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LinhaDeComponente.Marcado)) AtualizarMarcados(); };
        Itens.Add(linha);
    }

    [RelayCommand]
    void MarcarTodos(bool marcar)
    {
        foreach (var i in Itens) i.Marcado = marcar;
    }

    [RelayCommand]
    void Limpar()
    {
        (Names, Grupo, Versao, RepositorioEscolhido) = ("", "", "", "");
        Itens.Clear();
        TemMais = false;
        _sessao = null;
        AtualizarMarcados();
        Dizer("Informe nome, grupo ou versão e clique em Buscar.", erro: false);
    }

    public IReadOnlySet<string> Tipos => new HashSet<string>(new[] { (Jar, "jar"), (War, "war"), (Pom, "pom") }.Where(t => t.Item1).Select(t => t.Item2));

    partial void OnJarChanged(bool value) => AtualizarMarcados();

    partial void OnWarChanged(bool value) => AtualizarMarcados();

    partial void OnPomChanged(bool value) => AtualizarMarcados();

    void AtualizarMarcados()
    {
        var marcados = Itens.Where(i => i.Marcado).Select(i => i.Componente).ToList();
        if (marcados.Count == 0)
        {
            ResumoMarcados = "Marque os componentes (clique ou Espaço) e use Baixar marcados.";
            BaixarCommand.NotifyCanExecuteChanged();
            return;
        }

        var arquivos = BaixadorNexus.Arquivos(marcados, Tipos).ToList();
        ResumoMarcados = $"{marcados.Count} componente(s) marcado(s) = {arquivos.Count} arquivo(s), {PastaDoTomcat.Formatar(arquivos.Sum(a => a.Tamanho))} ({(Tipos.Count > 0 ? string.Join('/', Tipos) : "nenhum tipo marcado")})";
        BaixarCommand.NotifyCanExecuteChanged();
    }

    bool Livre() => !Ocupado;

    bool PodeBaixar() => !Ocupado && Itens.Any(i => i.Marcado);

    [RelayCommand(CanExecute = nameof(PodeBaixar))]
    async Task BaixarAsync()
    {
        var cliente = repositorio.Cliente();
        if (!cliente.EhOk)
        {
            Dizer(cliente.Erro.Causa, erro: true);
            return;
        }

        var pedido = new PedidoDeDownload([.. Itens.Where(i => i.Marcado).Select(i => i.Componente)], Tipos, Destino.Trim(), ManterEstrutura, Subpasta);
        repositorio.Salvar(repositorio.Configuracao with { Destino = Destino.Trim(), SubpastaAuto = Subpasta, Extensoes = string.Join(',', Tipos) });
        _cancelar = new CancellationTokenSource();
        Ocupado = true;
        Progresso = 0;
        Dizer("Baixando...", erro: false);
        try
        {
            var andamento = new Progress<AndamentoDoDownload>(a =>
            {
                Progresso = a.Total > 0 ? Math.Min(1, (double)a.Feito / a.Total) : 0;
                Status = $"Baixando {a.ArquivoAtual}/{a.Arquivos}: {a.Nome} ({PastaDoTomcat.Formatar(a.Feito)} de {PastaDoTomcat.Formatar(a.Total)})";
            });
            var r = await BaixadorNexus.BaixarAsync(cliente.Valor, pedido, andamento, _cancelar.Token);
            if (!r.EhOk)
            {
                Dizer(r.Erro.ToString(), erro: true);
                return;
            }

            UltimaPasta = r.Valor.Pasta;
            OnPropertyChanged(nameof(UltimaPasta));
            Dizer(r.Valor.Cancelado ? $"Cancelado. {r.Valor.Baixados} arquivo(s) já baixados em {r.Valor.Pasta}."
                : r.Valor.Ok ? $"{r.Valor.Baixados} arquivo(s) baixados ({PastaDoTomcat.Formatar(r.Valor.Bytes)}) em {r.Valor.Pasta}"
                : $"{r.Valor.Baixados} baixados, {r.Valor.Falhas.Count} falha(s): {string.Join("; ", r.Valor.Falhas)}", erro: !r.Valor.Ok && !r.Valor.Cancelado);
        }
        finally
        {
            Ocupado = false;
            Progresso = -1;
            _cancelar.Dispose();
            _cancelar = null;
        }
    }

    public string? UltimaPasta { get; private set; }

    public void Dispose() => _cancelar?.Dispose();

    [RelayCommand]
    void Cancelar() => _cancelar?.Cancel();

    [RelayCommand]
    void AbrirPasta()
    {
        if (UltimaPasta is { } p && Directory.Exists(p)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    async Task SalvarServidorAsync()
    {
        var nova = repositorio.Configuracao with { Url = Url.Trim(), Usuario = Usuario.Trim() };
        var teste = repositorio.Cliente(nova, string.IsNullOrEmpty(SenhaDigitada) ? null : SenhaDigitada);
        if (!teste.EhOk)
        {
            Dizer(teste.Erro.ToString(), erro: true);
            return;
        }

        Ocupado = true;
        Dizer("Testando o servidor...", erro: false);
        var repos = await teste.Valor.RepositoriosAsync();
        Ocupado = false;
        if (!repos.EhOk)
        {
            Dizer(repos.Erro.ToString() + " Nada foi salvo.", erro: true);
            return;
        }

        var salvo = repositorio.SalvarComSenha(nova, SenhaDigitada);
        SenhaDigitada = "";
        if (!salvo.EhOk)
        {
            Dizer(salvo.Erro.ToString(), erro: true);
            return;
        }

        PreencherRepositorios(repos.Valor);
        ServidorAberto = false;
        OnPropertyChanged(nameof(Resumo));
        OnPropertyChanged(nameof(TemSenhaSalva));
        Dizer($"Servidor salvo. {repos.Valor.Count} repositório(s) disponíveis.", erro: false);
    }

    async Task CarregarRepositoriosAsync()
    {
        var cliente = repositorio.Cliente();
        if (!cliente.EhOk) return;
        var repos = await cliente.Valor.RepositoriosAsync();
        if (repos.EhOk) PreencherRepositorios(repos.Valor);
        else Dizer(repos.Erro.ToString(), erro: true);
    }

    void PreencherRepositorios(IEnumerable<string> repos)
    {
        Repositorios.Clear();
        Repositorios.Add("(todos)");
        foreach (var r in repos) Repositorios.Add(r);
        if (RepositorioEscolhido.Length == 0) RepositorioEscolhido = repositorio.Configuracao.Repositorio.Length > 0 ? repositorio.Configuracao.Repositorio : "(todos)";
    }

    void RecarregarFavoritos()
    {
        Favoritos.Clear();
        foreach (var f in repositorio.Configuracao.Favoritos) Favoritos.Add(f);
    }

    [RelayCommand]
    void AplicarFavorito(FavoritoNexus? favorito)
    {
        if (favorito is not null) Names = string.Join(", ", favorito.Names);
    }

    [RelayCommand]
    async Task SalvarFavoritoAsync()
    {
        var nomes = SessaoDeBuscaNexus.SepararNames(Names);
        if (nomes.Count == 0)
        {
            await dialogos.InformarAsync("Favorito", "Digite um ou mais names (separados por vírgula) antes de salvar o favorito.");
            return;
        }

        if (await janelas.AbrirDialogoAsync(JanelaDeTexto.Id, new PedidoDeTexto("Salvar favorito", "Nome do favorito", "")) is not string nome || nome.Length == 0) return;
        var outros = repositorio.Configuracao.Favoritos.Where(f => !f.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
        repositorio.Salvar(repositorio.Configuracao with { Favoritos = [.. outros, new FavoritoNexus { Nome = nome, Names = nomes }] });
        RecarregarFavoritos();
    }

    [RelayCommand]
    void ExcluirFavorito(FavoritoNexus? favorito)
    {
        if (favorito is null) return;
        repositorio.Salvar(repositorio.Configuracao with { Favoritos = [.. repositorio.Configuracao.Favoritos.Where(f => f.Nome != favorito.Nome)] });
        RecarregarFavoritos();
    }

    void Dizer(string texto, bool erro) => (Status, StatusComErro) = (texto, erro);
}
