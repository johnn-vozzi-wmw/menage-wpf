using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.Diario;

namespace Menage.App.Modulos.Diario;

/// <summary>Um worklog na grade. As edições (issue, hora, minutos, comentário) vão direto para o <see cref="BlocoDeWorklog"/>.</summary>
public sealed partial class LinhaDoWorklog(BlocoDeWorklog bloco, Action<LinhaDoWorklog> aoMudar) : ObservableObject
{
    public BlocoDeWorklog Bloco { get; } = bloco;

    public bool Lancar
    {
        get => Bloco.Lancar && !Bloco.Lancado;
        set
        {
            if (Bloco.Lancado || Bloco.Lancar == value) return;
            Bloco.Lancar = value;
            OnPropertyChanged();
            aoMudar(this);
        }
    }

    public string Issue
    {
        get => Bloco.Issue;
        set
        {
            Bloco.Issue = value.Trim().ToUpperInvariant();
            OnPropertyChanged();
            aoMudar(this);
        }
    }

    /// <summary>"HH:mm"; texto inválido é ignorado e volta ao valor anterior.</summary>
    public string Inicio
    {
        get => Bloco.Inicio.ToString("HH:mm", CultureInfo.InvariantCulture);
        set
        {
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var h) && h >= TimeSpan.Zero && h < TimeSpan.FromDays(1))
                Bloco.Inicio = Bloco.Inicio.Date + h;
            OnPropertyChanged();
            aoMudar(this);
        }
    }

    public int Minutos
    {
        get => Bloco.Minutos;
        set
        {
            Bloco.Minutos = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Duracao));
            aoMudar(this);
        }
    }

    public string Duracao => ConfiguracaoDoDiario.FormatarMin(Bloco.Minutos);

    public string Comentario
    {
        get => Bloco.Comentario;
        set
        {
            Bloco.Comentario = value;
            OnPropertyChanged();
        }
    }

    public string Titulo => Bloco.TituloJira;

    public string Situacao => Bloco.Lancado ? $"lançado ({Bloco.WorklogId})" : Bloco.Aviso;

    public bool Lancado => Bloco.Lancado;

    /// <summary>Relê o que a regra de negócio mudou (lançamento, título do Jira, aviso).</summary>
    public void Atualizar()
    {
        foreach (var n in new[] { nameof(Lancar), nameof(Titulo), nameof(Situacao), nameof(Lancado), nameof(Comentario), nameof(Issue), nameof(Inicio), nameof(Minutos), nameof(Duracao) })
            OnPropertyChanged(n);
    }
}

public sealed record CommitDaLinha(string Hora, string Projeto, string Hash, string Assunto, string Branch, string Arquivos);
