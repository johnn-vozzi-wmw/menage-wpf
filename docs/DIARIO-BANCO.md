# Diário de trabalho e Contexto do banco

Motor em `src/Menage.Core/Diario/` e `src/Menage.Core/Banco/` (sem WPF); telas em `src/Menage.App/Modulos/Diario/` e `Modulos/Banco/`. Dados em `%LOCALAPPDATA%\Menage`: `diario.json` (jornada, Jira, IA, o que já foi lançado; **nunca** token nem chave, só os nomes das variáveis de ambiente) e `contexto-banco.json` (**contém usuários e senhas já criptografadas: não versionar**).

## Decisões

- **Banco**: o `IServicoDeBanco` da janela principal deixou de ser o falso (`ServicoDeBancoFalso` ficou só para galerias/testes). `ServicoDeBancoContexto` escolhe e aplica perfis e lê o estado do próprio `context.xml` ("Ativo: X", "outra configuração", "nenhum banco ativo"). Os marcadores `GIT-MANAGER:BANCO INICIO/FIM` foram **mantidos iguais** aos do Git Manager antigo: os dois programas reconhecem e substituem o mesmo trecho, sem duplicar. Aplicar: copia `context.xml.gm.bak`, comenta os `<Resource>` LAVENDERE*DS ativos, grava cada perfil válido uma vez (só o escolhido descomentado), valida o XML antes de gravar, preserva BOM e quebra de linha. Idempotente. Perfil inválido não cria cópia nem altera o arquivo.
- **Importar** lê bancos ativos e comentados (WEB/PDA/INT em sequência no mesmo servidor); lista tudo desmarcado e só entra o marcado; os que já existem indicam o perfil. Nome sugerido: tipo + database (Oracle: tipo + usuário).
- **Perfis** têm `Id` estável (ligação futura com melhorias). Ainda **não** migrados: melhorias (outra branch), "Usar ao ativar a melhoria" e o sqlite por atividade.
- **Diário**: commits seus do dia em todas as branches (`git log --all --source --no-merges`, janela de 14 dias filtrada pela data de **autoria**; cherry-pick conta uma vez). Issue vem da mensagem, depois da branch (`VENDAS-123`; chaves em Configurar). Jornada 8h48 dividida proporcionalmente (ou um worklog por commit), blocos de 4h, 1h de almoço. Resumo **por regras** a partir de fatos lidos dos commits (diff local `git show -w`; segredos mascarados, `context.xml`/senhas com conteúdo omitido).
- **A IA nunca roda sozinha**: abrir, trocar de dia ou recarregar não faz chamada de rede. Só "Resumir com IA" (um worklog por clique) e "Testar IA". O código só vai à IA com "Enviar trechos do código" ligado. Resposta que cita nome que não existe no que foi enviado é refeita e, se insistir, as frases sem respaldo são removidas.
- **Jira** (Server/Data Center, REST v2, `https://jira2.wmw.com.br`): token = variável de ambiente `JIRA_TOKEN` (configurável). "Conferir no Jira" é só leitura (título, status, horas já lançadas hoje). **Lançar** pede confirmação com a lista exata, para no primeiro erro e registra cada worklog enviado em `diario.json`; reabrir o dia não duplica.
- Ligação com melhorias (issue pela branch da melhoria) fica como gancho (`issueDaBranch` no coletor), a ser ligado quando o módulo de melhorias chegar.
- Comandos no catálogo: `banco.gerenciar`, `banco.aplicar`, `diario.abrir` (Ctrl+J, só perfil Desenvolvedor; cartão "Diário de trabalho" na lateral também só nele).

## Testar

```bash
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~Menage.Tests.Banco|FullyQualifiedName~Menage.Tests.Diario"
dotnet run --project src/Menage.App -- --galeria-diario-banco docs/galeria/diario-banco
```

Os testes usam repositórios e `context.xml` descartáveis em `%TEMP%` (caminho curto) e servidores HTTP falsos: nada chega ao Jira nem à IA reais, nenhum banco é acessado. A galeria cria um repositório temporário com commits do dia e um `context.xml` temporário; não lança nada.

## Roteiro manual (nada disto foi clicado por mim)

Banco
1. Barra lateral > **Gerenciar**: informe o caminho de um **cópia** do `context.xml` (não o real da primeira vez).
2. **Importar do arquivo**: a lista traz os bancos ativos e comentados, desmarcados; marque e importe.
3. Edite um perfil: a prévia (senhas ocultas) muda; troque o tipo e veja a porta e o rótulo (Service name / Database).
4. **Salvar e aplicar**: confira o `context.xml` (região gerada, só o escolhido descomentado, `.gm.bak` ao lado). Aplicar de novo não muda nada.
5. Na lateral, troque o perfil no combo e **Aplicar**; o estado mostra "Ativo: <perfil>".

Diário (perfil Desenvolvedor)
1. **Ctrl+J** ou cartão "Diário de trabalho": o dia abre sem nenhuma chamada de rede.
2. Confira commits do dia, issues, horas (soma 8h48), edite hora/minutos/comentário; marque **Um worklog por commit**.
3. **Atribuir issue** aos commits sem id; **Resumo por regras (este)**.
4. Defina `JIRA_TOKEN`, reabra e use **Configurar > Testar Jira** e **Conferir no Jira**.
5. **Resumir com IA** só com a variável da chave definida (Configurar > provedor).
6. **Lançar no Jira**: confira a lista de confirmação. Cuidado: grava horas de verdade; teste primeiro numa issue de teste. Reabra o dia: o que foi lançado aparece marcado e não duplica.
