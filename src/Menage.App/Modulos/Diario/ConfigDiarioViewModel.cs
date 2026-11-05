using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Diario;

namespace Menage.App.Modulos.Diario;

/// <summary>Configurar o Diário. Nunca pede nem mostra token ou chave: só os NOMES das variáveis de ambiente.</summary>
public sealed partial class ConfigDiarioViewModel : ObservableObject
{
    readonly ServicoDoDiario _servico;
    readonly ConfiguracaoDoDiario _original;

    [ObservableProperty] string _jornada;
    [ObservableProperty] string _bloco;
    [ObservableProperty] string _arredondar;
    [ObservableProperty] string _inicioDoDia;
    [ObservableProperty] bool _almoco;
    [ObservableProperty] string _autor;
    [ObservableProperty] string _projetosJira;
    [ObservableProperty] string _jiraUrl;
    [ObservableProperty] string _varTokenJira;
    [ObservableProperty] string _provedor;
    [ObservableProperty] string _modelo;
    [ObservableProperty] string _urlDaIa;
    [ObservableProperty] string _varChave;
    [ObservableProperty] bool _enviarDiff;
    [ObservableProperty] string _mensagem = "";
    [ObservableProperty] bool _erro;
    [ObservableProperty] bool _ocupado;

    public ConfigDiarioViewModel(ServicoDoDiario servico)
    {
        _servico = servico;
        _original = servico.Configuracao;
        var c = _original;
        _jornada = c.JornadaMin.ToString(CultureInfo.InvariantCulture);
        _bloco = c.BlocoMin.ToString(CultureInfo.InvariantCulture);
        _arredondar = c.ArredondarMin.ToString(CultureInfo.InvariantCulture);
        (_inicioDoDia, _almoco, _autor, _projetosJira, _jiraUrl, _varTokenJira) = (c.InicioDia, c.Almoco, c.Autor, c.ProjetosJira, c.JiraUrl, c.VarTokenJira);
        (_provedor, _modelo, _urlDaIa, _varChave, _enviarDiff) = (ClienteDeIa.Normalizar(c.Ia.Provedor), c.Ia.Modelo, c.Ia.Url, c.Ia.VarChave, c.EnviarDiff);
    }

    public IReadOnlyList<string> Provedores { get; } = ["regras", .. ClienteDeIa.Provedores];

    /// <summary>Config nova a partir dos campos, ou o motivo de não poder gravar.</summary>
    (ConfiguracaoDoDiario? Config, string? Problema) Montar()
    {
        if (!int.TryParse(Jornada, CultureInfo.InvariantCulture, out var j) || j < 30) return (null, "Jornada em minutos inválida (ex.: 528 = 8h48).");
        if (!int.TryParse(Bloco, CultureInfo.InvariantCulture, out var b) || b < 15) return (null, "Bloco máximo em minutos inválido (ex.: 240).");
        if (!int.TryParse(Arredondar, CultureInfo.InvariantCulture, out var a) || a < 1) return (null, "Arredondamento em minutos inválido (ex.: 5).");
        if (!TimeSpan.TryParse(InicioDoDia, CultureInfo.InvariantCulture, out _)) return (null, "Início do dia inválido (ex.: 09:00).");
        if (!Uri.TryCreate(JiraUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return (null, "Endereço do Jira inválido.");
        var ia = new ConfiguracaoDaIa { Provedor = Provedor, Modelo = Modelo.Trim(), Url = UrlDaIa.Trim(), VarChave = VarChave.Trim(), TimeoutSeg = _original.Ia.TimeoutSeg };
        return (_original with
        {
            JornadaMin = j, BlocoMin = b, ArredondarMin = a, InicioDia = InicioDoDia.Trim(), Almoco = Almoco, Autor = Autor.Trim(), ProjetosJira = ProjetosJira.Trim(),
            JiraUrl = JiraUrl.Trim(), VarTokenJira = VarTokenJira.Trim(), EnviarDiff = EnviarDiff, UsarIa = Provedor != "regras", Ia = ia,
        }, null);
    }

    bool Gravar()
    {
        var (config, problema) = Montar();
        if (config is null) { (Mensagem, Erro) = (problema!, true); return false; }
        var r = _servico.SalvarConfiguracao(config);
        if (!r.EhOk) (Mensagem, Erro) = (r.Erro.ToString(), true);
        return r.EhOk;
    }

    /// <summary>Salva e devolve verdadeiro se a janela pode fechar.</summary>
    public bool Salvar() => Gravar();

    [RelayCommand]
    async Task TestarJiraAsync()
    {
        if (!Gravar()) return;
        Ocupado = true;
        Mensagem = "Testando o Jira (só leitura)...";
        var r = await _servico.TestarJiraAsync();
        Ocupado = false;
        (Mensagem, Erro) = (r.EhOk ? r.Valor : r.Erro.ToString(), !r.EhOk);
    }

    [RelayCommand]
    async Task TestarIaAsync()
    {
        if (!Gravar()) return;
        Ocupado = true;
        Mensagem = "Fazendo uma chamada mínima à IA...";
        var r = await _servico.TestarIaAsync();
        Ocupado = false;
        (Mensagem, Erro) = (r.EhOk ? r.Valor : r.Erro.ToString(), !r.EhOk);
    }
}
