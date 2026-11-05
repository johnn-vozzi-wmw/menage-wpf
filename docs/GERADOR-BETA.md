# Gerador de Beta e Entregas

Motor em `src/Menage.Core/Beta/` e `src/Menage.Core/Entregas/` (sem WPF); tela em `src/Menage.App/Modulos/GeradorBeta/`. Dados em `%LOCALAPPDATA%\Menage\gerador-beta.json` (`Armazenamento<ConfiguracaoDoGerador>`); logs em `%LOCALAPPDATA%\Menage\gerador-beta\logs\<data>_<modo>\`.

## Decisões

- **Sem PowerShell.** O `gerador-beta-executar.ps1` antigo virou um `.cmd` por projeto (`ScriptDoProjeto`): define `JAVA_HOME`, `JAVA8_HOME`, `PATH`, vai à pasta e roda `call <comando> >> log 2>&1`. O `ExecutorDoGerador` roda `cmd /d /c <script>` pelo `IProcessoRunner` (cancelamento mata a árvore). O log é gravado pelo próprio cmd, então a tela o acompanha lendo só o que foi acrescentado (`AcompanhaLog`). **Não há janela por projeto**; ficam o log completo, o status por projeto, "Abrir log" e "Abrir cmd do projeto" (cmd interativo com o JDK do projeto).
- **Falha não para a fila** (como no Git Manager): o projeto seguinte roda; `PararNoPrimeiroErro` existe no pedido mas a tela não o liga. "Parar fila" deixa o atual terminar; "Interromper agora" mata o Maven em curso.
- **Não usa `OperacaoEmLote`**: o Gerador precisa de status ao vivo por projeto, log em arquivo e perguntas no meio (entrega), que o fluxo único não modela. O perfil é conferido em `PreparadorDeGeracao` (Tester: só install; deploy bloqueado na tela e no motor).
- **Surefire do lavenderepda**: falha com sinais do surefire no log repete uma vez com `JAVA_TOOL_OPTIONS` (log `NN-lavenderepda-surefire.log`); não repete com comando personalizado.
- **Limpar target** (`LimpadorDeTarget`): só `target` ao lado de `pom.xml`; ignora `.git`, `node_modules`, pastas ocultas e links; somente leitura não impede; arquivo em uso vira `[AVISO]` no log e o build segue. Desligado por padrão; habilitado sem projeto não salva.
- **Entregas**: Web (`.war` de lavenderedados e lavendereweb; gatilho = lavendereweb terminando ok com core e dados ok) e PDA (jar `wmwvendasapp-*` + `simulador.zip`; pergunta no fim da geração). Pasta `<pacote>_<data>` em `Entregas.Pasta` (padrão `C:\wmw\entregas`, configurável), `.zip` com a pasta na raiz, `.zip` na área de transferência, Explorer com o `.zip` selecionado. **Nunca envia nada ao Slack.** Deixado de fora por ora: pasta por chave de atividade (`VENDAS-123`), que só serve à beta automática.
- **Pasta base**: vazia = projetos do Menage (pelo nome); ou uma pasta base com os caminhos relativos do config (`web\lavendereweb`, `lavendereweb`...).
- A pílula "Gerar beta" abre o Gerador e **substitui a seleção** pelos projetos marcados na janela principal (pelo nome); sem nenhum deles, fica a seleção guardada. Comandos no catálogo: `beta.gerar` e `beta.entregas`.

## Testar

```bash
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~Menage.Tests.Beta"
dotnet run --project src/Menage.App -- --galeria-gerador docs/galeria/gerador-beta
```

Os testes usam o `mvn.cmd` falso do TestKit, pastas em `%TEMP%` e uma pasta de entregas temporária. **Nunca rode deploy nem o Maven real em teste.** A galeria usa pastas descartáveis e dados temporários, não toca em `C:\wmw\entregas`.

## Roteiro manual

1. Marque 2 projetos na janela principal e clique em **Gerar beta**: eles vêm marcados no Gerador, em ordem.
2. Escolha uma pasta base (`...`), clique em **Atualizar branches**; um clique na linha marca/desmarca, Espaço marca as selecionadas.
3. **Limpar target ao gerar**: Habilitado + projetos; gere em install e confira `removida: target` no log.
4. **Salvar seleção como favorito**; clique aplica; botão direito: aplicar e gerar, atualizar, renomear, excluir.
5. Gere (install) e acompanhe o log; teste **Parar fila** e **Interromper agora**; **Somente erros**.
6. Com core, dados e web ok: aparece a pergunta da entrega; confira a pasta, o `.zip`, o Ctrl+V e o painel "Entrega pronta".
