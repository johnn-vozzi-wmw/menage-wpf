using Menage.Core.Operacoes.Decoradores;

namespace Menage.Core.Protecao;

/// <summary>Valida a mensagem de um commit; nulo significa aceita.</summary>
public interface IValidadorDeCommit
{
    string? Validar(string mensagem);
}

/// <summary>Configuração em uso, gravada em <c>protecao.json</c>. Sem arquivo, vale o padrão.</summary>
public sealed class ProtecaoDeBranches(IArmazenamento<ConfigDeProtecao> armazenamento) : IRegrasDeProtecao, IValidadorDeCommit
{
    public ProtecaoDeBranches(PastaDeDados pasta)
        : this(new Armazenamento<ConfigDeProtecao>(pasta, "protecao", padrao: () => new ConfigDeProtecao()))
    {
    }

    /// <summary>Arquivo ilegível não desliga a proteção: vale o padrão.</summary>
    public ConfigDeProtecao Atual { get; private set; } = armazenamento.Carregar().ValorOu(new ConfigDeProtecao());

    public bool Protegida(string branch) => (Atual.BloquearPush || Atual.BloquearExclusao) && Atual.EhProtegida(branch);

    public string? Validar(string mensagem) => Atual.ValidarMensagem(mensagem);

    public Resultado<Nada> Salvar(ConfigDeProtecao config)
    {
        var gravou = armazenamento.Salvar(config);
        if (gravou.EhOk) Atual = config;
        return gravou;
    }
}
