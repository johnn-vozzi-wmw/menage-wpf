using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Banco;

namespace Menage.App.Modulos.Banco;

public sealed record OpcaoDeTipo(string Id, string Nome);

/// <summary>Um banco achado no context.xml, para escolher o que importar.</summary>
public sealed partial class ItemDeImportacao(BancoNoArquivo banco, string? jaExiste) : ObservableObject
{
    [ObservableProperty]
    bool _marcado;

    public PerfilDeBanco Perfil { get; } = banco.Perfil;

    public bool JaExiste { get; } = jaExiste is not null;

    public string Titulo => $"{TipoDeBanco.Nome(Perfil.Tipo)}  {Perfil.Host}:{Perfil.Porta}/{Perfil.Web.Banco}";

    public string Detalhe => $"usuários {Perfil.Web.Usuario}/{Perfil.App.Usuario}  •  " + (banco.Ativo ? "ativo" : "comentado")
        + (jaExiste is null ? "" : $"  •  já existe como \"{jaExiste}\"");
}

/// <summary>Janela Gerenciar: caminho do context.xml, lista de perfis, campos por tipo e prévia do XML (sem senhas).</summary>
public sealed partial class BancoViewModel : ObservableObject
{
    static readonly Regex _senha = new("password=\"[^\"]*\"", RegexOptions.CultureInvariant);

    readonly RepositorioDeBanco _repositorio;
    readonly ServicoDeBancoContexto _servico;
    bool _carregando;

    [ObservableProperty] PerfilDeBanco? _selecionado;
    [ObservableProperty] string _caminho = "";
    [ObservableProperty] string _mensagem = "";
    [ObservableProperty] bool _erro;
    [ObservableProperty] string _previa = "";
    [ObservableProperty] string _nome = "";
    [ObservableProperty] string _tipo = TipoDeBanco.Oracle;
    [ObservableProperty] string _host = "localhost";
    [ObservableProperty] string _porta = "1521";
    [ObservableProperty] string _webUsuario = "";
    [ObservableProperty] string _webBanco = "";
    [ObservableProperty] string _webSenha = "";
    [ObservableProperty] string _appUsuario = "";
    [ObservableProperty] string _appBanco = "";
    [ObservableProperty] string _appSenha = "";
    [ObservableProperty] string _intUsuario = "";
    [ObservableProperty] string _intBanco = "";
    [ObservableProperty] string _intSenha = "";
    [ObservableProperty] bool _intIgualWeb = true;

    public BancoViewModel(RepositorioDeBanco repositorio, ServicoDeBancoContexto servico)
    {
        (_repositorio, _servico) = (repositorio, servico);
        _caminho = repositorio.Caminho;
        foreach (var p in repositorio.Perfis) Perfis.Add(p);
        Selecionado = repositorio.PorId(repositorio.Configuracao.Selecionado) ?? Perfis.FirstOrDefault();
    }

    public ObservableCollection<PerfilDeBanco> Perfis { get; } = [];

    public IReadOnlyList<OpcaoDeTipo> Tipos { get; } = [.. TipoDeBanco.Todos.Select(t => new OpcaoDeTipo(t, TipoDeBanco.Nome(t)))];

    public string RotuloDoBanco => TipoDeBanco.RotuloDoBanco(Tipo);

    public bool EhOracle => Tipo == TipoDeBanco.Oracle;

    public bool TemSelecao => Selecionado is not null;

    /// <summary>Os campos de APP e INT por conexão só existem fora do Oracle (que tem um service name só) e o INT some quando espelha o WEB.</summary>
    public bool MostrarInt => !IntIgualWeb;

    /// <summary>Perfis a importar do arquivo: preenchido por quem chama; o resultado volta por <see cref="ImportarMarcados"/>.</summary>
    public IReadOnlyList<ItemDeImportacao> LerDoArquivo()
    {
        var texto = ArquivoDeContexto.Ler(Caminho);
        if (!texto.EhOk) { Avisar(texto.Erro.ToString(), true); return []; }
        var bancos = ContextoXml.LerTodos(texto.Valor);
        if (bancos.Count == 0) Avisar("Nenhum banco LAVENDERE*DS reconhecido no arquivo.", true);
        return [.. bancos.Select(b => new ItemDeImportacao(b, _repositorio.Equivalente(b.Perfil)?.Nome))];
    }

    public void ImportarMarcados(IEnumerable<PerfilDeBanco> lidos)
    {
        var r = _repositorio.Importar(lidos);
        if (!r.EhOk) { Avisar(r.Erro.ToString(), true); return; }
        Recarregar(r.Valor.Count > 0 ? r.Valor[0].Id : null);
        Avisar($"{r.Valor.Count} perfil(is) importado(s).", false);
    }

    PerfilDeBanco Montar() => new()
    {
        Id = Selecionado?.Id ?? Guid.NewGuid().ToString("N"), Nome = Nome.Trim(), Tipo = Tipo, Host = Host.Trim(), Porta = Porta.Trim(),
        Web = new ConexaoDeBanco(WebUsuario.Trim(), WebBanco.Trim(), WebSenha.Trim()),
        App = new ConexaoDeBanco(AppUsuario.Trim(), AppBanco.Trim(), AppSenha.Trim()),
        Integracao = new ConexaoDeBanco(IntUsuario.Trim(), IntBanco.Trim(), IntSenha.Trim()),
        IntIgualWeb = IntIgualWeb,
    };

    void Avisar(string texto, bool erro) => (Mensagem, Erro) = (texto, erro);

    void Recarregar(string? escolher)
    {
        Perfis.Clear();
        foreach (var p in _repositorio.Perfis) Perfis.Add(p);
        Selecionado = _repositorio.PorId(escolher ?? "") ?? Perfis.FirstOrDefault();
    }

    partial void OnSelecionadoChanged(PerfilDeBanco? value)
    {
        OnPropertyChanged(nameof(TemSelecao));
        if (value is null) return;
        _carregando = true;
        (Nome, Tipo, Host, Porta) = (value.Nome, value.Tipo, value.Host, value.Porta);
        (WebUsuario, WebBanco, WebSenha) = (value.Web.Usuario, value.Web.Banco, value.Web.Senha);
        (AppUsuario, AppBanco, AppSenha) = (value.App.Usuario, value.App.Banco, value.App.Senha);
        (IntUsuario, IntBanco, IntSenha, IntIgualWeb) = (value.Integracao.Usuario, value.Integracao.Banco, value.Integracao.Senha, value.IntIgualWeb);
        _carregando = false;
        AtualizarPrevia();
    }

    partial void OnTipoChanged(string value)
    {
        OnPropertyChanged(nameof(RotuloDoBanco));
        OnPropertyChanged(nameof(EhOracle));
        if (!_carregando && Porta == TipoDeBanco.PortaPadrao(Tipo == value ? "" : Tipo)) Porta = TipoDeBanco.PortaPadrao(value);
    }

    partial void OnIntIgualWebChanged(bool value) => OnPropertyChanged(nameof(MostrarInt));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_carregando && e.PropertyName is nameof(Tipo) or nameof(Host) or nameof(Porta) or nameof(WebUsuario) or nameof(WebBanco)
            or nameof(WebSenha) or nameof(AppUsuario) or nameof(AppBanco) or nameof(AppSenha) or nameof(IntUsuario) or nameof(IntBanco)
            or nameof(IntSenha) or nameof(IntIgualWeb)) AtualizarPrevia();
    }

    void AtualizarPrevia()
    {
        var p = Montar();
        var motivo = ContextoXml.Validar(p with { Nome = p.Nome.Length == 0 ? "x" : p.Nome });
        Previa = motivo is null ? _senha.Replace(ContextoXml.GerarBloco(p), "password=\"***\"") : "Prévia indisponível: " + motivo;
    }

    [RelayCommand]
    void Novo()
    {
        Selecionado = null;
        _carregando = true;
        (Nome, Tipo, Host, Porta) = ("", TipoDeBanco.Oracle, "localhost", "1521");
        (WebUsuario, WebBanco, WebSenha, AppUsuario, AppBanco, AppSenha, IntUsuario, IntBanco, IntSenha, IntIgualWeb) = ("", "", "", "", "", "", "", "", "", true);
        _carregando = false;
        OnPropertyChanged(nameof(TemSelecao));
        AtualizarPrevia();
        Avisar("Preencha os campos e clique em Salvar.", false);
    }

    [RelayCommand]
    void Duplicar()
    {
        if (Selecionado is null) return;
        var r = _repositorio.Duplicar(Selecionado.Id);
        if (!r.EhOk) { Avisar(r.Erro.ToString(), true); return; }
        Recarregar(r.Valor.Id);
        Avisar($"Criado \"{r.Valor.Nome}\".", false);
    }

    [RelayCommand]
    void Excluir()
    {
        if (Selecionado is null) return;
        var nome = Selecionado.Nome;
        var r = _repositorio.Excluir(Selecionado.Id);
        Recarregar(null);
        Avisar(r.EhOk ? $"\"{nome}\" excluído (o context.xml não foi alterado)." : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    void Salvar() => Gravar(aplicar: false);

    [RelayCommand]
    void SalvarEAplicar() => Gravar(aplicar: true);

    void Gravar(bool aplicar)
    {
        var perfil = Montar();
        var salvo = _repositorio.DefinirCaminho(Caminho);
        if (salvo.EhOk) salvo = _repositorio.Salvar(perfil);
        if (!salvo.EhOk) { Avisar(salvo.Erro.ToString(), true); return; }
        _repositorio.Escolher(perfil.Id);
        Recarregar(perfil.Id);
        if (!aplicar) { Avisar($"Perfil \"{perfil.Nome}\" salvo.", false); return; }

        var r = ArquivoDeContexto.Aplicar(_repositorio.Caminho, perfil, _repositorio.Perfis);
        _servico.Atualizar();
        Avisar(r.EhOk ? $"Perfil \"{perfil.Nome}\" salvo e aplicado. Cópia em context.xml{ArquivoDeContexto.SufixoDaCopia}." : r.Erro.ToString(), !r.EhOk);
    }
}
